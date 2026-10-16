using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Mantenimiento de usuarios (alta, cambio, inactivacion, restablecer contrasena). La identidad vive en Keycloak
/// (se crea/deshabilita alli con KeycloakAdminServicio) y el usuario de negocio en sec.USUARIO, enlazado por subject_iam.
/// Permiso: SEG_ADMINISTRACION.
/// </summary>
public class UsuarioRepositorio
{
    public const string Entidad = "USUARIO";
    public const string TipoFisico = "FISICO";
    public const string TipoIntegracion = "INTEGRACION";
    private static readonly Regex PatronUsername = new(@"^[a-z0-9][a-z0-9._-]{2,49}$", RegexOptions.Compiled);
    private static readonly Regex PatronCorreo = new(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$", RegexOptions.Compiled);

    private readonly TenantContext _tenant;
    private readonly KeycloakAdminServicio _keycloak;

    public UsuarioRepositorio(TenantContext tenant, KeycloakAdminServicio keycloak)
    {
        _tenant = tenant;
        _keycloak = keycloak;
    }

    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private static string? Puesto(string? p) => string.IsNullOrWhiteSpace(p) ? null : (p.Trim().Length > 100 ? p.Trim()[..100] : p.Trim());

    public bool KeycloakConfigurado => _keycloak.Configurado;

    private const string SqlBase = """
        SELECT u.id_usuario, u.username, u.nombre_completo, u.nombres, u.apellidos, u.correo, u.estado, u.subject_iam,
               u.id_departamento, d.nombre AS unidad_nombre, u.id_superior, s.nombre_completo AS superior_nombre,
               u.creado_en, u.creado_por, u.modificado_en, u.modificado_por, u.puesto, u.tipo_usuario
        FROM sec.USUARIO u
        LEFT JOIN sec.DEPARTAMENTO d ON d.id_departamento = u.id_departamento
        LEFT JOIN sec.USUARIO s ON s.id_usuario = u.id_superior
        WHERE u.id_compania = @idCompania
        """;

    private static UsuarioAdminResponse Mapear(dynamic u, ILookup<long, string> roles) => new(
        (long)u.id_usuario, (string?)u.username, (string)u.nombre_completo, (string?)u.nombres, (string?)u.apellidos,
        (string)u.correo, (string)u.estado, (long?)u.id_departamento, (string?)u.unidad_nombre,
        (long?)u.id_superior, (string?)u.superior_nombre,
        roles[(long)u.id_usuario].ToList(), (DateTime)u.creado_en, (string)u.creado_por, (DateTime?)u.modificado_en, (string?)u.modificado_por, (string?)u.puesto, (string)u.tipo_usuario);

    private static async Task<ILookup<long, string>> RolesAsync(SqlConnection c, SqlTransaction? tx)
    {
        var filas = await c.QueryAsync(
            """
            SELECT ur.id_usuario, ra.codigo FROM sec.USUARIO_ROL ur
            JOIN sec.ROL_AUTORIZACION ra ON ra.id_rol_autorizacion = ur.id_rol_autorizacion
            """, null, tx);
        return filas.ToLookup(f => (long)f.id_usuario, f => (string)f.codigo);
    }

    public async Task<IReadOnlyList<UsuarioAdminResponse>> ListarAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var usuarios = (await c.QueryAsync(SqlBase + " ORDER BY u.nombre_completo", new { idCompania })).ToList();
        var roles = await RolesAsync(c, null);
        return usuarios.Select(u => (UsuarioAdminResponse)Mapear(u, roles)).ToList();
    }

    public async Task<UsuarioAdminResponse?> ObtenerAsync(long idCompania, long idUsuario)
    {
        using var c = NuevaConexion();
        return await ObtenerAsync(c, null, idCompania, idUsuario);
    }

