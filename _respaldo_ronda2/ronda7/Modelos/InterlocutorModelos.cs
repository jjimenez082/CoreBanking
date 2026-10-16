namespace CoreBancario.Api.Modelos;

// ---------------------------------------------------------------------------
// Datos de entrada. Identificaciones, direcciones y contactos son colecciones
// 1:n: cada elemento puede traer Id (ya existe) o no (es nuevo). En una
// modificacion, lo que existe y no viene en la lista se da de baja.
// ---------------------------------------------------------------------------

public record IdentificacionDto(
    long? Id,
    long IdTipoIdentificacion,
    string Numero,
    long? IdInstitucionEmisora,
    DateOnly? FechaEmision,
    DateOnly? FechaVencimiento,
    bool EsPrincipal
);

/// <summary>
/// Un nivel de la direccion (provincia/estado, canton/ciudad...). Segun la configuracion del
/// pais, el nivel es de LISTA (se envia Id de gen.DIVISION_TERRITORIAL) o de TEXTO (se envia Texto).
/// </summary>
public record NivelDireccionDto(long? Id, string? Texto);

/// <summary>
/// Direccion con su propio pais. Niveles: hasta 4, posicion 0 = nivel 1. El orden y la cantidad
/// los define gen.PAIS_DIRECCION_NIVEL del pais (GET /api/catalogos/direccion/config).
/// </summary>
public record DireccionDto(
    long? Id,
    long IdTipoDireccion,
    long IdPais,
    IReadOnlyList<NivelDireccionDto>? Niveles,
    string? CodigoPostal,
    string Detalle,
    bool EsPrincipal,
    string? Detalle2 = null,
    string? Detalle3 = null
);

public record ContactoDto(
    long? Id,
    long IdTipoContacto,
    string Valor,
    bool EsPrincipal
);

/// <summary>
/// Alta de un cliente Persona Fisica (wizard). Los ids de tipo salen de
/// GET /api/catalogos/listas/{codigo}. IdBorrador (opcional) es el borrador de
/// origen: se elimina al crear el registro definitivo.
/// </summary>
public record InterlocutorAltaRequest(
    long IdPaisRegulador,
    long? IdUnidad,
    string Nombre1,
    string? Nombre2,
    string Apellido1,
    string? Apellido2,
    DateOnly FechaNacimiento,
    IReadOnlyList<IdentificacionDto> Identificaciones,
    IReadOnlyList<DireccionDto>? Direcciones,
    IReadOnlyList<ContactoDto>? Contactos,
    long? IdBorrador
);

/// <summary>Edicion de un cliente: se envia el estado deseado completo; el servidor calcula las diferencias.</summary>
public record InterlocutorModificacionRequest(
    long IdPaisRegulador,
    long? IdUnidad,
    string Nombre1,
    string? Nombre2,
    string Apellido1,
    string? Apellido2,
    DateOnly FechaNacimiento,
    IReadOnlyList<IdentificacionDto> Identificaciones,
    IReadOnlyList<DireccionDto>? Direcciones,
    IReadOnlyList<ContactoDto>? Contactos,
    string? Comentario,
    long? IdGestionDevuelta = null
);

// ---------------------------------------------------------------------------
// Respuestas
// ---------------------------------------------------------------------------

/// <summary>Fila del listado: la identificacion mostrada es la principal.</summary>
public record InterlocutorResponse(
    long IdInterlocutor,
    string Numero,
    string TipoPersona,
    long IdTipoIdentificacion,
    string NumeroIdentificacion,
    string NombreCompleto,
    string Roles,
    string Estado,
    DateTime CreadoEn
);

/// <summary>Resultado de la busqueda global (barra superior).</summary>
public record InterlocutorBusquedaResponse(
    long IdInterlocutor,
    string Numero,
    string Nombre,
    string Roles,
    string Identificaciones,
    string Estado
);

public record InterlocutorListaResponse(
    IReadOnlyList<InterlocutorResponse> Items,
    int Pagina,
    int TamanoPagina,
    int Total
);

public record IdentificacionResponse(
    long Id, long IdTipoIdentificacion, string TipoNombre, string Numero,
    long? IdInstitucionEmisora, string? InstitucionNombre,
    DateOnly? FechaEmision, DateOnly? FechaVencimiento, bool EsPrincipal,
    string CreadoPor, DateTime CreadoEn, string? ModificadoPor, DateTime? ModificadoEn);

/// <summary>Nivel de una direccion ya guardada: Etiqueta (p. ej. "Provincia"), Id si es de lista y Texto a mostrar.</summary>
public record NivelDireccionResponse(int Nivel, string Etiqueta, string Modo, long? Id, string? Texto);

