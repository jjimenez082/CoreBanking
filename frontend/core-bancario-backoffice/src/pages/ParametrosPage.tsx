import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { fmtFechaHora, mensajeError } from "../api/mensajes";
import type { ParametroFila } from "../api/tipos";

/**
 * Administración › Parámetros generales (permiso PAR_ADMINISTRACION). Se cambia el valor de cada parámetro
 * (validado según su tipo); se puede restablecer el valor por defecto. Todo cambio queda en la auditoría.
 */
export function ParametrosPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<ParametroFila[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [editando, setEditando] = useState(false);
  const [valor, setValor] = useState("");
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { const r = await api.get<ParametroFila[]>("/api/parametros"); setFilas(r.data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar los parámetros.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<ParametroFila>[] = useMemo(() => [
    { clave: "cat", encabezado: "Categoría", render: (r) => r.categoria, textoBusqueda: (r) => r.categoria },
    { clave: "nombre", encabezado: "Parámetro", render: (r) => <span title={r.descripcion ?? ""}>{r.nombre}</span>, textoBusqueda: (r) => `${r.nombre} ${r.codigo}` },
    { clave: "codigo", encabezado: "Código", render: (r) => <code>{r.codigo}</code>, textoBusqueda: (r) => r.codigo },
    { clave: "valor", encabezado: "Valor", render: (r) => <strong>{r.valor}</strong> },
    { clave: "def", encabezado: "Por defecto", render: (r) => (r.esDefecto ? "✓" : r.valorDefecto) },
    { clave: "mod", encabezado: "Última modificación", render: (r) => (r.modificadoPor ? `${r.modificadoPor} · ${fmtFechaHora(r.modificadoEn)}` : "—") },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const p = filas.find((f) => f.idParametro === idSel) ?? null;

  async function guardar(e: FormEvent) {
    e.preventDefault();
    if (!p) return;
    setGuardando(true); setErrorForm(null);
    try { await api.put(`/api/parametros/${p.idParametro}`, { valor }); setExito("Parámetro actualizado."); setEditando(false); await cargar(); }
    catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  async function restablecer() {
    if (!p) return;
    setExito(null); setError(null);
    try { await api.post(`/api/parametros/${p.idParametro}/restablecer`); setExito("Valor restablecido."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo restablecer.")); }
  }

  return (
    <div>
      <h1>Parámetros generales</h1>
      <p className="nota">Parámetros de la compañía. Pase el cursor sobre el nombre para ver qué controla. Los cambios se registran en la auditoría.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idParametro}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay parámetros. Corra administracion_transversal.sql."
        comandos={[
          { clave: "editar", etiqueta: "Cambiar valor", icono: "✎", requiereSeleccion: "una", onClick: () => { if (!p) return; setExito(null); setErrorForm(null); setValor(p.valor); setEditando(true); } },
          { clave: "restablecer", etiqueta: "Restablecer", icono: "↺", requiereSeleccion: "una", deshabilitado: p?.esDefecto, onClick: () => void restablecer() },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
      {editando && p && (
        <PanelModal onCerrar={() => setEditando(false)}>
          <h3>{p.nombre}</h3>
          {p.descripcion && <p className="nota">{p.descripcion}</p>}
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Valor ({p.tipoDato})
              {p.tipoDato === "Booleano" ? (
                <select value={valor} onChange={(e) => setValor(e.target.value)}>
                  <option value="true">Sí</option><option value="false">No</option>
                </select>
              ) : (
                <input type="text" maxLength={500} value={valor} onChange={(e) => setValor(e.target.value)} required />
              )}
            </label>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setEditando(false)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}
