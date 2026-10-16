import type {
  RevisionDuplicados,
  GestionCampoResponse,
  CatalogoItemResponse,
  UnidadItem,
  ContactoDto,
  DireccionConfig,
  DireccionDto,
  IdentificacionDto,
  InterlocutorAltaRequest,
  InterlocutorDetalleResponse,
  InterlocutorModificacionRequest,
  OrganizacionDto,
  PerfilFinancieroDto,
} from "../api/tipos";

/**
 * Estado del wizard de cliente (alta y edicion). Tiene la misma forma que el
 * request del API, mas `clave` (identidad local de cada fila) y algunos textos
 * de solo lectura. El borrador guarda este mismo estado como JSON, por eso los
 * nombres de campo coinciden con los del API.
 */

export interface FilaIdent extends IdentificacionDto {
  clave: string;
  /** Texto de auditoria (creado/modificado por) de una fila ya existente. */
  auditoria?: string;
  tipoNombre?: string;
  institucionNombre?: string;
}

/** Nivel de una direccion en el formulario. `nombre` = texto visible de una division elegida (solo lectura). */
export interface NivelFila {
  id: number | null;
  texto: string;
  nombre?: string;
}

export interface FilaDir extends DireccionDto {
  clave: string;
  niveles: NivelFila[];
  codigoPostal: string;
  auditoria?: string;
  tipoNombre?: string;
  paisNombre?: string;
}

export const NUM_NIVELES = 4;
export const nivelesVacios = (): NivelFila[] => Array.from({ length: NUM_NIVELES }, () => ({ id: null, texto: "" }));

export type ConfigsDireccion = Record<number, DireccionConfig>;

export interface FilaCont extends ContactoDto {
  clave: string;
  auditoria?: string;
  tipoNombre?: string;
}

export interface FilaNom { clave: string; id?: number | null; nombre: string }
export interface FilaNac { clave: string; id?: number | null; idPais: number }
export interface FilaAct {
  clave: string; id?: number | null; idActividad: number; descripcion: string; esPrincipal: boolean; fechaInicio: string; actividadNombre?: string;
}

// ----- Perfil financiero (montos como texto mientras se edita) -----
export interface FilaRF {
  clave: string; id?: number | null; idPais: number; identificadorTributario: string; esPrincipal: boolean; fechaDesde: string; fechaHasta: string; paisNombre?: string;
}
export interface FilaIng {
  clave: string; id?: number | null; idTipoIngreso: number; monto: string; idMoneda: number; idPeriodicidad: number;
  tipoNombre?: string; periodicidadNombre?: string; monedaCodigo?: string;
}
export interface FilaOrig { clave: string; id?: number | null; clase: "Fondos" | "Patrimonio"; idOrigen: number; origenNombre?: string }
export interface FilaPT {
  clave: string; id?: number | null; idTipoTransaccion: number; idFrecuencia: number; montoMinimo: string; montoMaximo: string; idMoneda: number;
  idPaisOrigen: number; idPaisDestino: number; tipoNombre?: string; frecuenciaNombre?: string; monedaCodigo?: string;
}
export interface FilaCx {
  clave: string; id?: number | null; idEntidad: number; numeroCuenta: string; idTipoCuenta: number; idMoneda: number; entidadNombre?: string; tipoCuentaNombre?: string; monedaCodigo?: string;
}
export interface PerfilEco {
  idCondicionLaboral: number; idOcupacion: number; empleador: string; gastosEstimados: string;
  activosDeclarados: string; pasivosDeclarados: string; patrimonioDeclarado: string;
  idMonedaBalance: number; fechaCorte: string;
}
export interface PerfilForm {
  economico: PerfilEco;
  residenciasFiscales: FilaRF[];
  ingresos: FilaIng[];
  origenes: FilaOrig[];
  perfilTransaccional: FilaPT[];
  cuentasExternas: FilaCx[];
}

export const ecoVacio = (): PerfilEco => ({
  idCondicionLaboral: 0, idOcupacion: 0, empleador: "", gastosEstimados: "", activosDeclarados: "", pasivosDeclarados: "", patrimonioDeclarado: "",
  idMonedaBalance: 0, fechaCorte: "",
});
export const perfilVacio = (): PerfilForm => ({ economico: ecoVacio(), residenciasFiscales: [], ingresos: [], origenes: [], perfilTransaccional: [], cuentasExternas: [] });

export type TipoPersona = "Fisica" | "Juridica";

export interface FormCliente {
  /** Fisica = Persona, Juridica = Organización. No cambia después de creado. */
  tipoPersona: TipoPersona;
  // --- Organización (solo Juridica) ---
  razonSocial: string;
  nombresComerciales: FilaNom[];
  idFormaJuridica: number;
  idPaisConstitucion: number;
  fechaConstitucion: string;
  referenciaRegistral: string;
  idEstadoLegal: number;
  fechaDisolucion: string;
  actividades: FilaAct[];
  idPaisRegulador: number;
  /** Unidad (sucursal) a la que pertenece el cliente; 0 = sin elegir. */
  idUnidad: number;
  unidadNombre?: string;
  nombre1: string;
  nombre2: string;
  apellido1: string;
  apellido2: string;
  fechaNacimiento: string;
  // --- Identidad de la persona (opcionales salvo lo que exija un rol) ---
  nombreConocido: string;
  idPaisNacimiento: number;
  idSexo: number;
  idEstadoCivil: number;
  nacionalidades: FilaNac[];
  identificaciones: FilaIdent[];
  direcciones: FilaDir[];
  contactos: FilaCont[];
  /** Rol con el que se da de alta a la persona (id de TIPO_ROL_INTERLOCUTOR). Vacio/0 = Cliente. */
  rolId?: number;
  /** Solo si el rol del alta es Empleado: unidad (sucursal o departamento) y número de empleado. */
  empleado?: { idUnidad: number; numero: string };
  /** Perfil financiero: residencia fiscal, perfil economico, ingresos, origenes y perfil transaccional. */
  perfil: PerfilForm;
  /**
   * Edicion con datos enmascarados por el perfil de datos del usuario: clave de fila -> valor enmascarado recibido. Mientras el valor no se
   * cambie no se valida su formato (el servidor conserva el dato real). Etiquetas = campos enmascarados, para avisar al usuario.
   */
  protegidos?: Record<string, string>;
  fechaNacimientoProtegida?: boolean;
  /** Etiquetas y códigos de los campos enmascarados de la ficha. */
  enmascarados?: string[];
  codigosEnmascarados?: string[];
}

/** Campos obligatorios segun los roles de la persona (códigos de ReglasCampos del backend). */
export type Reglas = ReadonlySet<string>;

/** Lo que se exigía antes de parametrizar por rol: regla de respaldo mientras cargan los requisitos y para los prospectos. */
export const REGLAS_BASE: Reglas = new Set(["PERSONA_FECHA_NACIMIENTO", "IDENT_INSTITUCION", "IDENT_FECHA_EMISION", "DIR_NIVELES", "DIR_CODIGO_POSTAL"]);

/** Lo que se exigía en organizaciones antes de parametrizar. */
export const REGLAS_BASE_ORG: Reglas = new Set(["IDENT_INSTITUCION", "DIR_NIVELES", "DIR_CODIGO_POSTAL", "ORG_ACTIVIDAD"]);

export const ETIQUETA_REQUISITO: Record<string, string> = {
  ORG_NOMBRE_COMERCIAL: "nombre comercial", ORG_FECHA_CONSTITUCION: "fecha de constitución",
  ORG_REFERENCIA_REGISTRAL: "referencia registral", ORG_ACTIVIDAD: "actividad económica",
  PERSONA_NOMBRE2: "segundo nombre", PERSONA_APELLIDO2: "segundo apellido", PERSONA_FECHA_NACIMIENTO: "fecha de nacimiento",
  PERSONA_NOMBRE_CONOCIDO: "nombre conocido", PERSONA_PAIS_NACIMIENTO: "país de nacimiento", PERSONA_NACIONALIDAD: "nacionalidad",
  PERSONA_SEXO: "sexo", PERSONA_ESTADO_CIVIL: "estado civil",
  IDENT_INSTITUCION: "institución emisora", IDENT_FECHA_EMISION: "fecha de emisión", IDENT_FECHA_VENCIMIENTO: "fecha de vencimiento",
  DIRECCION: "al menos una dirección", DIR_NIVELES: "división territorial", DIR_CODIGO_POSTAL: "código postal",
  DIR_LINEA2: "dirección (línea 2)", DIR_LINEA3: "dirección (línea 3)", CORREO: "correo electrónico", TELEFONO: "teléfono",
  RF_RESIDENCIA: "residencia fiscal", RF_IDENT_TRIBUTARIO: "identificador tributario", PF_CONDICION_LABORAL: "condición laboral",
  PF_OCUPACION: "ocupación", PF_EMPLEADOR: "empleador o negocio", PF_INGRESO: "ingresos", PF_GASTOS: "gastos estimados",
  PF_ACTIVOS: "activos declarados", PF_PASIVOS: "pasivos declarados", PF_PATRIMONIO: "patrimonio declarado",
  PF_ORIGEN_FONDOS: "origen de fondos", PF_ORIGEN_PATRIMONIO: "origen del patrimonio", PT_PERFIL: "perfil transaccional", CX_CUENTA_EXTERNA: "cuenta externa (IBAN)",
  REL_REPRESENTANTE_LEGAL: "representante legal (relación)", REL_BENEFICIARIO_FINAL: "beneficiario final (relación)",
};

/** Unión de lo que exigen los roles indicados (una persona con varios roles cumple todos). Sin roles cargados: reglas base. */
export function reglasDeRoles(roles: { idTipoRol: number; requeridos: string[] }[], ids: number[], tipo: TipoPersona = "Fisica"): Reglas {
  const elegidos = roles.filter((x) => ids.includes(x.idTipoRol));
  if (elegidos.length === 0) return tipo === "Juridica" ? REGLAS_BASE_ORG : REGLAS_BASE;
  return new Set(elegidos.flatMap((x) => x.requeridos));
}

export interface CatalogosCliente {
  tiposIdentificacion: CatalogoItemResponse[];
  instituciones: CatalogoItemResponse[];
  tiposDireccion: CatalogoItemResponse[];
  tiposContacto: CatalogoItemResponse[];
  paises: CatalogoItemResponse[];
  /** Lista PAIS: paises para direcciones. */
  paisesDireccion: CatalogoItemResponse[];
  /** Unidades organizativas activas (sucursales...). */
  unidades: UnidadItem[];
  /** Listas propias de organizaciones. */
  tiposIdentificacionOrg: CatalogoItemResponse[];
  formasJuridicas: CatalogoItemResponse[];
  estadosLegales: CatalogoItemResponse[];
  actividades: CatalogoItemResponse[];
  /** Listas de identidad de la persona (SEXO, ESTADO_CIVIL). */
  sexos: CatalogoItemResponse[];
  estadosCiviles: CatalogoItemResponse[];
  /** Listas del perfil financiero. */
  condicionesLaborales: CatalogoItemResponse[];
  ocupaciones: CatalogoItemResponse[];
  tiposIngreso: CatalogoItemResponse[];
  periodicidades: CatalogoItemResponse[];
  origenesFondos: CatalogoItemResponse[];
  origenesPatrimonio: CatalogoItemResponse[];
  tiposTransaccion: CatalogoItemResponse[];
  frecuencias: CatalogoItemResponse[];
  tiposCuentaExterna: CatalogoItemResponse[];
  entidadesFinancieras: CatalogoItemResponse[];
  /** Monedas activas de la compañía (esBase = moneda base). */
  monedas: MonedaOpcion[];
}

export interface MonedaOpcion { id: number; codigo: string; nombre: string; esBase: boolean }

