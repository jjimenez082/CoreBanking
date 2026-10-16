using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Consulta de la bitacora de cambios (aud.LOG_AUDITORIA). Solo lectura.</summary>
public class AuditoriaConsultaRepositorio
{
    public const string ObjetoConsulta = "AUD_CONSULTA";

    private readonly TenantContext _tenant;
    public AuditoriaConsultaRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    public async Task<IReadOnlyList<AuditoriaEntidadResponse>> EntidadesAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync<string>(
            "SELECT DISTINCT entidad_negocio FROM aud.LOG_AUDITORIA WHERE id_compania = @idCompania", new { idCompania });
        return filas.Select(e => new AuditoriaEntidadResponse(e, AuditoriaLegible.EntidadNombre(e))).OrderBy(e => e.Nombre).ToList();
    }

    private const string Filtro =
        """
        FROM aud.LOG_AUDITORIA l
        LEFT JOIN sec.USUARIO u ON u.id_usuario = l.id_usuario
        LEFT JOIN sec.USUARIO_INTEGRACION ui ON ui.id_usuario_integracion = l.id_usuario_integracion
        WHERE l.id_compania = @idCompania
          AND (@entidad IS NULL OR l.entidad_negocio = @entidad)
          AND (@llave IS NULL OR l.llave_registro = @llave)
          AND (@idUsuario IS NULL OR l.id_usuario = @idUsuario)
          AND (@accion IS NULL OR l.accion = @accion)
          AND (@desde IS NULL OR l.fecha_hora >= @desde)
          AND (@hasta IS NULL OR l.fecha_hora < @hasta)
          AND (@idGestion IS NULL OR EXISTS (SELECT 1 FROM apr.GESTION gf WHERE gf.id_gestion = l.id_gestion AND TRY_CONVERT(bigint, gf.numero) = @idGestion))
          AND (@texto IS NULL OR l.nombre_campo LIKE @texto OR l.valor_anterior LIKE @texto OR l.valor_nuevo LIKE @texto)
        """;

    /// <summary>
    /// Clave de evento: todo lo que una operacion escribio sobre un mismo registro. Con gestion: (entidad, registro, gestion);
    /// sin gestion: (entidad, registro, usuario o integracion, segundo).
    /// </summary>
    private const string Grupo =
        """
        CONCAT(l.entidad_negocio, N'|', l.llave_registro, N'|',
               CASE WHEN l.id_gestion IS NOT NULL THEN CONCAT(N'G', CONVERT(nvarchar(20), l.id_gestion))
                    ELSE CONCAT(N'T', CONVERT(nvarchar(19), l.fecha_hora, 120), N'|', ISNULL(CONVERT(nvarchar(20), l.id_usuario), N''), N'|', ISNULL(CONVERT(nvarchar(20), l.id_usuario_integracion), N'')) END)
        """;

    private static object Parametros(long idCompania, string? entidad, string? llave, long? idUsuario, string? accion, DateOnly? desde, DateOnly? hasta,
        long? idGestion, string? texto, int pagina, int tamano, PerfilEfectivo? perfil = null)
    {
        if (accion is not null && accion is not ("Alta" or "Cambio" or "Baja" or "Consulta")) accion = null;
        return new
        {
            idCompania,
            entidad = string.IsNullOrWhiteSpace(entidad) ? null : entidad.Trim(),
            llave = string.IsNullOrWhiteSpace(llave) ? null : llave.Trim(),
            idUsuario, accion, idGestion,
            desde = desde?.ToDateTime(TimeOnly.MinValue),
            hasta = hasta?.AddDays(1).ToDateTime(TimeOnly.MinValue),
            texto = string.IsNullOrWhiteSpace(texto) ? null : "%" + texto.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%",
            salto = (pagina - 1) * tamano, tamano
        };
    }

    /// <summary>Fechas en UTC (como se guardan). 'hasta' incluye todo ese dia. Lista plana (una fila por campo); la usa la exportacion.</summary>
    public async Task<AuditoriaPaginaResponse> BuscarAsync(
        long idCompania, string? entidad, string? llave, long? idUsuario, string? accion, DateOnly? desde, DateOnly? hasta,
        long? idGestion, string? texto, int pagina, int tamano, PerfilEfectivo? perfil = null)
    {
        pagina = Math.Max(1, pagina);
        tamano = Math.Clamp(tamano, 10, 200);
        var p = Parametros(idCompania, entidad, llave, idUsuario, accion, desde, hasta, idGestion, texto, pagina, tamano);

        using var c = NuevaConexion();
        var total = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) " + Filtro, p);
        var filas = await c.QueryAsync(
            """
            SELECT l.id_log_auditoria, l.fecha_hora, l.entidad_negocio, l.llave_registro, l.accion, l.nombre_campo,
                   l.valor_anterior, l.valor_nuevo, l.id_gestion,
                   (SELECT g.numero FROM apr.GESTION g WHERE g.id_gestion = l.id_gestion) AS numero_gestion,
                   COALESCE(u.nombre_completo, ui.nombre, 'Sistema') AS usuario
            """ + "\n" + Filtro + """

            ORDER BY l.fecha_hora DESC, l.id_log_auditoria DESC
            OFFSET @salto ROWS FETCH NEXT @tamano ROWS ONLY
            """, p);
        var lista = filas.ToList();
        var cat = lista.Count > 0 ? await AuditoriaLegible.CargarAsync(c) : null;
        var items = lista.Select(f =>
        {
            var ent = (string)f.entidad_negocio;
            var campoTec = (string?)f.nombre_campo;
            return new AuditoriaFilaResponse(
                (long)f.id_log_auditoria, (DateTime)f.fecha_hora, ent, (string)f.llave_registro, (string)f.accion,
                AuditoriaLegible.Campo(ent, campoTec),
                AuditoriaLegible.Valor(ent, campoTec, EnmascaradoClientes.ValorAuditoria(perfil, ent, campoTec, (string?)f.valor_anterior, (string?)f.valor_nuevo, (string?)f.valor_anterior), cat!),
                AuditoriaLegible.Valor(ent, campoTec, EnmascaradoClientes.ValorAuditoria(perfil, ent, campoTec, (string?)f.valor_anterior, (string?)f.valor_nuevo, (string?)f.valor_nuevo), cat!),
                (long?)f.id_gestion, (string)f.usuario, AuditoriaLegible.EntidadNombre(ent), (string?)f.numero_gestion);
        }).ToList();
        return new AuditoriaPaginaResponse(items, pagina, tamano, total);
    }

    /// <summary>Vista por eventos: un encabezado por operacion y todos los campos en el detalle. La paginacion es por evento.</summary>
    public async Task<AuditoriaEventoPaginaResponse> BuscarEventosAsync(
        long idCompania, string? entidad, string? llave, long? idUsuario, string? accion, DateOnly? desde, DateOnly? hasta,
        long? idGestion, string? texto, int pagina, int tamano, PerfilEfectivo? perfil = null)
    {
        pagina = Math.Max(1, pagina);
        tamano = Math.Clamp(tamano, 10, 100);
        var p = Parametros(idCompania, entidad, llave, idUsuario, accion, desde, hasta, idGestion, texto, pagina, tamano);

        using var c = NuevaConexion();
        var total = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM (SELECT {Grupo} AS g {Filtro} GROUP BY {Grupo}) x", p);
        var grupos = (await c.QueryAsync<string>(
            $"""
            SELECT {Grupo} AS g {Filtro}
            GROUP BY {Grupo}
            ORDER BY MAX(l.fecha_hora) DESC, MAX(l.id_log_auditoria) DESC
            OFFSET @salto ROWS FETCH NEXT @tamano ROWS ONLY
            """, p)).ToList();
        if (grupos.Count == 0) return new AuditoriaEventoPaginaResponse([], pagina, tamano, total);

        var filas = (await c.QueryAsync(
            $"""
            SELECT {Grupo} AS g, l.id_log_auditoria, l.fecha_hora, l.entidad_negocio, l.llave_registro, l.accion, l.nombre_campo,
                   l.valor_anterior, l.valor_nuevo, l.id_gestion,
                   (SELECT gg.numero FROM apr.GESTION gg WHERE gg.id_gestion = l.id_gestion) AS numero_gestion,
                   COALESCE(u.nombre_completo, ui.nombre, 'Sistema') AS usuario
            {Filtro}
              AND {Grupo} IN @grupos
            ORDER BY l.id_log_auditoria
            """, MasGrupos(p, grupos))).ToList();
        var cat = await AuditoriaLegible.CargarAsync(c);

        return new AuditoriaEventoPaginaResponse(ConstruirEventos(filas, grupos, cat, perfil), pagina, tamano, total);
    }

    private static List<AuditoriaEventoResponse> ConstruirEventos(List<dynamic> filas, List<string> grupos, AuditoriaLegible.Catalogos cat, PerfilEfectivo? perfil = null)
    {
        var porGrupo = filas.GroupBy(f => (string)f.g).ToDictionary(g => g.Key, g => g.ToList());
        var items = new List<AuditoriaEventoResponse>();
        foreach (var g in grupos)
        {
            if (!porGrupo.TryGetValue(g, out var lineas) || lineas.Count == 0) continue;
            var primera = lineas[0];
            var ent = (string)primera.entidad_negocio;
            var detalle = lineas.Select(f =>
            {
                var campoTec = (string?)f.nombre_campo;
                return new AuditoriaDetalleResponse((long)f.id_log_auditoria, (string)f.accion, AuditoriaLegible.Campo(ent, campoTec),
                    AuditoriaLegible.Valor(ent, campoTec, EnmascaradoClientes.ValorAuditoria(perfil, ent, campoTec, (string?)f.valor_anterior, (string?)f.valor_nuevo, (string?)f.valor_anterior), cat),
                    AuditoriaLegible.Valor(ent, campoTec, EnmascaradoClientes.ValorAuditoria(perfil, ent, campoTec, (string?)f.valor_anterior, (string?)f.valor_nuevo, (string?)f.valor_nuevo), cat));
            }).ToList();
            var acciones = detalle.Select(x => x.Accion).Distinct().ToList();
            var accionResumen = acciones.Count == 1 ? acciones[0] : "Varias";
            var resumen = detalle.Count == 1 ? (detalle[0].Campo ?? detalle[0].Accion) : $"{detalle.Count} campos / registros";
            items.Add(new AuditoriaEventoResponse(
                g, lineas.Max(f => (DateTime)f.fecha_hora), (string)primera.usuario, ent, AuditoriaLegible.EntidadNombre(ent), (string)primera.llave_registro,
                accionResumen, resumen, (long?)primera.id_gestion, (string?)primera.numero_gestion, detalle.Count, detalle));
        }
        return items.OrderByDescending(x => x.FechaHora).ToList();
    }

    // ----- Auditoria de un cliente -----------------------------------------------------------------------------

    private const string FiltroCliente =
        """
        FROM aud.LOG_AUDITORIA l
        LEFT JOIN sec.USUARIO u ON u.id_usuario = l.id_usuario
        LEFT JOIN sec.USUARIO_INTEGRACION ui ON ui.id_usuario_integracion = l.id_usuario_integracion
        WHERE l.id_compania = @idCompania AND l.entidad_negocio = N'INTERLOCUTOR'
          AND l.llave_registro IN @llaves
          AND l.fecha_hora >= @desdeUtc AND l.fecha_hora < @hastaUtc
        """;

    private static async Task<(List<string> Llaves, List<AuditoriaClienteFusionado> Fusionados)> LlavesClienteAsync(
        SqlConnection c, long idCompania, long idInterlocutor, bool incluirFusionados)
    {
        var fusionados = (await c.QueryAsync(
            """
            SELECT f.id_fusionado AS id, i.numero
            FROM cli.INTERLOCUTOR_FUSION f JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = f.id_fusionado
            WHERE f.id_compania = @idCompania AND f.id_sobreviviente = @idInterlocutor
            ORDER BY f.id_fusion
            """, new { idCompania, idInterlocutor })).Select(x => new AuditoriaClienteFusionado((long)x.id, (string)x.numero)).ToList();
        var llaves = new List<string> { idInterlocutor.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (incluirFusionados) llaves.AddRange(fusionados.Select(x => x.IdInterlocutor.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return (llaves, fusionados);
    }

    /// <summary>
    /// Periodos con movimiento: nivel 'anio' (todos), 'mes' (los de un anio) o 'dia' (los de un mes). Los dias se agrupan en la zona del
    /// usuario (offsetMin = minutos de diferencia con UTC, p. ej. -360 para Costa Rica). Todo se guarda en UTC.
    /// </summary>
    public async Task<AuditoriaPeriodosResponse> PeriodosClienteAsync(
        long idCompania, long idInterlocutor, string nivel, int? anio, int? mes, int offsetMin, bool incluirFusionados)
    {
        offsetMin = Math.Clamp(offsetMin, -14 * 60, 14 * 60);
        nivel = nivel is "mes" or "dia" ? nivel : "anio";
        DateTime desdeLocal = new(2000, 1, 1), hastaLocal = new(2101, 1, 1);
        if (nivel == "mes" && anio is >= 2000 and <= 2100) { desdeLocal = new(anio.Value, 1, 1); hastaLocal = desdeLocal.AddYears(1); }
        else if (nivel == "dia" && anio is >= 2000 and <= 2100 && mes is >= 1 and <= 12) { desdeLocal = new(anio.Value, mes.Value, 1); hastaLocal = desdeLocal.AddMonths(1); }
        else nivel = "anio";

        var expr = nivel switch
        {
            "mes" => "MONTH(DATEADD(MINUTE, @offsetMin, l.fecha_hora))",
            "dia" => "DAY(DATEADD(MINUTE, @offsetMin, l.fecha_hora))",
            _ => "YEAR(DATEADD(MINUTE, @offsetMin, l.fecha_hora))"
        };

        using var c = NuevaConexion();
        var (llaves, fusionados) = await LlavesClienteAsync(c, idCompania, idInterlocutor, incluirFusionados);
        var p = new { idCompania, llaves, offsetMin, desdeUtc = desdeLocal.AddMinutes(-offsetMin), hastaUtc = hastaLocal.AddMinutes(-offsetMin) };
        var filas = (await c.QueryAsync(
            $"""
            SELECT {expr} AS valor, COUNT(DISTINCT {Grupo}) AS cantidad, MAX(l.fecha_hora) AS ultimo
            FROM aud.LOG_AUDITORIA l
            WHERE l.id_compania = @idCompania AND l.entidad_negocio = N'INTERLOCUTOR' AND l.llave_registro IN @llaves
              AND l.fecha_hora >= @desdeUtc AND l.fecha_hora < @hastaUtc
            GROUP BY {expr}
            ORDER BY {expr} DESC
            """, p)).ToList();
        var periodos = filas.Select(f => new AuditoriaPeriodoResponse((int)f.valor, (int)f.cantidad, (DateTime)f.ultimo)).ToList();

        // Resumen general (independiente del nivel): ultimo movimiento y total de eventos.
        var todo = await c.QuerySingleAsync(
            $"""
            SELECT COUNT(DISTINCT {Grupo}) AS total, MAX(l.fecha_hora) AS ultimo
            FROM aud.LOG_AUDITORIA l
            WHERE l.id_compania = @idCompania AND l.entidad_negocio = N'INTERLOCUTOR' AND l.llave_registro IN @llaves
            """, new { idCompania, llaves });
        return new AuditoriaPeriodosResponse(nivel, anio, mes, periodos, (DateTime?)todo.ultimo, (int)todo.total, fusionados);
    }

    /// <summary>Eventos de un dia local en un rango de horas [horaDesde, horaHasta) (0 a 24), con encabezado y detalle.</summary>
    public async Task<AuditoriaEventoPaginaResponse> EventosClienteAsync(
        long idCompania, long idInterlocutor, DateOnly fecha, int horaDesde, int horaHasta, int offsetMin, bool incluirFusionados, int pagina, int tamano, PerfilEfectivo? perfil = null)
    {
        offsetMin = Math.Clamp(offsetMin, -14 * 60, 14 * 60);
        horaDesde = Math.Clamp(horaDesde, 0, 23);
        horaHasta = Math.Clamp(horaHasta, horaDesde + 1, 24);
        pagina = Math.Max(1, pagina);
        tamano = Math.Clamp(tamano, 10, 100);
        var inicioLocal = fecha.ToDateTime(TimeOnly.MinValue);

        using var c = NuevaConexion();
        var (llaves, _) = await LlavesClienteAsync(c, idCompania, idInterlocutor, incluirFusionados);
        var p = new
        {
            idCompania, llaves,
            desdeUtc = inicioLocal.AddHours(horaDesde).AddMinutes(-offsetMin), hastaUtc = inicioLocal.AddHours(horaHasta).AddMinutes(-offsetMin),
            salto = (pagina - 1) * tamano, tamano
        };
        var total = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM (SELECT {Grupo} AS g {FiltroCliente} GROUP BY {Grupo}) x", p);
        var grupos = (await c.QueryAsync<string>(
            $"""
            SELECT {Grupo} AS g {FiltroCliente}
            GROUP BY {Grupo}
            ORDER BY MAX(l.fecha_hora) DESC, MAX(l.id_log_auditoria) DESC
            OFFSET @salto ROWS FETCH NEXT @tamano ROWS ONLY
            """, p)).ToList();
        if (grupos.Count == 0) return new AuditoriaEventoPaginaResponse([], pagina, tamano, total);
        var filas = (await c.QueryAsync(
            $"""
            SELECT {Grupo} AS g, l.id_log_auditoria, l.fecha_hora, l.entidad_negocio, l.llave_registro, l.accion, l.nombre_campo,
                   l.valor_anterior, l.valor_nuevo, l.id_gestion,
                   (SELECT gg.numero FROM apr.GESTION gg WHERE gg.id_gestion = l.id_gestion) AS numero_gestion,
                   COALESCE(u.nombre_completo, ui.nombre, 'Sistema') AS usuario
            {FiltroCliente}
              AND {Grupo} IN @grupos
            ORDER BY l.id_log_auditoria
            """, MasGrupos(p, grupos))).ToList();
        var cat = await AuditoriaLegible.CargarAsync(c);
        return new AuditoriaEventoPaginaResponse(ConstruirEventos(filas, grupos, cat, perfil), pagina, tamano, total);
    }

    private static DynamicParameters MasGrupos(object p, List<string> grupos)
    {
        var dp = new DynamicParameters(p);
        dp.Add("grupos", grupos);
        return dp;
    }
}
