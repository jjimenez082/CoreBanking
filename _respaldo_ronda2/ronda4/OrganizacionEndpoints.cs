using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Estructura organizativa (ORG_ADMINISTRACION) y catalogo de unidades activas para combos.</summary>
public static class OrganizacionEndpoints
{
    public static void MapOrganizacionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/catalogos/unidades", async (ActorContext actor, UnidadRepositorio repo) =>
            Results.Ok(await repo.ListarActivasAsync(actor))).RequireAuthorization();

        app.MapGet("/api/catalogos/usuarios", async (ActorContext actor, UnidadRepositorio repo) =>
            Results.Ok(await repo.ListarUsuariosActivosAsync(actor.IdCompania))).RequireAuthorization();

        var g = app.MapGroup("/api/organizacion/unidades")
            .RequireAuthorization()
            .RequierePermiso(UnidadRepositorio.ObjetoAdministracion);

        g.MapGet("/", async (ActorContext actor, UnidadRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania)));

        g.MapPost("/", async (UnidadGuardarRequest req, ActorContext actor, UnidadRepositorio repo) =>
        {
            var r = await repo.CrearAsync(actor, req);
            return r.Exito ? Results.Created($"/api/organizacion/unidades/{r.Id}", new { idUnidad = r.Id }) : Error(r);
        });

        g.MapPut("/{idUnidad:long}", async (long idUnidad, UnidadGuardarRequest req, ActorContext actor, UnidadRepositorio repo) =>
        {
            var r = await repo.EditarAsync(actor, idUnidad, req);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        g.MapPut("/{idUnidad:long}/estado", async (long idUnidad, UnidadEstadoRequest req, ActorContext actor, UnidadRepositorio repo) =>
        {
            var r = await repo.CambiarEstadoAsync(actor, idUnidad, req.Estado);
            return r.Exito ? Results.NoContent() : Error(r);
        });
    }

    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
