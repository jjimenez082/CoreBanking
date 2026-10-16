using System.Security.Claims;

namespace CoreBancario.Api.Infraestructura;

/// <summary>
/// Primer eslabon del pipeline multi-tenant (DAD Seccion 6.4).
/// Toma el claim 'compania' del JWT ya validado, lo busca en dir.CLIENTE_SAAS
/// y arma la cadena de conexion del tenant en el TenantContext (Scoped) para
/// que el resto del request (middlewares, repositorios) la use.
///
/// Debe registrarse DESPUES de UseAuthentication()/UseAuthorization() y ANTES
/// de ActorResolutionMiddleware y de cualquier endpoint de negocio.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private const string ClaimCompania = "compania";

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, DirectorioService directorioService, TenantContext tenantContext)
    {
        // Rutas publicas (health check, etc.) no requieren tenant.
        if (EsRutaPublica(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            // No deberia llegar aqui si UseAuthorization esta bien configurado,
            // pero se deja como defensa en profundidad.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var codigoCliente = context.User.FindFirstValue(ClaimCompania);

        if (string.IsNullOrWhiteSpace(codigoCliente))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "token_sin_compania",
                mensaje = "El token no trae el claim 'compania'; no es posible resolver el tenant."
            });
            return;
        }

        var cliente = await directorioService.BuscarClienteActivoAsync(codigoCliente);

        if (cliente is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "cliente_no_encontrado_o_inactivo",
                mensaje = $"El cliente '{codigoCliente}' no existe en dir.CLIENTE_SAAS o no esta Activo."
            });
            return;
        }

        var connectionString = directorioService.ArmarConnectionStringTenant(cliente.NombreBaseDatos);
        tenantContext.Establecer(cliente.CodigoCliente, cliente.NombreBaseDatos, connectionString);

        await _next(context);
    }

    private static bool EsRutaPublica(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger");
}
