using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Gestion CLI_DOCUMENTO: agregar, reemplazar y retirar documentos del expediente de un cliente (ver DocumentoGestionServicio).</summary>
public partial class GestionRepositorio
{
    public const string TipoDocumentoGestion = DocumentoGestionServicio.TipoGestion;
    public const string EntidadDocumento = DocumentoGestionServicio.Entidad;

    public async Task<ResultadoOperacion> SolicitarDocumentosAsync(ActorContext actor, DocumentoSolicitudRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");
        if (req is null) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Faltan los datos de la solicitud.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var v = await DocumentoGestionServicio.ValidarAsync(conexion, tx, actor, req.IdInterlocutor, req.Operaciones);
            if (v.Error is not null) { tx.Rollback(); return v.Error; }

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoDocumentoGestion }, tx);
            if (idTipoGestion is null)
            { tx.Rollback(); return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoDocumentoGestion}; correr documentos_ronda37.sql."); }

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var llave = req.IdInterlocutor.ToString(CultureInfo.InvariantCulture);
            var comentario = string.IsNullOrWhiteSpace(req.Comentario) ? null : req.Comentario.Trim();
            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, @comentario, @creadoPor)
                """,
                new { numeroGestion, actor.IdCompania, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario, entidad = EntidadDocumento, llave, comentario, creadoPor = actor.NombreDescriptivo }, tx);

            foreach (var op in v.Operaciones)
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, @anterior, @nuevo)",
                    new { idGestion, campo = op.Campo, anterior = op.Anterior, nuevo = op.Nuevo }, tx);

            await DocumentoGestionServicio.PrepararTemporalesAsync(conexion, tx, req.IdInterlocutor, v.Operaciones);

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value);
            if (inicio.AplicarYa)
            {
                var errorAuto = await AplicarAutomaticaAsync(conexion, tx, actor, idGestion, inicio.NombreRegla);
                if (errorAuto is not null) { tx.Rollback(); return errorAuto; }
            }
            tx.Commit();
            return ResultadoOperacion.Ok(idGestion) with { Numero = numeroGestion, Datos = new DocumentoSolicitudResponse(idGestion, numeroGestion, inicio.AplicarYa) };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
