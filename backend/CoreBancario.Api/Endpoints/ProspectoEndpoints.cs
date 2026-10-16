using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

public static class ProspectoEndpoints
{
    public static void MapProspectoEndpoints(this WebApplication app)
    {
        // Validacion previa a cualquier alta (prospecto o rol): ¿la persona ya existe / ya es cliente?
        app.MapGet("/api/interlocutores/validar-identificacion", ValidarAsync)
            .RequireAuthorization().RequierePermiso("CLI_CONSULTA");

        var g = app.MapGroup("/api/prospectos").RequireAuthorization();
        g.MapGet("/", ListarAsync).RequierePermiso("PRO_CONSULTA");
        g.MapGet("/{id:long}", ObtenerAsync).RequierePermiso("PRO_CONSULTA");
        g.MapPost("/", CrearAsync).RequierePermiso("PRO_ALTA");
        g.MapPut("/{id:long}", EditarAsync).RequierePermiso("PRO_EDICION");
        g.MapPut("/{id:long}/estado", EstadoAsync).RequierePermiso("PRO_EDICION");
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);

    private static async Task<IResult> ValidarAsync(long idTipo, string? numero, ActorContext actor, ProspectoRepositorio repo)
        => string.IsNullOrWhiteSpace(numero)
            ? Results.BadRequest(new { error = "datos_invalidos", mensaje = "Indique el numero de identificacion." })
            : Results.Ok(await repo.ValidarAsync(actor.IdCompania, idTipo, numero));

    private static async Task<IResult> ListarAsync(string? estado, ActorContext actor, ProspectoRepositorio repo)
        => Results.Ok(await repo.ListarAsync(actor.IdCompania, estado));

    private static async Task<IResult> ObtenerAsync(long id, ActorContext actor, ProspectoRepositorio repo)
    {
        var p = await repo.ObtenerAsync(actor.IdCompania, id);
        return p is null ? Results.NotFound(new { error = "no_encontrado", id }) : Results.Ok(p);
    }

    private static async Task<IResult> CrearAsync(ProspectoCrearRequest req, ActorContext actor, ProspectoRepositorio repo)
    {
        var r = await repo.CrearAsync(actor, req);
        return r.Exito ? Results.Created($"/api/prospectos/{r.Id}", await repo.ObtenerAsync(actor.IdCompania, r.Id!.Value)) : Error(r);
    }

    private static async Task<IResult> EditarAsync(long id, ProspectoEditarRequest req, ActorContext actor, ProspectoRepositorio repo)
    {
        var r = await repo.EditarAsync(actor, id, req);
        return r.Exito ? Results.Ok(await repo.ObtenerAsync(actor.IdCompania, id)) : Error(r);
    }

    private static async Task<IResult> EstadoAsync(long id, ProspectoEstadoRequest req, ActorContext actor, ProspectoRepositorio repo)
    {
        var r = await repo.CambiarEstadoAsync(actor, id, req.Estado);
        return r.Exito ? Results.Ok(await repo.ObtenerAsync(actor.IdCompania, id)) : Error(r);
    }
}