public record DireccionResponse(
    long Id, long IdTipoDireccion, string TipoNombre,
    long IdPais, string PaisNombre, IReadOnlyList<NivelDireccionResponse> Niveles, string? CodigoPostal,
    string Detalle, bool EsPrincipal,
    string CreadoPor, DateTime CreadoEn, string? ModificadoPor, DateTime? ModificadoEn,
    string? Detalle2 = null, string? Detalle3 = null);

public record ContactoResponse(
    long Id, long IdTipoContacto, string TipoCodigo, string TipoNombre, string Valor, bool EsPrincipal,
    string CreadoPor, DateTime CreadoEn, string? ModificadoPor, DateTime? ModificadoEn);

public record RolInterlocutorResponse(long IdTipoRol, string Codigo, string Nombre, string Estado);

/// <summary>Ficha completa: datos del interlocutor, roles y colecciones 1:n (solo registros activos).</summary>
public record InterlocutorDetalleResponse(
    long IdInterlocutor,
    string Numero,
    string TipoPersona,
    string Estado,
    long IdPaisRegulador,
    string PaisReguladorNombre,
    long? IdUnidad,
    string? UnidadNombre,
    long? IdCanalOrigen,
    string? CanalOrigenNombre,
    string? Nombre1,
    string? Nombre2,
    string? Apellido1,
    string? Apellido2,
    DateOnly? FechaNacimiento,
    DateTime CreadoEn,
    string CreadoPor,
    DateTime? ModificadoEn,
    string? ModificadoPor,
    long? IdGestionPendiente,
    IReadOnlyList<RolInterlocutorResponse> Roles,
    IReadOnlyList<IdentificacionResponse> Identificaciones,
    IReadOnlyList<DireccionResponse> Direcciones,
    IReadOnlyList<ContactoResponse> Contactos
);

public record GestionResumenResponse(
    long IdGestion, string Tipo, string Estado, string SolicitadoPor,
    DateTime CreadoEn, DateTime? FechaResolucion, string? Comentario, string? MotivoRechazo, int CamposCambiados);

public record RelacionResponse(
    long IdInterlocutorRelacionado, string Numero, string Nombre, string Roles,
    string TipoRelacion, string Sentido, decimal? PorcentajeParticipacion);

/// <summary>Producto del cliente (cuentas = pasivos, creditos = activos). Vacio hasta construir esos modulos.</summary>
public record ProductoResponse(
    string Clase, string Producto, string Numero, string Estado,
    decimal? Saldo, decimal? Cuota, DateOnly? ProximoPago, int? DiasMora);

/// <summary>Vista 360: ficha, gestiones, relaciones y productos del interlocutor.</summary>
public record Interlocutor360Response(
    InterlocutorDetalleResponse Ficha,
    IReadOnlyList<GestionResumenResponse> GestionesPendientes,
    IReadOnlyList<GestionResumenResponse> GestionesCerradas,
    IReadOnlyList<RelacionResponse> Relaciones,
    IReadOnlyList<ProductoResponse> Productos
);

// ---------------------------------------------------------------------------
// Borradores del wizard de alta
// ---------------------------------------------------------------------------

public record BorradorGuardarRequest(long? IdBorrador, int Paso, System.Text.Json.JsonElement Contenido);

public record BorradorResumenResponse(
    long IdBorrador, string? Nombre, string? Identificacion, int Paso, DateTime ModificadoEn, DateTime VenceEn);

public record BorradorDetalleResponse(
    long IdBorrador, int Paso, DateTime VenceEn, System.Text.Json.JsonElement Contenido);

/// <summary>
/// Identidad y permisos efectivos del actor autenticado (GET /api/yo).
/// El Backoffice la consulta al iniciar sesion para decidir que mostrar
/// u ocultar en la UI segun RBAC (SEG-002) — la API sigue siendo la que
/// realmente hace cumplir el permiso en cada endpoint, esto es solo para
/// no mostrarle al usuario acciones que de todas formas le van a rechazar.
/// </summary>
public record YoResponse(
    string NombreDescriptivo,
    bool EsUsuarioHumano,
    IReadOnlyCollection<string> CodigosPermiso
);


// ---------------------------------------------------------------------------
// Notificaciones (campanita). Se calculan al consultar a partir de gestiones y
// borradores; la marca de "leida" vive en el navegador (por usuario).
// ---------------------------------------------------------------------------

public record NotificacionResponse(
    string Clave,
    string Tipo,
    string Titulo,
    string Detalle,
    DateTime Fecha,
    string Ruta
);
