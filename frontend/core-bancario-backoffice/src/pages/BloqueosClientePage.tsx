import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError, fmtFechaUtc } from "../api/mensajes";
import { VentanaModal } from "../components/VentanaModal";
import type { BloqueoResponse, BloqueosClienteResponse, CatalogoItemResponse, GestionDetalleResponse, RestriccionesCliente } from "../api/tipos";

function Campo({ etiqueta, children }: { etiqueta: string; children: ReactNode }) {
  return (<label className="wiz-campo"><span>{etiqueta}</span>{children}</label>);
}

/**
 * Bloqueos de un cliente (ruta /interlocutores/:id/bloqueos): vigentes, historial y solicitud de bloqueo o desbloqueo.
 * El estado del cliente (Activo, Inactivo...) no cambia: «bloqueado» es tener al menos un bloqueo vigente. Cada bloqueo es de un tipo
 * (KYC, Cumplimiento, Fraude, Ente externo...) y lleva su comentario. Todo se solicita por gestión con aprobación.
 */
export function BloqueosClientePage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const idInt = Number(id);
  const { tienePermiso } = usePermisos();

  const [datos, setDatos] = useState<BloqueosClienteResponse | null>(null);
  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [restr, setRestr] = useState<RestriccionesCliente | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [idTipo, setIdTipo] = useState(0);
  const [comentario, setComentario] = useState("");
  const [levantando, setLevantando] = useState<BloqueoResponse | null>(null);
  const [comentarioLev, setComentarioLev] = useState("");
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [modal, setModal] = useState<"bloquear" | "desbloquear" | null>(null);
  const [resultado, setResultado] = useState<{ gestion: GestionDetalleResponse; accion: string } | null>(null);

  const cargar = useCallback(async () => {
    try {
      const [d, t, r] = await Promise.all([
        api.get<BloqueosClienteResponse>(`/api/interlocutores/${idInt}/bloqueos`),
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_BLOQUEO"),
        api.get<RestriccionesCliente>(`/api/interlocutores/${idInt}/restricciones`),
      ]);
      setDatos(d.data); setTipos(t.data); setRestr(r.data);
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los bloqueos del cliente.")); }
  }, [api, idInt]);
  useEffect(() => { void cargar(); }, [cargar]);

  async function enviar(bloquear: boolean) {
    const texto = (bloquear ? comentario : comentarioLev).trim();
    const tipo = bloquear ? idTipo : levantando?.idTipoBloqueo ?? 0;
    if (!tipo) { setErrorForm("Elija el tipo de bloqueo."); return; }
    if (texto.length < 10) { setErrorForm("Indique el comentario (mínimo 10 caracteres)."); return; }
    setErrorForm(null); setEnviando(true); setError(null);
    try {
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInt}/${bloquear ? "bloqueos" : "desbloqueos"}`, { idTipoBloqueo: tipo, comentario: texto });
      setResultado({ gestion: r.data, accion: bloquear ? "Bloqueo" : "Desbloqueo" });
    } catch (e) {
      setError(mensajeError(e, bloquear ? "No se pudo solicitar el bloqueo." : "No se pudo solicitar el desbloqueo."));
    } finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_BLOQUEO_VER")) return <p className="mensaje-error">No tiene permiso para consultar los bloqueos de clientes.</p>;
  if (!datos) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;

  function abrirBloqueo() { setIdTipo(0); setComentario(""); setErrorForm(null); setError(null); setResultado(null); setModal("bloquear"); }
  function abrirDesbloqueo(b: BloqueoResponse) { setLevantando(b); setComentarioLev(""); setErrorForm(null); setError(null); setResultado(null); setModal("desbloquear"); }
  function cerrarModal() { const hubo = !!resultado; setModal(null); setLevantando(null); setErrorForm(null); setResultado(null); if (hubo) void cargar(); }

  const puede = tienePermiso("CLI_BLOQUEO") && datos.estado !== "Fusionado";
  const vigentes = datos.bloqueos.filter((b) => b.estado === "Vigente");
  const historial = datos.bloqueos.filter((b) => b.estado !== "Vigente");
  const pendiente = (idTipoB: number) => datos.pendientes.find((p) => p.idTipoBloqueo === idTipoB);
  const tiposLibres = tipos.filter((t) => !vigentes.some((v) => v.idTipoBloqueo === t.id));
  const pendienteElegido = idTipo ? pendiente(idTipo) : undefined;

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Bloqueos · N.º {datos.numero}</h1>
          <div className="nota">{datos.nombre} · {datos.bloqueado ? "Cliente bloqueado" : "Sin bloqueos vigentes"} · Todo cambio se solicita por gestión y requiere aprobación.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Volver al cliente</button>
      </div>
      {error && <p className="mensaje-error">{error}</p>}
      {datos.estado === "Fusionado" && <div className="wiz__alerta">El cliente fue fusionado en otro; los bloqueos se gestionan sobre el cliente que se conservó.</div>}

      {datos.pendientes.length > 0 && (
        <div className="wiz__aviso">
          Solicitudes pendientes de aprobación: {datos.pendientes.map((p) => `${p.numero} (${p.accion === "Bloquear" ? "bloqueo" : "desbloqueo"} · ${p.tipo})`).join("; ")}.
        </div>
      )}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cabecera"><h2>Qué puede hacer el cliente</h2></div>
        <div className="wiz__cuerpo">
          {!restr ? <p className="nota">Cargando...</p> : (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Acción</th><th>Estado</th><th>Motivo</th></tr></thead>
              <tbody>{restr.acciones.map((a) => (
                <tr key={a.codigo}>
                  <td>{a.accion}</td>
                  <td>{a.permitida ? "Permitida" : <strong>Restringida</strong>}</td>
                  <td>{a.motivo ?? "—"}</td>
                </tr>
              ))}</tbody>
            </table>
          )}
        </div>
      </section>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cabecera">
          <h2>Bloqueos vigentes</h2>
          {puede && <button type="button" className="boton-enlace" onClick={abrirBloqueo}>Nuevo bloqueo</button>}
        </div>
        <div className="wiz__cuerpo">
          {vigentes.length === 0 ? <p className="nota">El cliente no tiene bloqueos vigentes.</p> : (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Tipo</th><th>Comentario</th><th>Desde</th><th>Solicitó</th><th /></tr></thead>
              <tbody>{vigentes.map((b) => {
                const p = pendiente(b.idTipoBloqueo);
                return (
                  <tr key={b.id}>
                    <td>{b.tipo}</td><td>{b.comentario}</td><td>{fmtFechaUtc(b.fechaBloqueo)}</td><td>{b.bloqueadoPor}</td>
                    <td>{puede && (
                      <button type="button" className="boton-enlace" disabled={!!p} title={p ? `Ya existe la gestión ${p.numero} pendiente` : "Solicitar el desbloqueo"}
                        onClick={() => abrirDesbloqueo(b)}>Desbloquear</button>
                    )}</td>
                  </tr>
                );
              })}</tbody>
            </table>
          )}
        </div>
      </section>

      {modal && (
        <VentanaModal
          titulo={resultado ? `${resultado.accion} solicitado` : modal === "bloquear" ? `Nuevo bloqueo · ${datos.nombre}` : `Desbloquear: ${levantando?.tipo ?? ""}`}
          onCerrar={cerrarModal}
          acciones={resultado ? (
            <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          ) : (
            <button type="button" disabled={enviando || (modal === "bloquear" && !!pendienteElegido)} onClick={() => void enviar(modal === "bloquear")}>
              {enviando ? "Solicitando…" : modal === "bloquear" ? "Solicitar bloqueo" : "Solicitar desbloqueo"}
            </button>
          )}
        >
          {resultado ? (
            <div className="wiz__aviso">
              Se creó la gestión <strong>{resultado.gestion.numero ?? `#${resultado.gestion.idGestion}`}</strong>. El cliente no cambia hasta que se apruebe
              {resultado.gestion.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
            </div>
          ) : modal === "bloquear" ? (
            <>
              <div className="wiz__rejilla">
                <Campo etiqueta="Tipo de bloqueo *">
                  <select value={idTipo} onChange={(e) => setIdTipo(Number(e.target.value))}>
                    <option value={0}>Seleccione...</option>
                    {tiposLibres.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
                  </select>
                </Campo>
              </div>
              <Campo etiqueta="Comentario *">
                <textarea rows={3} style={{ width: "100%" }} maxLength={500} value={comentario} placeholder="Explique el motivo (mínimo 10 caracteres)" onChange={(e) => setComentario(e.target.value)} />
              </Campo>
              {pendienteElegido && <p className="mensaje-error">Ya existe la gestión {pendienteElegido.numero} pendiente de aprobación para este tipo.</p>}
              {errorForm && <p className="mensaje-error">{errorForm}</p>}
              {error && <p className="mensaje-error">{error}</p>}
            </>
          ) : (
            <>
              <Campo etiqueta="Comentario del desbloqueo *">
                <textarea rows={3} style={{ width: "100%" }} maxLength={500} value={comentarioLev} onChange={(e) => setComentarioLev(e.target.value)} />
              </Campo>
              {errorForm && <p className="mensaje-error">{errorForm}</p>}
              {error && <p className="mensaje-error">{error}</p>}
            </>
          )}
        </VentanaModal>
      )}

      <section className="wiz__tarjeta">
        <div className="wiz__cabecera"><h2>Historial</h2></div>
        <div className="wiz__cuerpo">
          {historial.length === 0 ? <p className="nota">Sin bloqueos levantados.</p> : (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Tipo</th><th>Bloqueo</th><th>Desbloqueo</th></tr></thead>
              <tbody>{historial.map((b) => (
                <tr key={b.id}>
                  <td>{b.tipo}</td>
                  <td>{fmtFechaUtc(b.fechaBloqueo)} · {b.bloqueadoPor}<br /><span className="nota">{b.comentario}</span></td>
                  <td>{b.fechaLevantamiento ? fmtFechaUtc(b.fechaLevantamiento) : ""} · {b.levantadoPor}<br /><span className="nota">{b.comentarioLevantamiento}</span></td>
                </tr>))}</tbody>
            </table>
          )}
          <p className="nota" style={{ marginTop: "0.5rem" }}><Link to={`/interlocutores/${idInt}`}>Volver a la ficha del cliente</Link></p>
        </div>
      </section>
    </div>
  );
}
