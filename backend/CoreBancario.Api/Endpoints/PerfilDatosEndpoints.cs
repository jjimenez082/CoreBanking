using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Administracion de perfiles de datos, mascaras y su asignacion a usuarios (SEG_PERFILES).</summary>
public static class PerfilDatosEndpoints
{
    public static void MapPerfilDatosEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/perfiles-datos").RequireAuthorization().RequierePermiso(PerfilDatosAdminRepositorio.Permiso);

        g.MapGet("/catalogo", async (ActorContext actor, PerfilDatosAdminRepositorio repo) => Results.Ok(await repo.CatalogoAsync(actor.IdCompania)));

        g.MapGet("/", async (ActorContext actor, PerfilDatosAdminRepositorio repo) => Results.Ok(await repo.ListarPerfilesAsync(actor.IdCompania)));
        g.MapGet("/{id:long}", async (long id, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var p = await repo.ObtenerPerfilAsync(actor.IdCompania, id);
            return p is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el perfil." }) : Results.Ok(p);
        });
        g.MapPost("/", async (PerfilDatosGuardarRequest req, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var r = await repo.CrearPerfilAsync(actor, req);
            return r.Exito ? Results.Created($"/api/perfiles-datos/{r.Id}", new { id = r.Id }) : Error(r);
        });
        g.MapPut("/{id:long}", async (long id, PerfilDatosGuardarRequest req, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var r = await repo.EditarPerfilAsync(actor, id, req);
            return r.Exito ? Results.NoContent() : Error(r);
        });
        g.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var r = await repo.CambiarEstadoPerfilAsync(actor, id, req.Estado);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        g.MapGet("/mascaras", async (ActorContext actor, PerfilDatosAdminRepositorio repo) => Results.Ok(await repo.ListarMascarasAsync(actor.IdCompania)));
        g.MapPost("/mascaras", async (MascaraGuardarRequest req, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var r = await repo.CrearMascaraAsync(actor, req);
            return r.Exito ? Results.Created($"/api/perfiles-datos/mascaras/{r.Id}", new { id = r.Id }) : Error(r);
        });
        g.MapPut("/mascaras/{id:long}", async (long id, MascaraGuardarRequest req, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var r = await repo.EditarMascaraAsync(actor, id, req);
            return r.Exito ? Results.NoContent() : Error(r);
        });
        g.MapPut("/mascaras/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var r = await repo.CambiarEstadoMascaraAsync(actor, id, req.Estado);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        g.MapGet("/usuarios", async (ActorContext actor, PerfilDatosAdminRepositorio repo) => Results.Ok(await repo.ListarUsuariosAsync(actor.IdCompania)));
        g.MapPut("/usuarios/{idUsuario:long}", async (long idUsuario, UsuarioPerfilesGuardarRequest req, ActorContext actor, PerfilDatosAdminRepositorio repo) =>
        {
            var r = await repo.AsignarPerfilesAsync(actor, idUsuario, req.Perfiles ?? []);
            return r.Exito ? Results.NoContent() : Error(r);
        });
    }

    public record EstadoRequest(string Estado);
    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
