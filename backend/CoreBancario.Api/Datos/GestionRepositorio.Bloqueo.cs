using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestiones CLI_BLOQUEO y CLI_DESBLOQUEO: bloquear o levantar un tipo de bloqueo de un cliente. Carril propio (entidad INTERLOCUTOR_BLOQUEO):
/// no depende de que haya gestiones de datos o de KYC pendientes y viceversa. Una gestion pendiente por cliente y tipo.
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoBloqueo = "CLI_BLOQUEO";
    public const string TipoDesbloqueo = "CLI_DESBLOQUEO";
    public const string EntidadBloqueo = BloqueoServicio.Entidad;

    public Task<ResultadoOperacion> SolicitarBloqueoAsync(ActorContext actor, long idInterlocutor, BloqueoSolicitudRequest req, bool bloquear)
        => SolicitarCambioBloqueoAsync(actor, idInterlocutor, req, bloquear);

    private async Task<ResultadoOperacion> SolicitarCambioBloqueoAsync(ActorContext actor, long idInterlocutor, BloqueoSolicitudRequest req, bool bloquear)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");
        if (req is null) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Faltan los datos del bloqueo.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var (error, estado, codigo, nombreTipo) = await BloqueoServicio.ValidarAsync(conexion, tx, actor.IdCompania, idInterlocutor, req.IdTipoBloqueo, req.Comentario, bloquear);
            if (error is not null) return ResultadoOperacion.Fallo(estado, codigo ?? "datos_invalidos", error);

            var llave = BloqueoServicio.Llave(idInterlocutor, req.IdTipoBloqueo);
            var pendiente = await conexion.ExecuteScalarAsync<string?>(
                "SELECT TOP 1 numero FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
                new { e = EntidadBloqueo, llave, p = EstadoPendiente }, tx);
            if (pendiente is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"Ya existe la gestión {pendiente} pendiente de aprobación para este bloqueo; debe resolverse antes.");

            var codigoGestion = bloquear ? TipoBloqueo : TipoDesbloqueo;
            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = codigoGestion }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {codigoGestion}; correr bloqueos_ronda31.sql.");

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
                    numeroGestion, actor.IdCompania, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario,
                    entidad = EntidadBloqueo, llave, creadoPor = actor.NombreDescriptivo
                }, tx);

            foreach (var (campo, valor) in new[]
                     {
                         (BloqueoServicio.CampoAccion, bloquear ? BloqueoServicio.Bloquear : BloqueoServicio.Desbloquear),
                         (BloqueoServicio.CampoTipo, req.IdTipoBloqueo.ToString(CultureInfo.InvariantCulture)),
                         (BloqueoServicio.CampoComentario, req.Comentario.Trim()),
                     })
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
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "Ya existe una gestión pendiente de aprobación para este bloqueo.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
