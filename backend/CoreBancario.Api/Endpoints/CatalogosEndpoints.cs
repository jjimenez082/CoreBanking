using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Catalogos de solo lectura para combo boxes del Backoffice. Basta con estar
/// autenticado (son opciones de pantalla, no datos de negocio); quien puede
/// DAR DE ALTA o CONSULTAR un registro lo controla el endpoint de ese registro.
/// </summary>
public static class CatalogosEndpoints
{
    public static void MapCatalogosEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/catalogos").RequireAuthorization();

        grupo.MapGet("/listas/{codigoLista}", ObtenerListaAsync);
        grupo.MapGet("/paises-reguladores", ObtenerPaisesAsync);
    }

    private static async Task<IResult> ObtenerListaAsync(string codigoLista, CatalogosRepositorio repo)
    {
        var items = await repo.ObtenerListaAsync(codigoLista.Trim().ToUpperInvariant());
        return items is null
            ? Results.NotFound(new { error = "lista_no_encontrada", mensaje = $"No existe una lista activa con el codigo '{codigoLista}'." })
            : Results.Ok(items);
    }

    private static async Task<IResult> ObtenerPaisesAsync(ActorContext actor, CatalogosRepositorio repo)
        => Results.Ok(await repo.ObtenerPaisesReguladoresAsync(actor.IdCompania));
}