    private static async Task<UsuarioAdminResponse?> ObtenerAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idUsuario)
    {
        var u = await c.QuerySingleOrDefaultAsync(SqlBase + " AND u.id_usuario = @idUsuario", new { idCompania, idUsuario }, tx);
        if (u is null) return null;
        return Mapear(u, await RolesAsync(c, tx));
    }

    // ----- Validaciones comunes ------------------------------------------------------------

    private static string? ValidarDatos(string nombres, string apellidos, string correo)
    {
        if (string.IsNullOrWhiteSpace(nombres) || nombres.Trim().Length > 100) return "Los nombres son obligatorios (maximo 100 caracteres).";
        if (string.IsNullOrWhiteSpace(apellidos) || apellidos.Trim().Length > 100) return "Los apellidos son obligatorios (maximo 100 caracteres).";
        if (string.IsNullOrWhiteSpace(correo) || correo.Trim().Length > 200 || !PatronCorreo.IsMatch(correo.Trim())) return "El correo no tiene un formato valido.";
        return null;
    }

    private static async Task<ResultadoOperacion?> ValidarUbicacionAsync(
        SqlConnection c, SqlTransaction tx, long idCompania, long? idUsuarioEditando, long? idUnidad, long? idSuperior, bool unidadCambia, bool superiorCambia)
    {
        if (idUnidad is { } un && unidadCambia)
        {
            var ok = await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE id_departamento = @u AND id_compania = @c AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { u = un, c = idCompania }, tx);
            if (!ok) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La unidad no existe o esta inactiva.");
        }
        if (idSuperior is { } sup && superiorCambia)
        {
            if (sup == idUsuarioEditando) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Un usuario no puede ser su propio superior.");
            var ok = await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.USUARIO WHERE id_usuario = @u AND id_compania = @c AND estado = 'Activo' AND tipo_usuario = 'FISICO') THEN 1 ELSE 0 END",
                new { u = sup, c = idCompania }, tx);
            if (!ok) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El superior no existe o esta inactivo.");
            if (idUsuarioEditando is { } yo)
            {
                // Evita ciclos: subiendo desde el superior no se debe llegar al usuario editado.
                var ciclo = await c.ExecuteScalarAsync<bool>(
                    """
                    WITH cadena AS (
                        SELECT id_usuario, id_superior FROM sec.USUARIO WHERE id_usuario = @sup
                        UNION ALL
                        SELECT u.id_usuario, u.id_superior FROM sec.USUARIO u JOIN cadena ch ON u.id_usuario = ch.id_superior
                    )
                    SELECT CASE WHEN EXISTS (SELECT 1 FROM cadena WHERE id_usuario = @yo) THEN 1 ELSE 0 END
                    OPTION (MAXRECURSION 100)
                    """, new { sup, yo }, tx);
                if (ciclo) return ResultadoOperacion.Fallo(400, "ciclo", "Ese superior depende (directa o indirectamente) de este usuario; no se puede formar un ciclo.");
            }
        }
        return null;
    }

    private static async Task ReemplazarRolesAsync(SqlConnection c, SqlTransaction tx, long idCompania, long idUsuario, IReadOnlyCollection<string> codigos)
    {
        await c.ExecuteAsync("DELETE FROM sec.USUARIO_ROL WHERE id_usuario = @idUsuario", new { idUsuario }, tx);
        if (codigos.Count == 0) return;
        await c.ExecuteAsync(
            """
            INSERT INTO sec.USUARIO_ROL (id_usuario, id_rol_autorizacion)
            SELECT @idUsuario, id_rol_autorizacion FROM sec.ROL_AUTORIZACION
            WHERE codigo IN @codigos AND (id_compania IS NULL OR id_compania = @idCompania)
            """, new { idUsuario, codigos, idCompania }, tx);
    }

    private static string Cod(IEnumerable<string> roles) => string.Join(",", roles.OrderBy(x => x));

    // ----- Alta ----------------------------------------------------------------------------

    public async Task<(ResultadoOperacion Resultado, UsuarioCredencialResponse? Credencial)> CrearAsync(ActorContext actor, UsuarioCrearRequest req)
    {
        var username = (req.Username ?? "").Trim().ToLowerInvariant();
        if (!PatronUsername.IsMatch(username))
            return (ResultadoOperacion.Fallo(400, "datos_invalidos", "El usuario debe tener 3 a 50 caracteres: minusculas, numeros, punto, guion o guion bajo."), null);
        var tecnico = string.Equals(req.Tipo, TipoIntegracion, StringComparison.OrdinalIgnoreCase);
        if (req.Tipo is not null && !tecnico && !string.Equals(req.Tipo, TipoFisico, StringComparison.OrdinalIgnoreCase))
            return (ResultadoOperacion.Fallo(400, "datos_invalidos", "El tipo de usuario debe ser FISICO o INTEGRACION."), null);

        // Usuario tecnico (INTEGRACION): solo nombre; no tiene correo real, contrasena ni cuenta en Keycloak, asi que no puede iniciar sesion.
        var nombresIn = req.Nombres; var apellidosIn = req.Apellidos; var correoIn = req.Correo;
        if (tecnico)
        {
            if (string.IsNullOrWhiteSpace(nombresIn) || nombresIn.Trim().Length > 100)
                return (ResultadoOperacion.Fallo(400, "datos_invalidos", "El nombre es obligatorio (maximo 100 caracteres)."), null);
            apellidosIn = string.IsNullOrWhiteSpace(apellidosIn) ? "(integracion)" : apellidosIn;
            correoIn = $"{username}@integracion.local";
        }
        var err = ValidarDatos(nombresIn, apellidosIn, correoIn);
        if (err is not null) return (ResultadoOperacion.Fallo(400, "datos_invalidos", err), null);
        var nombres = nombresIn.Trim(); var apellidos = apellidosIn.Trim(); var correo = correoIn.Trim().ToLowerInvariant();

        using var c = NuevaConexion();
        c.Open();

        if (await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.USUARIO WHERE id_compania = @c AND (username = @u OR LOWER(correo) = @correo)) THEN 1 ELSE 0 END",
                new { c = actor.IdCompania, u = username, correo }))
            return (ResultadoOperacion.Fallo(409, "usuario_existente", "Ya existe un usuario con ese nombre de usuario o correo."), null);

        using (var tv = c.BeginTransaction())
        {
            var bad = await ValidarUbicacionAsync(c, tv, actor.IdCompania, null, req.IdUnidad, req.IdSuperior, true, true);
            if (bad is not null) return (bad, null);
            tv.Rollback();
        }

        var password = tecnico ? "" : KeycloakAdminServicio.GenerarPasswordTemporal();
        string sub;
        if (tecnico) sub = $"svc:{Guid.NewGuid():N}";   // marcador unico: sin cuenta en Keycloak, nunca coincide con el 'sub' de un token
        else
        {
            try { sub = await _keycloak.CrearUsuarioAsync(username, nombres, apellidos, correo, _tenant.CodigoCliente, password); }
            catch (KeycloakAdminException ex) { return (ResultadoOperacion.Fallo(ex.Estado, ex.Codigo, ex.Message), null); }
        }

        try
        {
            using var tx = c.BeginTransaction();
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO sec.USUARIO (id_compania, id_departamento, id_superior, subject_iam, username, nombres, apellidos, nombre_completo, correo, puesto, tipo_usuario, creado_por)
                OUTPUT INSERTED.id_usuario
                VALUES (@idCompania, @unidad, @superior, @sub, @username, @nombres, @apellidos, @completo, @correo, @puesto, @tipoUsuario, @por)
                """,
                new
                {
                    actor.IdCompania, unidad = req.IdUnidad, superior = req.IdSuperior, sub, username, nombres, apellidos,
                    completo = tecnico && apellidos == "(integracion)" ? nombres : $"{nombres} {apellidos}", correo, puesto = Puesto(req.Puesto),
                    tipoUsuario = tecnico ? TipoIntegracion : TipoFisico, por = actor.NombreDescriptivo
                }, tx);
            await ReemplazarRolesAsync(c, tx, actor.IdCompania, id, req.CodigosRol ?? []);
            await Auditoria.AltaAsync(c, tx, actor, Entidad, id, $"{username} · tipo {(tecnico ? TipoIntegracion : TipoFisico)} · roles: {Cod(req.CodigosRol ?? [])}");
            tx.Commit();
            return (ResultadoOperacion.Ok(id), new UsuarioCredencialResponse(id, username, password));
        }
        catch
        {
            if (!tecnico) await _keycloak.EliminarSilenciosoAsync(sub);   // no dejar un usuario huerfano en Keycloak
            throw;
        }
    }

    // ----- Cambio --------------------------------------------------------------------------

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long idUsuario, UsuarioEditarRequest req)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();

        var actual = await c.QuerySingleOrDefaultAsync(
            "SELECT tipo_usuario, subject_iam, nombres, apellidos, nombre_completo, correo, id_departamento, id_superior, estado, puesto FROM sec.USUARIO WHERE id_usuario = @idUsuario AND id_compania = @c",
            new { idUsuario, c = actor.IdCompania }, tx);
        if (actual is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El usuario no existe.");
        var tecnico = (string)actual.tipo_usuario == TipoIntegracion;

        // Usuario tecnico: el correo es un marcador interno y no se edita; el apellido puede quedar vacio.
        var apellidosIn = tecnico && string.IsNullOrWhiteSpace(req.Apellidos) ? "(integracion)" : req.Apellidos;
        var correoIn = tecnico ? (string)actual.correo : req.Correo;
        var err = ValidarDatos(req.Nombres, apellidosIn, correoIn);
        if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
        var nombres = req.Nombres.Trim(); var apellidos = apellidosIn.Trim(); var correo = correoIn.Trim().ToLowerInvariant();
        var completo = tecnico && apellidos == "(integracion)" ? nombres : $"{nombres} {apellidos}";

        if (await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.USUARIO WHERE id_compania = @c AND id_usuario <> @id AND LOWER(correo) = @correo) THEN 1 ELSE 0 END",
                new { c = actor.IdCompania, id = idUsuario, correo }, tx))
            return ResultadoOperacion.Fallo(409, "usuario_existente", "Ya existe otro usuario con ese correo.");

        var unidadCambia = (long?)actual.id_departamento != req.IdUnidad;
        var superiorCambia = (long?)actual.id_superior != req.IdSuperior;
        var bad = await ValidarUbicacionAsync(c, tx, actor.IdCompania, idUsuario, req.IdUnidad, req.IdSuperior, unidadCambia, superiorCambia);
        if (bad is not null) return bad;

        var datosCambian = (string?)actual.nombres != nombres || (string?)actual.apellidos != apellidos || !string.Equals((string)actual.correo, correo, StringComparison.OrdinalIgnoreCase);
        if (datosCambian && !tecnico)
        {
            try { await _keycloak.ActualizarDatosAsync((string)actual.subject_iam, nombres, apellidos, correo); }
            catch (KeycloakAdminException ex) { return ResultadoOperacion.Fallo(ex.Estado, ex.Codigo, ex.Message); }
        }

        await c.ExecuteAsync(
            """
            UPDATE sec.USUARIO SET nombres = @nombres, apellidos = @apellidos, nombre_completo = @completo, correo = @correo,
                   id_departamento = @unidad, id_superior = @superior, puesto = @puesto, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            WHERE id_usuario = @idUsuario
            """,
            new { nombres, apellidos, completo, correo, unidad = req.IdUnidad, superior = req.IdSuperior, puesto = Puesto(req.Puesto), por = actor.NombreDescriptivo, idUsuario }, tx);

        if ((string)actual.nombre_completo != completo) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "nombre", (string)actual.nombre_completo, completo);
        if (!string.Equals((string)actual.correo, correo, StringComparison.OrdinalIgnoreCase)) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "correo", (string)actual.correo, correo);
        if (unidadCambia) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "id_unidad", ((long?)actual.id_departamento)?.ToString(), req.IdUnidad?.ToString());
        if ((string?)actual.puesto != Puesto(req.Puesto)) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "puesto", (string?)actual.puesto, Puesto(req.Puesto));
        if (superiorCambia) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "id_superior", ((long?)actual.id_superior)?.ToString(), req.IdSuperior?.ToString());

        if (req.CodigosRol is not null)
        {
            var antes = (await RolesAsync(c, tx))[idUsuario].ToList();
            // No dejar al sistema sin ningun administrador de seguridad activo.
            var quitaAdmin = antes.Any() && !(await ConservaAdministradorAsync(c, tx, actor.IdCompania, idUsuario, req.CodigosRol));
            if (quitaAdmin && (string)actual.estado == "Activo")
                return ResultadoOperacion.Fallo(409, "ultimo_administrador", "Con este cambio no quedaria ningun usuario activo con permiso de administrar la seguridad.");
            await ReemplazarRolesAsync(c, tx, actor.IdCompania, idUsuario, req.CodigosRol);
            var despues = (await RolesAsync(c, tx))[idUsuario].ToList();
            if (Cod(antes) != Cod(despues)) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "roles", Cod(antes), Cod(despues));
        }

        tx.Commit();
        return ResultadoOperacion.Ok(idUsuario);
    }

    /// <summary>Reemplaza solo los roles de un usuario (usado desde la pestana Usuarios de un rol). Mismas protecciones y auditoria que el cambio completo.</summary>
    public async Task<ResultadoOperacion> CambiarRolesAsync(ActorContext actor, long idUsuario, IReadOnlyCollection<string> codigos)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var u = await c.QuerySingleOrDefaultAsync(
            "SELECT estado FROM sec.USUARIO WHERE id_usuario = @idUsuario AND id_compania = @c", new { idUsuario, c = actor.IdCompania }, tx);
        if (u is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El usuario no existe.");

        var antes = (await RolesAsync(c, tx))[idUsuario].ToList();
        if (antes.Any() && (string)u.estado == "Activo" && !(await ConservaAdministradorAsync(c, tx, actor.IdCompania, idUsuario, codigos)))
            return ResultadoOperacion.Fallo(409, "ultimo_administrador", "Con este cambio no quedaria ningun usuario activo con permiso de administrar la seguridad.");
        await ReemplazarRolesAsync(c, tx, actor.IdCompania, idUsuario, codigos);
        var despues = (await RolesAsync(c, tx))[idUsuario].ToList();
        if (Cod(antes) != Cod(despues)) await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "roles", Cod(antes), Cod(despues));
        tx.Commit();
        return ResultadoOperacion.Ok(idUsuario);
    }

    /// <summary>True si, tras aplicar `codigosRolNuevos` al usuario, sigue habiendo otro usuario activo con SEG_ADMINISTRACION.</summary>
    private static async Task<bool> ConservaAdministradorAsync(SqlConnection c, SqlTransaction tx, long idCompania, long idUsuario, IReadOnlyCollection<string> codigosRolNuevos)
    {
        var otros = await ExisteOtroAdministradorAsync(c, tx, idCompania, idUsuario);
        if (otros) return true;
        if (codigosRolNuevos.Count == 0) return false;
        return await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM sec.ROL_AUTORIZACION ra
                JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = ra.id_rol_autorizacion
                JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
                WHERE ra.codigo IN @codigos AND (ra.id_compania IS NULL OR ra.id_compania = @idCompania) AND oa.codigo = 'SEG_ADMINISTRACION') THEN 1 ELSE 0 END
            """, new { codigos = codigosRolNuevos, idCompania }, tx);
    }

    private static Task<bool> ExisteOtroAdministradorAsync(SqlConnection c, SqlTransaction tx, long idCompania, long idUsuario) =>
        c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM sec.USUARIO u
                JOIN sec.USUARIO_ROL ur ON ur.id_usuario = u.id_usuario
                JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = ur.id_rol_autorizacion
                JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
                WHERE u.id_compania = @idCompania AND u.estado = 'Activo' AND u.tipo_usuario = 'FISICO' AND u.id_usuario <> @idUsuario AND oa.codigo = 'SEG_ADMINISTRACION') THEN 1 ELSE 0 END
            """, new { idCompania, idUsuario }, tx);

    // ----- Activar / inactivar -------------------------------------------------------------

    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long idUsuario, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var u = await c.QuerySingleOrDefaultAsync(
            "SELECT subject_iam, estado, nombre_completo, tipo_usuario FROM sec.USUARIO WHERE id_usuario = @idUsuario AND id_compania = @c",
            new { idUsuario, c = actor.IdCompania }, tx);
        if (u is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El usuario no existe.");
        if ((string)u.estado == estado) return ResultadoOperacion.Ok(idUsuario);

        var tecnico = (string)u.tipo_usuario == TipoIntegracion;
        if (estado == "Inactivo" && tecnico)
        {
            var usadoPor = (await c.QueryAsync<string>(
                "SELECT TOP 5 nombre FROM sec.USUARIO_INTEGRACION WHERE id_usuario = @idUsuario AND estado = 'Activo' ORDER BY nombre", new { idUsuario }, tx)).ToList();
            if (usadoPor.Count > 0)
                return ResultadoOperacion.Fallo(409, "usuario_en_uso", $"Lo usan integraciones activas ({string.Join(", ", usadoPor)}). Inactive o reasigne esas integraciones antes.");
        }
        else if (estado == "Inactivo")
        {
            if (idUsuario == actor.IdUsuario) return ResultadoOperacion.Fallo(409, "autoinactivacion", "No puede inactivar su propio usuario.");

            if (!await ExisteOtroAdministradorAsync(c, tx, actor.IdCompania, idUsuario)
                && await c.ExecuteScalarAsync<bool>(
                    """
                    SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.USUARIO_ROL ur
                        JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = ur.id_rol_autorizacion
                        JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
                        WHERE ur.id_usuario = @idUsuario AND oa.codigo = 'SEG_ADMINISTRACION') THEN 1 ELSE 0 END
                    """, new { idUsuario }, tx))
                return ResultadoOperacion.Fallo(409, "ultimo_administrador", "Es el ultimo usuario activo que puede administrar la seguridad; no se puede inactivar.");

            var subs = (await c.QueryAsync<string>(
                "SELECT TOP 5 nombre_completo FROM sec.USUARIO WHERE id_superior = @idUsuario AND estado = 'Activo' ORDER BY nombre_completo", new { idUsuario }, tx)).ToList();
            if (subs.Count > 0)
                return ResultadoOperacion.Fallo(409, "tiene_subordinados", $"Tiene subordinados activos ({string.Join(", ", subs)}...). Reasigne su superior antes de inactivarlo.");

            var unidades = (await c.QueryAsync<string>(
                "SELECT TOP 5 nombre FROM sec.DEPARTAMENTO WHERE id_responsable = @idUsuario AND estado = 'Activo'", new { idUsuario }, tx)).ToList();
            if (unidades.Count > 0)
                return ResultadoOperacion.Fallo(409, "es_responsable", $"Es responsable de unidades activas ({string.Join(", ", unidades)}). Cambie el responsable antes de inactivarlo.");

            var pendientes = await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM apr.GESTION WHERE id_usuario_solicitante = @idUsuario AND estado = 'Pendiente de Aprobacion'", new { idUsuario }, tx);
            if (pendientes > 0)
                return ResultadoOperacion.Fallo(409, "gestiones_pendientes", $"Tiene {pendientes} gestion(es) pendiente(s) de aprobacion; resuelvalas antes de inactivarlo.");
        }

        if (!tecnico)
        {
            try { await _keycloak.EstablecerHabilitadoAsync((string)u.subject_iam, estado == "Activo"); }
            catch (KeycloakAdminException ex) { return ResultadoOperacion.Fallo(ex.Estado, ex.Codigo, ex.Message); }
        }

        await c.ExecuteAsync(
            "UPDATE sec.USUARIO SET estado = @estado, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_usuario = @idUsuario",
            new { estado, por = actor.NombreDescriptivo, idUsuario }, tx);
        await Auditoria.CambioAsync(c, tx, actor, Entidad, idUsuario, "estado", (string)u.estado, estado);
        tx.Commit();
        return ResultadoOperacion.Ok(idUsuario);
    }

    // ----- Restablecer contrasena -----------------------------------------------------------

    public async Task<(ResultadoOperacion Resultado, UsuarioCredencialResponse? Credencial)> RestablecerPasswordAsync(ActorContext actor, long idUsuario)
    {
        using var c = NuevaConexion();
        c.Open();
        var u = await c.QuerySingleOrDefaultAsync(
            "SELECT subject_iam, username, estado, tipo_usuario FROM sec.USUARIO WHERE id_usuario = @idUsuario AND id_compania = @c",
            new { idUsuario, c = actor.IdCompania });
        if (u is null) return (ResultadoOperacion.Fallo(404, "no_encontrado", "El usuario no existe."), null);
        if ((string)u.tipo_usuario == TipoIntegracion) return (ResultadoOperacion.Fallo(409, "usuario_tecnico", "Un usuario de integracion no tiene contrasena ni inicia sesion."), null);
        if ((string)u.estado != "Activo") return (ResultadoOperacion.Fallo(409, "usuario_inactivo", "El usuario esta inactivo."), null);

        var password = KeycloakAdminServicio.GenerarPasswordTemporal();
        try { await _keycloak.RestablecerPasswordAsync((string)u.subject_iam, password); }
        catch (KeycloakAdminException ex) { return (ResultadoOperacion.Fallo(ex.Estado, ex.Codigo, ex.Message), null); }

        await Auditoria.CambioAsync(c, null, actor, Entidad, idUsuario, "password", null, "restablecida");
        return (ResultadoOperacion.Ok(idUsuario), new UsuarioCredencialResponse(idUsuario, (string?)u.username ?? "", password));
    }
}
