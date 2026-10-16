using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Administracion de aprobaciones: reglas (GES_REGLAS) y grupos de distribucion (GES_GRUPOS).</summary>
public static class AprobacionAdminEndpoints
{
    public static void MapAprobacionAdminEndpoints(this WebApplication app)
    {
        var reglas = app.MapGroup("/api/aprobacion").RequireAuthorization().RequierePermiso(ReglaAprobacionRepositorio.Objeto);

        reglas.MapGet("/tipos", async (ActorContext actor, ReglaAprobacionRepositorio repo) => Results.Ok(await repo.TiposAsync(actor.IdCompania)));
        reglas.MapPut("/tipos/{id:long}/permite-repetir", async (long id, PermiteRepetirRequest r, ActorContext actor, ReglaAprobacionRepositorio repo) =>
            Resp(await repo.CambiarPermiteRepetirAsync(actor, id, r.PermiteRepetir)));
        reglas.MapGet("/grupos-activos", async (ActorContext actor, GrupoUsuariosRepositorio repo) => Results.Ok(await repo.CatalogoActivosAsync(actor.IdCompania)));

        reglas.MapGet("/reglas", async (long idTipoGestion, ActorContext actor, ReglaAprobacionRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania, idTipoGestion)));
        reglas.MapPost("/reglas", async (ReglaGuardarRequest r, ActorContext actor, ReglaAprobacionRepositorio repo) =>
        {
            var x = await repo.CrearAsync(actor, r);
            return x.Exito ? Results.Created($"/api/aprobacion/reglas/{x.Id}", new { idRegla = x.Id }) : Error(x);
        });
        reglas.MapPut("/reglas/orden", async (ReglaOrdenRequest r, ActorContext actor, ReglaAprobacionRepositorio repo) => Resp(await repo.ReordenarAsync(actor, r)));
        reglas.MapPut("/reglas/{id:long}", async (long id, ReglaGuardarRequest r, ActorContext actor, ReglaAprobacionRepositorio repo) => Resp(await repo.EditarAsync(actor, id, r)));
        reglas.MapPut("/reglas/{id:long}/estado", async (long id, EstadoRequest r, ActorContext actor, ReglaAprobacionRepositorio repo) => Resp(await repo.CambiarEstadoAsync(actor, id, r.Estado)));
        reglas.MapDelete("/reglas/{id:long}", async (long id, ActorContext actor, ReglaAprobacionRepositorio repo) => Resp(await repo.EliminarAsync(actor, id)));
        reglas.MapPost("/reglas/simular", async (SimulacionRequest r, ActorContext actor, ReglaAprobacionRepositorio repo) =>
        {
            SimulacionResponse? sal = null;
            var x = await repo.SimularAsync(actor, r, s => sal = s);
            return x.Exito ? Results.Ok(sal) : Error(x);
        });

        var grupos = app.MapGroup("/api/aprobacion/grupos").RequireAuthorization().RequierePermiso(GrupoUsuariosRepositorio.Objeto);
        grupos.MapGet("", async (ActorContext actor, GrupoUsuariosRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania)));
        grupos.MapPost("", async (GrupoGuardarRequest r, ActorContext actor, GrupoUsuariosRepositorio repo) =>
        {
            var x = await repo.CrearAsync(actor, r);
            return x.Exito ? Results.Created($"/api/aprobacion/grupos/{x.Id}", new { idGrupo = x.Id }) : Error(x);
        });
        grupos.MapPut("/{id:long}", async (long id, GrupoGuardarRequest r, ActorContext actor, GrupoUsuariosRepositorio repo) => Resp(await repo.EditarAsync(actor, id, r)));
        grupos.MapPut("/{id:long}/estado", async (long id, EstadoRequest r, ActorContext actor, GrupoUsuariosRepositorio repo) => Resp(await repo.CambiarEstadoAsync(actor, id, r.Estado)));
        grupos.MapGet("/{id:long}/miembros", async (long id, ActorContext actor, GrupoUsuariosRepositorio repo) => Results.Ok(await repo.MiembrosAsync(actor.IdCompania, id)));
        grupos.MapPut("/{id:long}/miembros", async (long id, GrupoMiembrosRequest r, ActorContext actor, GrupoUsuariosRepositorio repo) => Resp(await repo.GuardarMiembrosAsync(actor, id, r.IdsUsuarios)));
    }

    private static IResult Resp(ResultadoOperacion r) => r.Exito ? Results.NoContent() : Error(r);
    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
