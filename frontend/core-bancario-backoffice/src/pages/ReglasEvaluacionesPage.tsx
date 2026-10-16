import { useCallback, useEffect, useMemo, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import { VentanaModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { ResultadoReglas } from "../components/TrazaReglas";
import { fmtFechaHora, mensajeError } from "../api/mensajes";
import type { EvaluacionDetalle, EvaluacionFila, EvaluacionResultado, TrazaRegla } from "../api/reglas";

/** Bitácora de evaluaciones reales (RUL_CONSULTA): qué se evaluó, con qué datos y por qué se llegó al resultado. */
export function ReglasEvaluacionesPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<EvaluacionFila[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detalle, setDetalle] = useState<EvaluacionDetalle | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { setFilas((await api.get<EvaluacionFila[]>("/api/reglas/evaluaciones")).data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudo cargar la bitácora.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  async function abrir(id: number) {
    setError(null);
    try { setDetalle((await api.get<EvaluacionDetalle>(`/api/reglas/evaluaciones/${id}`)).data); }
    catch (e) { setError(mensajeError(e, "No se pudo abrir la evaluación.")); }
  }

  const columnas: ColumnaTabla<EvaluacionFila>[] = useMemo(() => [
    { clave: "id", encabezado: "#", render: (r) => <button type="button" className="boton-enlace-id" onClick={() => void abrir(r.idEvaluacion)}>{r.idEvaluacion}</button>, valorOrden: (r) => r.idEvaluacion },
    { clave: "fecha", encabezado: "Fecha", render: (r) => fmtFechaHora(r.evaluadoEn), valorOrden: (r) => r.evaluadoEn },
    { clave: "conj", encabezado: "Conjunto", render: (r) => `${r.conjuntoCodigo} v${r.numeroVersion}`, textoBusqueda: (r) => r.conjuntoCodigo, filtroLista: true },
    { clave: "ref", encabezado: "Referencia", render: (r) => r.referencia ?? "—", textoBusqueda: (r) => r.referencia ?? "" },
    { clave: "dec", encabezado: "Decisión", render: (r) => r.decision ?? "Sin decisión", textoBusqueda: (r) => r.decision ?? "Sin decisión", filtroLista: true },
    { clave: "regla", encabezado: "Regla aplicada", render: (r) => r.reglaAplicada ?? "—", textoBusqueda: (r) => r.reglaAplicada ?? "" },
    { clave: "por", encabezado: "Evaluó", render: (r) => r.evaluadoPor, textoBusqueda: (r) => r.evaluadoPor },
  ], []);

  let resultado: EvaluacionResultado | null = null;
  if (detalle) {
    try {
      const r = JSON.parse(detalle.resultado) as Partial<EvaluacionResultado>;
      const traza = detalle.traza ? (JSON.parse(detalle.traza) as TrazaRegla[]) : [];
      resultado = { decision: r.decision ?? null, asignaciones: r.asignaciones ?? {}, mensajes: r.mensajes ?? [], reglasAplicadas: r.reglasAplicadas ?? [], coincidioAlguna: (r.reglasAplicadas ?? []).length > 0, traza };
    } catch { resultado = null; }
  }

  return (
    <div>
      <h1>Evaluaciones</h1>
      <p className="nota">Cada vez que un proceso (solicitud, crédito...) consulta una regla se guarda aquí con los datos usados y la explicación del resultado. Las pruebas del simulador no se guardan. Se muestran las últimas 200.</p>
      {error && <p className="mensaje-error">{error}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idEvaluacion} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay evaluaciones registradas." filtrosPorColumna
        comandos={[{ clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() }]} />
      {detalle && (
        <VentanaModal titulo={`Evaluación #${detalle.fila.idEvaluacion} · ${detalle.fila.conjuntoCodigo} v${detalle.fila.numeroVersion}`} onCerrar={() => setDetalle(null)} ancho={860}>
          <p className="nota">{fmtFechaHora(detalle.fila.evaluadoEn)} · {detalle.fila.evaluadoPor}{detalle.fila.referencia ? ` · ${detalle.fila.referencia}` : ""}</p>
          {resultado ? <ResultadoReglas r={resultado} /> : <p className="mensaje-error">No se pudo interpretar el resultado guardado.</p>}
          <h4 style={{ margin: "0.8rem 0 0.3rem" }}>Datos usados</h4>
          <pre style={{ background: "#f6f7fb", padding: "0.6rem", borderRadius: 6, overflow: "auto", maxHeight: 220 }}>{formatear(detalle.entrada)}</pre>
        </VentanaModal>
      )}
    </div>
  );
}

function formatear(json: string): string {
  try { return JSON.stringify(JSON.parse(json), null, 2); } catch { return json; }
}
