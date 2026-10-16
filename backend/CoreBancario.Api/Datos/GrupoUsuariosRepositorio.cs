using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Grupos de distribucion (sec.GRUPO_USUARIOS). Son listas de usuarios independientes de la estructura organizativa.</summary>
public class GrupoUsuariosRepositorio
{
    public const string Objeto = "GES_GRUPOS";
    private readonly TenantContext _tenant;
    public GrupoUsuariosRepositorio(TenantContext tenant) => _tenant = tenant;
    private SqlConnection Nueva() => new(_tenant.ConnectionString);

    public async Task<IReadOnlyList<GrupoAdminResponse>> ListarAsync(long idCompania)
    {
        using var c = Nueva();
        var f = await c.QueryAsync(
            """
            SELECT g.id_grupo, g.codigo, g.nombre, g.descripcion, g.estado,
                   (SELECT COUNT(*) FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = g.id_grupo) AS miembros,
                   (SELECT COUNT(DISTINCT n.id_regla) FROM apr.REGLA_APROBACION_NIVEL n JOIN apr.REGLA_APROBACION r ON r.id_regla = n.id_regla
                     WHERE n.id_grupo = g.id_grupo AND r.estado = 'Activo') AS reglas
            FROM sec.GRUPO_USUARIOS g WHERE g.id_compania = @c ORDER BY g.nombre
            """, new { c = idCompania });
        return f.Select(x => new GrupoAdminResponse((long)x.id_grupo, (string)x.codigo, (string)x.nombre, (string?)x.descripcion, (string)x.estado, (int)x.miembros, (int)x.reglas)).ToList();
    }

    public async Task<IReadOnlyList<GrupoCatalogoResponse>> CatalogoActivosAsync(long idCompania)
    {
        using var c = Nueva();
        return (await c.QueryAsync<GrupoCatalogoResponse>(
            """
            SELECT g.id_grupo AS IdGrupo, g.codigo AS Codigo, g.nombre AS Nombre FROM sec.GRUPO_USUARIOS g
            WHERE g.id_compania = @c AND g.estado = 'Activo' AND EXISTS (SELECT 1 FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = g.id_grupo)
            ORDER BY g.nombre
            """, new { c = idCompania })).ToList();
    }

