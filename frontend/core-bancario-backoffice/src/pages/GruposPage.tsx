import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, GrupoAdmin, GrupoMiembro } from "../api/tipos";

interface UsuarioItem { id: number; nombre: string; username?: string | null }
interface FormGrupo { codigo: string; nombre: string; descripcion: string }
const NUEVO: FormGrupo = { codigo: "", nombre: "", descripcion: "" };

function mensaje(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

/**
 * Administración › Grupos de distribución (permiso GES_GRUPOS).
 * Un grupo es una lista de usuarios independiente de la estructura organizativa. Hoy se usa como aprobador en las reglas
 * de aprobación (cualquier miembro puede resolver) y queda listo para dirigir notificaciones.
 */
export function GruposPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<GrupoAdmin[]>([]);
  const [usuarios, setUsuarios] = useState<UsuarioItem[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nuevo" | "editar" | "miembros" | null>(null);
  const [form, setForm] = useState<FormGrupo>(NUEVO);
  const [guardando, setGuardando] = useState(false);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [marcados, setMarcados] = useState<Set<number>>(new Set());
  const [busqueda, setBusqueda] = useState("");

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [g, u] = await Promise.all([
        api.get<GrupoAdmin[]>("/api/aprobacion/grupos"),
        api.get<UsuarioItem[]>("/api/catalogos/usuarios"),
      ]);
      setFilas(g.data); setUsuarios(u.data); setSel(new Set());
    } catch (e) { setError(mensaje(e, "No se pudieron cargar los grupos.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const seleccionado = filas.find((f) => f.idGrupo === idSel) ?? null;

  const columnas: ColumnaTabla<GrupoAdmin>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (g) => g.codigo, textoBusqueda: (g) => g.codigo },
    { clave: "nombre", encabezado: "Nombre", render: (g) => g.nombre, textoBusqueda: (g) => g.nombre },
    { clave: "desc", encabezado: "Descripción", render: (g) => g.descripcion ?? "—", textoBusqueda: (g) => g.descripcion ?? "" },
    { clave: "miembros", encabezado: "Miembros", render: (g) => g.miembros },
    { clave: "reglas", encabezado: "Reglas activas que lo usan", render: (g) => g.reglasQueLoUsan },
    { clave: "estado", encabezado: "Estado", render: (g) => g.estado, textoBusqueda: (g) => g.estado },
  ], []);

  function abrirNuevo() { setExito(null); setErrorForm(null); setForm(NUEVO); setPanel("nuevo"); }
  function abrirEditar() {
    if (!seleccionado) return;
    setExito(null); setErrorForm(null);
    setForm({ codigo: seleccionado.codigo, nombre: seleccionado.nombre, descripcion: seleccionado.descripcion ?? "" });
    setPanel("editar");
  }
  async function abrirMiembros() {
    if (!seleccionado) return;
    setExito(null); setErrorForm(null); setBusqueda("");
    try {
      const r = await api.get<GrupoMiembro[]>(`/api/aprobacion/grupos/${seleccionado.idGrupo}/miembros`);
      setMarcados(new Set(r.data.map((m) => m.idUsuario)));
      setPanel("miembros");
    } catch (e) { setError(mensaje(e, "No se pudieron cargar los miembros.")); }
  }

  async function guardar(e: FormEvent) {
    e.preventDefault();
    setGuardando(true); setErrorForm(null);
    try {
      const cuerpo = { codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion.trim() || null };
      if (panel === "nuevo") { await api.post("/api/aprobacion/grupos", cuerpo); setExito("Grupo creado. Agregue sus miembros con «Miembros»."); }
      else if (seleccionado) { await api.put(`/api/aprobacion/grupos/${seleccionado.idGrupo}`, cuerpo); setExito("Grupo actualizado."); }
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensaje(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  async function guardarMiembros() {
    if (!seleccionado) return;
    setGuardando(true); setErrorForm(null);
    try {
      await api.put(`/api/aprobacion/grupos/${seleccionado.idGrupo}/miembros`, { idsUsuarios: Array.from(marcados) });
      setExito("Miembros actualizados."); setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensaje(err, "No se pudieron guardar los miembros.")); }
    finally { setGuardando(false); }
  }

  async function cambiarEstado(estado: "Activo" | "Inactivo") {
    if (!seleccionado) return;
    if (estado === "Inactivo" && !window.confirm(`¿Inactivar el grupo ${seleccionado.nombre}?`)) return;
    setExito(null); setError(null);
    try {
      await api.put(`/api/aprobacion/grupos/${seleccionado.idGrupo}/estado`, { estado });
      setExito(estado === "Activo" ? "Grupo activado." : "Grupo inactivado.");
      await cargar();
    } catch (err) { setError(mensaje(err, "No se pudo cambiar el estado.")); }
  }

  const visibles = usuarios.filter((u) => {
    const t = busqueda.trim().toLowerCase();
    return !t || u.nombre.toLowerCase().includes(t) || (u.username ?? "").toLowerCase().includes(t);
  });
  function alternar(id: number) {
    setMarcados((m) => { const n = new Set(m); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  }

  return (
    <div>
      <h1>Grupos de distribución</h1>
      <p className="nota">
        Listas de usuarios independientes de la estructura organizativa. Un grupo puede ser el aprobador de un nivel en las reglas de
        aprobación: cualquiera de sus miembros puede resolverlo. Un grupo usado por reglas activas no se puede inactivar.
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(g) => g.idGrupo} seleccion={sel} onCambiarSeleccion={setSel}
        cargando={cargando} mensajeVacio="No hay grupos definidos." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", onClick: abrirNuevo },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: abrirEditar },
          { clave: "miembros", etiqueta: "Miembros", icono: "👥", requiereSeleccion: "una", onClick: () => void abrirMiembros() },
          { clave: "activar", etiqueta: "Activar", requiereSeleccion: "una", oculto: seleccionado?.estado !== "Inactivo", onClick: () => void cambiarEstado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", requiereSeleccion: "una", oculto: seleccionado?.estado !== "Activo", onClick: () => void cambiarEstado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {(panel === "nuevo" || panel === "editar") && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nuevo" ? "Nuevo grupo" : `Editar grupo ${seleccionado?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código
              <input value={form.codigo} maxLength={50} required placeholder="Ej. COMITE_CREDITO"
                onChange={(e) => setForm({ ...form, codigo: e.target.value.toUpperCase() })} />
            </label>
            <label>Nombre
              <input value={form.nombre} maxLength={200} required onChange={(e) => setForm({ ...form, nombre: e.target.value })} />
            </label>
            <label>Descripción
              <input value={form.descripcion} maxLength={500} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} />
            </label>
            <div className="panel-formulario__acciones">
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}

      {panel === "miembros" && seleccionado && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>Miembros de {seleccionado.nombre}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <input placeholder="Buscar usuario" value={busqueda} onChange={(e) => setBusqueda(e.target.value)} style={{ maxWidth: "22rem" }} />
          <p className="nota">{marcados.size} seleccionado(s). Solo se ofrecen usuarios activos.</p>
          <div style={{ maxHeight: "20rem", overflowY: "auto", border: "1px solid #dde3f0", borderRadius: 6, padding: "0.25rem 0.5rem" }}>
            {visibles.map((u) => (
              <label key={u.id} style={{ display: "flex", gap: "0.5rem", alignItems: "center", padding: "0.2rem 0" }}>
                <input type="checkbox" checked={marcados.has(u.id)} onChange={() => alternar(u.id)} />
                {u.nombre}{u.username ? <span className="nota"> ({u.username})</span> : null}
              </label>
            ))}
            {visibles.length === 0 && <p className="nota">Sin resultados.</p>}
          </div>
          <div className="panel-formulario__acciones">
            <button type="button" disabled={guardando} onClick={() => void guardarMiembros()}>{guardando ? "Guardando..." : "Guardar miembros"}</button>
            <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
          </div>
        </PanelModal>
      )}
    </div>
  );
}
