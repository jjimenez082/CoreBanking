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

public record NombreComercialDto(long? Id, string Nombre);

/// <summary>Nacionalidad de una persona (pais de la lista PAIS). Una persona puede tener varias.</summary>
public record NacionalidadDto(long? Id, long IdPais);

/// <summary>
/// Datos de identidad de la persona, todos opcionales salvo que un rol los exija: nombre conocido, pais de nacimiento, sexo (lista SEXO),
/// estado civil (lista ESTADO_CIVIL) y nacionalidades 1..N. Si no se envia el bloque en una modificacion, no se toca nada de esto.
/// </summary>
public record IdentidadPersonaDto(
    string? NombreConocido, long? IdPaisNacimiento, long? IdSexo, long? IdEstadoCivil, IReadOnlyList<NacionalidadDto>? Nacionalidades);

public record ActividadDto(long? Id, long IdActividad, string? Descripcion, bool EsPrincipal, DateOnly? FechaInicio = null);

// ---------------------------------------------------------------------------
// Perfil financiero (persona y organizacion). Si el bloque no viene en una modificacion, no se toca nada de esto.
// Los valores de lista (condicion laboral, ocupacion, tipo de ingreso...) son ids de gen.LISTA_VALORES_ITEM; las monedas, ids de gen.MONEDA.
// ---------------------------------------------------------------------------

/// <summary>Residencia fiscal: pais, identificador tributario en ese pais (NIF/TIN), si es la principal y su vigencia.</summary>
public record ResidenciaFiscalDto(long? Id, long IdPais, string? IdentificadorTributario, bool EsPrincipal, DateOnly? FechaDesde = null, DateOnly? FechaHasta = null);

/// <summary>Ingreso declarado: tipo, monto por periodo en una moneda.</summary>
public record IngresoDto(long? Id, long IdTipoIngreso, decimal Monto, long IdMoneda, long IdPeriodicidad);

/// <summary>Origen de fondos o de patrimonio (Clase = Fondos | Patrimonio).</summary>
public record OrigenDto(long? Id, string Clase, long IdOrigen);

/// <summary>Perfil transaccional esperado: tipo de transaccion, frecuencia, rango de monto esperado en el periodo de esa frecuencia (minimo y maximo, opcionales y juntos) y paises de origen/destino.</summary>
public record PerfilTransaccionalDto(long? Id, long IdTipoTransaccion, long IdFrecuencia, decimal? MontoMinimo, decimal? MontoMaximo, long? IdMoneda, long? IdPaisOrigen, long? IdPaisDestino);

/// <summary>
/// Perfil economico 1:1. Persona: condicion laboral, ocupacion, empleador, gastos. Organizacion: patrimonio. Ambas: activos, pasivos
/// (en la moneda IdMonedaBalance, con fecha de corte) y uso esperado de efectivo.
/// </summary>
public record PerfilEconomicoDto(
    long? IdCondicionLaboral, long? IdOcupacion, string? Empleador, decimal? GastosEstimados,
    decimal? ActivosDeclarados, decimal? PasivosDeclarados, decimal? PatrimonioDeclarado,
    long? IdMonedaBalance, DateOnly? FechaCorte);

/// <summary>Cuenta que el cliente mantiene en otra institucion: entidad (lista ENTIDAD_FINANCIERA), numero en formato IBAN, tipo de cuenta y moneda opcional.</summary>
public record CuentaExternaDto(long? Id, long IdEntidad, string NumeroCuenta, long IdTipoCuenta, long? IdMoneda);

public record PerfilFinancieroDto(
    PerfilEconomicoDto? Economico,
    IReadOnlyList<ResidenciaFiscalDto>? ResidenciasFiscales,
    IReadOnlyList<IngresoDto>? Ingresos,
    IReadOnlyList<OrigenDto>? Origenes,
    IReadOnlyList<PerfilTransaccionalDto>? PerfilTransaccional,
    IReadOnlyList<CuentaExternaDto>? CuentasExternas = null);