/** Moneda por defecto para filas nuevas: la base (o la primera). */
export const monedaPorDefecto = (cat: CatalogosCliente): number => (cat.monedas.find((m) => m.esBase) ?? cat.monedas[0])?.id ?? 0;

/** Tipos de identificación que corresponden al tipo de interlocutor. */
export const tiposIdentificacionDe = (cat: CatalogosCliente, tipo: TipoPersona) =>
  tipo === "Juridica" ? cat.tiposIdentificacionOrg : cat.tiposIdentificacion;

let secuencia = 0;
export function nuevaClave(): string {
  secuencia += 1;
  return `k${Date.now()}-${secuencia}`;
}

export function formVacio(tipo: TipoPersona = "Fisica"): FormCliente {
  return {
    tipoPersona: tipo,
    razonSocial: "", nombresComerciales: [], idFormaJuridica: 0, idPaisConstitucion: 0, fechaConstitucion: "",
    referenciaRegistral: "", idEstadoLegal: 0, fechaDisolucion: "", actividades: [],
    idPaisRegulador: 0,
    idUnidad: 0,
    nombre1: "",
    nombre2: "",
    apellido1: "",
    apellido2: "",
    fechaNacimiento: "",
    nombreConocido: "", idPaisNacimiento: 0, idSexo: 0, idEstadoCivil: 0, nacionalidades: [],
    identificaciones: [],
    direcciones: [],
    contactos: [],
    perfil: perfilVacio(),
  };
}

export const rfVacia = (esPrincipal: boolean): FilaRF => ({ clave: nuevaClave(), id: null, idPais: 0, identificadorTributario: "", esPrincipal, fechaDesde: "", fechaHasta: "" });
export const ingVacio = (idMoneda: number): FilaIng => ({ clave: nuevaClave(), id: null, idTipoIngreso: 0, monto: "", idMoneda, idPeriodicidad: 0 });
/** IBAN sin espacios ni guiones y en mayúsculas. */
export const normalizarIban = (v: string) => v.replace(/[\s-]/g, "").toUpperCase();
const LONGITUD_IBAN: Record<string, number> = { CR: 22, ES: 24, DE: 22, FR: 27, GB: 22, IT: 27, PT: 25, NL: 18, BE: 16, CH: 21, DO: 28, GT: 28, SV: 28, BR: 29, IE: 22, AT: 20 };
/** Devuelve el motivo si el IBAN no es válido (formato, longitud del país y dígitos de control mod 97), o null. */
export function errorIban(valor: string): string | null {
  const iban = normalizarIban(valor);
  if (!/^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$/.test(iban)) return "IBAN no válido (ej.: CR05015202001026284066)";
  const largo = LONGITUD_IBAN[iban.slice(0, 2)];
  if (largo && iban.length !== largo) return `El IBAN de ${iban.slice(0, 2)} debe tener ${largo} caracteres`;
  if (iban.length > 34) return "Máximo 34 caracteres";
  let resto = 0;
  for (const ch of iban.slice(4) + iban.slice(0, 4)) {
    const n = /[0-9]/.test(ch) ? ch : String(ch.charCodeAt(0) - 55);
    for (const d of n) resto = (resto * 10 + Number(d)) % 97;
  }
  return resto === 1 ? null : "Dígitos de control del IBAN incorrectos";
}
export const cxVacia = (): FilaCx => ({ clave: nuevaClave(), id: null, idEntidad: 0, numeroCuenta: "", idTipoCuenta: 0, idMoneda: 0 });
export const ptVacio = (idMoneda: number): FilaPT => ({ clave: nuevaClave(), id: null, idTipoTransaccion: 0, idFrecuencia: 0, montoMinimo: "", montoMaximo: "", idMoneda, idPaisOrigen: 0, idPaisDestino: 0 });

export function nacVacia(): FilaNac { return { clave: nuevaClave(), id: null, idPais: 0 }; }

export function identVacia(esPrincipal: boolean): FilaIdent {
  return {
    clave: nuevaClave(), id: null, idTipoIdentificacion: 0, numero: "",
    idInstitucionEmisora: null, fechaEmision: "", fechaVencimiento: "", esPrincipal,
  };
}

export function dirVacia(esPrincipal: boolean, idTipoDireccion: number, idPais = 0): FilaDir {
  return { clave: nuevaClave(), id: null, idTipoDireccion, idPais, niveles: nivelesVacios(), codigoPostal: "", detalle: "", detalle2: "", detalle3: "", esPrincipal };
}

export function nomVacio(): FilaNom { return { clave: nuevaClave(), id: null, nombre: "" }; }
export function actVacia(esPrincipal: boolean): FilaAct {
  return { clave: nuevaClave(), id: null, idActividad: 0, descripcion: "", esPrincipal, fechaInicio: "" };
}

export function contVacio(idTipoContacto: number, esPrincipal: boolean): FilaCont {
  return { clave: nuevaClave(), id: null, idTipoContacto, valor: "", esPrincipal };
}

// ----- Validacion de formato (misma regla que el servidor) -------------------------

const CORREO = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

/** Devuelve el mensaje de error o null si el dato de contacto es valido para ese tipo. */
export function validarContacto(codigoTipo: string | undefined, valor: string): string | null {
  const v = valor.trim();
  if (!v) return "Ingrese el dato de contacto o quite esta fila.";
  if (codigoTipo === "CORREO") {
    return CORREO.test(v) ? null : "Correo con formato inválido (ej. nombre@dominio.com).";
  }
  if (codigoTipo === "TELEFONO_MOVIL" || codigoTipo === "TELEFONO_FIJO" || codigoTipo === "WHATSAPP") {
    let digitos = v.replace(/\D/g, "");
    if (digitos.length === 11 && digitos.startsWith("506")) digitos = digitos.slice(3);
    return digitos.length === 8 ? null : "Número inválido: debe tener 8 dígitos (con o sin +506).";
  }
  return null;
}

export function ejemploContacto(codigoTipo: string | undefined): string {
  switch (codigoTipo) {
    case "CORREO": return "nombre@dominio.com";
    case "WHATSAPP": return "+506 8888-1234";
    case "TELEFONO_FIJO": return "2222-3333";
    default: return "8888-1234";
  }
}

export function codigoTipoContacto(cat: CatalogosCliente, idTipo: number, respaldo?: string): string | undefined {
  return cat.tiposContacto.find((t) => t.id === idTipo)?.codigo ?? respaldo;
}

export type ErroresPaso = Record<string, string>;

const hoy = () => new Date().toISOString().slice(0, 10);

/**
 * Valida un paso. Las claves del resultado identifican el campo:
 *  - "general": mensaje para toda la pantalla
 *  - "<clave de fila>.<campo>": error de un campo de una fila
 *  - nombre simple (nombre1, ...) en el paso 1
 */
