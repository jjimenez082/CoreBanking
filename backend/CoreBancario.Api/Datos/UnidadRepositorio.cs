using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Estructura organizativa: arbol de unidades (sec.DEPARTAMENTO) con tipo (lista TIPO_UNIDAD_ORG) y responsable.
/// Sirve para ubicar a los usuarios y para asociar clientes (y mas adelante cuentas y creditos) a su sucursal/unidad.
/// </summary>
public class UnidadRepositorio
{
    public const string ObjetoAdministracion = "ORG_ADMINISTRACION";
    public const string ListaTipoUnidad = "TIPO_UNIDAD_ORG";
    public const string Entidad = "UNIDAD_ORG";

    private readonly TenantContext _tenant;
    public UnidadRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private sealed record Fila(
        long id_departamento, string codigo, string nombre, long id_tipo_unidad, string tipo_nombre, long? id_departamento_padre,
        long? id_responsable, string? responsable_nombre, string estado, bool permite_registros, int usuarios);

    private static async Task<List<Fila>> LeerAsync(SqlConnection c, SqlTransaction? tx, long idCompania)
    {
        var filas = await c.QueryAsync<Fila>(
            """
            SELECT d.id_departamento, d.codigo, d.nombre, d.id_tipo_unidad, t.nombre AS tipo_nombre, d.id_departamento_padre,
                   d.id_responsable, r.nombre_completo AS responsable_nombre, d.estado, d.permite_registros,
                   (SELECT COUNT(*) FROM sec.USUARIO u WHERE u.id_departamento = d.id_departamento AND u.estado = 'Activo') AS usuarios
            FROM sec.DEPARTAMENTO d
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = d.id_tipo_unidad
            LEFT JOIN sec.USUARIO r ON r.id_usuario = d.id_responsable
            WHERE d.id_compania = @idCompania
            """, new { idCompania }, tx);
        return filas.ToList();
    }

    /// <summary>Filas en orden de arbol (cada padre seguido de sus hijos, por nombre), con ruta y profundidad.</summary>
    private static List<(Fila F, string Ruta, int Prof)> EnArbol(List<Fila> filas)
    {
        var porPadre = filas.ToLookup(f => f.id_departamento_padre ?? 0);
        var res = new List<(Fila, string, int)>();
        void Recorrer(long padre, string rutaPadre, int prof)
        {
            foreach (var f in porPadre[padre].OrderBy(x => x.nombre, StringComparer.CurrentCultureIgnoreCase))
            {
                var ruta = rutaPadre.Length == 0 ? f.nombre : $"{rutaPadre} › {f.nombre}";
                res.Add((f, ruta, prof));
                Recorrer(f.id_departamento, ruta, prof + 1);
            }
        }
        Recorrer(0, "", 0);
        // Huérfanas (no deberia ocurrir): se agregan al final.
        foreach (var f in filas.Where(f => res.All(r => r.Item1.id_departamento != f.id_departamento)))
            res.Add((f, f.nombre, 0));
        return res;
    }

