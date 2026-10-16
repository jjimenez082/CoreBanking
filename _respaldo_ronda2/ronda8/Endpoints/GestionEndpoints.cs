using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Gestiones (aprobaciones). Listar/consultar basta con estar autenticado y
/// ser de la misma compania; aprobar/rechazar/devolver exige GES_APROBAR. Quien solicita
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
        grupo.MapPost("/{idGestion:long}/devolver", DevolverAsync).RequierePermiso("GES_APROBAR");
    }

    private static (bool PermitirAuto, bool PorSuperior) Reglas(IConfiguration config)
        => (config.GetValue("Gestiones:PermitirAutoaprobacion", false), config.GetValue("Gestiones:AprobacionPorSuperior", true));

    private static async Task<IResult> ListarPendientesAsync(ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var (auto, sup) = Reglas(config);
        return Results.Ok(await repo.ListarPendientesAsync(actor, auto, sup));
    }

    /// <summary>?estado=pendientes (default) | todas</summary>
    private static async Task<IResult> ListarAsync(string? estado, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var (auto, sup) = Reglas(config);
        return Results.Ok(await repo.ListarAsync(actor, soloPendientes: !string.Equals(estado, "todas", StringComparison.OrdinalIgnoreCase), auto, sup));
    }

    private static async Task<IResult> ObtenerAsync(long idGestion, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var (auto, sup) = Reglas(config);
        var g = await repo.ObtenerDetalleAsync(actor, idGestion, auto, sup);
        return g is null ? Results.NotFound(new { error = "gestion_no_encontrada", idGestion }) : Results.Ok(g);
    }

    private static async Task<IResult> Resultado(ResultadoOperacion r, long idGestion, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var (auto, sup) = Reglas(config);
        return r.Exito
            ? Results.Ok(await repo.ObtenerDetalleAsync(actor, idGestion, auto, sup))
            : Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
    }

    private static async Task<IResult> AprobarAsync(long idGestion, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var (auto, sup) = Reglas(config);
        return await Resultado(await repo.AprobarAsync(actor, idGestion, auto, sup), idGestion, actor, repo, config);
    }

    private static async Task<IResult> RechazarAsync(long idGestion, GestionRechazarRequest request, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var (auto, sup) = Reglas(config);
        return await Resultado(await repo.RechazarAsync(actor, idGestion, request.Motivo, sup, auto), idGestion, actor, repo, config);
    }

    private static async Task<IResult> DevolverAsync(long idGestion, GestionDevolverRequest request, ActorContext actor, GestionRepositorio repo, IConfiguration config)
    {
        var (auto, sup) = Reglas(config);
        return await Resultado(await repo.DevolverAsync(actor, idGestion, request.Comentario, sup, auto), idGestion, actor, repo, config);
    }
}
