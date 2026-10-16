import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, CatalogoItemResponse, UnidadAdmin } from "../api/tipos";

interface UsuarioItem { id: number; nombre: string; username?: string | null }

function mensaje(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

interface FormUnidad { codigo: string; nombre: string; idTipoUnidad: number; idPadre: number; idResponsable: number; permiteRegistros: boolean }
const NUEVA: FormUnidad = { codigo: "", nombre: "", idTipoUnidad: 0, idPadre: 0, idResponsable: 0, permiteRegistros: false };

/**
 * Administración › Estructura organizativa (permiso ORG_ADMINISTRACION).
 * Árbol de unidades (sucursal, agencia, departamento, equipo... según la lista TIPO_UNIDAD_ORG), cada una con
 * su unidad superior y responsable. Los usuarios y los clientes (luego cuentas y créditos) se asocian a una unidad.
 */
export function UnidadesPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<UnidadAdmin[]>([]);
  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [usuarios, setUsuarios] = useState<UsuarioItem[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nueva" | "editar" | null>(null);
  const [form, setForm] = useState<FormUnidad>(NUEVA);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const [u, t, us] = await Promise.all([
        api.get<UnidadAdmin[]>("/api/organizacion/unidades"),
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_UNIDAD_ORG"),
        api.get<UsuarioItem[]>("/api/catalogos/usuarios"),
      ]);
      setFilas(u.data); setTipos(t.data); setUsuarios(us.data); setSel(new Set());
    } catch (e) { setError(mensaje(e, "No se pudo cargar la estructura organizativa.")); }
    finally { setCargando(false); }
  }, [api]);

  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<UnidadAdmin>[] = useMemo(() => [
    {
      clave: "nombre", encabezado: "Unidad",
      render: (r) => (
        <span style={{ paddingLeft: `${r.profundidad * 1.25}rem`, opacity: r.estado === "Activo" ? 1 : 0.55 }}>
          {r.profundidad > 0 ? "└ " : ""}{r.nombre}
        </span>
      ),
      textoBusqueda: (r) => `${r.nombre} ${r.ruta}`,
    },
    { clave: "codigo", encabezado: "Código", render: (r) => r.codigo, textoBusqueda: (r) => r.codigo },
    { clave: "tipo", encabezado: "Tipo", render: (r) => r.tipoNombre, textoBusqueda: (r) => r.tipoNombre },
    { clave: "reg", encabezado: "Permite registros", render: (r) => (r.permiteRegistros ? "Sí" : "No"), textoBusqueda: (r) => (r.permiteRegistros ? "Sí" : "No") },
    { clave: "resp", encabezado: "Responsable", render: (r) => r.responsableNombre ?? "—", textoBusqueda: (r) => r.responsableNombre ?? "" },
    { clave: "us", encabezado: "Usuarios", render: (r) => r.cantidadUsuarios },
    { clave: "hijas", encabezado: "Subunidades", render: (r) => r.cantidadHijos },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const seleccionada = filas.find((f) => f.idUnidad === idSel) ?? null;

  // Descendientes de la unidad en edicion (no pueden ser su superior).
  const descendientes = useMemo(() => {
    const set = new Set<number>();
    if (!seleccionada || panel !== "editar") return set;
    const agregar = (id: number) => filas.filter((f) => f.idPadre === id).forEach((h) => { set.add(h.idUnidad); agregar(h.idUnidad); });
    set.add(seleccionada.idUnidad);
    agregar(seleccionada.idUnidad);
    return set;
  }, [filas, seleccionada, panel]);

  function abrirNueva() {
    setExito(null); setErrorForm(null);
    setForm({ ...NUEVA, idTipoUnidad: tipos[0]?.id ?? 0, idPadre: seleccionada?.idUnidad ?? 0 });
    setPanel("nueva");
  }

  function abrirEditar() {
    if (!seleccionada) return;
    setExito(null); setErrorForm(null);
    setForm({
      codigo: seleccionada.codigo, nombre: seleccionada.nombre, idTipoUnidad: seleccionada.idTipoUnidad,
      idPadre: seleccionada.idPadre ?? 0, idResponsable: seleccionada.idResponsable ?? 0, permiteRegistros: seleccionada.permiteRegistros,
    });
    setPanel("editar");
  }

  async function guardar(e: FormEvent) {
    e.preventDefault();
    setGuardando(true); setErrorForm(null);
    const cuerpo = {
      codigo: form.codigo, nombre: form.nombre, idTipoUnidad: form.idTipoUnidad,
      idPadre: form.idPadre || null, idResponsable: form.idResponsable || null, permiteRegistros: form.permiteRegistros,
    };
    try {
      if (panel === "nueva") { await api.post("/api/organizacion/unidades", cuerpo); setExito("Unidad creada."); }
      else if (seleccionada) { await api.put(`/api/organizacion/unidades/${seleccionada.idUnidad}`, cuerpo); setExito("Unidad actualizada."); }
      setPanel(null);
      await cargar();
    } catch (err) { setErrorForm(mensaje(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  async function cambiarEstado(estado: "Activo" | "Inactivo") {
    if (!seleccionada) return;
    setExito(null); setError(null);
    try {
      await api.put(`/api/organizacion/unidades/${seleccionada.idUnidad}/estado`, { estado });
      setExito(estado === "Activo" ? "Unidad activada." : "Unidad inactivada.");
      await cargar();
    } catch (err) { setError(mensaje(err, "No se pudo cambiar el estado.")); }
  }

  const opcionesPadre = filas.filter((f) => f.estado === "Activo" && !descendientes.has(f.idUnidad));

  return (
    <div>
      <h1>Estructura organizativa</h1>
      <p className="nota">
        Árbol de unidades (sucursales, agencias, departamentos, equipos...). Cada usuario y cada cliente se asocia a una unidad.
        El tipo de unidad se mantiene en Listas de Valores (TIPO_UNIDAD_ORG).
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idUnidad}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay unidades. Use «Nueva» para crear la primera (por ejemplo, la casa matriz)."
        comandos={[
          { clave: "nueva", etiqueta: "Nueva", icono: "+", onClick: abrirNueva },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: abrirEditar },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: seleccionada?.estado === "Activo", onClick: () => void cambiarEstado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: seleccionada?.estado === "Inactivo", onClick: () => void cambiarEstado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
      {seleccionada && !panel && <p className="nota">Ruta: {seleccionada.ruta}. «Nueva» con una unidad seleccionada crea una subunidad.</p>}

      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nueva" ? "Nueva unidad" : `Editar ${seleccionada?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código (único)
              <input type="text" maxLength={20} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={200} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            <label>Tipo
              <select value={form.idTipoUnidad} onChange={(e) => setForm({ ...form, idTipoUnidad: Number(e.target.value) })} required>
                {tipos.map((t) => (<option key={t.id} value={t.id}>{t.nombre}</option>))}
                {form.idTipoUnidad > 0 && !tipos.some((t) => t.id === form.idTipoUnidad) && (
                  <option value={form.idTipoUnidad}>{seleccionada?.tipoNombre} (inactivo)</option>
                )}
              </select>
            </label>
            <label>Unidad superior
              <select value={form.idPadre} onChange={(e) => setForm({ ...form, idPadre: Number(e.target.value) })}>
                <option value={0}>— Ninguna (raíz) —</option>
                {opcionesPadre.map((u) => (<option key={u.idUnidad} value={u.idUnidad}>{u.ruta}</option>))}
              </select>
            </label>
            <label>Responsable
              <select value={form.idResponsable} onChange={(e) => setForm({ ...form, idResponsable: Number(e.target.value) })}>
                <option value={0}>— Sin responsable —</option>
                {usuarios.map((u) => (<option key={u.id} value={u.id}>{u.nombre}</option>))}
              </select>
            </label>
            <label style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
              <input type="checkbox" checked={form.permiteRegistros} onChange={(e) => setForm({ ...form, permiteRegistros: e.target.checked })} />
              Permite registros (aparece en el combo «Unidad (sucursal)» al dar de alta o modificar clientes)
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
