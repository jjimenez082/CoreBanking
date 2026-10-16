using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Opciones administrativas transversales, cada una con su permiso: monedas, tipo de cambio, calendario de feriados,
/// parametros generales, consulta de auditoria y usuarios de integracion. Los cambios directos se auditan en cada repositorio;
/// la carga de tipo de cambio pasa por gestion (se audita al aprobarse).
/// </summary>
public static class AdminTransversalEndpoints
{
    /// <summary>Tope de la bitacora general: maximo 7 dias por consulta (se exime si se busca por un registro o una gestion concretos).</summary>
    public const int MaxDiasAuditoria = 7;

    private static string? RangoExcedido(string? llave, long? idGestion, DateOnly? desde, DateOnly? hasta)
    {
        if (idGestion is not null || !string.IsNullOrWhiteSpace(llave)) return null;
        if (desde is null || hasta is null)
            return $"Indique el rango de fechas (desde y hasta); el máximo es de {MaxDiasAuditoria} días. Para ver todo el historial de un cliente use su ficha.";
        if (hasta < desde) return "La fecha 'hasta' no puede ser anterior a 'desde'.";
        if (hasta.Value.DayNumber - desde.Value.DayNumber + 1 > MaxDiasAuditoria)
            return $"El rango máximo es de {MaxDiasAuditoria} días. Acote las fechas o busque por N.º de registro o de gestión.";
        return null;
    }