export function validarPaso(
  paso: number, f: FormCliente, cat: CatalogosCliente,
  configs: ConfigsDireccion = {}, sinOpciones: ReadonlySet<string> = new Set(), reglas: Reglas | null = null,
): ErroresPaso {
  const e: ErroresPaso = {};
  const R = reglas ?? (f.tipoPersona === "Juridica" ? REGLAS_BASE_ORG : REGLAS_BASE);
  const pide = (c: string) => R.has(c);

  const esOrg = f.tipoPersona === "Juridica";
  if (paso === 1 && esOrg) {
    if (!f.idPaisRegulador) e.idPaisRegulador = "Obligatorio";
    if (!f.idUnidad && cat.unidades.length > 0) e.idUnidad = "Obligatorio";
    if (!f.razonSocial.trim()) e.razonSocial = "Obligatorio";
    else if (f.razonSocial.length > 200) e.razonSocial = "Máximo 200 caracteres";
    if (!f.idFormaJuridica) e.idFormaJuridica = "Obligatorio";
    if (!f.idPaisConstitucion) e.idPaisConstitucion = "Obligatorio";
    if (!f.idEstadoLegal) e.idEstadoLegal = "Obligatorio";
    if (!f.fechaConstitucion) { if (pide("ORG_FECHA_CONSTITUCION")) e.fechaConstitucion = "Obligatorio"; }
    else if (f.fechaConstitucion > hoy()) e.fechaConstitucion = "No puede ser futura";
    if (pide("ORG_REFERENCIA_REGISTRAL") && !f.referenciaRegistral.trim()) e.referenciaRegistral = "Obligatorio";
    if (f.referenciaRegistral.length > 200) e.referenciaRegistral = "Máximo 200 caracteres";
    const estado = cat.estadosLegales.find((x) => x.id === f.idEstadoLegal)?.codigo;
    if (f.fechaDisolucion) {
      if (estado !== "DISUELTA") e.fechaDisolucion = "Solo aplica si el estado legal es Disuelta";
      else if (f.fechaConstitucion && f.fechaDisolucion < f.fechaConstitucion) e.fechaDisolucion = "No puede ser anterior a la constitución";
    }
    const noms = f.nombresComerciales.filter((n) => n.nombre.trim());
    if (pide("ORG_NOMBRE_COMERCIAL") && noms.length === 0) e.nombresComerciales = "Debe registrar al menos un nombre comercial.";
    const vistosN = new Set<string>();
    for (const n of f.nombresComerciales) {
      const t = n.nombre.trim();
      if (!t) { e[`${n.clave}.nombre`] = "Ingrese el nombre o quite la fila"; continue; }
      if (t.length > 200) e[`${n.clave}.nombre`] = "Máximo 200 caracteres";
      if (vistosN.has(t.toUpperCase())) e[`${n.clave}.nombre`] = "Repetido";
      vistosN.add(t.toUpperCase());
    }
    if (pide("ORG_ACTIVIDAD") && f.actividades.length === 0) e.actividades = "Debe registrar al menos una actividad económica.";
    if (f.actividades.length > 0 && f.actividades.filter((a) => a.esPrincipal).length !== 1) e.actividades = "Marque exactamente una actividad como principal.";
    const vistasA = new Set<number>();
    for (const a of f.actividades) {
      if (!a.idActividad) e[`${a.clave}.actividad`] = "Obligatorio";
      else if (vistasA.has(a.idActividad)) e[`${a.clave}.actividad`] = "Repetida";
      vistasA.add(a.idActividad);
      if (a.descripcion.length > 300) e[`${a.clave}.descripcion`] = "Máximo 300 caracteres";
    }
  } else if (paso === 1) {
    if (!f.idPaisRegulador) e.idPaisRegulador = "Obligatorio";
    if (!f.idUnidad && cat.unidades.length > 0) e.idUnidad = "Obligatorio";
    if (f.empleado) {
      if (!f.empleado.idUnidad) e.empUnidad = "Obligatorio";
      if (!f.empleado.numero.trim()) e.empNumero = "Obligatorio";
      else if (!/^[A-Za-z0-9][A-Za-z0-9\-_./]{0,29}$/.test(f.empleado.numero.trim())) e.empNumero = "Letras, números y - _ . / (máximo 30)";
    }
    if (!f.nombre1.trim()) e.nombre1 = "Obligatorio";
    if (!f.apellido1.trim()) e.apellido1 = "Obligatorio";
    if (pide("PERSONA_NOMBRE2") && !f.nombre2.trim()) e.nombre2 = "Obligatorio";
    if (pide("PERSONA_APELLIDO2") && !f.apellido2.trim()) e.apellido2 = "Obligatorio";
    if (!f.fechaNacimiento) { if (pide("PERSONA_FECHA_NACIMIENTO") && !f.fechaNacimientoProtegida) e.fechaNacimiento = "Obligatorio"; }
    else if (f.fechaNacimiento > hoy()) e.fechaNacimiento = "No puede ser futura";
    if (f.nombreConocido.length > 100) e.nombreConocido = "Máximo 100 caracteres";
    else if (pide("PERSONA_NOMBRE_CONOCIDO") && !f.nombreConocido.trim()) e.nombreConocido = "Obligatorio";
    if (pide("PERSONA_PAIS_NACIMIENTO") && !f.idPaisNacimiento) e.idPaisNacimiento = "Obligatorio";
    if (pide("PERSONA_SEXO") && !f.idSexo) e.idSexo = "Obligatorio";
    if (pide("PERSONA_ESTADO_CIVIL") && !f.idEstadoCivil) e.idEstadoCivil = "Obligatorio";
    if (pide("PERSONA_NACIONALIDAD") && f.nacionalidades.length === 0) e.nacionalidades = "Debe registrar al menos una nacionalidad.";
    const vistasN = new Set<number>();
    for (const n of f.nacionalidades) {
      if (!n.idPais) e[`${n.clave}.pais`] = "Obligatorio";
      else if (vistasN.has(n.idPais)) e[`${n.clave}.pais`] = "Repetida";
      vistasN.add(n.idPais);
    }
  }

  if (paso === 2) {
    if (f.identificaciones.length === 0) e.general = "Debe registrar al menos una identificación.";
    const vistos = new Set<string>();
    for (const i of f.identificaciones) {
      if (!i.idTipoIdentificacion) e[`${i.clave}.tipo`] = "Obligatorio";
      if (!i.numero.trim()) e[`${i.clave}.numero`] = "Obligatorio";
      else {
        const llave = `${i.idTipoIdentificacion}|${i.numero.trim().toUpperCase()}`;
        if (vistos.has(llave)) e[`${i.clave}.numero`] = "Repetida (mismo tipo y número)";
        vistos.add(llave);
      }
      if (!i.idInstitucionEmisora && pide("IDENT_INSTITUCION")) e[`${i.clave}.institucion`] = "Obligatorio";
      if (!i.fechaEmision) { if (pide("IDENT_FECHA_EMISION")) e[`${i.clave}.emision`] = "Obligatorio"; }
      else if (i.fechaEmision > hoy()) e[`${i.clave}.emision`] = "No puede ser futura";
      if (!i.fechaVencimiento && pide("IDENT_FECHA_VENCIMIENTO")) e[`${i.clave}.vencimiento`] = "Obligatorio";
      if (i.fechaEmision && i.fechaVencimiento && i.fechaVencimiento <= i.fechaEmision)
        e[`${i.clave}.vencimiento`] = "Debe ser posterior a la emisión";
    }
  }

  if (paso === 3) {
    if (pide("DIRECCION") && f.direcciones.length === 0) e.general = "Debe registrar al menos una dirección.";
    for (const d of f.direcciones) {
      if (!d.idTipoDireccion) e[`${d.clave}.tipo`] = "Obligatorio";
      if (!d.idPais) e[`${d.clave}.pais`] = "Obligatorio";
      else {
        const cfg = configs[d.idPais];
        if (!cfg) e[`${d.clave}.pais`] = "Cargando la estructura del país...";
        else {
          let cadenaOk = true;
          for (const nv of cfg.niveles) {
            const fila = d.niveles[nv.nivel - 1] ?? { id: null, texto: "" };
            const vacio = nv.modo === "Lista" ? !fila.id : !fila.texto.trim();
            if (nv.modo === "Texto" && fila.texto.length > 150) e[`${d.clave}.n${nv.nivel}`] = "Máximo 150 caracteres";
            if (vacio && nv.esObligatorio && pide("DIR_NIVELES") && cadenaOk && !sinOpciones.has(`${d.clave}.${nv.nivel}`))
              e[`${d.clave}.n${nv.nivel}`] = "Obligatorio";
            if (nv.modo === "Lista" && vacio) cadenaOk = false;
          }
          const cp = d.codigoPostal.trim();
          if (cfg.etiquetaCodigoPostal) {
            if (!cp) { if (cfg.codigoPostalObligatorio && pide("DIR_CODIGO_POSTAL")) e[`${d.clave}.cp`] = "Obligatorio"; }
            else if (cfg.patronCodigoPostal) {
              try {
                if (!new RegExp(cfg.patronCodigoPostal).test(cp)) e[`${d.clave}.cp`] = `Formato inválido para ${cfg.paisNombre}`;
              } catch { /* patron no evaluable en el navegador: lo valida el servidor */ }
            }
          }
        }
      }
      if (!d.detalle.trim()) e[`${d.clave}.detalle`] = "Indique la dirección exacta (línea 1) o quite esta dirección";
      else if (d.detalle.length > 500) e[`${d.clave}.detalle`] = "Máximo 500 caracteres";
      if (pide("DIR_LINEA2") && !(d.detalle2 ?? "").trim()) e[`${d.clave}.detalle2`] = "Obligatorio";
      if (pide("DIR_LINEA3") && !(d.detalle3 ?? "").trim()) e[`${d.clave}.detalle3`] = "Obligatorio";
      if ((d.detalle2 ?? "").length > 200) e[`${d.clave}.detalle2`] = "Máximo 200 caracteres";
      if ((d.detalle3 ?? "").length > 200) e[`${d.clave}.detalle3`] = "Máximo 200 caracteres";
    }
  }

  if (paso === 4) {
    {
      const cod = (c: FilaCont) => codigoTipoContacto(cat, c.idTipoContacto);
      const faltan: string[] = [];
      if (pide("CORREO") && !f.contactos.some((c) => cod(c) === "CORREO")) faltan.push("un correo electrónico");
      if (pide("TELEFONO") && !f.contactos.some((c) => ["TELEFONO_MOVIL", "TELEFONO_FIJO", "WHATSAPP"].includes(cod(c) ?? ""))) faltan.push("un teléfono");
      if (faltan.length > 0) e.general = `Debe registrar ${faltan.join(" y ")}.`;
    }
    for (const c of f.contactos) {
      if (!c.idTipoContacto) { e[`${c.clave}.tipo`] = "Obligatorio"; continue; }
      if (f.protegidos?.[c.clave] === c.valor) continue; // dato enmascarado sin tocar: el servidor conserva el real
      const msg = validarContacto(codigoTipoContacto(cat, c.idTipoContacto), c.valor);
      if (msg) e[`${c.clave}.valor`] = msg;
    }
  }

  if (paso === 5) Object.assign(e, validarPerfil(f, pide));

  return e;
}

/** Monto escrito como texto: null si esta vacio, NaN si no es un numero valido. */
const aMonto = (s: string): number | null => (s.trim() === "" ? null : /^\d+([.,]\d{1,2})?$/.test(s.trim()) ? Number(s.trim().replace(",", ".")) : Number.NaN);
const MONTO_MAX = 9_999_999_999_999;

function validarPerfil(f: FormCliente, pide: (c: string) => boolean): ErroresPaso {
  const e: ErroresPaso = {};
  const p = f.perfil;
  const esOrg = f.tipoPersona === "Juridica";
  const eco = p.economico;

  // Residencia fiscal
  if (pide("RF_RESIDENCIA") && p.residenciasFiscales.length === 0) e.residenciasFiscales = "Debe registrar al menos una residencia fiscal.";
  if (p.residenciasFiscales.length > 0 && p.residenciasFiscales.filter((x) => x.esPrincipal).length !== 1) e.residenciasFiscales = "Marque exactamente una residencia fiscal como principal.";
  const vistos = new Set<number>();
  for (const r of p.residenciasFiscales) {
    if (!r.idPais) e[`${r.clave}.pais`] = "Obligatorio";
    else if (vistos.has(r.idPais)) e[`${r.clave}.pais`] = "País repetido";
    vistos.add(r.idPais);
    if (pide("RF_IDENT_TRIBUTARIO") && !r.identificadorTributario.trim()) e[`${r.clave}.ident`] = "Obligatorio";
    if (r.identificadorTributario.length > 50) e[`${r.clave}.ident`] = "Máximo 50 caracteres";
    if (r.fechaDesde && r.fechaHasta && r.fechaHasta < r.fechaDesde) e[`${r.clave}.hasta`] = "No puede ser anterior a «desde»";
  }

  // Perfil economico
  if (!esOrg) {
    if (pide("PF_CONDICION_LABORAL") && !eco.idCondicionLaboral) e["eco.condicion"] = "Obligatorio";
    if (pide("PF_OCUPACION") && !eco.idOcupacion) e["eco.ocupacion"] = "Obligatorio";
    if (pide("PF_EMPLEADOR") && !eco.empleador.trim()) e["eco.empleador"] = "Obligatorio";
    if (eco.empleador.length > 200) e["eco.empleador"] = "Máximo 200 caracteres";
  }
  const montos: [string, string, boolean][] = esOrg
    ? [["eco.activos", eco.activosDeclarados, pide("PF_ACTIVOS")], ["eco.pasivos", eco.pasivosDeclarados, pide("PF_PASIVOS")], ["eco.patrimonio", eco.patrimonioDeclarado, pide("PF_PATRIMONIO")]]
    : [["eco.gastos", eco.gastosEstimados, pide("PF_GASTOS")], ["eco.activos", eco.activosDeclarados, pide("PF_ACTIVOS")], ["eco.pasivos", eco.pasivosDeclarados, pide("PF_PASIVOS")]];
  let hayMonto = false;
  for (const [clave, texto, requerido] of montos) {
    const v = aMonto(texto);
    if (v === null) { if (requerido && !f.codigosEnmascarados?.includes("PF_MONTOS")) e[clave] = "Obligatorio"; continue; }
    hayMonto = true;
    if (Number.isNaN(v)) e[clave] = "Monto inválido (hasta 2 decimales)";
    else if (v > MONTO_MAX) e[clave] = "Monto demasiado grande";
  }
  if (hayMonto && !eco.idMonedaBalance) e["eco.moneda"] = "Indique la moneda de los montos";
  if (eco.fechaCorte && eco.fechaCorte > hoy()) e["eco.corte"] = "No puede ser futura";

  // Ingresos
  if (pide("PF_INGRESO") && p.ingresos.length === 0) e.ingresos = "Debe registrar al menos un ingreso.";
  for (const i of p.ingresos) {
    if (!i.idTipoIngreso) e[`${i.clave}.tipo`] = "Obligatorio";
    const m = aMonto(i.monto);
    if (m === null) e[`${i.clave}.monto`] = "Obligatorio";
    else if (Number.isNaN(m)) e[`${i.clave}.monto`] = "Monto inválido (hasta 2 decimales)";
    else if (m > MONTO_MAX) e[`${i.clave}.monto`] = "Monto demasiado grande";
    if (!i.idMoneda) e[`${i.clave}.moneda`] = "Obligatorio";
    if (!i.idPeriodicidad) e[`${i.clave}.periodicidad`] = "Obligatorio";
  }

  // Origenes
  if (pide("PF_ORIGEN_FONDOS") && !p.origenes.some((o) => o.clase === "Fondos")) e.origenFondos = "Indique al menos un origen de fondos.";
  if (pide("PF_ORIGEN_PATRIMONIO") && !p.origenes.some((o) => o.clase === "Patrimonio")) e.origenPatrimonio = "Indique al menos un origen del patrimonio.";

  // Perfil transaccional
  if (pide("PT_PERFIL") && p.perfilTransaccional.length === 0) e.perfilTransaccional = "Debe registrar al menos una transacción esperada.";
  for (const t of p.perfilTransaccional) {
    if (!t.idTipoTransaccion) e[`${t.clave}.tipo`] = "Obligatorio";
    if (!t.idFrecuencia) e[`${t.clave}.frecuencia`] = "Obligatorio";
    const mi = aMonto(t.montoMinimo);
    const ma = aMonto(t.montoMaximo);
    if (mi !== null && Number.isNaN(mi)) e[`${t.clave}.minimo`] = "Monto inválido (hasta 2 decimales)";
    else if (mi !== null && mi > MONTO_MAX) e[`${t.clave}.minimo`] = "Monto demasiado grande";
    if (ma !== null && Number.isNaN(ma)) e[`${t.clave}.maximo`] = "Monto inválido (hasta 2 decimales)";
    else if (ma !== null && ma > MONTO_MAX) e[`${t.clave}.maximo`] = "Monto demasiado grande";
    if (mi !== null && ma === null) e[`${t.clave}.maximo`] = "Indique también el máximo";
    if (ma !== null && mi === null) e[`${t.clave}.minimo`] = "Indique también el mínimo";
    if (mi !== null && ma !== null && !Number.isNaN(mi) && !Number.isNaN(ma) && ma < mi) e[`${t.clave}.maximo`] = "El máximo no puede ser menor que el mínimo";
    if ((mi !== null || ma !== null) && !t.idMoneda) e[`${t.clave}.moneda`] = "Obligatorio si hay rango";
  }
  // Cuentas externas
  if (pide("CX_CUENTA_EXTERNA") && p.cuentasExternas.length === 0) e.cuentasExternas = "Debe registrar al menos una cuenta externa (IBAN).";
  const vistas = new Set<string>();
  for (const x of p.cuentasExternas) {
    if (!x.idEntidad) e[`${x.clave}.entidad`] = "Obligatorio";
    const errIban = x.numeroCuenta.trim() ? (f.protegidos?.[x.clave] === x.numeroCuenta ? null : errorIban(x.numeroCuenta)) : "Obligatorio";
    if (errIban) e[`${x.clave}.numero`] = errIban;
    if (!x.idTipoCuenta) e[`${x.clave}.tipo`] = "Obligatorio";
    const llave = normalizarIban(x.numeroCuenta);
    if (llave) {
      if (vistas.has(llave)) e[`${x.clave}.numero`] = "Cuenta repetida";
      vistas.add(llave);
    }
  }
  return e;
}

