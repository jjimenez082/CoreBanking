// Espejo de los DTOs de CoreBancario.Api (Modelos/InterlocutorModelos.cs).

// ----- Entrada: identificaciones, direcciones y contactos son colecciones 1:n -----

export interface IdentificacionDto {
  id?: number | null;
  idTipoIdentificacion: number;
  numero: string;
  idInstitucionEmisora?: number | null;
  fechaEmision?: string | null; // "YYYY-MM-DD"
  fechaVencimiento?: string | null;
  esPrincipal: boolean;
}

/** Un nivel de la direccion: id de la division (niveles de lista) o texto (niveles de texto libre). */
export interface NivelDireccionDto {
  id?: number | null;
  texto?: string | null;
}

export interface DireccionDto {
  id?: number | null;
  idTipoDireccion: number;
  idPais: number;
  niveles?: NivelDireccionDto[];
  codigoPostal?: string | null;
  detalle: string;
  detalle2?: string | null;
  detalle3?: string | null;
  esPrincipal: boolean;
}

export interface ContactoDto {
  id?: number | null;
  idTipoContacto: number;
  valor: string;
  esPrincipal: boolean;
}

export interface NombreComercialDto { id?: number | null; nombre: string }
export interface ActividadDto { id?: number | null; idActividad: number; descripcion?: string | null; esPrincipal: boolean; fechaInicio?: string | null }

/** Datos propios de una organización (persona jurídica). */
export interface OrganizacionDto {
  razonSocial: string;
  nombresComerciales: NombreComercialDto[];
  idFormaJuridica: number;
  idPaisConstitucion: number;
  fechaConstitucion?: string | null;
  referenciaRegistral?: string | null;
  idEstadoLegal: number;
  fechaDisolucion?: string | null;
  actividades: ActividadDto[];
}

export interface NombreComercialResponse { id: number; nombre: string }
export interface ActividadResponse {
  id: number; idActividad: number; actividadCodigo: string; actividadNombre: string;
  descripcion?: string | null; esPrincipal: boolean; fechaInicio?: string | null;
}
export interface OrganizacionResponse {
  razonSocial: string;
  idFormaJuridica?: number | null; formaJuridicaNombre?: string | null;
  idPaisConstitucion?: number | null; paisConstitucionNombre?: string | null;
  fechaConstitucion?: string | null; referenciaRegistral?: string | null;
  idEstadoLegal?: number | null; estadoLegalNombre?: string | null; fechaDisolucion?: string | null;
  nombresComerciales: NombreComercialResponse[];
  actividades: ActividadResponse[];
}

/** Nacionalidad de una persona (país de la lista PAIS). */
export interface NacionalidadDto { id?: number | null; idPais: number }

/** Identidad de la persona: todo opcional salvo lo que exija un rol. */
export interface IdentidadPersonaDto {
  nombreConocido?: string | null;
  idPaisNacimiento?: number | null;
  idSexo?: number | null;
  idEstadoCivil?: number | null;
  nacionalidades?: NacionalidadDto[] | null;
}

export interface IdentidadPersonaResponse {
  nombreConocido?: string | null;
  idPaisNacimiento?: number | null; paisNacimientoNombre?: string | null;
  idSexo?: number | null; sexoNombre?: string | null;
  idEstadoCivil?: number | null; estadoCivilNombre?: string | null;
  nacionalidades: { id: number; idPais: number; paisNombre: string }[];
}

// ----- Perfil financiero (persona y organización) -----
export interface ResidenciaFiscalDto { id?: number | null; idPais: number; identificadorTributario?: string | null; esPrincipal: boolean; fechaDesde?: string | null; fechaHasta?: string | null }
export interface IngresoDto { id?: number | null; idTipoIngreso: number; monto: number; idMoneda: number; idPeriodicidad: number }
export interface OrigenDto { id?: number | null; clase: "Fondos" | "Patrimonio"; idOrigen: number }
export interface PerfilTransaccionalDto {
  id?: number | null; idTipoTransaccion: number; idFrecuencia: number; montoMinimo?: number | null; montoMaximo?: number | null; idMoneda?: number | null;
  idPaisOrigen?: number | null; idPaisDestino?: number | null;
}
export interface CuentaExternaDto { id?: number | null; idEntidad: number; numeroCuenta: string; idTipoCuenta: number; idMoneda?: number | null }
export interface PerfilEconomicoDto {
  idCondicionLaboral?: number | null; idOcupacion?: number | null; empleador?: string | null; gastosEstimados?: number | null;
  activosDeclarados?: number | null; pasivosDeclarados?: number | null; patrimonioDeclarado?: number | null;
  idMonedaBalance?: number | null; fechaCorte?: string | null;
}
export interface PerfilFinancieroDto {
  economico?: PerfilEconomicoDto | null;
  residenciasFiscales?: ResidenciaFiscalDto[] | null;
  ingresos?: IngresoDto[] | null;
  origenes?: OrigenDto[] | null;
  perfilTransaccional?: PerfilTransaccionalDto[] | null;
  cuentasExternas?: CuentaExternaDto[] | null;
}
export interface PerfilFinancieroResponse {
  economico?: {
    idCondicionLaboral?: number | null; condicionLaboralNombre?: string | null; idOcupacion?: number | null; ocupacionNombre?: string | null;
    empleador?: string | null; gastosEstimados?: number | null; activosDeclarados?: number | null; pasivosDeclarados?: number | null;
    patrimonioDeclarado?: number | null; idMonedaBalance?: number | null; monedaBalanceCodigo?: string | null; fechaCorte?: string | null;
  } | null;
  residenciasFiscales: { id: number; idPais: number; paisNombre: string; identificadorTributario?: string | null; esPrincipal: boolean; fechaDesde?: string | null; fechaHasta?: string | null }[];
  ingresos: { id: number; idTipoIngreso: number; tipoNombre: string; monto: number; idMoneda: number; monedaCodigo: string; idPeriodicidad: number; periodicidadNombre: string }[];
  origenes: { id: number; clase: "Fondos" | "Patrimonio"; idOrigen: number; origenNombre: string }[];
  cuentasExternas?: { id: number; idEntidad: number; entidadNombre: string; numeroCuenta: string; idTipoCuenta: number; tipoCuentaNombre: string; idMoneda?: number | null; monedaCodigo?: string | null }[];
  perfilTransaccional: {
    id: number; idTipoTransaccion: number; tipoNombre: string; idFrecuencia: number; frecuenciaNombre: string; montoMinimo?: number | null; montoMaximo?: number | null;
    idMoneda?: number | null; monedaCodigo?: string | null; idPaisOrigen?: number | null; paisOrigenNombre?: string | null;
    idPaisDestino?: number | null; paisDestinoNombre?: string | null;
  }[];
}

