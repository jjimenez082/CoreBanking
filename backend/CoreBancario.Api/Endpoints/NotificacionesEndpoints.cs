using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;

namespace CoreBancario.Api.Endpoints;

/// <summary>Notificaciones del usuario autenticado (campanita). Solo ve las suyas.</summary>
public static class NotificacionesEndpoints
{
    public static void MapNotificacionesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notificaciones", async (ActorContext actor, NotificacionesRepositorio repo, bool? historial)
            => Results.Ok(await repo.ListarAsync(actor, historial ?? false))).RequireAuthorization();

        // Estado "leida" por usuario (vale en cualquier equipo).
        app.MapGet("/api/notificaciones/leidas", async (ActorContext actor, NotificacionesRepositorio repo)
            => Results.Ok(await repo.LeidasAsync(actor))).RequireAuthorization();
        app.MapPost("/api/notificaciones/leidas", async (ClavesRequest req, ActorContext actor, NotificacionesRepositorio repo) =>
        { await repo.MarcarAsync(actor, req.Claves); return Results.NoContent(); }).RequireAuthorization();
        app.MapPost("/api/notificaciones/no-leidas", async (ClavesRequest req, ActorContext actor, NotificacionesRepositorio repo) =>
        { await repo.DesmarcarAsync(actor, req.Claves); return Results.NoContent(); }).RequireAuthorization();
    }
}

public record ClavesRequest(IReadOnlyList<string>? Claves);