/** Primer paso (1-5) con errores, o null si todo es valido. */
export function primerPasoInvalido(
  f: FormCliente, cat: CatalogosCliente, configs: ConfigsDireccion = {}, sinOpciones: ReadonlySet<string> = new Set(), reglas: Reglas | null = null,
): number | null {
  for (let p = 1; p <= 5; p++) if (Object.keys(validarPaso(p, f, cat, configs, sinOpciones, reglas)).length > 0) return p;
  return null;
}

/** Minimo para guardar un borrador: primer nombre y una identificacion con tipo y numero. */
export function puedeGuardarBorrador(f: FormCliente): boolean {
  return (f.tipoPersona === "Juridica" ? !!f.razonSocial.trim() : !!f.nombre1.trim()) && f.identificaciones.some((i) => i.idTipoIdentificacion > 0 && i.numero.trim() !== "");
}

// ----- Conversion hacia/desde el API ----------------------------------------------------

const vacioANull = (s?: string | null) => (s && s.trim() !== "" ? s.trim() : null);

function aDtoIdent(i: FilaIdent): IdentificacionDto {
  return {
    id: i.id ?? null, idTipoIdentificacion: i.idTipoIdentificacion, numero: i.numero.trim(),
    idInstitucionEmisora: i.idInstitucionEmisora ?? null,
    fechaEmision: vacioANull(i.fechaEmision), fechaVencimiento: vacioANull(i.fechaVencimiento),
    esPrincipal: i.esPrincipal,
  };
}

function aDtoDir(d: FilaDir): DireccionDto {
  return {
    id: d.id ?? null, idTipoDireccion: d.idTipoDireccion, idPais: d.idPais,
    niveles: d.niveles.map((n) => ({ id: n.id, texto: vacioANull(n.texto) })),
    codigoPostal: vacioANull(d.codigoPostal), detalle: d.detalle.trim(),
    detalle2: vacioANull(d.detalle2), detalle3: vacioANull(d.detalle3), esPrincipal: d.esPrincipal,
  };
}

function aDtoCont(c: FilaCont): ContactoDto {
  return { id: c.id ?? null, idTipoContacto: c.idTipoContacto, valor: c.valor.trim(), esPrincipal: c.esPrincipal };
}

function aDtoPerfil(f: FormCliente): PerfilFinancieroDto {
  const p = f.perfil;
  const e = p.economico;
  const esOrg = f.tipoPersona === "Juridica";
  const n = (s: string) => { const v = aMonto(s); return v === null || Number.isNaN(v) ? null : v; };
  return {
    economico: {
      idCondicionLaboral: esOrg ? null : e.idCondicionLaboral || null, idOcupacion: esOrg ? null : e.idOcupacion || null,
      empleador: esOrg ? null : vacioANull(e.empleador), gastosEstimados: esOrg ? null : n(e.gastosEstimados),
      activosDeclarados: n(e.activosDeclarados), pasivosDeclarados: n(e.pasivosDeclarados), patrimonioDeclarado: esOrg ? n(e.patrimonioDeclarado) : null,
      idMonedaBalance: e.idMonedaBalance || null, fechaCorte: vacioANull(e.fechaCorte),
    },
    residenciasFiscales: p.residenciasFiscales.map((r) => ({
      id: r.id ?? null, idPais: r.idPais, identificadorTributario: vacioANull(r.identificadorTributario), esPrincipal: r.esPrincipal,
      fechaDesde: vacioANull(r.fechaDesde), fechaHasta: vacioANull(r.fechaHasta),
    })),
    ingresos: p.ingresos.map((i) => ({ id: i.id ?? null, idTipoIngreso: i.idTipoIngreso, monto: n(i.monto) ?? 0, idMoneda: i.idMoneda, idPeriodicidad: i.idPeriodicidad })),
    origenes: p.origenes.map((o) => ({ id: o.id ?? null, clase: o.clase, idOrigen: o.idOrigen })),
    perfilTransaccional: p.perfilTransaccional.map((t) => ({
      id: t.id ?? null, idTipoTransaccion: t.idTipoTransaccion, idFrecuencia: t.idFrecuencia, montoMinimo: n(t.montoMinimo), montoMaximo: n(t.montoMaximo),
      idMoneda: n(t.montoMinimo) === null && n(t.montoMaximo) === null ? null : t.idMoneda || null, idPaisOrigen: t.idPaisOrigen || null, idPaisDestino: t.idPaisDestino || null,
    })),
    cuentasExternas: p.cuentasExternas.map((x) => ({ id: x.id ?? null, idEntidad: x.idEntidad, numeroCuenta: normalizarIban(x.numeroCuenta), idTipoCuenta: x.idTipoCuenta, idMoneda: x.idMoneda || null })),
  };
}

function aDtoOrg(f: FormCliente): OrganizacionDto {
  return {
    razonSocial: f.razonSocial.trim(),
    nombresComerciales: f.nombresComerciales.filter((n) => n.nombre.trim()).map((n) => ({ id: n.id ?? null, nombre: n.nombre.trim() })),
    idFormaJuridica: f.idFormaJuridica, idPaisConstitucion: f.idPaisConstitucion,
    fechaConstitucion: vacioANull(f.fechaConstitucion), referenciaRegistral: vacioANull(f.referenciaRegistral),
    idEstadoLegal: f.idEstadoLegal, fechaDisolucion: vacioANull(f.fechaDisolucion),
    actividades: f.actividades.map((a) => ({
      id: a.id ?? null, idActividad: a.idActividad, descripcion: vacioANull(a.descripcion), esPrincipal: a.esPrincipal, fechaInicio: vacioANull(a.fechaInicio),
    })),
  };
}

export function aRequestAlta(f: FormCliente, idBorrador: number | null, idGestionDevuelta: number | null = null, idProspecto: number | null = null, revisionDuplicados: RevisionDuplicados | null = null): InterlocutorAltaRequest {
  const esOrg = f.tipoPersona === "Juridica";
  return {
    tipoPersona: f.tipoPersona,
    organizacion: esOrg ? aDtoOrg(f) : null,
    idPaisRegulador: f.idPaisRegulador,
    idUnidad: f.idUnidad || null,
    nombre1: esOrg ? "" : f.nombre1.trim(), nombre2: esOrg ? null : vacioANull(f.nombre2),
    apellido1: esOrg ? "" : f.apellido1.trim(), apellido2: esOrg ? null : vacioANull(f.apellido2),
    fechaNacimiento: esOrg ? null : vacioANull(f.fechaNacimiento),
    identidad: esOrg ? null : {
      nombreConocido: vacioANull(f.nombreConocido),
      idPaisNacimiento: f.idPaisNacimiento || null, idSexo: f.idSexo || null, idEstadoCivil: f.idEstadoCivil || null,
      nacionalidades: f.nacionalidades.map((n) => ({ id: n.id ?? null, idPais: n.idPais })),
    },
    identificaciones: f.identificaciones.map(aDtoIdent),
    direcciones: f.direcciones.map(aDtoDir),
    contactos: f.contactos.map(aDtoCont),
    perfilFinanciero: aDtoPerfil(f),
    idBorrador,
    idGestionDevuelta,
    idProspecto,
    revisionDuplicados,
    idTipoRol: f.rolId || null,
    empleado: f.empleado ? { idUnidad: f.empleado.idUnidad, numeroEmpleado: f.empleado.numero.trim() } : null,
  };
}

/** Direccion de un DTO del servidor (p. ej. la de un prospecto) como fila editable del formulario. */
export function filaDirDesdeDto(d: DireccionDto): FilaDir {
  const niveles = nivelesVacios().map((v, i) => ({ ...v, ...(d.niveles?.[i] ?? {}), texto: d.niveles?.[i]?.texto ?? "" }));
  return { ...dirVacia(true, d.idTipoDireccion, d.idPais), ...d, niveles, codigoPostal: d.codigoPostal ?? "", detalle2: d.detalle2 ?? "", detalle3: d.detalle3 ?? "", clave: nuevaClave(), id: null, esPrincipal: true };
}

export function aDtoDireccion(d: FilaDir): DireccionDto { return aDtoDir(d); }

export function aRequestModificacion(f: FormCliente, comentario: string, idGestionDevuelta?: number | null): InterlocutorModificacionRequest {
  const { idBorrador: _omitido, ...base } = aRequestAlta(f, null);
  void _omitido;
  return { ...base, comentario: vacioANull(comentario), idGestionDevuelta: idGestionDevuelta ?? null };
}

/** Contenido que se guarda en el borrador: el estado del formulario sin textos de solo lectura. */
export function aContenidoBorrador(f: FormCliente): unknown {
  return {
    ...f,
    // El servidor lee estos dos para armar el resumen del borrador.
    razonSocial: f.razonSocial,
    tipoPersona: f.tipoPersona,
    identificaciones: f.identificaciones.map(({ auditoria: _a, tipoNombre: _t, institucionNombre: _i, ...resto }) => (void _a, void _t, void _i, resto)),
    direcciones: f.direcciones.map(({ auditoria: _a, tipoNombre: _t, ...resto }) => (void _a, void _t, resto)),
    contactos: f.contactos.map(({ auditoria: _a, tipoNombre: _t, ...resto }) => (void _a, void _t, resto)),
  };
}