/// <summary>
/// Datos propios de una ORGANIZACION (persona juridica). Siempre obligatorios: razon social, forma juridica, pais de constitucion y estado legal.
/// El resto depende de los requisitos del rol (Administracion › Requisitos por rol › Organizacion).
/// </summary>
public record OrganizacionDto(
    string RazonSocial,
    IReadOnlyList<NombreComercialDto>? NombresComerciales,
    long IdFormaJuridica,
    long IdPaisConstitucion,
    DateOnly? FechaConstitucion,
    string? ReferenciaRegistral,
    long IdEstadoLegal,
    DateOnly? FechaDisolucion,
    IReadOnlyList<ActividadDto>? Actividades
);

/// <summary>
/// Alta de un cliente (wizard). TipoPersona = Fisica (por defecto) o Juridica; si es Juridica se usa Organizacion y los nombres/apellidos se ignoran. Los ids de tipo salen de
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
    DateOnly? FechaNacimiento,
    IReadOnlyList<IdentificacionDto> Identificaciones,
    IReadOnlyList<DireccionDto>? Direcciones,
    IReadOnlyList<ContactoDto>? Contactos,
    long? IdBorrador,
    long? IdGestionDevuelta = null,
    long? IdProspecto = null,
    RevisionDuplicadosDto? RevisionDuplicados = null,
    long? IdTipoRol = null,
    string? TipoPersona = null,
    OrganizacionDto? Organizacion = null,
    GrupoDto? Grupo = null,
    IdentidadPersonaDto? Identidad = null,
    PerfilFinancieroDto? PerfilFinanciero = null,
    EmpleadoDatosDto? Empleado = null
);