export interface InterlocutorAltaRequest {
  /** Solo si el rol del alta es Empleado. */
  empleado?: EmpleadoDatos | null;
  /** Perfil financiero (residencia fiscal, perfil económico, ingresos, orígenes, perfil transaccional). */
  perfilFinanciero?: PerfilFinancieroDto | null;
  /** Datos de identidad de la persona (nombre conocido, nacionalidades, sexo...). */
  identidad?: IdentidadPersonaDto | null;
  /** Fisica (por defecto) o Juridica. En Juridica se usa `organizacion` y los nombres/apellidos van vacíos. */
  tipoPersona?: string | null;
  organizacion?: OrganizacionDto | null;
  idPaisRegulador: number;
  idUnidad?: number | null;
  nombre1: string;
  nombre2?: string | null;
  apellido1: string;
  apellido2?: string | null;
  fechaNacimiento?: string | null; // "YYYY-MM-DD"; opcional si el rol no la exige
  identificaciones: IdentificacionDto[];
  direcciones: DireccionDto[];
  contactos: ContactoDto[];
  idBorrador?: number | null;
  /** Gestión de alta devuelta que se corrige con este envío. */
  idGestionDevuelta?: number | null;
  /** Prospecto que se promueve a cliente con esta alta (se marca Convertido al aprobarse). */
  idProspecto?: number | null;
  /** Decisión tras revisar personas parecidas ya registradas (control de calidad de datos). */
  revisionDuplicados?: RevisionDuplicados | null;
  /** Rol con el que se da de alta (null = Cliente). */
  idTipoRol?: number | null;
}

/** Respuesta del alta desde el Backoffice: siempre nace como gestión (aplicada al instante si la regla es automática). */
export interface InterlocutorAltaGestionResponse {
  idGestion: number;
  numeroGestion: string | null;
  aplicada: boolean;
  interlocutor: InterlocutorDetalleResponse | null;
}

export interface InterlocutorModificacionRequest {
  perfilFinanciero?: PerfilFinancieroDto | null;
  identidad?: IdentidadPersonaDto | null;
  organizacion?: OrganizacionDto | null;
  idPaisRegulador: number;
  idUnidad?: number | null;
  nombre1: string;
  nombre2?: string | null;
  apellido1: string;
  apellido2?: string | null;
  fechaNacimiento?: string | null;
  identificaciones: IdentificacionDto[];
  direcciones: DireccionDto[];
  contactos: ContactoDto[];
  comentario?: string | null;
  idGestionDevuelta?: number | null;
}

// ----- Respuestas -----

export interface InterlocutorResponse {
  idInterlocutor: number;
  numero: string;
  tipoPersona: string;
  idTipoIdentificacion: number;
  numeroIdentificacion: string;
  nombreCompleto: string;
  roles: string;
  estado: string;
  creadoEn: string;
}

export interface InterlocutorBusquedaResponse {
  idInterlocutor: number;
  numero: string;
  nombre: string;
  roles: string;
  identificaciones: string;
  estado: string;
}

interface AuditoriaFila {
  creadoPor: string;
  creadoEn: string;
  modificadoPor?: string | null;
  modificadoEn?: string | null;
}

export interface IdentificacionResponse extends AuditoriaFila {
  id: number;
  idTipoIdentificacion: number;
  tipoNombre: string;
  numero: string;
  idInstitucionEmisora?: number | null;
  institucionNombre?: string | null;
  fechaEmision?: string | null;
  fechaVencimiento?: string | null;
  esPrincipal: boolean;
}

export interface NivelDireccionResponse {
  nivel: number;
  etiqueta: string;
  modo: "Lista" | "Texto";
  id?: number | null;
  texto?: string | null; // para niveles de lista, el nombre de la division
}

export interface DireccionResponse extends AuditoriaFila {
  id: number;
  idTipoDireccion: number;
  tipoNombre: string;
  idPais: number;
  paisNombre: string;
  niveles: NivelDireccionResponse[];
  codigoPostal?: string | null;
  detalle: string;
  detalle2?: string | null;
  detalle3?: string | null;
  esPrincipal: boolean;
}

export interface ContactoResponse extends AuditoriaFila {
  id: number;
  idTipoContacto: number;
  tipoCodigo: string;
  tipoNombre: string;
  valor: string;
  esPrincipal: boolean;
}

export interface RolInterlocutorResponse {
  idTipoRol: number;
  codigo: string;
  nombre: string;
  estado: string;
  /** Desde cuándo tiene el rol (periodo vigente). */
  desde?: string | null;
  /** Datos propios del rol (Empleado: unidad y número de empleado). */
  detalle?: string | null;
}

/** Periodo en que la persona tuvo un rol; hasta = null: sigue vigente. */
export interface RolPeriodo { idTipoRol: number; codigo: string; nombre: string; desde: string; hasta?: string | null }

