using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Mantenimiento del modulo de Seguridad (RBAC, SEG-002) desde la UI, en vez
/// de sembrar/editar roles y asignaciones directamente por SQL: catalogo de
/// objetos de autorizacion (lectura), CRUD simple de roles, asignacion de
/// objetos a un rol, y asignacion de roles a un usuario.
/// </summary>
public class SeguridadRepositorio
{
    private readonly TenantContext _tenantContext;

    public SeguridadRepositorio(TenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    private SqlConnection NuevaConexion() => new(_tenantContext.ConnectionString);

    public async Task<IReadOnlyList<ObjetoAutorizacionResponse>> ListarObjetosAutorizacionAsync()
    {
        using var conexion = NuevaConexion();

        const string sql = """
            SELECT
                id_objeto_autorizacion AS IdObjetoAutorizacion,
                codigo                 AS Codigo,
                nombre                 AS Nombre,
                tipo_objeto            AS TipoObjeto,
                descripcion            AS Descripcion
            FROM sec.OBJETO_AUTORIZACION
            WHERE estado = 'Activo'
            ORDER BY codigo
            """;

        var filas = await conexion.QueryAsync<ObjetoAutorizacionResponse>(sql);
        return filas.ToList();
    }

    /// <summary>
    /// Roles visibles para la compania actual: los de plantilla
    /// (id_compania IS NULL, reutilizables entre companias) mas los propios
    /// de esta compania.
    /// </summary>
    public async Task<IReadOnlyList<RolResponse>> ListarRolesAsync(long idCompania)
    {
        using var conexion = NuevaConexion();

        const string sqlRoles = """
            SELECT id_rol_autorizacion, codigo, nombre, estado
            FROM sec.ROL_AUTORIZACION
            WHERE id_compania IS NULL OR id_compania = @idCompania
            ORDER BY codigo
            """;
        var roles = (await conexion.QueryAsync(sqlRoles, new { idCompania })).ToList();

        const string sqlObjetos = """
            SELECT rao.id_rol_autorizacion, oa.codigo
            FROM sec.ROL_AUTORIZACION_OBJETO rao
            JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
            """;
        var asignaciones = (await conexion.QueryAsync(sqlObjetos)).ToList();

        return roles.Select(r => new RolResponse(
            (long)r.id_rol_autorizacion,
            (string)r.codigo,
            (string)r.nombre,
            (string)r.estado,
            asignaciones
                .Where(a => (long)a.id_rol_autorizacion == (long)r.id_rol_autorizacion)
                .Select(a => (string)a.codigo)
                .ToList()
        )).ToList();
    }

    public const string EntidadRol = "ROL";

    public async Task<long> CrearRolAsync(ActorContext actor, string codigo, string nombre)
    {
        var idCompania = actor.IdCompania; var creadoPor = actor.NombreDescriptivo;
        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();

        const string sql = """
            INSERT INTO sec.ROL_AUTORIZACION (id_compania, codigo, nombre, creado_por)
            OUTPUT INSERTED.id_rol_autorizacion
            VALUES (@idCompania, @codigo, @nombre, @creadoPor)
            """;

        var id = await conexion.QuerySingleAsync<long>(sql, new { idCompania, codigo, nombre, creadoPor }, tx);
        await Auditoria.AltaAsync(conexion, tx, actor, EntidadRol, id, $"{codigo} · {nombre}");
        tx.Commit();
        return id;
    }

    /// <summary>Código derivado del nombre: mayúsculas, sin tildes, solo letras/números separados por «_» (máx. 100).</summary>
    public static string CodigoDesdeNombre(string nombre)
    {
        var d = nombre.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_');
        }
        var c = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        if (c.Length == 0) c = "ROL";
        return c.Length > 90 ? c[..90].TrimEnd('_') : c;
    }

