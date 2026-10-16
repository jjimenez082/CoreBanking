import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import type { MonedaAdmin } from "../api/tipos";

interface FormMoneda { codigo: string; nombre: string; simbolo: string; decimales: number }
const NUEVA: FormMoneda = { codigo: "", nombre: "", simbolo: "", decimales: 2 };

/**
 * Administración › Monedas (permiso MON_ADMINISTRACION). Catálogo de monedas (ISO 4217) y moneda base de la compañía.
 * Los tipos de cambio se cotizan contra la moneda base. Todo cambio queda en la auditoría.
 */
export function MonedasPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<MonedaAdmin[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nueva" | "editar" | null>(null);
  const [form, setForm] = useState<FormMoneda>(NUEVA);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { const r = await api.get<MonedaAdmin[]>("/api/monedas"); setFilas(r.data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar las monedas.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<MonedaAdmin>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Nombre", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "simbolo", encabezado: "Símbolo", render: (r) => r.simbolo },
    { clave: "dec", encabezado: "Decimales", render: (r) => r.decimales },
    { clave: "base", encabezado: "Moneda base", render: (r) => (r.esBase ? "Sí" : "") },
    { clave: "tc", encabezado: "Tipos de cambio", render: (r) => r.cantidadTiposCambio },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const m = filas.find((f) => f.idMoneda === idSel) ?? null;

  async function accion(fn: () => Promise<unknown>, ok: string) {
    setExito(null); setError(null);
    try { await fn(); setExito(ok); await cargar(); }
    catch (e) { setError(mensajeError(e, "No se pudo completar la acción.")); }
  }

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    try {
      if (panel === "nueva") await api.post("/api/monedas", form);
      else if (m) await api.put(`/api/monedas/${m.idMoneda}`, form);
      setExito(panel === "nueva" ? "Moneda creada." : "Moneda actualizada.");
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  return (
    <div>
      <h1>Monedas</h1>
      <p className="nota">
        Catálogo de monedas de la compañía. La moneda base es contra la que se cotizan los tipos de cambio; solo puede cambiarse
        mientras no haya tipos de cambio cargados. Los cambios se registran en la auditoría.
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idMoneda}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay monedas. Use «Nueva» (la primera queda como moneda base)."
        comandos={[
          { clave: "nueva", etiqueta: "Nueva", icono: "+", onClick: () => { setExito(null); setErrorForm(null); setForm(NUEVA); setPanel("nueva"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: () => { if (!m) return; setExito(null); setErrorForm(null); setForm({ codigo: m.codigo, nombre: m.nombre, simbolo: m.simbolo, decimales: m.decimales }); setPanel("editar"); } },
          { clave: "base", etiqueta: "Definir como base", icono: "★", requiereSeleccion: "una", deshabilitado: !m || m.esBase || m.estado !== "Activo", onClick: () => m && void accion(() => api.put(`/api/monedas/${m.idMoneda}/base`), `${m.codigo} es ahora la moneda base.`) },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: m?.estado === "Activo", onClick: () => m && void accion(() => api.put(`/api/monedas/${m.idMoneda}/estado`, { estado: "Activo" }), "Moneda activada.") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: m?.estado === "Inactivo", onClick: () => m && void accion(() => api.put(`/api/monedas/${m.idMoneda}/estado`, { estado: "Inactivo" }), "Moneda inactivada.") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nueva" ? "Nueva moneda" : `Editar ${m?.codigo}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código ISO (3 letras)
              <input type="text" maxLength={3} value={form.codigo} disabled={panel === "editar"}
                onChange={(e) => setForm({ ...form, codigo: e.target.value.toUpperCase() })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={100} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            <label>Símbolo
              <input type="text" maxLength={5} value={form.simbolo} onChange={(e) => setForm({ ...form, simbolo: e.target.value })} required />
            </label>
            <label>Decimales
              <input type="number" min={0} max={6} value={form.decimales} onChange={(e) => setForm({ ...form, decimales: Number(e.target.value) })} required />
            </label>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}
