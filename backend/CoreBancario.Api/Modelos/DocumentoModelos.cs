namespace CoreBancario.Api.Modelos;

/// <summary>Tipo de documento con sus reglas (para el combo de adjuntos y la administracion).</summary>
public record TipoDocumentoResponse(
    long Id, string Codigo, string Nombre, long IdCarpeta, string Carpeta, string Cardinalidad, int? Maximo,
    string Vencimiento, int DiasAviso, string Confidencialidad, bool RequiereAprobacion, string CarpetaCodigo, bool CarpetaSistema);

/// <summary>Documento con los datos de su version vigente. EstadoVigencia: SinVencimiento / Vigente / PorVencer / Vencido.</summary>
public record DocumentoResponse(
    long IdDocumento, long IdTipoDocumento, string TipoNombre, string? Descripcion, string Estado, string Origen,
    long? IdGestion, string? NumeroGestion, string NombreArchivo, long Tamano, DateTime SubidoEn, string SubidoPor, int Version,
    DateOnly? FechaEmision, DateOnly? FechaVencimiento, string EstadoVigencia, int? DiasParaVencer,
    IReadOnlyList<string> Claves, bool Restringido, string? MotivoObsoleto, string? FusionadoDesde);

/// <summary>Subcarpeta de la carpeta Gestiones: una por gestion con evidencia.</summary>
public record SubcarpetaGestionNodo(long IdGestion, string Numero, string Nombre, IReadOnlyList<DocumentoResponse> Documentos);

public record CarpetaNodo(
    long IdCarpeta, string Codigo, string Nombre, bool EsSistema,
    IReadOnlyList<DocumentoResponse> Documentos, IReadOnlyList<SubcarpetaGestionNodo> Subcarpetas);

public record ArbolDocumentosResponse(IReadOnlyList<CarpetaNodo> Carpetas);

/// <summary>Datos de un archivo que se sube (ya leido del formulario).</summary>
public record DocumentoSubida(
    string NombreArchivo, string TipoMime, byte[] Contenido, long IdTipoDocumento, string? Descripcion,
    IReadOnlyList<string> Claves, DateOnly? FechaEmision, DateOnly? FechaVencimiento);

/// <summary>Archivo ya descifrado listo para entregar.</summary>
public record ArchivoDescargado(string Nombre, string TipoMime, byte[] Contenido);

/// <summary>Cliente para el selector del Gestor documental.</summary>
public record ClienteDocumentoItem(long Id, string Numero, string Nombre, string TipoPersona, string Estado);

/// <summary>Resultado de la busqueda de documentos del expediente (varios clientes).</summary>
public record DocumentoBusquedaItem(DocumentoResponse Documento, long IdInterlocutor, string NumeroCliente, string NombreCliente, string Carpeta);

/// <summary>Filtros de la busqueda. Estado: Vigente / PorVencer / Vencido / Obsoleto (vacio = vigentes y obsoletos).</summary>
public record DocumentoBusquedaFiltro(string? Texto, long? IdInterlocutor, long? IdCarpeta, long? IdTipoDocumento, string? Estado);

public record DocumentoVersionResponse(
    long IdVersion, int Numero, bool EsVigente, string NombreArchivo, string TipoMime, long Tamano,
    DateOnly? FechaEmision, DateOnly? FechaVencimiento, string? MotivoReemplazo, string SubidoPor, DateTime SubidoEn, string? NumeroGestion);

/// <summary>Una operacion de la gestion de documentos: Alta (archivo nuevo), Reemplazo (archivo nuevo + documento destino + motivo) u Obsoleto (documento destino + motivo).</summary>
public record DocumentoOperacionRequest(string Tipo, long? IdDocumento, long? IdDocumentoDestino, string? Motivo);

/// <summary>Los archivos nuevos van como temporales en el encabezado X-Adjuntos; deben coincidir con las operaciones Alta y Reemplazo.</summary>
public record DocumentoSolicitudRequest(long IdInterlocutor, List<DocumentoOperacionRequest> Operaciones, string? Comentario);

public record DocumentoSolicitudResponse(long IdGestion, string Numero, bool Aplicada);

/// <summary>Limites de carga vigentes (parametros del sistema) para que la pantalla los valide antes de subir.</summary>
public record DocumentoLimitesResponse(int TamanoMaxMb, IReadOnlyList<string> Extensiones, int MaxArchivos);

// ----- Administracion de carpetas y tipos (r44c) -----

public record CarpetaAdminResponse(
    long IdCarpeta, string Codigo, string Nombre, string? Descripcion, int Orden, bool EsSistema,
    bool AplicaFisica, bool AplicaJuridica, bool AplicaGrupo, string Estado, int CantidadTipos);

public record CarpetaGuardarRequest(
    string Codigo, string Nombre, string? Descripcion, int Orden, bool AplicaFisica, bool AplicaJuridica, bool AplicaGrupo);

public record TipoAdminResponse(
    long IdTipoDocumento, string Codigo, string Nombre, string? Descripcion, long IdCarpeta, string Carpeta, int Orden,
    string Cardinalidad, int? Maximo, string Vencimiento, int DiasAviso, string Confidencialidad,
    bool AplicaFisica, bool AplicaJuridica, bool AplicaGrupo, string Estado, int DocumentosVigentes, int RolesQueLoExigen);

public record TipoGuardarRequest(
    string Codigo, string Nombre, string? Descripcion, long IdCarpeta, int Orden, string Cardinalidad, int? Maximo,
    string Vencimiento, int DiasAviso, string Confidencialidad, bool AplicaFisica, bool AplicaJuridica, bool AplicaGrupo);

// ----- Documentos exigidos por rol (r44c) -----

public record RolDocumentosResponse(long IdTipoRol, string Codigo, string Nombre, IReadOnlyList<long> IdsTipoDocumento);
public record TipoExigibleResponse(long IdTipoDocumento, string Nombre, string Carpeta);
public record RolDocumentosAdminResponse(string TipoPersona, IReadOnlyList<TipoExigibleResponse> Tipos, IReadOnlyList<RolDocumentosResponse> Roles);
public record RolDocumentosEditar(long IdTipoRol, IReadOnlyList<long> IdsTipoDocumento);
public record RolDocumentosGuardarRequest(string TipoPersona, IReadOnlyList<RolDocumentosEditar> Roles);

/// <summary>Estado de un documento exigido por los roles del cliente: Cumple, PorVencer, Vencido o Falta.</summary>
public record DocumentoRequeridoItem(long IdTipoDocumento, string Tipo, string Carpeta, IReadOnlyList<string> Roles, string Estado, DateOnly? Vence);

// ----- Fallecimiento (r44c) -----
public record FallecimientoSolicitudRequest(DateOnly? FechaFallecimiento, string? Comentario);
public record FallecimientoInfoResponse(DateOnly? Fecha, string? NumeroGestion);