    public async Task<IReadOnlyList<UnidadResponse>> ListarAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await LeerAsync(c, null, idCompania);
        var nombres = filas.ToDictionary(f => f.id_departamento, f => f.nombre);
        return EnArbol(filas).Select(x => new UnidadResponse(
            x.F.id_departamento, x.F.codigo, x.F.nombre, x.F.id_tipo_unidad, x.F.tipo_nombre,
            x.F.id_departamento_padre, x.F.id_departamento_padre is { } p ? nombres.GetValueOrDefault(p) : null,
            x.F.id_responsable, x.F.responsable_nombre, x.F.estado,
            filas.Count(h => h.id_departamento_padre == x.F.id_departamento), x.F.usuarios, x.Ruta, x.Prof, x.F.permite_registros)).ToList();
    }

    /// <summary>
    /// Unidades activas para combos. Con soloRegistros (alta/modificacion de clientes) solo las que tienen el parametro
    /// "permite registros". La predeterminada es la unidad del usuario o, si esa no admite registros, la superior mas cercana que si.
    /// </summary>
    public async Task<IReadOnlyList<UnidadItemResponse>> ListarActivasAsync(ActorContext actor, bool soloRegistros = false)
    {
        using var c = NuevaConexion();
        var filas = await LeerAsync(c, null, actor.IdCompania);
        var porId = filas.ToDictionary(f => f.id_departamento);
        var miUnidad = actor.EsIntegracion ? null
            : await c.ExecuteScalarAsync<long?>("SELECT id_departamento FROM sec.USUARIO WHERE id_usuario = @u", new { u = actor.IdUsuario });

        long? predeterminada = null;
        for (var cur = miUnidad; cur is not null && porId.TryGetValue(cur.Value, out var f); cur = f.id_departamento_padre)
            if (f.estado == "Activo" && (!soloRegistros || f.permite_registros)) { predeterminada = f.id_departamento; break; }

        return EnArbol(filas).Where(x => x.F.estado == "Activo" && (!soloRegistros || x.F.permite_registros))
            .Select(x => new UnidadItemResponse(x.F.id_departamento, x.F.codigo, x.F.nombre, x.Ruta, x.F.tipo_nombre, x.F.id_departamento == predeterminada))
            .ToList();
    }

    public async Task<IReadOnlyList<UsuarioItemResponse>> ListarUsuariosActivosAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync<UsuarioItemResponse>(
            "SELECT id_usuario AS Id, nombre_completo AS Nombre, username AS Username FROM sec.USUARIO WHERE id_compania = @idCompania AND estado = 'Activo' AND tipo_usuario = 'FISICO' ORDER BY nombre_completo",
            new { idCompania });
        return filas.ToList();
    }

    private static string? Validar(UnidadGuardarRequest req)
    {
        var codigo = req.Codigo?.Trim() ?? "";
        var nombre = req.Nombre?.Trim() ?? "";
        if (codigo.Length is 0 or > 20) return "El codigo es obligatorio (maximo 20 caracteres).";
        if (nombre.Length is 0 or > 200) return "El nombre es obligatorio (maximo 200 caracteres).";
        return null;
    }

    private static async Task<ResultadoOperacion?> ValidarReferenciasAsync(
        SqlConnection c, SqlTransaction tx, long idCompania, long? idEditando, UnidadGuardarRequest req, List<Fila> filas)
    {
        var tipoOk = await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i
                JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE t.codigo = @lista AND i.id_lista_valores_item = @id AND i.estado = 'Activo') THEN 1 ELSE 0 END
            """, new { lista = ListaTipoUnidad, id = req.IdTipoUnidad }, tx);
        if (!tipoOk) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El tipo de unidad no existe o esta inactivo.");

        if (req.IdPadre is { } padre)
        {
            var p = filas.FirstOrDefault(f => f.id_departamento == padre);
            if (p is null) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La unidad superior no existe.");
            if (p.estado != "Activo") return ResultadoOperacion.Fallo(400, "datos_invalidos", "La unidad superior esta inactiva.");
            if (idEditando is { } yo)
            {
                // No puede colgar de si misma ni de un descendiente.
                for (long? cur = padre; cur is not null; cur = filas.FirstOrDefault(f => f.id_departamento == cur)?.id_departamento_padre)
                    if (cur == yo) return ResultadoOperacion.Fallo(400, "ciclo", "Una unidad no puede depender de si misma ni de una de sus subunidades.");
            }
        }

        if (req.IdResponsable is { } resp)
        {
            var ok = await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.USUARIO WHERE id_usuario = @u AND id_compania = @c AND estado = 'Activo' AND tipo_usuario = 'FISICO') THEN 1 ELSE 0 END",
                new { u = resp, c = idCompania }, tx);
            if (!ok) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El responsable no existe o esta inactivo.");
        }
        return null;
    }

    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, UnidadGuardarRequest req)
    {
        var err = Validar(req);
        if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
        var codigo = req.Codigo.Trim();

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var filas = await LeerAsync(c, tx, actor.IdCompania);
        if (filas.Any(f => string.Equals(f.codigo, codigo, StringComparison.OrdinalIgnoreCase)))
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", $"Ya existe una unidad con el codigo {codigo}.");
        var bad = await ValidarReferenciasAsync(c, tx, actor.IdCompania, null, req, filas);
        if (bad is not null) return bad;

        var id = await c.QuerySingleAsync<long>(
            """
            INSERT INTO sec.DEPARTAMENTO (id_compania, id_departamento_padre, codigo, nombre, id_tipo_unidad, id_responsable, permite_registros, creado_por)
            OUTPUT INSERTED.id_departamento
            VALUES (@idCompania, @padre, @codigo, @nombre, @tipo, @resp, @permite, @por)
            """, new { actor.IdCompania, padre = req.IdPadre, codigo, nombre = req.Nombre.Trim(), tipo = req.IdTipoUnidad, resp = req.IdResponsable, permite = req.PermiteRegistros, por = actor.NombreDescriptivo }, tx);
        await Auditoria.AltaAsync(c, tx, actor, Entidad, id, $"{codigo} {req.Nombre.Trim()}");
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long idUnidad, UnidadGuardarRequest req)
    {
        var err = Validar(req);
        if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
        var codigo = req.Codigo.Trim();
        var nombre = req.Nombre.Trim();

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var filas = await LeerAsync(c, tx, actor.IdCompania);
        var actual = filas.FirstOrDefault(f => f.id_departamento == idUnidad);
        if (actual is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "La unidad no existe.");
        if (filas.Any(f => f.id_departamento != idUnidad && string.Equals(f.codigo, codigo, StringComparison.OrdinalIgnoreCase)))
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", $"Ya existe otra unidad con el codigo {codigo}.");

        // Los valores que no cambian no se revalidan (una unidad puede conservar un tipo o padre luego inactivado).
        var req2 = req with
        {
            IdTipoUnidad = req.IdTipoUnidad,
        };
        if (req.IdTipoUnidad != actual.id_tipo_unidad || req.IdPadre != actual.id_departamento_padre || req.IdResponsable != actual.id_responsable)
        {
            var bad = await ValidarReferenciasAsync(c, tx, actor.IdCompania, idUnidad,
                req2 with { IdTipoUnidad = req.IdTipoUnidad }, filas);
            if (bad is not null) return bad;
        }

        await c.ExecuteAsync(
            """
            UPDATE sec.DEPARTAMENTO SET codigo = @codigo, nombre = @nombre, id_tipo_unidad = @tipo, id_departamento_padre = @padre,
                   id_responsable = @resp, permite_registros = @permite, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            WHERE id_departamento = @id AND id_compania = @idCompania
            """, new { codigo, nombre, tipo = req.IdTipoUnidad, padre = req.IdPadre, resp = req.IdResponsable, permite = req.PermiteRegistros, por = actor.NombreDescriptivo, id = idUnidad, actor.IdCompania }, tx);

        if (actual.codigo != codigo) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUnidad, "codigo", actual.codigo, codigo);
        if (actual.nombre != nombre) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUnidad, "nombre", actual.nombre, nombre);
        if (actual.id_tipo_unidad != req.IdTipoUnidad) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUnidad, "id_tipo_unidad", actual.id_tipo_unidad.ToString(), req.IdTipoUnidad.ToString());
        if (actual.id_departamento_padre != req.IdPadre) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUnidad, "id_departamento_padre", actual.id_departamento_padre?.ToString(), req.IdPadre?.ToString());
        if (actual.id_responsable != req.IdResponsable) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUnidad, "id_responsable", actual.id_responsable?.ToString(), req.IdResponsable?.ToString());
        if (actual.permite_registros != req.PermiteRegistros) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUnidad, "permite_registros", actual.permite_registros ? "Si" : "No", req.PermiteRegistros ? "Si" : "No");
        tx.Commit();
        return ResultadoOperacion.Ok(idUnidad);
    }

    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long idUnidad, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var filas = await LeerAsync(c, tx, actor.IdCompania);
        var u = filas.FirstOrDefault(f => f.id_departamento == idUnidad);
        if (u is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "La unidad no existe.");
        if (u.estado == estado) return ResultadoOperacion.Ok(idUnidad);

        if (estado == "Inactivo")
        {
            var hijas = filas.Count(f => f.id_departamento_padre == idUnidad && f.estado == "Activo");
            if (hijas > 0) return ResultadoOperacion.Fallo(409, "tiene_subunidades", $"La unidad tiene {hijas} subunidad(es) activa(s); inactivelas o muevalas primero.");
            if (u.usuarios > 0) return ResultadoOperacion.Fallo(409, "tiene_usuarios", $"La unidad tiene {u.usuarios} usuario(s) activo(s); reasignelos primero.");
        }
        else if (u.id_departamento_padre is { } p && filas.FirstOrDefault(f => f.id_departamento == p)?.estado != "Activo")
            return ResultadoOperacion.Fallo(409, "padre_inactivo", "La unidad superior esta inactiva; active primero la superior.");

        await c.ExecuteAsync(
            "UPDATE sec.DEPARTAMENTO SET estado = @estado, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_departamento = @id",
            new { estado, por = actor.NombreDescriptivo, id = idUnidad }, tx);
        await Auditoria.CambioAsync(c, tx, actor, Entidad, idUnidad, "estado", u.estado, estado);
        tx.Commit();
        return ResultadoOperacion.Ok(idUnidad);
    }
}
