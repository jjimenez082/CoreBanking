using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Usuarios de integracion (sec.USUARIO_INTEGRACION): consulta y asignacion de su canal fijo.</summary>
public class IntegracionRepositorio
{
    public const string ObjetoAdministracion = "INT_ADMINISTRACION";
    public const string Entidad = "USUARIO_INTEGRACION";

    private static readonly System.Text.RegularExpressions.Regex PatronIdentificador = new("^[a-z0-9]([a-z0-9_-]{1,48}[a-z0-9])$");

    private readonly TenantContext _tenant;
    private readonly KeycloakAdminServicio _keycloak;
    private readonly string _urlToken;
    public IntegracionRepositorio(TenantContext tenant, KeycloakAdminServicio keycloak, IConfiguration config)
    {
        _tenant = tenant;
        _keycloak = keycloak;
        _urlToken = $"{(config["Keycloak:Authority"] ?? "").TrimEnd('/')}/protocol/openid-connect/token";
    }
    public bool KeycloakConfigurado => _keycloak.Configurado;
    public string CodigoCompania => _tenant.CodigoCliente.ToLowerInvariant();
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    public async Task<IReadOnlyList<IntegracionResponse>> ListarAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT ui.id_usuario_integracion, ui.client_id_iam, ui.nombre, ui.estado, ui.id_canal, ca.nombre AS canal_nombre,
                   ui.id_usuario, u.nombre_completo AS usuario_nombre
            FROM sec.USUARIO_INTEGRACION ui
            LEFT JOIN gen.LISTA_VALORES_ITEM ca ON ca.id_lista_valores_item = ui.id_canal
            LEFT JOIN sec.USUARIO u ON u.id_usuario = ui.id_usuario
            WHERE ui.id_compania = @idCompania ORDER BY ui.nombre
            """, new { idCompania });
        return filas.Select(f => new IntegracionResponse(
            (long)f.id_usuario_integracion, (string)f.client_id_iam, (string)f.nombre, (string)f.estado, (long?)f.id_canal, (string?)f.canal_nombre, (long?)f.id_usuario, (string?)f.usuario_nombre))
            .Cast<IntegracionResponse>().ToList();
    }

    public async Task<ResultadoOperacion> AsignarCanalAsync(ActorContext actor, long idIntegracion, long idCanal)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var a = await c.QuerySingleOrDefaultAsync(
                "SELECT id_canal FROM sec.USUARIO_INTEGRACION WITH (UPDLOCK) WHERE id_usuario_integracion = @id AND id_compania = @c",
                new { id = idIntegracion, c = actor.IdCompania }, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el usuario de integracion.");
            var error = await InterlocutorRepositorio.ValidarCanalAsync(c, tx, idCanal, null);
            if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
            if ((long?)a.id_canal == idCanal) return ResultadoOperacion.Ok(idIntegracion);

            await c.ExecuteAsync("UPDATE sec.USUARIO_INTEGRACION SET id_canal = @canal WHERE id_usuario_integracion = @id", new { canal = idCanal, id = idIntegracion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, Entidad, idIntegracion, "id_canal", ((long?)a.id_canal)?.ToString(), idCanal.ToString());
            tx.Commit();
            return ResultadoOperacion.Ok(idIntegracion);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Usuarios tecnicos activos (tipo INTEGRACION) para elegir el "Usuario asociado".</summary>
    public async Task<IReadOnlyList<UsuarioItemResponse>> UsuariosTecnicosAsync(long idCompania)
    {
        using var c = NuevaConexion();
        return (await c.QueryAsync<UsuarioItemResponse>(
            "SELECT id_usuario AS Id, nombre_completo AS Nombre, username AS Username FROM sec.USUARIO WHERE id_compania = @idCompania AND estado = 'Activo' AND tipo_usuario = 'INTEGRACION' ORDER BY nombre_completo",
            new { idCompania })).ToList();
    }

    /// <summary>Valida que sea un usuario tecnico (tipo INTEGRACION) de la compania y activo. Devuelve el mensaje de error o null.</summary>
    private static async Task<string?> ValidarUsuarioTecnicoAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idUsuario)
    {
        var u = await c.QuerySingleOrDefaultAsync(
            "SELECT estado, tipo_usuario FROM sec.USUARIO WHERE id_usuario = @u AND id_compania = @c", new { u = idUsuario, c = idCompania }, tx);
        if (u is null) return "El usuario asociado no existe.";
        if ((string)u.tipo_usuario != UsuarioRepositorio.TipoIntegracion) return "El usuario asociado debe ser de tipo Integracion (usuario tecnico).";
        if ((string)u.estado != "Activo") return "El usuario asociado esta inactivo.";
        return null;
    }

    /// <summary>Cambia el usuario tecnico cuyos permisos usa la integracion.</summary>
    public async Task<ResultadoOperacion> AsignarUsuarioAsync(ActorContext actor, long idIntegracion, long idUsuario)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync(
            "SELECT id_usuario FROM sec.USUARIO_INTEGRACION WITH (UPDLOCK) WHERE id_usuario_integracion = @id AND id_compania = @c",
            new { id = idIntegracion, c = actor.IdCompania }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el usuario de integracion.");
        var error = await ValidarUsuarioTecnicoAsync(c, tx, actor.IdCompania, idUsuario);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        if ((long?)a.id_usuario == idUsuario) return ResultadoOperacion.Ok(idIntegracion);

        await c.ExecuteAsync("UPDATE sec.USUARIO_INTEGRACION SET id_usuario = @u WHERE id_usuario_integracion = @id", new { u = idUsuario, id = idIntegracion }, tx);
        await Auditoria.CambioAsync(c, tx, actor, Entidad, idIntegracion, "id_usuario", ((long?)a.id_usuario)?.ToString(), idUsuario.ToString());
        tx.Commit();
        return ResultadoOperacion.Ok(idIntegracion);
    }

    /// <summary>
    /// Alta de una integracion: crea el client en Keycloak (Client Credentials, con el claim de compania) y lo registra aqui con su canal fijo.
    /// El client_id queda como "{codigo compania}-{identificador}" para que no choque con el de otras companias del mismo realm.
    /// </summary>
    public async Task<(ResultadoOperacion Resultado, IntegracionCredencialResponse? Credencial)> CrearAsync(ActorContext actor, IntegracionCrearRequest req)
    {
        var ident = (req.Identificador ?? "").Trim().ToLowerInvariant();
        var nombre = (req.Nombre ?? "").Trim();
        if (!PatronIdentificador.IsMatch(ident))
            return (ResultadoOperacion.Fallo(400, "datos_invalidos", "El identificador debe tener 3 a 50 caracteres: minusculas, numeros, guion o guion bajo (sin espacios), y empezar y terminar con letra o numero."), null);
        if (nombre.Length is < 3 or > 200)
            return (ResultadoOperacion.Fallo(400, "datos_invalidos", "El nombre es obligatorio (3 a 200 caracteres)."), null);
        var clientId = $"{_tenant.CodigoCliente.ToLowerInvariant()}-{ident}";

        using var c = NuevaConexion();
        c.Open();
        using (var tv = c.BeginTransaction())
        {
            var errCanal = await InterlocutorRepositorio.ValidarCanalAsync(c, tv, req.IdCanal, null);
            tv.Rollback();
            if (errCanal is not null) return (ResultadoOperacion.Fallo(400, "datos_invalidos", errCanal), null);
        }
        var errUsuario = req.IdUsuario <= 0 ? "Seleccione el usuario asociado (usuario tecnico de tipo Integracion)." : await ValidarUsuarioTecnicoAsync(c, null, actor.IdCompania, req.IdUsuario);
        if (errUsuario is not null) return (ResultadoOperacion.Fallo(400, "datos_invalidos", errUsuario), null);
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sec.USUARIO_INTEGRACION WHERE client_id_iam = @clientId", new { clientId }) > 0)
            return (ResultadoOperacion.Fallo(409, "integracion_existente", "Ya existe una integracion con ese identificador."), null);

        string idInterno, secret;
        try { (idInterno, secret) = await _keycloak.CrearClienteServicioAsync(clientId, nombre, _tenant.CodigoCliente); }
        catch (KeycloakAdminException ex) { return (ResultadoOperacion.Fallo(ex.Estado, ex.Codigo, ex.Message), null); }

        try
        {
            using var tx = c.BeginTransaction();
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO sec.USUARIO_INTEGRACION (id_compania, client_id_iam, nombre, id_canal, id_usuario, creado_por)
                OUTPUT INSERTED.id_usuario_integracion
                VALUES (@idCompania, @clientId, @nombre, @canal, @idUsuario, @por)
                """, new { actor.IdCompania, clientId, nombre, canal = req.IdCanal, idUsuario = req.IdUsuario, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, Entidad, id, $"{nombre} · client_id {clientId} · usuario asociado {req.IdUsuario}");
            tx.Commit();
            return (ResultadoOperacion.Ok(id), new IntegracionCredencialResponse(id, clientId, secret, _urlToken));
        }
        catch
        {
            await _keycloak.EliminarClienteSilenciosoAsync(idInterno);   // no dejar un client huerfano en Keycloak
            throw;
        }
    }

    /// <summary>Genera un secret nuevo; el anterior deja de funcionar al instante. Se muestra una sola vez.</summary>
    public async Task<(ResultadoOperacion Resultado, IntegracionCredencialResponse? Credencial)> RegenerarSecretAsync(ActorContext actor, long id)
    {
        using var c = NuevaConexion();
        var f = await c.QuerySingleOrDefaultAsync("SELECT client_id_iam FROM sec.USUARIO_INTEGRACION WHERE id_usuario_integracion = @id AND id_compania = @c", new { id, c = actor.IdCompania });
        if (f is null) return (ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el usuario de integracion."), null);
        var clientId = (string)f.client_id_iam;
        string secret;
        try { secret = await _keycloak.RegenerarSecretClienteAsync(clientId); }
        catch (KeycloakAdminException ex) { return (ResultadoOperacion.Fallo(ex.Estado, ex.Codigo, ex.Message), null); }
        await Auditoria.CambioAsync(c, null, actor, Entidad, id, "client_secret", null, "Secret regenerado");
        return (ResultadoOperacion.Ok(id), new IntegracionCredencialResponse(id, clientId, secret, _urlToken));
    }

    /// <summary>Activa o inactiva la integracion: se deshabilita el client en Keycloak y la API deja de aceptar su token.</summary>
    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado invalido.");
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var f = await c.QuerySingleOrDefaultAsync("SELECT client_id_iam, estado FROM sec.USUARIO_INTEGRACION WITH (UPDLOCK) WHERE id_usuario_integracion = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
        if (f is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el usuario de integracion.");
        if ((string)f.estado == estado) return ResultadoOperacion.Ok(id);
        try { await _keycloak.EstablecerClienteHabilitadoAsync((string)f.client_id_iam, estado == "Activo"); }
        catch (KeycloakAdminException ex) { return ResultadoOperacion.Fallo(ex.Estado, ex.Codigo, ex.Message); }
        await c.ExecuteAsync("UPDATE sec.USUARIO_INTEGRACION SET estado = @estado WHERE id_usuario_integracion = @id", new { estado, id }, tx);
        await Auditoria.CambioAsync(c, tx, actor, Entidad, id, "estado", (string)f.estado, estado);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }
}
