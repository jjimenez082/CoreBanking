using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

public static class CoincidenciaEndpoints
{
    public static void MapCoincidenciaEndpoints(this WebApplication app)
    {
        // Control de calidad de datos: personas parecidas a la que se va a registrar (cliente, prospecto o rol).
        app.MapPost("/api/interlocutores/coincidencias", BuscarAsync)
            .RequireAuthorization().RequierePermiso("CLI_CONSULTA");
    }

    private static async Task<IResult> BuscarAsync(CoincidenciaRequest q, ActorContext actor, CoincidenciasRepositorio repo, PerfilDatosServicio perfiles)
        => Results.Ok(await Enmascarar(await repo.BuscarAsync(actor.IdCompania, q), perfiles));

    /// <summary>Aplica el perfil de datos del usuario a las coincidencias (clientes fuera de sus filas quedan restringidos; identificaciones enmascaradas).</summary>
    public static async Task<CoincidenciasResultado> Enmascarar(CoincidenciasResultado r, PerfilDatosServicio perfiles)
    {
        var perfil = await perfiles.ObtenerAsync();
        if (!perfil.RestringeFilas && !perfil.HayMascaras) return r;
        var items = new List<CoincidenciaResponse>();
        foreach (var i in r.Items)
        {
            var visible = i.Origen == "Prospecto" || !perfil.RestringeFilas || await perfiles.PuedeVerClienteAsync(i.Id);
            items.Add(EnmascaradoClientes.Aplicar(i, perfil, visible));
        }
        return r with { Items = items };
    }
}
