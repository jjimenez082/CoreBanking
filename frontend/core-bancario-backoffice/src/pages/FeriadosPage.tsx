import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { fmtFecha, hoyIso, mensajeError } from "../api/mensajes";
import type { CatalogoItemResponse, FeriadoFila } from "../api/tipos";

interface FormFeriado { idPais: number; fecha: string; nombre: string; esRecurrente: boolean }

/**
 * Administración › Calendario de feriados (permiso CAL_ADMINISTRACION). Feriados por país; los días no hábiles de la
 * semana se definen en Parámetros generales (DIAS_NO_HABILES). Cuentas y créditos usarán este calendario.
 */
export function FeriadosPage() {
  const api = useApiClient();
  const [paises, setPaises] = useState<CatalogoItemResponse[]>([]);
  const [idPais, setIdPais] = useState(0);
  const [anio, setAnio] = useState(new Date().getFullYear());
  const [filas, setFilas] = useState<FeriadoFila[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState<FormFeriado>({ idPais: 0, fecha: hoyIso(), nombre: "", esRecurrente: false });
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  // Verificador de dia habil
  const [vFecha, setVFecha] = useState(hoyIso());
  const [vRes, setVRes] = useState<string | null>(null);

  useEffect(() => {
    api.get<CatalogoItemResponse[]>("/api/catalogos/listas/PAIS").then((r) => {
      setPaises(r.data);
      setIdPais((p) => p || r.data.find((x) => x.codigo === "CR")?.id || r.data[0]?.id || 0);
    }).catch((e) => setError(mensajeError(e, "No se pudo cargar la lista de países.")));
  }, [api]);

  const cargar = useCallback(async () => {
    if (!idPais) return;
    setCargando(true); setError(null);
    try {
      const r = await api.get<FeriadoFila[]>("/api/feriados", { params: { idPais, anio } });
      setFilas(r.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los feriados.")); }
    finally { setCargando(false); }
  }, [api, idPais, anio]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<FeriadoFila>[] = useMemo(() => [
    { clave: "fecha", encabezado: "Fecha", render: (r) => (r.esRecurrente ? `${String(new Date(r.fecha + "T00:00:00").getDate()).padStart(2, "0")}/${String(new Date(r.fecha + "T00:00:00").getMonth() + 1).padStart(2, "0")} (cada año)` : fmtFecha(r.fecha)), textoBusqueda: (r) => r.fecha },
    { clave: "nombre", encabezado: "Feriado", render: (r) => <span style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}>{r.nombre}</span>, textoBusqueda: (r) => r.nombre },
    { clave: "recu", encabezado: "Se repite", render: (r) => (r.esRecurrente ? "Sí" : "No") },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const f = filas.find((x) => x.idFeriado === idSel) ?? null;

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    try {
      if (panel === "nuevo") await api.post("/api/feriados", form);
      else if (f) await api.put(`/api/feriados/${f.idFeriado}`, form);
      setExito(panel === "nuevo" ? "Feriado creado." : "Feriado actualizado.");
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  async function estado(e: "Activo" | "Inactivo") {
    if (!f) return;
    setExito(null); setError(null);
    try { await api.put(`/api/feriados/${f.idFeriado}/estado`, { estado: e }); setExito(e === "Activo" ? "Feriado activado." : "Feriado inactivado."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  async function verificar() {
    setVRes(null);
    try {
      const r = await api.get<{ esHabil: boolean; motivo?: string | null }>("/api/calendario/dia-habil", { params: { idPais, fecha: vFecha } });
      setVRes(r.data.esHabil ? "Es un día hábil." : `No es hábil — ${r.data.motivo}`);
    } catch (err) { setVRes(mensajeError(err, "No se pudo consultar.")); }
  }

  return (
    <div>
      <h1>Calendario de feriados</h1>
      <p className="nota">Feriados por país. Los feriados «cada año» se repiten el mismo día y mes desde el año indicado. Los fines de semana se definen en Parámetros generales.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <div className="formulario-en-linea" style={{ margin: "0 0 1rem", flexWrap: "wrap", alignItems: "center" }}>
        <select value={idPais} onChange={(e) => setIdPais(Number(e.target.value))} aria-label="País">
          {paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
        </select>
        <label>Año <input type="number" min={2000} max={2100} value={anio} onChange={(e) => setAnio(Number(e.target.value))} style={{ width: "6rem" }} /></label>
        <span style={{ marginLeft: "1rem" }}>¿Es hábil?</span>
        <input type="date" value={vFecha} onChange={(e) => setVFecha(e.target.value)} />
        <button type="button" className="boton-secundario" onClick={() => void verificar()}>Verificar</button>
        {vRes && <span>{vRes}</span>}
      </div>

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(x) => x.idFeriado}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay feriados para este país y año."
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", onClick: () => { setExito(null); setErrorForm(null); setForm({ idPais, fecha: hoyIso(), nombre: "", esRecurrente: false }); setPanel("nuevo"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: () => { if (!f) return; setExito(null); setErrorForm(null); setForm({ idPais: f.idPais, fecha: f.fecha, nombre: f.nombre, esRecurrente: f.esRecurrente }); setPanel("editar"); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: f?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: f?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nuevo" ? "Nuevo feriado" : `Editar ${f?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>País
              <select value={form.idPais} onChange={(e) => setForm({ ...form, idPais: Number(e.target.value) })} required>
                {paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
              </select>
            </label>
            <label>Fecha
              <input type="date" value={form.fecha} onChange={(e) => setForm({ ...form, fecha: e.target.value })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            <label>Se repite
              <select value={form.esRecurrente ? 1 : 0} onChange={(e) => setForm({ ...form, esRecurrente: e.target.value === "1" })}>
                <option value={0}>Solo esta fecha</option>
                <option value={1}>Cada año (mismo día y mes)</option>
              </select>
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
