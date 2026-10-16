using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CoreBancario.Api.Endpoints;

/// <summary>Relaciones entre interlocutores: consulta, tipos configurados y cambio por gestion (CLI_RELACION).</summary>
public static class RelacionEndpoints
{
    public static void MapRelacionEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/interlocutores").RequireAuthorization();
        grupo.MapGet("/relaciones/tipos", TiposAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}/relaciones", RelacionesAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapPost("/{idInterlocutor:long}/relaciones", SolicitarAsync).RequierePermiso("CLI_RELACION");
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje, datos = r.Datos }, statusCode: r.Estado);

    private static async Task<IResult> TiposAsync(TenantContext tenant)
    {
        using var c = new SqlConnection(tenant.ConnectionString);
        var tipos = await RelacionServicio.TiposAsync(c, null);
        return Results.Ok(tipos.Where(t => t.Estado == "Activo")
            .Select(t => new RelacionTipoResponse(t.Id, t.Codigo, t.Nombre, t.Inversa, t.UsaPct, t.Simetrica, t.Origen, t.Destino)));
    }

    private static async Task<IResult> RelacionesAsync(long idInterlocutor, ActorContext actor, TenantContext tenant)
    {
        using var c = new SqlConnection(tenant.ConnectionString);
        var yo = await c.QuerySingleOrDefaultAsync(
            "SELECT numero, estado, tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @id AND id_compania = @comp", new { id = idInterlocutor, comp = actor.IdCompania });
        if (yo is null) return Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el interlocutor." });
        var vigentes = await RelacionServicio.ListarAsync(c, null, actor.IdCompania, idInterlocutor, true);
        var historial = await RelacionServicio.ListarAsync(c, null, actor.IdCompania, idInterlocutor, false);
        var pend = await FusionServicio.GestionPendienteAsync(c, null, actor.IdCompania, idInterlocutor);
        var activo = (string)yo.estado == "Activo";
        return Results.Ok(new RelacionesInterlocutorResponse(
            idInterlocutor, (string)yo.numero, (string)yo.estado, (string)yo.tipo_persona, vigentes, historial, pend,
            activo && pend is null && actor.TienePermiso("CLI_RELACION")));
    }

    private static async Task<IResult> SolicitarAsync(long idInterlocutor, RelacionSolicitudRequest req, ActorContext actor, GestionRepositorio gestiones)
    {
        var r = await gestiones.SolicitarCambioRelacionAsync(actor, idInterlocutor, req);
        return r.Exito
            ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value))
            : Error(r);
    }
}
