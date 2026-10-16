import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { PanelModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import type { CategoriaRegulatoria } from "../api/tiposCreditos";

interface Form { idPaisRegulador: number; codigo: string; nombre: string; descripcion: string }

/** Pricing Studio › Categorías regulatorias (IND_ADMIN). Clasificación por país/regulador que define el cliente (SUGEF, SBP...). */
export function CategoriasRegulatoriasPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("IND_ADMIN");
  const [filas, setFilas] = useState<CategoriaRegulatoria[]>([]);
  const [paises, setPaises] = useState<{ id: number; nombre: string }[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState<Form>({ idPaisRegulador: 0, codigo: "", nombre: "", descripcion: "" });
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [c, p] = await Promise.all([api.get<CategoriaRegulatoria[]>("/api/categorias-regulatorias"), api.get<{ id: number; nombre: string }[]>("/api/categorias-regulatorias/paises")]);
      setFilas(c.data); setPaises(p.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar las categorías.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<CategoriaRegulatoria>[] = useMemo(() => [
    { clave: "pais", encabezado: "País / regulador", render: (r) => r.paisNombre, textoBusqueda: (r) => r.paisNombre, filtroLista: true },
    { clave: "codigo", encabezado: "Código", render: (r) => r.codigo, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Categoría", render: (r) => <span style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}>{r.nombre}</span>, textoBusqueda: (r) => r.nombre },
    { clave: "desc", encabezado: "Descripción", render: (r) => r.descripcion ?? "", textoBusqueda: (r) => r.descripcion ?? "" },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const c = filas.find((x) => x.idCategoria === idSel) ?? null;

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    try {
      if (panel === "nuevo") await api.post("/api/categorias-regulatorias", form);
      else if (c) await api.put(`/api/categorias-regulatorias/${c.idCategoria}`, form);
      setExito(panel === "nuevo" ? "Categoría creada." : "Categoría actualizada.");
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }
  async function estado(e: "Activo" | "Inactivo") {
    if (!c) return;
    setExito(null); setError(null);
    try { await api.put(`/api/categorias-regulatorias/${c.idCategoria}/estado`, { estado: e }); setExito(e === "Activo" ? "Categoría activada." : "Categoría inactivada."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Categorías regulatorias</h1>
      <p className="nota">Clasificación que exige el regulador de cada país. Cada producto operativo puede asociarse a una; la entidad define las suyas.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idCategoria} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay categorías regulatorias." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nueva", icono: "+", oculto: !admin, onClick: () => { setExito(null); setErrorForm(null); setForm({ idPaisRegulador: paises[0]?.id ?? 0, codigo: "", nombre: "", descripcion: "" }); setPanel("nuevo"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", oculto: !admin, onClick: () => { if (!c) return; setExito(null); setErrorForm(null); setForm({ idPaisRegulador: c.idPaisRegulador, codigo: c.codigo, nombre: c.nombre, descripcion: c.descripcion ?? "" }); setPanel("editar"); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: c?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: c?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />
      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nuevo" ? "Nueva categoría regulatoria" : `Editar ${c?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>País / regulador
              <select value={form.idPaisRegulador} onChange={(e) => setForm({ ...form, idPaisRegulador: Number(e.target.value) })} required>
                {paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
              </select>
            </label>
            <label>Código <input type="text" maxLength={30} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label>Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
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