export interface GrupoResponse {
  nombre: string;
  idTipoGrupo?: number | null;
  tipoGrupo?: string | null;
  descripcion?: string | null;
  fechaFormacion?: string | null;
  cantidadMiembros: number;
}

export interface InterlocutorDetalleResponse {
  idInterlocutor: number;
  numero: string;
  tipoPersona: string;
  estado: string;
  idPaisRegulador: number;
  paisReguladorNombre: string;
  idUnidad?: number | null;
  unidadNombre?: string | null;
  idCanalOrigen?: number | null;
  canalOrigenNombre?: string | null;
  nombre1?: string | null;
  nombre2?: string | null;
  apellido1?: string | null;
  apellido2?: string | null;
  fechaNacimiento?: string | null;
  creadoEn: string;
  creadoPor: string;
  modificadoEn?: string | null;
  modificadoPor?: string | null;
  idGestionPendiente?: number | null;
  numeroGestionPendiente?: string | null;
  idSobreviviente?: number | null;
  numeroSobreviviente?: string | null;
  /** Periodos de cada rol (cerrados y vigentes). */
  historialRoles?: RolPeriodo[];
  /** Solo si tipoPersona = Juridica. */
  organizacion?: OrganizacionResponse | null;
  /** Solo si tipoPersona = Grupo. */
  grupo?: GrupoResponse | null;
  /** Solo si tipoPersona = Fisica. */
  identidad?: IdentidadPersonaResponse | null;
  /** Persona y organización. */
  perfilFinanciero?: PerfilFinancieroResponse | null;
  bloqueos?: BloqueoVigenteResponse[] | null;
  bloqueado?: boolean;
  kycVigencia?: KycVigenciaResponse | null;
  /** Campos que el perfil de datos del usuario enmascara en esta ficha (null = ninguno). */
  mascaras?: MascarasInfoResponse | null;
  roles: RolInterlocutorResponse[];
  identificaciones: IdentificacionResponse[];
  direcciones: DireccionResponse[];
  contactos: ContactoResponse[];
}

export interface GestionResumenResponse {
  idGestion: number;
  tipo: string;
  estado: string;
  solicitadoPor: string;
  creadoEn: string;
  fechaResolucion?: string | null;
  comentario?: string | null;
  motivoRechazo?: string | null;
  camposCambiados: number;
  numero?: string | null;
}

export interface RelacionResponse {
  idInterlocutorRelacionado: number;
  numero: string;
  nombre: string;
  roles: string;
  tipoRelacion: string;
  sentido: string;
  porcentajeParticipacion?: number | null;
}

export interface ProductoResponse {
  clase: string; // "Pasivo" (cuenta) | "Activo" (credito)
  producto: string;
  numero: string;
  estado: string;
  saldo?: number | null;
  cuota?: number | null;
  proximoPago?: string | null;
  diasMora?: number | null;
}

export interface Interlocutor360Response {
  ficha: InterlocutorDetalleResponse;
  gestionesPendientes: GestionResumenResponse[];
  gestionesCerradas: GestionResumenResponse[];
  relaciones: RelacionResponse[];
  productos: ProductoResponse[];
}

// ----- Borradores del wizard -----

export interface BorradorResumenResponse {
  idBorrador: number;
  nombre?: string | null;
  identificacion?: string | null;
  paso: number;
  modificadoEn: string;
  venceEn: string;
}

export interface BorradorDetalleResponse {
  idBorrador: number;
  paso: number;
  venceEn: string;
  contenido: unknown;
}

// ----- Notificaciones (campanita) -----

export interface NotificacionResponse {
  clave: string;
  tipo: string;
  titulo: string;
  detalle: string;
  fecha: string;
  ruta: string;
}

export interface InterlocutorListaResponse {
  items: InterlocutorResponse[];
  pagina: number;
  tamanoPagina: number;
  total: number;
}

export interface GestionResponse {
  idGestion: number;
  tipoGestion: string;
  estado: string;
  entidadNegocio: string;
  llaveRegistro: string;
  solicitadoPor: string;
  creadoEn: string;
  esMia: boolean;
  puedeResolver: boolean;
  fechaResolucion?: string | null;
  idGestionOrigen?: number | null;
  idGestionCorreccion?: number | null;
  numeroCorreccion: number;
  resueltaPor?: string | null;
  resolucionTipo?: string | null;
  nivelActual?: number;
  nivelesRequeridos?: number;
  pendienteDe?: string | null;
  numero?: string | null;
  numeroOrigen?: string | null;
}

export interface ApiErrorBody {
  error: string;
  mensaje: string;
}

export interface YoResponse {
  nombreDescriptivo: string;
  esUsuarioHumano: boolean;
  codigosPermiso: string[];
}

// Espejo de los DTOs del modulo de Seguridad (Modelos/SeguridadModelos.cs).

export interface ObjetoAutorizacionResponse {
  idObjetoAutorizacion: number;
  codigo: string;
  nombre: string;
  tipoObjeto: string;
  descripcion?: string | null;
}

export interface RolResponse {
  idRolAutorizacion: number;
  codigo: string;
  nombre: string;
  estado: string;
  codigosObjetoAutorizacion: string[];
}

export interface RolCrearRequest {
  codigo: string;
  nombre: string;
}

export interface RolActualizarObjetosRequest {
  codigosObjetoAutorizacion: string[];
}

export interface UsuarioResponse {
  idUsuario: number;
  nombreCompleto: string;
  correo: string;
  estado: string;
  codigosRol: string[];
}

export interface UsuarioActualizarRolesRequest {
  codigosRol: string[];
}

// Espejo de los DTOs de Listas de Valores (Modelos/ListasValoresModelos.cs).

export interface ListaTipoResponse {
  idListaValoresTipo: number;
  codigo: string;
  nombre: string;
  descripcion?: string | null;
  estado: string;
  esSistema: boolean;
  cantidadItems: number;
}

