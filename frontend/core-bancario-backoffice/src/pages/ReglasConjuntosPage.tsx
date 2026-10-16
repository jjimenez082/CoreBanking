import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { PanelModal, VentanaModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { ResultadoReglas } from "../components/TrazaReglas";
import { fmtFecha, hoyIso, mensajeError } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import { EditorReglasVersion } from "./ReglasEditorVersion";
import type { CatalogoItemResponse } from "../api/tipos";
import type {
  CasoPrueba, CasosEjecutar, ConjuntoReferencia, ConjuntoReglas, ReglaAtributo, ReglaCatalogos, ReglaEvaluacion, ReglasVersionDetalle, VersionConjunto,
} from "../api/reglas";

type Modo = "ver" | "editar" | "nuevo";
interface Form { idContexto: number; codigo: string; nombre: string; descripcion: string; politicaResolucion: string }
const POLITICAS = [
  { valor: "PrimeraCoincidencia", texto: "Primera coincidencia", ayuda: "Se aplica la primera regla que se cumple, en el orden definido." },
  { valor: "AcumularTodas", texto: "Acumular todas", ayuda: "Se evalúan todas las reglas; prevalece Rechazar sobre Referir y Referir sobre Aprobar." },
];
const textoPolitica = (v: string) => POLITICAS.find((p) => p.valor === v)?.texto ?? v;

/** Conjuntos de reglas (RUL_CONSULTA ver; RUL_ADMIN mantener; RUL_PUBLICAR publicar por gestión; RUL_EVALUAR probar). */
export function ReglasConjuntosPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("RUL_ADMIN");
  const [filas, setFilas] = useState<ConjuntoReglas[]>([]);
  const [cat, setCat] = useState<ReglaCatalogos | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [ficha, setFicha] = useState<{ id: number | null; modo: Modo } | null>(null);
  const [versiones, setVersiones] = useState<ConjuntoReglas | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [c, k] = await Promise.all([api.get<ConjuntoReglas[]>("/api/reglas/conjuntos"), api.get<ReglaCatalogos>("/api/reglas/catalogos")]);
      setFilas(c.data); setCat(k.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los conjuntos.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<ConjuntoReglas>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Conjunto", render: (r) => (
        <button type="button" className="boton-enlace-id" title="Consultar el conjunto" style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}
          onClick={() => { setExito(null); setFicha({ id: r.idConjunto, modo: "ver" }); }}>{r.nombre}</button>
      ), textoBusqueda: (r) => r.nombre },
    { clave: "ctx", encabezado: "Contexto", render: (r) => r.contextoNombre, textoBusqueda: (r) => r.contextoNombre, filtroLista: true },
    { clave: "pol", encabezado: "Política", render: (r) => textoPolitica(r.politicaResolucion), textoBusqueda: (r) => r.politicaResolucion, filtroLista: true },
    { clave: "vig", encabezado: "Versión vigente", render: (r) => (r.versionVigente ? `v${r.versionVigente}` : "Sin publicar"), valorOrden: (r) => r.versionVigente ?? 0 },
    { clave: "prog", encabezado: "Programada", render: (r) => (r.versionProgramada ? `v${r.versionProgramada}` : "—") },
    { clave: "edi", encabezado: "En edición", render: (r) => (r.versionEnEdicion ? `v${r.versionEnEdicion}` : "—") },
    { clave: "ref", encabezado: "Usos", render: (r) => r.referencias, valorOrden: (r) => r.referencias },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const c = filas.find((x) => x.idConjunto === idSel) ?? null;
  const conjuntoFicha = ficha?.id != null ? filas.find((x) => x.idConjunto === ficha.id) ?? null : null;

  async function estado(e: "Activo" | "Inactivo") {
    if (!c) return;
    setExito(null); setError(null);
    try { await api.put(`/api/reglas/conjuntos/${c.idConjunto}/estado`, { estado: e }); setExito(e === "Activo" ? "Conjunto activado." : "Conjunto inactivado."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Conjuntos de reglas</h1>
      <p className="nota">Un conjunto agrupa las reglas que deciden algo (por ejemplo, la elegibilidad de un cliente para un producto). Sus reglas se definen por <strong>versión</strong>: se prepara un borrador, se prueba con el simulador y los casos de prueba, y se envía a aprobación. Pulse el nombre para consultarlo.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idConjunto} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay conjuntos de reglas." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !admin, onClick: () => { setExito(null); setFicha({ id: null, modo: "nuevo" }); } },
          { clave: "versiones", etiqueta: "Versiones", icono: "⎇", requiereSeleccion: "una", onClick: () => { setExito(null); setVersiones(c); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: c?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: c?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />

      {ficha && cat && (ficha.id == null || conjuntoFicha) && (
        <FichaConjunto key={ficha.id ?? "nuevo"} conjunto={conjuntoFicha} modoInicial={ficha.modo} cat={cat} admin={admin} onCerrar={() => setFicha(null)} onRefrescar={cargar}
          onVersiones={(x) => { setFicha(null); setVersiones(x); }}
          onCreado={async () => { setFicha(null); setExito("Conjunto creado. Selecciónelo y use «Versiones» para definir sus reglas."); await cargar(); }} />
      )}
      {versiones && cat && (
        <VersionesConjunto conjunto={versiones} cat={cat} puedeEditar={admin} puedePublicar={tienePermiso("RUL_PUBLICAR")} puedeEvaluar={tienePermiso("RUL_EVALUAR")}
          onCerrar={() => { setVersiones(null); void cargar(); }} />
      )}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Ficha
function FichaConjunto({ conjunto, modoInicial, cat, admin, onCerrar, onRefrescar, onCreado, onVersiones }: {
  conjunto: ConjuntoReglas | null; modoInicial: Modo; cat: ReglaCatalogos; admin: boolean; onCerrar: () => void; onRefrescar: () => Promise<void>; onCreado: () => Promise<void>; onVersiones: (c: ConjuntoReglas) => void;
}) {
  const api = useApiClient();
  const contextos = cat.contextos.filter((x) => x.estado === "Activo");
  const formDe = (x: ConjuntoReglas): Form => ({ idContexto: x.idContexto, codigo: x.codigo, nombre: x.nombre, descripcion: x.descripcion ?? "", politicaResolucion: x.politicaResolucion });
  const [modo, setModo] = useState<Modo>(modoInicial);
  const [form, setForm] = useState<Form>(() => (conjunto ? formDe(conjunto) : { idContexto: contextos[0]?.idContexto ?? 0, codigo: "", nombre: "", descripcion: "", politicaResolucion: "PrimeraCoincidencia" }));
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [refs, setRefs] = useState<ConjuntoReferencia[]>([]);

  useEffect(() => {
    if (!conjunto) return;
    api.get<ConjuntoReferencia[]>(`/api/reglas/conjuntos/${conjunto.idConjunto}/referencias`).then((r) => setRefs(r.data)).catch(() => undefined);
  }, [api, conjunto]);

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const cuerpo = { idContexto: form.idContexto, codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion.trim() || null, politicaResolucion: form.politicaResolucion };
    try {
      if (modo === "nuevo") { await api.post("/api/reglas/conjuntos", cuerpo); await onCreado(); return; }
      if (conjunto) await api.put(`/api/reglas/conjuntos/${conjunto.idConjunto}`, cuerpo);
      setAviso("Conjunto actualizado."); setModo("ver"); await onRefrescar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }
  const dato = (etiqueta: string, valor: ReactNode) => (<div className="info-fila"><span className="info-fila__etiqueta">{etiqueta}</span><span className="info-fila__valor">{valor}</span></div>);
  const tieneVersiones = !!conjunto && (conjunto.versionVigente || conjunto.versionEnEdicion || conjunto.versionProgramada);

  return (
    <VentanaModal titulo={conjunto ? `${conjunto.codigo} · ${conjunto.nombre}` : "Nuevo conjunto de reglas"} onCerrar={onCerrar} ancho={760}
      acciones={modo === "ver" && conjunto ? (
        <>
          {admin && <button type="button" onClick={() => { setForm(formDe(conjunto)); setErrorForm(null); setAviso(null); setModo("editar"); }}>✎ Editar</button>}
          <button type="button" className="boton-secundario" onClick={() => onVersiones(conjunto)}>⎇ Versiones</button>
        </>
      ) : undefined}>
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      {modo === "ver" && conjunto ? (
        <div>
          {dato("Código", conjunto.codigo)}
          {dato("Nombre", conjunto.nombre)}
          {dato("Descripción", conjunto.descripcion || "—")}
          {dato("Contexto", conjunto.contextoNombre)}
          {dato("Política", <>{textoPolitica(conjunto.politicaResolucion)} <small className="nota">— {POLITICAS.find((p) => p.valor === conjunto.politicaResolucion)?.ayuda}</small></>)}
          {dato("Versión vigente", conjunto.versionVigente ? `v${conjunto.versionVigente}` : "Sin publicar")}
          {dato("Programada / en edición", `${conjunto.versionProgramada ? `v${conjunto.versionProgramada}` : "—"} / ${conjunto.versionEnEdicion ? `v${conjunto.versionEnEdicion}` : "—"}`)}
          {dato("Estado", conjunto.estado)}
          <h4 style={{ margin: "0.9rem 0 0.3rem" }}>Dónde se usa</h4>
          {refs.length === 0 ? <p className="nota">Ningún producto u otro elemento usa este conjunto todavía. Mientras tenga usos no se puede inactivar.</p>
            : <ul style={{ margin: 0 }}>{refs.map((r, i) => (<li key={i}>{r.tipoConsumidor}: {r.descripcion}</li>))}</ul>}
        </div>
      ) : (
        <>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código <input type="text" maxLength={50} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label className="formulario__ancho">Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <label>Contexto
              <select value={form.idContexto} disabled={modo === "editar" && !!tieneVersiones} onChange={(e) => setForm({ ...form, idContexto: Number(e.target.value) })} required>
                {(modo === "editar" ? cat.contextos : contextos).map((x) => (<option key={x.idContexto} value={x.idContexto}>{x.nombre}</option>))}
              </select>
              {modo === "editar" && tieneVersiones && <small className="nota">No se cambia porque ya tiene versiones.</small>}
            </label>
            <label>Política de resolución
              <select value={form.politicaResolucion} onChange={(e) => setForm({ ...form, politicaResolucion: e.target.value })}>
                {POLITICAS.map((p) => (<option key={p.valor} value={p.valor}>{p.texto}</option>))}
              </select>
              <small className="nota">{POLITICAS.find((p) => p.valor === form.politicaResolucion)?.ayuda}</small>
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

// ------------------------------------------------------------------------------------------------ Versiones
function VersionesConjunto({ conjunto, cat, puedeEditar, puedePublicar, puedeEvaluar, onCerrar }: {
  conjunto: ConjuntoReglas; cat: ReglaCatalogos; puedeEditar: boolean; puedePublicar: boolean; puedeEvaluar: boolean; onCerrar: () => void;
}) {
  const api = useApiClient();
  const [filas, setFilas] = useState<VersionConjunto[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<{ texto: string; idGestion?: number; numero?: string } | null>(null);
  const [editor, setEditor] = useState<number | null>(null);
  const [publicar, setPublicar] = useState<VersionConjunto | null>(null);
  const [simulador, setSimulador] = useState(false);

  const cargar = useCallback(async () => {
    try { setFilas((await api.get<VersionConjunto[]>(`/api/reglas/conjuntos/${conjunto.idConjunto}/versiones`)).data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar las versiones.")); }
  }, [api, conjunto.idConjunto]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<VersionConjunto>[] = useMemo(() => [
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
    <VentanaModal titulo={`Versiones · ${conjunto.codigo} ${conjunto.nombre}`} onCerrar={onCerrar} ancho={1000}>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito.texto} {exito.idGestion && <Link to="/gestiones/consulta">Ver gestión {exito.numero ?? exito.idGestion}</Link>}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idVersion} seleccion={sel} onCambiarSeleccion={setSel}
        mensajeVacio="Aún no hay versiones: cree la primera."
        comandos={[
          { clave: "nueva", etiqueta: "Nueva versión", icono: "+", oculto: !puedeEditar || conjunto.estado !== "Activo", onClick: () => void accion(async () => {
              const r = await api.post<{ idVersion: number }>(`/api/reglas/conjuntos/${conjunto.idConjunto}/versiones`); setEditor(r.data.idVersion); }, "Borrador creado (copia de la última versión).") },
          { clave: "ver", etiqueta: v?.estado === "Borrador" && puedeEditar ? "Editar reglas" : "Ver reglas", icono: "✎", requiereSeleccion: "una", onClick: () => { if (v) setEditor(v.idVersion); } },
          { clave: "probar", etiqueta: "Probar", icono: "▶", oculto: !puedeEvaluar, onClick: () => setSimulador(true) },
          { clave: "publicar", etiqueta: "Publicar", icono: "➤", requiereSeleccion: "una", oculto: !puedePublicar, deshabilitado: v?.estado !== "Borrador", onClick: () => { if (v) setPublicar(v); } },
          { clave: "reabrir", etiqueta: "Volver a borrador", icono: "↩", requiereSeleccion: "una", oculto: !puedeEditar, deshabilitado: v?.estado !== "Revision" || v?.estadoGestion === "Pendiente de Aprobacion", onClick: () => { if (v) void accion(() => api.post(`/api/reglas/versiones/${v.idVersion}/reabrir`), "Versión devuelta a borrador."); } },
          { clave: "descartar", etiqueta: "Descartar borrador", icono: "🗑", requiereSeleccion: "una", oculto: !puedeEditar, deshabilitado: v?.estado !== "Borrador", onClick: () => { if (v && window.confirm(`¿Descartar el borrador v${v.numero}?`)) void accion(() => api.delete(`/api/reglas/versiones/${v.idVersion}`), "Borrador descartado."); } },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />
      {editor != null && <EditorReglasVersion idVersion={editor} conjunto={conjunto} puedeEditar={puedeEditar} onCerrar={() => { setEditor(null); void cargar(); }} />}
      {publicar && (
        <PublicarVersion conjunto={conjunto} version={publicar} onCerrar={() => setPublicar(null)}
          onEnviada={(texto, idGestion, numero) => { setPublicar(null); setExito({ texto, idGestion, numero }); void cargar(); }} />
      )}
      {simulador && <Simulador conjunto={conjunto} cat={cat} versiones={filas} puedeAdmin={puedeEditar} onCerrar={() => setSimulador(false)} />}
    </VentanaModal>
  );
}

function PublicarVersion({ conjunto, version, onCerrar, onEnviada }: { conjunto: ConjuntoReglas; version: VersionConjunto; onCerrar: () => void; onEnviada: (texto: string, idGestion: number, numero?: string) => void }) {
  const api = useApiClient();
  const [fecha, setFecha] = useState(hoyIso());
  const [comentario, setComentario] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [casos, setCasos] = useState<CasosEjecutar | null>(null);

  useEffect(() => {
    api.post<CasosEjecutar>(`/api/reglas/conjuntos/${conjunto.idConjunto}/casos/ejecutar?idVersion=${version.idVersion}`).then((r) => setCasos(r.data)).catch(() => undefined);
  }, [api, conjunto.idConjunto, version.idVersion]);

  async function enviar(e: FormEvent) {
    e.preventDefault(); setEnviando(true); setError(null);
    try {
      const r = await api.post<{ idGestion: number; numero?: string }>(`/api/reglas/versiones/${version.idVersion}/publicar`, { fechaVigenciaDesde: fecha, comentario: comentario.trim() || null });
      onEnviada(`Versión v${version.numero} enviada a aprobación: rige desde ${fmtFecha(fecha)} (o desde la aprobación, si es posterior).`, r.data.idGestion, r.data.numero);
    } catch (err) { setError(mensajeError(err, "No se pudo publicar.")); }
    finally { setEnviando(false); }
  }
  return (
    <PanelModal onCerrar={onCerrar}>
      <h3>Publicar versión v{version.numero}</h3>
      <p className="nota">Se enviará a aprobación. Al aprobarse queda programada (si la fecha es futura) o vigente, y la versión anterior expira un día antes.</p>
      {casos && casos.total > 0 && (
        <p className={casos.fallidos > 0 ? "mensaje-error" : "mensaje-exito"}>
          Casos de prueba: {casos.total - casos.fallidos} de {casos.total} pasan{casos.fallidos > 0 ? `; fallan: ${casos.casos.filter((x) => !x.paso).map((x) => x.nombre).join(", ")}. Puede publicar de todos modos.` : "."}
        </p>
      )}
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

// ------------------------------------------------------------------------------------------------ Simulador y casos
interface FilaDato { clave: number; codigo: string; texto: string }
let contadorFila = 0;

function valorDeFila(a: ReglaAtributo | undefined, texto: string): unknown {
  if (!a) return texto;
  if (a.esMultivalor) return texto.split(",").map((x) => x.trim()).filter(Boolean);
  if (a.tipoDato === "Numero") return texto.trim() === "" ? null : Number(texto);
  if (a.tipoDato === "Booleano") return texto === "true";
  return texto;
}
function filaDeValor(v: unknown): string { return Array.isArray(v) ? v.join(", ") : v == null ? "" : String(v); }

function Simulador({ conjunto, cat, versiones, puedeAdmin, onCerrar }: { conjunto: ConjuntoReglas; cat: ReglaCatalogos; versiones: VersionConjunto[]; puedeAdmin: boolean; onCerrar: () => void }) {
  const api = useApiClient();
  const atributos = useMemo(() => cat.atributos, [cat]);
  const [filas, setFilas] = useState<FilaDato[]>([]);
  const [idVersion, setIdVersion] = useState<number | "">(() => versiones.find((x) => x.estado === "Vigente")?.idVersion ?? versiones[0]?.idVersion ?? "");
  const [fecha, setFecha] = useState("");
  const [res, setRes] = useState<ReglaEvaluacion | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [probando, setProbando] = useState(false);
  const [casos, setCasos] = useState<CasoPrueba[]>([]);
  const [ejec, setEjec] = useState<CasosEjecutar | null>(null);
  const [nuevoCaso, setNuevoCaso] = useState<{ nombre: string; decision: string; regla: string } | null>(null);
  const [listas, setListas] = useState<Record<string, CatalogoItemResponse[]>>({});

  const cargarCasos = useCallback(async () => {
    try { setCasos((await api.get<CasoPrueba[]>(`/api/reglas/conjuntos/${conjunto.idConjunto}/casos`)).data); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar los casos.")); }
  }, [api, conjunto.idConjunto]);
  useEffect(() => { void cargarCasos(); }, [cargarCasos]);

  // Combos de las listas de valores de los atributos agregados.
  useEffect(() => {
    const codigos = Array.from(new Set(filas.map((f) => atributos.find((a) => a.codigo === f.codigo)?.listaCodigo).filter((c): c is string => !!c && !listas[c])));
    codigos.forEach((c) => {
      api.get<CatalogoItemResponse[]>(`/api/catalogos/listas/${c}`).then((r) => setListas((s) => ({ ...s, [c]: r.data }))).catch(() => setListas((s) => ({ ...s, [c]: [] })));
    });
  }, [api, atributos, filas, listas]);

  const libres = atributos.filter((a) => !filas.some((f) => f.codigo === a.codigo));
  const datos = () => Object.fromEntries(filas.map((f) => [f.codigo, valorDeFila(atributos.find((a) => a.codigo === f.codigo), f.texto)]));

  function agregar() { const a = libres[0]; if (a) setFilas((s) => [...s, { clave: ++contadorFila, codigo: a.codigo, texto: a.tipoDato === "Booleano" ? "false" : "" }]); }
  function cargarEntrada(entrada: string) {
    try {
      const o = JSON.parse(entrada) as Record<string, unknown>;
      setFilas(Object.entries(o).map(([codigo, v]) => ({ clave: ++contadorFila, codigo, texto: filaDeValor(v) })));
      setRes(null); setExito("Datos cargados en el simulador.");
    } catch { setError("El caso tiene un formato de datos inválido."); }
  }

  async function probar() {
    setProbando(true); setError(null); setExito(null);
    try { setRes((await api.post<ReglaEvaluacion>(`/api/reglas/conjuntos/${conjunto.idConjunto}/probar`, { datos: datos(), idVersion: idVersion === "" ? null : idVersion, fechaProceso: fecha || null })).data); }
    catch (e) { setError(mensajeError(e, "No se pudo probar.")); setRes(null); }
    finally { setProbando(false); }
  }
  async function guardarCaso(e: FormEvent) {
    e.preventDefault(); if (!nuevoCaso) return; setError(null);
    try {
      await api.post(`/api/reglas/conjuntos/${conjunto.idConjunto}/casos`, { nombre: nuevoCaso.nombre.trim(), entrada: datos(), decisionEsperada: nuevoCaso.decision || null, reglaEsperada: nuevoCaso.regla.trim() || null });
      setNuevoCaso(null); setExito("Caso de prueba guardado."); await cargarCasos();
    } catch (err) { setError(mensajeError(err, "No se pudo guardar el caso.")); }
  }
  async function ejecutar() {
    setError(null); setExito(null);
    try { setEjec((await api.post<CasosEjecutar>(`/api/reglas/conjuntos/${conjunto.idConjunto}/casos/ejecutar${idVersion === "" ? "" : `?idVersion=${idVersion}`}`)).data); }
    catch (e) { setError(mensajeError(e, "No se pudieron ejecutar los casos.")); }
  }
  async function estadoCaso(c: CasoPrueba, estado: "Activo" | "Inactivo") {
    setError(null);
    try { await api.put(`/api/reglas/conjuntos/${conjunto.idConjunto}/casos/${c.idCaso}/estado`, { estado }); await cargarCasos(); }
    catch (e) { setError(mensajeError(e, "No se pudo cambiar el estado del caso.")); }
  }

  const campoValor = (f: FilaDato) => {
    const a = atributos.find((x) => x.codigo === f.codigo);
    const set = (texto: string) => setFilas((s) => s.map((x) => (x.clave === f.clave ? { ...x, texto } : x)));
    if (a?.tipoDato === "Booleano") return <select value={f.texto} onChange={(e) => set(e.target.value)}><option value="true">Sí</option><option value="false">No</option></select>;
    if (a?.tipoDato === "Fecha") return <input type="date" value={f.texto} onChange={(e) => set(e.target.value)} />;
    if (a?.tipoDato === "Numero") return <input type="number" step="any" value={f.texto} onChange={(e) => set(e.target.value)} />;
    const lista = a?.listaCodigo ? listas[a.listaCodigo] : undefined;
    if (lista && lista.length > 0 && !a?.esMultivalor) return <select value={f.texto} onChange={(e) => set(e.target.value)}><option value="">(sin dato)</option>{lista.map((i) => (<option key={i.id} value={i.codigo}>{i.nombre}</option>))}</select>;
    return <input type="text" value={f.texto} placeholder={a?.esMultivalor ? "valores separados por coma" : ""} onChange={(e) => set(e.target.value)} />;
  };

  return (
    <VentanaModal titulo={`Simulador · ${conjunto.codigo}`} onCerrar={onCerrar} ancho={1000}
      acciones={<button type="button" onClick={() => void probar()} disabled={probando}>{probando ? "Probando..." : "▶ Probar"}</button>}>
      <p className="nota">Escriba los datos de un caso hipotético y vea qué decidiría el conjunto y por qué. La prueba no se registra ni afecta a nadie. Un dato que no se indique se considera «sin dato».</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap", marginBottom: "0.6rem" }}>
        <label>Versión
          <select value={idVersion} onChange={(e) => setIdVersion(e.target.value === "" ? "" : Number(e.target.value))}>
            <option value="">La vigente</option>
            {versiones.map((x) => (<option key={x.idVersion} value={x.idVersion}>v{x.numero} · {x.estado === "Revision" ? "En revisión" : x.estado}</option>))}
          </select>
        </label>
        <label>Fecha del proceso (opcional) <input type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} /></label>
      </div>
      <h4 style={{ margin: "0.4rem 0" }}>Datos del caso</h4>
      {filas.length === 0 && <p className="nota">Agregue los datos que necesite.</p>}
      {filas.map((f) => (
        <div key={f.clave} style={{ display: "flex", gap: "0.5rem", alignItems: "center", marginBottom: "0.3rem" }}>
          <select value={f.codigo} style={{ minWidth: "16rem" }} onChange={(e) => setFilas((s) => s.map((x) => (x.clave === f.clave ? { ...x, codigo: e.target.value, texto: "" } : x)))}>
            {atributos.filter((a) => a.codigo === f.codigo || !filas.some((x) => x.codigo === a.codigo)).map((a) => (<option key={a.codigo} value={a.codigo}>{a.nombre}</option>))}
          </select>
          {campoValor(f)}
          <button type="button" className="boton-secundario" aria-label="Quitar dato" onClick={() => setFilas((s) => s.filter((x) => x.clave !== f.clave))}>✕</button>
        </div>
      ))}
      <button type="button" className="boton-secundario" onClick={agregar} disabled={libres.length === 0}>+ Dato</button>

      {res && (
        <div style={{ marginTop: "1rem", borderTop: "1px solid var(--color-borde)", paddingTop: "0.6rem" }}>
          <h4 style={{ margin: "0 0 0.3rem" }}>Resultado con la versión v{res.numeroVersion}</h4>
          <ResultadoReglas r={res.resultado} />
          {puedeAdmin && !nuevoCaso && <button type="button" className="boton-secundario" onClick={() => setNuevoCaso({ nombre: "", decision: res.resultado.decision ?? "", regla: res.resultado.reglasAplicadas[0] ?? "" })}>Guardar como caso de prueba</button>}
          {nuevoCaso && (
            <form className="formulario" onSubmit={guardarCaso} style={{ marginTop: "0.5rem" }}>
              <label>Nombre del caso <input type="text" maxLength={150} required value={nuevoCaso.nombre} onChange={(e) => setNuevoCaso({ ...nuevoCaso, nombre: e.target.value })} /></label>
              <label>Decisión esperada
                <select value={nuevoCaso.decision} onChange={(e) => setNuevoCaso({ ...nuevoCaso, decision: e.target.value })}>
                  <option value="">(no verificar)</option><option value="Aprobar">Aprobar</option><option value="Referir">Referir</option><option value="Rechazar">Rechazar</option>
                </select>
              </label>
              <label>Regla esperada (opcional) <input type="text" maxLength={50} value={nuevoCaso.regla} onChange={(e) => setNuevoCaso({ ...nuevoCaso, regla: e.target.value })} /></label>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <button type="submit">Guardar caso</button>
                <button type="button" className="boton-secundario" onClick={() => setNuevoCaso(null)}>Cancelar</button>
              </div>
            </form>
          )}
        </div>
      )}

      <div style={{ marginTop: "1.2rem", borderTop: "1px solid var(--color-borde)", paddingTop: "0.6rem" }}>
        <div style={{ display: "flex", gap: "0.6rem", alignItems: "center" }}>
          <h4 style={{ margin: 0 }}>Casos de prueba ({casos.length})</h4>
          <button type="button" className="boton-secundario" onClick={() => void ejecutar()} disabled={casos.filter((c) => c.estado === "Activo").length === 0}>Ejecutar casos</button>
        </div>
        {ejec && <p className={ejec.fallidos > 0 ? "mensaje-error" : "mensaje-exito"}>v{ejec.numeroVersion}: {ejec.total - ejec.fallidos} de {ejec.total} casos pasan.</p>}
        {casos.length === 0 ? <p className="nota">Aún no hay casos. Pruebe un escenario y guárdelo para repetirlo cada vez que cambie las reglas.</p> : (
          <table className="tabla-datos" style={{ width: "100%" }}>
            <thead><tr><th>Caso</th><th>Espera</th><th>Resultado</th><th /></tr></thead>
            <tbody>
              {casos.map((c) => {
                const r = ejec?.casos.find((x) => x.idCaso === c.idCaso);
                return (
                  <tr key={c.idCaso} style={{ opacity: c.estado === "Activo" ? 1 : 0.55 }}>
                    <td>{c.nombre}</td>
                    <td>{[c.decisionEsperada, c.reglaEsperada].filter(Boolean).join(" · ") || "—"}</td>
                    <td>{r ? (r.paso ? <span style={{ color: "#15803d" }}>✔ Pasa</span> : <span style={{ color: "#b91c1c" }}>✘ {r.detalle ?? `Obtuvo ${r.decisionObtenida ?? "nada"}`}</span>) : "—"}</td>
                    <td style={{ whiteSpace: "nowrap" }}>
                      <button type="button" className="boton-secundario" onClick={() => cargarEntrada(c.entrada)}>Cargar</button>{" "}
                      {puedeAdmin && <button type="button" className="boton-secundario" onClick={() => void estadoCaso(c, c.estado === "Activo" ? "Inactivo" : "Activo")}>{c.estado === "Activo" ? "Inactivar" : "Activar"}</button>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </VentanaModal>
  );
}

export type { ReglasVersionDetalle };
