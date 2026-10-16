using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Notificaciones del usuario para la campanita de la barra superior. Por ahora
/// se derivan de datos que ya existen (sin tabla propia):
///   - gestiones pendientes cuyo nivel actual le corresponde (usuario nombrado, integrante del grupo o superior)
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
        if (actor.EsIntegracion) return lista;

        using var conexion = NuevaConexion();

        if (actor.TienePermiso("GES_APROBAR"))
        {
            var pendientes = await conexion.QueryAsync(
                """
                SELECT TOP (@tope) g.id_gestion, g.numero, tg.nombre AS tipo, CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     ELSE ISNULL(g.llave_registro, '') END AS llave,
                       u.nombre_completo AS solicitante, g.creado_en
                FROM apr.GESTION g
                JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
                JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
                JOIN apr.GESTION_APROBACION ac ON ac.id_gestion = g.id_gestion AND ac.nivel = g.nivel_actual
                WHERE g.id_compania = @c AND g.estado = 'Pendiente de Aprobacion' AND g.id_usuario_solicitante <> @u
                  AND ac.tipo_aprobador <> 'AUTOMATICA'
                  AND (tg.permite_mismo_usuario_multiples_niveles = 1
                       OR NOT EXISTS (SELECT 1 FROM apr.GESTION_APROBACION p WHERE p.id_gestion = g.id_gestion AND p.estado = 'Aprobado' AND p.id_usuario_aprobador = @u))
                  AND ((ac.tipo_aprobador = 'USUARIO' AND ac.id_usuario_asignado = @u)
                       OR (ac.tipo_aprobador = 'GRUPO' AND EXISTS (SELECT 1 FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = ac.id_grupo_asignado AND m.id_usuario = @u))
                       OR (ac.tipo_aprobador = 'SUPERIOR' AND (@porSup = 0 OR ac.id_usuario_asignado IS NULL OR ac.id_usuario_asignado = @u)))
                ORDER BY g.creado_en DESC
                """, new
                {
                    c = actor.IdCompania, u = actor.IdUsuario, tope,
                    // Con aprobacion por superior, solo se avisa de las gestiones que el usuario puede resolver.
                    porSup = _config.GetValue("Gestiones:AprobacionPorSuperior", true) ? 1 : 0
                });
            foreach (var g in pendientes)
                lista.Add(new NotificacionResponse(
                    $"ges-pend-{(long)g.id_gestion}", "gestion_pendiente",
                    $"Gestión {(string)g.numero} pendiente de aprobar",
                    $"{(string)g.tipo} · registro {(string)g.llave} · solicitada por {(string)g.solicitante}",
                    (DateTime)g.creado_en, $"/gestiones?ver={(long)g.id_gestion}"));
        }

        var resueltas = await conexion.QueryAsync(
            """
            SELECT TOP (@tope) g.id_gestion, g.numero, tg.nombre AS tipo, g.estado, CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
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
                $"Gestión {(string)g.numero} {(aprobada ? "aprobada" : "rechazada")}",
                aprobada ? $"{(string)g.tipo} · el cambio ya fue aplicado" : $"{(string)g.tipo} · motivo: {motivo}",
                (DateTime)g.fecha_resolucion, $"/gestiones?ver={(long)g.id_gestion}"));
        }

        var devueltas = await conexion.QueryAsync(
            """
            SELECT TOP (@tope) g.id_gestion, g.numero, tg.nombre AS tipo, g.comentario_devolucion, g.devuelto_por, g.fecha_devolucion
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            WHERE g.id_compania = @c AND g.id_usuario_solicitante = @u AND g.estado = 'Devuelta'
            ORDER BY g.fecha_devolucion DESC
            """, new { c = actor.IdCompania, u = actor.IdUsuario, tope });
        foreach (var g in devueltas)
            lista.Add(new NotificacionResponse(
                $"ges-dev-{(long)g.id_gestion}", "gestion_devuelta",
                $"Gestión {(string)g.numero} devuelta para corrección",
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

    // ----- Estado "leida" por usuario (sec.NOTIFICACION_LEIDA) ---------------------------------

    public async Task<IReadOnlyList<string>> LeidasAsync(ActorContext actor)
    {
        if (actor.EsIntegracion || actor.IdUsuario is null) return [];
        using var c = NuevaConexion();
        return (await c.QueryAsync<string>("SELECT clave FROM sec.NOTIFICACION_LEIDA WHERE id_usuario = @u", new { u = actor.IdUsuario })).ToList();
    }

    private static List<string> Limpias(IEnumerable<string>? claves) =>
        (claves ?? []).Select(k => (k ?? "").Trim()).Where(k => k.Length is > 0 and <= 120).Distinct().Take(200).ToList();

    public async Task MarcarAsync(ActorContext actor, IEnumerable<string>? claves)
    {
        var lista = Limpias(claves);
        if (actor.EsIntegracion || actor.IdUsuario is null || lista.Count == 0) return;
        using var c = NuevaConexion();
        await c.ExecuteAsync(
            """
            INSERT INTO sec.NOTIFICACION_LEIDA (id_usuario, clave)
            SELECT @u, k.clave FROM (SELECT DISTINCT value AS clave FROM OPENJSON(@json)) k
            WHERE NOT EXISTS (SELECT 1 FROM sec.NOTIFICACION_LEIDA x WHERE x.id_usuario = @u AND x.clave = k.clave);
            -- limpieza: lo leido hace mas de 90 dias ya no aparece en ninguna lista
            DELETE FROM sec.NOTIFICACION_LEIDA WHERE id_usuario = @u AND leida_en < DATEADD(day, -90, SYSUTCDATETIME());
            """, new { u = actor.IdUsuario, json = System.Text.Json.JsonSerializer.Serialize(lista) });
    }

    public async Task DesmarcarAsync(ActorContext actor, IEnumerable<string>? claves)
    {
        var lista = Limpias(claves);
        if (actor.EsIntegracion || actor.IdUsuario is null || lista.Count == 0) return;
        using var c = NuevaConexion();
        await c.ExecuteAsync("DELETE FROM sec.NOTIFICACION_LEIDA WHERE id_usuario = @u AND clave IN @lista", new { u = actor.IdUsuario, lista });
    }

    /// <summary>Al abrir o resolver una gestion, sus avisos quedan leidos (pendiente, devuelta, aprobada/rechazada), sea cual sea el camino usado.</summary>
    public Task MarcarGestionAsync(ActorContext actor, long idGestion)
        => MarcarAsync(actor, [$"ges-pend-{idGestion}", $"ges-dev-{idGestion}", $"ges-res-{idGestion}-Aplicada", $"ges-res-{idGestion}-Rechazada"]);
}
