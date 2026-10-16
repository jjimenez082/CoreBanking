using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestor documental (r44a). Dos clases de documento:
///  - Evidencia de gestion (origen 'Gestion'): se sube como temporal, se vincula a la gestion al crearla, el aprobador la ve y,
///    al aprobarse, pasa a ser documento vigente del cliente dentro de la carpeta Gestiones (inmutable). Si se rechaza, se descarta.
///  - Expediente vivo (origen 'Expediente'): lo maneja el modulo Gestor documental (r44b).
/// Los archivos van cifrados en el almacen; aqui solo viven los metadatos. Nada se borra fisicamente salvo un temporal que nunca llego a una gestion.
/// </summary>
public partial class DocumentoRepositorio
{
    public const string Entidad = "DOCUMENTO";
    public const string PermisoVer = "DOC_VER";
    public const string PermisoVerRestringido = "DOC_VER_RESTRINGIDO";
    public const string PermisoAdjuntar = "DOC_ADJUNTAR";
    public const string CarpetaGestiones = "GESTIONES";

    private const string ParamDirectorio = "DOC_DIRECTORIO";
    private const string ParamTamanoMb = "DOC_TAMANO_MAX_MB";
    private const string ParamExtensiones = "DOC_EXTENSIONES";
    private const string ParamMaxArchivos = "DOC_MAX_ARCHIVOS_GESTION";
    private const int MaxClaves = 10;

    private readonly TenantContext _tenant;
    private readonly IAlmacenDocumentos _almacen;

    public DocumentoRepositorio(TenantContext tenant, IAlmacenDocumentos almacen)
    {
        _tenant = tenant;
        _almacen = almacen;
    }

    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private static async Task<string> ParametroAsync(SqlConnection c, SqlTransaction? tx, long idCompania, string codigo, string defecto)
        => await c.ExecuteScalarAsync<string?>(
               "SELECT valor FROM gen.PARAMETRO WHERE id_compania = @idCompania AND codigo = @codigo", new { idCompania, codigo }, tx) ?? defecto;

    // ------------------------------------------------------------------ Catalogo

    /// <summary>Tipos de documento activos; si se indica el tipo de persona, solo los que aplican a ella.</summary>
    /// <summary>Estado de los documentos que exigen los roles activos del cliente (Cumple, PorVencer, Vencido, Falta).</summary>
    public async Task<IReadOnlyList<DocumentoRequeridoItem>> RequeridosAsync(long idCompania, long idInterlocutor)
    {
        using var c = NuevaConexion();
        return await DocumentoRequisitoServicio.SituacionAsync(c, null, idCompania, idInterlocutor);
    }

