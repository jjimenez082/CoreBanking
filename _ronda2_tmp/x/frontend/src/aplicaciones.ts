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
      { titulo: "Nuevo cliente", sub: "Persona Física", icono: "＋", permiso: "CLI_ALTA", ruta: "/interlocutores/clientes/nuevo" },
      { titulo: "Mis borradores", sub: "Altas sin terminar", icono: "✎", permiso: "CLI_ALTA", ruta: "/interlocutores/borradores" },
      { titulo: "Modificar cliente", sub: "Se elige desde la consulta", icono: "⟳", permiso: "CLI_EDICION", ruta: "/interlocutores/consulta" },
    ],
  },
  {
    titulo: "Prospectos",
    tiles: [{ titulo: "Nuevo prospecto", sub: "Próximamente", icono: "☆", permiso: "CLI_ALTA" }],
  },
  {
    titulo: "Beneficiarios",
    tiles: [{ titulo: "Nuevo beneficiario", sub: "Próximamente", icono: "♡", permiso: "CLI_ALTA" }],
  },
  {
    titulo: "Firmantes",
    tiles: [{ titulo: "Nuevo firmante", sub: "Próximamente", icono: "✍", permiso: "CLI_ALTA" }],
  },
];


export interface Aplicacion {
  titulo: string;
  detalle: string;
  ruta: string;
  permiso?: string;
}

export function catalogoAplicaciones(): Aplicacion[] {
  const apps: Aplicacion[] = [];
  for (const e of NAVEGACION) {
    if (esGrupo(e)) e.hijos.forEach((h) => apps.push({ titulo: h.etiqueta, detalle: e.etiqueta, ruta: h.ruta, permiso: h.permiso }));
    else apps.push({ titulo: e.etiqueta, detalle: "Módulo", ruta: e.ruta, permiso: e.permiso });
  }
  for (const z of ZONAS_ADMINISTRACION)
    for (const t of z.tiles)
      if (t.ruta) apps.push({ titulo: t.titulo, detalle: `Administración · ${z.titulo}`, ruta: t.ruta, permiso: t.permiso });
  for (const z of ZONAS_INTERLOCUTORES)
    for (const t of z.tiles)
      if (t.ruta) apps.push({ titulo: t.titulo, detalle: `Interlocutores · ${z.titulo}`, ruta: t.ruta, permiso: t.permiso });
  return apps;
}
