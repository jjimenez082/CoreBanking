using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Catalogo de monedas (gen.MONEDA) y moneda base de la compania. Todo cambio se audita.</summary>
public class MonedaRepositorio
{
    public const string ObjetoAdministracion = "MON_ADMINISTRACION";
    public const string Entidad = "MONEDA";

    private readonly TenantContext _tenant;
    public MonedaRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private static MonedaResponse Mapear(dynamic r) => new(
        (long)r.id_moneda, (string)r.codigo, (string)r.nombre, (string)r.simbolo, (int)(byte)r.decimales,
        (bool)r.es_base, (string)r.estado, (int)r.cantidad_tc);

    public async Task<IReadOnlyList<MonedaResponse>> ListarAsync(long idCompania, bool soloActivas)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT m.id_moneda, m.codigo, m.nombre, m.simbolo, m.decimales, m.es_base, m.estado,
                   (SELECT COUNT(*) FROM gen.TIPO_CAMBIO t WHERE t.id_moneda = m.id_moneda) AS cantidad_tc
            FROM gen.MONEDA m
            WHERE m.id_compania = @idCompania AND (@soloActivas = 0 OR m.estado = 'Activo')
            ORDER BY m.es_base DESC, m.codigo
            """, new { idCompania, soloActivas = soloActivas ? 1 : 0 });
        return filas.Select(f => (MonedaResponse)Mapear(f)).ToList();
    }

    private static string? Validar(MonedaGuardarRequest req, bool validarCodigo)
    {
        if (validarCodigo && !Regex.IsMatch((req.Codigo ?? "").Trim().ToUpperInvariant(), "^[A-Z]{3}$"))
            return "El codigo debe tener 3 letras (ISO 4217, por ejemplo CRC, USD, EUR).";
        if (string.IsNullOrWhiteSpace(req.Nombre) || req.Nombre.Trim().Length > 100) return "El nombre es obligatorio (maximo 100 caracteres).";
        if (string.IsNullOrWhiteSpace(req.Simbolo) || req.Simbolo.Trim().Length > 5) return "El simbolo es obligatorio (maximo 5 caracteres).";
        if (req.Decimales is < 0 or > 6) return "Los decimales deben estar entre 0 y 6.";
        return null;
    }

    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, MonedaGuardarRequest req)
    {
        var error = Validar(req, true);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        var codigo = req.Codigo.Trim().ToUpperInvariant();

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            // La primera moneda de la compania queda como base.
            var hayBase = await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.MONEDA WHERE id_compania = @c AND es_base = 1) THEN 1 ELSE 0 END", new { c = actor.IdCompania }, tx);
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO gen.MONEDA (id_compania, codigo, nombre, simbolo, decimales, es_base, creado_por)
                OUTPUT INSERTED.id_moneda
                VALUES (@c, @codigo, @nombre, @simbolo, @dec, @esBase, @por)
                """, new { c = actor.IdCompania, codigo, nombre = req.Nombre.Trim(), simbolo = req.Simbolo.Trim(), dec = req.Decimales, esBase = !hayBase, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, Entidad, id, $"{codigo} · {req.Nombre.Trim()}{(hayBase ? "" : " (moneda base)")}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "moneda_duplicada", $"Ya existe la moneda {codigo}.");
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long idMoneda, MonedaGuardarRequest req)
    {
        var error = Validar(req, false);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var m = await c.QuerySingleOrDefaultAsync(
                "SELECT nombre, simbolo, decimales FROM gen.MONEDA WITH (UPDLOCK) WHERE id_moneda = @id AND id_compania = @c",
                new { id = idMoneda, c = actor.IdCompania }, tx);
            if (m is null) return ResultadoOperacion.Fallo(404, "no_encontrada", "No existe la moneda.");

            var nombre = req.Nombre.Trim(); var simbolo = req.Simbolo.Trim();
            await c.ExecuteAsync(
                "UPDATE gen.MONEDA SET nombre = @nombre, simbolo = @simbolo, decimales = @dec, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_moneda = @id",
                new { nombre, simbolo, dec = req.Decimales, por = actor.NombreDescriptivo, id = idMoneda }, tx);
            if ((string)m.nombre != nombre) await Auditoria.CambioAsync(c, tx, actor, Entidad, idMoneda, "nombre", (string)m.nombre, nombre);
            if ((string)m.simbolo != simbolo) await Auditoria.CambioAsync(c, tx, actor, Entidad, idMoneda, "simbolo", (string)m.simbolo, simbolo);
            if ((int)(byte)m.decimales != req.Decimales) await Auditoria.CambioAsync(c, tx, actor, Entidad, idMoneda, "decimales", ((int)(byte)m.decimales).ToString(), req.Decimales.ToString());
            tx.Commit();
            return ResultadoOperacion.Ok(idMoneda);
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long idMoneda, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado invalido.");
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var m = await c.QuerySingleOrDefaultAsync(
                "SELECT estado, es_base, codigo FROM gen.MONEDA WITH (UPDLOCK) WHERE id_moneda = @id AND id_compania = @c", new { id = idMoneda, c = actor.IdCompania }, tx);
            if (m is null) return ResultadoOperacion.Fallo(404, "no_encontrada", "No existe la moneda.");
            if ((string)m.estado == estado) return ResultadoOperacion.Ok(idMoneda);
            if (estado == "Inactivo" && (bool)m.es_base)
                return ResultadoOperacion.Fallo(409, "moneda_base", "La moneda base no se puede inactivar; defina primero otra moneda base.");
            if (estado == "Inactivo")
            {
                var pend = await c.ExecuteScalarAsync<bool>(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION WHERE entidad_negocio = 'TIPO_CAMBIO' AND estado = 'Pendiente de Aprobacion' AND llave_registro LIKE @p) THEN 1 ELSE 0 END",
                    new { p = $"{idMoneda}|%" }, tx);
                if (pend) return ResultadoOperacion.Fallo(409, "gestion_pendiente", "La moneda tiene cargas de tipo de cambio pendientes de aprobacion.");
            }
            await c.ExecuteAsync("UPDATE gen.MONEDA SET estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_moneda = @id",
                new { e = estado, por = actor.NombreDescriptivo, id = idMoneda }, tx);
            await Auditoria.CambioAsync(c, tx, actor, Entidad, idMoneda, "estado", (string)m.estado, estado);
            tx.Commit();
            return ResultadoOperacion.Ok(idMoneda);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Define la moneda base. Solo mientras no existan tipos de cambio cargados (cambiarla los dejaria sin sentido).</summary>
    public async Task<ResultadoOperacion> DefinirBaseAsync(ActorContext actor, long idMoneda)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var m = await c.QuerySingleOrDefaultAsync(
                "SELECT estado, es_base, codigo FROM gen.MONEDA WITH (UPDLOCK) WHERE id_moneda = @id AND id_compania = @c", new { id = idMoneda, c = actor.IdCompania }, tx);
            if (m is null) return ResultadoOperacion.Fallo(404, "no_encontrada", "No existe la moneda.");
            if ((bool)m.es_base) return ResultadoOperacion.Ok(idMoneda);
            if ((string)m.estado != "Activo") return ResultadoOperacion.Fallo(409, "moneda_inactiva", "La moneda base debe estar activa.");

