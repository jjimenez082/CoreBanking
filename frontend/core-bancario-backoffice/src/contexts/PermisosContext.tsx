import { createContext, useContext, useEffect, useState, type PropsWithChildren } from "react";
import { useApiClient } from "../api/useApiClient";
import type { YoResponse } from "../api/tipos";

/**
 * Permisos efectivos (RBAC, SEG-002) del usuario autenticado, consultados una
 * vez via GET /api/yo y compartidos a toda la app. Esto es solo para decidir
 * que mostrar/ocultar en la UI — la API sigue siendo quien realmente hace
 * cumplir el permiso en cada endpoint (ver RequierePermisoFiltro en el
 * backend); nunca confiar solo en esto para "proteger" algo.
 */
interface PermisosContextValor {
  cargando: boolean;
  error: string | null;
  nombreDescriptivo: string | null;
  codigosPermiso: string[];
  tienePermiso: (codigo: string) => boolean;
}

const PermisosContext = createContext<PermisosContextValor>({
  cargando: true,
  error: null,
  nombreDescriptivo: null,
  codigosPermiso: [],
  tienePermiso: () => false,
});

export function PermisosProvider({ children }: PropsWithChildren) {
  const api = useApiClient();
  const [yo, setYo] = useState<YoResponse | null>(null);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelado = false;

    api
      .get<YoResponse>("/api/yo")
      .then((respuesta) => {
        if (!cancelado) setYo(respuesta.data);
      })
      .catch((err) => {
        if (!cancelado) setError(err.message);
      })
      .finally(() => {
        if (!cancelado) setCargando(false);
      });

    return () => {
      cancelado = true;
    };
  }, [api]);

  const codigosPermiso = yo?.codigosPermiso ?? [];

  const valor: PermisosContextValor = {
    cargando,
    error,
    nombreDescriptivo: yo?.nombreDescriptivo ?? null,
    codigosPermiso,
    tienePermiso: (codigo: string) => codigosPermiso.includes(codigo),
  };

  return <PermisosContext.Provider value={valor}>{children}</PermisosContext.Provider>;
}

export function usePermisos() {
  return useContext(PermisosContext);
}
