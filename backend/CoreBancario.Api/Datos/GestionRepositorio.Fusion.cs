using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestion CLI_FUSION: fusionar dos clientes duplicados. La llave de la gestion es el id del cliente que se ABSORBE (asi el indice
/// unico de «una gestion pendiente por registro» impide dos fusiones sobre el mismo cliente); el sobreviviente y las decisiones
/// sobre las direcciones quedan en el detalle (fus_sobreviviente, fus_motivo, fus_dir[id]).
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoFusionCliente = "CLI_FUSION";
    public const string EntidadFusion = FusionServicio.Entidad;

    /// <summary>Vista previa (sin escribir nada): plan, impedimentos y advertencias.</summary>
    public async Task<FusionServicio.Calculo> PlanFusionAsync(ActorContext actor, long idSobreviviente, long idDuplicado)
    {
        using var conexion = NuevaConexion();
        return await FusionServicio.CalcularAsync(conexion, null, actor.IdCompania, idSobreviviente, idDuplicado, null);
    }

    public async Task<ResultadoOperacion> SolicitarFusionAsync(ActorContext actor, FusionSolicitudRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una fusion de clientes.");
        var motivo = (req.Motivo ?? "").Trim();
        if (motivo.Length < 10)
            return ResultadoOperacion.Fallo(400, "motivo_requerido", "Indique el motivo de la fusion (minimo 10 caracteres).");
        if (motivo.Length > 500)
            return ResultadoOperacion.Fallo(400, "motivo_largo", "El motivo no puede superar 500 caracteres.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var decisiones = (req.Direcciones ?? []).GroupBy(x => x.IdDireccion).ToDictionary(x => x.Key, x => x.First());

            // Los tipos de direccion elegidos deben existir y estar activos.
            var cat = await InterlocutorRepositorio.CargarCatalogosAsync(conexion, tx);
            foreach (var dec in decisiones.Values.Where(x => x.Incorporar))
            {
                var tipo = dec.IdTipoDireccion is null ? null : cat.Buscar(CatalogosInterlocutor.ListaTipoDireccion, dec.IdTipoDireccion.Value);
                if (dec.IdTipoDireccion is not null && (tipo is null || tipo.Estado != "Activo"))
                    return ResultadoOperacion.Fallo(400, "datos_invalidos", $"El tipo de direccion elegido para la direccion #{dec.IdDireccion} no existe o esta inactivo.");
            }

            var calc = await FusionServicio.CalcularAsync(conexion, tx, actor.IdCompania, req.IdSobreviviente, req.IdDuplicado, decisiones);
            if (calc.Impedimentos.Count > 0)
                return ResultadoOperacion.Fallo(409, "fusion_no_valida", string.Join("\n", calc.Impedimentos));

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoFusionCliente }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoFusionCliente}; correr fusion_clientes_ronda18.sql.");

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, NULL, @creadoPor)
                """,
                new
                {
                    actor.IdCompania, numeroGestion, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario,
                    entidad = EntidadFusion, llave = req.IdDuplicado.ToString(CultureInfo.InvariantCulture), creadoPor = actor.NombreDescriptivo
                }, tx);

            var cambios = new List<Cambio>
            {
                new(FusionServicio.CampoSobreviviente, null, $"{req.IdSobreviviente}|N.º {calc.NumeroSobreviviente} · {calc.NombreSobreviviente}"),
                new(FusionServicio.CampoDuplicado, null, $"{req.IdDuplicado}|N.º {calc.NumeroDuplicado} · {calc.NombreDuplicado}"),
                new(FusionServicio.CampoMotivo, null, motivo),
            };
            foreach (var d in calc.Plan.Direcciones)
                cambios.Add(new($"{FusionServicio.PrefijoDireccion}{d.IdDireccion}]", null, FusionServicio.TextoDecisionDireccion(d)));
            foreach (var c in cambios)
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, NULL, @nuevo)",
                    new { idGestion, campo = c.Campo, nuevo = c.Nuevo }, tx);

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value);
            if (inicio.AplicarYa)
            {
                var errorAuto = await AplicarAutomaticaAsync(conexion, tx, actor, idGestion, inicio.NombreRegla);
                if (errorAuto is not null) { tx.Rollback(); return errorAuto; }
            }

            tx.Commit();
            return ResultadoOperacion.Ok(idGestion) with { Numero = numeroGestion };
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "El cliente a absorber ya tiene una gestion pendiente; debe resolverse antes.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
