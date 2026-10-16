import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import type {
  GestionDetalleResponse, InterlocutorBusquedaResponse, RelacionDetalleResponse,
  RelacionesInterlocutorResponse, RelacionTipoResponse,
} from "../api/tipos";

function Campo({ etiqueta, children }: { etiqueta: string; children: ReactNode }) {
  return (
    <label className="wiz-campo">
      <span>{etiqueta}</span>
      {children}
    </label>
  );
}

const num = (v: string) => (v.trim() === "" ? null : Number(v.replace(",", ".")));
const txt = (n?: number | null) => (n === null || n === undefined ? "" : String(n));

/**
 * Nueva relación o edición de una relación vigente de un interlocutor (rutas /relaciones/nueva y /relaciones/:idRel/editar).
 * El cambio NO se aplica aquí: se solicita como gestión CLI_RELACION y requiere aprobación.
 * En la edición el tipo y el otro interlocutor no cambian: si estaban mal, se inactiva la relación y se crea otra.
 */
export function RelacionFormPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id, idRel } = useParams();
  const idInt = Number(id);
  const esEditar = idRel !== undefined;
  const { tienePermiso } = usePermisos();

  const [datos, setDatos] = useState<RelacionesInterlocutorResponse | null>(null);
  const esGrupo = datos?.tipoPersona === "Grupo";
  const [tipos, setTipos] = useState<RelacionTipoResponse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<GestionDetalleResponse | null>(null);

  const [idTipo, setIdTipo] = useState(0);
  const [sentido, setSentido] = useState<"Origen" | "Destino">("Origen");
  const [busqueda, setBusqueda] = useState("");
  const [encontrados, setEncontrados] = useState<InterlocutorBusquedaResponse[]>([]);
  const [otro, setOtro] = useState<InterlocutorBusquedaResponse | null>(null);
  const [pPart, setPPart] = useState(""); const [pVoto, setPVoto] = useState(""); const [pCtrl, setPCtrl] = useState("");
  const [principal, setPrincipal] = useState(false);
  const [funcion, setFuncion] = useState("");
  const [fechaInicio, setFechaInicio] = useState("");
  const [errorForm, setErrorForm] = useState<string | null>(null);

  const actual: RelacionDetalleResponse | undefined = useMemo(
    () => (esEditar ? datos?.vigentes.find((r) => r.idRelacion === Number(idRel)) : undefined), [datos, esEditar, idRel]);

  useEffect(() => {
    let cancelado = false;
    Promise.all([
      api.get<RelacionesInterlocutorResponse>(`/api/interlocutores/${idInt}/relaciones`),
      api.get<RelacionTipoResponse[]>("/api/interlocutores/relaciones/tipos"),
    ]).then(([r, t]) => { if (!cancelado) { setDatos(r.data); setTipos(t.data); } })
      .catch((e) => !cancelado && setError(mensajeError(e, "No se pudo cargar la información.")));
    return () => { cancelado = true; };
  }, [api, idInt]);

  // En un grupo el tipo es siempre «miembro» y el grupo es el destino de la relación.
  useEffect(() => {
    if (esEditar || !esGrupo) return;
    const m = tipos.find((t) => t.codigo === "MIEMBRO_GRUPO");
    if (m) { setIdTipo(m.idTipoRelacion); setSentido("Destino"); }
  }, [esEditar, esGrupo, tipos]);

  // Al editar se precargan los valores vigentes.
  useEffect(() => {
    if (!actual) return;
    setPPart(txt(actual.porcentajeParticipacion)); setPVoto(txt(actual.porcentajeVoto)); setPCtrl(txt(actual.porcentajeControl));
    setPrincipal(actual.esPrincipal); setFuncion(actual.funcion ?? ""); setFechaInicio(actual.fechaInicio.slice(0, 10));
  }, [actual]);

  useEffect(() => {
    if (esEditar || busqueda.trim().length < 2 || otro) { setEncontrados([]); return; }
    let cancelado = false;
    const t = setTimeout(async () => {
      try {
        const r = await api.get<InterlocutorBusquedaResponse[]>("/api/interlocutores/buscar", { params: { q: busqueda.trim() } });
        if (!cancelado) setEncontrados(r.data.filter((x) => x.idInterlocutor !== idInt && x.estado === "Activo"));
      } catch { if (!cancelado) setEncontrados([]); }
    }, 300);
    return () => { cancelado = true; clearTimeout(t); };
  }, [api, busqueda, otro, idInt, esEditar]);

  const tipo = useMemo(
    () => tipos.find((t) => t.idTipoRelacion === (esEditar ? actual?.idTipoRelacion : idTipo)) ?? null, [tipos, idTipo, esEditar, actual]);
  const bloqueado = !datos || !datos.puedeSolicitar;
  // Un grupo solo admite miembros; el resto de interlocutores no ve el tipo «miembro» (se agrega desde el grupo).
  const tiposVisibles = useMemo(() => tipos.filter((t) => {
    const p = datos?.tipoPersona;
    if (!p) return true;
    if (esGrupo) return t.codigo === "MIEMBRO_GRUPO";
    if (t.codigo === "MIEMBRO_GRUPO") return false;
    return t.tiposOrigen.includes(p) || t.tiposDestino.includes(p);
  }), [tipos, datos, esGrupo]);

  function validar(): string | null {
    if (!esEditar) {
      if (!tipo) return "Elija el tipo de relación.";
      if (!otro) return esGrupo ? "Busque y elija el miembro." : "Busque y elija el otro interlocutor.";
    }
    for (const [v, e] of [[pPart, "participación"], [pVoto, "voto"], [pCtrl, "control"]] as const) {
      const n = num(v);
      if (n !== null && (Number.isNaN(n) || n < 0 || n > 100)) return `El % de ${e} debe estar entre 0 y 100.`;
    }
    if (motivo.trim().length < 10) return "Indique el motivo (mínimo 10 caracteres).";
    return null;
  }

  async function solicitar() {
    const e = validar();
    setErrorForm(e);
    if (e) return;
    setEnviando(true); setError(null);
    const valores = tipo?.usaPorcentajes
      ? { porcentajeParticipacion: num(pPart), porcentajeVoto: num(pVoto), porcentajeControl: num(pCtrl) }
      : { porcentajeParticipacion: null, porcentajeVoto: null, porcentajeControl: null };
    try {
      const cuerpo = esEditar
        ? { modificar: [{ idRelacion: Number(idRel), ...valores, esPrincipal: principal, funcion: funcion.trim() || null, fechaInicio: fechaInicio || null }], motivo: motivo.trim() }
        : {
            agregar: [{
              idOtro: otro!.idInterlocutor, idTipoRelacion: tipo!.idTipoRelacion, sentido: tipo!.esSimetrica ? "Origen" : esGrupo ? "Destino" : sentido, ...valores,
              esPrincipal: principal, funcion: funcion.trim() || null, fechaInicio: fechaInicio || null,
            }],
            motivo: motivo.trim(),
          };
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInt}/relaciones`, cuerpo);
      setResultado(r.data);
    } catch (err) {
      setError(mensajeError(err, "No se pudo solicitar el cambio de relaciones."));
    } finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_RELACION")) return <p className="mensaje-error">No tiene permiso para gestionar relaciones.</p>;
  if (!datos) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;
  if (esEditar && !actual) return <p className="mensaje-error">La relación no existe o ya no está vigente. <Link to={`/interlocutores/${idInt}`}>Volver</Link></p>;

  if (resultado) {
    return (
      <div className="wiz">
        <div className="wiz__titulo"><div><h1>{esEditar ? "Modificación de relación solicitada" : "Relación solicitada"}</h1></div></div>
        <div className="wiz__aviso">
          Se creó la gestión <strong>{resultado.numero ?? `#${resultado.idGestion}`}</strong>. La relación no cambia hasta que se apruebe
          {resultado.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
        </div>
        <div style={{ display: "flex", gap: "0.5rem", marginTop: "1rem" }}>
          <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Volver al interlocutor</button>
        </div>
      </div>
    );
  }

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>{esEditar ? "Editar relación" : esGrupo ? "Agregar miembro" : "Nueva relación"} · interlocutor N.º {datos.numero}</h1>
          <div className="nota">Todo cambio de relaciones se solicita por gestión y requiere aprobación.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Cancelar</button>
      </div>

      {datos.estado !== "Activo" && <div className="wiz__alerta">El interlocutor está «{datos.estado}» y no admite cambios de relaciones.</div>}
      {datos.numeroGestionPendiente && <div className="wiz__alerta">Tiene la gestión {datos.numeroGestionPendiente} pendiente de aprobación; debe resolverse antes.</div>}
      {error && <p className="mensaje-error">{error}</p>}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          {esEditar && actual ? (
            <p style={{ marginTop: 0 }}>
              <strong>{actual.etiqueta}</strong> → <Link to={`/interlocutores/${actual.idOtro}`}>{actual.nombreOtro}</Link>{" "}
              <span className="nota">N.º {actual.numeroOtro}. El tipo y el otro interlocutor no se pueden cambiar; si están mal, inactive la relación y cree otra.</span>
            </p>
          ) : (
            <>
              <p className="nota" style={{ marginTop: 0 }}>{esGrupo ? "Busque la persona u organización que será miembro del grupo." : "Elija el tipo y busque al otro interlocutor."}</p>
              <div className="wiz__rejilla">
                {!esGrupo && (<Campo etiqueta="Tipo de relación">
                  <select value={idTipo} disabled={bloqueado} onChange={(e) => { setIdTipo(Number(e.target.value)); setSentido("Origen"); }}>
                    <option value={0}>Seleccione...</option>
                    {tiposVisibles.map((t) => (<option key={t.idTipoRelacion} value={t.idTipoRelacion}>{t.nombre.replace(/ de$/, "")}</option>))}
                  </select>
                </Campo>)}
                {!esGrupo && tipo && !tipo.esSimetrica && (
                  <Campo etiqueta="Papel de este interlocutor">
                    <select value={sentido} onChange={(e) => setSentido(e.target.value as "Origen" | "Destino")}>
                      <option value="Origen">{tipo.nombre} … el otro</option>
                      <option value="Destino">{tipo.etiquetaInversa} … el otro</option>
                    </select>
                  </Campo>
                )}
                <Campo etiqueta={esGrupo ? "Miembro" : "Otro interlocutor"}>
                  {otro ? (
                    <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
                      <strong>{otro.numero} · {otro.nombre}</strong>
                      <button type="button" className="wiz-enlace" onClick={() => { setOtro(null); setBusqueda(""); }}>Cambiar</button>
                    </span>
                  ) : (
                    <input type="text" value={busqueda} disabled={bloqueado} placeholder="Buscar por nombre, N.º o identificación" onChange={(e) => setBusqueda(e.target.value)} />
                  )}
                </Campo>
              </div>
              {encontrados.length > 0 && (
                <ul className="lista-checkbox" style={{ maxHeight: 180, overflowY: "auto", margin: "0.5rem 0" }}>
                  {encontrados.map((x) => (
                    <li key={x.idInterlocutor}>
                      <button type="button" className="wiz-enlace" onClick={() => { setOtro(x); setEncontrados([]); }}>{x.numero} · {x.nombre}</button>{" "}
                      <span className="nota">{x.identificaciones}</span>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}

          {tipo?.usaPorcentajes && (
            <div className="wiz__rejilla">
              <Campo etiqueta="% de participación"><input type="text" inputMode="decimal" placeholder="0 – 100" value={pPart} disabled={bloqueado} onChange={(e) => setPPart(e.target.value)} /></Campo>
              <Campo etiqueta="% de voto"><input type="text" inputMode="decimal" placeholder="0 – 100" value={pVoto} disabled={bloqueado} onChange={(e) => setPVoto(e.target.value)} /></Campo>
              <Campo etiqueta="% de control"><input type="text" inputMode="decimal" placeholder="0 – 100" value={pCtrl} disabled={bloqueado} onChange={(e) => setPCtrl(e.target.value)} /></Campo>
            </div>
          )}
          <div className="wiz__rejilla">
            <Campo etiqueta="Función (opcional)"><input type="text" maxLength={200} value={funcion} disabled={bloqueado} onChange={(e) => setFuncion(e.target.value)} /></Campo>
            <Campo etiqueta="Fecha de inicio (opcional)"><input type="date" value={fechaInicio} disabled={bloqueado} onChange={(e) => setFechaInicio(e.target.value)} /></Campo>
            <label style={{ display: "flex", alignItems: "center", gap: 8, alignSelf: "end", paddingBottom: 8 }}>
              <input type="checkbox" checked={principal} disabled={bloqueado} onChange={(e) => setPrincipal(e.target.checked)} /> Marcar como principal
            </label>
          </div>
        </div>
      </section>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <h2 style={{ marginTop: 0 }}>Motivo y solicitud</h2>
          <textarea rows={3} style={{ width: "100%", maxWidth: 640 }} maxLength={500} value={motivo} disabled={bloqueado}
            placeholder="Explique el motivo (mínimo 10 caracteres)" onChange={(e) => setMotivo(e.target.value)} />
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem" }}>
            <button type="button" disabled={bloqueado || enviando} onClick={solicitar}>
              {enviando ? "Solicitando…" : esEditar ? "Solicitar modificación" : esGrupo ? "Solicitar miembro" : "Solicitar relación"}
            </button>
            <Link to={`/interlocutores/${idInt}`} className="boton-secundario" style={{ padding: "0.45rem 0.9rem" }}>Cancelar</Link>
          </div>
        </div>
      </section>
    </div>
  );
}
