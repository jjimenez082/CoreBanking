using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Roles del interlocutor: consulta, cambio por gestion (CLI_ROL) y datos minimos que exige cada rol.</summary>
public static class RolEndpoints
{
    public static void MapRolEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/interlocutores").RequireAuthorization();
        grupo.MapGet("/{idInterlocutor:long}/roles", RolesAsync).RequierePermiso("CLI_ROL_CONSULTA");
        grupo.MapPost("/{idInterlocutor:long}/roles", SolicitarAsync).RequierePermiso("CLI_ROL");
        // Para armar el alta de una persona con un rol distinto de Cliente (los requisitos se muestran en el formulario).
        // Configuracion no sensible que el asistente necesita tanto en el alta como en la modificacion: basta estar autenticado.
        grupo.MapGet("/roles/requisitos", RequisitosAltaAsync);
        grupo.MapGet("/roles/validar-identificacion", ValidarIdentificacionAsync).RequierePermiso("CLI_ALTA");

        var admin = app.MapGroup("/api/roles").RequireAuthorization();
        admin.MapGet("/requisitos", RequisitosAdminAsync).RequierePermiso(RolRequisitoRepositorio.ObjetoAdmin);
        admin.MapPut("/requisitos", GuardarAsync).RequierePermiso(RolRequisitoRepositorio.ObjetoAdmin);
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje, datos = r.Datos }, statusCode: r.Estado);

    private static async Task<IResult> RolesAsync(long idInterlocutor, ActorContext actor, RolRequisitoRepositorio repo)
    {
        var r = await repo.RolesDeClienteAsync(actor.IdCompania, idInterlocutor);
        return r is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el cliente." }) : Results.Ok(r);
    }

    private static async Task<IResult> SolicitarAsync(long idInterlocutor, RolSolicitudRequest req, ActorContext actor, GestionRepositorio gestiones)
    {
        var r = await gestiones.SolicitarCambioRolAsync(actor, idInterlocutor, req);
        return r.Exito
            ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value))
            : Error(r);
    }

    private static string Tipo(string? t) => ReglasCampos.TipoValido(t) ? t! : "Fisica";

    private static async Task<IResult> RequisitosAltaAsync(ActorContext actor, RolRequisitoRepositorio repo, string? tipo = null)
        => Results.Ok(await repo.ListarAsync(actor.IdCompania, true, Tipo(tipo)));

    /// <summary>La ficha del cliente existente se devuelve para mostrarla de solo lectura antes de pedir el rol.</summary>
    private static async Task<IResult> ValidarIdentificacionAsync(
        long idTipo, string numero, long idTipoRol, ActorContext actor, RolRequisitoRepositorio repo, InterlocutorRepositorio interlocutores, PerfilDatosServicio perfiles)
    {
        var r = await repo.ValidarIdentificacionAsync(actor, idTipo, numero, idTipoRol);
        if (r.IdInterlocutor is not null && r.Estado is "PUEDE_AGREGAR" or "FALTAN_DATOS" or "YA_TIENE_ROL" or "GESTION_PENDIENTE")
        {
            // El cliente existente se muestra con el perfil de datos del usuario; si no puede ver ese cliente, no se revela su ficha.
            if (await perfiles.PuedeVerClienteAsync(r.IdInterlocutor.Value))
            {
                var ficha = await interlocutores.ObtenerDetalleAsync(actor.IdCompania, r.IdInterlocutor.Value);
                r = r with { Cliente = ficha is null ? null : EnmascaradoClientes.Aplicar(ficha, await perfiles.ObtenerAsync()) };
            }
            else r = r with { Cliente = null };
        }
        return Results.Ok(r);
    }

    private static async Task<IResult> RequisitosAdminAsync(ActorContext actor, RolRequisitoRepositorio repo, string? tipo = null)
        => Results.Ok(new RolRequisitosAdminResponse(RolRequisitoRepositorio.Campos(Tipo(tipo)), await repo.ListarAsync(actor.IdCompania, false, Tipo(tipo)), Tipo(tipo)));

    private static async Task<IResult> GuardarAsync(RolRequisitosGuardarRequest req, ActorContext actor, RolRequisitoRepositorio repo)
    {
        var r = await repo.GuardarAsync(actor, req);
        var t = Tipo(req.TipoPersona);
        return r.Exito ? Results.Ok(new RolRequisitosAdminResponse(RolRequisitoRepositorio.Campos(t), await repo.ListarAsync(actor.IdCompania, false, t), t)) : Error(r);
    }
}