/** Reconstruye el formulario desde el JSON de un borrador (tolera campos faltantes). */
export function desdeContenidoBorrador(contenido: unknown): FormCliente {
  const c = (contenido ?? {}) as Partial<FormCliente>;
  const base = formVacio();
  return {
    tipoPersona: c.tipoPersona === "Juridica" ? "Juridica" : "Fisica",
    razonSocial: c.razonSocial ?? "",
    nombresComerciales: (c.nombresComerciales ?? []).map((n) => ({ ...nomVacio(), ...n, clave: nuevaClave(), id: null })),
    idFormaJuridica: Number(c.idFormaJuridica ?? 0),
    idPaisConstitucion: Number(c.idPaisConstitucion ?? 0),
    fechaConstitucion: c.fechaConstitucion ?? "",
    referenciaRegistral: c.referenciaRegistral ?? "",
    idEstadoLegal: Number(c.idEstadoLegal ?? 0),
    fechaDisolucion: c.fechaDisolucion ?? "",
    actividades: (c.actividades ?? []).map((a) => ({ ...actVacia(false), ...a, clave: nuevaClave(), id: null })),
    idPaisRegulador: Number(c.idPaisRegulador ?? 0),
    idUnidad: Number(c.idUnidad ?? 0),
    rolId: Number(c.rolId ?? 0) || undefined,
    empleado: c.empleado ? { idUnidad: Number(c.empleado.idUnidad ?? 0), numero: c.empleado.numero ?? "" } : undefined,
    nombre1: c.nombre1 ?? base.nombre1,
    nombre2: c.nombre2 ?? "",
    apellido1: c.apellido1 ?? "",
    apellido2: c.apellido2 ?? "",
    fechaNacimiento: c.fechaNacimiento ?? "",
    nombreConocido: c.nombreConocido ?? "",
    idPaisNacimiento: Number(c.idPaisNacimiento ?? 0),
    idSexo: Number(c.idSexo ?? 0),
    idEstadoCivil: Number(c.idEstadoCivil ?? 0),
    nacionalidades: (c.nacionalidades ?? []).map((n) => ({ ...nacVacia(), ...n, clave: nuevaClave(), id: null })),
    identificaciones: (c.identificaciones ?? []).map((i) => ({ ...identVacia(false), ...i, clave: nuevaClave(), id: null })),
    direcciones: (c.direcciones ?? []).map((d) => {
      // Borradores anteriores a las direcciones por pais traian provincia/canton/distrito como texto.
      const viejo = d as unknown as { provincia?: string; canton?: string; distrito?: string };
      const niveles = nivelesVacios().map((v, i) => ({ ...v, ...(d.niveles?.[i] ?? {}), texto: d.niveles?.[i]?.texto ?? "" }));
      if (!d.niveles && (viejo.provincia || viejo.canton || viejo.distrito)) {
        niveles[0].texto = viejo.provincia ?? ""; niveles[1].texto = viejo.canton ?? ""; niveles[2].texto = viejo.distrito ?? "";
      }
      return { ...dirVacia(false, 0), ...d, niveles, codigoPostal: d.codigoPostal ?? "", clave: nuevaClave(), id: null };
    }),
    contactos: (c.contactos ?? []).map((x) => ({ ...contVacio(0, false), ...x, clave: nuevaClave(), id: null })),
    perfil: {
      economico: { ...ecoVacio(), ...(c.perfil?.economico ?? {}) },
      residenciasFiscales: (c.perfil?.residenciasFiscales ?? []).map((x) => ({ ...rfVacia(false), ...x, clave: nuevaClave(), id: null })),
      ingresos: (c.perfil?.ingresos ?? []).map((x) => ({ ...ingVacio(0), ...x, clave: nuevaClave(), id: null })),
      origenes: (c.perfil?.origenes ?? []).map((x) => ({ ...x, clave: nuevaClave(), id: null })),
      perfilTransaccional: (c.perfil?.perfilTransaccional ?? []).map((x) => ({ ...ptVacio(0), ...x, clave: nuevaClave(), id: null })),
      cuentasExternas: (c.perfil?.cuentasExternas ?? []).map((x) => ({ ...cxVacia(), ...x, clave: nuevaClave(), id: null })),
    },
  };
}

const fecha = (s?: string | null) => (s ? s.slice(0, 10) : "");
const cuando = (s?: string | null) => (s ? new Date(s).toLocaleString() : "");

/** Reconstruye el formulario desde la ficha vigente del cliente (modo editar). */
export function desdeDetalle(d: InterlocutorDetalleResponse): FormCliente {
  const aud = (x: { creadoPor: string; creadoEn: string; modificadoPor?: string | null; modificadoEn?: string | null }) =>
    `Creado por ${x.creadoPor} el ${cuando(x.creadoEn)} · ` +
    (x.modificadoPor ? `Modificado por ${x.modificadoPor} el ${cuando(x.modificadoEn)}` : "Sin modificaciones");

  const o = d.organizacion;
  const form: FormCliente = {
    tipoPersona: d.tipoPersona === "Juridica" ? "Juridica" : "Fisica",
    razonSocial: o?.razonSocial ?? "",
    nombresComerciales: (o?.nombresComerciales ?? []).map((n) => ({ clave: nuevaClave(), id: n.id, nombre: n.nombre })),
    idFormaJuridica: o?.idFormaJuridica ?? 0,
    idPaisConstitucion: o?.idPaisConstitucion ?? 0,
    fechaConstitucion: fecha(o?.fechaConstitucion),
    referenciaRegistral: o?.referenciaRegistral ?? "",
    idEstadoLegal: o?.idEstadoLegal ?? 0,
    fechaDisolucion: fecha(o?.fechaDisolucion),
    actividades: (o?.actividades ?? []).map((a) => ({
      clave: nuevaClave(), id: a.id, idActividad: a.idActividad, descripcion: a.descripcion ?? "", esPrincipal: a.esPrincipal,
      fechaInicio: fecha(a.fechaInicio), actividadNombre: a.actividadNombre,
    })),
    idPaisRegulador: d.idPaisRegulador,
    idUnidad: d.idUnidad ?? 0,
    unidadNombre: d.unidadNombre ?? undefined,
    nombre1: d.nombre1 ?? "", nombre2: d.nombre2 ?? "", apellido1: d.apellido1 ?? "", apellido2: d.apellido2 ?? "",
    fechaNacimiento: fecha(d.fechaNacimiento),
    nombreConocido: d.identidad?.nombreConocido ?? "",
    idPaisNacimiento: d.identidad?.idPaisNacimiento ?? 0,
    idSexo: d.identidad?.idSexo ?? 0,
    idEstadoCivil: d.identidad?.idEstadoCivil ?? 0,
    nacionalidades: (d.identidad?.nacionalidades ?? []).map((n) => ({ clave: nuevaClave(), id: n.id, idPais: n.idPais })),
    identificaciones: d.identificaciones.map((i) => ({
      clave: nuevaClave(), id: i.id, idTipoIdentificacion: i.idTipoIdentificacion, numero: i.numero,
      idInstitucionEmisora: i.idInstitucionEmisora ?? null, fechaEmision: fecha(i.fechaEmision), fechaVencimiento: fecha(i.fechaVencimiento),
      esPrincipal: i.esPrincipal, auditoria: aud(i), tipoNombre: i.tipoNombre, institucionNombre: i.institucionNombre ?? undefined,
    })),
    direcciones: d.direcciones.map((x) => ({
      clave: nuevaClave(), id: x.id, idTipoDireccion: x.idTipoDireccion, idPais: x.idPais, paisNombre: x.paisNombre,
      niveles: nivelesVacios().map((v, i) => {
        const n = x.niveles.find((y) => y.nivel === i + 1);
        if (!n) return v;
        return n.modo === "Lista" ? { id: n.id ?? null, texto: "", nombre: n.texto ?? undefined } : { id: null, texto: n.texto ?? "" };
      }),
      codigoPostal: x.codigoPostal ?? "",
      detalle: x.detalle, detalle2: x.detalle2 ?? "", detalle3: x.detalle3 ?? "", esPrincipal: x.esPrincipal, auditoria: aud(x), tipoNombre: x.tipoNombre,
    })),
    contactos: d.contactos.map((x) => ({
      clave: nuevaClave(), id: x.id, idTipoContacto: x.idTipoContacto, valor: x.valor, esPrincipal: x.esPrincipal,
      auditoria: aud(x), tipoNombre: x.tipoNombre,
    })),
    perfil: perfilDesdeDetalle(d),
  };
  // Datos enmascarados por el perfil de datos: se recuerdan para no validar su formato mientras no se toquen.
  const codigos = (d.mascaras?.campos ?? []).map((c) => c.campo);
  if (codigos.length > 0) {
    const protegidos: Record<string, string> = {};
    form.contactos.forEach((c, i) => {
      const o2 = d.contactos[i];
      const campo = o2 && (o2.tipoCodigo === "CORREO" || (o2.tipoCodigo !== "TELEFONO" && o2.valor.includes("@"))) ? "CLI_CORREO" : "CLI_TELEFONO";
      if (codigos.includes(campo)) protegidos[c.clave] = c.valor;
    });
    if (codigos.includes("CE_NUMERO_CUENTA")) form.perfil.cuentasExternas.forEach((x) => { protegidos[x.clave] = x.numeroCuenta; });
    form.protegidos = protegidos;
    form.fechaNacimientoProtegida = codigos.includes("CLI_FECHA_NACIMIENTO") && !!d.mascaras?.textos.fechaNacimiento;
    form.enmascarados = (d.mascaras?.campos ?? []).map((c) => c.etiqueta);
    form.codigosEnmascarados = codigos;
  }
  return form;
}

const montoTexto = (v?: number | null) => (v == null ? "" : v.toFixed(2));

function perfilDesdeDetalle(d: InterlocutorDetalleResponse): PerfilForm {
  const pf = d.perfilFinanciero;
  const e = pf?.economico;
  return {
    economico: {
      idCondicionLaboral: e?.idCondicionLaboral ?? 0, idOcupacion: e?.idOcupacion ?? 0, empleador: e?.empleador ?? "",
      gastosEstimados: montoTexto(e?.gastosEstimados), activosDeclarados: montoTexto(e?.activosDeclarados),
      pasivosDeclarados: montoTexto(e?.pasivosDeclarados), patrimonioDeclarado: montoTexto(e?.patrimonioDeclarado),
      idMonedaBalance: e?.idMonedaBalance ?? 0, fechaCorte: fecha(e?.fechaCorte),
    },
    residenciasFiscales: (pf?.residenciasFiscales ?? []).map((r) => ({
      clave: nuevaClave(), id: r.id, idPais: r.idPais, identificadorTributario: r.identificadorTributario ?? "", esPrincipal: r.esPrincipal,
      fechaDesde: fecha(r.fechaDesde), fechaHasta: fecha(r.fechaHasta), paisNombre: r.paisNombre,
    })),
    ingresos: (pf?.ingresos ?? []).map((i) => ({
      clave: nuevaClave(), id: i.id, idTipoIngreso: i.idTipoIngreso, monto: montoTexto(i.monto), idMoneda: i.idMoneda, idPeriodicidad: i.idPeriodicidad,
      tipoNombre: i.tipoNombre, periodicidadNombre: i.periodicidadNombre, monedaCodigo: i.monedaCodigo,
    })),
    origenes: (pf?.origenes ?? []).map((o) => ({ clave: nuevaClave(), id: o.id, clase: o.clase, idOrigen: o.idOrigen, origenNombre: o.origenNombre })),
    perfilTransaccional: (pf?.perfilTransaccional ?? []).map((t) => ({
      clave: nuevaClave(), id: t.id, idTipoTransaccion: t.idTipoTransaccion, idFrecuencia: t.idFrecuencia, montoMinimo: montoTexto(t.montoMinimo), montoMaximo: montoTexto(t.montoMaximo),
      idMoneda: t.idMoneda ?? 0, idPaisOrigen: t.idPaisOrigen ?? 0, idPaisDestino: t.idPaisDestino ?? 0,
      tipoNombre: t.tipoNombre, frecuenciaNombre: t.frecuenciaNombre, monedaCodigo: t.monedaCodigo ?? undefined,
    })),
    cuentasExternas: (pf?.cuentasExternas ?? []).map((x) => ({
      clave: nuevaClave(), id: x.id, idEntidad: x.idEntidad, numeroCuenta: x.numeroCuenta, idTipoCuenta: x.idTipoCuenta, idMoneda: x.idMoneda ?? 0,
      entidadNombre: x.entidadNombre, tipoCuentaNombre: x.tipoCuentaNombre, monedaCodigo: x.monedaCodigo ?? undefined,
    })),
  };
}

