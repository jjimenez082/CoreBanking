using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Notificaciones del usuario para la campanita de la barra superior. Por ahora
/// se derivan de datos que ya existen (sin tabla propia):
///   - gestiones de otros usuarios pendientes de aprobar (si tiene GES_APROBAR)
///   - gestiones propias resueltas en los ultimos 14 dias
///   - borradores propios que vencen en 3 dias o menos
/// Evolucion: tabla de notificaciones escrita por el motor de gestiones, con
/// tiempo real y correo opcional por tipo de evento.
/// </summary>
public class NotificacionesRepositorio
{
    private readonly TenantContext _tenantContext;

    private readonly IConfiguration _config;

    public NotificacionesRepositorio(TenantContext tenantContext, IConfiguration config)
    {
        _tenantContext = tenantContext;
        _config = config;
    }

    private SqlConnection NuevaConexion() => new(_tenantContext.ConnectionString);

    public async Task<IReadOnlyList<NotificacionResponse>> ListarAsync(ActorContext actor, bool historial = false)
    {
        // Campanita: lo mas reciente. Historial (app Notificaciones): ventana y tope mayores.
        var tope = historial ? 100 : 10;
        var dias = historial ? 60 : 14;
        var lista = new List<NotificacionResponse>();
        if (actor.IdUsuario is null) return lista;

        using var conexion = NuevaConexion();

        if (actor.TienePermiso("GES_APROBAR"))
        {
            var pendientes = await conexion.QueryAsync(
                """
                SELECT TOP (@tope) g.id_gestion, tg.nombre AS tipo, CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     ELSE ISNULL(g.llave_registro, '') END AS llave,
                       u.nombre_completo AS solicitante, g.creado_en
                FROM apr.GESTION g
                JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
                JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
                WHERE g.id_compania = @c AND g.estado = 'Pendiente de Aprobacion' AND g.id_usuario_solicitante <> @u
                  AND (@todas = 1 OR u.id_superior IS NULL OR u.id_superior = @u)
                ORDER BY g.creado_en DESC
                """, new
                {
                    c = actor.IdCompania, u = actor.IdUsuario, tope,
                    // Con aprobacion por superior, solo se avisa de las gestiones que el usuario puede resolver.
                    todas = !_config.GetValue("Gestiones:AprobacionPorSuperior", true) || actor.TienePermiso("GES_APROBAR_TODAS")
                });
            foreach (var g in pendientes)
                lista.Add(new NotificacionResponse(
                    $"ges-pend-{(long)g.id_gestion}", "gestion_pendiente",
                    $"Gestión #{(long)g.id_gestion} pendiente de aprobar",
                    $"{(string)g.tipo} · registro {(string)g.llave} · solicitada por {(string)g.solicitante}",
                    (DateTime)g.creado_en, $"/gestiones?ver={(long)g.id_gestion}"));
        }

        var resueltas = await conexion.QueryAsync(
            """
            SELECT TOP (@tope) g.id_gestion, tg.nombre AS tipo, g.estado, CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     ELSE ISNULL(g.llave_registro, '') END AS llave,
                   g.motivo_rechazo, g.fecha_resolucion
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            WHERE g.id_compania = @c AND g.id_usuario_solicitante = @u
              AND g.estado IN ('Aplicada', 'Rechazada') AND g.fecha_resolucion >= DATEADD(day, -@dias, SYSUTCDATETIME())
            ORDER BY g.fecha_resolucion DESC
            """, new { c = actor.IdCompania, u = actor.IdUsuario, tope, dias });
        foreach (var g in resueltas)
        {
            var aprobada = (string)g.estado == "Aplicada";
            var motivo = (string?)g.motivo_rechazo;
            lista.Add(new NotificacionResponse(
                $"ges-res-{(long)g.id_gestion}-{(string)g.estado}", aprobada ? "gestion_aprobada" : "gestion_rechazada",
                $"Gestión #{(long)g.id_gestion} {(aprobada ? "aprobada" : "rechazada")}",
                aprobada ? $"{(string)g.tipo} · el cambio ya fue aplicado" : $"{(string)g.tipo} · motivo: {motivo}",
                (DateTime)g.fecha_resolucion, $"/gestiones?ver={(long)g.id_gestion}"));
        }

        var devueltas = await conexion.QueryAsync(
            """
            SELECT TOP (@tope) g.id_gestion, tg.nombre AS tipo, g.comentario_devolucion, g.devuelto_por, g.fecha_devolucion
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            WHERE g.id_compania = @c AND g.id_usuario_solicitante = @u AND g.estado = 'Devuelta'
            ORDER BY g.fecha_devolucion DESC
            """, new { c = actor.IdCompania, u = actor.IdUsuario, tope });
        foreach (var g in devueltas)
            lista.Add(new NotificacionResponse(
                $"ges-dev-{(long)g.id_gestion}", "gestion_devuelta",
                $"Gestión #{(long)g.id_gestion} devuelta para corrección",
                $"{(string)g.tipo} · {(string?)g.devuelto_por}: {(string?)g.comentario_devolucion}",
                (DateTime)(g.fecha_devolucion ?? DateTime.UtcNow), $"/gestiones?ver={(long)g.id_gestion}"));

        var borradores = await conexion.QueryAsync(
            """
            SELECT id_borrador, resumen_nombre, vence_en, ISNULL(modificado_en, creado_en) AS fecha
            FROM cli.INTERLOCUTOR_BORRADOR
            WHERE id_compania = @c AND id_usuario = @u
              AND vence_en >= SYSUTCDATETIME() AND vence_en <= DATEADD(day, 3, SYSUTCDATETIME())
            """, new { c = actor.IdCompania, u = actor.IdUsuario });
        foreach (var b in borradores)
            lista.Add(new NotificacionResponse(
                $"bor-{(long)b.id_borrador}", "borrador_por_vencer",
                "Borrador por vencer",
                $"El alta de «{(string?)b.resumen_nombre ?? "sin nombre"}» vence el {((DateTime)b.vence_en).ToLocalTime():dd/MM/yyyy}",
                (DateTime)b.fecha, $"/interlocutores/clientes/nuevo?borrador={(long)b.id_borrador}"));

        return lista.OrderByDescending(n => n.Fecha).ToList();
    }
}
