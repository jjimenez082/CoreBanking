import type { Zona } from "./aplicaciones";

/**
 * Aplicaciones de Administración (launchpad, igual que Interlocutores). Cada mosaico se muestra solo con su permiso.
 * Para sumar una opción administrativa (Motor de Reglas, Pricing...) basta agregar su mosaico en la zona que corresponda.
 */
export const ZONAS_ADMINISTRACION: Zona[] = [
  {
    titulo: "Seguridad y organización",
    tiles: [
      { titulo: "Seguridad", sub: "Roles y permisos", icono: "🔒", permiso: "SEG_ADMINISTRACION", ruta: "/seguridad" },
      { titulo: "Usuarios", sub: "Altas, roles y superior", icono: "👤", permiso: "SEG_ADMINISTRACION", ruta: "/usuarios" },
      { titulo: "Estructura organizativa", sub: "Unidades y sucursales", icono: "⌘", permiso: "ORG_ADMINISTRACION", ruta: "/unidades" },
      { titulo: "Usuarios de integración", sub: "Canal fijo por integración", icono: "⇄", permiso: "INT_ADMINISTRACION", ruta: "/integraciones" },
    ],
  },
  {
    titulo: "Catálogos y parámetros",
    tiles: [
      { titulo: "Listas de Valores", sub: "Combos y catálogos", icono: "☰", permiso: "LOV_ACCESO", ruta: "/listas-valores" },
      { titulo: "Rangos de numeración", sub: "Números de cliente, cuenta...", icono: "#", permiso: "NUM_ADMINISTRACION", ruta: "/numeracion" },
      { titulo: "División territorial", sub: "Provincias, cantones...", icono: "⌖", permiso: "DIV_ADMINISTRACION", ruta: "/division-territorial" },
      { titulo: "Parámetros generales", sub: "Valores de la compañía", icono: "≡", permiso: "PAR_ADMINISTRACION", ruta: "/parametros" },
    ],
  },
  {
    titulo: "Finanzas y calendario",
    tiles: [
      { titulo: "Monedas", sub: "Moneda base y decimales", icono: "¤", permiso: "MON_ADMINISTRACION", ruta: "/monedas" },
      { titulo: "Tipo de cambio", sub: "Compra, venta y valor contable", icono: "⇅", permiso: "TC_CONSULTA", ruta: "/tipo-cambio" },
      { titulo: "Calendario de feriados", sub: "Días hábiles por país", icono: "▦", permiso: "CAL_ADMINISTRACION", ruta: "/feriados" },
    ],
  },
  {
    titulo: "Control",
    tiles: [
      { titulo: "Consulta de auditoría", sub: "Quién cambió qué y cuándo", icono: "◔", permiso: "AUD_CONSULTA", ruta: "/auditoria" },
    ],
  },
];

/** Todos los permisos que habilitan alguna aplicación de Administración (el menú se muestra si tiene al menos uno). */
export const PERMISOS_ADMINISTRACION = Array.from(new Set(ZONAS_ADMINISTRACION.flatMap((z) => z.tiles.map((t) => t.permiso))));
