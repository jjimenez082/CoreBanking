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

    public async Task<IReadOnlyList<string>> EntidadesAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync<string>(
            "SELECT DISTINCT entidad_negocio FROM aud.LOG_AUDITORIA WHERE id_compania = @idCompania ORDER BY entidad_negocio", new { idCompania });
        return filas.ToList();
    }

    /// <summary>Fechas en UTC (como se guardan). 'hasta' incluye todo ese dia.</summary>
    public async Task<AuditoriaPaginaResponse> BuscarAsync(
        long idCompania, string? entidad, string? llave, long? idUsuario, string? accion, DateOnly? desde, DateOnly? hasta,
        long? idGestion, string? texto, int pagina, int tamano)
    {
        pagina = Math.Max(1, pagina);
        tamano = Math.Clamp(tamano, 10, 200);
        if (accion is not null && accion is not ("Alta" or "Cambio" or "Baja")) accion = null;

        const string filtro =
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
              AND (@idGestion IS NULL OR l.id_gestion = @idGestion)
              AND (@texto IS NULL OR l.nombre_campo LIKE @texto OR l.valor_anterior LIKE @texto OR l.valor_nuevo LIKE @texto)
            """;
        var p = new
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

        using var c = NuevaConexion();
        var total = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) " + filtro, p);
        var filas = await c.QueryAsync(
            """
            SELECT l.id_log_auditoria, l.fecha_hora, l.entidad_negocio, l.llave_registro, l.accion, l.nombre_campo,
                   l.valor_anterior, l.valor_nuevo, l.id_gestion,
                   COALESCE(u.nombre_completo, ui.nombre, 'Sistema') AS usuario
            """ + "\n" + filtro + """

            ORDER BY l.fecha_hora DESC, l.id_log_auditoria DESC
            OFFSET @salto ROWS FETCH NEXT @tamano ROWS ONLY
            """, p);
        var items = filas.Select(f => new AuditoriaFilaResponse(
            (long)f.id_log_auditoria, (DateTime)f.fecha_hora, (string)f.entidad_negocio, (string)f.llave_registro, (string)f.accion,
            (string?)f.nombre_campo, (string?)f.valor_anterior, (string?)f.valor_nuevo, (long?)f.id_gestion, (string)f.usuario)).Cast<AuditoriaFilaResponse>().ToList();
        return new AuditoriaPaginaResponse(items, pagina, tamano, total);
    }
}
