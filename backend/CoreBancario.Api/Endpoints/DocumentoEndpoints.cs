using System.Globalization;
using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Gestor documental: tipos de documento, subida de adjuntos temporales, arbol de documentos del cliente (solo lectura), busqueda,
/// adjuntos de una gestion y descarga. Agregar, reemplazar y obsoletar documentos del expediente se piden con POST /solicitud (gestion CLI_DOCUMENTO).
/// El formulario se lee a mano (sin IFormFile) para no depender del middleware antifalsificacion en una API con token Bearer.
/// </summary>
public static class DocumentoEndpoints
{
    public static void MapDocumentoEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/documentos").RequireAuthorization();
        // Catalogo de tipos: lo usan los adjuntos de una gestion y el Gestor documental; basta estar autenticado.
        grupo.MapGet("/tipos", TiposAsync);
        grupo.MapGet("/temporales", MisTemporalesAsync).RequierePermiso(DocumentoRepositorio.PermisoAdjuntar);
        grupo.MapPost("/temporales", SubirTemporalAsync).RequierePermiso(DocumentoRepositorio.PermisoAdjuntar);
        grupo.MapDelete("/temporales/{idDocumento:long}", QuitarTemporalAsync).RequierePermiso(DocumentoRepositorio.PermisoAdjuntar);
        // La visibilidad fina (solicitante, aprobador, confidencialidad) se resuelve dentro; basta estar autenticado.
        grupo.MapGet("/{idDocumento:long}/descargar", DescargarAsync);

        // Gestor documental (r44b): expediente vivo del cliente, sin gestion de aprobacion (cada accion se audita).
        grupo.MapGet("/clientes", ClientesAsync).RequierePermiso(DocumentoRepositorio.PermisoVer);
        grupo.MapGet("/buscar", BuscarAsync).RequierePermiso(DocumentoRepositorio.PermisoVer);
        grupo.MapGet("/{idDocumento:long}/versiones", VersionesAsync);
        grupo.MapGet("/limites", async (ActorContext actor, DocumentoRepositorio repo) => Results.Ok(await repo.LimitesAsync(actor.IdCompania)));
        // Agregar / reemplazar / obsoletar = gestion CLI_DOCUMENTO (el permiso de cada operacion se valida dentro). Los archivos nuevos
        // se suben antes como temporales y viajan en el encabezado X-Adjuntos.
        grupo.MapPost("/solicitud", SolicitarAsync);

        // Administracion (r44c): carpetas, tipos y documentos exigidos por rol. Configuracion directa, auditada.
        var adm = app.MapGroup("/api/documentos/admin").RequireAuthorization().RequierePermiso(DocumentoAdminRepositorio.Permiso);
        adm.MapGet("/carpetas", async (ActorContext actor, DocumentoAdminRepositorio repo) => Results.Ok(await repo.ListarCarpetasAsync(actor.IdCompania)));
        adm.MapPost("/carpetas", async (CarpetaGuardarRequest req, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.CrearCarpetaAsync(actor, req); return r.Exito ? Results.Created($"/api/documentos/admin/carpetas/{r.Id}", new { idCarpeta = r.Id }) : Error(r); });
        adm.MapPut("/carpetas/{id:long}", async (long id, CarpetaGuardarRequest req, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.EditarCarpetaAsync(actor, id, req); return r.Exito ? Results.NoContent() : Error(r); });
        adm.MapPut("/carpetas/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.EstadoCarpetaAsync(actor, id, req.Estado); return r.Exito ? Results.NoContent() : Error(r); });
        adm.MapGet("/tipos", async (ActorContext actor, DocumentoAdminRepositorio repo) => Results.Ok(await repo.ListarTiposAsync(actor.IdCompania)));
        adm.MapPost("/tipos", async (TipoGuardarRequest req, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.CrearTipoAsync(actor, req); return r.Exito ? Results.Created($"/api/documentos/admin/tipos/{r.Id}", new { idTipoDocumento = r.Id }) : Error(r); });
        adm.MapPut("/tipos/{id:long}", async (long id, TipoGuardarRequest req, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.EditarTipoAsync(actor, id, req); return r.Exito ? Results.NoContent() : Error(r); });
        adm.MapPut("/tipos/{id:long}/estado", async (long id, EstadoRequest req, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.EstadoTipoAsync(actor, id, req.Estado); return r.Exito ? Results.NoContent() : Error(r); });
        adm.MapGet("/roles", async (string? tipoPersona, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.RolesAsync(actor.IdCompania, tipoPersona ?? "Fisica"); return r is null ? Results.BadRequest(new { error = "datos_invalidos", mensaje = "Tipo de interlocutor inválido." }) : Results.Ok(r); });
        adm.MapPut("/roles", async (RolDocumentosGuardarRequest req, ActorContext actor, DocumentoAdminRepositorio repo) =>
        { var r = await repo.GuardarRolesAsync(actor, req); return r.Exito ? Results.NoContent() : Error(r); });

        // Situacion de los documentos que exigen los roles activos del cliente (ficha 360).
        app.MapGet("/api/interlocutores/{idInterlocutor:long}/documentos/requeridos", async (long idInterlocutor, ActorContext actor, DocumentoRepositorio repo) =>
            Results.Ok(await repo.RequeridosAsync(actor.IdCompania, idInterlocutor))).RequireAuthorization().RequierePermiso(DocumentoRepositorio.PermisoVer);

        app.MapGet("/api/interlocutores/{idInterlocutor:long}/documentos", ArbolAsync).RequireAuthorization().RequierePermiso(DocumentoRepositorio.PermisoVer);
        app.MapGet("/api/gestiones/{idGestion:long}/adjuntos", PorGestionAsync).RequireAuthorization();
    }

