/**
 * Definicion unica de la navegacion del Backoffice. La usan el sidebar
 * (Layout) y la busqueda global (modulos), para que agregar un modulo nuevo
 * sea solo agregarlo aqui — por ejemplo, Pricing o Motor de Reglas entran
 * como hijos del grupo "Administracion" con su propio permiso.
 */

import { PERMISOS_ADMINISTRACION } from "./zonasAdministracion";

export interface ItemNavegacion {
  ruta: string;
  etiqueta: string;
  icono: string;
  /** Si se define, el item solo se muestra a quien tenga este permiso (RBAC). */
  permiso?: string;
  /** Se muestra si el usuario tiene al menos uno de estos permisos (launchpads). */
  permisosAlguno?: string[];
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
  { ruta: "/administracion", etiqueta: "Administración", icono: "⚙", permisosAlguno: PERMISOS_ADMINISTRACION },
];

/** Un item es visible si no pide permiso o el usuario tiene el permiso (o alguno de la lista). */
export function permitido(item: ItemNavegacion, tienePermiso: (codigo: string) => boolean): boolean {
  if (item.permisosAlguno) return item.permisosAlguno.some(tienePermiso);
  return !item.permiso || tienePermiso(item.permiso);
}

/** Aplana la navegacion filtrando por permisos (para sidebar y busqueda). */
export function itemsVisibles(
  tienePermiso: (codigo: string) => boolean,
): { item: ItemNavegacion; grupo?: string }[] {
  const resultado: { item: ItemNavegacion; grupo?: string }[] = [];
  for (const entrada of NAVEGACION) {
    if (esGrupo(entrada)) {
      for (const hijo of entrada.hijos) {
        if (permitido(hijo, tienePermiso)) {
          resultado.push({ item: hijo, grupo: entrada.etiqueta });
        }
      }
    } else if (permitido(entrada, tienePermiso)) {
      resultado.push({ item: entrada });
    }
  }
  return resultado;
}
