import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { PanelModal, VentanaModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError, parseDecimal } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import type { CatalogoItemResponse } from "../api/tipos";
import type { Calendario, CategoriaRegulatoria, ParametroProducto, ProductoOperativo } from "../api/tiposCreditos";

interface MonedaOpc { idMoneda: number; codigo: string; nombre: string; estado: string }
interface Form {
  codigo: string; nombre: string; descripcion: string; dominio: string; idCategoria: number; idCategoriaRegulatoria: number | null; estrategiaVersionado: string;
  tipoPersona: string; idCalendario: number | null; monedas: Record<number, boolean>; principal: number; sembrarPlantilla: boolean;
}
interface Catalogos { monedas: MonedaOpc[]; calendarios: Calendario[]; categoriasReg: CategoriaRegulatoria[]; catsCredito: CatalogoItemResponse[]; catsCuenta: CatalogoItemResponse[] }
type Modo = "ver" | "editar" | "nuevo";
type Pestana = "datos" | "parametros";

const vacio = (): Form => ({ codigo: "", nombre: "", descripcion: "", dominio: "Credito", idCategoria: 0, idCategoriaRegulatoria: null, estrategiaVersionado: "FijoAlOrigen", tipoPersona: "Ambas", idCalendario: null, monedas: {}, principal: 0, sembrarPlantilla: true });
function formNuevo(cat: Catalogos): Form {
  const f = vacio();
  const base = cat.monedas.find((m) => m.estado === "Activo");
  if (base) { f.monedas = { [base.idMoneda]: true }; f.principal = base.idMoneda; }
  f.idCategoria = cat.catsCredito[0]?.id ?? 0;
  return f;
}
function formDe(p: ProductoOperativo): Form {
  return {
    codigo: p.codigo, nombre: p.nombre, descripcion: p.descripcion ?? "", dominio: p.dominio, idCategoria: p.idCategoria, idCategoriaRegulatoria: p.idCategoriaRegulatoria ?? null,
    estrategiaVersionado: p.estrategiaVersionado, tipoPersona: p.tipoPersona, idCalendario: p.idCalendario ?? null,
    monedas: Object.fromEntries(p.monedas.map((m) => [m.idMoneda, true])), principal: p.monedas.find((m) => m.esPrincipal)?.idMoneda ?? 0, sembrarPlantilla: false,
  };
}

