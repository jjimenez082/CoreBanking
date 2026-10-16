/**
 * Definicion unica de la navegacion del Backoffice. La usan el sidebar
 * (Layout) y la busqueda global (modulos), para que agregar un modulo nuevo
 * sea solo agregarlo aqui — por ejemplo, Pricing o Motor de Reglas entran
 * como hijos del grupo "Administracion" con su propio permiso.
 */

export interface ItemNavegacion {
  ruta: string;
  etiqueta: string;
  icono: string;
  /** Si se define, el item solo se muestra a quien tenga este permiso (RBAC). */
  permiso?: string;
}

export interface GrupoNavegacion {
  etiqueta: string;
  icono: string;
  hijos: ItemNavegacion[];
}

export type EntradaNavegacion = ItemNavegacion | GrupoNavegacion;

export function esGrupo(entrada: EntradaNavegacion): entrada is GrupoNavegacion {
  return "hijos" in entrada;
}

export const NAVEGACION: EntradaNavegacion[] = [
  { ruta: "/", etiqueta: "Dashboard", icono: "▤" },
  { ruta: "/interlocutores", etiqueta: "Interlocutores", icono: "◐", permiso: "CLI_CONSULTA" },
  { ruta: "/gestiones", etiqueta: "Gestiones", icono: "☑" },
  {
    etiqueta: "Administración",
    icono: "⚙",
    hijos: [
      { ruta: "/seguridad", etiqueta: "Seguridad", icono: "🔒", permiso: "SEG_ADMINISTRACION" },
      { ruta: "/usuarios", etiqueta: "Usuarios", icono: "👤", permiso: "SEG_ADMINISTRACION" },
      { ruta: "/unidades", etiqueta: "Estructura organizativa", icono: "⌘", permiso: "ORG_ADMINISTRACION" },
      { ruta: "/listas-valores", etiqueta: "Listas de Valores", icono: "☰", permiso: "LOV_ACCESO" },
      { ruta: "/numeracion", etiqueta: "Rangos de numeración", icono: "#", permiso: "NUM_ADMINISTRACION" },
      { ruta: "/division-territorial", etiqueta: "División territorial", icono: "⌖", permiso: "DIV_ADMINISTRACION" },
      { ruta: "/integraciones", etiqueta: "Usuarios de integración", icono: "⇄", permiso: "INT_ADMINISTRACION" },
      { ruta: "/monedas", etiqueta: "Monedas", icono: "¤", permiso: "MON_ADMINISTRACION" },
      { ruta: "/tipo-cambio", etiqueta: "Tipo de cambio", icono: "⇅", permiso: "TC_CONSULTA" },
      { ruta: "/feriados", etiqueta: "Calendario de feriados", icono: "▦", permiso: "CAL_ADMINISTRACION" },
      { ruta: "/parametros", etiqueta: "Parámetros generales", icono: "≡", permiso: "PAR_ADMINISTRACION" },
      { ruta: "/auditoria", etiqueta: "Consulta de auditoría", icono: "◔", permiso: "AUD_CONSULTA" },
      // Futuros: Motor de Reglas, Pricing...
    ],
  },
];

/** Aplana la navegacion filtrando por permisos (para sidebar y busqueda). */
export function itemsVisibles(
  tienePermiso: (codigo: string) => boolean,
): { item: ItemNavegacion; grupo?: string }[] {
  const resultado: { item: ItemNavegacion; grupo?: string }[] = [];
  for (const entrada of NAVEGACION) {
    if (esGrupo(entrada)) {
      for (const hijo of entrada.hijos) {
        if (!hijo.permiso || tienePermiso(hijo.permiso)) {
          resultado.push({ item: hijo, grupo: entrada.etiqueta });
        }
      }
    } else if (!entrada.permiso || tienePermiso(entrada.permiso)) {
      resultado.push({ item: entrada });
    }
  }
  return resultado;
}
