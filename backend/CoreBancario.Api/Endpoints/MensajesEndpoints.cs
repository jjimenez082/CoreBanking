using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Administracion del maestro de mensajes. Exige MSG_ADMINISTRACION.</summary>
public static class MensajesEndpoints
{
    public static void MapMensajesEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/mensajes").RequireAuthorization().RequierePermiso(MensajesRepositorio.ObjetoAdministracion);

        grupo.MapGet("/", async (ActorContext actor, MensajesRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania)));

        grupo.MapPut("/{idMensaje:long}", async (long idMensaje, MensajeEditarRequest request, ActorContext actor, MensajesRepositorio repo) =>
        {
            var r = await repo.EditarAsync(actor, idMensaje, request.Texto, restaurar: false);
            return r.Exito ? Results.NoContent() : Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
        });

        grupo.MapPost("/{idMensaje:long}/restaurar", async (long idMensaje, ActorContext actor, MensajesRepositorio repo) =>
        {
            var r = await repo.EditarAsync(actor, idMensaje, null, restaurar: true);
            return r.Exito ? Results.NoContent() : Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
        });
    }
}