export interface ListaItemResponse {
  idListaValoresItem: number;
  idListaValoresTipo: number;
  codigo: string;
  nombre: string;
  orden?: number | null;
  estado: string;
  esSistema: boolean;
  enUso: boolean;
  descripcion?: string | null;
}

// Catalogos para combo boxes (Modelos/CatalogosModelos.cs).

export interface CatalogoItemResponse {
  id: number;
  codigo: string;
  nombre: string;
  esPredeterminado: boolean;
  descripcion?: string | null;
}

// Consulta/edicion de clientes y gestiones (Modelos/GestionModelos.cs).

export interface GestionCampoResponse {
  nombreCampo: string;
  etiqueta: string;
  valorAnterior?: string | null;
  valorNuevo?: string | null;
  anteriorCrudo?: string | null;
  nuevoCrudo?: string | null;
}

export interface GestionDetalleResponse {
  idGestion: number;
  tipoGestion: string;
  estado: string;
  entidadNegocio: string;
  llaveRegistro: string;
  solicitadoPor: string;
  comentario?: string | null;
  motivoRechazo?: string | null;
  creadoEn: string;
  fechaResolucion?: string | null;
  campos: GestionCampoResponse[];
  llaveCruda: string;
  esMia: boolean;
  puedeResolver: boolean;
  comentarioDevolucion?: string | null;
  devueltoPor?: string | null;
  fechaDevolucion?: string | null;
  idGestionOrigen?: number | null;
  idGestionCorreccion?: number | null;
  numeroCorreccion: number;
  resueltaPor?: string | null;
  resolucionTipo?: string | null;
  resueltaEn?: string | null;
  nivelActual?: number;
  nivelesRequeridos?: number;
  pendienteDe?: string | null;
  regla?: string | null;
  niveles?: GestionNivelResponse[] | null;
  numero?: string | null;
  numeroOrigen?: string | null;
  numeroCorreccionRef?: string | null;
}

export interface GestionNivelResponse {
  nivel: number;
  tipo: string; // USUARIO | GRUPO | SUPERIOR | AUTOMATICA
  asignado?: string | null;
  estado: string;
  aprobador?: string | null;
  fecha?: string | null;
  comentario?: string | null;
}

// ----- Rangos de numeracion (Administracion) -----

export interface RangoNumeracionResponse {
  idRangoNumeracion: number;
  objeto: string;
  objetoNombre: string;
  idAlcance: number;
  longitud: number;
  numeroDesde: number;
  numeroHasta: number;
  ultimoAsignado: number;
  tamanoBloque: number;
  estado: string;
  disponibles: number;
  proximo?: string | null;
  modificadoPor?: string | null;
  modificadoEn?: string | null;
}

export interface RangoNumeracionCrearRequest {
  objeto: string;
  longitud: number;
  numeroDesde: number;
  numeroHasta: number;
  tamanoBloque: number;
}

export interface RangoNumeracionEditarRequest {
  longitud: number;
  numeroDesde: number;
  numeroHasta: number;
  tamanoBloque: number;
  estado: string;
}

// ----- Direccion por pais y division territorial -----

export interface NivelConfig {
  nivel: number;
  nombre: string;
  modo: "Lista" | "Texto";
  esObligatorio: boolean;
}

export interface DireccionConfig {
  idPais: number;
  paisNombre: string;
  niveles: NivelConfig[];
  etiquetaCodigoPostal?: string | null;
  patronCodigoPostal?: string | null;
  codigoPostalObligatorio: boolean;
  esPorDefecto: boolean;
}

export interface DivisionItem {
  id: number;
  codigo: string;
  nombre: string;
}

export interface DivisionAdmin {
  idDivision: number;
  idPais: number;
  nivel: number;
  idDivisionPadre?: number | null;
  nombrePadre?: string | null;
  codigo: string;
  nombre: string;
  estado: string;
  cantidadHijos: number;
}

export interface DivisionImportFila {
  nivel: number;
  codigo: string;
  codigoPadre?: string | null;
  nombre: string;
}

export interface DivisionImportResponse {
  creadas: number;
  actualizadas: number;
  errores: string[];
}

// ----- Estructura organizativa y usuarios -----

export interface UnidadAdmin {
  idUnidad: number;
  codigo: string;
  nombre: string;
  idTipoUnidad: number;
  tipoNombre: string;
  idPadre?: number | null;
  nombrePadre?: string | null;
  idResponsable?: number | null;
  responsableNombre?: string | null;
  estado: string;
  cantidadHijos: number;
  cantidadUsuarios: number;
  ruta: string;
  profundidad: number;
  permiteRegistros: boolean;
}

export interface UnidadItem {
  id: number;
  codigo: string;
  nombre: string;
  ruta: string;
  tipoNombre: string;
  esPredeterminado: boolean;
}

export interface UsuarioAdmin {
  idUsuario: number;
  username?: string | null;
  nombreCompleto: string;
  nombres?: string | null;
  apellidos?: string | null;
  correo: string;
  estado: string;
  idUnidad?: number | null;
  unidadNombre?: string | null;
  idSuperior?: number | null;
  superiorNombre?: string | null;
  puesto?: string | null;
  codigosRol: string[];
  creadoEn: string;
  creadoPor: string;
  modificadoEn?: string | null;
  modificadoPor?: string | null;
  /** FISICO (persona, inicia sesión) o INTEGRACION (usuario técnico, no inicia sesión). */
  tipoUsuario: "FISICO" | "INTEGRACION";
}

export interface UsuarioCredencial {
  idUsuario: number;
  username: string;
  passwordTemporal: string;
}

// ----- Administracion transversal ---------------------------------------------------------------------------
export interface MonedaAdmin {
  idMoneda: number;
  codigo: string;
  nombre: string;
  simbolo: string;
  decimales: number;
  esBase: boolean;
  estado: string;
  cantidadTiposCambio: number;
}

