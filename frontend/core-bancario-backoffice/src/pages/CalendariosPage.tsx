import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { PanelModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import type { CatalogoItemResponse } from "../api/tipos";
import type { Calendario } from "../api/tiposCreditos";

const DIAS = [["lunes", "Lun"], ["martes", "Mar"], ["miercoles", "Mié"], ["jueves", "Jue"], ["viernes", "Vie"], ["sabado", "Sáb"], ["domingo", "Dom"]] as const;
type Dia = (typeof DIAS)[number][0];
interface Form { codigo: string; nombre: string; idPais: number; esDefecto: boolean; dias: Record<Dia, boolean> }
const diasInicial = (): Record<Dia, boolean> => ({ lunes: true, martes: true, miercoles: true, jueves: true, viernes: true, sabado: false, domingo: false });

/** Administración › Calendarios (IND_ADMIN). Días hábiles de la semana; los feriados salen del Calendario de feriados del país. */
export function CalendariosPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("IND_ADMIN");
  const [filas, setFilas] = useState<Calendario[]>([]);
  const [paises, setPaises] = useState<CatalogoItemResponse[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState<Form>({ codigo: "", nombre: "", idPais: 0, esDefecto: false, dias: diasInicial() });
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [c, p] = await Promise.all([api.get<Calendario[]>("/api/calendarios"), api.get<CatalogoItemResponse[]>("/api/catalogos/listas/PAIS")]);
      setFilas(c.data); setPaises(p.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los calendarios.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const habiles = (r: Calendario) => DIAS.filter(([k]) => r[k]).map(([, e]) => e).join(" · ");
  const columnas: ColumnaTabla<Calendario>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => r.codigo, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Calendario", render: (r) => <span style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}>{r.nombre}{r.esDefecto ? " ★" : ""}</span>, textoBusqueda: (r) => r.nombre },
    { clave: "pais", encabezado: "País (feriados)", render: (r) => r.paisNombre, textoBusqueda: (r) => r.paisNombre },
    { clave: "dias", encabezado: "Días hábiles", render: habiles, textoBusqueda: habiles },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const k = filas.find((x) => x.idCalendario === idSel) ?? null;
  const cuerpo = () => ({ codigo: form.codigo, nombre: form.nombre, idPais: form.idPais, esDefecto: form.esDefecto, ...form.dias });

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    try {
      if (panel === "nuevo") await api.post("/api/calendarios", cuerpo());
      else if (k) await api.put(`/api/calendarios/${k.idCalendario}`, cuerpo());
      setExito(panel === "nuevo" ? "Calendario creado." : "Calendario actualizado.");
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }
  async function estado(e: "Activo" | "Inactivo") {
    if (!k) return;
    setExito(null); setError(null);
    try { await api.put(`/api/calendarios/${k.idCalendario}/estado`, { estado: e }); setExito(e === "Activo" ? "Calendario activado." : "Calendario inactivado."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Calendarios</h1>
      <p className="nota">Un calendario define qué días de la semana son hábiles; los feriados vienen del país elegido (Administración › Calendario de feriados). El calendario por defecto (★) se usa cuando el producto no tiene uno propio.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idCalendario} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay calendarios." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !admin, onClick: () => { setExito(null); setErrorForm(null); setForm({ codigo: "", nombre: "", idPais: paises[0]?.id ?? 0, esDefecto: filas.length === 0, dias: diasInicial() }); setPanel("nuevo"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", oculto: !admin, onClick: () => { if (!k) return; setExito(null); setErrorForm(null); setForm({ codigo: k.codigo, nombre: k.nombre, idPais: k.idPais, esDefecto: k.esDefecto, dias: { lunes: k.lunes, martes: k.martes, miercoles: k.miercoles, jueves: k.jueves, viernes: k.viernes, sabado: k.sabado, domingo: k.domingo } }); setPanel("editar"); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: k?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: k?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />
      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nuevo" ? "Nuevo calendario" : `Editar ${k?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código <input type="text" maxLength={30} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label>País (feriados)
              <select value={form.idPais} onChange={(e) => setForm({ ...form, idPais: Number(e.target.value) })} required>
                {paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
              </select>
            </label>
            <fieldset style={{ border: "none", padding: 0 }}>
              <legend>Días hábiles</legend>
              <div style={{ display: "flex", gap: "0.8rem", flexWrap: "wrap" }}>
                {DIAS.map(([clave, etiqueta]) => (
                  <label key={clave} style={{ display: "flex", gap: "0.3rem", alignItems: "center" }}>
                    <input type="checkbox" checked={form.dias[clave]} onChange={(e) => setForm({ ...form, dias: { ...form.dias, [clave]: e.target.checked } })} /> {etiqueta}
                  </label>
                ))}
              </div>
            </fieldset>
            <label style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}>
              <input type="checkbox" checked={form.esDefecto} onChange={(e) => setForm({ ...form, esDefecto: e.target.checked })} /> Calendario por defecto de la compañía
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