    /// <summary>
    /// Crea un rol nuevo copiando los permisos de otro. Solo se indica el nombre; el código se deriva (y se numera si ya existe).
    /// No copia los usuarios asignados. Devuelve null si el rol origen no existe; Error si el nombre ya está en uso.
    /// </summary>
    public async Task<(long? IdNuevo, string? Error)> CopiarRolAsync(ActorContext actor, long idOrigen, string nombre)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) return (null, "Indique el nombre del nuevo rol.");
        if (nombre.Length > 200) return (null, "El nombre no puede pasar de 200 caracteres.");

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();

        var origen = await c.QuerySingleOrDefaultAsync(
            "SELECT id_rol_autorizacion, codigo, nombre FROM sec.ROL_AUTORIZACION WHERE id_rol_autorizacion = @idOrigen AND (id_compania IS NULL OR id_compania = @idCompania)",
            new { idOrigen, actor.IdCompania }, tx);
        if (origen is null) return (null, null);

        if (await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sec.ROL_AUTORIZACION WHERE (id_compania IS NULL OR id_compania = @idCompania) AND nombre = @nombre",
                new { actor.IdCompania, nombre }, tx) > 0)
            return (null, $"Ya existe un rol con el nombre «{nombre}».");

        var baseCodigo = CodigoDesdeNombre(nombre);
        var codigo = baseCodigo;
        for (var n = 2; await c.ExecuteScalarAsync<int>(
                 "SELECT COUNT(*) FROM sec.ROL_AUTORIZACION WHERE (id_compania IS NULL OR id_compania = @idCompania) AND codigo = @codigo",
                 new { actor.IdCompania, codigo }, tx) > 0; n++)
            codigo = $"{baseCodigo}_{n}";

        var id = await c.QuerySingleAsync<long>(
            """
            INSERT INTO sec.ROL_AUTORIZACION (id_compania, codigo, nombre, creado_por)
            OUTPUT INSERTED.id_rol_autorizacion VALUES (@IdCompania, @codigo, @nombre, @por)
            """, new { actor.IdCompania, codigo, nombre, por = actor.NombreDescriptivo }, tx);

        await c.ExecuteAsync(
            """
            INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
            SELECT @id, id_objeto_autorizacion FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @idOrigen
            """, new { id, idOrigen }, tx);
        var copiados = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @id", new { id }, tx);

        await Auditoria.AltaAsync(c, tx, actor, EntidadRol, id, $"{codigo} · {nombre} (copia de {(string)origen.codigo}, {copiados} permisos)");
        tx.Commit();
        return (id, null);
    }

    public async Task<RolResponse?> ObtenerRolAsync(long idRolAutorizacion)
    {
        using var conexion = NuevaConexion();

        const string sqlRol = """
            SELECT id_rol_autorizacion, codigo, nombre, estado
            FROM sec.ROL_AUTORIZACION
            WHERE id_rol_autorizacion = @idRolAutorizacion
            """;
        var rol = await conexion.QuerySingleOrDefaultAsync(sqlRol, new { idRolAutorizacion });
        if (rol is null) return null;

        const string sqlObjetos = """
            SELECT oa.codigo
            FROM sec.ROL_AUTORIZACION_OBJETO rao
            JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
            WHERE rao.id_rol_autorizacion = @idRolAutorizacion
            """;
        var codigos = (await conexion.QueryAsync<string>(sqlObjetos, new { idRolAutorizacion })).ToList();

        return new RolResponse(
            (long)rol.id_rol_autorizacion,
            (string)rol.codigo,
            (string)rol.nombre,
            (string)rol.estado,
            codigos);
    }

    /// <summary>
    /// Reemplaza por completo el conjunto de objetos de autorizacion
    /// asignados a un rol (borra y vuelve a insertar dentro de una
    /// transaccion) — mas simple de razonar desde la UI que un diff
    /// incremental de checkboxes.
    /// </summary>
    public async Task ReemplazarObjetosRolAsync(ActorContext actor, long idRolAutorizacion, IReadOnlyCollection<string> codigosObjeto)
    {
        using var conexion = NuevaConexion();
        conexion.Open();
        using var transaccion = conexion.BeginTransaction();

        try
        {
            var antes = (await conexion.QueryAsync<string>(
                """
                SELECT oa.codigo FROM sec.ROL_AUTORIZACION_OBJETO rao
                JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
                WHERE rao.id_rol_autorizacion = @idRolAutorizacion
                """, new { idRolAutorizacion }, transaccion)).ToHashSet();

            await conexion.ExecuteAsync(
                "DELETE FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @idRolAutorizacion",
                new { idRolAutorizacion }, transaccion);

            if (codigosObjeto.Count > 0)
            {
                const string sqlIds = """
                    SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo IN @codigos
                    """;
                var idsObjeto = (await conexion.QueryAsync<long>(
                    sqlIds, new { codigos = codigosObjeto }, transaccion)).ToList();

                foreach (var idObjeto in idsObjeto)
                {
                    await conexion.ExecuteAsync(
                        """
                        INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
                        VALUES (@idRolAutorizacion, @idObjeto)
                        """,
                        new { idRolAutorizacion, idObjeto }, transaccion);
                }
            }

            var despues = (await conexion.QueryAsync<string>(
                """
                SELECT oa.codigo FROM sec.ROL_AUTORIZACION_OBJETO rao
                JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
                WHERE rao.id_rol_autorizacion = @idRolAutorizacion
                """, new { idRolAutorizacion }, transaccion)).ToHashSet();

            // Un solo renglon por cambio: lo que se quito y lo que se agrego.
            var quitados = antes.Except(despues).OrderBy(x => x).ToList();
            var agregados = despues.Except(antes).OrderBy(x => x).ToList();
            if (quitados.Count > 0 || agregados.Count > 0)
                await Auditoria.CambioAsync(conexion, transaccion, actor, EntidadRol, idRolAutorizacion, "permisos",
                    quitados.Count > 0 ? "Quitados: " + string.Join(", ", quitados) : "Ninguno quitado",
                    agregados.Count > 0 ? "Agregados: " + string.Join(", ", agregados) : "Ninguno agregado");

            transaccion.Commit();
        }
        catch
        {
            transaccion.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<UsuarioResponse>> ListarUsuariosAsync(long idCompania)
    {
        using var conexion = NuevaConexion();

        const string sqlUsuarios = """
            SELECT id_usuario, nombre_completo, correo, estado
            FROM sec.USUARIO
            WHERE id_compania = @idCompania
            ORDER BY nombre_completo
            """;
        var usuarios = (await conexion.QueryAsync(sqlUsuarios, new { idCompania })).ToList();

        const string sqlRoles = """
            SELECT ur.id_usuario, ra.codigo
            FROM sec.USUARIO_ROL ur
            JOIN sec.ROL_AUTORIZACION ra ON ra.id_rol_autorizacion = ur.id_rol_autorizacion
            """;
        var asignaciones = (await conexion.QueryAsync(sqlRoles)).ToList();

        return usuarios.Select(u => new UsuarioResponse(
            (long)u.id_usuario,
            (string)u.nombre_completo,
            (string)u.correo,
            (string)u.estado,
            asignaciones
                .Where(a => (long)a.id_usuario == (long)u.id_usuario)
                .Select(a => (string)a.codigo)
                .ToList()
        )).ToList();
    }

    public async Task<UsuarioResponse?> ObtenerUsuarioAsync(long idCompania, long idUsuario)
    {
        var usuarios = await ListarUsuariosAsync(idCompania);
        return usuarios.FirstOrDefault(u => u.IdUsuario == idUsuario);
    }

    /// <summary>
    /// Reemplaza por completo el conjunto de roles asignados a un usuario.
    /// Solo considera roles visibles para su compania (plantilla o propios)
    /// para no poder "colarle" por codigo un rol de otra compania.
    /// </summary>
    public async Task ReemplazarRolesUsuarioAsync(long idCompania, long idUsuario, IReadOnlyCollection<string> codigosRol)
    {
        using var conexion = NuevaConexion();
        conexion.Open();
        using var transaccion = conexion.BeginTransaction();

        try
        {
            await conexion.ExecuteAsync(
                "DELETE FROM sec.USUARIO_ROL WHERE id_usuario = @idUsuario",
                new { idUsuario }, transaccion);

            if (codigosRol.Count > 0)
            {
                const string sqlIds = """
                    SELECT id_rol_autorizacion
                    FROM sec.ROL_AUTORIZACION
                    WHERE codigo IN @codigos AND (id_compania IS NULL OR id_compania = @idCompania)
                    """;
                var idsRol = (await conexion.QueryAsync<long>(
                    sqlIds, new { codigos = codigosRol, idCompania }, transaccion)).ToList();

                foreach (var idRol in idsRol)
                {
                    await conexion.ExecuteAsync(
                        """
                        INSERT INTO sec.USUARIO_ROL (id_usuario, id_rol_autorizacion)
                        VALUES (@idUsuario, @idRol)
                        """,
                        new { idUsuario, idRol }, transaccion);
                }
            }

            transaccion.Commit();
        }
        catch
        {
            transaccion.Rollback();
            throw;
        }
    }
}
