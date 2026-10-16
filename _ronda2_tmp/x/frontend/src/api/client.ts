import axios from "axios";

/**
 * Cliente HTTP central hacia CoreBancario.Api. El token JWT se inyecta por
 * request desde el componente que lo use (ver hook useApiClient), no aqui,
 * porque el token vive en el estado de react-oidc-context, no en este
 * modulo estatico.
 */
export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL as string,
});

let tokenActual: string | undefined;

/** Lo llama useApiClient en cada render con el token vigente de la sesion. */
export function establecerToken(token: string | undefined) {
  tokenActual = token;
}

// Un unico interceptor de request para toda la vida de la aplicacion.
apiClient.interceptors.request.use((config) => {
  if (tokenActual) config.headers.Authorization = `Bearer ${tokenActual}`;
  return config;
});

/** Evento global: la API respondio 401 (token vencido o invalido). SesionGuardian lo atiende. */
export const EVENTO_SESION_VENCIDA = "cb:sesion-vencida";

apiClient.interceptors.response.use(
  (r) => r,
  (error) => {
    if (axios.isAxiosError(error) && error.response?.status === 401) {
      window.dispatchEvent(new Event(EVENTO_SESION_VENCIDA));
    }
    return Promise.reject(error);
  },
);
