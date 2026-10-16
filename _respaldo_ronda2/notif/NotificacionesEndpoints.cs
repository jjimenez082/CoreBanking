using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;

namespace CoreBancario.Api.Endpoints;

/// <summary>Notificaciones del usuario autenticado (campanita). Solo ve las suyas.</summary>
public static class NotificacionesEndpoints
{
    public static void MapNotificacionesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notificaciones", async (ActorContext actor, NotificacionesRepositorio repo)
            => Results.Ok(await repo.ListarAsync(actor))).RequireAuthorization();
    }
}
