namespace CoreBancario.Api.Modelos;

/// <summary>Cambio de roles de un cliente: ids de TIPO_ROL_INTERLOCUTOR a agregar y a quitar, con el motivo (obligatorio).</summary>
public record RolSolicitudRequest(IReadOnlyList<long>? Agregar, IReadOnlyList<long>? Quitar, string Motivo, EmpleadoDatosDto? Empleado = null);

/// <summary>Datos del rol Empleado: unidad de la estructura organizativa (sec.DEPARTAMENTO) y numero de empleado.</summary>
public record EmpleadoDatosDto(long IdUnidad, string NumeroEmpleado);

/// <summary>Campos que exige un rol (codigos de ReglasCampos). Siempre se exigen ademas: nombre, primer apellido, tipo y numero de identificacion.</summary>
public record RolRequisitosResponse(long IdTipoRol, string Codigo, string Nombre, IReadOnlyList<string> Requeridos);

public record RolRequisitoCampoResponse(string Codigo, string Seccion, string Etiqueta, string Aplica = "Ambos");

/// <summary>Pantalla de administracion: los campos parametrizables (agrupados por seccion) y lo que exige cada rol.</summary>
public record RolRequisitosAdminResponse(IReadOnlyList<RolRequisitoCampoResponse> Campos, IReadOnlyList<RolRequisitosResponse> Roles, string TipoPersona = "Fisica");

public record RolClienteResponse(long IdTipoRol, string Codigo, string Nombre, string Estado, DateTime FechaAsignacion, string? Detalle = null);

/// <summary>Un periodo en que la persona tuvo un rol: Hasta = null significa que sigue vigente (p. ej. fiador del 12/03/2026 al 05/09/2026).</summary>
public record RolPeriodoResponse(long IdTipoRol, string Codigo, string Nombre, DateTime Desde, DateTime? Hasta);

/// <summary>Rol que el cliente aun no tiene activo; Faltantes = datos que le faltan para asumirlo (vacio = puede agregarse ya).</summary>
public record RolDisponibleResponse(long IdTipoRol, string Codigo, string Nombre, IReadOnlyList<string> Faltantes);

public record RolesClienteResponse(
    long IdInterlocutor, string Numero, string Estado,
    IReadOnlyList<RolClienteResponse> Roles, IReadOnlyList<RolDisponibleResponse> Disponibles, string? NumeroGestionPendiente,
    IReadOnlyList<RolPeriodoResponse> Historial, string TipoPersona = "Fisica");

/// <summary>
/// Resultado de buscar una identificacion para dar de alta a una persona con un rol (flujo igual al de Prospectos).
/// Estado: NUEVO (no existe: se piden los datos), PUEDE_AGREGAR (ya es cliente: se le agrega el rol por gestion, sin tocar sus datos),
/// FALTAN_DATOS (existe pero le faltan datos que exige el rol), YA_TIENE_ROL, GESTION_PENDIENTE, ALTA_PENDIENTE, FUSIONADO, NO_DISPONIBLE.
/// </summary>
public record RolIdentificacionResponse(
    string Estado, string Mensaje, long? IdInterlocutor, long? IdSobreviviente, IReadOnlyList<string> Faltantes,
    InterlocutorDetalleResponse? Cliente, bool PuedeSolicitar);

public record RolRequisitoEditar(long IdTipoRol, IReadOnlyList<string> Requeridos);
public record RolRequisitosGuardarRequest(IReadOnlyList<RolRequisitoEditar> Roles, string? TipoPersona = null);
