using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Expone la identidad y los permisos efectivos (RBAC, SEG-002) del actor
/// autenticado, para que el Backoffice (u otro consumidor) sepa que mostrar
/// en la UI sin tener que adivinarlo o duplicar la logica de permisos.
/// </summary>
public static class YoEndpoints
{
    public static void MapYoEndpoints(this WebApplication app)
    {
        app.MapGet("/api/yo", ObtenerYoAsync).RequireAuthorization();
    }

    private static IResult ObtenerYoAsync(ActorContext actorContext)
    {
        return Results.Ok(new YoResponse(
            actorContext.NombreDescriptivo,
            actorContext.EsUsuarioHumano,
            actorContext.CodigosPermiso));
    }
}
