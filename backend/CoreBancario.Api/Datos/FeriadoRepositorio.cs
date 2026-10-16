using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Calendario de feriados por pais (gen.FERIADO) y consulta de dias habiles. Todo cambio se audita.</summary>
public class FeriadoRepositorio
{
    public const string ObjetoAdministracion = "CAL_ADMINISTRACION";
    public const string Entidad = "FERIADO";
    private static readonly string[] Dias = ["DOM", "LUN", "MAR", "MIE", "JUE", "VIE", "SAB"];   // indice = DayOfWeek

    private readonly TenantContext _tenant;
    public FeriadoRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private static FeriadoResponse Mapear(dynamic f) => new(
        (long)f.id_feriado, (long)f.id_pais, (string)f.pais_nombre, DateOnly.FromDateTime((DateTime)f.fecha), (string)f.nombre, (bool)f.es_recurrente, (string)f.estado);

    public async Task<IReadOnlyList<FeriadoResponse>> ListarAsync(long idCompania, long? idPais, int? anio)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT f.id_feriado, f.id_pais, p.nombre AS pais_nombre, f.fecha, f.nombre, f.es_recurrente, f.estado
            FROM gen.FERIADO f
            JOIN gen.LISTA_VALORES_ITEM p ON p.id_lista_valores_item = f.id_pais
            WHERE f.id_compania = @idCompania AND (@idPais IS NULL OR f.id_pais = @idPais)
              AND (@anio IS NULL OR YEAR(f.fecha) = @anio OR (f.es_recurrente = 1 AND YEAR(f.fecha) <= @anio))
            ORDER BY MONTH(f.fecha), DAY(f.fecha), p.nombre
            """, new { idCompania, idPais, anio });
        return filas.Select(f => (FeriadoResponse)Mapear(f)).ToList();
    }

    private static async Task<string?> ValidarAsync(SqlConnection c, SqlTransaction tx, FeriadoGuardarRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nombre) || req.Nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        var ok = await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE t.codigo = @lista AND i.id_lista_valores_item = @p AND i.estado = 'Activo') THEN 1 ELSE 0 END
            """, new { lista = DireccionGeografia.ListaPais, p = req.IdPais }, tx);
        return ok ? null : "El pais no existe o esta inactivo.";
    }

    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, FeriadoGuardarRequest req)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var error = await ValidarAsync(c, tx, req);
            if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO gen.FERIADO (id_compania, id_pais, fecha, nombre, es_recurrente, creado_por)
                OUTPUT INSERTED.id_feriado VALUES (@c, @p, @f, @n, @r, @por)
                """, new { c = actor.IdCompania, p = req.IdPais, f = req.Fecha.ToDateTime(TimeOnly.MinValue), n = req.Nombre.Trim(), r = req.EsRecurrente, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, Entidad, id, $"{req.Fecha:yyyy-MM-dd} · {req.Nombre.Trim()}{(req.EsRecurrente ? " (anual)" : "")}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "feriado_duplicado", "Ya hay un feriado para ese pais y esa fecha.");
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long idFeriado, FeriadoGuardarRequest req)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var error = await ValidarAsync(c, tx, req);
            if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
            var a = await c.QuerySingleOrDefaultAsync(
                "SELECT id_pais, fecha, nombre, es_recurrente FROM gen.FERIADO WITH (UPDLOCK) WHERE id_feriado = @id AND id_compania = @c",
                new { id = idFeriado, c = actor.IdCompania }, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el feriado.");

            var fecha = req.Fecha.ToDateTime(TimeOnly.MinValue);
            await c.ExecuteAsync(
                "UPDATE gen.FERIADO SET id_pais = @p, fecha = @f, nombre = @n, es_recurrente = @r, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_feriado = @id",
                new { p = req.IdPais, f = fecha, n = req.Nombre.Trim(), r = req.EsRecurrente, por = actor.NombreDescriptivo, id = idFeriado }, tx);
            if ((long)a.id_pais != req.IdPais) await Auditoria.CambioAsync(c, tx, actor, Entidad, idFeriado, "id_pais", ((long)a.id_pais).ToString(), req.IdPais.ToString());
            if ((DateTime)a.fecha != fecha) await Auditoria.CambioAsync(c, tx, actor, Entidad, idFeriado, "fecha", ((DateTime)a.fecha).ToString("yyyy-MM-dd"), req.Fecha.ToString("yyyy-MM-dd"));
            if ((string)a.nombre != req.Nombre.Trim()) await Auditoria.CambioAsync(c, tx, actor, Entidad, idFeriado, "nombre", (string)a.nombre, req.Nombre.Trim());
            if ((bool)a.es_recurrente != req.EsRecurrente) await Auditoria.CambioAsync(c, tx, actor, Entidad, idFeriado, "es_recurrente", (bool)a.es_recurrente ? "1" : "0", req.EsRecurrente ? "1" : "0");
            tx.Commit();
            return ResultadoOperacion.Ok(idFeriado);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "feriado_duplicado", "Ya hay un feriado para ese pais y esa fecha.");
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long idFeriado, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado invalido.");
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var a = await c.ExecuteScalarAsync<string?>("SELECT estado FROM gen.FERIADO WITH (UPDLOCK) WHERE id_feriado = @id AND id_compania = @c", new { id = idFeriado, c = actor.IdCompania }, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el feriado.");
            if (a == estado) return ResultadoOperacion.Ok(idFeriado);
            await c.ExecuteAsync("UPDATE gen.FERIADO SET estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_feriado = @id",
                new { e = estado, por = actor.NombreDescriptivo, id = idFeriado }, tx);
            await Auditoria.CambioAsync(c, tx, actor, Entidad, idFeriado, "estado", a, estado);
            tx.Commit();
            return ResultadoOperacion.Ok(idFeriado);
        }
        catch { tx.Rollback(); throw; }
    }

    // ----- Dias habiles ---------------------------------------------------------------------------------

    private sealed record Reglas(HashSet<DayOfWeek> NoHabiles, List<(DateTime Fecha, string Nombre, bool Recurrente)> Feriados);

    private async Task<Reglas> CargarReglasAsync(long idCompania, long idPais)
    {
        using var c = NuevaConexion();
        var param = await ParametroRepositorio.ValorAsync(c, null, idCompania, "DIAS_NO_HABILES") ?? "SAB,DOM";
        var no = param.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Array.IndexOf(Dias, d.ToUpperInvariant())).Where(i => i >= 0).Select(i => (DayOfWeek)i).ToHashSet();
        var fer = (await c.QueryAsync(
            "SELECT fecha, nombre, es_recurrente FROM gen.FERIADO WHERE id_compania = @idCompania AND id_pais = @idPais AND estado = 'Activo'", new { idCompania, idPais }))
            .Select(f => ((DateTime)f.fecha, (string)f.nombre, (bool)f.es_recurrente)).ToList();
        return new Reglas(no, fer);
    }

    private static string? Motivo(Reglas r, DateOnly d)
    {
        foreach (var f in r.Feriados)
        {
            var coincide = f.Fecha.Date == d.ToDateTime(TimeOnly.MinValue)
                || (f.Recurrente && f.Fecha.Month == d.Month && f.Fecha.Day == d.Day && f.Fecha.Year <= d.Year);
            if (coincide) return $"Feriado: {f.Nombre}";
        }
        if (r.NoHabiles.Contains(d.DayOfWeek)) return "Dia no habil de la semana";
        return null;
    }

    public async Task<DiaHabilResponse> EsHabilAsync(long idCompania, long idPais, DateOnly fecha)
    {
        var reglas = await CargarReglasAsync(idCompania, idPais);
        var motivo = Motivo(reglas, fecha);
        return new DiaHabilResponse(fecha, motivo is null, motivo);
    }

    /// <summary>Primer dia habil a partir de la fecha (inclusive) o, con dias &gt; 0, sumando esa cantidad de dias habiles.</summary>
    public async Task<ProximoHabilResponse> ProximoHabilAsync(long idCompania, long idPais, DateOnly desde, int dias)
    {
        var reglas = await CargarReglasAsync(idCompania, idPais);
        var d = desde; var avanzados = 0; var contados = 0;
        dias = Math.Clamp(dias, 0, 365);
        if (dias == 0) { while (Motivo(reglas, d) is not null && avanzados < 400) { d = d.AddDays(1); avanzados++; } }
        else
        {
            while (contados < dias && avanzados < 2000)
            {
                d = d.AddDays(1); avanzados++;
                if (Motivo(reglas, d) is null) contados++;
            }
        }
        return new ProximoHabilResponse(desde, d, avanzados);
    }
}
