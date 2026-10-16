using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Motor de reglas (ronda 43): catalogos, pool de condiciones y acciones, conjuntos versionados, simulador, casos de prueba y bitacora.</summary>
public static class ReglasEndpoints
{
    public static void MapReglasEndpoints(this WebApplication app)
    {
        const string Cons = ReglasRepositorio.PermisoConsulta, Adm = ReglasRepositorio.PermisoAdmin, Pub = ReglasRepositorio.PermisoPublicar, Eva = ReglasRepositorio.PermisoEvaluar;
        var g = app.MapGroup("/api/reglas").RequireAuthorization();

        g.MapGet("/catalogos", async (string? contexto, ReglasRepositorio repo) => Results.Ok(await repo.CatalogosAsync(contexto))).RequierePermiso(Cons);

        // ----- Condiciones -----
        g.MapGet("/condiciones", async (ActorContext actor, ReglasRepositorio repo) => Results.Ok(await repo.ListarCondicionesAsync(actor))).RequierePermiso(Cons);
        g.MapPost("/condiciones", async (CondicionGuardarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.GuardarCondicionAsync(actor, null, req);
            return r.Exito ? Results.Created($"/api/reglas/condiciones/{r.Id}", new { idCondicion = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        g.MapPut("/condiciones/{id:long}", async (long id, CondicionGuardarRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.GuardarCondicionAsync(actor, id, req))).RequierePermiso(Adm);
        g.MapPut("/condiciones/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.CambiarEstadoCondicionAsync(actor, id, req.Estado))).RequierePermiso(Adm);
        g.MapPost("/condiciones/{id:long}/duplicar", async (long id, CondicionDuplicarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.DuplicarCondicionAsync(actor, id, req);
            return r.Exito ? Results.Created($"/api/reglas/condiciones/{r.Id}", new { idCondicion = r.Id }) : Error(r);
        }).RequierePermiso(Adm);

        // ----- Acciones -----
        g.MapGet("/acciones", async (ActorContext actor, ReglasRepositorio repo) => Results.Ok(await repo.ListarAccionesAsync(actor))).RequierePermiso(Cons);
        g.MapPost("/acciones", async (AccionGuardarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.GuardarAccionAsync(actor, null, req);
            return r.Exito ? Results.Created($"/api/reglas/acciones/{r.Id}", new { idAccion = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        g.MapPut("/acciones/{id:long}", async (long id, AccionGuardarRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.GuardarAccionAsync(actor, id, req))).RequierePermiso(Adm);
        g.MapPut("/acciones/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.CambiarEstadoAccionAsync(actor, id, req.Estado))).RequierePermiso(Adm);
        g.MapPost("/acciones/{id:long}/duplicar", async (long id, CondicionDuplicarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.DuplicarAccionAsync(actor, id, req);
            return r.Exito ? Results.Created($"/api/reglas/acciones/{r.Id}", new { idAccion = r.Id }) : Error(r);
        }).RequierePermiso(Adm);

        // ----- Conjuntos -----
        g.MapGet("/conjuntos", async (ActorContext actor, ReglasRepositorio repo) => Results.Ok(await repo.ListarConjuntosAsync(actor))).RequierePermiso(Cons);
        g.MapPost("/conjuntos", async (ConjuntoReglasGuardarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.GuardarConjuntoAsync(actor, null, req);
            return r.Exito ? Results.Created($"/api/reglas/conjuntos/{r.Id}", new { idConjunto = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        g.MapPut("/conjuntos/{id:long}", async (long id, ConjuntoReglasGuardarRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.GuardarConjuntoAsync(actor, id, req))).RequierePermiso(Adm);
        g.MapPut("/conjuntos/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.CambiarEstadoConjuntoAsync(actor, id, req.Estado))).RequierePermiso(Adm);
        g.MapGet("/conjuntos/{id:long}/referencias", async (long id, ActorContext actor, ReglasRepositorio repo) => Results.Ok(await repo.ReferenciasAsync(actor, id))).RequierePermiso(Cons);

        // ----- Versiones -----
        g.MapGet("/conjuntos/{id:long}/versiones", async (long id, ActorContext actor, ReglasRepositorio repo) => Results.Ok(await repo.ListarVersionesAsync(actor, id))).RequierePermiso(Cons);
        g.MapPost("/conjuntos/{id:long}/versiones", async (long id, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.CrearBorradorAsync(actor, id);
            return r.Exito ? Results.Created($"/api/reglas/versiones/{r.Id}", new { idVersion = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        g.MapGet("/versiones/{idVersion:long}", async (long idVersion, ActorContext actor, ReglasRepositorio repo) =>
            await repo.ObtenerVersionAsync(actor, idVersion) is { } v ? Results.Ok(v) : Results.NotFound()).RequierePermiso(Cons);
        g.MapPut("/versiones/{idVersion:long}/reglas", async (long idVersion, ReglasGuardarRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.GuardarReglasAsync(actor, idVersion, req))).RequierePermiso(Adm);
        g.MapDelete("/versiones/{idVersion:long}", async (long idVersion, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.EliminarBorradorAsync(actor, idVersion))).RequierePermiso(Adm);
        g.MapPost("/versiones/{idVersion:long}/publicar", async (long idVersion, VersionPublicarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.PublicarAsync(actor, idVersion, req);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", new { idGestion = r.Id, numero = r.Numero }) : Error(r);
        }).RequierePermiso(Pub);
        g.MapPost("/versiones/{idVersion:long}/reabrir", async (long idVersion, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.ReabrirAsync(actor, idVersion))).RequierePermiso(Adm);

        // ----- Simulador, casos de prueba y bitacora -----
        g.MapPost("/conjuntos/{id:long}/probar", async (long id, EvaluarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var (resp, err) = await repo.ProbarAsync(actor, id, req);
            return err is not null ? Error(err) : Results.Ok(resp);
        }).RequierePermiso(Eva);
        g.MapGet("/conjuntos/{id:long}/casos", async (long id, ActorContext actor, ReglasRepositorio repo) => Results.Ok(await repo.ListarCasosAsync(actor, id))).RequierePermiso(Cons);
        g.MapPost("/conjuntos/{id:long}/casos", async (long id, CasoPruebaGuardarRequest req, ActorContext actor, ReglasRepositorio repo) =>
        {
            var r = await repo.GuardarCasoAsync(actor, id, null, req);
            return r.Exito ? Results.Created($"/api/reglas/conjuntos/{id}/casos/{r.Id}", new { idCaso = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        g.MapPut("/conjuntos/{id:long}/casos/{idCaso:long}", async (long id, long idCaso, CasoPruebaGuardarRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.GuardarCasoAsync(actor, id, idCaso, req))).RequierePermiso(Adm);
        g.MapPut("/conjuntos/{id:long}/casos/{idCaso:long}/estado", async (long id, long idCaso, EstadoRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.CambiarEstadoCasoAsync(actor, id, idCaso, req.Estado))).RequierePermiso(Adm);
        g.MapPost("/conjuntos/{id:long}/casos/ejecutar", async (long id, long? idVersion, ActorContext actor, ReglasRepositorio repo) =>
        {
            var (resp, err) = await repo.EjecutarCasosAsync(actor, id, idVersion);
            return err is not null ? Error(err) : Results.Ok(resp);
        }).RequierePermiso(Eva);
        g.MapGet("/evaluaciones", async (long? idConjunto, string? referencia, ActorContext actor, ReglasRepositorio repo) => Results.Ok(await repo.ListarEvaluacionesAsync(actor, idConjunto, referencia))).RequierePermiso(Cons);
        g.MapGet("/evaluaciones/{id:long}", async (long id, ActorContext actor, ReglasRepositorio repo) =>
            await repo.ObtenerEvaluacionAsync(actor, id) is { } e ? Results.Ok(e) : Results.NotFound()).RequierePermiso(Cons);

        // ----- Elegibilidad de productos comerciales -----
        var co = app.MapGroup("/api/productos-comerciales").RequireAuthorization();
        co.MapGet("/{id:long}/elegibilidad", async (long id, ActorContext actor, ReglasRepositorio repo) => Results.Ok(new { idConjunto = await repo.ElegibilidadDeProductoAsync(actor, id) })).RequierePermiso(ProductoRepositorio.PermisoConsulta);
        co.MapPut("/{id:long}/elegibilidad", async (long id, ElegibilidadRequest req, ActorContext actor, ReglasRepositorio repo) => Sin(await repo.AsignarElegibilidadAsync(actor, id, req.IdConjunto))).RequierePermiso(ProductoRepositorio.PermisoAdmin);
    }

    private static IResult Sin(ResultadoOperacion r) => r.Exito ? Results.NoContent() : Error(r);
    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
