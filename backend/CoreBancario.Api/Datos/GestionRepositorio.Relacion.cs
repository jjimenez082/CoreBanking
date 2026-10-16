using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestion CLI_RELACION: agregar o cerrar relaciones (representante legal, accionista, beneficiario final, persona de contacto, miembro de grupo, familiar).
/// La llave de la gestion es el id del interlocutor desde cuya ficha se pide, asi que solo puede haber una solicitud pendiente por interlocutor.
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoCambioRelacion = "CLI_RELACION";
    public const string EntidadRelacion = RelacionServicio.Entidad;

    public async Task<ResultadoOperacion> SolicitarCambioRelacionAsync(ActorContext actor, long idInterlocutor, RelacionSolicitudRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar un cambio de relaciones.");
        var motivo = (req.Motivo ?? "").Trim();
        if (motivo.Length < 10) return ResultadoOperacion.Fallo(400, "motivo_requerido", "Indique el motivo del cambio de relaciones (mínimo 10 caracteres).");
        if (motivo.Length > 500) return ResultadoOperacion.Fallo(400, "motivo_largo", "El motivo no puede superar 500 caracteres.");
        var agregar = (req.Agregar ?? []).ToList();
        var cerrar = (req.Cerrar ?? []).Distinct().ToList();
        var modificar = (req.Modificar ?? []).GroupBy(x => x.IdRelacion).Select(g => g.First()).ToList();

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var calc = await RelacionServicio.ValidarAsync(conexion, tx, actor.IdCompania, idInterlocutor, agregar, cerrar, modificar, null);
            if (calc.Errores.Count > 0)
                return ResultadoOperacion.Fallo(409, "cambio_relacion_no_valido", string.Join("\n", calc.Errores));

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoCambioRelacion }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoCambioRelacion}; correr relaciones_ronda23.sql.");

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
                    entidad = EntidadRelacion, llave = idInterlocutor.ToString(CultureInfo.InvariantCulture), creadoPor = actor.NombreDescriptivo
                }, tx);

            foreach (var (campo, anterior, valor) in await RelacionServicio.FilasAsync(conexion, tx, actor.IdCompania, agregar, cerrar, modificar, motivo, calc.Tipos))
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, @anterior, @valor)",
                    new { idGestion, campo, anterior, valor }, tx);

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
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "El interlocutor ya tiene una gestión de relaciones pendiente; debe resolverse antes.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
