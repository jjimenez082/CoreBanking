using CoreBancario.Api.Datos;
using CoreBancario.Api.Endpoints;
using CoreBancario.Api.Infraestructura;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Autenticacion: JWT Bearer contra Keycloak (o cualquier IdP OIDC/JWKS,
// SEG-009). El core NUNCA depende de Keycloak como tal, solo de que el
// IdP cumpla OAuth2/OIDC estandar.
// -----------------------------------------------------------------------
var authority = builder.Configuration["Keycloak:Authority"]
    ?? throw new InvalidOperationException("Falta Keycloak:Authority en la configuracion.");
var requireHttps = builder.Configuration.GetValue("Keycloak:RequireHttpsMetadata", true);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.RequireHttpsMetadata = requireHttps;

        // Sin esto, .NET remapea claims estandar (ej. 'sub' -> un nombre largo
        // de ClaimTypes.NameIdentifier) y el codigo que busca "sub"/"azp"
        // literalmente en TenantResolutionMiddleware/ActorResolutionMiddleware
        // nunca los encuentra.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // El realm de Keycloak es el emisor (issuer) esperado.
            ValidateIssuer = true,
            ValidIssuer = authority,

            // MVP: no se valida audience para no atarse a un client_id fijo
            // (frontend y backend usan clients distintos). Si se requiere
            // mayor rigor, fijar ValidAudiences por configuracion.
            ValidateAudience = false,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// Los numeros pueden llegar como texto ("1.5") desde formularios; se aceptan en ambos formatos.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString);

// -----------------------------------------------------------------------
// OpenAPI / Swagger (solo Development, ver mas abajo). Define el boton "Authorize"
// para pegar el token Bearer de Keycloak.
// -----------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Core Bancario API", Version = "v1", Description = "API del Core Bancario (clientes, aprobaciones y administracion). Autenticacion: JWT de Keycloak." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Pegue el access token de Keycloak (sin la palabra Bearer)."
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
    // Agrupa por modulo: /api/interlocutores/... -> "interlocutores".
    o.TagActionsBy(api =>
    {
        var partes = (api.RelativePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        return new[] { partes.Length > 1 ? partes[1] : (partes.FirstOrDefault() ?? "general") };
    });
    // Evita choques de nombre entre records de distintos modulos.
    o.CustomSchemaIds(t => t.FullName?.Replace("CoreBancario.Api.Modelos.", "").Replace('+', '.'));
});

// -----------------------------------------------------------------------
// CORS: el Backoffice (React, http://localhost:3000) llama a esta API
// desde el navegador. Sin esto, el navegador bloquea las respuestas por
// politica de mismo origen aunque el backend responda 200 sin problema.
// La lista de origenes permitidos viene de configuracion (appsettings)
// para poder variar por ambiente sin tocar codigo.
// -----------------------------------------------------------------------
var origenesPermitidos = builder.Configuration
    .GetSection("Cors:OrigenesPermitidos")
    .Get<string[]>() ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(origenesPermitidos)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// -----------------------------------------------------------------------
// Servicios de infraestructura multi-tenant
// -----------------------------------------------------------------------
builder.Services.AddSingleton<DirectorioService>();   // sin estado por request, solo usa IConfiguration
builder.Services.AddSingleton<NumeracionServicio>();   // motor de numeracion: cache de bloques en memoria
builder.Services.AddScoped<TenantContext>();          // uno por request, lo llena TenantResolutionMiddleware
builder.Services.AddScoped<ActorContext>();           // uno por request, lo llena ActorResolutionMiddleware