/** Pricing Studio › Productos operativos (consulta PRD_CONSULTA; mantenimiento PRD_ADMIN). Estructura del producto y catálogo de parámetros. */
export function ProductosOperativosPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("PRD_ADMIN");
  const [filas, setFilas] = useState<ProductoOperativo[]>([]);
  const [cat, setCat] = useState<Catalogos>({ monedas: [], calendarios: [], categoriasReg: [], catsCredito: [], catsCuenta: [] });
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [ficha, setFicha] = useState<{ id: number | null; modo: Modo; pestana: Pestana; aviso?: string } | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [p, m, c, r, a, b] = await Promise.all([
        api.get<ProductoOperativo[]>("/api/productos-operativos"), api.get<MonedaOpc[]>("/api/catalogos/monedas"), api.get<Calendario[]>("/api/calendarios"),
        api.get<CategoriaRegulatoria[]>("/api/categorias-regulatorias"), api.get<CatalogoItemResponse[]>("/api/catalogos/listas/CATEGORIA_PRODUCTO_CREDITO"),
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/CATEGORIA_PRODUCTO_CUENTA"),
      ]);
      setFilas(p.data);
      setCat({ monedas: m.data, calendarios: c.data.filter((x) => x.estado === "Activo"), categoriasReg: r.data.filter((x) => x.estado === "Activo"), catsCredito: a.data, catsCuenta: b.data });
      setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los productos.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<ProductoOperativo>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Producto", render: (r) => (
        <button type="button" className="boton-enlace-id" title="Consultar el producto" style={{ opacity: r.estado === "Inactivo" ? 0.55 : 1 }}
          onClick={() => { setExito(null); setFicha({ id: r.idProducto, modo: "ver", pestana: "datos" }); }}>{r.nombre}</button>
      ), textoBusqueda: (r) => r.nombre },
    { clave: "dominio", encabezado: "Dominio", render: (r) => (r.dominio === "Credito" ? "Crédito" : "Cuenta"), textoBusqueda: (r) => r.dominio, filtroLista: true },
    { clave: "cat", encabezado: "Categoría", render: (r) => r.categoriaNombre, textoBusqueda: (r) => r.categoriaNombre, filtroLista: true },
    { clave: "mon", encabezado: "Monedas", render: (r) => r.monedas.map((m) => m.codigo + (m.esPrincipal ? "*" : "")).join(", "), textoBusqueda: (r) => r.monedas.map((m) => m.codigo).join(" ") },
    { clave: "est", encabezado: "Versionado", render: (r) => (r.estrategiaVersionado === "Dinamico" ? "Dinámico" : "Fijo al origen"), textoBusqueda: (r) => r.estrategiaVersionado, filtroLista: true },
    { clave: "par", encabezado: "Parámetros", render: (r) => r.cantidadParametros, valorOrden: (r) => r.cantidadParametros },
    { clave: "com", encabezado: "Comerciales", render: (r) => r.cantidadComerciales, valorOrden: (r) => r.cantidadComerciales },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const p = filas.find((x) => x.idProducto === idSel) ?? null;
  const productoFicha = ficha?.id != null ? filas.find((x) => x.idProducto === ficha.id) ?? null : null;

  async function estado(e: "Borrador" | "Activo" | "Inactivo") {
    if (!p) return;
    setExito(null); setError(null);
    try { await api.put(`/api/productos-operativos/${p.idProducto}/estado`, { estado: e }); setExito(`Producto ${e === "Activo" ? "activado" : e === "Inactivo" ? "inactivado" : "devuelto a borrador"}.`); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Productos operativos</h1>
      <p className="nota">Lo que el producto <em>es</em> y puede hacer: dominio, categoría, monedas, calendario y los parámetros que se podrán configurar. Los valores (tasas, plazos, comisiones) se definen en los productos comerciales, por versión. Pulse el nombre de un producto para consultarlo y editarlo.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idProducto} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay productos operativos." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !admin, onClick: () => { setExito(null); setFicha({ id: null, modo: "nuevo", pestana: "datos" }); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: p?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: p?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />

      {ficha && (ficha.id == null || productoFicha) && (
        <FichaProductoOperativo key={ficha.id ?? "nuevo"} producto={productoFicha} modoInicial={ficha.modo} pestanaInicial={ficha.pestana} avisoInicial={ficha.aviso}
          admin={admin} cat={cat} onCerrar={() => setFicha(null)} onRefrescar={cargar}
          onCreado={async (id) => { await cargar(); setFicha({ id, modo: "editar", pestana: "parametros", aviso: "Producto operativo creado en Borrador. Revise sus parámetros y actívelo desde la lista." }); }} />
      )}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Ficha
function FichaProductoOperativo({ producto, modoInicial, pestanaInicial, avisoInicial, admin, cat, onCerrar, onRefrescar, onCreado }: {
  producto: ProductoOperativo | null; modoInicial: Modo; pestanaInicial: Pestana; avisoInicial?: string; admin: boolean; cat: Catalogos;
  onCerrar: () => void; onRefrescar: () => Promise<void>; onCreado: (id: number) => Promise<void>;
}) {
  const api = useApiClient();
  const [modo, setModo] = useState<Modo>(modoInicial);
  const [pestana, setPestana] = useState<Pestana>(pestanaInicial);
  const [form, setForm] = useState<Form>(() => (producto ? formDe(producto) : formNuevo(cat)));
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(avisoInicial ?? null);
  const [guardando, setGuardando] = useState(false);
  const categorias = form.dominio === "Credito" ? cat.catsCredito : cat.catsCuenta;

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const ids = Object.entries(form.monedas).filter(([, v]) => v).map(([k]) => Number(k));
    const cuerpo = {
      codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion.trim() || null, dominio: form.dominio, idCategoria: form.idCategoria,
      idCategoriaRegulatoria: form.idCategoriaRegulatoria, estrategiaVersionado: form.estrategiaVersionado, tipoPersona: form.tipoPersona, idCalendario: form.idCalendario,
      monedas: ids.map((id) => ({ idMoneda: id, esPrincipal: id === form.principal })), sembrarPlantilla: form.sembrarPlantilla,
    };
    try {
      if (modo === "nuevo") {
        const r = await api.post<{ idProducto: number }>("/api/productos-operativos", cuerpo);
        await onCreado(r.data.idProducto);
        return;
      }
      if (producto) await api.put(`/api/productos-operativos/${producto.idProducto}`, cuerpo);
      setAviso("Producto operativo actualizado."); setModo("ver"); await onRefrescar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  const titulo = producto ? `${producto.codigo} · ${producto.nombre}` : "Nuevo producto operativo";
  const nombreMoneda = (m: { codigo: string; nombre: string; esPrincipal: boolean }) => `${m.codigo} · ${m.nombre}${m.esPrincipal ? " (principal)" : ""}`;
  const dato = (etiqueta: string, valor: React.ReactNode) => (<div className="info-fila"><span className="info-fila__etiqueta">{etiqueta}</span><span className="info-fila__valor">{valor}</span></div>);

  return (
    <VentanaModal titulo={titulo} onCerrar={onCerrar} ancho={1100}
      acciones={modo === "ver" && admin && producto ? (
        <button type="button" onClick={() => { setForm(formDe(producto)); setErrorForm(null); setAviso(null); setPestana("datos"); setModo("editar"); }}>✎ Editar</button>
      ) : undefined}>
      {producto && <p className="nota" style={{ marginTop: 0 }}>Estado: <strong>{producto.estado}</strong>{modo === "editar" ? " · modo edición" : ""}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      <div className="v360__pestanas" role="tablist">
        <button type="button" role="tab" aria-selected={pestana === "datos"} className={`v360__pestana ${pestana === "datos" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("datos")}>Datos del producto</button>
        <button type="button" role="tab" aria-selected={pestana === "parametros"} className={`v360__pestana ${pestana === "parametros" ? "v360__pestana--activa" : ""}`}
          onClick={() => setPestana("parametros")}>Parámetros{producto ? ` (${producto.cantidadParametros})` : ""}</button>
      </div>

      {pestana === "datos" && modo === "ver" && producto && (
        <div>
          {dato("Código", producto.codigo)}
          {dato("Nombre", producto.nombre)}
          {dato("Descripción", producto.descripcion || "—")}
          {dato("Dominio", producto.dominio === "Credito" ? "Crédito" : "Cuenta")}
          {dato("Categoría", producto.categoriaNombre)}
          {dato("Categoría regulatoria", producto.categoriaRegulatoriaNombre || "—")}
          {dato("Estrategia de versionado", producto.estrategiaVersionado === "Dinamico" ? "Dinámico: el crédito sigue la versión vigente" : "Fijo al origen: el crédito conserva las condiciones con que nació")}
          {dato("Tipo de persona", producto.tipoPersona === "Fisica" ? "Solo física" : producto.tipoPersona === "Juridica" ? "Solo jurídica" : "Física y jurídica")}
          {dato("Calendario de pagos", producto.calendarioNombre || "El calendario por defecto de la compañía")}
          {dato("Monedas permitidas", producto.monedas.map(nombreMoneda).join(" · ") || "—")}
          {dato("Productos comerciales", producto.cantidadComerciales)}
        </div>
      )}

      {pestana === "datos" && modo !== "ver" && (
        <>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código <input type="text" maxLength={30} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label>Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <label>Dominio
              <select value={form.dominio} onChange={(e) => { const d = e.target.value; setForm({ ...form, dominio: d, idCategoria: (d === "Credito" ? cat.catsCredito : cat.catsCuenta)[0]?.id ?? 0 }); }} disabled={modo === "editar" && (producto?.cantidadComerciales ?? 0) > 0}>
                <option value="Credito">Crédito</option><option value="Cuenta">Cuenta</option>
              </select>
            </label>
            <label>Categoría
              <select value={form.idCategoria} onChange={(e) => setForm({ ...form, idCategoria: Number(e.target.value) })} required>
                {categorias.map((c) => (<option key={c.id} value={c.id}>{c.nombre}</option>))}
              </select>
            </label>
            <label>Categoría regulatoria
              <select value={form.idCategoriaRegulatoria ?? ""} onChange={(e) => setForm({ ...form, idCategoriaRegulatoria: e.target.value ? Number(e.target.value) : null })}>
                <option value="">— Sin categoría —</option>
                {cat.categoriasReg.map((c) => (<option key={c.idCategoria} value={c.idCategoria}>{c.codigo} · {c.nombre}</option>))}
              </select>
            </label>
            <label>Estrategia de versionado
              <select value={form.estrategiaVersionado} onChange={(e) => setForm({ ...form, estrategiaVersionado: e.target.value })}>
                <option value="FijoAlOrigen">Fijo al origen: el crédito conserva las condiciones con que nació</option>
                <option value="Dinamico">Dinámico: el crédito sigue la versión vigente</option>
              </select>
            </label>
            <label>Tipo de persona
              <select value={form.tipoPersona} onChange={(e) => setForm({ ...form, tipoPersona: e.target.value })}>
                <option value="Ambas">Física y jurídica</option><option value="Fisica">Solo física</option><option value="Juridica">Solo jurídica</option>
              </select>
            </label>
            <label>Calendario de pagos
              <select value={form.idCalendario ?? ""} onChange={(e) => setForm({ ...form, idCalendario: e.target.value ? Number(e.target.value) : null })}>
                <option value="">— El calendario por defecto de la compañía —</option>
                {cat.calendarios.map((c) => (<option key={c.idCalendario} value={c.idCalendario}>{c.nombre}</option>))}
              </select>
            </label>
            <fieldset style={{ border: "none", padding: 0 }}>
              <legend>Monedas permitidas (● = principal)</legend>
              {cat.monedas.filter((m) => m.estado === "Activo").map((m) => (
                <div key={m.idMoneda} style={{ display: "flex", gap: "0.6rem", alignItems: "center" }}>
                  <input type="checkbox" checked={!!form.monedas[m.idMoneda]} onChange={(e) => setForm({ ...form, monedas: { ...form.monedas, [m.idMoneda]: e.target.checked }, principal: e.target.checked ? (form.principal || m.idMoneda) : (form.principal === m.idMoneda ? 0 : form.principal) })} />
                  <input type="radio" name="principal" disabled={!form.monedas[m.idMoneda]} checked={form.principal === m.idMoneda} onChange={() => setForm({ ...form, principal: m.idMoneda })} />
                  <span>{m.codigo} · {m.nombre}</span>
                </div>
              ))}
            </fieldset>
            {modo === "nuevo" && (
              <label className="formulario__check formulario__ancho">
                <input type="checkbox" checked={form.sembrarPlantilla} onChange={(e) => setForm({ ...form, sembrarPlantilla: e.target.checked })} /> Crear con la plantilla estándar de parámetros (montos, plazos, tasa, plan de pagos, gracia, mora, comisiones). Al guardar pasará a la pestaña Parámetros para ajustarlos.
              </label>
            )}
            <div className="formulario__ancho" style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : modo === "nuevo" ? "Guardar y definir parámetros" : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => { if (modo === "nuevo") onCerrar(); else { setModo("ver"); setErrorForm(null); } }}>Cancelar</button>
            </div>
          </form>
        </>
      )}

      {pestana === "parametros" && (producto ? (
        <ParametrosProducto producto={producto} puedeEditar={admin && modo === "editar"} onCambio={() => void onRefrescar()} />
      ) : (
        <p className="nota">Guarde primero los datos del producto: después podrá definir sus parámetros aquí mismo (con la plantilla estándar ya se crean con valores razonables).</p>
      ))}
    </VentanaModal>
  );
}

// ------------------------------------------------------------------------------------------------ Parámetros
interface FormPar {
  codigo: string; nombre: string; descripcion: string; grupo: string; tipoDato: string; idListaTipo: number | null; minimo: string; maximo: string; obligatorio: boolean; valorDefecto: string;
  esComponenteEconomico: boolean; tipoCalculoEconomico: string; idBaseComponente: number | null; codigoTipoEvento: string; orden: string;
}
const GRUPOS = ["Monto", "Plazo", "Tasa", "PlanPago", "Comision", "Mora", "Seguro", "Garantia", "Gracia", "General"];
const TIPOS = ["Texto", "Entero", "Decimal", "Booleano", "Fecha", "Lista"];
const parVacio = (orden: number): FormPar => ({ codigo: "", nombre: "", descripcion: "", grupo: "General", tipoDato: "Texto", idListaTipo: null, minimo: "", maximo: "", obligatorio: false, valorDefecto: "", esComponenteEconomico: false, tipoCalculoEconomico: "MontoFijo", idBaseComponente: null, codigoTipoEvento: "", orden: String(orden) });

function ParametrosProducto({ producto, puedeEditar, onCambio }: { producto: ProductoOperativo; puedeEditar: boolean; onCambio: () => void }) {
  const api = useApiClient();
  const [filas, setFilas] = useState<ParametroProducto[]>([]);
  const [listas, setListas] = useState<{ id: number; codigo: string; nombre: string }[]>([]);
  const [bases, setBases] = useState<CatalogoItemResponse[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState<FormPar>(parVacio(10));
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    try {
      const [p, l, b] = await Promise.all([
        api.get<ParametroProducto[]>(`/api/productos-operativos/${producto.idProducto}/parametros`),
        api.get<{ id: number; codigo: string; nombre: string }[]>("/api/productos-operativos/listas"),
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/BASE_COMPONENTE_ECONOMICO"),
      ]);
      setFilas(p.data); setListas(l.data); setBases(b.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los parámetros.")); }
  }, [api, producto.idProducto]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<ParametroProducto>[] = useMemo(() => [
    { clave: "orden", encabezado: "#", render: (r) => r.orden, valorOrden: (r) => r.orden },
    { clave: "codigo", encabezado: "Código", render: (r) => <code>{r.codigo}</code>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Parámetro", render: (r) => <span style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}>{r.nombre}{r.obligatorio ? " *" : ""}</span>, textoBusqueda: (r) => r.nombre },
    { clave: "grupo", encabezado: "Grupo", render: (r) => r.grupo, textoBusqueda: (r) => r.grupo, filtroLista: true },
    { clave: "tipo", encabezado: "Tipo", render: (r) => (r.tipoDato === "Lista" ? `Lista: ${r.listaNombre}` : r.tipoDato), textoBusqueda: (r) => r.tipoDato },
    { clave: "lim", encabezado: "Límites", render: (r) => (r.minimo == null && r.maximo == null ? "—" : `${r.minimo ?? "…"} a ${r.maximo ?? "…"}`) },
    { clave: "eco", encabezado: "Componente económico", render: (r) => (r.esComponenteEconomico ? (r.tipoCalculoEconomico === "Porcentaje" ? `% sobre ${r.baseComponenteNombre}` : "Monto fijo") : "—"), textoBusqueda: (r) => r.tipoCalculoEconomico ?? "" },
    { clave: "def", encabezado: "Por defecto", render: (r) => r.valorDefecto ?? "—" },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const par = filas.find((x) => x.idParametro === idSel) ?? null;

  async function guardar(e: FormEvent) {
    e.preventDefault(); setErrorForm(null);
    const min = form.minimo.trim() === "" ? null : parseDecimal(form.minimo);
    const max = form.maximo.trim() === "" ? null : parseDecimal(form.maximo);
    if ((form.minimo.trim() !== "" && min == null) || (form.maximo.trim() !== "" && max == null)) { setErrorForm("Los límites deben ser numéricos."); return; }
    const cuerpo = {
      codigo: form.codigo.trim(), nombre: form.nombre, descripcion: form.descripcion.trim() || null, grupo: form.grupo, tipoDato: form.tipoDato,
      idListaTipo: form.tipoDato === "Lista" ? form.idListaTipo : null, minimo: ["Entero", "Decimal"].includes(form.tipoDato) ? min : null, maximo: ["Entero", "Decimal"].includes(form.tipoDato) ? max : null,
      obligatorio: form.obligatorio, valorDefecto: form.valorDefecto.trim() || null, esComponenteEconomico: form.esComponenteEconomico,
      tipoCalculoEconomico: form.esComponenteEconomico ? form.tipoCalculoEconomico : null,
      idBaseComponente: form.esComponenteEconomico && form.tipoCalculoEconomico === "Porcentaje" ? form.idBaseComponente : null,
      codigoTipoEvento: form.codigoTipoEvento.trim() || null, orden: Number(form.orden) || 0,
    };
    setGuardando(true);
    try {
      if (panel === "nuevo") await api.post(`/api/productos-operativos/${producto.idProducto}/parametros`, cuerpo);
      else if (par) await api.put(`/api/productos-operativos/${producto.idProducto}/parametros/${par.idParametro}`, cuerpo);
      setExito(panel === "nuevo" ? "Parámetro creado." : "Parámetro actualizado.");
      setPanel(null); await cargar(); onCambio();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }
  async function estado(e: "Activo" | "Inactivo") {
    if (!par) return;
    setExito(null); setError(null);
    try { await api.put(`/api/productos-operativos/${producto.idProducto}/parametros/${par.idParametro}/estado`, { estado: e }); await cargar(); onCambio(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      {!puedeEditar && <p className="nota" style={{ marginTop: 0 }}>Consulta. Para agregar o cambiar parámetros pulse «Editar» en la parte inferior de la ficha.</p>}
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idParametro} seleccion={sel} onCambiarSeleccion={setSel} filtrosPorColumna
        mensajeVacio="Este producto aún no tiene parámetros."
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !puedeEditar, onClick: () => { setExito(null); setErrorForm(null); setForm(parVacio((filas.at(-1)?.orden ?? 0) + 10)); setPanel("nuevo"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", oculto: !puedeEditar, onClick: () => {
            if (!par) return; setExito(null); setErrorForm(null);
            setForm({ codigo: par.codigo, nombre: par.nombre, descripcion: par.descripcion ?? "", grupo: par.grupo, tipoDato: par.tipoDato, idListaTipo: par.idListaTipo ?? null, minimo: par.minimo?.toString() ?? "", maximo: par.maximo?.toString() ?? "",
              obligatorio: par.obligatorio, valorDefecto: par.valorDefecto ?? "", esComponenteEconomico: par.esComponenteEconomico, tipoCalculoEconomico: par.tipoCalculoEconomico ?? "MontoFijo",
              idBaseComponente: par.idBaseComponente ?? null, codigoTipoEvento: par.codigoTipoEvento ?? "", orden: String(par.orden) });
            setPanel("editar"); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !puedeEditar, deshabilitado: par?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !puedeEditar, deshabilitado: par?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
        ]} />
      {panel && (
        <PanelModal onCerrar={() => setPanel(null)} ancho={680}>
          <h3>{panel === "nuevo" ? "Nuevo parámetro" : `Editar ${par?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código (letras, números y _) <input type="text" maxLength={60} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label>Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <label>Grupo <select value={form.grupo} onChange={(e) => setForm({ ...form, grupo: e.target.value })}>{GRUPOS.map((g) => (<option key={g}>{g}</option>))}</select></label>
            <label>Tipo de dato
              <select value={form.tipoDato} onChange={(e) => setForm({ ...form, tipoDato: e.target.value, esComponenteEconomico: e.target.value === "Decimal" ? form.esComponenteEconomico : false })}>{TIPOS.map((t) => (<option key={t}>{t}</option>))}</select>
            </label>
            {form.tipoDato === "Lista" && (
              <label>Lista de valores
                <select value={form.idListaTipo ?? ""} onChange={(e) => setForm({ ...form, idListaTipo: e.target.value ? Number(e.target.value) : null })} required>
                  <option value="">— Elija —</option>
                  {listas.map((l) => (<option key={l.id} value={l.id}>{l.nombre}</option>))}
                </select>
              </label>
            )}
            {["Entero", "Decimal"].includes(form.tipoDato) && (
              <div className="formulario-en-linea">
                <label>Mínimo <input type="text" inputMode="decimal" value={form.minimo} onChange={(e) => setForm({ ...form, minimo: e.target.value })} /></label>
                <label>Máximo <input type="text" inputMode="decimal" value={form.maximo} onChange={(e) => setForm({ ...form, maximo: e.target.value })} /></label>
              </div>
            )}
            <label>Valor por defecto (código del valor si es lista) <input type="text" maxLength={500} value={form.valorDefecto} onChange={(e) => setForm({ ...form, valorDefecto: e.target.value })} /></label>
            <label style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}><input type="checkbox" checked={form.obligatorio} onChange={(e) => setForm({ ...form, obligatorio: e.target.checked })} /> Obligatorio para publicar una versión</label>
            {form.tipoDato === "Decimal" && (
              <label style={{ display: "flex", gap: "0.4rem", alignItems: "center" }}><input type="checkbox" checked={form.esComponenteEconomico} onChange={(e) => setForm({ ...form, esComponenteEconomico: e.target.checked })} /> Es un componente económico (comisión, seguro, cargo)</label>
            )}
            {form.esComponenteEconomico && (
              <>
                <label>Se calcula como
                  <select value={form.tipoCalculoEconomico} onChange={(e) => setForm({ ...form, tipoCalculoEconomico: e.target.value })}>
                    <option value="MontoFijo">Monto fijo</option><option value="Porcentaje">Porcentaje sobre una base</option>
                  </select>
                </label>
                {form.tipoCalculoEconomico === "Porcentaje" && (
                  <label>Base del porcentaje
                    <select value={form.idBaseComponente ?? ""} onChange={(e) => setForm({ ...form, idBaseComponente: e.target.value ? Number(e.target.value) : null })} required>
                      <option value="">— Elija —</option>
                      {bases.map((b) => (<option key={b.id} value={b.id}>{b.nombre}</option>))}
                    </select>
                  </label>
                )}
                <label>Evento contable (código, opcional) <input type="text" maxLength={100} value={form.codigoTipoEvento} onChange={(e) => setForm({ ...form, codigoTipoEvento: e.target.value })} /></label>
              </>
            )}
            <label>Orden <input type="number" value={form.orden} onChange={(e) => setForm({ ...form, orden: e.target.value })} style={{ width: "6rem" }} /></label>
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
