import { useAuth } from "react-oidc-context";
import { apiClient, establecerToken } from "./client";

/**
 * Devuelve el cliente HTTP central. El Bearer token se inyecta con UN SOLO interceptor (ver client.ts) que lee
 * siempre el token vigente: asi, cuando la renovacion silenciosa cambia el token, el siguiente request ya usa el nuevo.
 * (Antes se agregaba un interceptor por cada token y el mas viejo, con el token vencido, pisaba al nuevo:
 * por eso la sesion "vencia" en cualquier momento aunque hubiera actividad.)
 */
export function useApiClient() {
  const auth = useAuth();
  establecerToken(auth.user?.access_token);
  return apiClient;
}