export interface TipoCambioFila {
  idTipoCambio: number;
  idMoneda: number;
  monedaCodigo: string;
  monedaNombre: string;
  fechaVigencia: string;
  compra: number;
  venta: number;
  contable: number;
  origen: string;
  idGestion?: number | null;
  numeroGestion?: string | null;
  creadoPor: string;
  creadoEn: string;
}

export interface TipoCambioVigente {
  idMoneda: number;
  monedaCodigo: string;
  monedaNombre: string;
  monedaBase: string;
  fechaVigencia?: string | null;
  compra?: number | null;
  venta?: number | null;
  contable?: number | null;
}

export interface FeriadoFila {
  idFeriado: number;
  idPais: number;
  paisNombre: string;
  fecha: string;
  nombre: string;
  esRecurrente: boolean;
  estado: string;
}

export interface ParametroFila {
  idParametro: number;
  codigo: string;
  nombre: string;
  descripcion?: string | null;
  categoria: string;
  tipoDato: string;
  valor: string;
  valorDefecto: string;
  esDefecto: boolean;
  modificadoPor?: string | null;
  modificadoEn?: string | null;
}

export interface AuditoriaEntidad {
  codigo: string;
  nombre: string;
}

export interface AuditoriaRegistro {
  entidadNombre: string;
  idLog: number;
  fechaHora: string;
  entidad: string;
  llave: string;
  accion: string;
  campo?: string | null;
  anterior?: string | null;
  nuevo?: string | null;
  idGestion?: number | null;
  numeroGestion?: string | null;
  usuario: string;
}

export interface AuditoriaPagina {
  items: AuditoriaRegistro[];
  pagina: number;
  tamanoPagina: number;
  total: number;
}

export interface AuditoriaDetalle {
  idLog: number;
  accion: string;
  campo?: string | null;
  anterior?: string | null;
  nuevo?: string | null;
}

/** Encabezado de auditoría: una operación sobre un registro, con todos sus campos en el detalle. */
export interface AuditoriaEvento {
  clave: string;
  fechaHora: string;
  usuario: string;
  entidad: string;
  entidadNombre: string;
  llave: string;
  accion: string;
  resumen: string;
  idGestion?: number | null;
  numeroGestion?: string | null;
  cantidad: number;
  detalle: AuditoriaDetalle[];
}

export interface AuditoriaEventoPagina {
  items: AuditoriaEvento[];
  pagina: number;
  tamanoPagina: number;
  total: number;
}

export interface AuditoriaPeriodo { valor: number; cantidad: number; ultimo: string }
export interface AuditoriaClienteFusionado { idInterlocutor: number; numero: string }
export interface AuditoriaPeriodos {
  nivel: "anio" | "mes" | "dia";
  anio?: number | null;
  mes?: number | null;
  periodos: AuditoriaPeriodo[];
  ultimoMovimiento?: string | null;
  totalEventos: number;
  fusionados: AuditoriaClienteFusionado[];
}

export interface IntegracionFila {
  idUsuarioIntegracion: number;
  clientId: string;
  nombre: string;
  estado: string;
  idCanal?: number | null;
  canalNombre?: string | null;
  idUsuario?: number | null;
  usuarioNombre?: string | null;
}

// ----- Cambio de asignacion de aprobador (Modelos/GestionModelos.cs) -----
export interface UsuarioAprobadorResponse { idUsuario: number; nombre: string }
export interface ReasignacionFilaResponse {
  idGestion: number; numero: string; tipo: string; registro: string; solicitadoPor: string;
  nivel: number; nivelesRequeridos: number; creadoEn: string;
}
export interface ReasignacionResultadoResponse { reasignadas: number; rechazadas: { gestion: string; motivo: string }[] }

// ----- Administracion de aprobaciones (Modelos/AprobacionAdminModelos.cs) -------------------------------------
export interface TipoGestionAdmin {
  idTipoGestion: number; codigo: string; nombre: string; categoria: string;
  permiteRepetir: boolean; reglasActivas: number; reglasTotal: number;
  entidadCodigo: string; entidadNombre: string; entidadOrden: number;
}
export interface CondicionRegla { variable: string; operador: string; valor: string; valorTexto?: string }
export interface NivelRegla { nivel?: number; tipo: string; idUsuario?: number | null; idGrupo?: number | null; destino?: string }
export interface ReglaAdmin {
  idRegla: number; idTipoGestion: number; nombre: string; prioridad: number; estado: string;
  condiciones: CondicionRegla[]; niveles: NivelRegla[]; gestionesUsandola: number;
}
export interface SimulacionResultado {
  solicitante: string; resumen: string; idReglaAplicada?: number | null; reglaAplicada?: string | null;
  reglas: { idRegla: number; nombre: string; prioridad: number; aplica: boolean; condiciones: { texto: string; cumple: boolean }[] }[];
  niveles: { nivel: number; tipo: string; aprobador: string; nota?: string | null }[];
}
export interface GrupoAdmin {
  idGrupo: number; codigo: string; nombre: string; descripcion?: string | null; estado: string; miembros: number; reglasQueLoUsan: number;
}
export interface GrupoCatalogo { idGrupo: number; codigo: string; nombre: string }
export interface GrupoMiembro { idUsuario: number; nombre: string; username?: string | null; unidad?: string | null }

export interface IntegracionCredencial { idUsuarioIntegracion: number; clientId: string; clientSecret: string; tokenUrl: string }

/** Usuario activo para llenar un combo (Modelos/OrganizacionModelos.cs · UsuarioItemResponse). */
export interface UsuarioItem { id: number; nombre: string; username?: string | null }

export interface IdentificacionValidacion {
  existePersona: boolean;
  idInterlocutor?: number | null;
  numeroInterlocutor?: string | null;
  nombre?: string | null;
  esCliente: boolean;
  roles: string[];
  idProspecto?: number | null;
  estadoProspecto?: string | null;
  nombreProspecto?: string | null;
}

