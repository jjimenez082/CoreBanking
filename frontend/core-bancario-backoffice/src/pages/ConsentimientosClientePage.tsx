import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError, fmtFechaUtc } from "../api/mensajes";
import { VentanaModal } from "../components/VentanaModal";
import type { CatalogoItemResponse, ConsentimientosCliente, GestionDetalleResponse } from "../api/tipos";

function Campo({ etiqueta, children }: { etiqueta: string; children: ReactNode }) {
  return (<label className="wiz-campo"><span>{etiqueta}</span>{children}</label>);
}

/**
 * Consentimientos de un cliente (ruta /interlocutores/:id/consentimientos): estado actual por tipo, historial y solicitud de
 * otorgamiento o revocación. Todo cambio se pide por gestión con aprobación; una sola gestión pendiente por cliente.
 */
export function ConsentimientosClientePage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const idInt = Number(id);
  const { tienePermiso } = usePermisos();

  const [datos, setDatos] = useState<ConsentimientosCliente | null>(null);
  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [canales, setCanales] = useState<CatalogoItemResponse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [cambios, setCambios] = useState<Set<number>>(new Set());
  const [idCanal, setIdCanal] = useState(0);
  const [comentario, setComentario] = useState("");
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<GestionDetalleResponse | null>(null);
  const [abierta, setAbierta] = useState(false);

  const cargar = useCallback(async () => {
    try {
      const [d, t, c] = await Promise.all([
        api.get<ConsentimientosCliente>(`/api/interlocutores/${idInt}/consentimientos`),
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_CONSENTIMIENTO"),
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/CANAL_CONSENTIMIENTO"),
      ]);
      setDatos(d.data); setTipos(t.data); setCanales(c.data);
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los consentimientos del cliente.")); }
  }, [api, idInt]);
  useEffect(() => { void cargar(); }, [cargar]);

  function abrir() { setCambios(new Set()); setIdCanal(0); setComentario(""); setErrorForm(null); setError(null); setResultado(null); setAbierta(true); }
  function cerrar() { setAbierta(false); if (resultado) { setResultado(null); void cargar(); } }

  async function enviar() {
    if (!datos) return;
    if (cambios.size === 0) { setErrorForm("Marque al menos un consentimiento a otorgar o revocar."); return; }
    if (!idCanal) { setErrorForm("Indique el canal por el que se recibió el consentimiento."); return; }
    setErrorForm(null); setEnviando(true); setError(null);
    try {
      const vigentes = new Set(datos.consentimientos.filter((c) => c.estado === "Vigente").map((c) => c.idTipoConsentimiento));
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInt}/consentimientos`, {
        cambios: Array.from(cambios).map((t) => ({ idTipoConsentimiento: t, otorgar: !vigentes.has(t) })),
        idCanal, comentario: comentario.trim() || null,
      });
      setResultado(r.data);
    } catch (e) { setError(mensajeError(e, "No se pudo solicitar el cambio de consentimientos.")); }
    finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_CONSENTIMIENTO_VER")) return <p className="mensaje-error">No tiene permiso para consultar los consentimientos de clientes.</p>;
  if (!datos) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;

  const puede = tienePermiso("CLI_CONSENTIMIENTO") && datos.estado !== "Fusionado" && !datos.pendiente && (datos.tipoPersona === "Fisica" || datos.tipoPersona === "Juridica");
  const vigente = (idTipo: number) => datos.consentimientos.find((c) => c.idTipoConsentimiento === idTipo && c.estado === "Vigente");
  const historial = datos.consentimientos.filter((c) => c.estado !== "Vigente");
  const alternar = (idTipo: number) => setCambios((prev) => { const s = new Set(prev); if (s.has(idTipo)) s.delete(idTipo); else s.add(idTipo); return s; });

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Consentimientos · N.º {datos.numero}</h1>
          <div className="nota">{datos.nombre} · Todo cambio se solicita por gestión y requiere aprobación.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Volver al cliente</button>
      </div>
      {error && <p className="mensaje-error">{error}</p>}
      {datos.estado === "Fusionado" && <div className="wiz__alerta">El cliente fue fusionado en otro; los consentimientos se gestionan sobre el cliente que se conservó.</div>}
      {datos.pendiente && (
        <div className="wiz__aviso">Hay una solicitud pendiente de aprobación: <strong>{datos.pendiente.numero}</strong> (solicitada por {datos.pendiente.solicitadoPor}). Debe resolverse antes de pedir otro cambio.</div>
      )}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cabecera">
          <h2>Consentimientos</h2>
          {puede && <button type="button" className="boton-enlace" onClick={abrir}>Modificar consentimientos</button>}
        </div>
        <div className="wiz__cuerpo">
          <table className="tabla tabla--compacta">
            <thead><tr><th>Consentimiento</th><th>Estado</th><th>Desde</th><th>Canal</th></tr></thead>
            <tbody>{tipos.map((t) => {
              const v = vigente(t.id);
              return (
                <tr key={t.id}>
                  <td><strong>{t.nombre}</strong>{t.descripcion && <span className="cons-descripcion">{t.descripcion}</span>}</td>
                  <td>{v ? "Otorgado" : "Sin consentimiento"}</td>
                  <td>{v ? fmtFechaUtc(v.fechaOtorgamiento) : "—"}</td>
                  <td>{v ? v.canal : "—"}</td>
                </tr>
              );
            })}</tbody>
          </table>
        </div>
      </section>

      {abierta && (
        <VentanaModal
          titulo={resultado ? "Cambio de consentimientos solicitado" : `Modificar consentimientos · ${datos.nombre}`}
          onCerrar={cerrar}
          acciones={resultado ? (
            <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          ) : (
            <button type="button" disabled={enviando} onClick={() => void enviar()}>{enviando ? "Solicitando…" : "Solicitar cambio"}</button>
          )}
        >
          {resultado ? (
            <div className="wiz__aviso">
              Se creó la gestión <strong>{resultado.numero ?? `#${resultado.idGestion}`}</strong>. Los consentimientos no cambian hasta que se apruebe
              {resultado.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
            </div>
          ) : (
            <>
              <p className="nota">Marque los consentimientos que el cliente otorga (hoy sin consentimiento) o revoca (hoy otorgados). Se enviará una gestión de aprobación.</p>
              <div className="cons-lista">
                {tipos.map((t) => {
                  const v = vigente(t.id);
                  return (
                    <label key={t.id}>
                      <input type="checkbox" checked={cambios.has(t.id)} onChange={() => alternar(t.id)} />
                      <span>
                        <strong>{t.nombre}</strong>{" "}
                        <span className="nota">· Hoy: {v ? "otorgado (al marcar, se revocará)" : "sin consentimiento (al marcar, se otorgará)"}</span>
                        {t.descripcion && <span className="cons-descripcion">{t.descripcion}</span>}
                      </span>
                    </label>
                  );
                })}
              </div>
              <div className="wiz__rejilla">
                <Campo etiqueta="Canal *">
                  <select value={idCanal} onChange={(e) => setIdCanal(Number(e.target.value))}>
                    <option value={0}>Seleccione...</option>
                    {canales.map((c) => <option key={c.id} value={c.id}>{c.nombre}</option>)}
                  </select>
                </Campo>
              </div>
              <Campo etiqueta="Comentario">
                <textarea rows={2} style={{ width: "100%" }} maxLength={500} value={comentario} onChange={(e) => setComentario(e.target.value)} />
              </Campo>
              {errorForm && <p className="mensaje-error">{errorForm}</p>}
              {error && <p className="mensaje-error">{error}</p>}
            </>
          )}
        </VentanaModal>
      )}

      <section className="wiz__tarjeta">
        <div className="wiz__cabecera"><h2>Historial de revocados</h2></div>
        <div className="wiz__cuerpo">
          {historial.length === 0 ? <p className="nota">Sin consentimientos revocados.</p> : (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Consentimiento</th><th>Otorgado</th><th>Revocado</th></tr></thead>
              <tbody>{historial.map((c) => (
                <tr key={c.id}>
                  <td>{c.tipo}</td>
                  <td>{fmtFechaUtc(c.fechaOtorgamiento)} · {c.otorgadoPor} · {c.canal}{c.comentario && <><br /><span className="nota">{c.comentario}</span></>}</td>
                  <td>{c.fechaRevocacion ? fmtFechaUtc(c.fechaRevocacion) : ""} · {c.revocadoPor} · {c.canalRevocacion}{c.comentarioRevocacion && <><br /><span className="nota">{c.comentarioRevocacion}</span></>}</td>
                </tr>))}</tbody>
            </table>
          )}
          <p className="nota" style={{ marginTop: "0.5rem" }}><Link to={`/interlocutores/${idInt}`}>Volver a la ficha del cliente</Link></p>
        </div>
      </section>
    </div>
  );
}
