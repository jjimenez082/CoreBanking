using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

public static class InterlocutorEndpoints
{
    public static void MapInterlocutorEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/interlocutores")
            .RequireAuthorization();

        // RBAC (SEG-002): cada endpoint exige su propio codigo de
        // sec.OBJETO_AUTORIZACION, sembrado en modelo_fisico_transversal_minimo.sql.
        grupo.MapPost("/", AltaAsync).RequierePermiso("CLI_ALTA");
        grupo.MapGet("/", ListarAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/buscar", BuscarAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}", DetalleAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}/detalle", DetalleAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}/360", Vista360Async).RequierePermiso("CLI_CONSULTA");

        // Todo cambio a datos de un cliente va por Gestion (principio del sistema):
        // este endpoint solo SOLICITA el cambio; se aplica cuando alguien con
        // GES_APROBAR lo aprueba (ver GestionEndpoints).
        grupo.MapPost("/{idInterlocutor:long}/modificacion", SolicitarModificacionAsync).RequierePermiso("CLI_EDICION");

        // Borradores del wizard de alta: privados de su propietario (cada consulta filtra por usuario).
        grupo.MapGet("/borradores", ListarBorradoresAsync).RequierePermiso("CLI_ALTA");
        grupo.MapGet("/borradores/{idBorrador:long}", ObtenerBorradorAsync).RequierePermiso("CLI_ALTA");
        grupo.MapPut("/borradores", GuardarBorradorAsync).RequierePermiso("CLI_ALTA");
        grupo.MapDelete("/borradores/{idBorrador:long}", EliminarBorradorAsync).RequierePermiso("CLI_ALTA");
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);

    private static async Task<IResult> ListarAsync(
        string? q, int? pagina, int? tamanoPagina, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var paginaValida = pagina is > 0 ? pagina.Value : 1;
        var tamanoValido = tamanoPagina is > 0 and <= 100 ? tamanoPagina.Value : 20;

        var (items, total) = await repositorio.ListarAsync(actorContext.IdCompania, q, paginaValida, tamanoValido);

        return Results.Ok(new InterlocutorListaResponse(items, paginaValida, tamanoValido, total));
    }

    /// <summary>Busqueda global de la barra superior: cualquier rol; el usuario elige y abre la vista 360.</summary>
    private static async Task<IResult> BuscarAsync(string? q, ActorContext actorContext, InterlocutorRepositorio repositorio)
        => Results.Ok(await repositorio.BuscarAsync(actorContext.IdCompania, q));

    private static async Task<IResult> AltaAsync(
        InterlocutorAltaRequest request, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var r = await repositorio.AltaAsync(actorContext, request);
        if (!r.Exito) return Error(r);

        var creado = await repositorio.ObtenerDetalleAsync(actorContext.IdCompania, r.Id!.Value);
        return Results.Created($"/api/interlocutores/{r.Id}", creado);
    }

    private static async Task<IResult> DetalleAsync(
        long idInterlocutor, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var detalle = await repositorio.ObtenerDetalleAsync(actorContext.IdCompania, idInterlocutor);

        return detalle is null
            ? Results.NotFound(new { error = "no_encontrado", idInterlocutor })
            : Results.Ok(detalle);
    }

    private static async Task<IResult> Vista360Async(
        long idInterlocutor, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var vista = await repositorio.Obtener360Async(actorContext.IdCompania, idInterlocutor);

        return vista is null
            ? Results.NotFound(new { error = "no_encontrado", idInterlocutor })
            : Results.Ok(vista);
    }

    private static async Task<IResult> SolicitarModificacionAsync(
        long idInterlocutor, InterlocutorModificacionRequest request, ActorContext actorContext, GestionRepositorio gestiones)
    {
        var r = await gestiones.SolicitarModificacionInterlocutorAsync(actorContext, idInterlocutor, request);

        return r.Exito
            ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actorContext, r.Id!.Value))
            : Error(r);
    }

    // ----- Borradores ----------------------------------------------------------------

    private static async Task<IResult> ListarBorradoresAsync(ActorContext actorContext, InterlocutorRepositorio repositorio)
        => Results.Ok(await repositorio.ListarBorradoresAsync(actorContext));

    private static async Task<IResult> ObtenerBorradorAsync(
        long idBorrador, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var b = await repositorio.ObtenerBorradorAsync(actorContext, idBorrador);
        return b is null ? Results.NotFound(new { error = "borrador_no_encontrado", idBorrador }) : Results.Ok(b);
    }

    private static async Task<IResult> GuardarBorradorAsync(
        BorradorGuardarRequest request, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var r = await repositorio.GuardarBorradorAsync(actorContext, request);
        if (!r.Exito) return Error(r);

        var guardado = await repositorio.ObtenerBorradorAsync(actorContext, r.Id!.Value);
        return Results.Ok(new { idBorrador = r.Id, venceEn = guardado?.VenceEn });
    }

    private static async Task<IResult> EliminarBorradorAsync(
        long idBorrador, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var r = await repositorio.EliminarBorradorAsync(actorContext, idBorrador);
        return r.Exito ? Results.NoContent() : Error(r);
    }
}
