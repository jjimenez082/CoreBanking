using CoreBancario.Api.Datos;
using CoreBancario.Api.Endpoints;
using CoreBancario.Api.Infraestructura;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

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
builder.Services.AddScoped<AuditoriaConsultaRepositorio>(); // consulta de auditoria
builder.Services.AddScoped<IntegracionRepositorio>();    // usuarios de integracion (canal fijo)
builder.Services.AddScoped<DivisionRepositorio>();         // Division territorial y direcciones por pais
builder.Services.AddScoped<NumeracionRepositorio>();     // Administracion de rangos de numeracion

var app = builder.Build();

// En Desarrollo, devuelve la traza completa del error en la respuesta HTTP
// (mas comodo para depurar que buscarla solo en la consola del servidor).
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
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

app.MapInterlocutorEndpoints();
app.MapGestionEndpoints();
app.MapYoEndpoints();
app.MapSeguridadEndpoints();
app.MapListasValoresEndpoints();
app.MapCatalogosEndpoints();
app.MapNotificacionesEndpoints();
app.MapNumeracionEndpoints();
app.MapDivisionEndpoints();
app.MapOrganizacionEndpoints();
app.MapUsuariosEndpoints();
app.MapAdminTransversalEndpoints();

app.Run();
