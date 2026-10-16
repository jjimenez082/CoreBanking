using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Mantenimiento de usuarios (SEG_ADMINISTRACION): alta, cambio, activar/inactivar y restablecer contrasena.</summary>
public static class UsuariosEndpoints
{
    public static void MapUsuariosEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/usuarios").RequireAuthorization().RequierePermiso("SEG_ADMINISTRACION");

        g.MapGet("/", async (ActorContext actor, UsuarioRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania)));

        // Si Keycloak no esta configurado, la pantalla avisa en vez de fallar al crear.
        g.MapGet("/estado-integracion", (UsuarioRepositorio repo) => Results.Ok(new { keycloakConfigurado = repo.KeycloakConfigurado }));

        g.MapGet("/{idUsuario:long}", async (long idUsuario, ActorContext actor, UsuarioRepositorio repo) =>
        {
            var u = await repo.ObtenerAsync(actor.IdCompania, idUsuario);
            return u is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "El usuario no existe." }) : Results.Ok(u);
        });

        g.MapPost("/", async (UsuarioCrearRequest req, ActorContext actor, UsuarioRepositorio repo) =>
        {
            var (r, cred) = await repo.CrearAsync(actor, req);
            return r.Exito ? Results.Created($"/api/usuarios/{r.Id}", cred) : Error(r);
        });

        g.MapPut("/{idUsuario:long}", async (long idUsuario, UsuarioEditarRequest req, ActorContext actor, UsuarioRepositorio repo) =>
        {
            var r = await repo.EditarAsync(actor, idUsuario, req);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        g.MapPut("/{idUsuario:long}/roles", async (long idUsuario, UsuarioRolesRequest req, ActorContext actor, UsuarioRepositorio repo) =>
        {
            var r = await repo.CambiarRolesAsync(actor, idUsuario, req.CodigosRol ?? []);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        g.MapPut("/{idUsuario:long}/estado", async (long idUsuario, UsuarioEstadoRequest req, ActorContext actor, UsuarioRepositorio repo) =>
        {
            var r = await repo.CambiarEstadoAsync(actor, idUsuario, req.Estado);
            return r.Exito ? Results.NoContent() : Error(r);
        });

        g.MapPost("/{idUsuario:long}/restablecer-password", async (long idUsuario, ActorContext actor, UsuarioRepositorio repo) =>
        {
            var (r, cred) = await repo.RestablecerPasswordAsync(actor, idUsuario);
            return r.Exito ? Results.Ok(cred) : Error(r);
        });
    }

    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