/// <summary>Edicion de un cliente: se envia el estado deseado completo; el servidor calcula las diferencias.</summary>
public record InterlocutorModificacionRequest(
    long IdPaisRegulador,
    long? IdUnidad,
    string Nombre1,
    string? Nombre2,
    string Apellido1,
    string? Apellido2,
    DateOnly? FechaNacimiento,
    IReadOnlyList<IdentificacionDto> Identificaciones,
    IReadOnlyList<DireccionDto>? Direcciones,
    IReadOnlyList<ContactoDto>? Contactos,
    string? Comentario,
    long? IdGestionDevuelta = null,
    OrganizacionDto? Organizacion = null,
    IdentidadPersonaDto? Identidad = null,
    PerfilFinancieroDto? PerfilFinanciero = null
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

public record NombreComercialResponse(long Id, string Nombre);

public record NacionalidadResponse(long Id, long IdPais, string PaisNombre);

/// <summary>Datos de identidad de la persona en la ficha.</summary>
public record IdentidadPersonaResponse(
    string? NombreConocido, long? IdPaisNacimiento, string? PaisNacimientoNombre, long? IdSexo, string? SexoNombre,
    long? IdEstadoCivil, string? EstadoCivilNombre, IReadOnlyList<NacionalidadResponse> Nacionalidades);

public record ActividadResponse(long Id, long IdActividad, string ActividadCodigo, string ActividadNombre, string? Descripcion, bool EsPrincipal, DateOnly? FechaInicio);

/// <summary>Datos propios de la organizacion en la ficha.</summary>
public record OrganizacionResponse(
    string RazonSocial, long? IdFormaJuridica, string? FormaJuridicaNombre, long? IdPaisConstitucion, string? PaisConstitucionNombre,
    DateOnly? FechaConstitucion, string? ReferenciaRegistral, long? IdEstadoLegal, string? EstadoLegalNombre, DateOnly? FechaDisolucion,
    IReadOnlyList<NombreComercialResponse> NombresComerciales, IReadOnlyList<ActividadResponse> Actividades);

// Perfil financiero en la ficha: cada valor de lista viene con su nombre.
public record ResidenciaFiscalResponse(long Id, long IdPais, string PaisNombre, string? IdentificadorTributario, bool EsPrincipal, DateOnly? FechaDesde, DateOnly? FechaHasta);
public record IngresoResponse(long Id, long IdTipoIngreso, string TipoNombre, decimal Monto, long IdMoneda, string MonedaCodigo, long IdPeriodicidad, string PeriodicidadNombre);
public record OrigenResponse(long Id, string Clase, long IdOrigen, string OrigenNombre);
public record PerfilTransaccionalResponse(
    long Id, long IdTipoTransaccion, string TipoNombre, long IdFrecuencia, string FrecuenciaNombre, decimal? MontoMinimo, decimal? MontoMaximo,
    long? IdMoneda, string? MonedaCodigo, long? IdPaisOrigen, string? PaisOrigenNombre, long? IdPaisDestino, string? PaisDestinoNombre);
public record PerfilEconomicoResponse(
    long? IdCondicionLaboral, string? CondicionLaboralNombre, long? IdOcupacion, string? OcupacionNombre, string? Empleador, decimal? GastosEstimados,
    decimal? ActivosDeclarados, decimal? PasivosDeclarados, decimal? PatrimonioDeclarado, long? IdMonedaBalance, string? MonedaBalanceCodigo,
    DateOnly? FechaCorte);
public record CuentaExternaResponse(long Id, long IdEntidad, string EntidadNombre, string NumeroCuenta, long IdTipoCuenta, string TipoCuentaNombre, long? IdMoneda, string? MonedaCodigo);
public record PerfilFinancieroResponse(
    PerfilEconomicoResponse? Economico,
    IReadOnlyList<ResidenciaFiscalResponse> ResidenciasFiscales,
    IReadOnlyList<IngresoResponse> Ingresos,
    IReadOnlyList<OrigenResponse> Origenes,
    IReadOnlyList<PerfilTransaccionalResponse> PerfilTransaccional,
    IReadOnlyList<CuentaExternaResponse>? CuentasExternas = null);

public record RolInterlocutorResponse(long IdTipoRol, string Codigo, string Nombre, string Estado, DateTime? Desde = null, string? Detalle = null);

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
    IReadOnlyList<ContactoResponse> Contactos,
    string? NumeroGestionPendiente = null,
    long? IdSobreviviente = null,
    string? NumeroSobreviviente = null,
    IReadOnlyList<RolPeriodoResponse>? HistorialRoles = null,
    OrganizacionResponse? Organizacion = null,
    GrupoResponse? Grupo = null,
    IdentidadPersonaResponse? Identidad = null,
    PerfilFinancieroResponse? PerfilFinanciero = null,
    IReadOnlyList<BloqueoVigenteResponse>? Bloqueos = null,
    bool Bloqueado = false,
    KycVigenciaResponse? KycVigencia = null,
    MascarasInfoResponse? Mascaras = null
);

/// <summary>Campos de la ficha que el perfil de datos del usuario enmascara. Textos = lo que se muestra en lugar de las fechas y montos (que viajan vacios).</summary>
public record MascaraCampoInfo(string Campo, string Etiqueta, bool PuedeRevelar);
public record MascarasInfoResponse(IReadOnlyList<MascaraCampoInfo> Campos, IReadOnlyDictionary<string, string> Textos);

public record GestionResumenResponse(
    long IdGestion, string Tipo, string Estado, string SolicitadoPor,
    DateTime CreadoEn, DateTime? FechaResolucion, string? Comentario, string? MotivoRechazo, int CamposCambiados,
    string? Numero = null);

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

/// <summary>
/// Resultado del alta hecha desde el Backoffice: siempre nace como gestion. Si la regla es automatica el cliente ya
/// existe (Interlocutor viene lleno); si no, la gestion queda pendiente y el cliente se crea al aprobarse.
/// </summary>
public record InterlocutorAltaGestionResponse(
    long IdGestion,
    string? NumeroGestion,
    bool Aplicada,
    InterlocutorDetalleResponse? Interlocutor
);
