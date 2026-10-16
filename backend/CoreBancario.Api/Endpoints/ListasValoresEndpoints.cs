using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;
using Microsoft.Data.SqlClient;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Mantenimiento de Listas de Valores. Seguridad en tres niveles:
///  - LOV_ACCESO: entrar a la pantalla (exigido a todo el grupo).
///  - LOV_&lt;codigo&gt;: mantener los valores de ESA lista.
///  - LOV_ADMINISTRACION: todas las listas, mas crear/editar/desactivar/borrar listas.
/// Estados: Activo / Inactivo. Un valor usado en algun registro solo se
/// desactiva; solo se borra si nadie lo referencia. Los valores de sistema
/// (es_sistema = 1) no se pueden desactivar ni borrar.
/// </summary>
public static class ListasValoresEndpoints
{
    private const string PermisoAcceso = "LOV_ACCESO";

    private static bool EsAdministrador(ActorContext actor) =>
        actor.TienePermiso(ListasValoresRepositorio.ObjetoAdministracion);

    private static bool PuedeMantenerLista(ActorContext actor, string codigoLista) =>
        EsAdministrador(actor) || actor.TienePermiso(ListasValoresRepositorio.PrefijoObjetoLista + codigoLista);

    private static IResult Denegado(string codigoPermiso) =>
        Results.Json(
            new { error = "permiso_denegado", mensaje = $"No tienes el permiso requerido ({codigoPermiso}) para esta accion.", codigoPermiso },
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult ValidacionEstado(string estado) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["estado"] = [$"'{estado}' no es valido; use Activo o Inactivo."] });

    public static void MapListasValoresEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/listas-valores")
            .RequireAuthorization()
            .RequierePermiso(PermisoAcceso);

        // ----- Listas
        grupo.MapGet("/tipos", ListarTiposAsync);
        grupo.MapPost("/tipos", CrearTipoAsync);
        grupo.MapPut("/tipos/{idTipo:long}", ActualizarTipoAsync);
        grupo.MapPost("/tipos/{idTipo:long}/estado", CambiarEstadoTipoAsync);
        grupo.MapDelete("/tipos/{idTipo:long}", BorrarTipoAsync);

        // ----- Valores
        grupo.MapGet("/tipos/{idTipo:long}/items", ListarItemsAsync);
        grupo.MapPost("/tipos/{idTipo:long}/items", CrearItemAsync);
        grupo.MapPut("/items/{idItem:long}", ActualizarItemAsync);
        grupo.MapPost("/items/{idItem:long}/estado", CambiarEstadoItemAsync);
        grupo.MapDelete("/items/{idItem:long}", BorrarItemAsync);
    }

    // ======================= Listas =======================

    private static async Task<IResult> ListarTiposAsync(ActorContext actor, ListasValoresRepositorio repo)
    {
        var tipos = await repo.ListarTiposAsync();
        // Cada usuario ve solo las listas que puede mantener.
        return Results.Ok(tipos.Where(t => PuedeMantenerLista(actor, t.Codigo)).ToList());
    }

    private static async Task<IResult> CrearTipoAsync(
        ListaTipoCrearRequest request, ActorContext actor, ListasValoresRepositorio repo)
    {
        if (!EsAdministrador(actor)) return Denegado(ListasValoresRepositorio.ObjetoAdministracion);

        if (string.IsNullOrWhiteSpace(request.Codigo) || string.IsNullOrWhiteSpace(request.Nombre))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["codigo/nombre"] = ["Son obligatorios."] });
        }

        var codigo = request.Codigo.Trim().ToUpperInvariant().Replace(' ', '_');
        try
        {
            var id = await repo.CrearTipoAsync(
                codigo, request.Nombre.Trim(), string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim(),
                actor.NombreDescriptivo);
            return Results.Created($"/api/listas-valores/tipos/{id}", await repo.ObtenerTipoAsync(id));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Results.Conflict(new { error = "codigo_duplicado", mensaje = $"Ya existe una lista con el codigo '{codigo}'." });
        }
    }

    private static async Task<IResult> ActualizarTipoAsync(
        long idTipo, ListaTipoActualizarRequest request, ActorContext actor, ListasValoresRepositorio repo)
    {
        if (!EsAdministrador(actor)) return Denegado(ListasValoresRepositorio.ObjetoAdministracion);

        var tipo = await repo.ObtenerTipoAsync(idTipo);
        if (tipo is null) return Results.NotFound(new { error = "lista_no_encontrada", idTipo });
        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Es obligatorio."] });
        }

        await repo.ActualizarTipoAsync(
            idTipo, request.Nombre.Trim(), string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim(),
            actor.NombreDescriptivo);
        return Results.Ok(await repo.ObtenerTipoAsync(idTipo));
    }

    private static async Task<IResult> CambiarEstadoTipoAsync(
        long idTipo, CambiarEstadoRequest request, ActorContext actor, ListasValoresRepositorio repo)
    {
        if (!EsAdministrador(actor)) return Denegado(ListasValoresRepositorio.ObjetoAdministracion);
        if (request.Estado is not ("Activo" or "Inactivo")) return ValidacionEstado(request.Estado);

        var tipo = await repo.ObtenerTipoAsync(idTipo);
        if (tipo is null) return Results.NotFound(new { error = "lista_no_encontrada", idTipo });
        if (tipo.EsSistema && request.Estado == "Inactivo")
        {
            return Results.Conflict(new { error = "lista_de_sistema", mensaje = "Una lista de sistema no se puede desactivar." });
        }

        await repo.CambiarEstadoTipoAsync(idTipo, request.Estado, actor.NombreDescriptivo);
        return Results.Ok(await repo.ObtenerTipoAsync(idTipo));
    }

    private static async Task<IResult> BorrarTipoAsync(long idTipo, ActorContext actor, ListasValoresRepositorio repo)
    {
        if (!EsAdministrador(actor)) return Denegado(ListasValoresRepositorio.ObjetoAdministracion);

        var tipo = await repo.ObtenerTipoAsync(idTipo);
        if (tipo is null) return Results.NotFound(new { error = "lista_no_encontrada", idTipo });
        if (tipo.EsSistema)
        {
            return Results.Conflict(new { error = "lista_de_sistema", mensaje = "Una lista de sistema no se puede borrar." });
        }
        if (tipo.CantidadItems > 0)
        {
            return Results.Conflict(new { error = "lista_con_valores", mensaje = "La lista tiene valores; borre o desactive sus valores primero (o desactive la lista)." });
        }
        if (await repo.TipoEsPadreAsync(idTipo))
        {
            return Results.Conflict(new { error = "lista_es_padre", mensaje = "Otra lista depende de esta como lista padre." });
        }

        try
        {
            await repo.BorrarTipoAsync(idTipo, tipo.Codigo);
            return Results.NoContent();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            return Results.Conflict(new { error = "lista_referenciada", mensaje = "La lista esta referenciada por otros datos; desactivela en su lugar." });
        }
    }

    // ======================= Valores =======================

    private static async Task<IResult> ListarItemsAsync(
        long idTipo, ActorContext actor, ListasValoresRepositorio repo)
    {
        var tipo = await repo.ObtenerTipoAsync(idTipo);
        if (tipo is null) return Results.NotFound(new { error = "lista_no_encontrada", idTipo });
        if (!PuedeMantenerLista(actor, tipo.Codigo)) return Denegado(ListasValoresRepositorio.PrefijoObjetoLista + tipo.Codigo);

        return Results.Ok(await repo.ListarItemsAsync(idTipo));
    }

    private static async Task<IResult> CrearItemAsync(
        long idTipo, ListaItemCrearRequest request, ActorContext actor, ListasValoresRepositorio repo)
    {
        var tipo = await repo.ObtenerTipoAsync(idTipo);
        if (tipo is null) return Results.NotFound(new { error = "lista_no_encontrada", idTipo });
        if (!PuedeMantenerLista(actor, tipo.Codigo)) return Denegado(ListasValoresRepositorio.PrefijoObjetoLista + tipo.Codigo);

        if (string.IsNullOrWhiteSpace(request.Codigo) || string.IsNullOrWhiteSpace(request.Nombre))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["codigo/nombre"] = ["Son obligatorios."] });
        }

        var codigo = request.Codigo.Trim().ToUpperInvariant().Replace(' ', '_');
        try
        {
            var id = await repo.CrearItemAsync(idTipo, codigo, request.Nombre.Trim(), request.Orden, actor.NombreDescriptivo, string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim());
            return Results.Created($"/api/listas-valores/items/{id}", await repo.ObtenerItemAsync(id));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Results.Conflict(new { error = "codigo_duplicado", mensaje = $"Ya existe el valor '{codigo}' en esta lista." });
        }
    }

    // Resuelve el item y la lista a la que pertenece, y valida el permiso sobre esa lista.
    private static async Task<(ListaItemResponse? Item, ListaTipoResponse? Tipo, IResult? Error)> ResolverItemAsync(
        long idItem, ActorContext actor, ListasValoresRepositorio repo)
    {
        var item = await repo.ObtenerItemAsync(idItem);
        if (item is null) return (null, null, Results.NotFound(new { error = "valor_no_encontrado", idItem }));

        var tipo = await repo.ObtenerTipoAsync(item.IdListaValoresTipo);
        if (tipo is null) return (null, null, Results.NotFound(new { error = "lista_no_encontrada", item.IdListaValoresTipo }));

        if (!PuedeMantenerLista(actor, tipo.Codigo))
            return (null, null, Denegado(ListasValoresRepositorio.PrefijoObjetoLista + tipo.Codigo));

        return (item, tipo, null);
    }

    private static async Task<IResult> ActualizarItemAsync(
        long idItem, ListaItemActualizarRequest request, ActorContext actor, ListasValoresRepositorio repo)
    {
        var (item, _, error) = await ResolverItemAsync(idItem, actor, repo);
        if (error is not null) return error;

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = ["Es obligatorio."] });
        }

        await repo.ActualizarItemAsync(idItem, request.Nombre.Trim(), request.Orden, actor.NombreDescriptivo, string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim());
        return Results.Ok(await repo.ObtenerItemAsync(item!.IdListaValoresItem));
    }

    private static async Task<IResult> CambiarEstadoItemAsync(
        long idItem, CambiarEstadoRequest request, ActorContext actor, ListasValoresRepositorio repo)
    {
        if (request.Estado is not ("Activo" or "Inactivo")) return ValidacionEstado(request.Estado);

        var (item, _, error) = await ResolverItemAsync(idItem, actor, repo);
        if (error is not null) return error;

        if (item!.EsSistema && request.Estado == "Inactivo")
        {
            return Results.Conflict(new { error = "valor_de_sistema", mensaje = "Un valor de sistema no se puede desactivar." });
        }

        await repo.CambiarEstadoItemAsync(idItem, request.Estado, actor.NombreDescriptivo);
        return Results.Ok(await repo.ObtenerItemAsync(idItem));
    }

    private static async Task<IResult> BorrarItemAsync(long idItem, ActorContext actor, ListasValoresRepositorio repo)
    {
        var (item, _, error) = await ResolverItemAsync(idItem, actor, repo);
        if (error is not null) return error;

        if (item!.EsSistema)
        {
            return Results.Conflict(new { error = "valor_de_sistema", mensaje = "Un valor de sistema no se puede borrar." });
        }
        if (item.EnUso)
        {
            return Results.Conflict(new { error = "valor_en_uso", mensaje = "El valor ya se uso en algun registro; no se puede borrar. Desactivelo para que no aparezca en registros nuevos." });
        }

        try
        {
            await repo.BorrarItemAsync(idItem);
            return Results.NoContent();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            // Carrera: se uso entre la verificacion y el borrado.
            return Results.Conflict(new { error = "valor_en_uso", mensaje = "El valor ya se uso en algun registro; desactivelo en su lugar." });
        }
    }
}