export interface ProspectoFila {
  idProspecto: number;
  nombre: string;
  nombre1?: string | null;
  nombre2?: string | null;
  apellido1?: string | null;
  apellido2?: string | null;
  fechaNacimiento?: string | null;
  idTipoIdentificacion?: number | null;
  tipoIdentificacion?: string | null;
  numeroIdentificacion?: string | null;
  telefono?: string | null;
  correo?: string | null;
  canalOrigen?: string | null;
  estado: string;
  fechaRegistro: string;
  fechaConversion?: string | null;
  idInterlocutor?: number | null;
  creadoPor: string;
  idUnidad?: number | null;
  unidadNombre?: string | null;
  direccion?: DireccionDto | null;
  idGestionPendiente?: number | null;
  numeroGestionPendiente?: string | null;
}

export interface CoincidenciaFila {
  origen: "Cliente" | "Persona" | "Prospecto";
  id: number;
  numero?: string | null;
  nombre: string;
  fechaNacimiento?: string | null;
  identificaciones: string;
  roles: string;
  estado: string;
  porcentaje: number;
  motivos: string[];
  fuerte: boolean;
}

export interface CoincidenciasResultado {
  items: CoincidenciaFila[];
  umbralAlerta: number;
  umbralFuerte: number;
  fuerteBloquea: boolean;
}

export interface RevisionDuplicados {
  confirmado: boolean;
  motivo?: string | null;
}

// ----- Mensajes del sistema (Administracion) -----

export interface MensajeSistemaResponse {
  idMensaje: number;
  codigo: string;
  nombre: string;
  severidad: "ERROR" | "WARNING" | "INFORMATIVO";
  modulo: string;
  resultado: "CLIENTE_CREADO" | "GESTION_PENDIENTE" | "RECHAZADO";
  httpEstado: number;
  texto: string;
  textoDefecto: string;
  descripcion: string | null;
  modificado: boolean;
  modificadoPor: string | null;
  modificadoEn: string | null;
}

// ----- Fusion de clientes (Modelos/FusionModelos.cs) -----

export interface FusionDireccionPlan {
  idDireccion: number;
  idTipoDireccionOriginal: number;
  idTipoDireccionSugerido: number;
  conflicto: boolean;
  incorporar: boolean;
}

export interface FusionPlan {
  identificaciones: { idIdentificacion: number; accion: string }[];
  direcciones: FusionDireccionPlan[];
  contactos: { idContacto: number; accion: "TRASLADAR" | "DESCARTAR_REPETIDO"; seraPrincipal: boolean }[];
  roles: { idTipoRol: number; nombre: string; accion: "TRASLADAR" | "YA_EXISTE" | "REACTIVAR" }[];
  otros: { concepto: string; cantidad: number }[];
}

export interface FusionVistaPreviaResponse {
  sobreviviente: InterlocutorDetalleResponse;
  duplicado: InterlocutorDetalleResponse;
  plan: FusionPlan;
  impedimentos: string[];
  advertencias: string[];
}

export interface FusionDireccionDecision {
  idDireccion: number;
  incorporar: boolean;
  idTipoDireccion: number | null;
}

// ----- Roles del interlocutor (Ronda 20) -----
/** Campos que exige un rol (códigos de ReglasCampos del backend). */
export interface RolRequisitos { idTipoRol: number; codigo: string; nombre: string; requeridos: string[] }
export interface RolRequisitoCampo { codigo: string; seccion: string; etiqueta: string; aplica?: string }
export interface RolRequisitosAdmin { campos: RolRequisitoCampo[]; roles: RolRequisitos[]; tipoPersona?: string }
export interface RolCliente { idTipoRol: number; codigo: string; nombre: string; estado: string; fechaAsignacion: string; detalle?: string | null }
export interface RolDisponible { idTipoRol: number; codigo: string; nombre: string; faltantes: string[] }
export interface RolesClienteResponse {
  idInterlocutor: number; numero: string; estado: string;
  roles: RolCliente[]; disponibles: RolDisponible[]; numeroGestionPendiente: string | null; historial: RolPeriodo[]; tipoPersona?: string;
}
export interface RolIdentificacion {
  estado: "NUEVO" | "PUEDE_AGREGAR" | "FALTAN_DATOS" | "YA_TIENE_ROL" | "GESTION_PENDIENTE" | "ALTA_PENDIENTE" | "FUSIONADO" | "NO_DISPONIBLE";
  mensaje: string;
  idInterlocutor?: number | null;
  idSobreviviente?: number | null;
  faltantes: string[];
  cliente?: InterlocutorDetalleResponse | null;
  puedeSolicitar: boolean;
}
export interface EmpleadoDatos { idUnidad: number; numeroEmpleado: string }
export interface RolSolicitudRequest { agregar: number[]; quitar: number[]; motivo: string; empleado?: EmpleadoDatos | null }

// ----- Relaciones entre interlocutores (fase 2) -----
export interface RelacionTipoResponse {
  idTipoRelacion: number; codigo: string; nombre: string; etiquetaInversa: string;
  usaPorcentajes: boolean; esSimetrica: boolean; tiposOrigen: string[]; tiposDestino: string[];
}
export interface RelacionDetalleResponse {
  idRelacion: number; idOtro: number; numeroOtro: string; nombreOtro: string; tipoOtro: string;
  idTipoRelacion: number; etiqueta: string; sentido: "Origen" | "Destino";
  porcentajeParticipacion?: number | null; porcentajeVoto?: number | null; porcentajeControl?: number | null;
  esPrincipal: boolean; funcion?: string | null; fechaInicio: string; fechaFin?: string | null;
  creadoPor?: string | null; cerradoPor?: string | null;
}
export interface RelacionesInterlocutorResponse {
  idInterlocutor: number; numero: string; estado: string; tipoPersona: string;
  vigentes: RelacionDetalleResponse[]; historial: RelacionDetalleResponse[];
  numeroGestionPendiente?: string | null; puedeSolicitar: boolean;
}
export interface RelacionNuevaDto {
  idOtro: number; idTipoRelacion: number; sentido: "Origen" | "Destino";
  porcentajeParticipacion?: number | null; porcentajeVoto?: number | null; porcentajeControl?: number | null;
  esPrincipal: boolean; funcion?: string | null; fechaInicio?: string | null;
}