    public async Task<IReadOnlyList<TipoDocumentoResponse>> ListarTiposAsync(long idCompania, string? tipoPersona)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT t.id_tipo_documento, t.codigo, t.nombre, t.id_carpeta, ca.nombre AS carpeta, ca.codigo AS carpeta_codigo, ca.es_sistema, t.cardinalidad, t.maximo,
                   t.vencimiento, t.dias_aviso, t.confidencialidad, t.requiere_aprobacion
              FROM doc.TIPO_DOCUMENTO t
              JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta
             WHERE t.id_compania = @idCompania AND t.estado = 'Activo' AND ca.estado = 'Activo'
               AND (@tipoPersona IS NULL
                    OR (@tipoPersona = 'Fisica' AND t.aplica_fisica = 1)
                    OR (@tipoPersona = 'Juridica' AND t.aplica_juridica = 1)
                    OR (@tipoPersona = 'Grupo' AND t.aplica_grupo = 1))
             ORDER BY ca.orden, t.orden, t.nombre
            """, new { idCompania, tipoPersona });
        return filas.Select(f => new TipoDocumentoResponse(
            (long)f.id_tipo_documento, (string)f.codigo, (string)f.nombre, (long)f.id_carpeta, (string)f.carpeta, (string)f.cardinalidad,
            (int?)f.maximo, (string)f.vencimiento, (int)f.dias_aviso, (string)f.confidencialidad, (bool)f.requiere_aprobacion, (string)f.carpeta_codigo, (bool)f.es_sistema)).ToList();
    }

    // ------------------------------------------------------------------ Subida de temporales

    private static bool FirmaValida(string extension, byte[] b)
    {
        if (b.Length < 4) return false;
        return extension switch
        {
            "pdf" => b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46,                 // %PDF
            "jpg" or "jpeg" => b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
            "png" => b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
            _ => true,   // otras extensiones que el administrador habilite: sin verificacion de firma
        };
    }

    private static string NombreSeguro(string nombre)
    {
        var n = Path.GetFileName((nombre ?? "").Replace('\\', '/'));
        foreach (var ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch, '_');
        n = n.Trim();
        if (n.Length == 0) n = "documento";
        return n.Length > 200 ? n[^200..] : n;
    }

    /// <summary>Lo ya validado de un archivo que se va a guardar (compartido por temporales, expediente y reemplazos).</summary>
    private sealed record Preparado(
        string Directorio, string Nombre, string Mime, byte[] Contenido, string Sha, DateOnly? Emision, DateOnly? Vence,
        string? Descripcion, List<string> Claves, string TipoNombre, string CarpetaCodigo, bool CarpetaSistema, string Cardinalidad, int? Maximo,
        bool RequiereAprobacion, bool AplicaFisica, bool AplicaJuridica, bool AplicaGrupo);

    /// <summary>Valida archivo (extension, tamano, firma), tipo de documento, fechas y textos segun las reglas del tipo y los parametros de la compania.</summary>
    private async Task<(ResultadoOperacion? Error, Preparado? Datos)> PrepararAsync(SqlConnection c, ActorContext actor, DocumentoSubida s)
    {
        if (s.Contenido.Length == 0) return (ResultadoOperacion.Fallo(400, "archivo_vacio", "El archivo está vacío."), null);

        var directorio = await ParametroAsync(c, null, actor.IdCompania, ParamDirectorio, "almacen_documentos");
        var maxMb = int.TryParse(await ParametroAsync(c, null, actor.IdCompania, ParamTamanoMb, "10"), out var mb) && mb > 0 ? mb : 10;
        var extensiones = (await ParametroAsync(c, null, actor.IdCompania, ParamExtensiones, "pdf,jpg,jpeg,png"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.TrimStart('.').ToLowerInvariant()).ToHashSet();

        var nombre = NombreSeguro(s.NombreArchivo);
        var ext = Path.GetExtension(nombre).TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0 || !extensiones.Contains(ext))
            return (ResultadoOperacion.Fallo(400, "extension_no_permitida", $"Solo se permiten archivos {string.Join(", ", extensiones.OrderBy(x => x))}."), null);
        if (s.Contenido.LongLength > maxMb * 1024L * 1024L)
            return (ResultadoOperacion.Fallo(400, "archivo_muy_grande", $"El archivo supera el máximo de {maxMb} MB."), null);
        if (!FirmaValida(ext, s.Contenido))
            return (ResultadoOperacion.Fallo(400, "archivo_no_coincide", "El contenido del archivo no corresponde a su extensión."), null);

        var tipo = await c.QuerySingleOrDefaultAsync(
            """
            SELECT t.nombre, t.vencimiento, t.cardinalidad, t.maximo, t.requiere_aprobacion, t.aplica_fisica, t.aplica_juridica, t.aplica_grupo,
                   ca.codigo AS carpeta_codigo, ca.es_sistema
              FROM doc.TIPO_DOCUMENTO t JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta
             WHERE t.id_tipo_documento = @id AND t.id_compania = @idCompania AND t.estado = 'Activo' AND ca.estado = 'Activo'
            """, new { id = s.IdTipoDocumento, actor.IdCompania });
        if (tipo is null) return (ResultadoOperacion.Fallo(400, "tipo_documento_invalido", "El tipo de documento no existe o está inactivo."), null);

        var vencimientoRegla = (string)tipo.vencimiento;
        DateOnly? emision = s.FechaEmision, vence = s.FechaVencimiento;
        if (vencimientoRegla == "NoAplica") { emision = null; vence = null; }
        else
        {
            if (vencimientoRegla == "Obligatorio" && vence is null)
                return (ResultadoOperacion.Fallo(400, "vencimiento_requerido", $"El tipo «{(string)tipo.nombre}» exige la fecha de vencimiento."), null);
            if (emision is not null && vence is not null && vence < emision)
                return (ResultadoOperacion.Fallo(400, "fechas_invalidas", "La fecha de vencimiento no puede ser anterior a la de emisión."), null);
            if (vence is not null && vence < DateOnly.FromDateTime(DateTime.UtcNow))
                return (ResultadoOperacion.Fallo(400, "documento_vencido", "El documento ya está vencido."), null);
        }

        var descripcion = string.IsNullOrWhiteSpace(s.Descripcion) ? null : s.Descripcion.Trim();
        if (descripcion is { Length: > 300 }) return (ResultadoOperacion.Fallo(400, "descripcion_larga", "La descripción no puede superar 300 caracteres."), null);
        var claves = s.Claves.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (claves.Count > MaxClaves) return (ResultadoOperacion.Fallo(400, "claves_exceso", $"Máximo {MaxClaves} palabras clave."), null);
        if (claves.Any(x => x.Length > 60)) return (ResultadoOperacion.Fallo(400, "clave_larga", "Cada palabra clave puede tener hasta 60 caracteres."), null);

        var sha = Convert.ToHexString(SHA256.HashData(s.Contenido)).ToLowerInvariant();
        var mime = string.IsNullOrWhiteSpace(s.TipoMime) ? "application/octet-stream" : s.TipoMime;
        return (null, new Preparado(directorio, nombre, mime, s.Contenido, sha, emision, vence, descripcion, claves, (string)tipo.nombre,
            (string)tipo.carpeta_codigo, (bool)tipo.es_sistema, (string)tipo.cardinalidad, (int?)tipo.maximo, (bool)tipo.requiere_aprobacion,
            (bool)tipo.aplica_fisica, (bool)tipo.aplica_juridica, (bool)tipo.aplica_grupo));
    }

    /// <summary>Inserta la version del documento (ya con el archivo guardado en el almacen).</summary>
    private static Task InsertarVersionAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idDocumento, int numero, Preparado p, string ruta, string idLlave, string? motivo)
        => c.ExecuteAsync(
            """
            INSERT INTO doc.DOCUMENTO_VERSION (id_documento, numero, es_vigente, nombre_archivo, tipo_mime, tamano_bytes, sha256, ruta_almacen, id_llave,
                                               fecha_emision, fecha_vencimiento, motivo_reemplazo, subido_por)
            VALUES (@idDocumento, @numero, 1, @nombre, @mime, @tamano, @sha, @ruta, @idLlave, @emision, @vence, @motivo, @por)
            """,
            new
            {
                idDocumento, numero, nombre = p.Nombre, mime = p.Mime, tamano = (long)p.Contenido.Length, sha = p.Sha, ruta, idLlave,
                emision = p.Emision?.ToDateTime(TimeOnly.MinValue), vence = p.Vence?.ToDateTime(TimeOnly.MinValue), motivo, por = actor.NombreDescriptivo
            }, tx);

    private static async Task InsertarClavesAsync(SqlConnection c, SqlTransaction tx, long idDocumento, IEnumerable<string> claves)
    {
        foreach (var clave in claves)
            await c.ExecuteAsync("INSERT INTO doc.DOCUMENTO_CLAVE (id_documento, clave) VALUES (@idDocumento, @clave)", new { idDocumento, clave }, tx);
    }

    /// <summary>Sube un archivo como documento temporal del usuario. Se vincula a una gestion cuando esta se crea (encabezado X-Adjuntos).</summary>
    public async Task<ResultadoOperacion> SubirTemporalAsync(ActorContext actor, DocumentoSubida s)
    {
        if (actor.IdUsuario is null) return ResultadoOperacion.Fallo(400, "actor_invalido", "No se pudo identificar al usuario.");

        using var c = NuevaConexion();
        c.Open();
        var (error, p) = await PrepararAsync(c, actor, s);
        if (error is not null) return error;

        var maxArchivos = int.TryParse(await ParametroAsync(c, null, actor.IdCompania, ParamMaxArchivos, "10"), out var ma) && ma > 0 ? ma : 10;
        var pendientes = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM doc.DOCUMENTO WHERE id_compania = @idCompania AND id_usuario_creador = @idUsuario AND estado = 'Temporal'",
            new { actor.IdCompania, actor.IdUsuario });
        if (pendientes >= maxArchivos)
            return ResultadoOperacion.Fallo(409, "documentos_maximo", $"Ya tiene {pendientes} archivos sin enviar; el máximo por gestión es {maxArchivos}. Quite alguno o envíe la gestión.");

        string ruta, idLlave;
        try { (ruta, idLlave) = await _almacen.GuardarAsync(p!.Directorio, actor.IdCompania, p.Contenido); }
        catch (AlmacenDocumentosException ex) { return ResultadoOperacion.Fallo(500, ex.Codigo, ex.Message); }

        using var tx = c.BeginTransaction();
        try
        {
            var idDocumento = await c.QuerySingleAsync<long>(
                """
                INSERT INTO doc.DOCUMENTO (id_compania, id_interlocutor, id_tipo_documento, descripcion, origen, id_gestion, estado, creado_por, id_usuario_creador)
                OUTPUT INSERTED.id_documento
                VALUES (@idCompania, NULL, @idTipo, @descripcion, 'Gestion', NULL, 'Temporal', @por, @idUsuario)
                """, new { actor.IdCompania, idTipo = s.IdTipoDocumento, descripcion = p.Descripcion, por = actor.NombreDescriptivo, actor.IdUsuario }, tx);
            await InsertarVersionAsync(c, tx, actor, idDocumento, 1, p, ruta, idLlave, null);
            await InsertarClavesAsync(c, tx, idDocumento, p.Claves);
            tx.Commit();
            var doc = (await LeerDocumentosAsync(c, null, "d.id_documento = @id", new { id = idDocumento })).First();
            return ResultadoOperacion.Ok(idDocumento) with { Datos = doc };
        }
        catch
        {
            tx.Rollback();
            try { await _almacen.EliminarAsync(p!.Directorio, ruta); } catch { /* el archivo huerfano no debe tapar el error original */ }
            throw;
        }
    }

    /// <summary>Quita un temporal propio que aun no se envio con ninguna gestion (unico caso de borrado fisico).</summary>
    public async Task<ResultadoOperacion> QuitarTemporalAsync(ActorContext actor, long idDocumento)
    {
        using var c = NuevaConexion();
        c.Open();
        var d = await c.QuerySingleOrDefaultAsync(
            """
            SELECT d.estado, v.ruta_almacen
              FROM doc.DOCUMENTO d JOIN doc.DOCUMENTO_VERSION v ON v.id_documento = d.id_documento
             WHERE d.id_documento = @idDocumento AND d.id_compania = @idCompania AND d.id_usuario_creador = @idUsuario
            """, new { idDocumento, actor.IdCompania, actor.IdUsuario });
        if (d is null) return ResultadoOperacion.Fallo(404, "documento_no_encontrado", "No existe el documento.");
        if ((string)d.estado != "Temporal")
            return ResultadoOperacion.Fallo(409, "documento_en_gestion", "El documento ya forma parte de una gestión y no se puede quitar.");

        var directorio = await ParametroAsync(c, null, actor.IdCompania, ParamDirectorio, "almacen_documentos");
        using var tx = c.BeginTransaction();
        try
        {
            await c.ExecuteAsync("DELETE FROM doc.DOCUMENTO_CLAVE WHERE id_documento = @idDocumento", new { idDocumento }, tx);
            await c.ExecuteAsync("DELETE FROM doc.DOCUMENTO_VERSION WHERE id_documento = @idDocumento", new { idDocumento }, tx);
            await c.ExecuteAsync("DELETE FROM doc.DOCUMENTO WHERE id_documento = @idDocumento", new { idDocumento }, tx);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
        try { await _almacen.EliminarAsync(directorio, (string)d.ruta_almacen); } catch { /* ya no hay metadatos; si el archivo queda, no afecta */ }
        return ResultadoOperacion.Ok(idDocumento);
    }

    // ------------------------------------------------------------------ Lectura

    private static (string Estado, int? Dias) Vigencia(DateTime? vence, int diasAviso)
    {
        if (vence is null) return ("SinVencimiento", null);
        var dias = (int)(vence.Value.Date - DateTime.UtcNow.Date).TotalDays;
        return dias < 0 ? ("Vencido", dias) : dias <= diasAviso ? ("PorVencer", dias) : ("Vigente", dias);
    }

    private sealed record FilaDoc(DocumentoResponse Doc, bool Restringido, long? IdCarpeta, string? CodigoCarpeta, string? TipoGestion, long? IdUsuarioCreador, long? IdInterlocutor, string NombreCarpeta, string? EntidadGestion);

    /// <summary>Documentos con su version vigente. El filtro es un fragmento SQL fijo definido aqui (nunca texto del usuario).</summary>
    private static async Task<IReadOnlyList<DocumentoResponse>> LeerDocumentosAsync(SqlConnection c, SqlTransaction? tx, string filtro, object parametros)
        => (await LeerFilasAsync(c, tx, filtro, parametros)).Select(x => x.Doc).ToList();

    private static async Task<IReadOnlyList<FilaDoc>> LeerFilasAsync(SqlConnection c, SqlTransaction? tx, string filtro, object parametros, int limite = 0)
    {
        var filas = (await c.QueryAsync(
            $"""
            SELECT {(limite > 0 ? $"TOP ({limite}) " : "")}d.id_documento, d.id_interlocutor, ca.nombre AS nombre_carpeta, d.id_tipo_documento, t.nombre AS tipo_nombre, t.confidencialidad, t.dias_aviso, t.id_carpeta, ca.codigo AS codigo_carpeta,
                   d.descripcion, d.estado, d.origen, d.id_gestion, g.numero AS numero_gestion, tg.nombre AS tipo_gestion, g.entidad_negocio AS entidad_gestion, d.motivo_obsoleto,
                   d.id_usuario_creador, io.numero AS numero_origen,
                   v.numero AS version, v.nombre_archivo, v.tamano_bytes, v.subido_en, v.subido_por, v.fecha_emision, v.fecha_vencimiento
              FROM doc.DOCUMENTO d
              JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
              JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta
              JOIN doc.DOCUMENTO_VERSION v ON v.id_documento = d.id_documento AND v.es_vigente = 1
              LEFT JOIN apr.GESTION g ON g.id_gestion = d.id_gestion
              LEFT JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
              LEFT JOIN cli.INTERLOCUTOR io ON io.id_interlocutor = d.id_interlocutor_origen
             WHERE {filtro}
             ORDER BY d.creado_en, d.id_documento
            """, parametros, tx)).ToList();
        if (filas.Count == 0) return [];

        var ids = filas.Select(f => (long)f.id_documento).ToArray();
        var claves = (await c.QueryAsync("SELECT id_documento, clave FROM doc.DOCUMENTO_CLAVE WHERE id_documento IN @ids ORDER BY clave", new { ids }, tx))
            .GroupBy(x => (long)x.id_documento).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => (string)x.clave).ToList());

        return filas.Select(f =>
        {
            DateTime? venc = (DateTime?)f.fecha_vencimiento;
            DateTime? emi = (DateTime?)f.fecha_emision;
            var (estadoVig, dias) = Vigencia(venc, (int)f.dias_aviso);
            var restringido = (string)f.confidencialidad == "Restringida";
            var doc = new DocumentoResponse(
                (long)f.id_documento, (long)f.id_tipo_documento, (string)f.tipo_nombre, (string?)f.descripcion, (string)f.estado, (string)f.origen,
                (long?)f.id_gestion, (string?)f.numero_gestion, (string)f.nombre_archivo, (long)f.tamano_bytes, (DateTime)f.subido_en, (string)f.subido_por, (int)f.version,
                emi is null ? null : DateOnly.FromDateTime(emi.Value), venc is null ? null : DateOnly.FromDateTime(venc.Value), estadoVig, dias,
                claves.GetValueOrDefault((long)f.id_documento) ?? Array.Empty<string>(), restringido, (string?)f.motivo_obsoleto,
                f.numero_origen is null ? null : (string)f.numero_origen);
            return new FilaDoc(doc, restringido, (long?)f.id_carpeta, (string?)f.codigo_carpeta, (string?)f.tipo_gestion, (long?)f.id_usuario_creador, (long?)f.id_interlocutor, (string)f.nombre_carpeta, (string?)f.entidad_gestion);
        }).ToList();
    }

    /// <summary>Archivo que espera aprobacion dentro de una gestion de documentos: se ve en su carpeta, marcado como pendiente.</summary>
    private static bool EsPendienteDeDocumentos(FilaDoc f) => f.Doc.Estado == "EnGestion" && f.EntidadGestion == DocumentoGestionServicio.Entidad;

    /// <summary>Limites de carga vigentes, para validar en pantalla antes de subir.</summary>
    public async Task<DocumentoLimitesResponse> LimitesAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var mb = int.TryParse(await ParametroAsync(c, null, idCompania, ParamTamanoMb, "10"), out var m) && m > 0 ? m : 10;
        var ext = (await ParametroAsync(c, null, idCompania, ParamExtensiones, "pdf,jpg,jpeg,png"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant().TrimStart('.')).ToArray();
        var max = int.TryParse(await ParametroAsync(c, null, idCompania, ParamMaxArchivos, "10"), out var ma) && ma > 0 ? ma : 10;
        return new DocumentoLimitesResponse(mb, ext, max);
    }

    /// <summary>Regla unica de visibilidad de un documento para quien consulta.</summary>
    private static bool PuedeVer(ActorContext actor, string estado, bool restringido, long? idUsuarioCreador)
    {
        if (idUsuarioCreador is not null && idUsuarioCreador == actor.IdUsuario) return true;
        var verRestringido = actor.TienePermiso(PermisoVerRestringido);
        var normal = actor.TienePermiso(PermisoVer) && (!restringido || verRestringido);
        return estado switch
        {
            "Temporal" => false,
            "EnGestion" or "Descartado" => actor.TienePermiso("GES_APROBAR") || normal,
            _ => normal,
        };
    }

    /// <summary>Arbol de documentos de un cliente: carpetas aplicables con sus documentos (Gestiones agrupa por gestion). Oculta los restringidos si no hay permiso.</summary>
    public async Task<ArbolDocumentosResponse?> ArbolAsync(ActorContext actor, long idInterlocutor)
    {
        using var c = NuevaConexion();
        var tipoPersona = await c.ExecuteScalarAsync<string?>(
            "SELECT tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania",
            new { idInterlocutor, actor.IdCompania });
        if (tipoPersona is null) return null;

        var carpetas = (await c.QueryAsync(
            """
            SELECT id_carpeta, codigo, nombre, es_sistema FROM doc.CARPETA
             WHERE id_compania = @idCompania AND estado = 'Activo'
               AND ((@tp = 'Fisica' AND aplica_fisica = 1) OR (@tp = 'Juridica' AND aplica_juridica = 1) OR (@tp = 'Grupo' AND aplica_grupo = 1))
             ORDER BY orden, nombre
            """, new { actor.IdCompania, tp = tipoPersona })).ToList();

        var docs = (await LeerFilasAsync(c, null,
            "d.id_interlocutor = @idInterlocutor AND d.id_compania = @idCompania AND (d.estado IN ('Vigente','Obsoleto') OR (d.estado = 'EnGestion' AND g.entidad_negocio = 'INTERLOCUTOR_DOCUMENTO'))",
            new { idInterlocutor, actor.IdCompania }))
            .Where(f => PuedeVer(actor, f.Doc.Estado, f.Restringido, f.IdUsuarioCreador)).ToList();

        var nodos = new List<CarpetaNodo>();
        foreach (var ca in carpetas)
        {
            var idCarpeta = (long)ca.id_carpeta;
            var codigo = (string)ca.codigo;
            var propios = docs.Where(d => d.CodigoCarpeta == codigo).ToList();
            if (codigo == CarpetaGestiones)
            {
                var evidencia = docs.Where(d => d.Doc.Origen == "Gestion" && !EsPendienteDeDocumentos(d)).ToList();
                var subs = evidencia.GroupBy(d => d.Doc.IdGestion ?? 0).OrderByDescending(g => g.Key)
                    .Select(g => new SubcarpetaGestionNodo(
                        g.Key, g.First().Doc.NumeroGestion ?? "#" + g.Key,
                        $"{g.First().Doc.NumeroGestion ?? "#" + g.Key} · {g.First().TipoGestion ?? "Gestión"}",
                        g.Select(x => x.Doc).ToList())).ToList();
                nodos.Add(new CarpetaNodo(idCarpeta, codigo, (string)ca.nombre, (bool)ca.es_sistema, [], subs));
            }
            else
            {
                // La evidencia de gestion vive siempre en Gestiones, sea cual sea su tipo.
                var lista = propios.Where(d => d.Doc.Origen == "Expediente" || EsPendienteDeDocumentos(d)).Select(x => x.Doc).ToList();
                nodos.Add(new CarpetaNodo(idCarpeta, codigo, (string)ca.nombre, (bool)ca.es_sistema, lista, []));
            }
        }
        return new ArbolDocumentosResponse(nodos);
    }

    /// <summary>Adjuntos de una gestion (el aprobador los ve antes de decidir). Temporales: solo los ve quien los subio.</summary>
    public async Task<IReadOnlyList<DocumentoResponse>?> PorGestionAsync(ActorContext actor, long idGestion)
    {
        using var c = NuevaConexion();
        var g = await c.QuerySingleOrDefaultAsync(
            "SELECT id_usuario_solicitante FROM apr.GESTION WHERE id_gestion = @idGestion AND id_compania = @idCompania", new { idGestion, actor.IdCompania });
        if (g is null) return null;
        var esSolicitante = (long)g.id_usuario_solicitante == actor.IdUsuario;
        if (!esSolicitante && !actor.TienePermiso("GES_APROBAR") && !actor.TienePermiso(PermisoVer)) return [];

        var filas = await LeerFilasAsync(c, null,
            "d.id_gestion = @idGestion AND d.id_compania = @idCompania AND d.estado IN ('EnGestion','Vigente','Obsoleto','Descartado')",
            new { idGestion, actor.IdCompania });
        // En la gestion el aprobador y el solicitante ven todo; el resto, segun su permiso sobre confidencialidad.
        return filas.Where(f => esSolicitante || actor.TienePermiso("GES_APROBAR") || PuedeVer(actor, f.Doc.Estado, f.Restringido, f.IdUsuarioCreador))
                    .Select(f => f.Doc).ToList();
    }

    /// <summary>Ids de los documentos que el usuario subio y aun no envio (para reanudar el formulario).</summary>
    public async Task<IReadOnlyList<DocumentoResponse>> MisTemporalesAsync(ActorContext actor)
    {
        using var c = NuevaConexion();
        return await LeerDocumentosAsync(c, null,
            "d.id_compania = @idCompania AND d.id_usuario_creador = @idUsuario AND d.estado = 'Temporal'", new { actor.IdCompania, actor.IdUsuario });
    }

    // ------------------------------------------------------------------ Descarga

    /// <summary>Descifra y entrega el archivo de la version vigente (o la indicada). Cada descarga queda en la auditoria.</summary>
    public async Task<(ResultadoOperacion? Error, ArchivoDescargado? Archivo)> DescargarAsync(ActorContext actor, long idDocumento, long? idVersion = null)
    {
        using var c = NuevaConexion();
        c.Open();
        var d = await c.QuerySingleOrDefaultAsync(
            """
            SELECT d.estado, d.id_usuario_creador, t.confidencialidad
              FROM doc.DOCUMENTO d JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
             WHERE d.id_documento = @idDocumento AND d.id_compania = @idCompania
            """, new { idDocumento, actor.IdCompania });
        if (d is null) return (ResultadoOperacion.Fallo(404, "documento_no_encontrado", "No existe el documento."), null);
        if (!PuedeVer(actor, (string)d.estado, (string)d.confidencialidad == "Restringida", (long?)d.id_usuario_creador))
            return (ResultadoOperacion.Fallo(403, "permiso_denegado", "No tiene permiso para ver este documento."), null);

        var v = await c.QuerySingleOrDefaultAsync(
            """
            SELECT id_version, numero, nombre_archivo, tipo_mime, ruta_almacen, id_llave
              FROM doc.DOCUMENTO_VERSION
             WHERE id_documento = @idDocumento AND ((@idVersion IS NULL AND es_vigente = 1) OR id_version = @idVersion)
            """, new { idDocumento, idVersion });
        if (v is null) return (ResultadoOperacion.Fallo(404, "version_no_encontrada", "No existe esa versión del documento."), null);

        var directorio = await ParametroAsync(c, null, actor.IdCompania, ParamDirectorio, "almacen_documentos");
        byte[] contenido;
        try { contenido = await _almacen.LeerAsync(directorio, (string)v.ruta_almacen, (string)v.id_llave); }
        catch (AlmacenDocumentosException ex) { return (ResultadoOperacion.Fallo(500, ex.Codigo, ex.Message), null); }

        await Auditoria.CambioAsync(c, null, actor, Entidad, idDocumento, "descarga", null, $"{(string)v.nombre_archivo} (versión {(int)v.numero})");
        return (null, new ArchivoDescargado((string)v.nombre_archivo, (string)v.tipo_mime, contenido));
    }

    // ------------------------------------------------------------------ Ciclo de vida con la gestion (estaticos: corren dentro de la transaccion de la gestion)

    /// <summary>Al crear una gestion: vincula los temporales que llegaron en X-Adjuntos (solo los del propio usuario).</summary>
    public static async Task VincularAGestionAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion)
    {
        if (actor.AdjuntosTemporales.Count == 0 || actor.IdUsuario is null) return;
        var ids = actor.AdjuntosTemporales.ToArray();
        await c.ExecuteAsync(
            """
            UPDATE doc.DOCUMENTO
               SET estado = 'EnGestion', id_gestion = @idGestion, modificado_por = @por, modificado_en = SYSUTCDATETIME()
             WHERE id_documento IN @ids AND estado = 'Temporal' AND id_compania = @idCompania AND id_usuario_creador = @idUsuario
            """, new { idGestion, ids, por = actor.NombreDescriptivo, actor.IdCompania, actor.IdUsuario }, tx);
    }

    /// <summary>La gestion devuelta se corrige con una nueva: los adjuntos pasan a la nueva gestion.</summary>
    public static Task TrasladarCorreccionAsync(SqlConnection c, SqlTransaction tx, long idDevuelta, long idNueva)
        => c.ExecuteAsync(
            "UPDATE doc.DOCUMENTO SET id_gestion = @idNueva, modificado_en = SYSUTCDATETIME() WHERE id_gestion = @idDevuelta AND estado = 'EnGestion'",
            new { idDevuelta, idNueva }, tx);

    /// <summary>Al aprobarse la gestion: la evidencia pasa a ser documento vigente del cliente (carpeta Gestiones). No cambia mas.</summary>
    public static async Task PromoverAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string entidad, string porQuien)
    {
        var ids = (await c.QueryAsync<long>("SELECT id_documento FROM doc.DOCUMENTO WHERE id_gestion = @idGestion AND estado = 'EnGestion'", new { idGestion }, tx)).ToList();
        if (ids.Count == 0) return;

        long? idCliente = null;
        if (entidad != TipoCambioRepositorio.Entidad)
        {
            var llave = await c.ExecuteScalarAsync<string?>("SELECT llave_registro FROM apr.GESTION WHERE id_gestion = @idGestion", new { idGestion }, tx);
            if (long.TryParse(llave, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                // Si el cliente quedo fusionado (p. ej. la gestion de fusion apunta al absorbido), la evidencia va al que se conserva.
                var cli = await c.QuerySingleOrDefaultAsync(
                    "SELECT estado, id_interlocutor_sobreviviente FROM cli.INTERLOCUTOR WHERE id_interlocutor = @id AND id_compania = @idCompania",
                    new { id, actor.IdCompania }, tx);
                if (cli is not null) idCliente = (string)cli.estado == "Fusionado" && cli.id_interlocutor_sobreviviente is not null ? (long)cli.id_interlocutor_sobreviviente : id;
            }
        }

        await c.ExecuteAsync(
            """
            UPDATE doc.DOCUMENTO
               SET estado = 'Vigente', id_interlocutor = @idCliente, modificado_por = @por, modificado_en = SYSUTCDATETIME()
             WHERE id_gestion = @idGestion AND estado = 'EnGestion'
            """, new { idGestion, idCliente, por = porQuien }, tx);
        foreach (var id in ids)
            await Auditoria.AltaAsync(c, tx, actor, Entidad, id, "Evidencia incorporada al aprobarse la gestión", idGestion);
    }

    /// <summary>Al rechazarse la gestion: la evidencia se descarta (el archivo se conserva en el almacen, el documento no aparece en ningun arbol).</summary>
    public static async Task DescartarDeGestionAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion)
    {
        var ids = (await c.QueryAsync<long>("SELECT id_documento FROM doc.DOCUMENTO WHERE id_gestion = @idGestion AND estado = 'EnGestion'", new { idGestion }, tx)).ToList();
        if (ids.Count == 0) return;
        await c.ExecuteAsync(
            "UPDATE doc.DOCUMENTO SET estado = 'Descartado', modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_gestion = @idGestion AND estado = 'EnGestion'",
            new { idGestion, por = actor.NombreDescriptivo }, tx);
        foreach (var id in ids)
            await Auditoria.BajaAsync(c, tx, actor, Entidad, id, "Descartado: la gestión fue rechazada", idGestion);
    }

    /// <summary>Cantidad de documentos del cliente (vigentes u obsoletos), para la vista previa de la fusion.</summary>
    public static Task<int> ContarDeClienteAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor)
        => c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM doc.DOCUMENTO WHERE id_interlocutor = @idInterlocutor AND estado IN ('Vigente','Obsoleto')", new { idInterlocutor }, tx);

    /// <summary>
    /// Fusion: el arbol del absorbido pasa al que se conserva, recordando de donde viene. Si ambos tienen vigente un documento de un tipo
    /// de cardinalidad unica, prevalece el del que se conserva y el del absorbido queda obsoleto (con su motivo).
    /// </summary>
    public static async Task<(int Trasladados, int Obsoletos)> TrasladarPorFusionAsync(SqlConnection c, SqlTransaction tx, long idSobreviviente, long idAbsorbido, string porQuien)
    {
        var p = new { s = idSobreviviente, d = idAbsorbido, por = porQuien };
        var obsoletos = await c.ExecuteAsync(
            """
            UPDATE d
               SET estado = 'Obsoleto', motivo_obsoleto = N'Fusión: el cliente conservado ya tiene un documento vigente de este tipo',
                   modificado_por = @por, modificado_en = SYSUTCDATETIME()
              FROM doc.DOCUMENTO d JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
             WHERE d.id_interlocutor = @d AND d.origen = 'Expediente' AND d.estado = 'Vigente' AND t.cardinalidad = 'Unico'
               AND EXISTS (SELECT 1 FROM doc.DOCUMENTO x WHERE x.id_interlocutor = @s AND x.id_tipo_documento = d.id_tipo_documento
                              AND x.origen = 'Expediente' AND x.estado = 'Vigente')
            """, p, tx);
        var trasladados = await c.ExecuteAsync(
            """
            UPDATE doc.DOCUMENTO
               SET id_interlocutor = @s, id_interlocutor_origen = @d, modificado_por = @por, modificado_en = SYSUTCDATETIME()
             WHERE id_interlocutor = @d AND estado IN ('Vigente','Obsoleto')
            """, p, tx);
        return (trasladados, obsoletos);
    }
}
