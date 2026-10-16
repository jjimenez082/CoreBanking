import { useEffect, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import type { CatalogoItemResponse, CumplimientoResponse, GestionDetalleResponse, InterlocutorDetalleResponse } from "../api/tipos";

function Campo({ etiqueta, children }: { etiqueta: string; children: ReactNode }) {
  return (
    <label className="wiz-campo">
      <span>{etiqueta}</span>
      {children}
    </label>
  );
}

type FilaExpediente = { id: number | null; referencia: string; sistema: string; descripcion: string };
type FilaEvaluacion = { tipo: "PEP" | "Sanciones"; idResultado: number; fecha: string; fuente: string; observacion: string };

const LISTAS = ["ESTADO_KYC", "NIVEL_DEBIDA_DILIGENCIA", "CLASIFICACION_RIESGO", "MOTIVO_RIESGO", "RESULTADO_PEP", "RESULTADO_SANCIONES", "SEGMENTO_CLIENTE", "SEGMENTO_PRICING", "SEGMENTO_RIESGO"] as const;
type Listas = Record<(typeof LISTAS)[number], CatalogoItemResponse[]>;

const fmt = (iso?: string | null) => (iso ? iso.slice(0, 10) : "—");

/**
 * Actualizar el KYC y cumplimiento de un cliente (ruta /interlocutores/:id/cumplimiento).
 * Se solicita por gestión CLI_KYC y requiere aprobación. Las evaluaciones PEP y de sanciones son un historial: no se editan,
 * se agrega una nueva (la más reciente de cada tipo es la vigente).
 */
export function CumplimientoPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const idInt = Number(id);
  const { tienePermiso } = usePermisos();

  const [det, setDet] = useState<InterlocutorDetalleResponse | null>(null);
  const [actual, setActual] = useState<CumplimientoResponse | null>(null);
  const [listas, setListas] = useState<Listas | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [estado, setEstado] = useState(0);
  const [nivel, setNivel] = useState(0);
  const [riesgo, setRiesgo] = useState(0);
  const [ultima, setUltima] = useState("");
  const [proxima, setProxima] = useState("");
  const [segCliente, setSegCliente] = useState(0);
  const [segPricing, setSegPricing] = useState(0);
  const [segRiesgo, setSegRiesgo] = useState(0);
  const [motivos, setMotivos] = useState<number[]>([]);
  const [expedientes, setExpedientes] = useState<FilaExpediente[]>([]);
  const [nuevas, setNuevas] = useState<FilaEvaluacion[]>([]);
  const [comentario, setComentario] = useState("");
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<GestionDetalleResponse | null>(null);

  useEffect(() => {
    let cancelado = false;
    Promise.all([
      api.get<InterlocutorDetalleResponse>(`/api/interlocutores/${idInt}`),
      api.get<CumplimientoResponse>(`/api/interlocutores/${idInt}/cumplimiento`),
      ...LISTAS.map((l) => api.get<CatalogoItemResponse[]>(`/api/catalogos/listas/${l}`)),
    ]).then(([d, c, ...ls]) => {
      if (cancelado) return;
      const dato = c.data as CumplimientoResponse;
      setDet(d.data as InterlocutorDetalleResponse);
      setActual(dato);
      setListas(Object.fromEntries(LISTAS.map((l, i) => [l, (ls[i].data as CatalogoItemResponse[])])) as Listas);
      const k = dato.kyc;
      setEstado(k?.idEstadoKyc ?? 0); setNivel(k?.idNivelDebidaDiligencia ?? 0); setRiesgo(k?.idClasificacionRiesgo ?? 0);
      setUltima(k?.fechaUltimaRevision?.slice(0, 10) ?? ""); setProxima(k?.fechaProximaRevision?.slice(0, 10) ?? "");
      setSegCliente(k?.idSegmentoCliente ?? 0); setSegPricing(k?.idSegmentoPricing ?? 0); setSegRiesgo(k?.idSegmentoRiesgo ?? 0);
      setMotivos(dato.motivosRiesgo.map((m) => m.idMotivo));
      setExpedientes(dato.expedientes.map((x) => ({ id: x.id, referencia: x.referencia, sistema: x.sistema ?? "", descripcion: x.descripcion ?? "" })));
    }).catch((e) => !cancelado && setError(mensajeError(e, "No se pudo cargar el cumplimiento del cliente.")));
    return () => { cancelado = true; };
  }, [api, idInt]);

  const nz = (n: number) => (n ? n : null);

  async function enviar() {
    for (const [i, x] of expedientes.entries()) if (!x.referencia.trim()) { setErrorForm(`Referencia de expediente #${i + 1}: la referencia es obligatoria.`); return; }
    for (const [i, x] of nuevas.entries()) {
      if (!x.idResultado) { setErrorForm(`Evaluación nueva #${i + 1}: elija el resultado.`); return; }
      if (!x.fecha) { setErrorForm(`Evaluación nueva #${i + 1}: indique la fecha.`); return; }
      if (!x.fuente.trim()) { setErrorForm(`Evaluación nueva #${i + 1}: indique la fuente o lista consultada.`); return; }
    }
    if (comentario.trim().length > 0 && comentario.trim().length < 10) { setErrorForm("El comentario, si lo indica, debe tener al menos 10 caracteres."); return; }
    setErrorForm(null); setEnviando(true); setError(null);
    try {
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInt}/cumplimiento`, {
        kyc: {
          idEstadoKyc: nz(estado), idNivelDebidaDiligencia: nz(nivel), idClasificacionRiesgo: nz(riesgo),
          fechaUltimaRevision: ultima || null, fechaProximaRevision: proxima || null,
          idSegmentoCliente: nz(segCliente), idSegmentoPricing: nz(segPricing), idSegmentoRiesgo: nz(segRiesgo),
        },
        idsMotivosRiesgo: motivos,
        expedientes: expedientes.map((x) => ({ id: x.id, referencia: x.referencia.trim(), sistema: x.sistema.trim() || null, descripcion: x.descripcion.trim() || null })),
        nuevasEvaluaciones: nuevas.map((x) => ({ tipo: x.tipo, idResultado: x.idResultado, fecha: x.fecha, fuente: x.fuente.trim(), observacion: x.observacion.trim() || null })),
        comentario: comentario.trim() || null,
      });
      setResultado(r.data);
    } catch (err) {
      setError(mensajeError(err, "No se pudo solicitar la actualización de cumplimiento."));
    } finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_KYC")) return <p className="mensaje-error">No tiene permiso para actualizar el KYC y cumplimiento.</p>;
  if (!det || !listas || !actual) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;
  if (det.tipoPersona === "Grupo") return <p className="mensaje-error">El KYC de un grupo se gestiona por sus miembros. <Link to={`/interlocutores/${idInt}`}>Volver</Link></p>;

  const bloqueado = det.estado !== "Activo" || !!det.idGestionPendiente;
  const nombreCliente = det.tipoPersona === "Juridica" ? det.organizacion?.razonSocial : [det.nombre1, det.apellido1].filter(Boolean).join(" ");

  if (resultado) {
    return (
      <div className="wiz">
        <div className="wiz__titulo"><div><h1>Actualización de cumplimiento solicitada</h1></div></div>
        <div className="wiz__aviso">
          Se creó la gestión <strong>{resultado.numero ?? `#${resultado.idGestion}`}</strong>. El KYC del cliente no cambia hasta que se apruebe
          {resultado.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
        </div>
        <div style={{ display: "flex", gap: "0.5rem", marginTop: "1rem" }}>
          <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Volver al cliente</button>
        </div>
      </div>
    );
  }

  const combo = (valor: number, set: (n: number) => void, lista: CatalogoItemResponse[]) => (
    <select value={valor} disabled={bloqueado} onChange={(ev) => set(Number(ev.target.value))}>
      <option value={0}>— Sin definir —</option>
      {lista.map((i) => <option key={i.id} value={i.id}>{i.nombre}</option>)}
    </select>
  );
  const alternar = (idM: number) => setMotivos((m) => (m.includes(idM) ? m.filter((x) => x !== idM) : [...m, idM]));

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Cumplimiento y KYC · N.º {det.numero}</h1>
          <div className="nota">{nombreCliente} · Todo cambio se solicita por gestión y requiere aprobación.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Cancelar</button>
      </div>

      {det.estado !== "Activo" && <div className="wiz__alerta">El cliente está «{det.estado}» y no admite cambios.</div>}
      {det.idGestionPendiente && <div className="wiz__alerta">Tiene la gestión #{det.idGestionPendiente} pendiente de aprobación; debe resolverse antes.</div>}
      {error && <p className="mensaje-error">{error}</p>}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cabecera"><h2>Expediente KYC</h2></div>
        <div className="wiz__cuerpo">
          <div className="wiz__rejilla">
            <Campo etiqueta="Estado KYC">{combo(estado, setEstado, listas.ESTADO_KYC)}</Campo>
            <Campo etiqueta="Nivel de debida diligencia">{combo(nivel, setNivel, listas.NIVEL_DEBIDA_DILIGENCIA)}</Campo>
            <Campo etiqueta="Clasificación de riesgo">{combo(riesgo, setRiesgo, listas.CLASIFICACION_RIESGO)}</Campo>
            <Campo etiqueta="Última revisión"><input type="date" value={ultima} disabled={bloqueado} onChange={(ev) => setUltima(ev.target.value)} /></Campo>
            <Campo etiqueta="Próxima revisión (automática)"><input type="date" value={proxima} disabled readOnly title="Se calcula sola: última revisión + frecuencia según el riesgo" /></Campo>
          </div>
          {actual?.vigencia && actual.vigencia.situacion !== "Sin KYC" && (
            <p className={actual.vigencia.situacion === "Vencido" ? "mensaje-error" : "nota"}>Vigencia: <strong>{actual.vigencia.situacion}</strong>. {actual.vigencia.mensaje}</p>
          )}
          <p className="nota">La próxima revisión se calcula automáticamente al aprobarse la gestión: última revisión + la frecuencia que corresponde al riesgo (parámetros de Cumplimiento). Al crear el cliente se fija con el plazo de primera revisión.</p>
          <p className="nota">Para dejar el KYC en «Completo» se exige nivel, riesgo y la fecha de la última revisión, y que no haya una evaluación PEP o de sanciones en revisión ni una coincidencia confirmada en sanciones.</p>
          <div style={{ marginTop: "0.5rem" }}>
            <strong>Motivos de la clasificación de riesgo</strong>
            <div style={{ display: "flex", flexWrap: "wrap", gap: "0.4rem 1.25rem", marginTop: "0.35rem" }}>
              {listas.MOTIVO_RIESGO.map((m) => (
                <label key={m.id} style={{ display: "flex", gap: "0.35rem", alignItems: "center" }}>
                  <input type="checkbox" checked={motivos.includes(m.id)} disabled={bloqueado} onChange={() => alternar(m.id)} />
                  {m.nombre}
                </label>
              ))}
            </div>
          </div>
        </div>
      </section>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cabecera"><h2>Segmentación</h2></div>
        <div className="wiz__cuerpo">
          <div className="wiz__rejilla">
            <Campo etiqueta="Segmento de cliente">{combo(segCliente, setSegCliente, listas.SEGMENTO_CLIENTE)}</Campo>
            <Campo etiqueta="Segmento de pricing">{combo(segPricing, setSegPricing, listas.SEGMENTO_PRICING)}</Campo>
            <Campo etiqueta="Segmento de riesgo">{combo(segRiesgo, setSegRiesgo, listas.SEGMENTO_RIESGO)}</Campo>
          </div>
        </div>
      </section>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cabecera"><h2>Referencias de expediente</h2></div>
        <div className="wiz__cuerpo">
          <p className="nota" style={{ marginTop: 0 }}>Se guarda la referencia al expediente en el gestor documental, no el documento.</p>
          {expedientes.map((x, i) => (
            <div key={i} className="wiz__rejilla" style={{ alignItems: "end", marginBottom: "0.4rem" }}>
              <Campo etiqueta="Referencia *"><input type="text" maxLength={100} value={x.referencia} disabled={bloqueado}
                onChange={(ev) => setExpedientes((l) => l.map((y, j) => (j === i ? { ...y, referencia: ev.target.value } : y)))} /></Campo>
              <Campo etiqueta="Sistema"><input type="text" maxLength={100} value={x.sistema} disabled={bloqueado}
                onChange={(ev) => setExpedientes((l) => l.map((y, j) => (j === i ? { ...y, sistema: ev.target.value } : y)))} /></Campo>
              <Campo etiqueta="Descripción"><input type="text" maxLength={300} value={x.descripcion} disabled={bloqueado}
                onChange={(ev) => setExpedientes((l) => l.map((y, j) => (j === i ? { ...y, descripcion: ev.target.value } : y)))} /></Campo>
              <button type="button" className="boton-secundario" disabled={bloqueado} onClick={() => setExpedientes((l) => l.filter((_, j) => j !== i))}>Quitar</button>
            </div>
          ))}
          <button type="button" className="boton-secundario" disabled={bloqueado}
            onClick={() => setExpedientes((l) => [...l, { id: null, referencia: "", sistema: "", descripcion: "" }])}>+ Agregar referencia</button>
        </div>
      </section>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cabecera"><h2>Evaluaciones PEP y sanciones</h2></div>
        <div className="wiz__cuerpo">
          <p className="nota" style={{ marginTop: 0 }}>Historial: no se edita. Para registrar un nuevo resultado agregue una evaluación; la más reciente de cada tipo queda como vigente.</p>
          {actual.evaluaciones.length > 0 ? (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Tipo</th><th>Resultado</th><th>Fecha</th><th>Fuente</th><th>Observación</th><th>Vigente</th></tr></thead>
              <tbody>{actual.evaluaciones.map((e) => (
                <tr key={e.id}><td>{e.tipo === "PEP" ? "PEP" : "Sanciones"}</td><td>{e.resultado}</td><td>{fmt(e.fecha)}</td><td>{e.fuente}</td><td>{e.observacion ?? "—"}</td><td>{e.vigente ? "Sí" : ""}</td></tr>
              ))}</tbody>
            </table>
          ) : <p className="nota">Sin evaluaciones registradas.</p>}

          {nuevas.map((x, i) => (
            <div key={i} className="wiz__rejilla" style={{ alignItems: "end", margin: "0.5rem 0" }}>
              <Campo etiqueta="Tipo *">
                <select value={x.tipo} disabled={bloqueado}
                  onChange={(ev) => setNuevas((l) => l.map((y, j) => (j === i ? { ...y, tipo: ev.target.value as "PEP" | "Sanciones", idResultado: 0 } : y)))}>
                  <option value="PEP">PEP</option><option value="Sanciones">Sanciones</option>
                </select>
              </Campo>
              <Campo etiqueta="Resultado *">
                <select value={x.idResultado} disabled={bloqueado}
                  onChange={(ev) => setNuevas((l) => l.map((y, j) => (j === i ? { ...y, idResultado: Number(ev.target.value) } : y)))}>
                  <option value={0}>Seleccione...</option>
                  {(x.tipo === "PEP" ? listas.RESULTADO_PEP : listas.RESULTADO_SANCIONES).map((r) => <option key={r.id} value={r.id}>{r.nombre}</option>)}
                </select>
              </Campo>
              <Campo etiqueta="Fecha *"><input type="date" value={x.fecha} disabled={bloqueado}
                onChange={(ev) => setNuevas((l) => l.map((y, j) => (j === i ? { ...y, fecha: ev.target.value } : y)))} /></Campo>
              <Campo etiqueta="Fuente o lista consultada *"><input type="text" maxLength={200} value={x.fuente} disabled={bloqueado}
                onChange={(ev) => setNuevas((l) => l.map((y, j) => (j === i ? { ...y, fuente: ev.target.value } : y)))} /></Campo>
              <Campo etiqueta="Observación"><input type="text" maxLength={500} value={x.observacion} disabled={bloqueado}
                onChange={(ev) => setNuevas((l) => l.map((y, j) => (j === i ? { ...y, observacion: ev.target.value } : y)))} /></Campo>
              <button type="button" className="boton-secundario" disabled={bloqueado} onClick={() => setNuevas((l) => l.filter((_, j) => j !== i))}>Quitar</button>
            </div>
          ))}
          <button type="button" className="boton-secundario" disabled={bloqueado}
            onClick={() => setNuevas((l) => [...l, { tipo: "PEP", idResultado: 0, fecha: new Date().toISOString().slice(0, 10), fuente: "", observacion: "" }])}>+ Agregar evaluación</button>
        </div>
      </section>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <Campo etiqueta="Comentario (opcional)">
            <input type="text" style={{ width: "100%", maxWidth: 640 }} maxLength={500} value={comentario} disabled={bloqueado} onChange={(ev) => setComentario(ev.target.value)} />
          </Campo>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem" }}>
            <button type="button" disabled={bloqueado || enviando} onClick={enviar}>{enviando ? "Solicitando…" : "Solicitar actualización"}</button>
            <Link to={`/interlocutores/${idInt}`} className="boton-secundario" style={{ padding: "0.45rem 0.9rem" }}>Cancelar</Link>
          </div>
        </div>
      </section>
    </div>
  );
}
