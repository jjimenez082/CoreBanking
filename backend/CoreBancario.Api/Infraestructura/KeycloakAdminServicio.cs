using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoreBancario.Api.Infraestructura;

public class KeycloakAdminException : Exception
{
    public string Codigo { get; }
    public int Estado { get; }
    public KeycloakAdminException(string codigo, string mensaje, int estado = 502) : base(mensaje) { Codigo = codigo; Estado = estado; }
}

/// <summary>
/// Administracion de usuarios en Keycloak (Admin REST API) con la cuenta de servicio de la API
/// (cliente core-bancario-backend, Client Credentials). Requiere en Keycloak, para esa cuenta de servicio,
/// los roles de realm-management: manage-users, view-users y query-users.
/// Config: Keycloak:Admin { BaseUrl, Realm, ClientId, ClientSecret }. Sin ClientSecret la integracion queda deshabilitada.
/// </summary>
public class KeycloakAdminServicio
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _realm;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _atributoCompania;

    private static string? _token;
    private static DateTime _tokenVence = DateTime.MinValue;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public KeycloakAdminServicio(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(15);
        var s = config.GetSection("Keycloak:Admin");
        _baseUrl = (s["BaseUrl"] ?? "").TrimEnd('/');
        _realm = s["Realm"] ?? "";
        _clientId = s["ClientId"] ?? "core-bancario-backend";
        _clientSecret = s["ClientSecret"] ?? "";
        _atributoCompania = s["AtributoCompania"] ?? "compania";
    }

    public bool Configurado => _baseUrl.Length > 0 && _realm.Length > 0 && _clientSecret.Length > 0;

    private void Exigir()
    {
        if (!Configurado)
            throw new KeycloakAdminException("keycloak_no_configurado",
                "La administracion de usuarios requiere configurar Keycloak:Admin (BaseUrl, Realm, ClientId, ClientSecret) en appsettings.", 503);
    }

    private async Task<string> TokenAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_token is not null && DateTime.UtcNow < _tokenVence) return _token;
            using var resp = await _http.PostAsync($"{_baseUrl}/realms/{_realm}/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials", ["client_id"] = _clientId, ["client_secret"] = _clientSecret
                }));
            if (!resp.IsSuccessStatusCode)
                throw new KeycloakAdminException("keycloak_token", $"No se pudo autenticar la API contra Keycloak ({(int)resp.StatusCode}). Revise ClientId/ClientSecret y que el cliente tenga 'Service accounts roles' habilitado.");
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
            _token = json["access_token"]!.GetValue<string>();
            _tokenVence = DateTime.UtcNow.AddSeconds(Math.Max(10, (json["expires_in"]?.GetValue<int>() ?? 60) - 20));
            return _token;
        }
        finally { _lock.Release(); }
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        Exigir();
        try
        {
            var req = new HttpRequestMessage(metodo, $"{_baseUrl}/admin/realms/{_realm}/{ruta}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
            if (cuerpo is not null) req.Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
            return await _http.SendAsync(req);
        }
        catch (HttpRequestException ex)
        {
            throw new KeycloakAdminException("keycloak_inaccesible", $"No se pudo comunicar con Keycloak: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new KeycloakAdminException("keycloak_inaccesible", "Keycloak no respondio a tiempo.");
        }
    }

    private static async Task<KeycloakAdminException> ErrorAsync(HttpResponseMessage r, string accion)
    {
        var detalle = "";
        try { detalle = (await r.Content.ReadAsStringAsync()); } catch { /* ignorar */ }
        if (r.StatusCode == HttpStatusCode.Forbidden)
            return new KeycloakAdminException("keycloak_sin_permiso", $"Keycloak rechazo la operacion ({accion}): la cuenta de servicio de la API necesita los roles manage-users y view-users de realm-management.");
        return new KeycloakAdminException("keycloak_error", $"Keycloak respondio {(int)r.StatusCode} al {accion}. {Recortar(detalle)}");
    }

    private static string Recortar(string s) => s.Length > 300 ? s[..300] : s;

    /// <summary>Contrasena temporal aleatoria (mayuscula, minuscula, digito), 12 caracteres.</summary>
    public static string GenerarPasswordTemporal()
    {
        const string may = "ABCDEFGHJKLMNPQRSTUVWXYZ", min = "abcdefghijkmnpqrstuvwxyz", dig = "23456789";
        var todos = may + min + dig;
        var chars = new List<char> { may[RandomNumberGenerator.GetInt32(may.Length)], min[RandomNumberGenerator.GetInt32(min.Length)], dig[RandomNumberGenerator.GetInt32(dig.Length)] };
        while (chars.Count < 12) chars.Add(todos[RandomNumberGenerator.GetInt32(todos.Length)]);
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(1000)).ToArray());
    }

    /// <summary>Crea el usuario (habilitado, con contrasena temporal y atributo de compania). Devuelve su id ('sub').</summary>
    public async Task<string> CrearUsuarioAsync(string username, string nombres, string apellidos, string correo, string codigoCompania, string passwordTemporal)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["username"] = username, ["email"] = correo, ["firstName"] = nombres, ["lastName"] = apellidos,
            ["enabled"] = true, ["emailVerified"] = false,
            ["attributes"] = new Dictionary<string, string[]> { [_atributoCompania] = [codigoCompania] },
            ["credentials"] = new[] { new { type = "password", value = passwordTemporal, temporary = true } },
        };
        using var r = await EnviarAsync(HttpMethod.Post, "users", cuerpo);
        if (r.StatusCode == HttpStatusCode.Conflict)
            throw new KeycloakAdminException("usuario_existente", "Ya existe un usuario o correo con esos datos en Keycloak.", 409);
        if (r.StatusCode != HttpStatusCode.Created) throw await ErrorAsync(r, "crear el usuario");

        var ubicacion = r.Headers.Location?.ToString();
        var id = ubicacion?[(ubicacion.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(id))
            throw new KeycloakAdminException("keycloak_error", "Keycloak creo el usuario pero no devolvio su id.");

        // Verifica que Keycloak conservo el atributo de compania: sin el, el token no trae el tenant y el usuario no puede operar.
        var guardado = false;
        try
        {
            var u = await ObtenerAsync(id);
            guardado = u["attributes"]?[_atributoCompania] is JsonArray a && a.Any(x => x?.GetValue<string>() == codigoCompania);
        }
        catch { /* si no se puede verificar, se trata como no guardado */ }
        if (!guardado)
        {
            await EliminarSilenciosoAsync(id);   // no dejar un usuario inutilizable
            throw new KeycloakAdminException("atributo_compania_no_guardado",
                $"Keycloak no guardo el atributo '{_atributoCompania}'. En el realm, Realm settings > General > 'Unmanaged Attributes' debe estar en Enabled. No se creo el usuario.", 502);
        }
        return id;
    }

    private async Task<JsonObject> ObtenerAsync(string id)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"users/{id}");
        if (r.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException("usuario_no_existe_en_keycloak", "El usuario no existe en Keycloak (¿fue eliminado alla?).", 409);
        if (!r.IsSuccessStatusCode) throw await ErrorAsync(r, "consultar el usuario");
        return JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();
    }

    private async Task ActualizarAsync(string id, JsonObject representacion)
    {
        using var r = await EnviarAsync(HttpMethod.Put, $"users/{id}", representacion);
        if (r.StatusCode == HttpStatusCode.Conflict)
            throw new KeycloakAdminException("usuario_existente", "Ese correo ya esta en uso por otro usuario de Keycloak.", 409);
        if (!r.IsSuccessStatusCode) throw await ErrorAsync(r, "actualizar el usuario");
    }

    public async Task ActualizarDatosAsync(string id, string nombres, string apellidos, string correo)
    {
        var u = await ObtenerAsync(id);
        u["firstName"] = nombres; u["lastName"] = apellidos; u["email"] = correo;
        await ActualizarAsync(id, u);
    }

    /// <summary>Habilita o deshabilita el usuario; al deshabilitar cierra sus sesiones abiertas.</summary>
    public async Task EstablecerHabilitadoAsync(string id, bool habilitado)
    {
        var u = await ObtenerAsync(id);
        u["enabled"] = habilitado;
        await ActualizarAsync(id, u);
        if (!habilitado)
        {
            using var r = await EnviarAsync(HttpMethod.Post, $"users/{id}/logout");
            // Si no se pudieron cerrar sesiones, el usuario igual queda deshabilitado (y la API lo rechaza por estado).
        }
    }

    public async Task RestablecerPasswordAsync(string id, string passwordTemporal)
    {
        using var r = await EnviarAsync(HttpMethod.Put, $"users/{id}/reset-password",
            new { type = "password", value = passwordTemporal, temporary = true });
        if (!r.IsSuccessStatusCode) throw await ErrorAsync(r, "restablecer la contrasena");
    }

    // ----- Clientes de servicio (usuarios de integracion) -------------------------------------------------
    // Requiere ademas los roles de realm-management: manage-clients, view-clients y query-clients.

    /// <summary>
    /// Crea un client confidencial solo para Client Credentials (sin login de personas) con un mapper que agrega al token el claim
    /// 'compania' fijo. Devuelve el id interno de Keycloak y el secret generado.
    /// </summary>
    public async Task<(string IdInterno, string Secret)> CrearClienteServicioAsync(string clientId, string nombre, string codigoCompania)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["clientId"] = clientId, ["name"] = nombre, ["description"] = $"Integracion del Core Bancario (compania {codigoCompania})",
            ["protocol"] = "openid-connect", ["enabled"] = true, ["publicClient"] = false,
            ["clientAuthenticatorType"] = "client-secret", ["serviceAccountsEnabled"] = true,
            ["standardFlowEnabled"] = false, ["implicitFlowEnabled"] = false, ["directAccessGrantsEnabled"] = false,
            ["protocolMappers"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = _atributoCompania, ["protocol"] = "openid-connect", ["protocolMapper"] = "oidc-hardcoded-claim-mapper",
                    ["config"] = new Dictionary<string, string>
                    {
                        ["claim.name"] = _atributoCompania, ["claim.value"] = codigoCompania, ["jsonType.label"] = "String",
                        ["access.token.claim"] = "true", ["id.token.claim"] = "false", ["userinfo.token.claim"] = "false",
                    }
                }
            }
        };
        using var r = await EnviarAsync(HttpMethod.Post, "clients", cuerpo);
        if (r.StatusCode == HttpStatusCode.Conflict)
            throw new KeycloakAdminException("cliente_existente", "Ya existe un client con ese identificador en Keycloak.", 409);
        if (r.StatusCode != HttpStatusCode.Created) throw await ErrorAsync(r, "crear el client de la integracion");

        var ubicacion = r.Headers.Location?.ToString();
        var id = ubicacion?[(ubicacion.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(id)) throw new KeycloakAdminException("keycloak_error", "Keycloak creo el client pero no devolvio su id.");
        try { return (id, await ObtenerSecretAsync(id)); }
        catch { await EliminarClienteSilenciosoAsync(id); throw; }
    }

    private async Task<string> ObtenerSecretAsync(string idInterno)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"clients/{idInterno}/client-secret");
        if (!r.IsSuccessStatusCode) throw await ErrorAsync(r, "leer el secret del client");
        return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["value"]?.GetValue<string>()
            ?? throw new KeycloakAdminException("keycloak_error", "Keycloak no devolvio el secret del client.");
    }

    /// <summary>Id interno del client a partir de su clientId; null si no existe.</summary>
    public async Task<string?> BuscarClienteAsync(string clientId)
    {
        using var r = await EnviarAsync(HttpMethod.Get, $"clients?clientId={Uri.EscapeDataString(clientId)}");
        if (!r.IsSuccessStatusCode) throw await ErrorAsync(r, "buscar el client");
        var arr = JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsArray();
        return arr.Count == 0 ? null : arr[0]!["id"]!.GetValue<string>();
    }

    private async Task<string> ExigirClienteAsync(string clientId) =>
        await BuscarClienteAsync(clientId)
        ?? throw new KeycloakAdminException("cliente_no_existe_en_keycloak", $"El client '{clientId}' no existe en Keycloak (¿fue eliminado alla?).", 409);

    /// <summary>Genera un secret nuevo; el anterior deja de funcionar de inmediato.</summary>
    public async Task<string> RegenerarSecretClienteAsync(string clientId)
    {
        var id = await ExigirClienteAsync(clientId);
        using var r = await EnviarAsync(HttpMethod.Post, $"clients/{id}/client-secret");
        if (!r.IsSuccessStatusCode) throw await ErrorAsync(r, "regenerar el secret");
        return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["value"]?.GetValue<string>()
            ?? throw new KeycloakAdminException("keycloak_error", "Keycloak no devolvio el secret nuevo.");
    }

    public async Task EstablecerClienteHabilitadoAsync(string clientId, bool habilitado)
    {
        var id = await ExigirClienteAsync(clientId);
        using var g = await EnviarAsync(HttpMethod.Get, $"clients/{id}");
        if (!g.IsSuccessStatusCode) throw await ErrorAsync(g, "consultar el client");
        var c = JsonNode.Parse(await g.Content.ReadAsStringAsync())!.AsObject();
        c["enabled"] = habilitado;
        using var r = await EnviarAsync(HttpMethod.Put, $"clients/{id}", c);
        if (!r.IsSuccessStatusCode) throw await ErrorAsync(r, "actualizar el client");
    }

    public async Task EliminarClienteSilenciosoAsync(string idInterno)
    {
        try { using var r = await EnviarAsync(HttpMethod.Delete, $"clients/{idInterno}"); }
        catch { /* mejor esfuerzo */ }
    }

    /// <summary>Compensacion: elimina un usuario recien creado cuando falla el registro local.</summary>
    public async Task EliminarSilenciosoAsync(string id)
    {
        try { using var r = await EnviarAsync(HttpMethod.Delete, $"users/{id}"); }
        catch { /* mejor esfuerzo */ }
    }
}
