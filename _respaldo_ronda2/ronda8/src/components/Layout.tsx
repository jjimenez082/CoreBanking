import { useState, type PropsWithChildren } from "react";
import { Link, useLocation } from "react-router-dom";
import { Logo } from "./Logo";
import { MenuUsuario } from "./MenuUsuario";
import { BusquedaGlobal } from "./BusquedaGlobal";
import { CampanaNotificaciones } from "./CampanaNotificaciones";
import { BusquedaAplicaciones } from "./BusquedaAplicaciones";
import { SesionGuardian } from "./SesionGuardian";
import { usePermisos } from "../contexts/PermisosContext";
import { NAVEGACION, esGrupo, permitido, type ItemNavegacion } from "../navegacion";
import { ZONAS_ADMINISTRACION } from "../zonasAdministracion";

export function Layout({ children }: PropsWithChildren) {
  const location = useLocation();
  const { tienePermiso } = usePermisos();
  const [gruposAbiertos, setGruposAbiertos] = useState<Record<string, boolean>>({});

  // Solo se muestran los items para los que el usuario tiene permiso (RBAC,
  // SEG-002). La API igual lo exige; esto evita enlaces que terminarian en 403.
  const visible = (item: ItemNavegacion) => permitido(item, tienePermiso);

  // Breadcrumb: "Dashboard › [Grupo ›] Pantalla" (las subrutas de Interlocutores se resuelven aparte)
  let migas: string[] = [];
  const ruta = location.pathname;
  if (ruta.startsWith("/interlocutores")) {
    migas = ["Interlocutores"];
    if (ruta === "/interlocutores/consulta") migas.push("Consulta");
    else if (ruta === "/interlocutores/clientes/nuevo") migas.push("Clientes", "Nuevo cliente");
    else if (ruta.endsWith("/modificar")) migas.push("Clientes", "Modificar cliente");
    else if (ruta === "/interlocutores/borradores") migas.push("Mis borradores");
    else if (ruta !== "/interlocutores") migas.push("Consulta 360°");
  } else if (ruta.startsWith("/administracion") || ZONAS_ADMINISTRACION.some((z) => z.tiles.some((t) => t.ruta === ruta))) {
    migas = ["Administración"];
    const tile = ZONAS_ADMINISTRACION.flatMap((z) => z.tiles).find((t) => t.ruta === ruta);
    if (tile) migas.push(tile.titulo);
  } else {
    for (const entrada of NAVEGACION) {
      if (esGrupo(entrada)) {
        const hijo = entrada.hijos.find((h) => h.ruta === ruta);
        if (hijo) migas = [entrada.etiqueta, hijo.etiqueta];
      } else if (entrada.ruta === ruta && entrada.ruta !== "/") {
        migas = [entrada.etiqueta];
      }
    }
  }

  function enlace(item: ItemNavegacion) {
    return (
      <Link
        key={item.ruta}
        to={item.ruta}
        className={location.pathname === item.ruta || (item.ruta !== "/" && location.pathname.startsWith(item.ruta + "/")) ? "activo" : ""}
      >
        <span className="app-sidebar__icono">{item.icono}</span>
        {item.etiqueta}
      </Link>
    );
  }

  return (
    <div className="app-shell">
      <aside className="app-sidebar">
        <div className="app-sidebar__marca">
          <Logo tamano="sm" subtitulo="BACKOFFICE" />
        </div>

        <div className="app-sidebar__seccion">Módulos</div>
        <nav className="app-sidebar__nav">
          {NAVEGACION.map((entrada) => {
            if (!esGrupo(entrada)) {
              return visible(entrada) ? enlace(entrada) : null;
            }

            const hijosVisibles = entrada.hijos.filter(visible);
            // Si el usuario no tiene permiso para ningun hijo, el grupo completo se oculta.
            if (hijosVisibles.length === 0) return null;

            const contieneRutaActual = hijosVisibles.some((h) => h.ruta === location.pathname);
            const abierto = gruposAbiertos[entrada.etiqueta] ?? contieneRutaActual;

            return (
              <div key={entrada.etiqueta}>
                <button
                  type="button"
                  className={`app-sidebar__grupo-titulo ${contieneRutaActual ? "activo" : ""}`}
                  aria-expanded={abierto}
                  onClick={() =>
                    setGruposAbiertos((previo) => ({ ...previo, [entrada.etiqueta]: !abierto }))
                  }
                >
                  <span className="app-sidebar__icono">{entrada.icono}</span>
                  {entrada.etiqueta}
                  <span className="app-sidebar__chevron">{abierto ? "▾" : "▸"}</span>
                </button>
                {abierto && <div className="app-sidebar__subnav">{hijosVisibles.map(enlace)}</div>}
              </div>
            );
          })}
        </nav>
      </aside>

      <div className="app-main">
        <header className="app-topbar">
          <div className="app-topbar__breadcrumb">
            <span>Dashboard</span>
            {migas.map((miga, indice) => (
              <span key={`${indice}-${miga}`} className="app-topbar__miga">
                <span className="app-topbar__separador">›</span>{" "}
                {indice === migas.length - 1 ? <strong>{miga}</strong> : miga}
              </span>
            ))}
          </div>

          <BusquedaGlobal />

          <div className="app-topbar__derecha">
          <BusquedaAplicaciones />
          <CampanaNotificaciones />

          <MenuUsuario />
          </div>
        </header>

        <main className="app-content">{children}</main>
        <SesionGuardian />
      </div>
    </div>
  );
}
