import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { fmtFecha, fmtFechaHora, hoyIso, mensajeError, parseDecimal } from "../api/mensajes";
import type { GestionDetalleResponse, MonedaAdmin, TipoCambioFila, TipoCambioVigente } from "../api/tipos";

const num = (n?: number | null) => (n == null ? "—" : n.toLocaleString("es-CR", { minimumFractionDigits: 2, maximumFractionDigits: 6 }));

interface FormTc { idMoneda: number; fechaVigencia: string; compra: string; venta: string; contable: string; comentario: string }

/**
 * Administración › Tipo de cambio (TC_CONSULTA para ver, TC_CARGA para ingresar). Compra, venta y valor contable por moneda y fecha
 * contra la moneda base (el valor contable es el que usan los cálculos contables). La carga NO es directa: se solicita como gestión y rige al aprobarse, desde su fecha de vigencia.
 */
export function TipoCambioPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const puedeCargar = tienePermiso("TC_CARGA");

  const [monedas, setMonedas] = useState<MonedaAdmin[]>([]);
  const [vigentes, setVigentes] = useState<TipoCambioVigente[]>([]);
  const [filas, setFilas] = useState<TipoCambioFila[]>([]);
  const [fMoneda, setFMoneda] = useState(0);
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<{ texto: string; idGestion?: number; numero?: string } | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState(false);
  const [form, setForm] = useState<FormTc>({ idMoneda: 0, fechaVigencia: hoyIso(), compra: "", venta: "", contable: "", comentario: "" });
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [params, setParams] = useSearchParams();
  const [devuelta, setDevuelta] = useState<GestionDetalleResponse | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [m, v, h] = await Promise.all([
        api.get<MonedaAdmin[]>("/api/catalogos/monedas"),
        api.get<TipoCambioVigente[]>("/api/tipos-cambio/vigentes"),
        api.get<TipoCambioFila[]>("/api/tipos-cambio", { params: { idMoneda: fMoneda || undefined, desde: desde || undefined, hasta: hasta || undefined } }),
      ]);
      setMonedas(m.data); setVigentes(v.data); setFilas(h.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudo cargar el tipo de cambio.")); }
    finally { setCargando(false); }
  }, [api, fMoneda, desde, hasta]);
  useEffect(() => { void cargar(); }, [cargar]);

  const cotizables = monedas.filter((m) => !m.esBase && m.estado === "Activo");
  const base = monedas.find((m) => m.esBase);

  const columnas: ColumnaTabla<TipoCambioFila>[] = useMemo(() => [
    { clave: "fecha", encabezado: "Vigente desde", render: (r) => fmtFecha(r.fechaVigencia), textoBusqueda: (r) => r.fechaVigencia },
    { clave: "mon", encabezado: "Moneda", render: (r) => `${r.monedaCodigo} · ${r.monedaNombre}`, textoBusqueda: (r) => `${r.monedaCodigo} ${r.monedaNombre}` },
    { clave: "compra", encabezado: "Compra", render: (r) => num(r.compra) },
    { clave: "venta", encabezado: "Venta", render: (r) => num(r.venta) },
    { clave: "contable", encabezado: "Valor contable", render: (r) => num(r.contable) },
    { clave: "origen", encabezado: "Origen", render: (r) => r.origen },
    { clave: "ges", encabezado: "Gestión", render: (r) => (r.idGestion ? (r.numeroGestion ?? `#${r.idGestion}`) : "—") },
    { clave: "por", encabezado: "Cargado por", render: (r) => `${r.creadoPor} · ${fmtFechaHora(r.creadoEn)}`, textoBusqueda: (r) => r.creadoPor },
  ], []);

  // ?corregir=N: gestion de tipo de cambio devuelta; se abre el formulario con lo que ya se habia pedido.
  useEffect(() => {
    const id = params.get("corregir");
    if (!id || cotizables.length === 0) return;
    setParams({}, { replace: true });
    void (async () => {
      try {
        const r = await api.get<GestionDetalleResponse>(`/api/gestiones/${id}`);
        const g = r.data;
        if (g.estado !== "Devuelta" || !g.esMia || g.entidadNegocio !== "TIPO_CAMBIO") { setError("La gestión indicada ya no está devuelta para su corrección."); return; }
        const v = (c: string) => g.campos.find((x) => x.nombreCampo === c)?.nuevoCrudo ?? "";
        setDevuelta(g); setExito(null); setErrorForm(null);
        setForm({ idMoneda: Number(v("id_moneda")), fechaVigencia: v("fecha_vigencia"), compra: v("compra"), venta: v("venta"), contable: v("contable"), comentario: g.comentario ?? "" });
        setPanel(true);
      } catch (e) { setError(mensajeError(e, "No se pudo cargar la gestión devuelta.")); }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params, cotizables.length]);

  function abrirSolicitud(prefijar?: TipoCambioFila) {
    setExito(null); setErrorForm(null); setDevuelta(null);
    setForm({
      idMoneda: prefijar?.idMoneda ?? cotizables[0]?.idMoneda ?? 0,
      fechaVigencia: prefijar ? prefijar.fechaVigencia : hoyIso(),
      compra: prefijar ? String(prefijar.compra) : "", venta: prefijar ? String(prefijar.venta) : "",
      contable: prefijar ? String(prefijar.contable) : "", comentario: "",
    });
    setPanel(true);
  }

  async function enviar(e: FormEvent) {
    e.preventDefault(); setErrorForm(null);
    const compra = parseDecimal(form.compra), venta = parseDecimal(form.venta), contable = parseDecimal(form.contable);
    if (!form.idMoneda) return setErrorForm("Seleccione la moneda.");
    if (!form.fechaVigencia) return setErrorForm("Indique la fecha de vigencia.");
    if (compra === null || venta === null || contable === null)
      return setErrorForm("Compra, venta y valor contable deben ser números válidos (ej. 520,50).");
    if (compra <= 0 || venta <= 0 || contable <= 0) return setErrorForm("Los valores deben ser mayores que cero.");
    if ([compra, venta, contable].some((n) => Math.round(n * 1e6) / 1e6 !== n)) return setErrorForm("Máximo 6 decimales.");
    if (compra > venta) return setErrorForm("La compra no puede ser mayor que la venta.");
    setGuardando(true);
    try {
      const r = await api.post<{ idGestion: number; numero?: string }>("/api/tipos-cambio/solicitud", {
        idMoneda: form.idMoneda, fechaVigencia: form.fechaVigencia, compra, venta, contable,
        comentario: form.comentario || null, idGestionDevuelta: devuelta?.idGestion ?? null,
      });
      setExito({ texto: "Tipo de cambio enviado a aprobación: rige cuando otro usuario apruebe la gestión.", idGestion: r.data.idGestion, numero: r.data.numero });
      setPanel(false); setDevuelta(null);
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo enviar el tipo de cambio.")); }
    finally { setGuardando(false); }
  }

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const filaSel = filas.find((f) => f.idTipoCambio === idSel) ?? null;

  return (
    <div>
      <h1>Tipo de cambio</h1>
      <p className="nota">
        Cotización contra la moneda base{base ? ` (${base.codigo})` : ""}. Cada tipo de cambio rige desde su fecha hasta la siguiente cargada.
        El ingreso va por gestión y se aplica cuando otro usuario lo aprueba. El valor contable es el que se usa en los cálculos contables.
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && (
        <p className="mensaje-exito">
          {exito.texto} {exito.idGestion && <Link to="/gestiones">Ver gestión {exito.numero ?? exito.idGestion}</Link>}
        </p>
      )}

      <section className="seccion">
        <h2>Vigentes hoy</h2>
        {vigentes.length === 0 ? <p className="nota">No hay monedas distintas de la base. Créelas en Administración › Monedas.</p> : (
          <table className="tabla">
            <thead><tr><th>Moneda</th><th>Compra</th><th>Venta</th><th>Valor contable</th><th>Vigente desde</th></tr></thead>
            <tbody>
              {vigentes.map((v) => (
                <tr key={v.idMoneda}>
                  <td>{v.monedaCodigo} · {v.monedaNombre}</td>
                  <td>{num(v.compra)}</td><td>{num(v.venta)}</td><td>{num(v.contable)}</td>
                  <td>{v.fechaVigencia ? fmtFecha(v.fechaVigencia) : <em>Sin tipo de cambio cargado</em>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <div className="formulario-en-linea" style={{ margin: "1rem 0", flexWrap: "wrap", alignItems: "center" }}>
        <select value={fMoneda} onChange={(e) => setFMoneda(Number(e.target.value))} aria-label="Moneda">
          <option value={0}>Todas las monedas</option>
          {cotizables.map((m) => (<option key={m.idMoneda} value={m.idMoneda}>{m.codigo}</option>))}
        </select>
        <label>Desde <input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
        <label>Hasta <input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
      </div>

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idTipoCambio}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay tipos de cambio cargados para el filtro."
        comandos={[
          { clave: "nueva", etiqueta: "Ingreso de tipo de cambio", icono: "+", oculto: !puedeCargar, onClick: () => abrirSolicitud() },
          { clave: "corregir", etiqueta: "Ingresar corrección", icono: "✎", oculto: !puedeCargar, requiereSeleccion: "una", onClick: () => filaSel && abrirSolicitud(filaSel) },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {panel && (
        <section className="panel-formulario">
          <h3>Ingreso de tipo de cambio</h3>
          {devuelta && (
            <div className="gestion-modal__devolucion">
              <strong>Gestión {devuelta.numero ?? devuelta.idGestion} devuelta por {devuelta.devueltoPor}:</strong> {devuelta.comentarioDevolucion}
            </div>
          )}
          {errorForm && <p className="mensaje-error" style={{ whiteSpace: "pre-line" }}>{errorForm}</p>}
          <form className="formulario" onSubmit={enviar} noValidate>
            <label>Moneda
              <select value={form.idMoneda} onChange={(e) => setForm({ ...form, idMoneda: Number(e.target.value) })} required>
                {cotizables.map((m) => (<option key={m.idMoneda} value={m.idMoneda}>{m.codigo} · {m.nombre}</option>))}
              </select>
            </label>
            <label>Vigente desde
              <input type="date" value={form.fechaVigencia} onChange={(e) => setForm({ ...form, fechaVigencia: e.target.value })} required />
            </label>
            <label>Compra
              <input type="text" inputMode="decimal" value={form.compra} onChange={(e) => setForm({ ...form, compra: e.target.value })} required />
            </label>
            <label>Venta
              <input type="text" inputMode="decimal" value={form.venta} onChange={(e) => setForm({ ...form, venta: e.target.value })} required />
            </label>
            <label>Valor contable
              <input type="text" inputMode="decimal" value={form.contable} onChange={(e) => setForm({ ...form, contable: e.target.value })} required />
            </label>
            <label>Comentario (opcional)
              <input type="text" maxLength={500} value={form.comentario} onChange={(e) => setForm({ ...form, comentario: e.target.value })} />
            </label>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando || cotizables.length === 0}>{guardando ? "Enviando..." : "Enviar a aprobación"}</button>
              <button type="button" className="boton-secundario" onClick={() => { setPanel(false); setDevuelta(null); }}>Cancelar</button>
            </div>
          </form>
        </section>
      )}
    </div>
  );
}
