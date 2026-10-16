import type {
  CatalogoItemResponse,
  UnidadItem,
  ContactoDto,
  DireccionConfig,
  DireccionDto,
  IdentificacionDto,
  InterlocutorAltaRequest,
  InterlocutorDetalleResponse,
  InterlocutorModificacionRequest,
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

export interface FormCliente {
  idPaisRegulador: number;
  /** Unidad (sucursal) a la que pertenece el cliente; 0 = sin elegir. */
  idUnidad: number;
  unidadNombre?: string;
  /** Canal por el que nacio el cliente (lista CANAL); 0 = sin elegir. Independiente de la unidad. */
  idCanalOrigen: number;
  canalOrigenNombre?: string;
  nombre1: string;
  nombre2: string;
  apellido1: string;
  apellido2: string;
  fechaNacimiento: string;
  identificaciones: FilaIdent[];
  direcciones: FilaDir[];
  contactos: FilaCont[];
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
  /** Lista CANAL. */
  canales: CatalogoItemResponse[];
}

let secuencia = 0;
export function nuevaClave(): string {
  secuencia += 1;
  return `k${Date.now()}-${secuencia}`;
}

export function formVacio(): FormCliente {
  return {
    idPaisRegulador: 0,
    idUnidad: 0,
    idCanalOrigen: 0,
    nombre1: "",
    nombre2: "",
    apellido1: "",
    apellido2: "",
    fechaNacimiento: "",
    identificaciones: [],
    direcciones: [],
    contactos: [],
  };
}

export function identVacia(esPrincipal: boolean): FilaIdent {
  return {
    clave: nuevaClave(), id: null, idTipoIdentificacion: 0, numero: "",
    idInstitucionEmisora: null, fechaEmision: "", fechaVencimiento: "", esPrincipal,
  };
}

export function dirVacia(esPrincipal: boolean, idTipoDireccion: number, idPais = 0): FilaDir {
  return { clave: nuevaClave(), id: null, idTipoDireccion, idPais, niveles: nivelesVacios(), codigoPostal: "", detalle: "", esPrincipal };
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
  configs: ConfigsDireccion = {}, sinOpciones: ReadonlySet<string> = new Set(),
): ErroresPaso {
  const e: ErroresPaso = {};

  if (paso === 1) {
    if (!f.idPaisRegulador) e.idPaisRegulador = "Obligatorio";
    if (!f.idUnidad && cat.unidades.length > 0) e.idUnidad = "Obligatorio";
    if (!f.idCanalOrigen && cat.canales.length > 0) e.idCanalOrigen = "Obligatorio";
    if (!f.nombre1.trim()) e.nombre1 = "Obligatorio";
    if (!f.apellido1.trim()) e.apellido1 = "Obligatorio";
    if (!f.fechaNacimiento) e.fechaNacimiento = "Obligatorio";
    else if (f.fechaNacimiento > hoy()) e.fechaNacimiento = "No puede ser futura";
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
      if (!i.idInstitucionEmisora) e[`${i.clave}.institucion`] = "Obligatorio";
      if (!i.fechaEmision) e[`${i.clave}.emision`] = "Obligatorio";
      else if (i.fechaEmision > hoy()) e[`${i.clave}.emision`] = "No puede ser futura";
      if (i.fechaEmision && i.fechaVencimiento && i.fechaVencimiento <= i.fechaEmision)
        e[`${i.clave}.vencimiento`] = "Debe ser posterior a la emisión";
    }
  }

  if (paso === 3) {
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
            if (vacio && nv.esObligatorio && cadenaOk && !sinOpciones.has(`${d.clave}.${nv.nivel}`))
              e[`${d.clave}.n${nv.nivel}`] = "Obligatorio";
            if (nv.modo === "Lista" && vacio) cadenaOk = false;
          }
          const cp = d.codigoPostal.trim();
          if (cfg.etiquetaCodigoPostal) {
            if (!cp) { if (cfg.codigoPostalObligatorio) e[`${d.clave}.cp`] = "Obligatorio"; }
            else if (cfg.patronCodigoPostal) {
              try {
                if (!new RegExp(cfg.patronCodigoPostal).test(cp)) e[`${d.clave}.cp`] = `Formato inválido para ${cfg.paisNombre}`;
              } catch { /* patron no evaluable en el navegador: lo valida el servidor */ }
            }
          }
        }
      }
      if (!d.detalle.trim()) e[`${d.clave}.detalle`] = "Indique la dirección exacta o quite esta dirección";
    }
  }

  if (paso === 4) {
    for (const c of f.contactos) {
      if (!c.idTipoContacto) { e[`${c.clave}.tipo`] = "Obligatorio"; continue; }
      const msg = validarContacto(codigoTipoContacto(cat, c.idTipoContacto), c.valor);
      if (msg) e[`${c.clave}.valor`] = msg;
    }
  }

  return e;
}

