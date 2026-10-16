import { NAVEGACION, esGrupo } from "./navegacion";
import { ZONAS_ADMINISTRACION } from "./zonasAdministracion";

/**
 * Catalogo de aplicaciones (lo que el usuario puede abrir): mosaicos del launchpad de Interlocutores
 * y pantallas del menu. Lo usa la barra "Buscar aplicaciones"; para sumar un modulo con su propio
 * launchpad basta con agregar sus zonas aqui.
 */
export interface Tile {
  titulo: string;
  sub: string;
  icono: string;
  permiso: string;
  ruta?: string;
  /** Solo si el mosaico comparte ruta con otro (p. ej. "Modificar cliente" abre la consulta). */
  id?: string;
}

export interface Zona {
  titulo: string;
  tiles: Tile[];
}

export const ZONAS_INTERLOCUTORES: Zona[] = [
  {
    titulo: "Consulta",
    tiles: [
      { titulo: "Consulta de interlocutores", sub: "Buscar y abrir la vista 360°", icono: "🔎", permiso: "CLI_CONSULTA", ruta: "/interlocutores/consulta" },
    ],
  },
  {
    titulo: "Clientes",
    tiles: [
      { titulo: "Nueva persona", sub: "Persona Física", icono: "＋", permiso: "CLI_ALTA", ruta: "/interlocutores/clientes/nuevo" },
      { titulo: "Nueva organización", sub: "Persona Jurídica", icono: "🏢", permiso: "CLI_ALTA", ruta: "/interlocutores/clientes/nuevo?tipo=juridica", id: "INT-0018" },
      { titulo: "Nuevo grupo", sub: "Agrupa personas y organizaciones", icono: "🔗", permiso: "CLI_ALTA", ruta: "/interlocutores/grupos/nuevo" },
      { titulo: "Mis borradores", sub: "Altas sin terminar", icono: "✎", permiso: "CLI_ALTA", ruta: "/interlocutores/borradores" },
      { titulo: "Modificar cliente", sub: "Se elige desde la consulta", icono: "⟳", permiso: "CLI_EDICION", ruta: "/interlocutores/consulta", id: "INT-0007" },
    ],
  },
  {
    titulo: "Bloqueos",
    tiles: [{ titulo: "Bloqueos de clientes", sub: "Bloquear, desbloquear y consultar", icono: "⛔", permiso: "CLI_BLOQUEO_VER", ruta: "/interlocutores/bloqueos" }],
  },
  {
    titulo: "Prospectos",
    tiles: [{ titulo: "Prospectos", sub: "Registrar, consultar y descartar", icono: "☆", permiso: "PRO_CONSULTA", ruta: "/interlocutores/prospectos" }],
  },
  {
    titulo: "Otros roles",
    tiles: [
      { titulo: "Nuevo beneficiario", sub: "Persona que recibe pagos o desembolsos", icono: "♡", permiso: "CLI_ALTA", ruta: "/interlocutores/terceros/alta?rol=BENEFICIARIO", id: "INT-0012" },
      { titulo: "Nuevo firmante", sub: "Persona que firma por el titular", icono: "✍", permiso: "CLI_ALTA", ruta: "/interlocutores/terceros/alta?rol=FIRMANTE", id: "INT-0013" },
      { titulo: "Nuevo fiador", sub: "Garantiza una obligación", icono: "⚖", permiso: "CLI_ALTA", ruta: "/interlocutores/terceros/alta?rol=FIADOR", id: "INT-0014" },
      { titulo: "Nuevo codeudor", sub: "Responde junto al deudor", icono: "⇄", permiso: "CLI_ALTA", ruta: "/interlocutores/terceros/alta?rol=CODEUDOR", id: "INT-0015" },
      { titulo: "Nuevo autorizado", sub: "Opera en nombre del titular", icono: "✔", permiso: "CLI_ALTA", ruta: "/interlocutores/terceros/alta?rol=AUTORIZADO", id: "INT-0016" },
      { titulo: "Nuevo empleado", sub: "Persona que trabaja en la institución", icono: "🏢", permiso: "CLI_ALTA", ruta: "/interlocutores/terceros/alta?rol=EMPLEADO", id: "INT-0021" },
    ],
  },
];


