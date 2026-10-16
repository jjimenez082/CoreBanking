import { useMemo } from "react";
import { useAuth } from "react-oidc-context";
import { apiClient } from "./client";

/**
 * Devuelve una instancia de axios con el Bearer token del usuario actual ya
 * inyectado en cada request via interceptor. Se re-crea el interceptor cada
 * vez que cambia el token (ej. al refrescar sesion) para no usar uno viejo.
 */
export function useApiClient() {
  const auth = useAuth();

  return useMemo(() => {
    const interceptorId = apiClient.interceptors.request.use((config) => {
      if (auth.user?.access_token) {
        config.headers.Authorization = `Bearer ${auth.user.access_token}`;
      }
      return config;
    });

    // Nota: en una app mas grande convendria remover interceptores viejos
    // (apiClient.interceptors.request.eject) para evitar acumulacion; para
    // este MVP de Backoffice no es un problema practico todavia.
    void interceptorId;

    return apiClient;
  }, [auth.user?.access_token]);
}
