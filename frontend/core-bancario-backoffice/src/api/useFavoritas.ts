import { useCallback, useEffect, useState } from "react";
import { useAuth } from "react-oidc-context";

/**
 * APPs favoritas del usuario (por ID de APP). Viven en este navegador, por usuario, y se comparten entre
 * los mosaicos, "Acerca del APP" y el Dashboard mediante un evento, para que todos se actualicen al instante.
 */
const EVENTO = "apps-favoritas-cambio";

function leer(clave: string): string[] {
  try {
    const crudo = localStorage.getItem(clave);
    const v = crudo ? (JSON.parse(crudo) as unknown) : [];
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === "string") : [];
  } catch {
    return [];
  }
}

function guardar(clave: string, ids: string[]) {
  try {
    localStorage.setItem(clave, JSON.stringify(ids));
  } catch {
    /* sin almacenamiento: no se recuerdan las favoritas */
  }
  window.dispatchEvent(new CustomEvent(EVENTO, { detail: clave }));
}

export function useFavoritas() {
  const auth = useAuth();
  const clave = `apps-favoritas:${auth.user?.profile.sub ?? "anon"}`;
  const [favoritas, setFavoritas] = useState<string[]>(() => leer(clave));

  useEffect(() => {
    const alCambiar = (e: Event) => {
      if ((e as CustomEvent).detail === clave) setFavoritas(leer(clave));
    };
    setFavoritas(leer(clave));
    window.addEventListener(EVENTO, alCambiar);
    return () => window.removeEventListener(EVENTO, alCambiar);
  }, [clave]);

  const alternar = useCallback(
    (id: string) => {
      const actual = leer(clave);
      guardar(clave, actual.includes(id) ? actual.filter((x) => x !== id) : [...actual, id]);
    },
    [clave],
  );

  return { favoritas, esFavorita: (id?: string) => !!id && favoritas.includes(id), alternar };
}
