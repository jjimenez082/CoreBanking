import { isAxiosError } from "axios";
import type { ApiErrorBody } from "./tipos";

/** Mensaje legible de un error de la API (el cuerpo trae `mensaje` en los errores de negocio). */
export function mensajeError(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

export const fmtFecha = (iso?: string | null) => (iso ? new Date(iso.length <= 10 ? `${iso}T00:00:00` : iso).toLocaleDateString("es-CR") : "—");
export const fmtFechaHora = (iso?: string | null) => (iso ? new Date(iso.endsWith("Z") ? iso : `${iso}Z`).toLocaleString("es-CR") : "—");
export const hoyIso = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