/** Launchpad de Gestiones: una app por función. Consulta no pide permiso (la API solo exige estar autenticado). */
export const ZONAS_GESTIONES: Zona[] = [
  {
    titulo: "Consulta",
    tiles: [{ titulo: "Consulta de gestiones", sub: "Ver todas las gestiones y su recorrido", icono: "🔎", permiso: "", ruta: "/gestiones/consulta" }],
  },
  {
    titulo: "Aprobación",
    tiles: [{ titulo: "Aprobación de gestiones", sub: "Gestiones que le toca resolver", icono: "☑", permiso: "GES_APROBAR", ruta: "/gestiones/aprobacion" }],
  },
  {
    titulo: "Administración de aprobaciones",
    tiles: [{ titulo: "Cambio de asignación de aprobador", sub: "Pasar gestiones a otro aprobador", icono: "⇄", permiso: "GES_REASIGNAR", ruta: "/gestiones/reasignacion" }],
  },
];

/** Launchpad del Gestor documental: una app por función; "por vencer" y "vencidos" abren la misma lista ya filtrada. */
export const ZONAS_DOCUMENTOS: Zona[] = [
  {
    titulo: "Expediente",
    tiles: [
      { titulo: "Cargar documentos", sub: "Asistente para subir uno o varios archivos", icono: "＋", permiso: "DOC_SUBIR", ruta: "/documentos/cargar" },
    ],
  },
  {
    titulo: "Seguimiento de vencimientos",
    tiles: [
      { titulo: "Documentos por vencer", sub: "Vigentes dentro de su plazo de aviso", icono: "⏳", permiso: "DOC_VER", ruta: "/documentos/expediente?estado=PorVencer", id: "DOC-0003" },
      { titulo: "Documentos vencidos", sub: "Vigentes con la fecha de vencimiento pasada", icono: "⚠", permiso: "DOC_VER", ruta: "/documentos/expediente?estado=Vencido", id: "DOC-0004" },
    ],
  },
];

/**
 * Identificador interno de cada APP (8 caracteres, estable): sirve para soporte, para "Acerca del APP"
 * y para las favoritas. Se asigna por ruta; una APP nueva debe sumar aqui su codigo (MODULO-NNNN).
 */
export const IDS_APP: Record<string, string> = {
  "/": "DSH-0001",
  "/notificaciones": "NOT-0001",
  "/interlocutores": "INT-0001",
  "/interlocutores/consulta": "INT-0002",
  "/interlocutores/clientes/nuevo": "INT-0003",
  "/interlocutores/borradores": "INT-0004",
  "/interlocutores/prospectos": "INT-0008",
  "/interlocutores/grupos/nuevo": "INT-0019",
  "/interlocutores/bloqueos": "INT-0020",
  "/restricciones-bloqueo": "BLO-0001",
  "/documentos": "DOC-0001",
  "/documentos/expediente": "DOC-0002",
  "/documentos/cargar": "DOC-0005",
  "/gestiones": "GES-0001",
  "/gestiones/consulta": "GES-0002",
  "/gestiones/aprobacion": "GES-0003",
  "/gestiones/reasignacion": "GES-0004",
  "/administracion": "ADM-0001",
  "/seguridad": "SEG-0001",
  "/usuarios": "SEG-0002",
  "/integraciones": "SEG-0003",
  "/perfiles-datos": "SEG-0004",
  "/unidades": "ORG-0001",
  "/reglas-aprobacion": "APR-0001",
  "/grupos": "APR-0002",
  "/listas-valores": "LOV-0001",
  "/numeracion": "NUM-0001",
  "/mensajes": "MSG-0001",
  "/division-territorial": "DIV-0001",
  "/parametros": "PAR-0001",
  "/roles-requisitos": "ROL-0001",
  "/documentos-admin": "DOC-0006",
  "/monedas": "MON-0001",
  "/tipo-cambio": "TCM-0001",
  "/feriados": "CAL-0001",
  "/auditoria": "AUD-0001",
};