    private static ResultadoOperacion? Validar(GrupoGuardarRequest r, out string codigo, out string nombre, out string? desc)
    {
        codigo = (r.Codigo ?? "").Trim().ToUpperInvariant(); nombre = (r.Nombre ?? "").Trim(); desc = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim();
        if (codigo.Length is < 2 or > 50 || !codigo.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            return ResultadoOperacion.Fallo(400, "codigo_invalido", "El código debe tener entre 2 y 50 caracteres (letras, números o guion bajo).");
        if (nombre.Length is < 2 or > 200) return ResultadoOperacion.Fallo(400, "nombre_invalido", "El nombre es obligatorio (máximo 200 caracteres).");
        if (desc is { Length: > 500 }) return ResultadoOperacion.Fallo(400, "descripcion_invalida", "La descripción admite máximo 500 caracteres.");
        return null;
    }

    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, GrupoGuardarRequest req)
    {
        var err = Validar(req, out var codigo, out var nombre, out var desc); if (err is not null) return err;
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sec.GRUPO_USUARIOS WHERE id_compania = @c AND codigo = @k", new { c = actor.IdCompania, k = codigo }, tx) > 0)
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un grupo con ese código.");
        var id = await c.ExecuteScalarAsync<long>(
            "INSERT INTO sec.GRUPO_USUARIOS (id_compania, codigo, nombre, descripcion, creado_por) OUTPUT INSERTED.id_grupo VALUES (@c, @k, @n, @d, @u)",
            new { c = actor.IdCompania, k = codigo, n = nombre, d = desc, u = actor.NombreDescriptivo }, tx);
        await Auditoria.AltaAsync(c, tx, actor, "GRUPO_USUARIOS", id, $"{codigo} - {nombre}");
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long id, GrupoGuardarRequest req)
    {
        var err = Validar(req, out var codigo, out var nombre, out var desc); if (err is not null) return err;
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync("SELECT codigo, nombre, descripcion FROM sec.GRUPO_USUARIOS WHERE id_grupo = @i AND id_compania = @c", new { i = id, c = actor.IdCompania }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El grupo no existe.");
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sec.GRUPO_USUARIOS WHERE id_compania = @c AND codigo = @k AND id_grupo <> @i", new { c = actor.IdCompania, k = codigo, i = id }, tx) > 0)
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe otro grupo con ese código.");
        await c.ExecuteAsync("UPDATE sec.GRUPO_USUARIOS SET codigo=@k, nombre=@n, descripcion=@d, modificado_por=@u, modificado_en=SYSUTCDATETIME() WHERE id_grupo=@i",
            new { k = codigo, n = nombre, d = desc, u = actor.NombreDescriptivo, i = id }, tx);
        if ((string)a.codigo != codigo) await Auditoria.CambioAsync(c, tx, actor, "GRUPO_USUARIOS", id, "codigo", (string)a.codigo, codigo);
        if ((string)a.nombre != nombre) await Auditoria.CambioAsync(c, tx, actor, "GRUPO_USUARIOS", id, "nombre", (string)a.nombre, nombre);
        if ((string?)a.descripcion != desc) await Auditoria.CambioAsync(c, tx, actor, "GRUPO_USUARIOS", id, "descripcion", (string?)a.descripcion, desc);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado inválido.");
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync("SELECT estado FROM sec.GRUPO_USUARIOS WHERE id_grupo = @i AND id_compania = @c", new { i = id, c = actor.IdCompania }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El grupo no existe.");
        if ((string)a.estado == estado) return ResultadoOperacion.Ok(id);
        if (estado == "Inactivo")
        {
            var reglas = (await c.QueryAsync<string>(
                """
                SELECT DISTINCT r.nombre FROM apr.REGLA_APROBACION_NIVEL n JOIN apr.REGLA_APROBACION r ON r.id_regla = n.id_regla
                WHERE n.id_grupo = @i AND r.estado = 'Activo'
                """, new { i = id }, tx)).ToList();
            if (reglas.Count > 0)
                return ResultadoOperacion.Fallo(409, "grupo_en_uso", $"El grupo lo usan reglas activas: {string.Join(", ", reglas)}. Inactive o cambie esas reglas primero.");
        }
        await c.ExecuteAsync("UPDATE sec.GRUPO_USUARIOS SET estado=@e, modificado_por=@u, modificado_en=SYSUTCDATETIME() WHERE id_grupo=@i", new { e = estado, u = actor.NombreDescriptivo, i = id }, tx);
        await Auditoria.CambioAsync(c, tx, actor, "GRUPO_USUARIOS", id, "estado", (string)a.estado, estado);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<IReadOnlyList<GrupoMiembroResponse>> MiembrosAsync(long idCompania, long id)
    {
        using var c = Nueva();
        return (await c.QueryAsync<GrupoMiembroResponse>(
            """
            SELECT u.id_usuario AS IdUsuario, u.nombre_completo AS Nombre, u.username AS Username, d.nombre AS Unidad
            FROM sec.GRUPO_USUARIOS_MIEMBRO m JOIN sec.USUARIO u ON u.id_usuario = m.id_usuario
            JOIN sec.GRUPO_USUARIOS g ON g.id_grupo = m.id_grupo
            LEFT JOIN sec.DEPARTAMENTO d ON d.id_departamento = u.id_departamento
            WHERE m.id_grupo = @i AND g.id_compania = @c ORDER BY u.nombre_completo
            """, new { i = id, c = idCompania })).ToList();
    }

    public async Task<ResultadoOperacion> GuardarMiembrosAsync(ActorContext actor, long id, IReadOnlyList<long> ids)
    {
        var nuevos = (ids ?? []).Distinct().ToList();
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var g = await c.QuerySingleOrDefaultAsync("SELECT estado FROM sec.GRUPO_USUARIOS WHERE id_grupo = @i AND id_compania = @c", new { i = id, c = actor.IdCompania }, tx);
        if (g is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El grupo no existe.");
        if (nuevos.Count > 0)
        {
            var validos = (await c.QueryAsync<long>("SELECT id_usuario FROM sec.USUARIO WHERE id_compania = @c AND estado = 'Activo' AND tipo_usuario = 'FISICO' AND id_usuario IN @ids", new { c = actor.IdCompania, ids = nuevos }, tx)).ToHashSet();
            if (nuevos.Any(x => !validos.Contains(x))) return ResultadoOperacion.Fallo(400, "usuario_invalido", "Hay usuarios inexistentes o inactivos en la lista.");
        }
        var actuales = (await c.QueryAsync<long>("SELECT id_usuario FROM sec.GRUPO_USUARIOS_MIEMBRO WHERE id_grupo = @i", new { i = id }, tx)).ToHashSet();
        if (nuevos.Count == 0 && actuales.Count > 0)
        {
            var enUso = await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM apr.REGLA_APROBACION_NIVEL n JOIN apr.REGLA_APROBACION r ON r.id_regla = n.id_regla WHERE n.id_grupo = @i AND r.estado = 'Activo'", new { i = id }, tx);
            if (enUso > 0) return ResultadoOperacion.Fallo(409, "grupo_en_uso", "El grupo lo usan reglas activas; no puede quedar sin miembros.");
        }
        var agregar = nuevos.Where(x => !actuales.Contains(x)).ToList();
        var quitar = actuales.Where(x => !nuevos.Contains(x)).ToList();
        foreach (var u in agregar)
            await c.ExecuteAsync("INSERT INTO sec.GRUPO_USUARIOS_MIEMBRO (id_grupo, id_usuario, creado_por) VALUES (@g, @u, @p)", new { g = id, u, p = actor.NombreDescriptivo }, tx);
        if (quitar.Count > 0) await c.ExecuteAsync("DELETE FROM sec.GRUPO_USUARIOS_MIEMBRO WHERE id_grupo = @g AND id_usuario IN @q", new { g = id, q = quitar }, tx);
        var nombres = (await c.QueryAsync("SELECT id_usuario, nombre_completo FROM sec.USUARIO WHERE id_usuario IN @x", new { x = agregar.Concat(quitar).DefaultIfEmpty(0).ToList() }, tx))
            .ToDictionary(x => (long)x.id_usuario, x => (string)x.nombre_completo);
        if (agregar.Count > 0) await Auditoria.CambioAsync(c, tx, actor, "GRUPO_USUARIOS", id, "miembros", null, "Agregó: " + string.Join(", ", agregar.Select(a => nombres.GetValueOrDefault(a, a.ToString()))));
        if (quitar.Count > 0) await Auditoria.CambioAsync(c, tx, actor, "GRUPO_USUARIOS", id, "miembros", "Quitó: " + string.Join(", ", quitar.Select(a => nombres.GetValueOrDefault(a, a.ToString()))), null);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }
}