// KYC y cumplimiento (Modelos/CumplimientoModelos.cs).

export interface KycResponse {
  idEstadoKyc?: number | null; estadoKyc?: string | null; codigoEstadoKyc?: string | null;
  idNivelDebidaDiligencia?: number | null; nivelDebidaDiligencia?: string | null;
  idClasificacionRiesgo?: number | null; clasificacionRiesgo?: string | null; codigoRiesgo?: string | null;
  fechaUltimaRevision?: string | null; fechaProximaRevision?: string | null; revisionVencida: boolean;
  idSegmentoCliente?: number | null; segmentoCliente?: string | null;
  idSegmentoPricing?: number | null; segmentoPricing?: string | null;
  idSegmentoRiesgo?: number | null; segmentoRiesgo?: string | null;
}
export interface MotivoRiesgoResponse { id: number; idMotivo: number; motivo: string }
export interface ExpedienteResponse { id: number; referencia: string; sistema?: string | null; descripcion?: string | null }
export interface EvaluacionResponse {
  id: number; tipo: "PEP" | "Sanciones"; idResultado: number; resultado: string; codigoResultado: string;
  fecha: string; fuente: string; observacion?: string | null; vigente: boolean; creadoPor: string; creadoEn: string;
}
export interface CumplimientoResponse {
  idInterlocutor: number; tipoPersona: string; kyc?: KycResponse | null;
  motivosRiesgo: MotivoRiesgoResponse[]; expedientes: ExpedienteResponse[]; evaluaciones: EvaluacionResponse[];
  vigencia?: KycVigenciaResponse | null;
}

// Bloqueo de clientes (Modelos/BloqueoModelos.cs).

export interface BloqueoVigenteResponse { idTipoBloqueo: number; codigoTipo: string; tipo: string; desde: string }
export interface BloqueoResponse {
  id: number; idTipoBloqueo: number; codigoTipo: string; tipo: string; comentario: string; estado: "Vigente" | "Levantado";
  fechaBloqueo: string; bloqueadoPor: string; idGestionBloqueo?: number | null;
  fechaLevantamiento?: string | null; comentarioLevantamiento?: string | null; levantadoPor?: string | null; idGestionLevantamiento?: number | null;
}
export interface BloqueoPendienteResponse { idGestion: number; numero: string; accion: "Bloquear" | "Desbloquear"; idTipoBloqueo: number; tipo: string; solicitadoPor: string }
export interface BloqueosClienteResponse {
  idInterlocutor: number; numero: string; nombre: string; tipoPersona: string; estado: string; bloqueado: boolean;
  bloqueos: BloqueoResponse[]; pendientes: BloqueoPendienteResponse[];
}
export interface BloqueoListadoItemResponse {
  idBloqueo: number; idInterlocutor: number; numero: string; nombre: string; tipoPersona: string; idTipoBloqueo: number; tipo: string;
  comentario: string; fechaBloqueo: string; bloqueadoPor: string;
}

// Vigencia del KYC (calculada por fechas, Modelos/CumplimientoModelos.cs).
export interface KycVigenciaResponse {
  situacion: "Vigente" | "Por vencer" | "Vencido" | "Sin KYC";
  proximaRevision?: string | null; diasParaRevision?: number | null; fechaBloqueoPrevista?: string | null;
  permiteApertura: boolean; mensaje?: string | null;
}

// ----- Restricciones por tipo de bloqueo -----
export interface AccionRestringible { id: number; codigo: string; nombre: string; descripcion: string | null }
export interface TipoBloqueoRestriccion { idTipoBloqueo: number; codigo: string; nombre: string; idsAcciones: number[]; descripcion: string | null }
export interface MatrizRestriccion { acciones: AccionRestringible[]; tipos: TipoBloqueoRestriccion[] }
export interface RestriccionClienteItem { codigo: string; accion: string; permitida: boolean; motivo: string | null }
export interface RestriccionesCliente { idInterlocutor: number; bloqueado: boolean; acciones: RestriccionClienteItem[] }

// ----- Consentimientos del cliente -----
export interface ConsentimientoItem {
  id: number; idTipoConsentimiento: number; tipo: string; estado: "Vigente" | "Revocado"; fechaOtorgamiento: string; canal: string; comentario: string | null; otorgadoPor: string;
  idGestionOtorgamiento: number | null; fechaRevocacion: string | null; canalRevocacion: string | null; comentarioRevocacion: string | null; revocadoPor: string | null; idGestionRevocacion: number | null;
}
export interface ConsentimientosCliente {
  idInterlocutor: number; numero: string; nombre: string; tipoPersona: string; estado: string;
  consentimientos: ConsentimientoItem[]; pendiente: { idGestion: number; numero: string; solicitadoPor: string } | null;
}

