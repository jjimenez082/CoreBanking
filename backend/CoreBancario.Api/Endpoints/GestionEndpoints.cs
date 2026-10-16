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
        // Integracion: estado de un alta que quedo como gestion (la solicito el mismo usuario tecnico).
        grupo.MapGet("/{numero}/estado", EstadoAltaApiAsync).RequierePermiso("CLI_ALTA");
        grupo.MapPost("/{idGestion:long}/aprobar", AprobarAsync).RequierePermiso("GES_APROBAR");
        grupo.MapPost("/{idGestion:long}/rechazar", RechazarAsync).RequierePermiso("GES_APROBAR");
        grupo.MapPost("/{idGestion:long}/devolver", DevolverAsync).RequierePermiso("GES_APROBAR");

        // App "Cambio de asignacion de aprobador" (permiso GES_REASIGNAR).
        var reasig = app.MapGroup("/api/gestiones/reasignacion").RequireAuthorization().RequierePermiso(ReasignacionRepositorio.Permiso);
        reasig.MapGet("/aprobadores", async (ActorContext actor, ReasignacionRepositorio repo) => Results.Ok(await repo.AprobadoresAsync(actor.IdCompania)));
        reasig.MapGet("/", async (long idUsuario, ActorContext actor, ReasignacionRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania, idUsuario)));
        reasig.MapPost("/", async (ReasignarRequest request, ActorContext actor, ReasignacionRepositorio repo, IConfiguration config) =>
            Results.Ok(await repo.ReasignarAsync(actor, request, config.GetValue("Gestiones:PermitirAutoaprobacion", false))));
    }

    private static async Task<IResult> EstadoAltaApiAsync(
        string numero, ActorContext actor, GestionRepositorio repo, MensajesRepositorio mensajes, HttpContext http)
    {
        var correlacion = http.Request.Headers["X-Correlation-Id"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(correlacion) || correlacion.Length > 64) correlacion = Guid.NewGuid().ToString("N");
        http.Response.Headers["X-Correlation-Id"] = correlacion;

        var e = await repo.EstadoAltaDelSolicitanteAsync(actor, numero);
        string nombre; string? numeroCliente = null;
        if (e is null) nombre = "GESTION_NO_ENCONTRADA";
        else if (e.Estado == "Aplicada" && e.IdCliente is not null)
        {
            nombre = "GESTION_APROBADA";
            numeroCliente = await repo.NumeroClienteAsync(e.IdCliente.Value);
        }
        else nombre = e.Estado switch
        {
            "Rechazada" => "GESTION_RECHAZADA",
            "Devuelta" or "Corregida" => "GESTION_DEVUELTA",
            _ => "GESTION_EN_PROCESO",
        };

        var m = await mensajes.ResolverAsync(actor.IdCompania, nombre, new Dictionary<string, string>
            { ["numeroGestion"] = e?.Numero ?? numero, ["numeroCliente"] = numeroCliente ?? "" })
            ?? await mensajes.ResolverAsync(actor.IdCompania, "ERROR_INTERNO")
            ?? new MensajesRepositorio.Resuelto("ER_SIS_001", "ERROR_INTERNO", "ERROR", "RECHAZADO", 500, "Ocurrio un error inesperado.");
        return Results.Json(new RespuestaAltaApi(m.Severidad, m.Resultado, m.Codigo, m.Nombre, m.Texto, correlacion,
            numeroCliente, numeroCliente is null ? null : e!.IdCliente, e?.Numero), statusCode: m.Http);
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

    private static async Task<IResult> ObtenerAsync(long idGestion, ActorContext actor, GestionRepositorio repo, IConfiguration config, NotificacionesRepositorio notificaciones)
    {
        var (auto, sup) = Reglas(config);
        var g = await repo.ObtenerDetalleAsync(actor, idGestion, auto, sup);
        if (g is not null) await notificaciones.MarcarGestionAsync(actor, idGestion);   // abrirla por cualquier camino marca sus avisos como leidos
        return g is null ? Results.NotFound(new { error = "gestion_no_encontrada", idGestion }) : Results.Ok(g);
    }

    private static async Task<IResult> Resultado(ResultadoOperacion r, long idGestion, ActorContext actor, GestionRepositorio repo, IConfiguration config, NotificacionesRepositorio notificaciones)
    {
        var (auto, sup) = Reglas(config);
        if (r.Exito) await notificaciones.MarcarGestionAsync(actor, idGestion);   // resolverla marca sus avisos como leidos
        return r.Exito
            ? Results.Ok(await repo.ObtenerDetalleAsync(actor, idGestion, auto, sup))
            : Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
    }

    private static async Task<IResult> AprobarAsync(long idGestion, GestionAprobarRequest? request, ActorContext actor, GestionRepositorio repo, IConfiguration config, NotificacionesRepositorio notificaciones)
    {
        var (auto, sup) = Reglas(config);
        return await Resultado(await repo.AprobarAsync(actor, idGestion, auto, sup, request?.Comentario), idGestion, actor, repo, config, notificaciones);
    }

    private static async Task<IResult> RechazarAsync(long idGestion, GestionRechazarRequest request, ActorContext actor, GestionRepositorio repo, IConfiguration config, NotificacionesRepositorio notificaciones)
    {
        var (auto, sup) = Reglas(config);
        return await Resultado(await repo.RechazarAsync(actor, idGestion, request.Motivo, sup, auto), idGestion, actor, repo, config, notificaciones);
    }

    private static async Task<IResult> DevolverAsync(long idGestion, GestionDevolverRequest request, ActorContext actor, GestionRepositorio repo, IConfiguration config, NotificacionesRepositorio notificaciones)
    {
        var (auto, sup) = Reglas(config);
        return await Resultado(await repo.DevolverAsync(actor, idGestion, request.Comentario, sup, auto), idGestion, actor, repo, config, notificaciones);
    }
}
