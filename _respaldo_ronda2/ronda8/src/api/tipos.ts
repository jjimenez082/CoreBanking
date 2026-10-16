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

export interface InterlocutorAltaRequest {
  idPaisRegulador: number;
  idUnidad?: number | null;
  nombre1: string;
  nombre2?: string | null;
  apellido1: string;
  apellido2?: string | null;
  fechaNacimiento: string; // "YYYY-MM-DD"
  identificaciones: IdentificacionDto[];
  direcciones: DireccionDto[];
  contactos: ContactoDto[];
  idBorrador?: number | null;
}

export interface InterlocutorModificacionRequest {
  idPaisRegulador: number;
  idUnidad?: number | null;
  nombre1: string;
  nombre2?: string | null;
  apellido1: string;
  apellido2?: string | null;
  fechaNacimiento: string;
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
}

// Catalogos para combo boxes (Modelos/CatalogosModelos.cs).

export interface CatalogoItemResponse {
  id: number;
  codigo: string;
  nombre: string;
  esPredeterminado: boolean;
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
  codigosRol: string[];
  creadoEn: string;
  creadoPor: string;
  modificadoEn?: string | null;
  modificadoPor?: string | null;
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

export interface IntegracionFila {
  idUsuarioIntegracion: number;
  clientId: string;
  nombre: string;
  estado: string;
  idCanal?: number | null;
  canalNombre?: string | null;
}
