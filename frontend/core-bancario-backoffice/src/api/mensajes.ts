import { isAxiosError } from "axios";
import type { ApiErrorBody } from "./tipos";

/** Mensaje legible de un error de la API (el cuerpo trae `mensaje` en los errores de negocio). */
export function mensajeError(err: unknown, porDefecto: string): string {
  if (isAxiosError(err)) {
    const d = err.response?.data as (ApiErrorBody & { errors?: Record<string, string[]>; title?: string; detail?: string }) | undefined;
    if (d?.mensaje) return d.mensaje;
    // Errores de validacion/binding del framework (ProblemDetails): campo + detalle.
    if (d?.errors) {
      const txt = Object.entries(d.errors).map(([campo, msgs]) => `${campo.replace(/^\$\./, "")}: ${msgs.join(" ")}`).join("\n");
      if (txt) return `Datos inválidos — ${txt}`;
    }
    if (d?.detail || d?.title) return d.detail ?? d.title ?? porDefecto;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

/** Numero escrito por una persona ("520,50", "1.234,56", "1,234.56") -> number, o null si no es valido. */
export function parseDecimal(texto: string): number | null {
  let s = texto.trim().replace(/\s/g, "");
  if (!s) return null;
  const c = s.lastIndexOf(",");
  const p = s.lastIndexOf(".");
  if (c >= 0 && p >= 0) s = c > p ? s.replace(/\./g, "").replace(",", ".") : s.replace(/,/g, "");
  else if (c >= 0) s = s.replace(",", ".");
  return /^\d+(\.\d+)?$/.test(s) ? Number(s) : null;
}

export const fmtFecha = (iso?: string | null) => (iso ? new Date(iso.length <= 10 ? `${iso}T00:00:00` : iso).toLocaleDateString("es-CR") : "—");
/** Fecha (sin hora) de un instante guardado en UTC, en la zona del usuario que consulta. */
export const fmtFechaUtc = (iso?: string | null) => (iso ? new Date(iso.endsWith("Z") ? iso : `${iso}Z`).toLocaleDateString("es-CR") : "—");
export const fmtFechaHora = (iso?: string | null) => (iso ? new Date(iso.endsWith("Z") ? iso : `${iso}Z`).toLocaleString("es-CR") : "—");
export const hoyIso = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
