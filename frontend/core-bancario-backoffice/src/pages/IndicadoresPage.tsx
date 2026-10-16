import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { VentanaModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { fmtFecha, hoyIso, mensajeError, parseDecimal } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import { leerTasasExcel, type FilaTasaExcel } from "../utils/leerTasasExcel";
import type { CatalogoItemResponse } from "../api/tipos";
import type { Indicador, IndicadorLoteResultado, IndicadorValor } from "../api/tiposCreditos";

interface Form { codigo: string; nombre: string; descripcion: string; idTipo: number; monedaCodigo: string; frecuencia: string }
type Modo = "ver" | "editar" | "nuevo";
const FRECUENCIAS = ["Diaria", "Semanal", "Mensual", "Trimestral"];

/** Pricing Studio › Tasas de referencia (consulta PRD_CONSULTA; mantenimiento IND_ADMIN; carga de valores IND_CARGA, por gestión de aprobación). */
export function IndicadoresPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("IND_ADMIN");
  const puedeCargar = tienePermiso("IND_CARGA");
  const [filas, setFilas] = useState<Indicador[]>([]);
  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [ficha, setFicha] = useState<{ id: number | null; modo: Modo } | null>(null);
  const [valores, setValores] = useState<Indicador | null>(null);
  const [excel, setExcel] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [i, t] = await Promise.all([api.get<Indicador[]>("/api/indicadores"), api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_INDICADOR_FINANCIERO")]);
      setFilas(i.data); setTipos(t.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los indicadores.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<Indicador>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Indicador", render: (r) => (
        <button type="button" className="boton-enlace-id" title="Consultar el indicador" style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}
          onClick={() => { setExito(null); setFicha({ id: r.idIndicador, modo: "ver" }); }}>{r.nombre}</button>
      ), textoBusqueda: (r) => r.nombre },
    { clave: "tipo", encabezado: "Tipo", render: (r) => r.tipoNombre, textoBusqueda: (r) => r.tipoNombre, filtroLista: true },
    { clave: "moneda", encabezado: "Moneda", render: (r) => r.monedaCodigo ?? "—", textoBusqueda: (r) => r.monedaCodigo ?? "" },
    { clave: "frec", encabezado: "Frecuencia", render: (r) => r.frecuencia, textoBusqueda: (r) => r.frecuencia, filtroLista: true },
    { clave: "valor", encabezado: "Valor vigente", render: (r) => (r.valorVigente == null ? "Sin valor" : `${r.valorVigente.toLocaleString("es-CR", { maximumFractionDigits: 8 })} % (desde ${fmtFecha(r.fechaVigente)})`), valorOrden: (r) => r.valorVigente ?? -1 },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const ind = filas.find((x) => x.idIndicador === idSel) ?? null;
  const indFicha = ficha?.id != null ? filas.find((x) => x.idIndicador === ficha.id) ?? null : null;

  async function estado(e: "Activo" | "Inactivo") {
    if (!ind) return;
    setExito(null); setError(null);
    try { await api.put(`/api/indicadores/${ind.idIndicador}/estado`, { estado: e }); setExito(e === "Activo" ? "Indicador activado." : "Indicador inactivado."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Tasas de referencia</h1>
      <p className="nota">Indicadores publicados por una fuente externa (TBP, Prime, SOFR...), base de las tasas variables. Cada valor rige desde su fecha hasta el siguiente cargado y se carga con aprobación. Pulse el nombre de un indicador para consultarlo y editarlo; use «Cargar desde Excel» para enviar varios valores a la vez.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idIndicador} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay indicadores." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !admin, onClick: () => { setExito(null); setFicha({ id: null, modo: "nuevo" }); } },
          { clave: "valores", etiqueta: "Valores", icono: "％", requiereSeleccion: "una", onClick: () => { setExito(null); setValores(ind); } },
          { clave: "excel", etiqueta: "Cargar desde Excel", icono: "⇪", oculto: !puedeCargar, onClick: () => { setExito(null); setExcel(true); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: ind?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: ind?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />

      {ficha && (ficha.id == null || indFicha) && (
        <FichaIndicador key={ficha.id ?? "nuevo"} indicador={indFicha} modoInicial={ficha.modo} tipos={tipos} admin={admin} puedeCargar={puedeCargar}
          onCerrar={() => setFicha(null)} onRefrescar={cargar} onValores={(x) => { setFicha(null); setValores(x); }}
          onCreado={async () => { setFicha(null); setExito("Indicador creado."); await cargar(); }} />
      )}
      {valores && <ValoresIndicador indicador={valores} puedeCargar={puedeCargar} onCerrar={() => { setValores(null); void cargar(); }} />}
      {excel && <CargaExcelTasas indicadores={filas} onCerrar={() => { setExcel(false); void cargar(); }} />}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Ficha
function FichaIndicador({ indicador, modoInicial, tipos, admin, puedeCargar, onCerrar, onRefrescar, onCreado, onValores }: {
  indicador: Indicador | null; modoInicial: Modo; tipos: CatalogoItemResponse[]; admin: boolean; puedeCargar: boolean;
  onCerrar: () => void; onRefrescar: () => Promise<void>; onCreado: () => Promise<void>; onValores: (i: Indicador) => void;
}) {
  const api = useApiClient();
  const formDe = (i: Indicador): Form => ({ codigo: i.codigo, nombre: i.nombre, descripcion: i.descripcion ?? "", idTipo: i.idTipo, monedaCodigo: i.monedaCodigo ?? "", frecuencia: i.frecuencia });
  const [modo, setModo] = useState<Modo>(modoInicial);
  const [form, setForm] = useState<Form>(() => (indicador ? formDe(indicador) : { codigo: "", nombre: "", descripcion: "", idTipo: tipos.find((t) => t.codigo === "TASA_REFERENCIA")?.id ?? tipos[0]?.id ?? 0, monedaCodigo: "", frecuencia: "Mensual" }));
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const cuerpo = { ...form, monedaCodigo: form.monedaCodigo.trim() || null, descripcion: form.descripcion.trim() || null };
    try {
      if (modo === "nuevo") { await api.post("/api/indicadores", cuerpo); await onCreado(); return; }
      if (indicador) await api.put(`/api/indicadores/${indicador.idIndicador}`, cuerpo);
      setAviso("Indicador actualizado."); setModo("ver"); await onRefrescar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }
  const dato = (etiqueta: string, valor: ReactNode) => (<div className="info-fila"><span className="info-fila__etiqueta">{etiqueta}</span><span className="info-fila__valor">{valor}</span></div>);

  return (
    <VentanaModal titulo={indicador ? `${indicador.codigo} · ${indicador.nombre}` : "Nuevo indicador"} onCerrar={onCerrar} ancho={760}
      acciones={modo === "ver" && indicador ? (
        <>
          {admin && <button type="button" onClick={() => { setForm(formDe(indicador)); setErrorForm(null); setAviso(null); setModo("editar"); }}>✎ Editar</button>}
          <button type="button" className="boton-secundario" onClick={() => onValores(indicador)}>％ {puedeCargar ? "Valores y carga" : "Valores"}</button>
        </>
      ) : undefined}>
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      {modo === "ver" && indicador ? (
        <div>
          {dato("Código", indicador.codigo)}
          {dato("Nombre", indicador.nombre)}
          {dato("Descripción", indicador.descripcion || "—")}
          {dato("Tipo", indicador.tipoNombre)}
          {dato("Moneda", indicador.monedaCodigo || "—")}
          {dato("Frecuencia de publicación", indicador.frecuencia)}
          {dato("Valor vigente", indicador.valorVigente == null ? "Sin valor" : `${indicador.valorVigente.toLocaleString("es-CR", { maximumFractionDigits: 8 })} % (desde ${fmtFecha(indicador.fechaVigente)})`)}
          {dato("Estado", indicador.estado)}
        </div>
      ) : (
        <>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código <input type="text" maxLength={30} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label>Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <label>Tipo
              <select value={form.idTipo} onChange={(e) => setForm({ ...form, idTipo: Number(e.target.value) })} required>
                {tipos.map((t) => (<option key={t.id} value={t.id}>{t.nombre}</option>))}
              </select>
            </label>
            <label>Moneda (ISO, opcional) <input type="text" maxLength={3} value={form.monedaCodigo} onChange={(e) => setForm({ ...form, monedaCodigo: e.target.value.toUpperCase() })} style={{ width: "6rem" }} /></label>
            <label>Frecuencia de publicación
              <select value={form.frecuencia} onChange={(e) => setForm({ ...form, frecuencia: e.target.value })}>
                {FRECUENCIAS.map((f) => (<option key={f}>{f}</option>))}
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

// ------------------------------------------------------------------------------------------------ Carga desde Excel
function CargaExcelTasas({ indicadores, onCerrar }: { indicadores: Indicador[]; onCerrar: () => void }) {
  const api = useApiClient();
  const [archivo, setArchivo] = useState<string | null>(null);
  const [filas, setFilas] = useState<FilaTasaExcel[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [leyendo, setLeyendo] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [resultados, setResultados] = useState<IndicadorLoteResultado[] | null>(null);
  const activos = useMemo(() => new Set(indicadores.filter((i) => i.estado === "Activo").map((i) => i.codigo.toUpperCase())), [indicadores]);
  const validas = (filas ?? []).filter((f) => f.errores.length === 0);

  async function elegir(f: File | undefined) {
    if (!f) return;
    setError(null); setFilas(null); setResultados(null); setArchivo(f.name); setLeyendo(true);
    const r = await leerTasasExcel(f, activos);
    setLeyendo(false);
    if (r.errorGeneral) setError(r.errorGeneral); else setFilas(r.filas);
  }
  async function enviar() {
    setEnviando(true); setError(null);
    try {
      const r = await api.post<IndicadorLoteResultado[]>("/api/indicadores/valores/lote", {
        filas: validas.map((f) => ({ fila: f.fila, codigo: f.codigo, fechaVigencia: f.fecha, valor: f.valor, comentario: f.comentario || null })),
      });
      setResultados(r.data);
    } catch (e) { setError(mensajeError(e, "No se pudo enviar la carga.")); }
    finally { setEnviando(false); }
  }
  function reiniciar() { setArchivo(null); setFilas(null); setResultados(null); setError(null); }

  const ok = resultados?.filter((r) => r.exito).length ?? 0;
  return (
    <VentanaModal titulo="Cargar tasas de referencia desde Excel" onCerrar={onCerrar} ancho={1000}
      acciones={filas && !resultados ? (<button type="button" disabled={enviando || validas.length === 0} onClick={() => void enviar()}>{enviando ? "Enviando..." : `Enviar ${validas.length} solicitud${validas.length === 1 ? "" : "es"} a aprobación`}</button>)
        : resultados ? (<button type="button" onClick={reiniciar}>Cargar otro archivo</button>) : undefined}>
      {error && <p className="mensaje-error">{error}</p>}

      {!filas && !resultados && (
        <>
          <p>Suba un archivo <strong>.xlsx</strong> con una fila por indicador y fecha. La primera hoja debe tener estos encabezados, en la fila 1:</p>
          <table className="tabla tabla--compacta">
            <thead><tr><th>Codigo_indicador</th><th>Fecha_vigencia</th><th>Valor_porcentaje</th><th>Comentario (opcional)</th></tr></thead>
            <tbody>
              <tr><td>TBP</td><td>2026-10-16</td><td>4.25</td><td>Publicación BCCR del 16/10</td></tr>
              <tr><td>PRIME</td><td>16/10/2026</td><td>7,5</td><td></td></tr>
            </tbody>
          </table>
          <p className="nota">El código debe existir y estar activo. La fecha admite <code>aaaa-mm-dd</code>, <code>dd/mm/aaaa</code> o celda de fecha de Excel. El valor es el porcentaje anual (4.25 = 4,25 %), con punto o coma decimal. Máximo 500 filas. Cada fila válida genera una solicitud que se aprueba por separado.</p>
          <p>
            <a className="boton-secundario boton-enlace" href={`${import.meta.env.BASE_URL}plantilla_tasas_referencia.xlsx`} download>⇩ Descargar plantilla</a>
          </p>
          <label style={{ display: "flex", flexDirection: "column", gap: "0.3rem", maxWidth: "28rem" }}>Archivo Excel
            <input type="file" accept=".xlsx" onChange={(e) => { void elegir(e.target.files?.[0]); e.target.value = ""; }} disabled={leyendo} />
          </label>
          {leyendo && <p className="nota">Leyendo {archivo}…</p>}
        </>
      )}

      {filas && !resultados && (
        <>
          <p><strong>{archivo}</strong>: {filas.length} fila{filas.length === 1 ? "" : "s"} · <span style={{ color: "#1a7f37" }}>{validas.length} correcta{validas.length === 1 ? "" : "s"}</span> · <span style={{ color: filas.length - validas.length ? "#b3261e" : undefined }}>{filas.length - validas.length} con error</span>.
            {filas.length - validas.length > 0 && " Solo se enviarán las correctas; corrija el archivo y vuelva a cargarlo para el resto."}</p>
          <div className="tabla-scroll" style={{ maxHeight: "50vh", overflow: "auto" }}>
            <table className="tabla tabla--compacta">
              <thead><tr><th>Fila</th><th>Indicador</th><th>Vigente desde</th><th>Valor (%)</th><th>Comentario</th><th>Validación</th></tr></thead>
              <tbody>
                {filas.map((f) => (
                  <tr key={f.fila} style={f.errores.length ? { background: "#fdecea" } : undefined}>
                    <td>{f.fila}</td><td>{f.codigo || "—"}</td><td>{f.fecha ? fmtFecha(f.fecha) : "—"}</td>
                    <td>{f.valor == null ? "—" : f.valor.toLocaleString("es-CR", { maximumFractionDigits: 8 })}</td><td>{f.comentario || "—"}</td>
                    <td>{f.errores.length ? f.errores.join(" ") : "✓ Correcta"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {resultados && (
        <>
          <p className="mensaje-exito" style={{ background: ok ? undefined : "#fdecea" }}>
            {ok} solicitud{ok === 1 ? "" : "es"} enviada{ok === 1 ? "" : "s"} a aprobación{resultados.length - ok > 0 ? ` · ${resultados.length - ok} no se pudieron crear` : ""}. Siga su estado en <Link to="/gestiones/consulta">Consulta de gestiones</Link>.
          </p>
          <div className="tabla-scroll" style={{ maxHeight: "50vh", overflow: "auto" }}>
            <table className="tabla tabla--compacta">
              <thead><tr><th>Fila</th><th>Indicador</th><th>Vigente desde</th><th>Resultado</th><th>Gestión</th></tr></thead>
              <tbody>
                {resultados.map((r) => (
                  <tr key={r.fila} style={r.exito ? undefined : { background: "#fdecea" }}>
                    <td>{r.fila}</td><td>{r.codigo}</td><td>{fmtFecha(r.fechaVigencia)}</td><td>{r.exito ? "✓ " : "✗ "}{r.mensaje}</td><td>{r.numeroGestion ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </VentanaModal>
  );
}

function ValoresIndicador({ indicador, puedeCargar, onCerrar }: { indicador: Indicador; puedeCargar: boolean; onCerrar: () => void }) {
  const api = useApiClient();
  const [filas, setFilas] = useState<IndicadorValor[]>([]);
  const [fecha, setFecha] = useState(hoyIso());
  const [valor, setValor] = useState("");
  const [comentario, setComentario] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<{ numero?: string; id: number } | null>(null);
  const [enviando, setEnviando] = useState(false);

  const cargar = useCallback(async () => {
    try { setFilas((await api.get<IndicadorValor[]>(`/api/indicadores/${indicador.idIndicador}/valores`)).data); }
    catch (e) { setError(mensajeError(e, "No se pudo cargar el historial.")); }
  }, [api, indicador.idIndicador]);
  useEffect(() => { void cargar(); }, [cargar]);

  async function enviar(e: FormEvent) {
    e.preventDefault(); setError(null); setExito(null);
    const v = parseDecimal(valor);
    if (v == null) { setError("Escriba un valor numérico."); return; }
    setEnviando(true);
    try {
      const r = await api.post<{ idGestion: number; numero?: string }>(`/api/indicadores/${indicador.idIndicador}/valores`, { fechaVigencia: fecha, valor: v, comentario: comentario.trim() || null });
      setExito({ numero: r.data.numero, id: r.data.idGestion }); setValor(""); setComentario(""); await cargar();
    } catch (err) { setError(mensajeError(err, "No se pudo enviar la solicitud.")); }
    finally { setEnviando(false); }
  }

  return (
    <VentanaModal titulo={`${indicador.codigo} · ${indicador.nombre}`} onCerrar={onCerrar} ancho={720}>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">Valor enviado a aprobación. <Link to="/gestiones/consulta">Ver gestión {exito.numero ?? exito.id}</Link></p>}
      {puedeCargar && indicador.estado === "Activo" && (
        <form className="formulario-en-linea" onSubmit={enviar} style={{ flexWrap: "wrap", alignItems: "flex-end", marginBottom: "1rem" }}>
          <label>Vigente desde <input type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} required /></label>
          <label>Valor (% anual) <input type="text" inputMode="decimal" value={valor} onChange={(e) => setValor(e.target.value)} style={{ width: "8rem" }} required /></label>
          <label>Comentario <input type="text" maxLength={500} value={comentario} onChange={(e) => setComentario(e.target.value)} /></label>
          <button type="submit" disabled={enviando}>{enviando ? "Enviando..." : "Solicitar carga"}</button>
        </form>
      )}
      <table className="tabla">
        <thead><tr><th>Vigente desde</th><th>Valor (%)</th><th>Origen</th><th>Gestión</th><th>Cargó</th></tr></thead>
        <tbody>
          {filas.length === 0 && <tr><td colSpan={5}>Aún no hay valores cargados.</td></tr>}
          {filas.map((f) => (
            <tr key={f.idValor}><td>{fmtFecha(f.fechaVigencia)}</td><td>{f.valor.toLocaleString("es-CR", { maximumFractionDigits: 8 })}</td><td>{f.origen}</td><td>{f.numeroGestion ?? "—"}</td><td>{f.creadoPor}</td></tr>
          ))}
        </tbody>
      </table>
    </VentanaModal>
  );
}
