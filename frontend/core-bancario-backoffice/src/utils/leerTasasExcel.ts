import { readSheet } from "read-excel-file/browser";

/** Una fila del Excel de carga de tasas de referencia, ya interpretada y validada en el navegador. */
export interface FilaTasaExcel {
  fila: number;            // número de fila en el Excel (la 1 es el encabezado)
  codigo: string;
  fecha: string | null;    // aaaa-mm-dd
  valor: number | null;
  comentario: string;
  errores: string[];
}

export const MAX_FILAS_TASAS = 500;
const norm = (s: unknown) => String(s ?? "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]/g, "");
const p2 = (n: number) => String(n).padStart(2, "0");

function fechaValida(a: number, m: number, d: number): string | null {
  const dt = new Date(Date.UTC(a, m - 1, d));
  if (dt.getUTCFullYear() !== a || dt.getUTCMonth() !== m - 1 || dt.getUTCDate() !== d || a < 1990 || a > 2100) return null;
  return `${a}-${p2(m)}-${p2(d)}`;
}
function leerFecha(v: unknown): string | null {
  if (v instanceof Date) return fechaValida(v.getUTCFullYear(), v.getUTCMonth() + 1, v.getUTCDate()); // Excel entrega la fecha a medianoche UTC
  if (typeof v === "number") { const d = new Date(Date.UTC(1899, 11, 30) + Math.round(v) * 86400000); return fechaValida(d.getUTCFullYear(), d.getUTCMonth() + 1, d.getUTCDate()); }
  const t = String(v ?? "").trim();
  let m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(t); if (m) return fechaValida(+m[1], +m[2], +m[3]);
  m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(t); if (m) return fechaValida(+m[3], +m[2], +m[1]);
  return null;
}
function leerValor(v: unknown): number | null {
  const n = typeof v === "number" ? v : Number(String(v ?? "").trim().replace(/\s/g, "").replace("%", "").replace(",", "."));
  return Number.isFinite(n) && String(v ?? "").trim() !== "" ? n : null;
}

/** Lee la primera hoja del .xlsx (columnas: Codigo_indicador, Fecha_vigencia, Valor_porcentaje, Comentario) y valida cada fila. */
export async function leerTasasExcel(archivo: File, codigosActivos: Set<string>): Promise<{ filas: FilaTasaExcel[]; errorGeneral?: string }> {
  let datos: unknown[][];
  try { datos = (await readSheet(archivo)) as unknown[][]; }
  catch { return { filas: [], errorGeneral: "No se pudo leer el archivo. Verifique que sea un .xlsx válido (use la plantilla)." }; }
  if (datos.length === 0) return { filas: [], errorGeneral: "El archivo está vacío." };
  const enc = datos[0].map(norm);
  const col = { codigo: enc.indexOf("codigoindicador"), fecha: enc.indexOf("fechavigencia"), valor: enc.indexOf("valorporcentaje"), comentario: enc.indexOf("comentario") };
  if (col.codigo < 0 || col.fecha < 0 || col.valor < 0)
    return { filas: [], errorGeneral: "No se encontraron los encabezados esperados: Codigo_indicador, Fecha_vigencia, Valor_porcentaje (y opcional Comentario). Use la plantilla." };

  const filas: FilaTasaExcel[] = [];
  const vistos = new Map<string, number>();
  datos.slice(1).forEach((r, i) => {
    const vacia = [col.codigo, col.fecha, col.valor, col.comentario].every((c) => c < 0 || r[c] == null || String(r[c]).trim() === "");
    if (vacia) return;
    const numero = i + 2;
    const codigo = String(r[col.codigo] ?? "").trim().toUpperCase();
    const fecha = leerFecha(r[col.fecha]);
    const valor = leerValor(r[col.valor]);
    const comentario = col.comentario >= 0 ? String(r[col.comentario] ?? "").trim() : "";
    const errores: string[] = [];
    if (!codigo) errores.push("Falta el código del indicador.");
    else if (!codigosActivos.has(codigo)) errores.push("El indicador no existe o está inactivo.");
    if (!fecha) errores.push("Fecha inválida (use aaaa-mm-dd o dd/mm/aaaa).");
    if (valor == null) errores.push("Valor inválido (número).");
    else if (valor < 0 || valor > 1000) errores.push("El valor debe estar entre 0 y 1000.");
    else if (Math.round(valor * 1e8) / 1e8 !== valor) errores.push("Máximo 8 decimales.");
    if (comentario.length > 500) errores.push("El comentario excede 500 caracteres.");
    if (codigo && fecha) {
      const llave = `${codigo}|${fecha}`;
      const previa = vistos.get(llave);
      if (previa) errores.push(`Repetida (ya viene en la fila ${previa}).`); else vistos.set(llave, numero);
    }
    filas.push({ fila: numero, codigo, fecha, valor, comentario, errores });
  });
  if (filas.length === 0) return { filas, errorGeneral: "El archivo no trae filas con datos." };
  if (filas.length > MAX_FILAS_TASAS) return { filas: [], errorGeneral: `El archivo trae ${filas.length} filas; el máximo por carga es ${MAX_FILAS_TASAS}.` };
  return { filas };
}