// ----- Diferencias (modo editar) -----------------------------------------------------------

export interface LineaCambio {
  etiqueta: string;
  antes: string;
  despues: string;
}

function nombreItem(lista: CatalogoItemResponse[], id: number | null | undefined, respaldo?: string): string {
  if (!id) return "—";
  return lista.find((x) => x.id === id)?.nombre ?? respaldo ?? String(id);
}

const norm = (s?: string | null) => (s ?? "").trim();

/** Direccion en una linea: pais, niveles, codigo postal y direccion exacta. */
export function textoDireccion(d: FilaDir, cat: CatalogosCliente): string {
  return [nombreItem(cat.paisesDireccion, d.idPais, d.paisNombre), ...d.niveles.map(textoNivel), norm(d.codigoPostal), norm(d.detalle), norm(d.detalle2), norm(d.detalle3)]
    .filter((x) => x && x !== "—").join(", ");
}
const si = (b: boolean) => (b ? "Sí" : "No");

/** Texto visible de un nivel (nombre de la division o texto libre). */
const textoNivel = (n?: NivelFila) => (n ? (n.id ? n.nombre ?? String(n.id) : norm(n.texto)) : "");
const llaveNivel = (n?: NivelFila) => (n ? (n.id ? `#${n.id}` : norm(n.texto)) : "");

export function calcularCambios(orig: FormCliente, act: FormCliente, cat: CatalogosCliente, configs: ConfigsDireccion = {}): LineaCambio[] {
  const lineas: LineaCambio[] = [];
  const tiposId = tiposIdentificacionDe(cat, act.tipoPersona);
  const campo = (etiqueta: string, a: string, b: string) => {
    if (a !== b) lineas.push({ etiqueta, antes: a || "—", despues: b || "—" });
  };

  campo("País regulador", nombreItem(cat.paises, orig.idPaisRegulador), nombreItem(cat.paises, act.idPaisRegulador));
  campo("Unidad (sucursal)",
    orig.idUnidad ? cat.unidades.find((u) => u.id === orig.idUnidad)?.ruta ?? orig.unidadNombre ?? String(orig.idUnidad) : "",
    act.idUnidad ? cat.unidades.find((u) => u.id === act.idUnidad)?.ruta ?? String(act.idUnidad) : "");
  if (act.tipoPersona === "Juridica") {
    campo("Razón social", norm(orig.razonSocial), norm(act.razonSocial));
    campo("Forma jurídica", nombreItem(cat.formasJuridicas, orig.idFormaJuridica), nombreItem(cat.formasJuridicas, act.idFormaJuridica));
    campo("País de constitución", nombreItem(cat.paisesDireccion, orig.idPaisConstitucion), nombreItem(cat.paisesDireccion, act.idPaisConstitucion));
    campo("Fecha de constitución", orig.fechaConstitucion, act.fechaConstitucion);
    campo("Referencia registral", norm(orig.referenciaRegistral), norm(act.referenciaRegistral));
    campo("Estado legal", nombreItem(cat.estadosLegales, orig.idEstadoLegal), nombreItem(cat.estadosLegales, act.idEstadoLegal));
    campo("Fecha de disolución", orig.fechaDisolucion, act.fechaDisolucion);
    for (const o of orig.nombresComerciales) {
      const a = act.nombresComerciales.find((x) => x.id === o.id);
      if (!a) lineas.push({ etiqueta: `Nombre comercial ${o.nombre}`, antes: "Vigente", despues: "Se da de baja" });
      else campo("Nombre comercial", norm(o.nombre), norm(a.nombre));
    }
    for (const a of act.nombresComerciales.filter((x) => x.id == null && x.nombre.trim()))
      lineas.push({ etiqueta: "Nuevo nombre comercial", antes: "—", despues: norm(a.nombre) });
    for (const o of orig.actividades) {
      const a = act.actividades.find((x) => x.id === o.id);
      const nombre = `Actividad ${nombreItem(cat.actividades, o.idActividad, o.actividadNombre)}`;
      if (!a) { lineas.push({ etiqueta: nombre, antes: "Vigente", despues: "Se da de baja" }); continue; }
      campo(`${nombre} · Actividad`, nombreItem(cat.actividades, o.idActividad, o.actividadNombre), nombreItem(cat.actividades, a.idActividad));
      campo(`${nombre} · Descripción`, norm(o.descripcion), norm(a.descripcion));
      campo(`${nombre} · Inicio`, fecha(o.fechaInicio), fecha(a.fechaInicio));
      campo(`${nombre} · Principal`, si(o.esPrincipal), si(a.esPrincipal));
    }
    for (const a of act.actividades.filter((x) => x.id == null))
      lineas.push({ etiqueta: "Nueva actividad económica", antes: "—", despues: nombreItem(cat.actividades, a.idActividad) });
  } else {
    campo("Primer nombre", norm(orig.nombre1), norm(act.nombre1));
    campo("Segundo nombre", norm(orig.nombre2), norm(act.nombre2));
    campo("Primer apellido", norm(orig.apellido1), norm(act.apellido1));
    campo("Segundo apellido", norm(orig.apellido2), norm(act.apellido2));
    campo("Fecha de nacimiento", orig.fechaNacimiento, act.fechaNacimiento);
    campo("Nombre conocido", norm(orig.nombreConocido), norm(act.nombreConocido));
    campo("País de nacimiento", nombreItem(cat.paisesDireccion, orig.idPaisNacimiento), nombreItem(cat.paisesDireccion, act.idPaisNacimiento));
    campo("Sexo", nombreItem(cat.sexos, orig.idSexo), nombreItem(cat.sexos, act.idSexo));
    campo("Estado civil", nombreItem(cat.estadosCiviles, orig.idEstadoCivil), nombreItem(cat.estadosCiviles, act.idEstadoCivil));
    for (const o of orig.nacionalidades) {
      if (!act.nacionalidades.some((x) => x.id === o.id))
        lineas.push({ etiqueta: `Nacionalidad ${nombreItem(cat.paisesDireccion, o.idPais)}`, antes: "Vigente", despues: "Se da de baja" });
    }
    for (const a of act.nacionalidades.filter((x) => x.id == null))
      lineas.push({ etiqueta: "Nueva nacionalidad", antes: "—", despues: nombreItem(cat.paisesDireccion, a.idPais) });
  }

  // Identificaciones
  for (const o of orig.identificaciones) {
    const a = act.identificaciones.find((x) => x.id === o.id);
    const nombre = `Identificación ${o.tipoNombre ?? ""} ${o.numero}`.trim();
    if (!a) { lineas.push({ etiqueta: nombre, antes: "Vigente", despues: "Se da de baja" }); continue; }
    campo(`${nombre} · Tipo`, nombreItem(tiposId, o.idTipoIdentificacion, o.tipoNombre), nombreItem(tiposId, a.idTipoIdentificacion));
    campo(`${nombre} · Número`, norm(o.numero), norm(a.numero));
    campo(`${nombre} · Institución emisora`, nombreItem(cat.instituciones, o.idInstitucionEmisora, o.institucionNombre), nombreItem(cat.instituciones, a.idInstitucionEmisora));
    campo(`${nombre} · Emisión`, fecha(o.fechaEmision), fecha(a.fechaEmision));
    campo(`${nombre} · Vencimiento`, fecha(o.fechaVencimiento), fecha(a.fechaVencimiento));
    campo(`${nombre} · Principal`, si(o.esPrincipal), si(a.esPrincipal));
  }
  for (const a of act.identificaciones.filter((x) => x.id == null))
    lineas.push({ etiqueta: "Nueva identificación", antes: "—", despues: `${nombreItem(tiposId, a.idTipoIdentificacion)} ${norm(a.numero)}` });

  // Direcciones
  for (const o of orig.direcciones) {
    const a = act.direcciones.find((x) => x.id === o.id);
    const nombre = `Dirección ${o.tipoNombre ?? ""}`.trim();
    if (!a) { lineas.push({ etiqueta: nombre, antes: o.detalle, despues: "Se da de baja" }); continue; }
    campo(`${nombre} · Tipo`, nombreItem(cat.tiposDireccion, o.idTipoDireccion, o.tipoNombre), nombreItem(cat.tiposDireccion, a.idTipoDireccion));
    if (o.idPais !== a.idPais)
      lineas.push({ etiqueta: `${nombre} · País`, antes: nombreItem(cat.paisesDireccion, o.idPais, o.paisNombre), despues: nombreItem(cat.paisesDireccion, a.idPais) });
    for (let k = 0; k < NUM_NIVELES; k++) {
      if (llaveNivel(o.niveles[k]) === llaveNivel(a.niveles[k])) continue;
      const etiquetaNivel = configs[a.idPais]?.niveles.find((nv) => nv.nivel === k + 1)?.nombre ?? `Nivel ${k + 1}`;
      lineas.push({ etiqueta: `${nombre} · ${etiquetaNivel}`, antes: textoNivel(o.niveles[k]) || "—", despues: textoNivel(a.niveles[k]) || "—" });
    }
    campo(`${nombre} · ${configs[a.idPais]?.etiquetaCodigoPostal ?? "Código postal"}`, norm(o.codigoPostal), norm(a.codigoPostal));
    campo(`${nombre} · Dirección exacta (línea 1)`, norm(o.detalle), norm(a.detalle));
    campo(`${nombre} · Dirección exacta (línea 2)`, norm(o.detalle2), norm(a.detalle2));
    campo(`${nombre} · Dirección exacta (línea 3)`, norm(o.detalle3), norm(a.detalle3));
    campo(`${nombre} · Principal`, si(o.esPrincipal), si(a.esPrincipal));
  }
  for (const a of act.direcciones.filter((x) => x.id == null))
    lineas.push({ etiqueta: "Nueva dirección", antes: "—", despues: `${nombreItem(cat.tiposDireccion, a.idTipoDireccion)}: ${textoDireccion(a, cat)}` });

  // Contactos
  for (const o of orig.contactos) {
    const a = act.contactos.find((x) => x.id === o.id);
    const nombre = `${o.tipoNombre ?? "Contacto"}`;
    if (!a) { lineas.push({ etiqueta: nombre, antes: o.valor, despues: "Se da de baja" }); continue; }
    campo(`${nombre} · Tipo`, nombreItem(cat.tiposContacto, o.idTipoContacto, o.tipoNombre), nombreItem(cat.tiposContacto, a.idTipoContacto));
    campo(`${nombre} · Dato`, norm(o.valor), norm(a.valor));
    campo(`${nombre} · Principal`, si(o.esPrincipal), si(a.esPrincipal));
  }
  for (const a of act.contactos.filter((x) => x.id == null))
    lineas.push({ etiqueta: "Nuevo contacto", antes: "—", despues: `${nombreItem(cat.tiposContacto, a.idTipoContacto)}: ${norm(a.valor)}` });

  lineas.push(...cambiosPerfil(orig, act, cat));
  return lineas;
}

/** Texto de un monto con su moneda (codigo) para resumenes: «1500.00 CRC». */
function textoMonto(monto: string, idMoneda: number, cat: CatalogosCliente, codigo?: string): string {
  if (!monto.trim()) return "";
  const m = idMoneda ? cat.monedas.find((x) => x.id === idMoneda)?.codigo ?? codigo ?? "" : "";
  return `${monto.trim()}${m ? ` ${m}` : ""}`;
}

