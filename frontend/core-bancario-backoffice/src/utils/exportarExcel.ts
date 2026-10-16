import writeXlsxFile from "write-excel-file/browser";

/**
 * Descarga un archivo .xlsx con una hoja: encabezado en negrita y fijo, anchos según el contenido.
 * Todo se escribe como texto (las fechas ya vienen formateadas) para que Excel no cambie los valores
 * (por ejemplo números de cliente con ceros a la izquierda).
 */
export async function exportarExcel(nombreArchivo: string, hoja: string, encabezados: string[], filas: string[][]): Promise<void> {
  const data = [
    encabezados.map((h) => ({ value: h, fontWeight: "bold" as const, backgroundColor: "#e9ecf8" })),
    ...filas.map((f) => f.map((v) => ({ value: v ?? "", type: String }))),
  ];
  const anchos = encabezados.map((h, i) => {
    const mayor = filas.reduce((m, f) => Math.max(m, (f[i] ?? "").length), h.length);
    return { width: Math.min(60, Math.max(10, mayor + 2)) };
  });
  const sello = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  const archivo = `${nombreArchivo}_${sello.getFullYear()}${p(sello.getMonth() + 1)}${p(sello.getDate())}_${p(sello.getHours())}${p(sello.getMinutes())}.xlsx`;
  await writeXlsxFile(data, { sheet: hoja.slice(0, 30), columns: anchos, stickyRowsCount: 1 }).toFile(archivo);
}