/** Pantallas con ruta dinamica que no son mosaicos: tambien son APPs con su propio codigo. */
const PANTALLAS_DINAMICAS: { patron: RegExp; id: string; titulo: string }[] = [
  { patron: /^\/interlocutores\/clientes\/[^/]+\/modificar$/, id: "INT-0005", titulo: "Modificar cliente" },
  { patron: /^\/interlocutores\/[^/]+$/, id: "INT-0006", titulo: "Consulta 360°" },
  { patron: /^\/interlocutores\/terceros\/alta$/, id: "INT-0010", titulo: "Alta de persona con otro rol" },
  { patron: /^\/interlocutores\/terceros\/registro$/, id: "INT-0017", titulo: "Registro de persona con otro rol" },
  { patron: /^\/interlocutores\/[^/]+\/roles$/, id: "INT-0011", titulo: "Gestionar roles" },
];

export const idDeTile = (t: Tile): string | undefined => t.id ?? (t.ruta ? IDS_APP[t.ruta] : undefined);

export interface AppActual {
  id: string;
  titulo: string;
}

/** APP que corresponde a la ruta abierta (para "Acerca del APP"). */
export function aplicacionActual(ruta: string): AppActual | null {
  const limpia = ruta.length > 1 ? ruta.replace(/\/+$/, "") : ruta;
  const exacta = IDS_APP[limpia];
  if (exacta) {
    if (limpia === "/") return { id: exacta, titulo: "Dashboard" };
    const app = catalogoAplicaciones().find((a) => a.ruta === limpia);
    return { id: exacta, titulo: app?.titulo ?? limpia };
  }
  const dinamica = PANTALLAS_DINAMICAS.find((d) => d.patron.test(limpia));
  return dinamica ? { id: dinamica.id, titulo: dinamica.titulo } : null;
}

export interface Aplicacion {
  id?: string;
  titulo: string;
  detalle: string;
  ruta: string;
  permiso?: string;
}

export function catalogoAplicaciones(): Aplicacion[] {
  const apps: Aplicacion[] = [{ id: IDS_APP["/notificaciones"], titulo: "Notificaciones", detalle: "Aplicación", ruta: "/notificaciones" }];
  for (const e of NAVEGACION) {
    if (esGrupo(e)) e.hijos.forEach((h) => apps.push({ id: IDS_APP[h.ruta], titulo: h.etiqueta, detalle: e.etiqueta, ruta: h.ruta, permiso: h.permiso }));
    else apps.push({ id: IDS_APP[e.ruta], titulo: e.etiqueta, detalle: "Módulo", ruta: e.ruta, permiso: e.permiso });
  }
  for (const z of ZONAS_ADMINISTRACION)
    for (const t of z.tiles)
      if (t.ruta) apps.push({ id: idDeTile(t), titulo: t.titulo, detalle: `Administración · ${z.titulo}`, ruta: t.ruta, permiso: t.permiso });
  for (const z of ZONAS_GESTIONES)
    for (const t of z.tiles)
      if (t.ruta) apps.push({ id: idDeTile(t), titulo: t.titulo, detalle: `Gestiones · ${z.titulo}`, ruta: t.ruta, permiso: t.permiso });
  for (const z of ZONAS_DOCUMENTOS)
    for (const t of z.tiles)
      if (t.ruta) apps.push({ id: idDeTile(t), titulo: t.titulo, detalle: `Gestor documental · ${z.titulo}`, ruta: t.ruta, permiso: t.permiso });
  for (const z of ZONAS_INTERLOCUTORES)
    for (const t of z.tiles)
      if (t.ruta) apps.push({ id: idDeTile(t), titulo: t.titulo, detalle: `Interlocutores · ${z.titulo}`, ruta: t.ruta, permiso: t.permiso });
  return apps;
}

/** Todos los mosaicos con ruta (para armar las favoritas del Dashboard). */
export function todosLosTiles(): Tile[] {
  return [...ZONAS_INTERLOCUTORES, ...ZONAS_GESTIONES, ...ZONAS_DOCUMENTOS, ...ZONAS_ADMINISTRACION].flatMap((z) => z.tiles);
}
