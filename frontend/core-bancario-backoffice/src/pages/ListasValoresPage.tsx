import { PanelModal, VentanaModal } from "../components/VentanaModal";
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, ListaItemResponse, ListaTipoResponse } from "../api/tipos";

const COLUMNAS_VALORES: ColumnaTabla<ListaItemResponse>[] = [
  { clave: "orden", encabezado: "Orden", render: (i) => i.orden ?? "—" },
  { clave: "codigo", encabezado: "Código", render: (i) => i.codigo, textoBusqueda: (i) => i.codigo },
  { clave: "nombre", encabezado: "Nombre", render: (i) => i.nombre, textoBusqueda: (i) => i.nombre },
  { clave: "descripcion", encabezado: "Descripción", render: (i) => i.descripcion ?? "—", textoBusqueda: (i) => i.descripcion ?? "" },
  { clave: "estado", encabezado: "Estado", render: (i) => i.estado, textoBusqueda: (i) => i.estado },
  { clave: "sistema", encabezado: "De sistema", render: (i) => (i.esSistema ? "Sí" : "—") },
  { clave: "uso", encabezado: "En uso", render: (i) => (i.enUso ? "Sí" : "No") },
];

function mensajeError(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

function unica(seleccion: Set<string | number>): number | null {
  return seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
}

/** Ejecuta una acción por cada registro seleccionado y devuelve los fallos (vacío si todo salió bien). */
async function ejecutarLote(claves: Set<string | number>, accion: (clave: number) => Promise<unknown>, etiqueta: (clave: number) => string): Promise<string[]> {
  const fallos: string[] = [];
  for (const k of Array.from(claves)) {
    const clave = Number(k);
    try { await accion(clave); } catch (err) { fallos.push(`${etiqueta(clave)}: ${mensajeError(err, "error inesperado")}`); }
  }
  return fallos;
}

type Modo = "ver" | "editar" | "nuevo";
type Pestana = "datos" | "valores";

/**
 * Administracion de Listas de Valores (gen.LISTA_VALORES_TIPO / _ITEM).
 * Seguridad: LOV_ACCESO para entrar; LOV_<codigo> para mantener los valores de
 * una lista; LOV_ADMINISTRACION para todas las listas y para crear/borrar
 * listas. Los valores no se borran si ya se usaron: se desactivan.
 * Patrón de ficha: el nombre de la lista abre su consulta (datos y valores en pestañas) y desde ahí se edita.
 */
export function ListasValoresPage() {
  const api = useApiClient();
  const { cargando: cargandoPermisos, tienePermiso } = usePermisos();
  const puedeEntrar = tienePermiso("LOV_ACCESO");
  const esAdmin = tienePermiso("LOV_ADMINISTRACION");

  const [listas, setListas] = useState<ListaTipoResponse[]>([]);
  const [cargandoListas, setCargandoListas] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [seleccionListas, setSeleccionListas] = useState<Set<string | number>>(new Set());
  const [ficha, setFicha] = useState<{ id: number | null; modo: Modo; pestana: Pestana; aviso?: string } | null>(null);

  async function cargarListas() {
    setCargandoListas(true);
    setError(null);
    try {
      const r = await api.get<ListaTipoResponse[]>("/api/listas-valores/tipos");
      setListas(r.data);
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al cargar las listas."));
    } finally {
      setCargandoListas(false);
    }
  }

  useEffect(() => {
    if (cargandoPermisos || !puedeEntrar) return;
    void cargarListas();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cargandoPermisos, puedeEntrar]);

  const columnas: ColumnaTabla<ListaTipoResponse>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (l) => l.codigo, textoBusqueda: (l) => l.codigo },
    { clave: "nombre", encabezado: "Nombre", render: (l) => (
        <button type="button" className="boton-enlace-id" title="Consultar la lista y sus valores"
          onClick={() => { setAviso(null); setFicha({ id: l.idListaValoresTipo, modo: "ver", pestana: "datos" }); }}>{l.nombre}</button>
      ), textoBusqueda: (l) => l.nombre },
    { clave: "estado", encabezado: "Estado", render: (l) => l.estado },
    { clave: "sistema", encabezado: "De sistema", render: (l) => (l.esSistema ? "Sí" : "—") },
    { clave: "items", encabezado: "Valores", render: (l) => l.cantidadItems },
  ], []);

  async function cambiarEstadoListas(estado: "Activo" | "Inactivo") {
    setError(null); setAviso(null);
    const fallos = await ejecutarLote(seleccionListas, (id) => api.post(`/api/listas-valores/tipos/${id}/estado`, { estado }), (id) => listas.find((l) => l.idListaValoresTipo === id)?.codigo ?? String(id));
    if (fallos.length > 0) setError(fallos.join(" | ")); else setAviso(estado === "Activo" ? "Listas activadas." : "Listas desactivadas.");
    await cargarListas();
  }

  async function borrarListas() {
    if (!window.confirm(`¿Eliminar ${seleccionListas.size} lista(s)? Esta acción no se puede deshacer.`)) return;
    setError(null); setAviso(null);
    const fallos = await ejecutarLote(seleccionListas, (id) => api.delete(`/api/listas-valores/tipos/${id}`), (id) => listas.find((l) => l.idListaValoresTipo === id)?.codigo ?? String(id));
    if (fallos.length > 0) setError(fallos.join(" | ")); else setAviso("Listas eliminadas.");
    setSeleccionListas(new Set());
    await cargarListas();
  }

  if (cargandoPermisos) {
    return (
      <div>
        <h1>Listas de Valores</h1>
        <p className="nota">Verificando permisos...</p>
      </div>
    );
  }

  if (!puedeEntrar) {
    return (
      <div>
        <h1>Listas de Valores</h1>
        <p className="mensaje-error">
          No tienes el permiso <strong>LOV_ACCESO</strong> para ver esta sección. Pídele a un
          administrador que te asigne un rol con ese permiso.
        </p>
      </div>
    );
  }

  const listaFicha = ficha?.id != null ? listas.find((l) => l.idListaValoresTipo === ficha.id) ?? null : null;

  return (
    <div>
      <h1>Listas de Valores</h1>
      <p className="nota">
        Catálogos que alimentan los combos y filtros del sistema. Un valor que ya se usó no se borra:
        se desactiva y deja de ofrecerse en registros nuevos. Solo ves las listas para las que tienes
        permiso. Pulsa el nombre de una lista para consultarla, ver sus valores y editarla.
      </p>

      {error && <p className="mensaje-error">{error}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}

      <TablaDatos
        columnas={columnas}
        filas={listas}
        claveFila={(l) => l.idListaValoresTipo}
        seleccion={seleccionListas}
        onCambiarSeleccion={setSeleccionListas}
        cargando={cargandoListas}
        mensajeVacio="No hay listas visibles para tu usuario."
        placeholderBusqueda="Buscar lista"
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !esAdmin, onClick: () => { setAviso(null); setFicha({ id: null, modo: "nuevo", pestana: "datos" }); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", oculto: !esAdmin, requiereSeleccion: "varias", onClick: () => cambiarEstadoListas("Activo") },
          { clave: "desactivar", etiqueta: "Desactivar", icono: "⊘", oculto: !esAdmin, requiereSeleccion: "varias", onClick: () => cambiarEstadoListas("Inactivo") },
          { clave: "eliminar", etiqueta: "Eliminar", icono: "🗑", oculto: !esAdmin, requiereSeleccion: "varias", onClick: borrarListas },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: cargarListas },
        ]}
      />

      {ficha && (ficha.id == null || listaFicha) && (
        <FichaLista key={ficha.id ?? "nueva"} lista={listaFicha} modoInicial={ficha.modo} pestanaInicial={ficha.pestana} avisoInicial={ficha.aviso} esAdmin={esAdmin}
          onCerrar={() => setFicha(null)} onRefrescar={cargarListas}
          onCreada={async (id, codigo) => {
            await cargarListas();
            setFicha({ id, modo: "editar", pestana: "valores", aviso: `Lista creada. Asigne su permiso LOV_${codigo.trim().toUpperCase().replace(/ /g, "_")} a los roles que deban mantenerla (pantalla Seguridad). Agregue sus valores a continuación.` });
          }} />
      )}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Ficha de la lista
