using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestion CLI_CONSENTIMIENTO: otorgar y/o revocar consentimientos de un cliente. Carril propio (entidad INTERLOCUTOR_CONSENTIMIENTO, llave = id del cliente).
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoConsentimiento = "CLI_CONSENTIMIENTO";
    public const string EntidadConsentimiento = ConsentimientoServicio.Entidad;

    public async Task<ResultadoOperacion> SolicitarConsentimientosAsync(ActorContext actor, long idInterlocutor, ConsentimientoSolicitudRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");
        if (req is null) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Faltan los datos del consentimiento.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var (error, estado, codigo) = await ConsentimientoServicio.ValidarAsync(conexion, tx, actor.IdCompania, idInterlocutor, req.Cambios, req.IdCanal, req.Comentario);
            if (error is not null) return ResultadoOperacion.Fallo(estado, codigo ?? "datos_invalidos", error);

            var llave = idInterlocutor.ToString(CultureInfo.InvariantCulture);
            var pendiente = await conexion.ExecuteScalarAsync<string?>(
                "SELECT TOP 1 numero FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
                new { e = EntidadConsentimiento, llave, p = EstadoPendiente }, tx);
            if (pendiente is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"Ya existe la gestión {pendiente} pendiente de aprobación para los consentimientos de este cliente; debe resolverse antes.");

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoConsentimiento }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoConsentimiento}; correr consentimientos_ronda33.sql.");

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, NULL, @creadoPor)
                """,
                new { numeroGestion, actor.IdCompania, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario, entidad = EntidadConsentimiento, llave, creadoPor = actor.NombreDescriptivo }, tx);

            var filas = req.Cambios.Select(x => (ConsentimientoServicio.PrefijoTipo + x.IdTipoConsentimiento.ToString(CultureInfo.InvariantCulture), x.Otorgar ? ConsentimientoServicio.Otorgar : ConsentimientoServicio.Revocar)).ToList();
            filas.Add((ConsentimientoServicio.CampoCanal, req.IdCanal.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrWhiteSpace(req.Comentario)) filas.Add((ConsentimientoServicio.CampoComentario, req.Comentario.Trim()));
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
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "Ya existe una gestión pendiente de aprobación para los consentimientos de este cliente.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
