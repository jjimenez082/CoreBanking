import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { PanelModal, VentanaModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { fmtFecha, hoyIso, mensajeError } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import type { CatalogoItemResponse } from "../api/tipos";
import type { ConjuntoReglas } from "../api/reglas";
import type { BaseCalculo, Indicador, ProductoComercial, ProductoOperativo, VersionDetalle, VersionProducto, VersionValor } from "../api/tiposCreditos";

interface Form { idProductoOperativo: number; codigo: string; nombre: string; descripcion: string; idProductoBase: number | null }
type Modo = "ver" | "editar" | "nuevo";

/** Pricing Studio › Productos comerciales (consulta PRD_CONSULTA; mantenimiento PRD_ADMIN; publicar versión PRD_PUBLICAR, por gestión de aprobación). */
export function ProductosComercialesPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("PRD_ADMIN");
  const [filas, setFilas] = useState<ProductoComercial[]>([]);
  const [todosOperativos, setTodosOperativos] = useState<ProductoOperativo[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [ficha, setFicha] = useState<{ id: number | null; modo: Modo } | null>(null);
  const [versiones, setVersiones] = useState<ProductoComercial | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [c, o] = await Promise.all([api.get<ProductoComercial[]>("/api/productos-comerciales"), api.get<ProductoOperativo[]>("/api/productos-operativos")]);
      setFilas(c.data); setTodosOperativos(o.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los productos.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<ProductoComercial>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Producto", render: (r) => (
        <button type="button" className="boton-enlace-id" title="Consultar el producto" style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}
          onClick={() => { setExito(null); setFicha({ id: r.idProducto, modo: "ver" }); }}>{r.nombre}</button>
      ), textoBusqueda: (r) => r.nombre },
    { clave: "op", encabezado: "Producto operativo", render: (r) => r.operativoNombre, textoBusqueda: (r) => r.operativoNombre, filtroLista: true },
    { clave: "base", encabezado: "Variante de", render: (r) => r.baseNombre ?? "—", textoBusqueda: (r) => r.baseNombre ?? "" },
    { clave: "vig", encabezado: "Versión vigente", render: (r) => (r.versionVigente ? `v${r.versionVigente}` : "Sin publicar"), valorOrden: (r) => r.versionVigente ?? 0 },
    { clave: "prog", encabezado: "Programada", render: (r) => (r.versionProgramada ? `v${r.versionProgramada}` : "—") },
    { clave: "edi", encabezado: "En edición", render: (r) => (r.versionEnEdicion ? `v${r.versionEnEdicion}` : "—") },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const p = filas.find((x) => x.idProducto === idSel) ?? null;
  const productoFicha = ficha?.id != null ? filas.find((x) => x.idProducto === ficha.id) ?? null : null;

  async function estado(e: "Activo" | "Inactivo") {
    if (!p) return;
    setExito(null); setError(null);
    try { await api.put(`/api/productos-comerciales/${p.idProducto}/estado`, { estado: e }); setExito(e === "Activo" ? "Producto activado." : "Producto inactivado."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Productos comerciales</h1>
      <p className="nota">La oferta al cliente. Sus condiciones (tasa, plazos, montos, comisiones) se definen por <strong>versión</strong>: se prepara un borrador, se envía a aprobación y rige desde la fecha indicada. Una variante hereda las condiciones de su producto base y cambia solo lo que necesite. Pulse el nombre de un producto para consultarlo y editarlo.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idProducto} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay productos comerciales." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !admin, onClick: () => { setExito(null); setFicha({ id: null, modo: "nuevo" }); } },
          { clave: "versiones", etiqueta: "Versiones", icono: "⎇", requiereSeleccion: "una", onClick: () => { setExito(null); setVersiones(p); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: p?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: p?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />

      {ficha && (ficha.id == null || productoFicha) && (
        <FichaProductoComercial key={ficha.id ?? "nuevo"} producto={productoFicha} modoInicial={ficha.modo} filas={filas} operativos={todosOperativos} admin={admin}
          onCerrar={() => setFicha(null)} onRefrescar={cargar}
          onVersiones={(x) => { setFicha(null); setVersiones(x); }}
          onCreado={async () => { setFicha(null); setExito("Producto comercial creado. Seleccione el producto y use «Versiones» para definir sus condiciones."); await cargar(); }} />
      )}
      {versiones && <VersionesProducto producto={versiones} puedeEditar={admin} puedePublicar={tienePermiso("PRD_PUBLICAR")} onCerrar={() => { setVersiones(null); void cargar(); }} />}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Ficha
function FichaProductoComercial({ producto, modoInicial, filas, operativos, admin, onCerrar, onRefrescar, onCreado, onVersiones }: {
  producto: ProductoComercial | null; modoInicial: Modo; filas: ProductoComercial[]; operativos: ProductoOperativo[]; admin: boolean;
  onCerrar: () => void; onRefrescar: () => Promise<void>; onCreado: () => Promise<void>; onVersiones: (p: ProductoComercial) => void;
}) {
  const api = useApiClient();
  const activos = operativos.filter((o) => o.estado === "Activo");
  const formDe = (x: ProductoComercial): Form => ({ idProductoOperativo: x.idProductoOperativo, codigo: x.codigo, nombre: x.nombre, descripcion: x.descripcion ?? "", idProductoBase: x.idProductoBase ?? null });
  const [modo, setModo] = useState<Modo>(modoInicial);
  const [form, setForm] = useState<Form>(() => (producto ? formDe(producto) : { idProductoOperativo: activos[0]?.idProducto ?? 0, codigo: "", nombre: "", descripcion: "", idProductoBase: null }));
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  // Solo operativos activos; al editar se conserva el actual aunque ya no esté activo
  const operativosForm = producto && !activos.some((o) => o.idProducto === producto.idProductoOperativo)
    ? [...activos, ...operativos.filter((o) => o.idProducto === producto.idProductoOperativo)] : activos;
  const posiblesBase = filas.filter((x) => x.idProductoOperativo === form.idProductoOperativo && x.idProductoBase == null && x.estado === "Activo" && (modo !== "editar" || x.idProducto !== producto?.idProducto));

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const cuerpo = { ...form, descripcion: form.descripcion.trim() || null };
    try {
      if (modo === "nuevo") { await api.post("/api/productos-comerciales", cuerpo); await onCreado(); return; }
      if (producto) await api.put(`/api/productos-comerciales/${producto.idProducto}`, cuerpo);
      setAviso("Producto comercial actualizado."); setModo("ver"); await onRefrescar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  const dato = (etiqueta: string, valor: ReactNode) => (<div className="info-fila"><span className="info-fila__etiqueta">{etiqueta}</span><span className="info-fila__valor">{valor}</span></div>);
  const v = (n?: number | null) => (n ? `v${n}` : "—");

  return (
    <VentanaModal titulo={producto ? `${producto.codigo} · ${producto.nombre}` : "Nuevo producto comercial"} onCerrar={onCerrar} ancho={820}
      acciones={modo === "ver" && producto ? (
        <>
          {admin && <button type="button" onClick={() => { setForm(formDe(producto)); setErrorForm(null); setAviso(null); setModo("editar"); }}>✎ Editar</button>}
          <button type="button" className="boton-secundario" onClick={() => onVersiones(producto)}>⎇ Versiones</button>
        </>
      ) : undefined}>
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      {modo === "ver" && producto ? (
        <div>
          {dato("Código", producto.codigo)}
          {dato("Nombre", producto.nombre)}
          {dato("Descripción", producto.descripcion || "—")}
          {dato("Producto operativo", producto.operativoNombre)}
          {dato("Estrategia de versionado", producto.estrategiaVersionado === "Dinamico" ? "Dinámico: el crédito sigue la versión vigente" : "Fijo al origen: el crédito conserva las condiciones con que nació")}
          {dato("Es variante de", producto.baseNombre || "No (producto base)")}
          {dato("Versión vigente", producto.versionVigente ? v(producto.versionVigente) : "Sin publicar")}
          {dato("Versión programada", v(producto.versionProgramada))}
          {dato("Versión en edición", v(producto.versionEnEdicion))}
          {dato("Estado", producto.estado)}
          <ElegibilidadProducto idProducto={producto.idProducto} admin={admin} />
        </div>
      ) : (
        <>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Producto operativo
              <select value={form.idProductoOperativo || ""} onChange={(e) => setForm({ ...form, idProductoOperativo: Number(e.target.value), idProductoBase: null })} required>
                {operativosForm.length === 0 && <option value="">— No hay productos operativos activos —</option>}
                {operativosForm.length > 0 && !form.idProductoOperativo && <option value="">— Seleccione —</option>}
                {operativosForm.map((o) => (<option key={o.idProducto} value={o.idProducto}>{o.codigo} · {o.nombre}</option>))}
              </select>
              {operativosForm.length === 0 && <small className="nota">Primero cree un producto operativo y actívelo (Pricing Studio › Productos operativos › Activar).</small>}
            </label>
            <label>Código <input type="text" maxLength={30} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label>Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <label>Es variante de
              <select value={form.idProductoBase ?? ""} onChange={(e) => setForm({ ...form, idProductoBase: e.target.value ? Number(e.target.value) : null })}>
                <option value="">— Producto base (no es variante) —</option>
                {posiblesBase.map((b) => (<option key={b.idProducto} value={b.idProducto}>{b.codigo} · {b.nombre}</option>))}
              </select>
            </label>
            <div className="formulario__ancho" style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => { if (modo === "nuevo") onCerrar(); else { setModo("ver"); setErrorForm(null); } }}>Cancelar</button>
            </div>
          </form>
        </>
      )}
    </VentanaModal>
  );
}

/** Conjunto de reglas de elegibilidad del producto (motor de reglas, contexto CLIENTE). Visible con RUL_CONSULTA. */
function ElegibilidadProducto({ idProducto, admin }: { idProducto: number; admin: boolean }) {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const [conjuntos, setConjuntos] = useState<ConjuntoReglas[]>([]);
  const [actual, setActual] = useState<number | null>(null);
  const [elegido, setElegido] = useState<number | null>(null);
  const [editando, setEditando] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; texto: string } | null>(null);
  const ver = tienePermiso("RUL_CONSULTA");

  const cargar = useCallback(async () => {
    try {
      const [e, c] = await Promise.all([api.get<{ idConjunto: number | null }>(`/api/productos-comerciales/${idProducto}/elegibilidad`), api.get<ConjuntoReglas[]>("/api/reglas/conjuntos")]);
      setActual(e.data.idConjunto ?? null); setElegido(e.data.idConjunto ?? null);
      setConjuntos(c.data.filter((x) => x.contextoCodigo === "CLIENTE" && (x.estado === "Activo" || x.idConjunto === e.data.idConjunto)));
    } catch { /* sin acceso: no se muestra */ }
  }, [api, idProducto]);
  useEffect(() => { if (ver) void cargar(); }, [ver, cargar]);
  if (!ver) return null;

  async function guardar() {
    setMsg(null);
    try { await api.put(`/api/productos-comerciales/${idProducto}/elegibilidad`, { idConjunto: elegido }); setMsg({ ok: true, texto: "Elegibilidad actualizada." }); setEditando(false); await cargar(); }
    catch (e) { setMsg({ ok: false, texto: mensajeError(e, "No se pudo guardar la elegibilidad.") }); }
  }
  const nombre = conjuntos.find((x) => x.idConjunto === actual);
  return (
    <div className="info-fila">
      <span className="info-fila__etiqueta">Reglas de elegibilidad</span>
      <span className="info-fila__valor">
        {!editando ? (
          <>
            {nombre ? `${nombre.codigo} · ${nombre.nombre}` : "Sin reglas (cualquier cliente)"}{" "}
            {admin && <button type="button" className="boton-secundario" onClick={() => { setMsg(null); setEditando(true); }}>Cambiar</button>}
          </>
        ) : (
          <>
            <select value={elegido ?? ""} onChange={(e) => setElegido(e.target.value ? Number(e.target.value) : null)}>
              <option value="">— Sin reglas —</option>
              {conjuntos.map((x) => (<option key={x.idConjunto} value={x.idConjunto}>{x.codigo} · {x.nombre}</option>))}
            </select>{" "}
            <button type="button" onClick={() => void guardar()}>Guardar</button>{" "}
            <button type="button" className="boton-secundario" onClick={() => { setEditando(false); setElegido(actual); }}>Cancelar</button>
          </>
        )}
        {msg && <small className={msg.ok ? "mensaje-exito" : "mensaje-error"} style={{ marginLeft: "0.5rem" }}>{msg.texto}</small>}
      </span>
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Versiones
function VersionesProducto({ producto, puedeEditar, puedePublicar, onCerrar }: { producto: ProductoComercial; puedeEditar: boolean; puedePublicar: boolean; onCerrar: () => void }) {
  const api = useApiClient();
  const [filas, setFilas] = useState<VersionProducto[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<{ texto: string; idGestion?: number; numero?: string } | null>(null);
  const [editor, setEditor] = useState<number | null>(null);
  const [publicar, setPublicar] = useState<VersionProducto | null>(null);

  const cargar = useCallback(async () => {
    try { setFilas((await api.get<VersionProducto[]>(`/api/productos-comerciales/${producto.idProducto}/versiones`)).data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar las versiones.")); }
  }, [api, producto.idProducto]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<VersionProducto>[] = useMemo(() => [
    { clave: "num", encabezado: "Versión", render: (r) => <strong>v{r.numero}</strong>, valorOrden: (r) => r.numero },
    { clave: "estado", encabezado: "Estado", render: (r) => (r.estado === "Revision" ? "En revisión" : r.estado), textoBusqueda: (r) => r.estado, filtroLista: true },
    { clave: "desde", encabezado: "Vigente desde", render: (r) => fmtFecha(r.desde) },
    { clave: "hasta", encabezado: "Hasta", render: (r) => fmtFecha(r.hasta) },
    { clave: "ges", encabezado: "Gestión", render: (r) => (r.idGestion ? `${r.numeroGestion ?? r.idGestion} (${r.estadoGestion ?? ""})` : "—") },
    { clave: "com", encabezado: "Comentario", render: (r) => r.comentario ?? "" },
    { clave: "por", encabezado: "Creó", render: (r) => r.creadoPor },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const v = filas.find((x) => x.idVersion === idSel) ?? null;

  async function accion(fn: () => Promise<unknown>, ok: string) {
    setError(null); setExito(null);
    try { await fn(); setExito({ texto: ok }); await cargar(); }
    catch (e) { setError(mensajeError(e, "No se pudo completar la acción.")); }
  }

  return (
    <VentanaModal titulo={`Versiones · ${producto.codigo} ${producto.nombre}`} onCerrar={onCerrar} ancho={1000}>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito.texto} {exito.idGestion && <Link to="/gestiones/consulta">Ver gestión {exito.numero ?? exito.idGestion}</Link>}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idVersion} seleccion={sel} onCambiarSeleccion={setSel}
        mensajeVacio="Aún no hay versiones: cree la primera."
        comandos={[
          { clave: "nueva", etiqueta: "Nueva versión", icono: "+", oculto: !puedeEditar || producto.estado !== "Activo", onClick: () => void accion(async () => {
              const r = await api.post<{ idVersion: number }>(`/api/productos-comerciales/${producto.idProducto}/versiones`); setEditor(r.data.idVersion); }, "Borrador creado (copia de la última versión).") },
          { clave: "ver", etiqueta: v?.estado === "Borrador" && puedeEditar ? "Editar condiciones" : "Ver condiciones", icono: "✎", requiereSeleccion: "una", onClick: () => { if (v) setEditor(v.idVersion); } },
          { clave: "publicar", etiqueta: "Publicar", icono: "➤", requiereSeleccion: "una", oculto: !puedePublicar, deshabilitado: v?.estado !== "Borrador", onClick: () => { if (v) setPublicar(v); } },
          { clave: "reabrir", etiqueta: "Volver a borrador", icono: "↩", requiereSeleccion: "una", oculto: !puedeEditar, deshabilitado: v?.estado !== "Revision" || v?.estadoGestion === "Pendiente de Aprobacion", onClick: () => { if (v) void accion(() => api.post(`/api/productos-comerciales/versiones/${v.idVersion}/reabrir`), "Versión devuelta a borrador."); } },
          { clave: "descartar", etiqueta: "Descartar borrador", icono: "🗑", requiereSeleccion: "una", oculto: !puedeEditar, deshabilitado: v?.estado !== "Borrador", onClick: () => { if (v && window.confirm(`¿Descartar el borrador v${v.numero}?`)) void accion(() => api.delete(`/api/productos-comerciales/versiones/${v.idVersion}`), "Borrador descartado."); } },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />
      {editor != null && <EditorVersion idVersion={editor} producto={producto} puedeEditar={puedeEditar} onCerrar={() => { setEditor(null); void cargar(); }} />}
      {publicar && (
        <PublicarVersion version={publicar} onCerrar={() => setPublicar(null)}
          onEnviada={(texto, idGestion, numero) => { setPublicar(null); setExito({ texto, idGestion, numero }); void cargar(); }} />
      )}
    </VentanaModal>
  );
}

function PublicarVersion({ version, onCerrar, onEnviada }: { version: VersionProducto; onCerrar: () => void; onEnviada: (texto: string, idGestion: number, numero?: string) => void }) {
  const api = useApiClient();
  const [fecha, setFecha] = useState(hoyIso());
  const [comentario, setComentario] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  async function enviar(e: FormEvent) {
    e.preventDefault(); setEnviando(true); setError(null);
    try {
      const r = await api.post<{ idGestion: number; numero?: string }>(`/api/productos-comerciales/versiones/${version.idVersion}/publicar`, { fechaVigenciaDesde: fecha, comentario: comentario.trim() || null });
      onEnviada(`Versión v${version.numero} enviada a aprobación: rige desde ${fmtFecha(fecha)} (o desde la aprobación, si es posterior).`, r.data.idGestion, r.data.numero);
    } catch (err) { setError(mensajeError(err, "No se pudo publicar.")); }
    finally { setEnviando(false); }
  }
  return (
    <PanelModal onCerrar={onCerrar}>
      <h3>Publicar versión v{version.numero}</h3>
      <p className="nota">Se enviará a aprobación. Al aprobarse queda programada (si la fecha es futura) o vigente, y la versión anterior expira un día antes.</p>
      {error && <p className="mensaje-error">{error}</p>}
      <form className="formulario" onSubmit={enviar}>
        <label>Vigente desde <input type="date" min={hoyIso()} value={fecha} onChange={(e) => setFecha(e.target.value)} required /></label>
        <label>Comentario <input type="text" maxLength={500} value={comentario} onChange={(e) => setComentario(e.target.value)} /></label>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button type="submit" disabled={enviando}>{enviando ? "Enviando..." : "Enviar a aprobación"}</button>
          <button type="button" className="boton-secundario" onClick={onCerrar}>Cancelar</button>
        </div>
      </form>
    </PanelModal>
  );
}

// ------------------------------------------------------------------------------------------------ Editor de condiciones
const NOMBRE_GRUPO: Record<string, string> = { Monto: "Montos", Plazo: "Plazos", Tasa: "Tasa de interés", PlanPago: "Plan de pagos", Comision: "Comisiones y cargos", Mora: "Mora", Seguro: "Seguros", Garantia: "Garantías", Gracia: "Períodos de gracia", General: "General" };

function EditorVersion({ idVersion, producto, puedeEditar, onCerrar }: { idVersion: number; producto: ProductoComercial; puedeEditar: boolean; onCerrar: () => void }) {
  const api = useApiClient();
  const [det, setDet] = useState<VersionDetalle | null>(null);
  const [valores, setValores] = useState<Record<number, string>>({});
  const [listas, setListas] = useState<Record<string, CatalogoItemResponse[]>>({});
  const [indicadores, setIndicadores] = useState<Indicador[]>([]);
  const [bases, setBases] = useState<BaseCalculo[]>([]);
  const [comentario, setComentario] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    try {
      const d = (await api.get<VersionDetalle>(`/api/productos-comerciales/versiones/${idVersion}`)).data;
      setDet(d); setComentario(d.version.comentario ?? "");
      setValores(Object.fromEntries(d.valores.filter((x) => x.valor != null).map((x) => [x.idParametro, x.valor as string])));
      const codigos = Array.from(new Set(d.valores.map((x) => x.listaCodigo).filter((c): c is string => !!c)));
      const cargadas = await Promise.all(codigos.map(async (c) => [c, (await api.get<CatalogoItemResponse[]>(`/api/catalogos/listas/${c}`)).data] as const));
      setListas(Object.fromEntries(cargadas));
      if (d.valores.some((x) => x.codigo === "indicador_referencia")) setIndicadores((await api.get<Indicador[]>("/api/indicadores")).data.filter((i) => i.estado === "Activo"));
      if (d.valores.some((x) => x.codigo === "base_calculo")) setBases((await api.get<BaseCalculo[]>("/api/bases-calculo")).data.filter((b) => b.estado === "Activo"));
    } catch (e) { setError(mensajeError(e, "No se pudo cargar la versión.")); }
  }, [api, idVersion]);
  useEffect(() => { void cargar(); }, [cargar]);

  const editable = puedeEditar && det?.version.estado === "Borrador";
  const grupos = useMemo(() => {
    const m = new Map<string, VersionValor[]>();
    for (const x of det?.valores ?? []) m.set(x.grupo, [...(m.get(x.grupo) ?? []), x]);
    return Array.from(m.entries());
  }, [det]);

  function campo(x: VersionValor) {
    const val = valores[x.idParametro] ?? "";
    const poner = (nuevo: string) => setValores((s) => ({ ...s, [x.idParametro]: nuevo }));
    const ph = x.valorHeredado ?? x.valorDefecto ?? "";
    if (!editable) return <span>{val || (x.valorHeredado ? `${x.valorHeredado} (heredado)` : x.valorDefecto ? `${x.valorDefecto} (por defecto)` : "—")}</span>;
    const opciones: { valor: string; texto: string }[] | null =
      x.tipoDato === "Lista" ? (listas[x.listaCodigo ?? ""] ?? []).map((i) => ({ valor: i.codigo, texto: i.nombre }))
      : x.tipoDato === "Booleano" ? [{ valor: "true", texto: "Sí" }, { valor: "false", texto: "No" }]
      : x.codigo === "indicador_referencia" ? indicadores.map((i) => ({ valor: i.codigo, texto: `${i.codigo} · ${i.nombre}` }))
      : x.codigo === "base_calculo" ? bases.map((b) => ({ valor: b.codigo, texto: b.nombre }))
      : null;
    if (opciones) return (
      <select value={val} onChange={(e) => poner(e.target.value)}>
        <option value="">{ph ? `— Usar ${ph} —` : "— Sin valor —"}</option>
        {opciones.map((o) => (<option key={o.valor} value={o.valor}>{o.texto}</option>))}
      </select>
    );
    if (x.tipoDato === "Fecha") return <input type="date" value={val} onChange={(e) => poner(e.target.value)} />;
    return <input type="text" inputMode={x.tipoDato === "Texto" ? "text" : "decimal"} value={val} placeholder={ph} onChange={(e) => poner(e.target.value)} maxLength={500} style={{ width: "14rem" }} />;
  }

  async function guardar() {
    if (!det) return;
    setGuardando(true); setError(null); setExito(null);
    try {
      await api.put(`/api/productos-comerciales/versiones/${idVersion}/valores`, {
        valores: det.valores.map((x) => ({ idParametro: x.idParametro, valor: (valores[x.idParametro] ?? "").trim() || null })), comentario,
      });
      setExito("Condiciones guardadas."); await cargar();
    } catch (e) { setError(mensajeError(e, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  const v = det?.version;
  return (
    <VentanaModal titulo={`Condiciones · ${producto.codigo} v${v?.numero ?? ""}${v ? ` (${v.estado === "Revision" ? "en revisión" : v.estado})` : ""}`} onCerrar={onCerrar} ancho={900}
      acciones={editable ? <button type="button" onClick={() => void guardar()} disabled={guardando}>{guardando ? "Guardando..." : "Guardar condiciones"}</button> : undefined}>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      {producto.baseNombre && <p className="nota">Variante de <strong>{producto.baseNombre}</strong>: lo que deje vacío se hereda de su versión vigente.</p>}
      {!det && !error && <p>Cargando…</p>}
      {grupos.map(([g, items]) => (
        <section key={g} style={{ marginBottom: "1rem" }}>
          <h4>{NOMBRE_GRUPO[g] ?? g}</h4>
          <table className="tabla">
            <tbody>
              {items.map((x) => (
                <tr key={x.idParametro}>
                  <td style={{ width: "42%" }}>{x.nombre}{x.obligatorio ? " *" : ""}
                    {x.esComponenteEconomico && <small style={{ display: "block", opacity: 0.7 }}>{x.tipoCalculoEconomico === "Porcentaje" ? `% sobre ${x.baseComponenteNombre}` : "Monto fijo"}</small>}
                    {(x.minimo != null || x.maximo != null) && <small style={{ display: "block", opacity: 0.7 }}>Entre {x.minimo ?? "…"} y {x.maximo ?? "…"}</small>}
                  </td>
                  <td>{campo(x)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
      {editable && (
        <label style={{ display: "block" }}>Comentario de la versión <input type="text" maxLength={500} value={comentario} onChange={(e) => setComentario(e.target.value)} style={{ width: "100%" }} /></label>
      )}
    </VentanaModal>
  );
}
