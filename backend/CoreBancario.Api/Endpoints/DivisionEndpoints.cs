using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Estructura de direccion por pais y division territorial: catalogos (autenticado) y administracion (DIV_ADMINISTRACION).</summary>
public static class DivisionEndpoints
{
    public static void MapDivisionEndpoints(this WebApplication app)
    {
        // Catalogos para los combos del wizard
        var cat = app.MapGroup("/api/catalogos/direccion").RequireAuthorization();

        cat.MapGet("/config", async (long idPais, DivisionRepositorio repo) =>
        {
            var cfg = await repo.ObtenerConfigAsync(idPais);
            return cfg is null ? Results.NotFound(new { error = "pais_no_encontrado", mensaje = "El pais no existe." }) : Results.Ok(cfg);
        });

        cat.MapGet("/divisiones", async (long idPais, int nivel, long? idPadre, DivisionRepositorio repo) =>
            Results.Ok(await repo.ListarActivasAsync(idPais, nivel, idPadre)));

        // Administracion
        var adm = app.MapGroup("/api/division-territorial")
            .RequireAuthorization()
            .RequierePermiso(DivisionRepositorio.ObjetoAdministracion);

        adm.MapGet("/paises/{idPais:long}/config", async (long idPais, DivisionRepositorio repo) =>
        {
            var cfg = await repo.ObtenerConfigAsync(idPais);
            return cfg is null ? Results.NotFound(new { error = "pais_no_encontrado", mensaje = "El pais no existe." }) : Results.Ok(cfg);
        });

        adm.MapPut("/paises/{idPais:long}/config", async (long idPais, DireccionConfigGuardarRequest request, ActorContext actor, DivisionRepositorio repo) =>
        {
            var r = await repo.GuardarConfigAsync(actor, idPais, request);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        adm.MapGet("/divisiones", async (long idPais, int? nivel, long? idPadre, string? q, DivisionRepositorio repo) =>
            Results.Ok(await repo.ListarAsync(idPais, nivel, idPadre, q)));

        adm.MapPost("/divisiones", async (DivisionCrearRequest request, ActorContext actor, DivisionRepositorio repo) =>
        {
            var r = await repo.CrearAsync(actor, request);
            return r.Exito ? Results.Created($"/api/division-territorial/divisiones/{r.Id}", new { idDivision = r.Id }) : Error(r);
        });

        adm.MapPut("/divisiones/{idDivision:long}", async (long idDivision, DivisionEditarRequest request, ActorContext actor, DivisionRepositorio repo) =>
        {
            var r = await repo.EditarAsync(actor, idDivision, request);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        adm.MapPost("/importar", async (DivisionImportRequest request, ActorContext actor, DivisionRepositorio repo) =>
        {
            var (r, detalle) = await repo.ImportarAsync(actor, request);
            return r.Exito ? Results.Ok(detalle) : Error(r);
        });
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