// ----- Gestor documental (Modelos/DocumentoModelos.cs) -----
export interface TipoDocumento {
  id: number; codigo: string; nombre: string; idCarpeta: number; carpeta: string; cardinalidad: "Unico" | "Multiple"; maximo: number | null;
  vencimiento: "Obligatorio" | "Opcional" | "NoAplica"; diasAviso: number; confidencialidad: "Normal" | "Restringida"; requiereAprobacion: boolean;
  carpetaCodigo: string; carpetaSistema: boolean;
}
export interface DocumentoApi {
  idDocumento: number; idTipoDocumento: number; tipoNombre: string; descripcion: string | null; estado: string; origen: string;
  idGestion: number | null; numeroGestion: string | null; nombreArchivo: string; tamano: number; subidoEn: string; subidoPor: string; version: number;
  fechaEmision: string | null; fechaVencimiento: string | null; estadoVigencia: "SinVencimiento" | "Vigente" | "PorVencer" | "Vencido"; diasParaVencer: number | null;
  claves: string[]; restringido: boolean; motivoObsoleto: string | null; fusionadoDesde: string | null;
}
export interface SubcarpetaGestion { idGestion: number; numero: string; nombre: string; documentos: DocumentoApi[] }
export interface CarpetaDocumentos { idCarpeta: number; codigo: string; nombre: string; esSistema: boolean; documentos: DocumentoApi[]; subcarpetas: SubcarpetaGestion[] }
export interface ArbolDocumentos { carpetas: CarpetaDocumentos[] }

export interface ClienteDocumento { id: number; numero: string; nombre: string; tipoPersona: string; estado: string }
export interface DocumentoBusqueda { documento: DocumentoApi; idInterlocutor: number; numeroCliente: string; nombreCliente: string; carpeta: string }
export interface DocumentoVersion {
  idVersion: number; numero: number; esVigente: boolean; nombreArchivo: string; tipoMime: string; tamano: number;
  fechaEmision: string | null; fechaVencimiento: string | null; motivoReemplazo: string | null; subidoPor: string; subidoEn: string; numeroGestion: string | null;
}
export interface DocumentoOperacion { tipo: "Alta" | "Reemplazo" | "Obsoleto"; idDocumento?: number; idDocumentoDestino?: number; motivo?: string }
export interface SolicitudDocumentos { idGestion: number; numero: string; aplicada: boolean }
export interface LimitesDocumentos { tamanoMaxMb: number; extensiones: string[]; maxArchivos: number }

// ----- Administracion del Gestor documental (r44c) ---------------------------------------------------------
export interface CarpetaAdmin {
  idCarpeta: number; codigo: string; nombre: string; descripcion: string | null; orden: number; esSistema: boolean;
  aplicaFisica: boolean; aplicaJuridica: boolean; aplicaGrupo: boolean; estado: string; cantidadTipos: number;
}
export interface TipoDocumentoAdmin {
  idTipoDocumento: number; codigo: string; nombre: string; descripcion: string | null; idCarpeta: number; carpeta: string; orden: number;
  cardinalidad: "Unico" | "Multiple"; maximo: number | null; vencimiento: "Obligatorio" | "Opcional" | "NoAplica"; diasAviso: number;
  confidencialidad: "Normal" | "Restringida"; aplicaFisica: boolean; aplicaJuridica: boolean; aplicaGrupo: boolean; estado: string;
  documentosVigentes: number; rolesQueLoExigen: number;
}
export interface RolDocumentos { idTipoRol: number; codigo: string; nombre: string; idsTipoDocumento: number[] }
export interface RolDocumentosAdmin {
  tipoPersona: "Fisica" | "Juridica";
  tipos: { idTipoDocumento: number; nombre: string; carpeta: string }[];
  roles: RolDocumentos[];
}
export interface DocumentoRequerido {
  idTipoDocumento: number; tipo: string; carpeta: string; roles: string[]; estado: "Cumple" | "PorVencer" | "Vencido" | "Falta"; vence: string | null;
}


// ----- Perfiles de datos y enmascaramiento ------------------------------------------------------------------
export interface MascaraCampoInfo { campo: string; etiqueta: string; puedeRevelar: boolean }
/** textos: lo que se muestra en lugar de fechas y montos enmascarados (claves: fechaNacimiento, eco.gastos, ing.{id}, tra.{id}.min...). */
export interface MascarasInfoResponse { campos: MascaraCampoInfo[]; textos: Record<string, string> }
export interface RevelarResponse { campo: string; idItem?: number | null; valores: Record<string, string | null> }

export interface MascaraAdmin {
  idMascara: number; codigo: string; nombre: string; descripcion?: string | null; tipo: string;
  visiblesInicio: number; visiblesFin: number; caracter: string; esSistema: boolean; estado: string; ejemplo: string; perfilesQueLaUsan: number;
}
export interface MascaraGuardar { codigo: string; nombre: string; descripcion: string | null; tipo: string; visiblesInicio: number; visiblesFin: number; caracter: string }
export interface PerfilFiltro { atributo: string; modo: "INCLUIR" | "EXCLUIR"; valores: string[] }
export interface PerfilCampo { campo: string; idMascara: number; permiteRevelar: boolean }
export interface PerfilDatosResumen { idPerfilDatos: number; codigo: string; nombre: string; descripcion?: string | null; estado: string; filas: string; campos: number; usuarios: number }
export interface PerfilUsuario { idUsuario: number; codigo: string; nombre: string }
export interface PerfilDatosDetalle {
  idPerfilDatos: number; codigo: string; nombre: string; descripcion?: string | null; estado: string;
  filtros: PerfilFiltro[]; campos: PerfilCampo[]; usuarios: PerfilUsuario[];
}
export interface PerfilDatosGuardar { codigo: string; nombre: string; descripcion: string | null; filtros: PerfilFiltro[]; campos: PerfilCampo[] }
export interface OpcionValor { valor: string; nombre: string }
export interface CatalogoAtributo { codigo: string; etiqueta: string; valores: OpcionValor[] }
export interface CatalogoCampo { codigo: string; etiqueta: string; descripcion: string; tipo: string; tiposMascara: string[] }
export interface PerfilCatalogo { atributos: CatalogoAtributo[]; campos: CatalogoCampo[]; mascaras: MascaraAdmin[] }
export interface UsuarioPerfiles { idUsuario: number; codigo: string; nombre: string; tipo: string; estado: string; perfiles: number[] }