            var hayTc = await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.TIPO_CAMBIO WHERE id_compania = @c) THEN 1 ELSE 0 END", new { c = actor.IdCompania }, tx);
            if (hayTc)
                return ResultadoOperacion.Fallo(409, "hay_tipos_cambio", "Ya hay tipos de cambio cargados contra la moneda base actual; no se puede cambiar la moneda base.");

            var anterior = await c.QuerySingleOrDefaultAsync("SELECT id_moneda, codigo FROM gen.MONEDA WHERE id_compania = @c AND es_base = 1", new { c = actor.IdCompania }, tx);
            await c.ExecuteAsync("UPDATE gen.MONEDA SET es_base = 0, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_compania = @c AND es_base = 1",
                new { c = actor.IdCompania, por = actor.NombreDescriptivo }, tx);
            await c.ExecuteAsync("UPDATE gen.MONEDA SET es_base = 1, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_moneda = @id",
                new { id = idMoneda, por = actor.NombreDescriptivo }, tx);
            if (anterior is not null) await Auditoria.CambioAsync(c, tx, actor, Entidad, (long)anterior.id_moneda, "es_base", "1", "0");
            await Auditoria.CambioAsync(c, tx, actor, Entidad, idMoneda, "es_base", "0", "1");
            tx.Commit();
            return ResultadoOperacion.Ok(idMoneda);
        }
        catch { tx.Rollback(); throw; }
    }
}
