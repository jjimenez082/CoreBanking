using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Cimientos de Creditos (ronda 42): tasas de referencia, calendarios, categorias regulatorias, productos operativos y comerciales con versiones.</summary>
public static class CreditosEndpoints
{
    public static void MapCreditosEndpoints(this WebApplication app)
    {
        const string Cons = CatalogoFinancieroRepositorio.PermisoConsulta, Ind = CatalogoFinancieroRepositorio.Permiso, Carga = CatalogoFinancieroRepositorio.PermisoCarga;
        const string Adm = ProductoRepositorio.PermisoAdmin, Pub = ProductoRepositorio.PermisoPublicar;

        // ----- Bases de calculo -----
        var bas = app.MapGroup("/api/bases-calculo").RequireAuthorization();
        bas.MapGet("/", async (CatalogoFinancieroRepositorio repo) => Results.Ok(await repo.ListarBasesAsync())).RequierePermiso(Cons);
        bas.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) => Sin(await repo.CambiarEstadoBaseAsync(actor, id, req.Estado))).RequierePermiso(Ind);

        // ----- Indicadores financieros -----
        var ind = app.MapGroup("/api/indicadores").RequireAuthorization();
        ind.MapGet("/", async (CatalogoFinancieroRepositorio repo) => Results.Ok(await repo.ListarIndicadoresAsync())).RequierePermiso(Cons);
        ind.MapPost("/", async (IndicadorGuardarRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) =>
        {
            var r = await repo.CrearIndicadorAsync(actor, req);
            return r.Exito ? Results.Created($"/api/indicadores/{r.Id}", new { idIndicador = r.Id }) : Error(r);
        }).RequierePermiso(Ind);
        ind.MapPut("/{id:long}", async (long id, IndicadorGuardarRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) => Sin(await repo.EditarIndicadorAsync(actor, id, req))).RequierePermiso(Ind);
        ind.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) => Sin(await repo.CambiarEstadoIndicadorAsync(actor, id, req.Estado))).RequierePermiso(Ind);
        ind.MapGet("/{id:long}/valores", async (long id, CatalogoFinancieroRepositorio repo) => Results.Ok(await repo.ListarValoresAsync(id))).RequierePermiso(Cons);
        ind.MapPost("/valores/lote", async (IndicadorLoteRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) =>
        {
            if (req.Filas is null || req.Filas.Count == 0) return Results.Json(new { error = "datos_invalidos", mensaje = "El archivo no trae filas." }, statusCode: 400);
            if (req.Filas.Count > CatalogoFinancieroRepositorio.MaxFilasLote) return Results.Json(new { error = "datos_invalidos", mensaje = $"Maximo {CatalogoFinancieroRepositorio.MaxFilasLote} filas por carga." }, statusCode: 400);
            return Results.Ok(await repo.SolicitarValoresLoteAsync(actor, req.Filas));
        }).RequierePermiso(Carga);
        ind.MapPost("/{id:long}/valores", async (long id, IndicadorValorSolicitudRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) =>
        {
            var r = await repo.SolicitarValorAsync(actor, id, req);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", new { idGestion = r.Id, numero = r.Numero }) : Error(r);
        }).RequierePermiso(Carga);

        // ----- Calendarios -----
        var cal = app.MapGroup("/api/calendarios").RequireAuthorization();
        cal.MapGet("/", async (ActorContext actor, CatalogoFinancieroRepositorio repo) => Results.Ok(await repo.ListarCalendariosAsync(actor.IdCompania))).RequierePermiso(Cons);
        cal.MapPost("/", async (CalendarioGuardarRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) =>
        {
            var r = await repo.GuardarCalendarioAsync(actor, null, req);
            return r.Exito ? Results.Created($"/api/calendarios/{r.Id}", new { idCalendario = r.Id }) : Error(r);
        }).RequierePermiso(Ind);
        cal.MapPut("/{id:long}", async (long id, CalendarioGuardarRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) => Sin(await repo.GuardarCalendarioAsync(actor, id, req))).RequierePermiso(Ind);
        cal.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) => Sin(await repo.CambiarEstadoCalendarioAsync(actor, id, req.Estado))).RequierePermiso(Ind);

        // ----- Categorias regulatorias -----
        var cat = app.MapGroup("/api/categorias-regulatorias").RequireAuthorization();
        cat.MapGet("/", async (long? idPaisRegulador, CatalogoFinancieroRepositorio repo) => Results.Ok(await repo.ListarCategoriasAsync(idPaisRegulador))).RequierePermiso(Cons);
        cat.MapGet("/paises", async (CatalogoFinancieroRepositorio repo) => Results.Ok(await repo.PaisesReguladoresAsync())).RequierePermiso(Cons);
        cat.MapPost("/", async (CategoriaRegulatoriaGuardarRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) =>
        {
            var r = await repo.GuardarCategoriaAsync(actor, null, req);
            return r.Exito ? Results.Created($"/api/categorias-regulatorias/{r.Id}", new { idCategoria = r.Id }) : Error(r);
        }).RequierePermiso(Ind);
        cat.MapPut("/{id:long}", async (long id, CategoriaRegulatoriaGuardarRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) => Sin(await repo.GuardarCategoriaAsync(actor, id, req))).RequierePermiso(Ind);
        cat.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, CatalogoFinancieroRepositorio repo) => Sin(await repo.CambiarEstadoCategoriaAsync(actor, id, req.Estado))).RequierePermiso(Ind);

        // ----- Productos operativos -----
        var op = app.MapGroup("/api/productos-operativos").RequireAuthorization();
        op.MapGet("/", async (ActorContext actor, ProductoRepositorio repo) => Results.Ok(await repo.ListarOperativosAsync(actor.IdCompania))).RequierePermiso(Cons);
        op.MapGet("/listas", async (ProductoRepositorio repo) => Results.Ok(await repo.ListasDisponiblesAsync())).RequierePermiso(Cons);
        op.MapGet("/{id:long}", async (long id, ActorContext actor, ProductoRepositorio repo) =>
            (await repo.ListarOperativosAsync(actor.IdCompania, id)).FirstOrDefault() is { } p ? Results.Ok(p) : Results.NotFound()).RequierePermiso(Cons);
        op.MapPost("/", async (ProductoOperativoGuardarRequest req, ActorContext actor, ProductoRepositorio repo) =>
        {
            var r = await repo.CrearOperativoAsync(actor, req);
            return r.Exito ? Results.Created($"/api/productos-operativos/{r.Id}", new { idProducto = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        op.MapPut("/{id:long}", async (long id, ProductoOperativoGuardarRequest req, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.EditarOperativoAsync(actor, id, req))).RequierePermiso(Adm);
        op.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.CambiarEstadoOperativoAsync(actor, id, req.Estado))).RequierePermiso(Adm);
        op.MapGet("/{id:long}/parametros", async (long id, ProductoRepositorio repo) => Results.Ok(await repo.ListarParametrosAsync(id))).RequierePermiso(Cons);
        op.MapPost("/{id:long}/parametros", async (long id, ParametroProductoGuardarRequest req, ActorContext actor, ProductoRepositorio repo) =>
        {
            var r = await repo.GuardarParametroAsync(actor, id, null, req);
            return r.Exito ? Results.Created($"/api/productos-operativos/{id}/parametros/{r.Id}", new { idParametro = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        op.MapPut("/{id:long}/parametros/{idPar:long}", async (long id, long idPar, ParametroProductoGuardarRequest req, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.GuardarParametroAsync(actor, id, idPar, req))).RequierePermiso(Adm);
        op.MapPut("/{id:long}/parametros/{idPar:long}/estado", async (long id, long idPar, EstadoRequest req, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.CambiarEstadoParametroAsync(actor, id, idPar, req.Estado))).RequierePermiso(Adm);

        // ----- Productos comerciales y versiones -----
        var co = app.MapGroup("/api/productos-comerciales").RequireAuthorization();
        co.MapGet("/", async (ActorContext actor, ProductoRepositorio repo) => Results.Ok(await repo.ListarComercialesAsync(actor.IdCompania))).RequierePermiso(Cons);
        co.MapPost("/", async (ProductoComercialGuardarRequest req, ActorContext actor, ProductoRepositorio repo) =>
        {
            var r = await repo.GuardarComercialAsync(actor, null, req);
            return r.Exito ? Results.Created($"/api/productos-comerciales/{r.Id}", new { idProducto = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        co.MapPut("/{id:long}", async (long id, ProductoComercialGuardarRequest req, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.GuardarComercialAsync(actor, id, req))).RequierePermiso(Adm);
        co.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.CambiarEstadoComercialAsync(actor, id, req.Estado))).RequierePermiso(Adm);
        co.MapGet("/{id:long}/versiones", async (long id, ActorContext actor, ProductoRepositorio repo) => Results.Ok(await repo.ListarVersionesAsync(actor, id))).RequierePermiso(Cons);
        co.MapPost("/{id:long}/versiones", async (long id, ActorContext actor, ProductoRepositorio repo) =>
        {
            var r = await repo.CrearBorradorAsync(actor, id);
            return r.Exito ? Results.Created($"/api/productos-comerciales/versiones/{r.Id}", new { idVersion = r.Id }) : Error(r);
        }).RequierePermiso(Adm);
        co.MapGet("/versiones/{idVersion:long}", async (long idVersion, ActorContext actor, ProductoRepositorio repo) =>
            await repo.ObtenerVersionAsync(actor, idVersion) is { } v ? Results.Ok(v) : Results.NotFound()).RequierePermiso(Cons);
        co.MapPut("/versiones/{idVersion:long}/valores", async (long idVersion, VersionValoresGuardarRequest req, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.GuardarValoresAsync(actor, idVersion, req))).RequierePermiso(Adm);
        co.MapDelete("/versiones/{idVersion:long}", async (long idVersion, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.EliminarBorradorAsync(actor, idVersion))).RequierePermiso(Adm);
        co.MapPost("/versiones/{idVersion:long}/publicar", async (long idVersion, VersionPublicarRequest req, ActorContext actor, ProductoRepositorio repo) =>
        {
            var r = await repo.PublicarAsync(actor, idVersion, req);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", new { idGestion = r.Id, numero = r.Numero }) : Error(r);
        }).RequierePermiso(Pub);
        co.MapPost("/versiones/{idVersion:long}/reabrir", async (long idVersion, ActorContext actor, ProductoRepositorio repo) => Sin(await repo.ReabrirAsync(actor, idVersion))).RequierePermiso(Adm);
    }

    private static IResult Sin(ResultadoOperacion r) => r.Exito ? Results.NoContent() : Error(r);
    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