/** Primer paso (1-4) con errores, o null si todo es valido. */
export function primerPasoInvalido(
  f: FormCliente, cat: CatalogosCliente, configs: ConfigsDireccion = {}, sinOpciones: ReadonlySet<string> = new Set(),
): number | null {
  for (let p = 1; p <= 4; p++) if (Object.keys(validarPaso(p, f, cat, configs, sinOpciones)).length > 0) return p;
  return null;
}

/** Minimo para guardar un borrador: primer nombre y una identificacion con tipo y numero. */
export function puedeGuardarBorrador(f: FormCliente): boolean {
  return !!f.nombre1.trim() && f.identificaciones.some((i) => i.idTipoIdentificacion > 0 && i.numero.trim() !== "");
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
    codigoPostal: vacioANull(d.codigoPostal), detalle: d.detalle.trim(), esPrincipal: d.esPrincipal,
  };
}

function aDtoCont(c: FilaCont): ContactoDto {
  return { id: c.id ?? null, idTipoContacto: c.idTipoContacto, valor: c.valor.trim(), esPrincipal: c.esPrincipal };
}

export function aRequestAlta(f: FormCliente, idBorrador: number | null): InterlocutorAltaRequest {
  return {
    idPaisRegulador: f.idPaisRegulador,
    idUnidad: f.idUnidad || null,
    idCanalOrigen: f.idCanalOrigen || null,
    nombre1: f.nombre1.trim(), nombre2: vacioANull(f.nombre2), apellido1: f.apellido1.trim(), apellido2: vacioANull(f.apellido2),
    fechaNacimiento: f.fechaNacimiento,
    identificaciones: f.identificaciones.map(aDtoIdent),
    direcciones: f.direcciones.map(aDtoDir),
    contactos: f.contactos.map(aDtoCont),
    idBorrador,
  };
}

export function aRequestModificacion(f: FormCliente, comentario: string): InterlocutorModificacionRequest {
  const { idBorrador: _omitido, ...base } = aRequestAlta(f, null);
  void _omitido;
  return { ...base, comentario: vacioANull(comentario) };
}

/** Contenido que se guarda en el borrador: el estado del formulario sin textos de solo lectura. */
export function aContenidoBorrador(f: FormCliente): unknown {
  return {
    ...f,
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
    idPaisRegulador: Number(c.idPaisRegulador ?? 0),
    idUnidad: Number(c.idUnidad ?? 0),
    idCanalOrigen: Number(c.idCanalOrigen ?? 0),
    nombre1: c.nombre1 ?? base.nombre1,
    nombre2: c.nombre2 ?? "",
    apellido1: c.apellido1 ?? "",
    apellido2: c.apellido2 ?? "",
    fechaNacimiento: c.fechaNacimiento ?? "",
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
  };
}

const fecha = (s?: string | null) => (s ? s.slice(0, 10) : "");
const cuando = (s?: string | null) => (s ? new Date(s).toLocaleString() : "");

