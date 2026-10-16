using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Cambio de asignacion de aprobador: cuando quien debe aprobar el nivel actual de una gestion no esta disponible
/// (enfermedad, vacaciones, salida), alguien con GES_REASIGNAR pasa esas gestiones a otro usuario con GES_APROBAR.
/// Solo se cambia el nivel que esta pendiente; la cadena de superiores de los niveles siguientes sigue la original.
/// Cada cambio queda en auditoria con el motivo.
/// </summary>
public class ReasignacionRepositorio
{
    public const string Permiso = "GES_REASIGNAR";

    private readonly TenantContext _tenant;
    public ReasignacionRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    /// <summary>Usuarios activos que pueden aprobar gestiones (tienen GES_APROBAR por alguno de sus roles).</summary>
    public async Task<IReadOnlyList<UsuarioAprobadorResponse>> AprobadoresAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT u.id_usuario, u.nombre_completo
            FROM sec.USUARIO u
            WHERE u.id_compania = @idCompania AND u.estado = 'Activo' AND u.tipo_usuario = 'FISICO'
              AND EXISTS (SELECT 1 FROM sec.USUARIO_ROL ur
                          JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = ur.id_rol_autorizacion
                          JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
                          WHERE ur.id_usuario = u.id_usuario AND oa.codigo = 'GES_APROBAR')
            ORDER BY u.nombre_completo
            """, new { idCompania });
        return filas.Select(f => new UsuarioAprobadorResponse((long)f.id_usuario, (string)f.nombre_completo)).Cast<UsuarioAprobadorResponse>().ToList();
    }

    /// <summary>Gestiones pendientes cuyo nivel actual esta asignado a ese usuario.</summary>
    public async Task<IReadOnlyList<ReasignacionFilaResponse>> ListarAsync(long idCompania, long idUsuario)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT g.id_gestion, g.numero, tg.nombre AS tipo, u.nombre_completo AS solicitante, g.creado_en,
                   g.nivel_actual, g.niveles_requeridos,
                   CASE WHEN g.entidad_negocio = 'INTERLOCUTOR'
                        THEN N'Cliente N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = g.llave_registro), g.llave_registro)
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_ALTA'
                        THEN N'Alta de cliente · ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'razon_social'), LTRIM(ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'nombre1'), N'') + N' ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'apellido1'), N'')))
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_ROL'
                        THEN N'Roles del cliente N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = g.llave_registro), g.llave_registro)
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_RELACION'
                        THEN N'Relaciones del interlocutor N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = g.llave_registro), g.llave_registro)
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_KYC'
                        THEN N'KYC del cliente N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = g.llave_registro), g.llave_registro)
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_BLOQUEO'
                        THEN N'Bloqueo del cliente N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), g.llave_registro)
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_CONSENTIMIENTO'
                        THEN N'Consentimientos del cliente N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = g.llave_registro), g.llave_registro)
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_FALLECIMIENTO'
                        THEN N'Fallecimiento del cliente N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = g.llave_registro), g.llave_registro)
                        WHEN g.entidad_negocio = 'INTERLOCUTOR_DOCUMENTO'
                        THEN N'Documentos del cliente N.º ' + ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = g.llave_registro), g.llave_registro)
                        WHEN g.entidad_negocio = 'TIPO_CAMBIO' THEN N'Tipo de cambio'
                        ELSE g.entidad_negocio END AS registro
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            JOIN apr.GESTION_APROBACION a ON a.id_gestion = g.id_gestion AND a.nivel = g.nivel_actual
            WHERE g.id_compania = @idCompania AND g.estado = 'Pendiente de Aprobacion'
              AND a.tipo_aprobador <> 'AUTOMATICA' AND a.id_usuario_asignado = @idUsuario
            ORDER BY g.creado_en
            """, new { idCompania, idUsuario });
        return filas.Select(f => new ReasignacionFilaResponse(
            (long)f.id_gestion, (string)f.numero, (string)f.tipo, (string)f.registro, (string)f.solicitante,
            (int)(short)f.nivel_actual, (int)(short)f.niveles_requeridos, (DateTime)f.creado_en)).Cast<ReasignacionFilaResponse>().ToList();
    }

    public async Task<ReasignacionResultadoResponse> ReasignarAsync(ActorContext actor, ReasignarRequest req, bool permitirAuto)
    {
        var rechazadas = new List<ReasignacionRechazoResponse>();
        var ok = 0;
        if (string.IsNullOrWhiteSpace(req.Motivo))
            return new ReasignacionResultadoResponse(0, [new ReasignacionRechazoResponse("", "El motivo es obligatorio.")]);
        if (req.IdsGestion is null || req.IdsGestion.Count == 0)
            return new ReasignacionResultadoResponse(0, [new ReasignacionRechazoResponse("", "Seleccione al menos una gestion.")]);

        using var c = NuevaConexion();
        c.Open();

        var nuevo = await c.QuerySingleOrDefaultAsync(
            "SELECT id_usuario, nombre_completo, estado FROM sec.USUARIO WHERE id_usuario = @id AND id_compania = @c",
            new { id = req.IdUsuarioNuevo, c = actor.IdCompania });
        if (nuevo is null || (string)nuevo.estado != "Activo")
            return new ReasignacionResultadoResponse(0, [new ReasignacionRechazoResponse("", "El nuevo aprobador no existe o esta inactivo.")]);
        var puede = (await AprobadoresAsync(actor.IdCompania)).Any(a => a.IdUsuario == req.IdUsuarioNuevo);
        if (!puede)
            return new ReasignacionResultadoResponse(0, [new ReasignacionRechazoResponse("", $"{(string)nuevo.nombre_completo} no tiene permiso para aprobar gestiones (GES_APROBAR).")]);

        var motivo = req.Motivo.Trim();
        foreach (var idGestion in req.IdsGestion.Distinct())
        {
            using var tx = c.BeginTransaction();
            try
            {
                var g = await c.QuerySingleOrDefaultAsync(
                    """
                    SELECT g.id_gestion, g.numero, g.estado, g.id_usuario_solicitante, g.nivel_actual,
                           a.tipo_aprobador, a.id_usuario_asignado, a.id_grupo_asignado, a.id_usuario_original,
                           tg.permite_mismo_usuario_multiples_niveles AS permite_repetir,
                           ISNULL(ua.nombre_completo, CASE a.tipo_aprobador WHEN 'GRUPO' THEN 'Grupo ' + gr.nombre ELSE 'Cualquier aprobador' END) AS asignado_nombre
                    FROM apr.GESTION g WITH (UPDLOCK, ROWLOCK)
                    JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
                    JOIN apr.GESTION_APROBACION a ON a.id_gestion = g.id_gestion AND a.nivel = g.nivel_actual
                    LEFT JOIN sec.USUARIO ua ON ua.id_usuario = a.id_usuario_asignado
                    LEFT JOIN sec.GRUPO_USUARIOS gr ON gr.id_grupo = a.id_grupo_asignado
                    WHERE g.id_gestion = @id AND g.id_compania = @c
                    """, new { id = idGestion, c = actor.IdCompania }, tx);
                if (g is null) { tx.Rollback(); rechazadas.Add(new(idGestion.ToString(), "La gestion no existe.")); continue; }
                var numero = (string)g.numero;
                if ((string)g.estado != GestionRepositorio.EstadoPendiente)
                { tx.Rollback(); rechazadas.Add(new(numero, "Ya no esta pendiente de aprobacion.")); continue; }
                if ((string)g.tipo_aprobador == MotorAprobacion.TipoAutomatica)
                { tx.Rollback(); rechazadas.Add(new(numero, "El nivel actual es automatico.")); continue; }
                if ((long?)g.id_usuario_asignado == req.IdUsuarioNuevo)
                { tx.Rollback(); rechazadas.Add(new(numero, "Ya esta asignada a ese usuario.")); continue; }
                if ((long)g.id_usuario_solicitante == req.IdUsuarioNuevo && !permitirAuto)
                { tx.Rollback(); rechazadas.Add(new(numero, "El nuevo aprobador es quien solicito el cambio.")); continue; }
                if (!(bool)g.permite_repetir && await c.ExecuteScalarAsync<bool>(
                        "SELECT CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION_APROBACION WHERE id_gestion = @id AND estado = 'Aprobado' AND id_usuario_aprobador = @u) THEN 1 ELSE 0 END",
                        new { id = idGestion, u = req.IdUsuarioNuevo }, tx))
                { tx.Rollback(); rechazadas.Add(new(numero, "El nuevo aprobador ya aprobo un nivel anterior de esta gestion.")); continue; }

                var nivel = (short)g.nivel_actual;
                // Se conserva el aprobador original del nivel: la cadena de superiores de los niveles siguientes parte de el.
                await c.ExecuteAsync(
                    """
                    UPDATE apr.GESTION_APROBACION
                       SET id_usuario_original = COALESCE(id_usuario_original, id_usuario_asignado),
                           tipo_aprobador = 'USUARIO', id_usuario_asignado = @nuevo, id_grupo_asignado = NULL
                     WHERE id_gestion = @id AND nivel = @nivel
                    """, new { id = idGestion, nivel, nuevo = req.IdUsuarioNuevo }, tx);
                await c.ExecuteAsync(
                    "UPDATE apr.GESTION SET modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_gestion = @id",
                    new { id = idGestion, por = actor.NombreDescriptivo }, tx);
                await Auditoria.CambioAsync(c, tx, actor, "GESTION", idGestion, $"aprobador_nivel_{nivel}",
                    (string)g.asignado_nombre, $"{(string)nuevo.nombre_completo} (motivo: {motivo})", idGestion);
                tx.Commit();
                ok++;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        return new ReasignacionResultadoResponse(ok, rechazadas);
    }
}