// -----------------------------------------------------------------------
// Repositorios (Dapper), Scoped porque dependen de TenantContext
// -----------------------------------------------------------------------
builder.Services.AddScoped<InterlocutorRepositorio>();
builder.Services.AddScoped<ProspectoRepositorio>();
builder.Services.AddScoped<CoincidenciasRepositorio>();
builder.Services.AddScoped<GestionRepositorio>();
builder.Services.AddScoped<AutorizacionRepositorio>(); // RBAC (SEG-002), usado por ActorResolutionMiddleware
builder.Services.AddScoped<SeguridadRepositorio>();     // Mantenimiento de roles/usuarios (modulo de Seguridad)
builder.Services.AddScoped<ListasValoresRepositorio>(); // Mantenimiento de Listas de Valores (Administracion)
builder.Services.AddScoped<CatalogosRepositorio>();     // Catalogos de solo lectura para combo boxes
builder.Services.AddScoped<NotificacionesRepositorio>(); // Campanita de notificaciones
builder.Services.AddHttpClient<KeycloakAdminServicio>();      // Admin API de Keycloak (alta/baja de usuarios)
builder.Services.AddScoped<UsuarioRepositorio>();
builder.Services.AddScoped<UnidadRepositorio>();
builder.Services.AddScoped<MonedaRepositorio>();         // Administracion transversal: monedas
builder.Services.AddScoped<TipoCambioRepositorio>();     // tipo de cambio (carga por gestion)
builder.Services.AddScoped<FeriadoRepositorio>();        // calendario de feriados / dias habiles
builder.Services.AddScoped<ParametroRepositorio>();      // parametros generales
builder.Services.AddScoped<RestriccionBloqueoRepositorio>();   // acciones que impide cada tipo de bloqueo
builder.Services.AddScoped<AuditoriaConsultaRepositorio>(); // consulta de auditoria
builder.Services.AddScoped<IntegracionRepositorio>();    // usuarios de integracion (canal fijo)
builder.Services.AddScoped<DivisionRepositorio>();         // Division territorial y direcciones por pais
builder.Services.AddScoped<ReasignacionRepositorio>();    // cambio de asignacion de aprobador
builder.Services.AddScoped<ReglaAprobacionRepositorio>(); // reglas de aprobacion (Administracion)
builder.Services.AddScoped<GrupoUsuariosRepositorio>();   // grupos de distribucion
builder.Services.AddScoped<NumeracionRepositorio>();     // Administracion de rangos de numeracion
builder.Services.AddScoped<MensajesRepositorio>();       // Maestro de mensajes de la API
builder.Services.AddScoped<RolRequisitoRepositorio>();    // roles del interlocutor y requisitos por rol
builder.Services.AddSingleton<IAlmacenDocumentos, AlmacenDiscoCifrado>();   // almacen de documentos: disco cifrado (intercambiable)
builder.Services.AddScoped<PerfilDatosAdminRepositorio>();  // perfiles de datos y mascaras (administracion)
builder.Services.AddScoped<PerfilDatosServicio>();          // perfil de datos efectivo del usuario
builder.Services.AddScoped<DocumentoAdminRepositorio>();  // administracion de carpetas, tipos y documentos por rol
builder.Services.AddScoped<DocumentoRepositorio>();       // gestor documental (adjuntos)
builder.Services.AddScoped<IdempotenciaRepositorio>();   // Idempotency-Key de las altas por API

var app = builder.Build();

// En Desarrollo, devuelve la traza completa del error en la respuesta HTTP
// (mas comodo para depurar que buscarla solo en la consola del servidor).
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Swagger solo en Development; se registra antes de la autenticacion y del tenant porque no lleva token.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Core Bancario API v1"));
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", servicio = "CoreBancario.Api" }));

app.UseCors();

// Orden del pipeline (DAD Seccion 6.4):
// 1) Autenticacion/autorizacion -> valida el JWT contra el JWKS del IdP.
// 2) TenantResolutionMiddleware -> claim 'compania' -> dir.CLIENTE_SAAS -> TenantContext.
// 3) ActorResolutionMiddleware  -> 'sub'/'azp' -> sec.USUARIO(_INTEGRACION) -> ActorContext.
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<ActorResolutionMiddleware>();

// Adjuntos (r44a): X-Adjuntos = ids de documentos temporales ya subidos que acompanan la solicitud; se vinculan a la gestion al crearla.
app.Use(async (contexto, siguiente) =>
{
    var cabecera = contexto.Request.Headers["X-Adjuntos"].ToString();
    if (!string.IsNullOrWhiteSpace(cabecera))
    {
        var ids = cabecera.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : 0L)
            .Where(id => id > 0);
        contexto.RequestServices.GetRequiredService<ActorContext>().EstablecerAdjuntosTemporales(ids);
    }
    await siguiente();
});

// Perfiles de datos: un cliente fuera de las filas del perfil del usuario no existe para el: 404 en toda ruta /api/interlocutores/{id}/...
app.Use(async (contexto, siguiente) =>
{
    var ruta = contexto.Request.Path.Value ?? "";
    var m = System.Text.RegularExpressions.Regex.Match(ruta, @"^/api/interlocutores/(\d+)(/|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (m.Success && contexto.User.Identity?.IsAuthenticated == true && long.TryParse(m.Groups[1].Value, out var idCliente))
    {
        var servicio = contexto.RequestServices.GetRequiredService<PerfilDatosServicio>();
        if (!await servicio.PuedeVerClienteAsync(idCliente))
        {
            contexto.Response.StatusCode = StatusCodes.Status404NotFound;
            await contexto.Response.WriteAsJsonAsync(new { error = "no_encontrado", idInterlocutor = idCliente });
            return;
        }
    }
    await siguiente();
});

app.MapInterlocutorEndpoints();
app.MapProspectoEndpoints();
app.MapCoincidenciaEndpoints();
app.MapFusionEndpoints();
app.MapRolEndpoints();
app.MapRelacionEndpoints();
app.MapGestionEndpoints();
app.MapYoEndpoints();
app.MapSeguridadEndpoints();
app.MapListasValoresEndpoints();
app.MapCatalogosEndpoints();
app.MapNotificacionesEndpoints();
app.MapNumeracionEndpoints();
app.MapMensajesEndpoints();
app.MapDivisionEndpoints();
app.MapOrganizacionEndpoints();
app.MapUsuariosEndpoints();
app.MapPerfilDatosEndpoints();
app.MapAdminTransversalEndpoints();
app.MapAprobacionAdminEndpoints();
app.MapDocumentoEndpoints();

app.Run();
