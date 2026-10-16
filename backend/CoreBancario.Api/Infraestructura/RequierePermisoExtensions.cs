namespace CoreBancario.Api.Infraestructura;

/// <summary>
/// Endpoint filter que exige un codigo de sec.OBJETO_AUTORIZACION especifico
/// (RBAC, SEG-002). Corre despues de ActorResolutionMiddleware, que ya dejo
/// ActorContext poblado (incluyendo CodigosPermiso) para este request.
/// </summary>
public class RequierePermisoFiltro : IEndpointFilter
{
    private readonly string _codigoPermiso;

    public RequierePermisoFiltro(string codigoPermiso)
    {
        _codigoPermiso = codigoPermiso;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var actorContext = context.HttpContext.RequestServices.GetRequiredService<ActorContext>();

        if (!actorContext.Resuelto)
        {
            // No deberia ocurrir (ActorResolutionMiddleware corre antes del
            // endpoint), pero se niega por defecto como defensa en profundidad.
            return Results.Forbid();
        }

        if (!actorContext.TienePermiso(_codigoPermiso))
        {
            return Results.Json(
                new
                {
                    error = "permiso_denegado",
                    mensaje = $"No tienes el permiso requerido ({_codigoPermiso}) para esta accion.",
                    codigoPermiso = _codigoPermiso
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

public static class RequierePermisoExtensions
{
    /// <summary>
    /// Ejemplo de uso en un endpoint individual:
    ///   grupo.MapPost("/", AltaAsync).RequierePermiso("CLI_ALTA");
    /// o en un grupo completo (aplica a todos los endpoints mapeados en el):
    ///   var grupo = app.MapGroup("/api/seguridad").RequierePermiso("SEG_ADMINISTRACION");
    /// Generico sobre TBuilder (igual que AddEndpointFilter del framework) para
    /// que funcione tanto sobre RouteHandlerBuilder como sobre RouteGroupBuilder.
    /// </summary>
    public static TBuilder RequierePermiso<TBuilder>(this TBuilder builder, string codigoPermiso)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new RequierePermisoFiltro(codigoPermiso));
        return builder;
    }
}
