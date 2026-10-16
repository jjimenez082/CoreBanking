using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>Fusion de clientes duplicados: posibles duplicados de un cliente, vista previa (antes/despues) y solicitud por gestion.</summary>
public static class FusionEndpoints
{
    public static void MapFusionEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/interlocutores").RequireAuthorization();
        grupo.MapGet("/{idInterlocutor:long}/posibles-duplicados", PosiblesDuplicadosAsync).RequierePermiso("CLI_FUSION");
        grupo.MapPost("/fusion/vista-previa", VistaPreviaAsync).RequierePermiso("CLI_FUSION");
        grupo.MapPost("/fusion", SolicitarAsync).RequierePermiso("CLI_FUSION");
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje, datos = r.Datos }, statusCode: r.Estado);

    /// <summary>Clientes/personas vigentes parecidos al indicado (sin el mismo, sin prospectos ni ya fusionados).</summary>
    private static async Task<IResult> PosiblesDuplicadosAsync(
        long idInterlocutor, ActorContext actor, InterlocutorRepositorio interlocutores, CoincidenciasRepositorio coincidencias, PerfilDatosServicio perfiles)
    {
        var f = await interlocutores.ObtenerDetalleAsync(actor.IdCompania, idInterlocutor);
        if (f is null) return Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el cliente." });

        var q = new CoincidenciaRequest(
            f.Nombre1, f.Nombre2, f.Apellido1, f.Apellido2, f.FechaNacimiento?.ToDateTime(TimeOnly.MinValue),
            f.Identificaciones.Select(i => new IdentificacionCoincidenciaDto(i.IdTipoIdentificacion, i.Numero)).ToList(),
            f.Contactos.Where(c => c.TipoCodigo.StartsWith("TELEFONO", StringComparison.Ordinal) || c.TipoCodigo == "WHATSAPP").Select(c => c.Valor).ToList(),
            f.Contactos.Where(c => c.TipoCodigo == "CORREO").Select(c => c.Valor).ToList(),
            ExcluirInterlocutor: idInterlocutor,
            TipoPersona: f.TipoPersona == "Juridica" ? "Juridica" : null,
            RazonSocial: f.Organizacion?.RazonSocial,
            NombresComerciales: f.Organizacion?.NombresComerciales.Select(n => n.Nombre).ToList(),
            FechaConstitucion: f.Organizacion?.FechaConstitucion?.ToDateTime(TimeOnly.MinValue));
        var r = await coincidencias.BuscarAsync(actor.IdCompania, q);
        var items = r.Items.Where(i => i.Origen != "Prospecto" && i.Estado != "Fusionado").ToList();
        return Results.Ok(await CoincidenciaEndpoints.Enmascarar(r with { Items = items }, perfiles));
    }

    private static async Task<IResult> VistaPreviaAsync(
        FusionVistaPreviaRequest req, ActorContext actor, InterlocutorRepositorio interlocutores, GestionRepositorio gestiones, PerfilDatosServicio perfiles)
    {
        if (!await perfiles.PuedeVerClienteAsync(req.IdSobreviviente) || !await perfiles.PuedeVerClienteAsync(req.IdDuplicado))
            return Results.NotFound(new { error = "no_encontrado", mensaje = "Alguno de los clientes no existe." });
        var s = await interlocutores.ObtenerDetalleAsync(actor.IdCompania, req.IdSobreviviente);
        var d = await interlocutores.ObtenerDetalleAsync(actor.IdCompania, req.IdDuplicado);
        if (s is null || d is null) return Results.NotFound(new { error = "no_encontrado", mensaje = "Alguno de los clientes no existe." });

        var calc = await gestiones.PlanFusionAsync(actor, req.IdSobreviviente, req.IdDuplicado);
        var perfil = await perfiles.ObtenerAsync();
        return Results.Ok(new FusionVistaPreviaResponse(EnmascaradoClientes.Aplicar(s, perfil), EnmascaradoClientes.Aplicar(d, perfil), calc.Plan, calc.Impedimentos, calc.Advertencias));
    }

    private static async Task<IResult> SolicitarAsync(FusionSolicitudRequest req, ActorContext actor, GestionRepositorio gestiones, PerfilDatosServicio perfiles)
    {
        if (!await perfiles.PuedeVerClienteAsync(req.IdSobreviviente) || !await perfiles.PuedeVerClienteAsync(req.IdDuplicado))
            return Results.NotFound(new { error = "no_encontrado", mensaje = "Alguno de los clientes no existe." });
        var r = await gestiones.SolicitarFusionAsync(actor, req);
        return r.Exito
            ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value))
            : Error(r);
    }
}
