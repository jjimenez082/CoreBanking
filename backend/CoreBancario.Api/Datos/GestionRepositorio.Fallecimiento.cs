using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Gestion CLI_FALLECIMIENTO: registrar el fallecimiento de una persona (aprobacion obligatoria, acta de defuncion como respaldo).</summary>
public partial class GestionRepositorio
{
    public const string EntidadFallecimiento = FallecimientoServicio.Entidad;

    public async Task<ResultadoOperacion> SolicitarFallecimientoAsync(ActorContext actor, long idInterlocutor, FallecimientoSolicitudRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");
        if (req is null) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Faltan los datos del fallecimiento.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var (error, estado, codigo) = await FallecimientoServicio.ValidarAsync(conexion, tx, actor, idInterlocutor, req.FechaFallecimiento, req.Comentario, null);
            if (error is not null) { tx.Rollback(); return ResultadoOperacion.Fallo(estado, codigo ?? "datos_invalidos", error); }

            var pendiente = await FusionServicio.GestionPendienteAsync(conexion, tx, actor.IdCompania, idInterlocutor, null, paraFusion: true);
            if (pendiente is not null)
            { tx.Rollback(); return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"El cliente tiene la gestión {pendiente} pendiente de aprobación; debe resolverse antes."); }

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = FallecimientoServicio.TipoGestion }, tx);
            if (idTipoGestion is null)
            { tx.Rollback(); return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {FallecimientoServicio.TipoGestion}; correr fallecimiento_ronda39.sql."); }

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var llave = idInterlocutor.ToString(CultureInfo.InvariantCulture);
            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, NULL, @creadoPor)
                """, new { numeroGestion, actor.IdCompania, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario, entidad = EntidadFallecimiento, llave, creadoPor = actor.NombreDescriptivo }, tx);

            var filas = new List<(string, string)> { (FallecimientoServicio.CampoFecha, req.FechaFallecimiento!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) };
            if (!string.IsNullOrWhiteSpace(req.Comentario)) filas.Add((FallecimientoServicio.CampoComentario, req.Comentario.Trim()));
            foreach (var (campo, valor) in filas)
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, NULL, @valor)",
                    new { idGestion, campo, valor }, tx);

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
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "Ya existe una gestión pendiente de aprobación para este cliente.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<FallecimientoInfoResponse?> ObtenerFallecimientoAsync(long idCompania, long idInterlocutor)
    {
        using var c = NuevaConexion();
        return await FallecimientoServicio.InfoAsync(c, idCompania, idInterlocutor);
    }
}