    private static IResult Error(ResultadoOperacion r) => Results.Json(new { error = r.Error, mensaje = r.Mensaje }, statusCode: r.Estado);

    private static async Task<IResult> TiposAsync(string? tipoPersona, ActorContext actor, DocumentoRepositorio repo)
        => Results.Ok(await repo.ListarTiposAsync(actor.IdCompania, tipoPersona));

    private static async Task<IResult> MisTemporalesAsync(ActorContext actor, DocumentoRepositorio repo)
        => Results.Ok(await repo.MisTemporalesAsync(actor));

    private static DateOnly? Fecha(string? texto)
        => DateOnly.TryParseExact(texto?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    /// <summary>Lee el formulario multipart: archivo, idTipoDocumento (si se pide), descripcion, claves (coma), fechaEmision y fechaVencimiento (yyyy-MM-dd).</summary>
    private static async Task<(IResult? Error, DocumentoSubida? Subida, IFormCollection? Form)> LeerSubidaAsync(HttpContext http, bool pideTipo)
    {
        if (!http.Request.HasFormContentType)
            return (Results.Json(new { error = "formato_invalido", mensaje = "Envíe el archivo como multipart/form-data." }, statusCode: 400), null, null);
        var form = await http.Request.ReadFormAsync();
        var archivo = form.Files.Count > 0 ? form.Files[0] : null;
        if (archivo is null || archivo.Length == 0)
            return (Results.Json(new { error = "archivo_requerido", mensaje = "Seleccione un archivo." }, statusCode: 400), null, null);
        long idTipo = 0;
        if (pideTipo && !long.TryParse(form["idTipoDocumento"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out idTipo))
            return (Results.Json(new { error = "tipo_documento_requerido", mensaje = "Indique el tipo de documento." }, statusCode: 400), null, null);

        var emisionTxt = form["fechaEmision"].ToString();
        var vencimientoTxt = form["fechaVencimiento"].ToString();
        if ((!string.IsNullOrWhiteSpace(emisionTxt) && Fecha(emisionTxt) is null) || (!string.IsNullOrWhiteSpace(vencimientoTxt) && Fecha(vencimientoTxt) is null))
            return (Results.Json(new { error = "fechas_invalidas", mensaje = "Las fechas deben tener formato AAAA-MM-DD." }, statusCode: 400), null, null);

        byte[] contenido;
        using (var ms = new MemoryStream((int)Math.Min(archivo.Length, int.MaxValue)))
        {
            await archivo.CopyToAsync(ms);
            contenido = ms.ToArray();
        }
        var claves = form["claves"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (null, new DocumentoSubida(archivo.FileName, archivo.ContentType, contenido, idTipo, form["descripcion"].ToString(), claves, Fecha(emisionTxt), Fecha(vencimientoTxt)), form);
    }

    private static async Task<IResult> SubirTemporalAsync(HttpContext http, ActorContext actor, DocumentoRepositorio repo)
    {
        var (error, subida, _) = await LeerSubidaAsync(http, pideTipo: true);
        if (error is not null) return error;
        var r = await repo.SubirTemporalAsync(actor, subida!);
        return r.Exito ? Results.Ok(r.Datos) : Error(r);
    }

    private static async Task<IResult> ClientesAsync(string? texto, ActorContext actor, DocumentoRepositorio repo)
        => Results.Ok(await repo.BuscarClientesAsync(actor.IdCompania, texto));

    private static async Task<IResult> BuscarAsync(string? texto, long? idInterlocutor, long? idCarpeta, long? idTipoDocumento, string? estado, ActorContext actor, DocumentoRepositorio repo)
        => Results.Ok(await repo.BuscarAsync(actor, new DocumentoBusquedaFiltro(texto, idInterlocutor, idCarpeta, idTipoDocumento, estado)));

    private static async Task<IResult> VersionesAsync(long idDocumento, ActorContext actor, DocumentoRepositorio repo)
    {
        var lista = await repo.VersionesAsync(actor, idDocumento);
        return lista is null ? Results.NotFound(new { error = "documento_no_encontrado", mensaje = "No existe el documento." }) : Results.Ok(lista);
    }

    private static async Task<IResult> SolicitarAsync(DocumentoSolicitudRequest req, ActorContext actor, GestionRepositorio gestiones)
    {
        var r = await gestiones.SolicitarDocumentosAsync(actor, req);
        return r.Exito ? Results.Ok(r.Datos) : Error(r);
    }

    private static async Task<IResult> QuitarTemporalAsync(long idDocumento, ActorContext actor, DocumentoRepositorio repo)
    {
        var r = await repo.QuitarTemporalAsync(actor, idDocumento);
        return r.Exito ? Results.NoContent() : Error(r);
    }

    private static async Task<IResult> ArbolAsync(long idInterlocutor, ActorContext actor, DocumentoRepositorio repo)
    {
        var arbol = await repo.ArbolAsync(actor, idInterlocutor);
        return arbol is null ? Results.NotFound(new { error = "no_encontrado", mensaje = "No existe el cliente." }) : Results.Ok(arbol);
    }

    private static async Task<IResult> PorGestionAsync(long idGestion, ActorContext actor, DocumentoRepositorio repo)
    {
        var lista = await repo.PorGestionAsync(actor, idGestion);
        return lista is null ? Results.NotFound(new { error = "gestion_no_encontrada", mensaje = "No existe la gestión." }) : Results.Ok(lista);
    }

    private static async Task<IResult> DescargarAsync(long idDocumento, long? idVersion, ActorContext actor, DocumentoRepositorio repo)
    {
        var (error, archivo) = await repo.DescargarAsync(actor, idDocumento, idVersion);
        if (error is not null) return Error(error);
        return Results.File(archivo!.Contenido, archivo.TipoMime, archivo.Nombre);
    }
}
