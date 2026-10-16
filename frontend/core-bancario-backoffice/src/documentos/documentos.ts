import type { AxiosInstance } from "axios";
import type { DocumentoApi, DocumentoOperacion, SolicitudDocumentos } from "../api/tipos";

export function fmtTamano(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Etiqueta y clase del estado de vigencia de un documento (vencimiento calculado por el servidor). */
export function etiquetaVigencia(d: Pick<DocumentoApi, "estado" | "estadoVigencia" | "diasParaVencer">): { texto: string; clase: string } | null {
  if (d.estado === "EnGestion") return { texto: "Pendiente de aprobación", clase: "docs-chip docs-chip--aviso" };
  if (d.estado === "Obsoleto") return { texto: "Obsoleto", clase: "docs-chip docs-chip--obsoleto" };
  if (d.estadoVigencia === "Vencido") return { texto: "Vencido", clase: "docs-chip docs-chip--vencido" };
  if (d.estadoVigencia === "PorVencer") return { texto: `Vence en ${d.diasParaVencer} día(s)`, clase: "docs-chip docs-chip--aviso" };
  return null;
}

/** Descarga un documento: se pide con el token (no es un enlace directo) y se entrega como archivo. */
export async function descargarDocumento(api: AxiosInstance, d: Pick<DocumentoApi, "idDocumento" | "nombreArchivo">, idVersion?: number): Promise<void> {
  const r = await api.get<Blob>(`/api/documentos/${d.idDocumento}/descargar`, { responseType: "blob", params: idVersion ? { idVersion } : undefined });
  const url = URL.createObjectURL(r.data);
  const a = document.createElement("a");
  a.href = url;
  a.download = d.nombreArchivo;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/** Encabezado que liga los adjuntos ya subidos a la gestion que se esta creando. */
export const encabezadoAdjuntos = (ids: number[]) => (ids.length > 0 ? { headers: { "X-Adjuntos": ids.join(",") } } : undefined);

export interface DatosTemporal { emision?: string; vencimiento?: string; descripcion?: string; claves?: string }

/** Sube un archivo como temporal (aun no es parte del expediente: se liga a la gestion al solicitarla). */
export async function subirTemporal(api: AxiosInstance, archivo: File, idTipo: number, d: DatosTemporal = {}): Promise<DocumentoApi> {
  const fd = new FormData();
  fd.append("archivo", archivo);
  fd.append("idTipoDocumento", String(idTipo));
  fd.append("descripcion", d.descripcion ?? "");
  fd.append("claves", d.claves ?? "");
  fd.append("fechaEmision", d.emision ?? "");
  fd.append("fechaVencimiento", d.vencimiento ?? "");
  return (await api.post<DocumentoApi>("/api/documentos/temporales", fd)).data;
}

/** Quita temporales que no llegaron a una gestion (limpieza silenciosa). */
export async function quitarTemporales(api: AxiosInstance, ids: number[]): Promise<void> {
  await Promise.allSettled(ids.map((id) => api.delete(`/api/documentos/temporales/${id}`)));
}

/** Pide la gestion de documentos del expediente (agregar / reemplazar / obsoletar). Si la regla es automatica queda aplicada al instante. */
export async function solicitarDocumentos(api: AxiosInstance, idInterlocutor: number, operaciones: DocumentoOperacion[], idsTemporales: number[]): Promise<SolicitudDocumentos> {
  return (await api.post<SolicitudDocumentos>("/api/documentos/solicitud", { idInterlocutor, operaciones }, encabezadoAdjuntos(idsTemporales))).data;
}

export const mensajeSolicitud = (s: SolicitudDocumentos) =>
  s.aplicada ? `Gestión ${s.numero}: aprobada y aplicada automáticamente.` : `Gestión ${s.numero} creada: queda pendiente de aprobación.`;
