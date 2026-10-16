using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Administracion de rangos de numeracion. Exige NUM_ADMINISTRACION.</summary>
public static class NumeracionEndpoints
{
    public static void MapNumeracionEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/numeracion")
            .RequireAuthorization()
            .RequierePermiso(NumeracionRepositorio.ObjetoAdministracion);

        grupo.MapGet("/rangos", async (ActorContext actor, NumeracionRepositorio repo) =>
            Results.Ok(await repo.ListarAsync(actor.IdCompania)));

        grupo.MapPost("/rangos", async (RangoNumeracionCrearRequest request, ActorContext actor, NumeracionRepositorio repo) =>
        {
            var r = await repo.CrearAsync(actor, request);
            return r.Exito ? Results.Created($"/api/numeracion/rangos/{r.Id}", new { idRangoNumeracion = r.Id }) : Error(r);
        });

        grupo.MapPut("/rangos/{idRango:long}", async (long idRango, RangoNumeracionEditarRequest request, ActorContext actor, NumeracionRepositorio repo) =>
        {
            var r = await repo.EditarAsync(actor, idRango, request);
            return r.Exito ? Results.NoContent() : Error(r);
        });
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
