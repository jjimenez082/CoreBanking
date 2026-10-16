import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { useAuth } from "react-oidc-context";
import { Avatar } from "./Avatar";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import type { UsuarioAdmin } from "../api/tipos";
import { aplicacionActual } from "../aplicaciones";
import { useFavoritas } from "../api/useFavoritas";

type Vista = "usuario" | "sesion" | "app" | null;

const fmtFecha = (d: Date) => d.toLocaleString("es-CR", { dateStyle: "full", timeStyle: "medium" });

function Fila({ etiqueta, valor }: { etiqueta: string; valor: React.ReactNode }) {
  return (
    <div className="info-fila">
      <span className="info-fila__etiqueta">{etiqueta}</span>
      <span className="info-fila__valor">{valor ?? "—"}</span>
    </div>
  );
}

/** Menu del usuario (barra superior): informacion del usuario, informacion de la sesion y cierre de sesion. */
export function MenuUsuario() {
  const auth = useAuth();
  const api = useApiClient();
  const { codigosPermiso } = usePermisos();
  const [abierto, setAbierto] = useState(false);
  const [vista, setVista] = useState<Vista>(null);
  const [verPermisos, setVerPermisos] = useState(false);
  const location = useLocation();
  const { esFavorita, alternar } = useFavoritas();
  const appActual = aplicacionActual(location.pathname);
  const [perfil, setPerfil] = useState<UsuarioAdmin | null>(null);
  const [errorPerfil, setErrorPerfil] = useState<string | null>(null);
  const contenedor = useRef<HTMLDivElement>(null);

  const perfilToken = auth.user?.profile as Record<string, unknown> | undefined;
  const compania = perfilToken?.compania as string | undefined;
  const nombre = auth.user?.profile.name;

  useEffect(() => {
    function fuera(e: MouseEvent) {
      if (contenedor.current && !contenedor.current.contains(e.target as Node)) setAbierto(false);
    }
    function tecla(e: KeyboardEvent) {
      if (e.key === "Escape") { setAbierto(false); setVista(null); }
    }
    document.addEventListener("mousedown", fuera);
    document.addEventListener("keydown", tecla);
    return () => { document.removeEventListener("mousedown", fuera); document.removeEventListener("keydown", tecla); };
  }, []);

  async function abrirUsuario() {
    setAbierto(false);
    setVista("usuario");
    setVerPermisos(false);
    setErrorPerfil(null);
    try {
      setPerfil((await api.get<UsuarioAdmin>("/api/yo/perfil")).data);
    } catch (e) {
      setErrorPerfil(mensajeError(e, "No se pudieron cargar los datos del usuario."));
    }
  }

  function abrirApp() {
    setAbierto(false);
    setVista("app");
  }

  function abrirSesion() {
    setAbierto(false);
    setVista("sesion");
  }

  const u = auth.user;
  const inicioSesion = typeof perfilToken?.auth_time === "number" ? new Date((perfilToken.auth_time as number) * 1000)
    : typeof perfilToken?.iat === "number" ? new Date((perfilToken.iat as number) * 1000) : null;
  const venceToken = u?.expires_at ? new Date(u.expires_at * 1000) : null;
  const minutosRestantes = venceToken ? Math.max(0, Math.round((venceToken.getTime() - Date.now()) / 60000)) : null;

  return (
    <div className="menu-usuario" ref={contenedor}>
      <button type="button" className="app-topbar__usuario" aria-haspopup="menu" aria-expanded={abierto} onClick={() => setAbierto((a) => !a)}>
        <span className="app-topbar__usuario-info">
          <span className="app-topbar__usuario-nombre">{nombre}</span>
          {compania && <span className="app-topbar__usuario-compania">{compania}</span>}
        </span>
        <Avatar nombre={nombre} />
      </button>

      {abierto && (
        <div className="menu-usuario__panel" role="menu">
          <div className="menu-usuario__cabecera">
            <strong>{nombre}</strong>
            <span>{u?.profile.email}</span>
          </div>
          <button type="button" role="menuitem" onClick={() => void abrirUsuario()}>👤 Información del usuario</button>
          <button type="button" role="menuitem" onClick={abrirSesion}>🕒 Información de la sesión</button>
          <button type="button" role="menuitem" onClick={abrirApp}>ⓘ Acerca del APP</button>
          <button type="button" role="menuitem" onClick={() => void auth.signinRedirect({ extraQueryParams: { kc_action: "UPDATE_PASSWORD" } })}>🔑 Cambiar contraseña</button>
          <hr />
          <button type="button" role="menuitem" className="menu-usuario__salir" onClick={() => void auth.signoutRedirect()}>⏻ Cerrar sesión (Logoff)</button>
        </div>
      )}

      {vista && (
        <div className="sesion-modal" onMouseDown={(e) => { if (e.target === e.currentTarget) setVista(null); }}>
          <div className="sesion-modal__caja" style={{ minWidth: 380, maxWidth: 520 }} role="dialog" aria-modal="true">
            {vista === "usuario" ? (
              <>
                <h3>Información del usuario</h3>
                {errorPerfil && <p className="mensaje-error">{errorPerfil}</p>}
                {!perfil && !errorPerfil && <p>Cargando...</p>}
                {perfil && (
                  <div className="info-lista">
                    <Fila etiqueta="Nombre" valor={perfil.nombreCompleto} />
                    <Fila etiqueta="Usuario" valor={perfil.username} />
                    <Fila etiqueta="Correo" valor={perfil.correo} />
                    <Fila etiqueta="Compañía" valor={compania} />
                    <Fila etiqueta="Unidad" valor={perfil.unidadNombre} />
                    <Fila etiqueta="Superior directo" valor={perfil.superiorNombre} />
                    <Fila etiqueta="Roles" valor={perfil.codigosRol.length ? perfil.codigosRol.join(", ") : "Sin roles"} />
                    <Fila
                      etiqueta="Permisos"
                      valor={
                        <>
                          {codigosPermiso.length} permisos efectivos{" "}
                          <button type="button" className="boton-enlace" onClick={() => setVerPermisos((v) => !v)}>
                            {verPermisos ? "Ocultar" : "Ver permisos"}
                          </button>
                        </>
                      }
                    />
                    {verPermisos && (
                      <div className="info-permisos">
                        {codigosPermiso.length === 0
                          ? "No tiene permisos asignados; pida a un administrador que le asigne un rol."
                          : [...codigosPermiso].sort().map((c) => <code key={c}>{c}</code>)}
                      </div>
                    )}
                    <Fila etiqueta="Estado" valor={perfil.estado} />
                  </div>
                )}
                <p className="nota">Solo lectura. Para cambiar sus datos o roles, contacte a un administrador.</p>
              </>
            ) : vista === "app" ? (
              <>
                <h3>Acerca del APP</h3>
                {appActual ? (
                  <>
                    <div className="info-lista">
                      <Fila etiqueta="ID del APP" valor={<code>{appActual.id}</code>} />
                      <Fila etiqueta="Nombre" valor={appActual.titulo} />
                      <Fila etiqueta="Ruta" valor={location.pathname} />
                      <Fila etiqueta="Favorita" valor={esFavorita(appActual.id) ? "Sí (aparece en su Dashboard)" : "No"} />
                    </div>
                    <p className="nota">Indique este ID al contactar a soporte.</p>
                    <button type="button" className="boton-secundario" onClick={() => alternar(appActual.id)}>
                      {esFavorita(appActual.id) ? "★ Quitar de favoritas" : "☆ Marcar como favorita"}
                    </button>
                  </>
                ) : (
                  <p className="nota">Esta pantalla no tiene un ID de APP asignado.</p>
                )}
              </>
            ) : (
              <>
                <h3>Información de la sesión</h3>
                <div className="info-lista">
                  <Fila etiqueta="Inicio de sesión" valor={inicioSesion ? fmtFecha(inicioSesion) : "No disponible"} />
                  <Fila etiqueta="La sesión actual vence" valor={venceToken ? `${fmtFecha(venceToken)}${minutosRestantes !== null ? ` (en ${minutosRestantes} min; se renueva sola)` : ""}` : "No disponible"} />
                  <Fila etiqueta="Servidor (API)" valor={import.meta.env.VITE_API_BASE_URL as string} />
                  <Fila etiqueta="Proveedor de identidad" valor={import.meta.env.VITE_KEYCLOAK_AUTHORITY as string} />
                  <Fila etiqueta="Compañía (cliente)" valor={compania} />
                  <Fila etiqueta="Zona horaria" valor={Intl.DateTimeFormat().resolvedOptions().timeZone} />
                  <Fila etiqueta="Identificador de sesión" valor={(perfilToken?.sid as string | undefined) ?? "—"} />
                </div>
              </>
            )}
            <div style={{ display: "flex", justifyContent: "flex-end", gap: "0.5rem", marginTop: "1rem" }}>
              <button type="button" className="boton-secundario" onClick={() => setVista(null)}>Cerrar</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
