using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Gestiones (aprobaciones). Listar/consultar basta con estar autenticado y
/// ser de la misma compania; aprobar/rechazar exige GES_APROBAR. Quien solicita
/// el cambio no puede aprobarlo (salvo Gestiones:PermitirAutoaprobacion=true,
/// pensado solo para pruebas en DEV con un unico usuario).
/// </summary>
public static class GestionEndpoints
{
    public static void MapGestionEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/gestiones").RequireAuthorization();

        grupo.MapGet("/", ListarAsync);
        grupo.MapGet("/pendientes", ListarPendientesAsync);
        grupo.MapGet("/{idGestion:long}", ObtenerAsync);
        grupo.MapPost("/{idGestion:long}/aprobar", AprobarAsync).RequierePermiso("GES_APROBAR");
        grupo.MapPost("/{idGestion:long}/rechazar", RechazarAsync).RequierePermiso("GES_APROBAR");
    }

    private static async Task<IResult> ListarPendientesAsync(ActorContext actor, GestionRepositorio repo)
        => Results.Ok(await repo.ListarPendientesAsync(actor.IdCompania));

    /// <summary>?estado=pendientes (default) | todas</summary>
    private static async Task<IResult> ListarAsync(string? estado, ActorContext actor, GestionRepositorio repo)
        => Results.Ok(await repo.ListarAsync(actor.IdCompania, soloPendientes: !string.Equals(estado, "todas", StringComparison.OrdinalIgnoreCase)));

    private static async Task<IResult> ObtenerAsync(long idGestion, ActorContext actor, GestionRepositorio repo)
    {
        var g = await repo.ObtenerDetalleAsync(actor.IdCompania, idGestion);
        return g is null ? Results.NotFound(new { error = "gestion_no_encontrada", idGestion }) : Results.Ok(g);
    }

    private static async Task<IResult> AprobarAsync(
        long idGestion, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var permitirAuto = config.GetValue("Gestiones:PermitirAutoaprobacion", false);
        var porSuperior = config.GetValue("Gestiones:AprobacionPorSuperior", true);
        var r = await repo.AprobarAsync(actor, idGestion, permitirAuto, porSuperior);
        return r.Exito
            ? Results.Ok(await repo.ObtenerDetalleAsync(actor.IdCompania, idGestion))
            : Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
    }

    private static async Task<IResult> RechazarAsync(
        long idGestion, GestionRechazarRequest request, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var porSuperior = config.GetValue("Gestiones:AprobacionPorSuperior", true);
        var permitirAuto = config.GetValue("Gestiones:PermitirAutoaprobacion", false);
        var r = await repo.RechazarAsync(actor, idGestion, request.Motivo, porSuperior, permitirAuto);
        return r.Exito
            ? Results.Ok(await repo.ObtenerDetalleAsync(actor.IdCompania, idGestion))
            : Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
    }
}