function FichaLista({ lista, modoInicial, pestanaInicial, avisoInicial, esAdmin, onCerrar, onRefrescar, onCreada }: {
  lista: ListaTipoResponse | null; modoInicial: Modo; pestanaInicial: Pestana; avisoInicial?: string; esAdmin: boolean;
  onCerrar: () => void; onRefrescar: () => Promise<void>; onCreada: (id: number, codigo: string) => Promise<void>;
}) {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const puedeMantener = !!lista && (esAdmin || tienePermiso(`LOV_${lista.codigo}`));
  const [modo, setModo] = useState<Modo>(modoInicial);
  const [pestana, setPestana] = useState<Pestana>(pestanaInicial);
  const [form, setForm] = useState({ codigo: lista?.codigo ?? "", nombre: lista?.nombre ?? "", descripcion: lista?.descripcion ?? "" });
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(avisoInicial ?? null);
  const [guardando, setGuardando] = useState(false);

  async function guardar(e: FormEvent) {
    e.preventDefault();
    setGuardando(true); setErrorForm(null);
    try {
      if (modo === "nuevo") {
        const r = await api.post<ListaTipoResponse>("/api/listas-valores/tipos", { codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion || null });
        await onCreada(r.data.idListaValoresTipo, form.codigo);
        return;
      }
      if (lista) await api.put(`/api/listas-valores/tipos/${lista.idListaValoresTipo}`, { nombre: form.nombre, descripcion: form.descripcion || null });
      setAviso("Lista actualizada."); setModo("ver"); await onRefrescar();
    } catch (err) { setErrorForm(mensajeError(err, "Error inesperado al guardar la lista.")); }
    finally { setGuardando(false); }
  }

  const dato = (etiqueta: string, valor: ReactNode) => (<div className="info-fila"><span className="info-fila__etiqueta">{etiqueta}</span><span className="info-fila__valor">{valor}</span></div>);
  const editaDatos = esAdmin && modo !== "ver";

  return (
    <VentanaModal titulo={lista ? `${lista.codigo} · ${lista.nombre}` : "Nueva lista"} onCerrar={onCerrar} ancho={1000}
      acciones={modo === "ver" && lista && (esAdmin || puedeMantener) ? (
        <button type="button" onClick={() => { setForm({ codigo: lista.codigo, nombre: lista.nombre, descripcion: lista.descripcion ?? "" }); setErrorForm(null); setAviso(null); setModo("editar"); }}>✎ Editar</button>
      ) : undefined}>
      {lista && <p className="nota" style={{ marginTop: 0 }}>Estado: <strong>{lista.estado}</strong>{modo === "editar" ? " · modo edición" : ""}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      <div className="v360__pestanas" role="tablist">
        <button type="button" role="tab" aria-selected={pestana === "datos"} className={`v360__pestana ${pestana === "datos" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("datos")}>Datos de la lista</button>
        <button type="button" role="tab" aria-selected={pestana === "valores"} className={`v360__pestana ${pestana === "valores" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("valores")}>Valores{lista ? ` (${lista.cantidadItems})` : ""}</button>
      </div>

      {pestana === "datos" && !editaDatos && lista && (
        <div>
          {dato("Código", lista.codigo)}
          {dato("Nombre", lista.nombre)}
          {dato("Descripción", lista.descripcion || "—")}
          {dato("De sistema", lista.esSistema ? "Sí" : "No")}
          {dato("Permiso para mantener sus valores", <code>LOV_{lista.codigo}</code>)}
          {modo === "editar" && !esAdmin && <p className="nota">Puede mantener los valores de esta lista (pestaña Valores); los datos de la lista solo los edita un administrador de listas.</p>}
        </div>
      )}

      {pestana === "datos" && editaDatos && (
        <>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código (ej. TIPO_CLIENTE) <input type="text" value={form.codigo} disabled={modo === "editar"} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label className="formulario__ancho">Descripción (opcional) <input type="text" value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <div className="formulario__ancho" style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : modo === "nuevo" ? "Guardar y agregar valores" : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => { if (modo === "nuevo") onCerrar(); else { setModo("ver"); setErrorForm(null); } }}>Cancelar</button>
            </div>
          </form>
        </>
      )}

      {pestana === "valores" && (lista ? (
        <ValoresLista lista={lista} puedeEditar={puedeMantener && modo === "editar"} onCambio={() => void onRefrescar()} />
      ) : (
        <p className="nota">Guarde primero los datos de la lista: después podrá agregar sus valores aquí mismo.</p>
      ))}
    </VentanaModal>
  );
}

// ------------------------------------------------------------------------------------------------ Valores de la lista
type PanelValor = { modo: "nuevo" | "editar"; codigo: string; nombre: string; orden: string; descripcion: string } | null;

function ValoresLista({ lista, puedeEditar, onCambio }: { lista: ListaTipoResponse; puedeEditar: boolean; onCambio: () => void }) {
  const api = useApiClient();
  const [items, setItems] = useState<ListaItemResponse[]>([]);
  const [cargando, setCargando] = useState(false);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [panel, setPanel] = useState<PanelValor>(null);
  const [guardando, setGuardando] = useState(false);
  const idLista = lista.idListaValoresTipo;

  async function cargarItems() {
    setCargando(true);
    try {
      const r = await api.get<ListaItemResponse[]>(`/api/listas-valores/tipos/${idLista}/items`);
      setItems(r.data); setSeleccion(new Set());
    } catch (err) { setError(mensajeError(err, "Error inesperado al cargar los valores.")); setItems([]); }
    finally { setCargando(false); }
  }
  useEffect(() => { void cargarItems(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [idLista]);

  function abrirEditar() {
    const item = items.find((i) => i.idListaValoresItem === unica(seleccion));
    if (!item) return;
    setPanel({ modo: "editar", codigo: item.codigo, nombre: item.nombre, orden: item.orden?.toString() ?? "", descripcion: item.descripcion ?? "" });
  }

  async function guardar(e: FormEvent) {
    e.preventDefault();
    if (!panel) return;
    setGuardando(true); setError(null); setAviso(null);
    const orden = panel.orden.trim() === "" ? null : Number(panel.orden);
    try {
      if (panel.modo === "nuevo") {
        await api.post(`/api/listas-valores/tipos/${idLista}/items`, { codigo: panel.codigo, nombre: panel.nombre, orden, descripcion: panel.descripcion.trim() || null });
        setAviso("Valor creado.");
      } else {
        await api.put(`/api/listas-valores/items/${unica(seleccion)}`, { nombre: panel.nombre, orden, descripcion: panel.descripcion.trim() || null });
        setAviso("Valor actualizado.");
      }
      setPanel(null);
      await cargarItems(); onCambio();
    } catch (err) { setError(mensajeError(err, "Error inesperado al guardar el valor.")); }
    finally { setGuardando(false); }
  }

  async function cambiarEstado(estado: "Activo" | "Inactivo") {
    setError(null); setAviso(null);
    const fallos = await ejecutarLote(seleccion, (id) => api.post(`/api/listas-valores/items/${id}/estado`, { estado }), (id) => items.find((i) => i.idListaValoresItem === id)?.codigo ?? String(id));
    if (fallos.length > 0) setError(fallos.join(" | ")); else setAviso(estado === "Activo" ? "Valores activados." : "Valores desactivados.");
    await cargarItems(); onCambio();
  }

  async function borrar() {
    if (!window.confirm(`¿Eliminar ${seleccion.size} valor(es)? Solo se pueden borrar los que no se han usado.`)) return;
    setError(null); setAviso(null);
    const fallos = await ejecutarLote(seleccion, (id) => api.delete(`/api/listas-valores/items/${id}`), (id) => items.find((i) => i.idListaValoresItem === id)?.codigo ?? String(id));
    if (fallos.length > 0) setError(fallos.join(" | ")); else setAviso("Valores eliminados.");
    await cargarItems(); onCambio();
  }

  return (
    <div>
      {!puedeEditar && <p className="nota" style={{ marginTop: 0 }}>Consulta. Para agregar o cambiar valores pulse «Editar» en la parte inferior de la ficha.</p>}
      {error && <p className="mensaje-error">{error}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      <TablaDatos columnas={COLUMNAS_VALORES} filas={items} claveFila={(i) => i.idListaValoresItem} seleccion={seleccion} onCambiarSeleccion={setSeleccion} cargando={cargando}
        mensajeVacio="Esta lista no tiene valores." placeholderBusqueda="Buscar valor"
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !puedeEditar, onClick: () => setPanel({ modo: "nuevo", codigo: "", nombre: "", orden: "", descripcion: "" }) },
          { clave: "editar", etiqueta: "Editar", icono: "✎", oculto: !puedeEditar, requiereSeleccion: "una", onClick: abrirEditar },
          { clave: "activar", etiqueta: "Activar", icono: "✓", oculto: !puedeEditar, requiereSeleccion: "varias", onClick: () => cambiarEstado("Activo") },
          { clave: "desactivar", etiqueta: "Desactivar", icono: "⊘", oculto: !puedeEditar, requiereSeleccion: "varias", onClick: () => cambiarEstado("Inactivo") },
          { clave: "eliminar", etiqueta: "Eliminar", icono: "🗑", oculto: !puedeEditar, requiereSeleccion: "varias", onClick: borrar },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => cargarItems() },
        ]} />

      {panel && (
        <PanelModal onCerrar={() => setPanel(null)} ancho={640}>
          <h3>{panel.modo === "nuevo" ? "Nuevo valor" : `Editar valor ${panel.codigo}`}</h3>
          {error && <p className="mensaje-error">{error}</p>}
          <form onSubmit={guardar} className="formulario">
            <label>Código (ej. CEDULA_FISICA) <input type="text" value={panel.codigo} disabled={panel.modo === "editar"} onChange={(e) => setPanel({ ...panel, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" value={panel.nombre} onChange={(e) => setPanel({ ...panel, nombre: e.target.value })} required /></label>
            <label>Orden <input type="number" value={panel.orden} onChange={(e) => setPanel({ ...panel, orden: e.target.value })} /></label>
            <label className="formulario__ancho">Descripción (texto explicativo, opcional) <input type="text" maxLength={500} value={panel.descripcion} onChange={(e) => setPanel({ ...panel, descripcion: e.target.value })} /></label>
            <div className="formulario__ancho" style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}