/** Diferencias del perfil financiero entre la ficha vigente y lo editado. */
function cambiosPerfil(orig: FormCliente, act: FormCliente, cat: CatalogosCliente): LineaCambio[] {
  const lineas: LineaCambio[] = [];
  const campo = (etiqueta: string, a: string, b: string) => { if (a !== b) lineas.push({ etiqueta, antes: a || "—", despues: b || "—" }); };
  const o = orig.perfil, a = act.perfil;
  const esOrg = act.tipoPersona === "Juridica";
  const mn = (t: string) => { const v = aMonto(t); return v === null || Number.isNaN(v) ? norm(t) : v.toFixed(2); };
  const moneda = (id: number) => cat.monedas.find((m) => m.id === id)?.codigo ?? "";

  if (!esOrg) {
    campo("Condición laboral", nombreItem(cat.condicionesLaborales, o.economico.idCondicionLaboral), nombreItem(cat.condicionesLaborales, a.economico.idCondicionLaboral));
    campo("Ocupación o profesión", nombreItem(cat.ocupaciones, o.economico.idOcupacion), nombreItem(cat.ocupaciones, a.economico.idOcupacion));
    campo("Empleador o negocio", norm(o.economico.empleador), norm(a.economico.empleador));
    campo("Gastos estimados", mn(o.economico.gastosEstimados), mn(a.economico.gastosEstimados));
  } else campo("Patrimonio declarado", mn(o.economico.patrimonioDeclarado), mn(a.economico.patrimonioDeclarado));
  campo("Activos declarados", mn(o.economico.activosDeclarados), mn(a.economico.activosDeclarados));
  campo("Pasivos declarados", mn(o.economico.pasivosDeclarados), mn(a.economico.pasivosDeclarados));
  campo("Moneda de los montos", moneda(o.economico.idMonedaBalance), moneda(a.economico.idMonedaBalance));
  campo("Fecha de corte", o.economico.fechaCorte, a.economico.fechaCorte);

  for (const r of o.residenciasFiscales) {
    const x = a.residenciasFiscales.find((y) => y.id === r.id);
    const nombre = `Residencia fiscal ${nombreItem(cat.paisesDireccion, r.idPais, r.paisNombre)}`;
    if (!x) { lineas.push({ etiqueta: nombre, antes: "Vigente", despues: "Se da de baja" }); continue; }
    campo(`${nombre} · País`, nombreItem(cat.paisesDireccion, r.idPais, r.paisNombre), nombreItem(cat.paisesDireccion, x.idPais));
    campo(`${nombre} · Identificador tributario`, norm(r.identificadorTributario), norm(x.identificadorTributario));
    campo(`${nombre} · Desde`, r.fechaDesde, x.fechaDesde);
    campo(`${nombre} · Hasta`, r.fechaHasta, x.fechaHasta);
    campo(`${nombre} · Principal`, si(r.esPrincipal), si(x.esPrincipal));
  }
  for (const x of a.residenciasFiscales.filter((y) => y.id == null))
    lineas.push({ etiqueta: "Nueva residencia fiscal", antes: "—", despues: `${nombreItem(cat.paisesDireccion, x.idPais)}${norm(x.identificadorTributario) ? ` · ${norm(x.identificadorTributario)}` : ""}` });

  for (const i of o.ingresos) {
    const x = a.ingresos.find((y) => y.id === i.id);
    const nombre = `Ingreso ${nombreItem(cat.tiposIngreso, i.idTipoIngreso, i.tipoNombre)}`;
    if (!x) { lineas.push({ etiqueta: nombre, antes: textoMonto(i.monto, i.idMoneda, cat, i.monedaCodigo), despues: "Se da de baja" }); continue; }
    campo(`${nombre} · Tipo`, nombreItem(cat.tiposIngreso, i.idTipoIngreso, i.tipoNombre), nombreItem(cat.tiposIngreso, x.idTipoIngreso));
    campo(`${nombre} · Monto`, mn(i.monto), mn(x.monto));
    campo(`${nombre} · Moneda`, moneda(i.idMoneda) || i.monedaCodigo || "", moneda(x.idMoneda));
    campo(`${nombre} · Periodicidad`, nombreItem(cat.periodicidades, i.idPeriodicidad, i.periodicidadNombre), nombreItem(cat.periodicidades, x.idPeriodicidad));
  }
  for (const x of a.ingresos.filter((y) => y.id == null))
    lineas.push({ etiqueta: "Nuevo ingreso", antes: "—", despues: `${nombreItem(cat.tiposIngreso, x.idTipoIngreso)}: ${textoMonto(x.monto, x.idMoneda, cat)} (${nombreItem(cat.periodicidades, x.idPeriodicidad)})` });

  const listaOrigen = (c: "Fondos" | "Patrimonio") => (c === "Fondos" ? cat.origenesFondos : cat.origenesPatrimonio);
  for (const r of o.origenes) {
    if (!a.origenes.some((y) => y.id === r.id))
      lineas.push({ etiqueta: `Origen de ${r.clase === "Fondos" ? "fondos" : "patrimonio"} ${nombreItem(listaOrigen(r.clase), r.idOrigen, r.origenNombre)}`, antes: "Vigente", despues: "Se da de baja" });
  }
  for (const x of a.origenes.filter((y) => y.id == null))
    lineas.push({ etiqueta: `Nuevo origen de ${x.clase === "Fondos" ? "fondos" : "patrimonio"}`, antes: "—", despues: nombreItem(listaOrigen(x.clase), x.idOrigen) });

  for (const t of o.perfilTransaccional) {
    const x = a.perfilTransaccional.find((y) => y.id === t.id);
    const nombre = `Transacción esperada ${nombreItem(cat.tiposTransaccion, t.idTipoTransaccion, t.tipoNombre)}`;
    if (!x) { lineas.push({ etiqueta: nombre, antes: "Vigente", despues: "Se da de baja" }); continue; }
    campo(`${nombre} · Tipo`, nombreItem(cat.tiposTransaccion, t.idTipoTransaccion, t.tipoNombre), nombreItem(cat.tiposTransaccion, x.idTipoTransaccion));
    campo(`${nombre} · Frecuencia`, nombreItem(cat.frecuencias, t.idFrecuencia, t.frecuenciaNombre), nombreItem(cat.frecuencias, x.idFrecuencia));
    campo(`${nombre} · Monto mínimo`, mn(t.montoMinimo), mn(x.montoMinimo));
    campo(`${nombre} · Monto máximo`, mn(t.montoMaximo), mn(x.montoMaximo));
    campo(`${nombre} · Moneda`, mn(t.montoMinimo) ? moneda(t.idMoneda) : "", mn(x.montoMinimo) ? moneda(x.idMoneda) : "");
    campo(`${nombre} · País de origen`, nombreItem(cat.paisesDireccion, t.idPaisOrigen), nombreItem(cat.paisesDireccion, x.idPaisOrigen));
    campo(`${nombre} · País de destino`, nombreItem(cat.paisesDireccion, t.idPaisDestino), nombreItem(cat.paisesDireccion, x.idPaisDestino));
  }
  for (const x of a.perfilTransaccional.filter((y) => y.id == null))
    lineas.push({ etiqueta: "Nueva transacción esperada", antes: "—", despues: `${nombreItem(cat.tiposTransaccion, x.idTipoTransaccion)} · ${nombreItem(cat.frecuencias, x.idFrecuencia)}${mn(x.montoMinimo) ? ` · ${textoMonto(x.montoMinimo, x.idMoneda, cat)} a ${textoMonto(x.montoMaximo, x.idMoneda, cat)} por ${nombreItem(cat.frecuencias, x.idFrecuencia).toLowerCase()}` : ""}` });

  for (const t of o.cuentasExternas) {
    const x = a.cuentasExternas.find((y) => y.id === t.id);
    const nombre = `Cuenta externa ${t.numeroCuenta}`;
    if (!x) { lineas.push({ etiqueta: nombre, antes: "Vigente", despues: "Se da de baja" }); continue; }
    campo(`${nombre} · Entidad`, nombreItem(cat.entidadesFinancieras, t.idEntidad, t.entidadNombre), nombreItem(cat.entidadesFinancieras, x.idEntidad));
    campo(`${nombre} · IBAN`, t.numeroCuenta, normalizarIban(x.numeroCuenta));
    campo(`${nombre} · Tipo`, nombreItem(cat.tiposCuentaExterna, t.idTipoCuenta, t.tipoCuentaNombre), nombreItem(cat.tiposCuentaExterna, x.idTipoCuenta));
    campo(`${nombre} · Moneda`, moneda(t.idMoneda) || t.monedaCodigo || "", moneda(x.idMoneda));
  }
  for (const x of a.cuentasExternas.filter((y) => y.id == null))
    lineas.push({ etiqueta: "Nueva cuenta externa", antes: "—", despues: `${nombreItem(cat.entidadesFinancieras, x.idEntidad)} · ${normalizarIban(x.numeroCuenta)} · ${nombreItem(cat.tiposCuentaExterna, x.idTipoCuenta)}${x.idMoneda ? ` · ${moneda(x.idMoneda)}` : ""}` });
  return lineas;
}

// ----- Correccion de una gestion devuelta ------------------------------------------------

const PATRON_HIJO = /^(ident|dir|cont|nom|act|nac|rf|ing|orig|pt|cx)\[(\+?)(\d+)\]\.([a-z0-9_]+)$/;
const aNum = (v?: string | null) => (v == null || v === "" ? null : Number(v));

/**
 * Superpone sobre la ficha vigente los cambios que propuso una gestion (usa los valores crudos de cada campo),
 * para que quien recibe una gestion devuelta corrija sobre lo que ya habia pedido y no empiece de cero.
 */
