import { useEffect, useState, type ReactNode } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import type { CatalogoItemResponse, GestionDetalleResponse, InterlocutorDetalleResponse } from "../api/tipos";

function Campo({ etiqueta, children, obligatorio }: { etiqueta: string; children: ReactNode; obligatorio?: boolean }) {
  return (
    <label className="wiz-campo">
      <span>{etiqueta}{obligatorio && " *"}</span>
      {children}
    </label>
  );
}

/**
 * Modificar o disolver un Grupo (rutas /interlocutores/:id/grupo/editar y /interlocutores/:id/grupo/disolver).
 * Ambos cambios se solicitan por gestión. Los miembros no se editan aquí: se gestionan en la pestaña Relaciones.
 * Al disolver se cierran las membresías vigentes y el grupo queda «Disuelto» (no se borra: conserva su historial).
 */
export function GrupoEditarPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const idInt = Number(id);
  const disolver = useLocation().pathname.endsWith("/disolver");
  const { tienePermiso } = usePermisos();

  const [det, setDet] = useState<InterlocutorDetalleResponse | null>(null);
  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [nombre, setNombre] = useState("");
  const [idTipo, setIdTipo] = useState(0);
  const [descripcion, setDescripcion] = useState("");
  const [formacion, setFormacion] = useState("");
  const [motivo, setMotivo] = useState("");
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<GestionDetalleResponse | null>(null);

  useEffect(() => {
    let cancelado = false;
    Promise.all([
      api.get<InterlocutorDetalleResponse>(`/api/interlocutores/${idInt}`),
      api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_GRUPO"),
    ]).then(([d, t]) => {
      if (cancelado) return;
      setDet(d.data); setTipos(t.data);
      const g = d.data.grupo;
      if (g) { setNombre(g.nombre); setIdTipo(g.idTipoGrupo ?? 0); setDescripcion(g.descripcion ?? ""); setFormacion(g.fechaFormacion?.slice(0, 10) ?? ""); }
    }).catch((e) => !cancelado && setError(mensajeError(e, "No se pudo cargar el grupo.")));
    return () => { cancelado = true; };
  }, [api, idInt]);

  async function enviar() {
    let e: string | null = null;
    if (disolver) { if (motivo.trim().length < 10) e = "Indique el motivo de la disolución (mínimo 10 caracteres)."; }
    else if (!nombre.trim()) e = "El nombre del grupo es obligatorio.";
    else if (!idTipo) e = "Elija el tipo de grupo.";
    else if (motivo.trim().length > 0 && motivo.trim().length < 10) e = "El comentario, si lo indica, debe tener al menos 10 caracteres.";
    setErrorForm(e);
    if (e) return;
    setEnviando(true); setError(null);
    try {
      const r = disolver
        ? await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInt}/grupo/disolucion`, { motivo: motivo.trim() })
        : await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInt}/grupo/modificacion`, {
            nombre: nombre.trim(), idTipoGrupo: idTipo, descripcion: descripcion.trim() || null, fechaFormacion: formacion || null, comentario: motivo.trim() || null,
          });
      setResultado(r.data);
    } catch (err) {
      setError(mensajeError(err, "No se pudo solicitar el cambio del grupo."));
    } finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_EDICION")) return <p className="mensaje-error">No tiene permiso para modificar interlocutores.</p>;
  if (!det) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;
  if (det.tipoPersona !== "Grupo") return <p className="mensaje-error">El interlocutor no es un grupo. <Link to={`/interlocutores/${idInt}`}>Volver</Link></p>;

  const bloqueado = det.estado !== "Activo" || !!det.idGestionPendiente;

  if (resultado) {
    return (
      <div className="wiz">
        <div className="wiz__titulo"><div><h1>{disolver ? "Disolución solicitada" : "Modificación solicitada"}</h1></div></div>
        <div className="wiz__aviso">
          Se creó la gestión <strong>{resultado.numero ?? `#${resultado.idGestion}`}</strong>. El grupo no cambia hasta que se apruebe
          {resultado.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
        </div>
        <div style={{ display: "flex", gap: "0.5rem", marginTop: "1rem" }}>
          <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Volver al grupo</button>
        </div>
      </div>
    );
  }

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>{disolver ? "Disolver grupo" : "Modificar grupo"} · N.º {det.numero}</h1>
          <div className="nota">Todo cambio se solicita por gestión y requiere aprobación.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInt}`)}>Cancelar</button>
      </div>

      {det.estado !== "Activo" && <div className="wiz__alerta">El grupo está «{det.estado}» y no admite cambios.</div>}
      {det.idGestionPendiente && <div className="wiz__alerta">Tiene la gestión #{det.idGestionPendiente} pendiente de aprobación; debe resolverse antes.</div>}
      {error && <p className="mensaje-error">{error}</p>}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          {disolver ? (
            <>
              <p style={{ marginTop: 0 }}>
                Al aprobarse, las <strong>{det.grupo?.cantidadMiembros ?? 0} membresías vigentes</strong> de «{det.grupo?.nombre}» se cierran con la fecha de hoy
                y el grupo queda <strong>Disuelto</strong>. Los miembros no se modifican; el grupo y su historial se conservan para consulta.
              </p>
              <Campo etiqueta="Motivo de la disolución" obligatorio>
                <textarea rows={3} style={{ width: "100%", maxWidth: 640 }} maxLength={500} value={motivo} disabled={bloqueado}
                  placeholder="Explique el motivo (mínimo 10 caracteres)" onChange={(ev) => setMotivo(ev.target.value)} />
              </Campo>
            </>
          ) : (
            <>
              <div className="wiz__rejilla">
                <Campo etiqueta="Nombre del grupo" obligatorio><input type="text" maxLength={200} value={nombre} disabled={bloqueado} onChange={(ev) => setNombre(ev.target.value)} /></Campo>
                <Campo etiqueta="Tipo de grupo" obligatorio>
                  <select value={idTipo} disabled={bloqueado} onChange={(ev) => setIdTipo(Number(ev.target.value))}>
                    <option value={0}>Seleccione...</option>
                    {tipos.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
                  </select>
                </Campo>
                <Campo etiqueta="Fecha de formación"><input type="date" value={formacion} disabled={bloqueado} onChange={(ev) => setFormacion(ev.target.value)} /></Campo>
              </div>
              <Campo etiqueta="Descripción">
                <textarea rows={3} style={{ width: "100%", maxWidth: 640 }} maxLength={500} value={descripcion} disabled={bloqueado} onChange={(ev) => setDescripcion(ev.target.value)} />
              </Campo>
              <p className="nota">Los miembros se agregan o quitan en la pestaña Relaciones del grupo.</p>
              <Campo etiqueta="Comentario (opcional)">
                <input type="text" style={{ width: "100%", maxWidth: 640 }} maxLength={500} value={motivo} disabled={bloqueado} onChange={(ev) => setMotivo(ev.target.value)} />
              </Campo>
            </>
          )}
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem" }}>
            <button type="button" disabled={bloqueado || enviando} onClick={enviar}>
              {enviando ? "Solicitando…" : disolver ? "Solicitar disolución" : "Solicitar modificación"}
            </button>
            <Link to={`/interlocutores/${idInt}`} className="boton-secundario" style={{ padding: "0.45rem 0.9rem" }}>Cancelar</Link>
          </div>
        </div>
      </section>
    </div>
  );
}