/** Reconstruye el formulario desde la ficha vigente del cliente (modo editar). */
export function desdeDetalle(d: InterlocutorDetalleResponse): FormCliente {
  const aud = (x: { creadoPor: string; creadoEn: string; modificadoPor?: string | null; modificadoEn?: string | null }) =>
    `Creado por ${x.creadoPor} el ${cuando(x.creadoEn)} · ` +
    (x.modificadoPor ? `Modificado por ${x.modificadoPor} el ${cuando(x.modificadoEn)}` : "Sin modificaciones");

  return {
    idPaisRegulador: d.idPaisRegulador,
    idUnidad: d.idUnidad ?? 0,
    unidadNombre: d.unidadNombre ?? undefined,
    idCanalOrigen: d.idCanalOrigen ?? 0,
    canalOrigenNombre: d.canalOrigenNombre ?? undefined,
    nombre1: d.nombre1 ?? "", nombre2: d.nombre2 ?? "", apellido1: d.apellido1 ?? "", apellido2: d.apellido2 ?? "",
    fechaNacimiento: fecha(d.fechaNacimiento),
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
      detalle: x.detalle, esPrincipal: x.esPrincipal, auditoria: aud(x), tipoNombre: x.tipoNombre,
    })),
    contactos: d.contactos.map((x) => ({
      clave: nuevaClave(), id: x.id, idTipoContacto: x.idTipoContacto, valor: x.valor, esPrincipal: x.esPrincipal,
      auditoria: aud(x), tipoNombre: x.tipoNombre,
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
  return [nombreItem(cat.paisesDireccion, d.idPais, d.paisNombre), ...d.niveles.map(textoNivel), norm(d.codigoPostal), norm(d.detalle)]
    .filter((x) => x && x !== "—").join(", ");
}
const si = (b: boolean) => (b ? "Sí" : "No");

/** Texto visible de un nivel (nombre de la division o texto libre). */
const textoNivel = (n?: NivelFila) => (n ? (n.id ? n.nombre ?? String(n.id) : norm(n.texto)) : "");
const llaveNivel = (n?: NivelFila) => (n ? (n.id ? `#${n.id}` : norm(n.texto)) : "");

export function calcularCambios(orig: FormCliente, act: FormCliente, cat: CatalogosCliente, configs: ConfigsDireccion = {}): LineaCambio[] {
  const lineas: LineaCambio[] = [];
  const campo = (etiqueta: string, a: string, b: string) => {
    if (a !== b) lineas.push({ etiqueta, antes: a || "—", despues: b || "—" });
  };

  campo("País regulador", nombreItem(cat.paises, orig.idPaisRegulador), nombreItem(cat.paises, act.idPaisRegulador));
  campo("Unidad (sucursal)",
    orig.idUnidad ? cat.unidades.find((u) => u.id === orig.idUnidad)?.ruta ?? orig.unidadNombre ?? String(orig.idUnidad) : "",
    act.idUnidad ? cat.unidades.find((u) => u.id === act.idUnidad)?.ruta ?? String(act.idUnidad) : "");
  campo("Canal de origen",
    orig.idCanalOrigen ? nombreItem(cat.canales, orig.idCanalOrigen) || orig.canalOrigenNombre || String(orig.idCanalOrigen) : "",
    act.idCanalOrigen ? nombreItem(cat.canales, act.idCanalOrigen) || String(act.idCanalOrigen) : "");
  campo("Primer nombre", norm(orig.nombre1), norm(act.nombre1));
  campo("Segundo nombre", norm(orig.nombre2), norm(act.nombre2));
  campo("Primer apellido", norm(orig.apellido1), norm(act.apellido1));
  campo("Segundo apellido", norm(orig.apellido2), norm(act.apellido2));
  campo("Fecha de nacimiento", orig.fechaNacimiento, act.fechaNacimiento);

  // Identificaciones
  for (const o of orig.identificaciones) {
    const a = act.identificaciones.find((x) => x.id === o.id);
    const nombre = `Identificación ${o.tipoNombre ?? ""} ${o.numero}`.trim();
    if (!a) { lineas.push({ etiqueta: nombre, antes: "Vigente", despues: "Se da de baja" }); continue; }
    campo(`${nombre} · Tipo`, nombreItem(cat.tiposIdentificacion, o.idTipoIdentificacion, o.tipoNombre), nombreItem(cat.tiposIdentificacion, a.idTipoIdentificacion));
    campo(`${nombre} · Número`, norm(o.numero), norm(a.numero));
    campo(`${nombre} · Institución emisora`, nombreItem(cat.instituciones, o.idInstitucionEmisora, o.institucionNombre), nombreItem(cat.instituciones, a.idInstitucionEmisora));
    campo(`${nombre} · Emisión`, fecha(o.fechaEmision), fecha(a.fechaEmision));
    campo(`${nombre} · Vencimiento`, fecha(o.fechaVencimiento), fecha(a.fechaVencimiento));
    campo(`${nombre} · Principal`, si(o.esPrincipal), si(a.esPrincipal));
  }
  for (const a of act.identificaciones.filter((x) => x.id == null))
    lineas.push({ etiqueta: "Nueva identificación", antes: "—", despues: `${nombreItem(cat.tiposIdentificacion, a.idTipoIdentificacion)} ${norm(a.numero)}` });

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
    campo(`${nombre} · Dirección exacta`, norm(o.detalle), norm(a.detalle));
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

  return lineas;
}