export function aplicarGestion(base: FormCliente, campos: GestionCampoResponse[]): FormCliente {
  const f: FormCliente = JSON.parse(JSON.stringify(base));
  const nuevos = new Map<string, { prefijo: string; campos: GestionCampoResponse[] }>();
  const bajas = { ident: new Set<number>(), dir: new Set<number>(), cont: new Set<number>(), nom: new Set<number>(), act: new Set<number>(), nac: new Set<number>(), rf: new Set<number>(), ing: new Set<number>(), orig: new Set<number>(), pt: new Set<number>(), cx: new Set<number>() };

  const setIdent = (x: FilaIdent, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "id_tipo_identificacion") x.idTipoIdentificacion = aNum(v) ?? 0;
    else if (col === "numero_identificacion") x.numero = v ?? "";
    else if (col === "id_institucion_emisora") x.idInstitucionEmisora = aNum(v);
    else if (col === "fecha_emision") x.fechaEmision = v ?? "";
    else if (col === "fecha_vencimiento") x.fechaVencimiento = v ?? "";
    else if (col === "es_principal") x.esPrincipal = v === "1";
  };
  const setDir = (x: FilaDir, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    const m = /^nivel([1-4])_(id|texto)$/.exec(col);
    if (m) {
      const n = x.niveles[Number(m[1]) - 1];
      if (m[2] === "id") { n.id = aNum(v); n.nombre = c.valorNuevo ?? undefined; } else n.texto = v ?? "";
    } else if (col === "id_tipo_direccion") x.idTipoDireccion = aNum(v) ?? 0;
    else if (col === "id_pais") x.idPais = aNum(v) ?? 0;
    else if (col === "codigo_postal") x.codigoPostal = v ?? "";
    else if (col === "detalle") x.detalle = v ?? "";
    else if (col === "detalle2") x.detalle2 = v ?? "";
    else if (col === "detalle3") x.detalle3 = v ?? "";
    else if (col === "es_principal") x.esPrincipal = v === "1";
  };
  const setCont = (x: FilaCont, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "id_tipo_contacto") x.idTipoContacto = aNum(v) ?? 0;
    else if (col === "valor") x.valor = v ?? "";
    else if (col === "es_principal") x.esPrincipal = v === "1";
  };

  const setNac = (x: FilaNac, col: string, c: GestionCampoResponse) => { if (col === "id_pais") x.idPais = aNum(c.nuevoCrudo ?? null) ?? 0; };
  const setNom = (x: FilaNom, col: string, c: GestionCampoResponse) => { if (col === "nombre") x.nombre = c.nuevoCrudo ?? ""; };
  const setAct = (x: FilaAct, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "id_actividad") x.idActividad = aNum(v) ?? 0;
    else if (col === "descripcion") x.descripcion = v ?? "";
    else if (col === "fecha_inicio") x.fechaInicio = v ?? "";
    else if (col === "es_principal") x.esPrincipal = v === "1";
  };

  const setRf = (x: FilaRF, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "id_pais") x.idPais = aNum(v) ?? 0;
    else if (col === "identificador_tributario") x.identificadorTributario = v ?? "";
    else if (col === "fecha_desde") x.fechaDesde = v ?? "";
    else if (col === "fecha_hasta") x.fechaHasta = v ?? "";
    else if (col === "es_principal") x.esPrincipal = v === "1";
  };
  const setIng = (x: FilaIng, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "id_tipo_ingreso") x.idTipoIngreso = aNum(v) ?? 0;
    else if (col === "monto") x.monto = v ?? "";
    else if (col === "id_moneda") x.idMoneda = aNum(v) ?? 0;
    else if (col === "id_periodicidad") x.idPeriodicidad = aNum(v) ?? 0;
  };
  const setOrig = (x: FilaOrig, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "clase") x.clase = v === "Patrimonio" ? "Patrimonio" : "Fondos";
    else if (col === "id_origen") x.idOrigen = aNum(v) ?? 0;
  };
  const setPt = (x: FilaPT, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "id_tipo_transaccion") x.idTipoTransaccion = aNum(v) ?? 0;
    else if (col === "id_frecuencia") x.idFrecuencia = aNum(v) ?? 0;
    else if (col === "monto_minimo") x.montoMinimo = v ?? "";
    else if (col === "monto_maximo") x.montoMaximo = v ?? "";
    else if (col === "id_moneda") x.idMoneda = aNum(v) ?? 0;
    else if (col === "id_pais_origen") x.idPaisOrigen = aNum(v) ?? 0;
    else if (col === "id_pais_destino") x.idPaisDestino = aNum(v) ?? 0;
  };

  const setCx = (x: FilaCx, col: string, c: GestionCampoResponse) => {
    const v = c.nuevoCrudo ?? null;
    if (col === "id_entidad") x.idEntidad = aNum(v) ?? 0;
    else if (col === "numero_cuenta_externa") x.numeroCuenta = v ?? "";
    else if (col === "id_tipo_cuenta") x.idTipoCuenta = aNum(v) ?? 0;
    else if (col === "id_moneda") x.idMoneda = aNum(v) ?? 0;
  };

  for (const c of campos) {
    const v = c.nuevoCrudo ?? null;
    const m = PATRON_HIJO.exec(c.nombreCampo);
    if (!m) {
      const eco = f.perfil.economico;
      if (c.nombreCampo.startsWith("pe_")) {
        const k = c.nombreCampo.slice(3);
        if (k === "id_condicion_laboral") eco.idCondicionLaboral = aNum(v) ?? 0;
        else if (k === "id_ocupacion") eco.idOcupacion = aNum(v) ?? 0;
        else if (k === "empleador") eco.empleador = v ?? "";
        else if (k === "gastos_estimados") eco.gastosEstimados = v ?? "";
        else if (k === "activos_declarados") eco.activosDeclarados = v ?? "";
        else if (k === "pasivos_declarados") eco.pasivosDeclarados = v ?? "";
        else if (k === "patrimonio_declarado") eco.patrimonioDeclarado = v ?? "";
        else if (k === "id_moneda_balance") eco.idMonedaBalance = aNum(v) ?? 0;
        else if (k === "fecha_corte") eco.fechaCorte = v ?? "";
        continue;
      }
      if (c.nombreCampo === "tipo_persona") f.tipoPersona = v === "Juridica" ? "Juridica" : "Fisica";
      else if (c.nombreCampo === "razon_social") f.razonSocial = v ?? "";
      else if (c.nombreCampo === "id_forma_juridica") f.idFormaJuridica = aNum(v) ?? 0;
      else if (c.nombreCampo === "id_pais_constitucion") f.idPaisConstitucion = aNum(v) ?? 0;
      else if (c.nombreCampo === "fecha_constitucion") f.fechaConstitucion = v ?? "";
      else if (c.nombreCampo === "referencia_registral") f.referenciaRegistral = v ?? "";
      else if (c.nombreCampo === "id_estado_legal") f.idEstadoLegal = aNum(v) ?? 0;
      else if (c.nombreCampo === "fecha_disolucion") f.fechaDisolucion = v ?? "";
      else
      if (c.nombreCampo === "id_pais_regulador") f.idPaisRegulador = aNum(v) ?? f.idPaisRegulador;
      else if (c.nombreCampo === "id_unidad") f.idUnidad = aNum(v) ?? 0;
      else if (c.nombreCampo === "rol_alta") f.rolId = aNum(v) ?? undefined;
      else if (c.nombreCampo === "emp_unidad") f.empleado = { idUnidad: aNum(v) ?? 0, numero: f.empleado?.numero ?? "" };
      else if (c.nombreCampo === "emp_numero") f.empleado = { idUnidad: f.empleado?.idUnidad ?? 0, numero: v ?? "" };
      else if (c.nombreCampo === "nombre1") f.nombre1 = v ?? "";
      else if (c.nombreCampo === "nombre2") f.nombre2 = v ?? "";
      else if (c.nombreCampo === "apellido1") f.apellido1 = v ?? "";
      else if (c.nombreCampo === "apellido2") f.apellido2 = v ?? "";
      else if (c.nombreCampo === "fecha_nacimiento") f.fechaNacimiento = v ?? "";
      else if (c.nombreCampo === "nombre_conocido") f.nombreConocido = v ?? "";
      else if (c.nombreCampo === "id_pais_nacimiento") f.idPaisNacimiento = aNum(v) ?? 0;
      else if (c.nombreCampo === "id_sexo") f.idSexo = aNum(v) ?? 0;
      else if (c.nombreCampo === "id_estado_civil") f.idEstadoCivil = aNum(v) ?? 0;
      continue;
    }
    const [, prefijo, nuevo, num, col] = m;
    if (nuevo) {
      const k = `${prefijo}${num}`;
      if (!nuevos.has(k)) nuevos.set(k, { prefijo, campos: [] });
      nuevos.get(k)!.campos.push({ ...c, nombreCampo: col });
      continue;
    }
    const idReg = Number(num);
    if (col === "estado") { if (v === "Inactivo") bajas[prefijo as keyof typeof bajas].add(idReg); continue; }
    if (prefijo === "ident") { const x = f.identificaciones.find((y) => y.id === idReg); if (x) setIdent(x, col, c); }
    else if (prefijo === "dir") { const x = f.direcciones.find((y) => y.id === idReg); if (x) setDir(x, col, c); }
    else if (prefijo === "nom") { const x = f.nombresComerciales.find((y) => y.id === idReg); if (x) setNom(x, col, c); }
    else if (prefijo === "act") { const x = f.actividades.find((y) => y.id === idReg); if (x) setAct(x, col, c); }
    else if (prefijo === "nac") { const x = f.nacionalidades.find((y) => y.id === idReg); if (x) setNac(x, col, c); }
    else if (prefijo === "rf") { const x = f.perfil.residenciasFiscales.find((y) => y.id === idReg); if (x) setRf(x, col, c); }
    else if (prefijo === "ing") { const x = f.perfil.ingresos.find((y) => y.id === idReg); if (x) setIng(x, col, c); }
    else if (prefijo === "orig") { const x = f.perfil.origenes.find((y) => y.id === idReg); if (x) setOrig(x, col, c); }
    else if (prefijo === "pt") { const x = f.perfil.perfilTransaccional.find((y) => y.id === idReg); if (x) setPt(x, col, c); }
    else if (prefijo === "cx") { const x = f.perfil.cuentasExternas.find((y) => y.id === idReg); if (x) setCx(x, col, c); }
    else { const x = f.contactos.find((y) => y.id === idReg); if (x) setCont(x, col, c); }
  }

  f.identificaciones = f.identificaciones.filter((x) => !(x.id != null && bajas.ident.has(x.id)));
  f.direcciones = f.direcciones.filter((x) => !(x.id != null && bajas.dir.has(x.id)));
  f.contactos = f.contactos.filter((x) => !(x.id != null && bajas.cont.has(x.id)));
  f.nombresComerciales = f.nombresComerciales.filter((x) => !(x.id != null && bajas.nom.has(x.id)));
  f.actividades = f.actividades.filter((x) => !(x.id != null && bajas.act.has(x.id)));
  f.nacionalidades = f.nacionalidades.filter((x) => !(x.id != null && bajas.nac.has(x.id)));
  f.perfil.residenciasFiscales = f.perfil.residenciasFiscales.filter((x) => !(x.id != null && bajas.rf.has(x.id)));
  f.perfil.ingresos = f.perfil.ingresos.filter((x) => !(x.id != null && bajas.ing.has(x.id)));
  f.perfil.origenes = f.perfil.origenes.filter((x) => !(x.id != null && bajas.orig.has(x.id)));
  f.perfil.perfilTransaccional = f.perfil.perfilTransaccional.filter((x) => !(x.id != null && bajas.pt.has(x.id)));
  f.perfil.cuentasExternas = f.perfil.cuentasExternas.filter((x) => !(x.id != null && bajas.cx.has(x.id)));

  for (const { prefijo, campos: cs } of nuevos.values()) {
    if (prefijo === "ident") { const x = identVacia(false); cs.forEach((c) => setIdent(x, c.nombreCampo, c)); f.identificaciones.push(x); }
    else if (prefijo === "dir") { const x = dirVacia(false, 0); cs.forEach((c) => setDir(x, c.nombreCampo, c)); f.direcciones.push(x); }
    else if (prefijo === "nom") { const x = nomVacio(); cs.forEach((c) => setNom(x, c.nombreCampo, c)); f.nombresComerciales.push(x); }
    else if (prefijo === "act") { const x = actVacia(false); cs.forEach((c) => setAct(x, c.nombreCampo, c)); f.actividades.push(x); }
    else if (prefijo === "nac") { const x = nacVacia(); cs.forEach((c) => setNac(x, c.nombreCampo, c)); f.nacionalidades.push(x); }
    else if (prefijo === "rf") { const x = rfVacia(false); cs.forEach((c) => setRf(x, c.nombreCampo, c)); f.perfil.residenciasFiscales.push(x); }
    else if (prefijo === "ing") { const x = ingVacio(0); cs.forEach((c) => setIng(x, c.nombreCampo, c)); f.perfil.ingresos.push(x); }
    else if (prefijo === "orig") { const x: FilaOrig = { clave: nuevaClave(), id: null, clase: "Fondos", idOrigen: 0 }; cs.forEach((c) => setOrig(x, c.nombreCampo, c)); f.perfil.origenes.push(x); }
    else if (prefijo === "pt") { const x = ptVacio(0); cs.forEach((c) => setPt(x, c.nombreCampo, c)); f.perfil.perfilTransaccional.push(x); }
    else if (prefijo === "cx") { const x = cxVacia(); cs.forEach((c) => setCx(x, c.nombreCampo, c)); f.perfil.cuentasExternas.push(x); }
    else { const x = contVacio(0, false); cs.forEach((c) => setCont(x, c.nombreCampo, c)); f.contactos.push(x); }
  }
  return f;
}
