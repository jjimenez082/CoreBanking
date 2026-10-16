import { useCallback, useEffect, useRef, useState } from "react";
import { useAuth } from "react-oidc-context";
import { useLocation } from "react-router-dom";
import { useApiClient } from "./useApiClient";

/**
 * Estado "leida" de las notificaciones. Vive en la base de datos, por usuario (sec.NOTIFICACION_LEIDA),
 * asi que lo leido se conserva entre navegadores y dispositivos, y el servidor tambien lo marca al abrir
 * o resolver una gestion por cualquier camino. La campanita y la app Notificaciones se sincronizan con un evento.
 */
const EVENTO = "notificaciones-leidas-cambio";

export function useLeidas() {
  const auth = useAuth();
  const api = useApiClient();
  const location = useLocation();
  const sub = auth.user?.profile.sub ?? "anon";
  const [leidas, setLeidas] = useState<Set<string>>(new Set());
  const activo = useRef(true);

  const cargar = useCallback(async () => {
    if (!auth.isAuthenticated) return;
    try {
      const { data } = await api.get<string[]>("/api/notificaciones/leidas");
      if (activo.current) setLeidas(new Set(data));
    } catch {
      /* se conserva lo ultimo conocido */
    }
  }, [api, auth.isAuthenticated]);

  useEffect(() => {
    activo.current = true;
    void cargar();
    // Migracion unica: lo que este navegador tenia como leido en localStorage pasa a la base de datos.
    const claveVieja = `notificaciones-leidas:${sub}`;
    try {
      const crudo = localStorage.getItem(claveVieja);
      if (crudo && auth.isAuthenticated) {
        const claves = JSON.parse(crudo) as string[];
        if (claves.length)
          void api.post("/api/notificaciones/leidas", { claves }).then(() => {
            localStorage.removeItem(claveVieja);
            void cargar();
          });
        else localStorage.removeItem(claveVieja);
      }
    } catch {
      /* ignorar */
    }
    const alCambiar = () => void cargar();
    window.addEventListener(EVENTO, alCambiar);
    const t = window.setInterval(cargar, 60_000);
    return () => {
      activo.current = false;
      window.removeEventListener(EVENTO, alCambiar);
      window.clearInterval(t);
    };
  }, [cargar, sub, api, auth.isAuthenticated]);

  // Al navegar (p. ej. al abrir una gestion) el servidor pudo marcar avisos: se refresca.
  useEffect(() => {
    void cargar();
  }, [location.pathname, cargar]);

  const cambiar = useCallback(
    async (ruta: string, claves: string[], agregar: boolean) => {
      if (!claves.length) return;
      setLeidas((prev) => {
        const n = new Set(prev);
        claves.forEach((c) => (agregar ? n.add(c) : n.delete(c)));
        return n;
      });
      try {
        await api.post(ruta, { claves });
      } catch {
        /* el siguiente refresco corrige el estado */
      }
      window.dispatchEvent(new CustomEvent(EVENTO));
    },
    [api],
  );

  const marcarLeidas = useCallback((claves: string[]) => void cambiar("/api/notificaciones/leidas", claves, true), [cambiar]);
  const marcarNoLeidas = useCallback((claves: string[]) => void cambiar("/api/notificaciones/no-leidas", claves, false), [cambiar]);

  return { leidas, marcarLeidas, marcarNoLeidas };
}

/** Si la ruta de la notificacion es «/gestiones?ver=N», devuelve N: ese detalle se abre en un modal sin salir de la pantalla actual. */
export function idGestionDeRuta(ruta: string): number | null {
  const m = /^\/gestiones\?ver=(\d+)$/.exec(ruta);
  return m ? Number(m[1]) : null;
}