    public static void MapAdminTransversalEndpoints(this WebApplication app)
    {
        // ----- Monedas -----
        app.MapGet("/api/catalogos/monedas", async (ActorContext actor, MonedaRepositorio repo) =>
            Results.Ok(await repo.ListarAsync(actor.IdCompania, true))).RequireAuthorization();

        var mon = app.MapGroup("/api/monedas").RequireAuthorization().RequierePermiso(MonedaRepositorio.ObjetoAdministracion);
        mon.MapGet("/", async (ActorContext actor, MonedaRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania, false)));
        mon.MapPost("/", async (MonedaGuardarRequest req, ActorContext actor, MonedaRepositorio repo) =>
        {
            var r = await repo.CrearAsync(actor, req);
            return r.Exito ? Results.Created($"/api/monedas/{r.Id}", new { idMoneda = r.Id }) : Error(r);
        });
        mon.MapPut("/{id:long}", async (long id, MonedaGuardarRequest req, ActorContext actor, MonedaRepositorio repo) => Sin(await repo.EditarAsync(actor, id, req)));
        mon.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, MonedaRepositorio repo) => Sin(await repo.CambiarEstadoAsync(actor, id, req.Estado)));
        mon.MapPut("/{id:long}/base", async (long id, ActorContext actor, MonedaRepositorio repo) => Sin(await repo.DefinirBaseAsync(actor, id)));

        // ----- Tipo de cambio -----
        var tc = app.MapGroup("/api/tipos-cambio").RequireAuthorization();
        tc.MapGet("/", async (long? idMoneda, DateOnly? desde, DateOnly? hasta, ActorContext actor, TipoCambioRepositorio repo) =>
            Results.Ok(await repo.ListarAsync(actor.IdCompania, idMoneda, desde, hasta))).RequierePermiso(TipoCambioRepositorio.PermisoConsulta);
        tc.MapGet("/vigentes", async (DateOnly? fecha, ActorContext actor, TipoCambioRepositorio repo) =>
            Results.Ok(await repo.VigentesAsync(actor.IdCompania, fecha ?? DateOnly.FromDateTime(DateTime.Now)))).RequierePermiso(TipoCambioRepositorio.PermisoConsulta);
        tc.MapPost("/solicitud", async (TipoCambioSolicitudRequest req, ActorContext actor, TipoCambioRepositorio repo) =>
        {
            var r = await repo.SolicitarCargaAsync(actor, req);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", new { idGestion = r.Id, numero = r.Numero }) : Error(r);
        }).RequierePermiso(TipoCambioRepositorio.PermisoCarga);

        // ----- Calendario -----
        var cal = app.MapGroup("/api/feriados").RequireAuthorization().RequierePermiso(FeriadoRepositorio.ObjetoAdministracion);
        cal.MapGet("/", async (long? idPais, int? anio, ActorContext actor, FeriadoRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania, idPais, anio)));
        cal.MapPost("/", async (FeriadoGuardarRequest req, ActorContext actor, FeriadoRepositorio repo) =>
        {
            var r = await repo.CrearAsync(actor, req);
            return r.Exito ? Results.Created($"/api/feriados/{r.Id}", new { idFeriado = r.Id }) : Error(r);
        });
        cal.MapPut("/{id:long}", async (long id, FeriadoGuardarRequest req, ActorContext actor, FeriadoRepositorio repo) => Sin(await repo.EditarAsync(actor, id, req)));
        cal.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, FeriadoRepositorio repo) => Sin(await repo.CambiarEstadoAsync(actor, id, req.Estado)));

        // Consulta de dias habiles: para cualquier usuario autenticado (la usaran cuentas y creditos).
        app.MapGet("/api/calendario/dia-habil", async (long idPais, DateOnly fecha, ActorContext actor, FeriadoRepositorio repo) =>
            Results.Ok(await repo.EsHabilAsync(actor.IdCompania, idPais, fecha))).RequireAuthorization();
        app.MapGet("/api/calendario/proximo-habil", async (long idPais, DateOnly desde, int? dias, ActorContext actor, FeriadoRepositorio repo) =>
            Results.Ok(await repo.ProximoHabilAsync(actor.IdCompania, idPais, desde, dias ?? 0))).RequireAuthorization();

        // ----- Parametros -----
        var par = app.MapGroup("/api/parametros").RequireAuthorization().RequierePermiso(ParametroRepositorio.ObjetoAdministracion);
        par.MapGet("/", async (ActorContext actor, ParametroRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania)));
        par.MapPut("/{id:long}", async (long id, ParametroValorRequest req, ActorContext actor, ParametroRepositorio repo) =>
            Sin(await repo.CambiarValorAsync(actor, id, req.Valor, false)));
        par.MapPost("/{id:long}/restablecer", async (long id, ActorContext actor, ParametroRepositorio repo) =>
            Sin(await repo.CambiarValorAsync(actor, id, null, true)));

        // ----- Restricciones por tipo de bloqueo (que acciones impide cada tipo) -----
        var rest = app.MapGroup("/api/bloqueos-restricciones").RequireAuthorization().RequierePermiso(RestriccionBloqueoRepositorio.ObjetoAdministracion);
        rest.MapGet("/", async (RestriccionBloqueoRepositorio repo) => Results.Ok(await repo.ObtenerMatrizAsync()));
        rest.MapPut("/", async (MatrizRestriccionRequest req, ActorContext actor, RestriccionBloqueoRepositorio repo) => Sin(await repo.GuardarMatrizAsync(actor, req)));
        rest.MapPost("/tipos", async (TipoBloqueoRequest req, ActorContext actor, RestriccionBloqueoRepositorio repo) => Sin(await repo.CrearTipoBloqueoAsync(actor, req)));
        rest.MapPut("/tipos/{idTipo:long}", async (long idTipo, TipoBloqueoRequest req, ActorContext actor, RestriccionBloqueoRepositorio repo) => Sin(await repo.ActualizarTipoBloqueoAsync(actor, idTipo, req)));

        // ----- Auditoria -----
        var aud = app.MapGroup("/api/auditoria").RequireAuthorization().RequierePermiso(AuditoriaConsultaRepositorio.ObjetoConsulta);
        aud.MapGet("/", async (string? entidad, string? llave, long? idUsuario, string? accion, DateOnly? desde, DateOnly? hasta,
            long? idGestion, string? texto, int? pagina, int? tamanoPagina, ActorContext actor, AuditoriaConsultaRepositorio repo, PerfilDatosServicio perfiles) =>
            RangoExcedido(llave, idGestion, desde, hasta) is { } err
                ? Results.BadRequest(new { error = "rango_excedido", mensaje = err })
                : Results.Ok(await repo.BuscarAsync(actor.IdCompania, entidad, llave, idUsuario, accion, desde, hasta, idGestion, texto, pagina ?? 1, tamanoPagina ?? 50, await perfiles.ObtenerAsync())));
        aud.MapGet("/eventos", async (string? entidad, string? llave, long? idUsuario, string? accion, DateOnly? desde, DateOnly? hasta,
            long? idGestion, string? texto, int? pagina, int? tamanoPagina, ActorContext actor, AuditoriaConsultaRepositorio repo, PerfilDatosServicio perfiles) =>
            RangoExcedido(llave, idGestion, desde, hasta) is { } err
                ? Results.BadRequest(new { error = "rango_excedido", mensaje = err })
                : Results.Ok(await repo.BuscarEventosAsync(actor.IdCompania, entidad, llave, idUsuario, accion, desde, hasta, idGestion, texto, pagina ?? 1, tamanoPagina ?? 25, await perfiles.ObtenerAsync())));
        aud.MapGet("/entidades", async (ActorContext actor, AuditoriaConsultaRepositorio repo) => Results.Ok(await repo.EntidadesAsync(actor.IdCompania)));

        // ----- Usuarios de integracion -----
        var integ = app.MapGroup("/api/integraciones").RequireAuthorization().RequierePermiso(IntegracionRepositorio.ObjetoAdministracion);
        integ.MapGet("/", async (ActorContext actor, IntegracionRepositorio repo) => Results.Ok(await repo.ListarAsync(actor.IdCompania)));
        integ.MapGet("/usuarios-tecnicos", async (ActorContext actor, IntegracionRepositorio repo) => Results.Ok(await repo.UsuariosTecnicosAsync(actor.IdCompania)));
        integ.MapGet("/estado-integracion", (IntegracionRepositorio repo) => Results.Ok(new { keycloakConfigurado = repo.KeycloakConfigurado, codigoCompania = repo.CodigoCompania }));
        integ.MapPost("/", async (IntegracionCrearRequest req, ActorContext actor, IntegracionRepositorio repo) =>
        {
            var (r, cred) = await repo.CrearAsync(actor, req);
            return r.Exito ? Results.Created($"/api/integraciones/{r.Id}", cred) : Error(r);
        });
        integ.MapPost("/{id:long}/regenerar-secret", async (long id, ActorContext actor, IntegracionRepositorio repo) =>
        {
            var (r, cred) = await repo.RegenerarSecretAsync(actor, id);
            return r.Exito ? Results.Ok(cred) : Error(r);
        });
        integ.MapPut("/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, IntegracionRepositorio repo) =>
            Sin(await repo.CambiarEstadoAsync(actor, id, req.Estado)));
        integ.MapPut("/{id:long}/usuario", async (long id, IntegracionUsuarioRequest req, ActorContext actor, IntegracionRepositorio repo) =>
            Sin(await repo.AsignarUsuarioAsync(actor, id, req.IdUsuario)));
        integ.MapPut("/{id:long}/canal", async (long id, IntegracionCanalRequest req, ActorContext actor, IntegracionRepositorio repo) =>
            Sin(await repo.AsignarCanalAsync(actor, id, req.IdCanal)));
    }

    private static IResult Sin(ResultadoOperacion r) => r.Exito ? Results.NoContent() : Error(r);
    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);
}
