using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

public static class InterlocutorEndpoints
{
    public static void MapInterlocutorEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/interlocutores")
            .RequireAuthorization();

        // RBAC (SEG-002): cada endpoint exige su propio codigo de
        // sec.OBJETO_AUTORIZACION, sembrado en modelo_fisico_transversal_minimo.sql.
        grupo.MapPost("/", AltaAsync).RequierePermiso("CLI_ALTA");
        grupo.MapGet("/", ListarAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/buscar", BuscarAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}", DetalleAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}/detalle", DetalleAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}/360", Vista360Async).RequierePermiso("CLI_CONSULTA");
        grupo.MapPost("/{idInterlocutor:long}/revelar", RevelarAsync).RequierePermiso("CLI_CONSULTA");
        grupo.MapGet("/{idInterlocutor:long}/auditoria/periodos", async (long idInterlocutor, string? nivel, int? anio, int? mes, int? desplazamientoMin, bool? incluirFusionados,
            ActorContext actor, AuditoriaConsultaRepositorio repo) =>
            Results.Ok(await repo.PeriodosClienteAsync(actor.IdCompania, idInterlocutor, nivel ?? "anio", anio, mes, desplazamientoMin ?? 0, incluirFusionados ?? false)))
            .RequierePermiso("CLI_AUDITORIA");
        grupo.MapGet("/{idInterlocutor:long}/auditoria/eventos", async (long idInterlocutor, DateOnly fecha, int? horaDesde, int? horaHasta, int? desplazamientoMin, bool? incluirFusionados,
            int? pagina, int? tamanoPagina, ActorContext actor, AuditoriaConsultaRepositorio repo, PerfilDatosServicio perfiles) =>
            Results.Ok(await repo.EventosClienteAsync(actor.IdCompania, idInterlocutor, fecha, horaDesde ?? 0, horaHasta ?? 24, desplazamientoMin ?? 0, incluirFusionados ?? false,
                pagina ?? 1, tamanoPagina ?? 25, await perfiles.ObtenerAsync())))
            .RequierePermiso("CLI_AUDITORIA");

        // Todo cambio a datos de un cliente va por Gestion (principio del sistema):
        // este endpoint solo SOLICITA el cambio; se aplica cuando alguien con
        // GES_APROBAR lo aprueba (ver GestionEndpoints).
        grupo.MapPost("/{idInterlocutor:long}/modificacion", SolicitarModificacionAsync).RequierePermiso("CLI_EDICION");
        grupo.MapPost("/{idInterlocutor:long}/grupo/modificacion", async (long idInterlocutor, GrupoModificacionRequest request, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.SolicitarModificacionGrupoAsync(actor, idInterlocutor, request);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value)) : Error(r);
        }).RequierePermiso("CLI_EDICION");
        grupo.MapPost("/{idInterlocutor:long}/grupo/disolucion", async (long idInterlocutor, GrupoDisolucionRequest request, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.SolicitarDisolucionGrupoAsync(actor, idInterlocutor, request);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value)) : Error(r);
        }).RequierePermiso("CLI_EDICION");

        // KYC y cumplimiento: consulta con CLI_KYC_VER; la solicitud (gestion CLI_KYC) con CLI_KYC.
        grupo.MapGet("/{idInterlocutor:long}/cumplimiento", async (long idInterlocutor, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.ObtenerCumplimientoAsync(actor, idInterlocutor);
            return r is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el cliente indicado." }) : Results.Ok(r);
        }).RequierePermiso("CLI_KYC_VER");
        grupo.MapPost("/{idInterlocutor:long}/cumplimiento", async (long idInterlocutor, CumplimientoRequest request, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.SolicitarCumplimientoAsync(actor, idInterlocutor, request);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value)) : Error(r);
        }).RequierePermiso("CLI_KYC");

        // Vigencia del KYC (en tiempo real): una ficha, la guardia de apertura de productos y el listado por situacion (insumo del reporte).
        grupo.MapGet("/kyc/situacion", async (string? situacion, ActorContext actor, InterlocutorRepositorio repo) => Results.Ok(await repo.ListarKycPorSituacionAsync(actor.IdCompania, situacion)))
            .RequierePermiso("CLI_KYC_VER");
        grupo.MapGet("/{idInterlocutor:long}/kyc/vigencia", async (long idInterlocutor, ActorContext actor, InterlocutorRepositorio repo) =>
        {
            var r = await repo.ObtenerKycVigenciaAsync(actor.IdCompania, idInterlocutor);
            return r is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "El cliente no existe o no tiene expediente KYC." }) : Results.Ok(r);
        }).RequierePermiso("CLI_KYC_VER");
        grupo.MapGet("/{idInterlocutor:long}/kyc/apertura", async (long idInterlocutor, ActorContext actor, InterlocutorRepositorio repo) =>
            Results.Ok(await repo.ConsultarAperturaKycAsync(actor.IdCompania, idInterlocutor))).RequierePermiso("CLI_KYC_VER");

        // Bloqueo de clientes: consulta con CLI_BLOQUEO_VER; bloquear y desbloquear (gestion con aprobacion, carril propio) con CLI_BLOQUEO.
        grupo.MapGet("/bloqueos", async (ActorContext actor, InterlocutorRepositorio repo) => Results.Ok(await repo.ListarBloqueosVigentesAsync(actor.IdCompania)))
            .RequierePermiso("CLI_BLOQUEO_VER");
        grupo.MapGet("/{idInterlocutor:long}/restricciones", async (long idInterlocutor, ActorContext actor, RestriccionBloqueoRepositorio repo) =>
        {
            var r = await repo.RestriccionesClienteAsync(actor.IdCompania, idInterlocutor);
            return r is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el cliente indicado." }) : Results.Ok(r);
        }).RequierePermiso("CLI_BLOQUEO_VER");
        grupo.MapGet("/{idInterlocutor:long}/bloqueos", async (long idInterlocutor, ActorContext actor, InterlocutorRepositorio repo) =>
        {
            var r = await repo.ObtenerBloqueosClienteAsync(actor.IdCompania, idInterlocutor);
            return r is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el cliente indicado." }) : Results.Ok(r);
        }).RequierePermiso("CLI_BLOQUEO_VER");
        grupo.MapPost("/{idInterlocutor:long}/bloqueos", async (long idInterlocutor, BloqueoSolicitudRequest request, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.SolicitarBloqueoAsync(actor, idInterlocutor, request, true);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value)) : Error(r);
        }).RequierePermiso("CLI_BLOQUEO");
        grupo.MapPost("/{idInterlocutor:long}/desbloqueos", async (long idInterlocutor, BloqueoSolicitudRequest request, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.SolicitarBloqueoAsync(actor, idInterlocutor, request, false);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value)) : Error(r);
        }).RequierePermiso("CLI_BLOQUEO");

        // Consentimientos del cliente: consulta con CLI_CONSENTIMIENTO_VER; otorgar y revocar (gestion con aprobacion, carril propio) con CLI_CONSENTIMIENTO.
        grupo.MapGet("/{idInterlocutor:long}/consentimientos", async (long idInterlocutor, ActorContext actor, InterlocutorRepositorio repo) =>
        {
            var r = await repo.ObtenerConsentimientosClienteAsync(actor.IdCompania, idInterlocutor);
            return r is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el cliente indicado." }) : Results.Ok(r);
        }).RequierePermiso("CLI_CONSENTIMIENTO_VER");
        grupo.MapPost("/{idInterlocutor:long}/consentimientos", async (long idInterlocutor, ConsentimientoSolicitudRequest request, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.SolicitarConsentimientosAsync(actor, idInterlocutor, request);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value)) : Error(r);
        }).RequierePermiso("CLI_CONSENTIMIENTO");

        // Fallecimiento: gestion con aprobacion obligatoria; el acta de defuncion viaja como adjunto (X-Adjuntos). Irreversible.
        grupo.MapPost("/{idInterlocutor:long}/fallecimiento", async (long idInterlocutor, FallecimientoSolicitudRequest request, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.SolicitarFallecimientoAsync(actor, idInterlocutor, request);
            return r.Exito ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actor, r.Id!.Value)) : Error(r);
        }).RequierePermiso("CLI_FALLECIMIENTO");
        grupo.MapGet("/{idInterlocutor:long}/fallecimiento", async (long idInterlocutor, ActorContext actor, GestionRepositorio gestiones) =>
        {
            var r = await gestiones.ObtenerFallecimientoAsync(actor.IdCompania, idInterlocutor);
            return r is null ? Results.NoContent() : Results.Ok(r);
        }).RequierePermiso("CLI_CONSULTA");

        // Borradores del wizard de alta: privados de su propietario (cada consulta filtra por usuario).
        grupo.MapGet("/borradores", ListarBorradoresAsync).RequierePermiso("CLI_ALTA");
        grupo.MapGet("/borradores/{idBorrador:long}", ObtenerBorradorAsync).RequierePermiso("CLI_ALTA");
        grupo.MapPut("/borradores", GuardarBorradorAsync).RequierePermiso("CLI_ALTA");
        grupo.MapDelete("/borradores/{idBorrador:long}", EliminarBorradorAsync).RequierePermiso("CLI_ALTA");
    }

    private static IResult Error(ResultadoOperacion r) =>
        Results.Json(new { error = r.Error, mensaje = r.Mensaje, datos = r.Datos }, statusCode: r.Estado);

    private static async Task<IResult> ListarAsync(
        string? q, string? tipo, int? pagina, int? tamanoPagina, ActorContext actorContext, InterlocutorRepositorio repositorio, PerfilDatosServicio perfiles)
    {
        var paginaValida = pagina is > 0 ? pagina.Value : 1;
        var tamanoValido = tamanoPagina is > 0 and <= 100 ? tamanoPagina.Value : 20;

        var perfil = await perfiles.ObtenerAsync();
        var (items, total) = await repositorio.ListarAsync(actorContext.IdCompania, q, paginaValida, tamanoValido, tipo is "Fisica" or "Juridica" or "Grupo" ? tipo : null, perfil);

        return Results.Ok(new InterlocutorListaResponse(items.Select(x => EnmascaradoClientes.Aplicar(x, perfil)).ToList(), paginaValida, tamanoValido, total));
    }

    /// <summary>Busqueda global de la barra superior: cualquier rol; el usuario elige y abre la vista 360.</summary>
    private static async Task<IResult> BuscarAsync(string? q, string? rol, ActorContext actorContext, InterlocutorRepositorio repositorio, PerfilDatosServicio perfiles)
    {
        var perfil = await perfiles.ObtenerAsync();
        var r = await repositorio.BuscarAsync(actorContext.IdCompania, q, rol, perfil);
        return Results.Ok(r.Select(x => EnmascaradoClientes.Aplicar(x, perfil)).ToList());
    }

    private static async Task<IResult> AltaAsync(
        InterlocutorAltaRequest request, ActorContext actorContext, InterlocutorRepositorio repositorio, GestionRepositorio gestiones,
        HttpContext http, MensajesRepositorio mensajes, IdempotenciaRepositorio idempotencia)
    {
        // Backoffice (usuario nombrado): el alta es una gestion; segun la regla de aprobacion el cliente se crea de inmediato o al aprobarse.
        if (!actorContext.EsIntegracion)
        {
            var g = await gestiones.SolicitarAltaInterlocutorAsync(actorContext, request);
            if (!g.Exito) return Error(g);

            var idCliente = await gestiones.ClienteCreadoPorAltaAsync(g.Id!.Value);
            var ficha = idCliente is null ? null : await repositorio.ObtenerDetalleAsync(actorContext.IdCompania, idCliente.Value);
            var respuesta = new InterlocutorAltaGestionResponse(g.Id.Value, g.Numero, idCliente is not null, ficha);
            return idCliente is null
                ? Results.Json(respuesta, statusCode: StatusCodes.Status202Accepted)
                : Results.Created($"/api/interlocutores/{idCliente}", respuesta);
        }

        // Integraciones (API): el alta tambien es una gestion. Contrato de respuesta (ver AltaApiAsync):
        //   201 CLIENTE_CREADO | 202 GESTION_PENDIENTE | 4xx/5xx ERROR; siempre con Idempotency-Key e idCorrelacion.
        return await AltaApiAsync(request, actorContext, gestiones, http, mensajes, idempotencia);
    }

    private static readonly Dictionary<string, string> AliasError = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RANGO_AGOTADO"] = "NUMERACION_AGOTADA",
        ["RANGO_NO_CONFIGURADO"] = "NUMERACION_AGOTADA",
        ["TIPO_GESTION_NO_CONFIGURADO"] = "CONFIGURACION_INCOMPLETA",
        ["ROL_NO_CONFIGURADO"] = "CONFIGURACION_INCOMPLETA",
    };

    private static async Task<IResult> AltaApiAsync(
        InterlocutorAltaRequest request, ActorContext actor, GestionRepositorio gestiones,
        HttpContext http, MensajesRepositorio mensajes, IdempotenciaRepositorio idempotencia)
    {
        var correlacion = http.Request.Headers["X-Correlation-Id"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(correlacion) || correlacion.Length > 64) correlacion = Guid.NewGuid().ToString("N");
        http.Response.Headers["X-Correlation-Id"] = correlacion;

        // Arma la respuesta desde el maestro de mensajes (el texto es parametrizable; el codigo es estable).
        async Task<(int Estado, RespuestaAltaApi Cuerpo)> Armar(string nombre, Dictionary<string, string>? valores = null,
            string? numeroCliente = null, long? idCliente = null, string? numeroGestion = null, string? motivo = null,
            IReadOnlyList<string>? detalles = null, IReadOnlyList<CoincidenciaApi>? coincidencias = null)
        {
            var m = await mensajes.ResolverAsync(actor.IdCompania, nombre, valores)
                    ?? await mensajes.ResolverAsync(actor.IdCompania, "ERROR_INTERNO")
                    ?? new MensajesRepositorio.Resuelto("ER_SIS_001", "ERROR_INTERNO", "ERROR", "RECHAZADO", 500, "Ocurrio un error inesperado.");
            return (m.Http, new RespuestaAltaApi(m.Severidad, m.Resultado, m.Codigo, m.Nombre, m.Texto, correlacion,
                numeroCliente, idCliente, numeroGestion, motivo, detalles, coincidencias));
        }
        static IResult Salida(int estado, RespuestaAltaApi cuerpo) => Results.Json(cuerpo, statusCode: estado);

        // 1. Idempotency-Key obligatoria.
        var llave = http.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(llave)) { var (e, b) = await Armar("IDEMPOTENCY_KEY_REQUERIDA"); return Salida(e, b); }
        if (llave.Length is < 8 or > 100) { var (e, b) = await Armar("IDEMPOTENCY_KEY_INVALIDA"); return Salida(e, b); }

        // La integracion no puede imponer borradores, devoluciones, prospectos ni revisiones: eso es del Backoffice.
        var req = request with { IdBorrador = null, IdGestionDevuelta = null, IdProspecto = null, RevisionDuplicados = null };
        var huella = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(req)))).ToLowerInvariant();

        // 2. Reserva (o repeticion) de la llave.
        const string operacion = "INTERLOCUTOR_ALTA";
        var (nueva, reg) = await idempotencia.ReservarAsync(actor, operacion, llave, huella, correlacion);
        if (!nueva)
        {
            if (reg.Huella != huella) { var (e, b) = await Armar("IDEMPOTENCY_KEY_CONFLICTO"); return Salida(e, b); }
            if (reg.Estado != "COMPLETA") { var (e, b) = await Armar("IDEMPOTENCY_EN_PROCESO"); return Salida(e, b); }
            http.Response.Headers["Idempotent-Replay"] = "true";
            return Results.Content(reg.Respuesta ?? "{}", "application/json", System.Text.Encoding.UTF8, reg.HttpEstado ?? 200);
        }

        try
        {
            // 3. Alta como gestion: la regla de aprobacion decide si se aplica ya o queda pendiente.
            var g = await gestiones.SolicitarAltaInterlocutorAsync(actor, req);

            int estado; RespuestaAltaApi cuerpo;
            if (!g.Exito)
            {
                var codigo = AliasError.GetValueOrDefault(g.Error ?? "", (g.Error ?? "ERROR_INTERNO").ToUpperInvariant());
                var valores = g.Datos as Dictionary<string, string>;
                IReadOnlyList<string>? detalles = codigo == "DATOS_INVALIDOS" && g.Mensaje is not null
                    ? g.Mensaje.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
                (estado, cuerpo) = await Armar(codigo, valores, detalles: detalles);
                // Un codigo sin mensaje en el maestro nunca expone el texto interno (podria nombrar datos de otros clientes).
            }
            else
            {
                var idCliente = await gestiones.ClienteCreadoPorAltaAsync(g.Id!.Value);
                if (idCliente is not null)
                {
                    var numero = await gestiones.NumeroClienteAsync(idCliente.Value);
                    (estado, cuerpo) = await Armar("CLIENTE_CREADO", new() { ["numeroCliente"] = numero ?? "" }, numeroCliente: numero, idCliente: idCliente);
                }
                else
                {
                    var coincidencias = g.Datos as IReadOnlyList<CoincidenciaApi>;
                    var porCoincidencia = coincidencias is not null;
                    (estado, cuerpo) = await Armar(
                        porCoincidencia ? "GESTION_PENDIENTE_COINCIDENCIAS" : "GESTION_PENDIENTE_APROBACION",
                        new() { ["numeroGestion"] = g.Numero ?? "" }, numeroGestion: g.Numero,
                        motivo: porCoincidencia ? "REVISION_COINCIDENCIAS" : "APROBACION_REQUERIDA", coincidencias: coincidencias);
                }
            }

            if (estado >= 500) { await idempotencia.LiberarAsync(reg.Id); return Salida(estado, cuerpo); }
            await idempotencia.CompletarAsync(reg.Id, estado, System.Text.Json.JsonSerializer.Serialize(cuerpo,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            return Salida(estado, cuerpo);
        }
        catch
        {
            await idempotencia.LiberarAsync(reg.Id);
            throw;
        }
    }

    private static async Task<IResult> DetalleAsync(
        long idInterlocutor, ActorContext actorContext, InterlocutorRepositorio repositorio, PerfilDatosServicio perfiles)
    {
        var detalle = await repositorio.ObtenerDetalleAsync(actorContext.IdCompania, idInterlocutor);

        return detalle is null
            ? Results.NotFound(new { error = "no_encontrado", idInterlocutor })
            : Results.Ok(EnmascaradoClientes.Aplicar(detalle, await perfiles.ObtenerAsync()));
    }

    private static async Task<IResult> Vista360Async(
        long idInterlocutor, ActorContext actorContext, InterlocutorRepositorio repositorio, PerfilDatosServicio perfiles)
    {
        var vista = await repositorio.Obtener360Async(actorContext.IdCompania, idInterlocutor);

        return vista is null
            ? Results.NotFound(new { error = "no_encontrado", idInterlocutor })
            : Results.Ok(vista with { Ficha = EnmascaradoClientes.Aplicar(vista.Ficha, await perfiles.ObtenerAsync()) });
    }

    private static async Task<IResult> RevelarAsync(
        long idInterlocutor, RevelarRequest req, ActorContext actorContext, InterlocutorRepositorio repositorio, PerfilDatosServicio perfiles)
    {
        var perfil = await perfiles.ObtenerAsync();
        if (PerfilDatosCatalogo.BuscarCampo(req.Campo ?? "") is null)
            return Results.BadRequest(new { error = "campo_invalido", mensaje = "El campo no existe." });
        if (!perfil.Enmascara(req.Campo!)) return Results.BadRequest(new { error = "no_enmascarado", mensaje = "Ese dato no está enmascarado para usted." });
        if (!perfil.PuedeRevelar(req.Campo!))
            return Results.Json(new { error = "revelado_no_permitido", mensaje = "Su perfil no permite ver este dato completo." }, statusCode: 403);
        var real = await repositorio.ObtenerDetalleAsync(actorContext.IdCompania, idInterlocutor);
        var valores = real is null ? null : EnmascaradoClientes.Reales(real, req.Campo!, req.IdItem);
        if (valores is null) return Results.NotFound(new { error = "no_encontrado", mensaje = "No se encontró el dato." });
        await perfiles.RegistrarRevelacionAsync(req.Campo!, idInterlocutor, req.IdItem is null ? null : $"item {req.IdItem}");
        return Results.Ok(new RevelarResponse(req.Campo!, req.IdItem, valores));
    }

    private static async Task<IResult> SolicitarModificacionAsync(
        long idInterlocutor, InterlocutorModificacionRequest request, ActorContext actorContext, GestionRepositorio gestiones,
        InterlocutorRepositorio repositorio, PerfilDatosServicio perfiles)
    {
        var perfil = await perfiles.ObtenerAsync();
        if (perfil.HayMascaras)
        {
            var real = await repositorio.ObtenerDetalleAsync(actorContext.IdCompania, idInterlocutor);
            if (real is not null) request = EnmascaradoClientes.Restaurar(request, real, perfil);
        }
        var r = await gestiones.SolicitarModificacionInterlocutorAsync(actorContext, idInterlocutor, request);

        return r.Exito
            ? Results.Created($"/api/gestiones/{r.Id}", await gestiones.ObtenerDetalleAsync(actorContext, r.Id!.Value))
            : Error(r);
    }

    // ----- Borradores ----------------------------------------------------------------

    private static async Task<IResult> ListarBorradoresAsync(ActorContext actorContext, InterlocutorRepositorio repositorio)
        => Results.Ok(await repositorio.ListarBorradoresAsync(actorContext));

    private static async Task<IResult> ObtenerBorradorAsync(
        long idBorrador, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var b = await repositorio.ObtenerBorradorAsync(actorContext, idBorrador);
        return b is null ? Results.NotFound(new { error = "borrador_no_encontrado", idBorrador }) : Results.Ok(b);
    }

    private static async Task<IResult> GuardarBorradorAsync(
        BorradorGuardarRequest request, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var r = await repositorio.GuardarBorradorAsync(actorContext, request);
        if (!r.Exito) return Error(r);

        var guardado = await repositorio.ObtenerBorradorAsync(actorContext, r.Id!.Value);
        return Results.Ok(new { idBorrador = r.Id, venceEn = guardado?.VenceEn });
    }

    private static async Task<IResult> EliminarBorradorAsync(
        long idBorrador, ActorContext actorContext, InterlocutorRepositorio repositorio)
    {
        var r = await repositorio.EliminarBorradorAsync(actorContext, idBorrador);
        return r.Exito ? Results.NoContent() : Error(r);
    }
}
