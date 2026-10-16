import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import { TablaDatos, type ColumnaTabla } from "./TablaDatos";
import type { GestionDetalleResponse, RelacionDetalleResponse, RelacionesInterlocutorResponse } from "../api/tipos";

const ICONO: Record<string, string> = { Fisica: "👤", Juridica: "🏢", Grupo: "🔗" };
const fmtF = (iso?: string | null) => (iso ? new Date(iso).toLocaleDateString("es-CR") : "—");
const pct = (n?: number | null) => (n == null ? "—" : `${n}%`);

/**
 * Pestaña «Relaciones» del 360: lista con checkbox + barra de comandos
 * (Nueva relación / Editar relación / Inactivar relación). Todo cambio se solicita por gestión.
 */
export function RelacionesTab({ idInterlocutor, activo }: { idInterlocutor: number; activo: boolean }) {
  const api = useApiClient();
  const navigate = useNavigate();
  const { tienePermiso } = usePermisos();
  const [datos, setDatos] = useState<RelacionesInterlocutorResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());
  const [inactivando, setInactivando] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [verHistorial, setVerHistorial] = useState(false);

  const cargar = useCallback(async () => {
    try {
      const r = await api.get<RelacionesInterlocutorResponse>(`/api/interlocutores/${idInterlocutor}/relaciones`);
      setDatos(r.data); setError(null);
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar las relaciones.")); }
  }, [api, idInterlocutor]);

  useEffect(() => { void cargar(); }, [cargar]);

  const puede = tienePermiso("CLI_RELACION") && activo && !!datos?.puedeSolicitar;
  const motivoNo = !tienePermiso("CLI_RELACION") ? "" : !activo ? "El interlocutor no está activo" :
    datos?.numeroGestionPendiente ? `Ya tiene la gestión ${datos.numeroGestionPendiente} pendiente` : "";

  const columnas: ColumnaTabla<RelacionDetalleResponse>[] = [
    { clave: "otro", encabezado: "Interlocutor", textoBusqueda: (r) => `${r.nombreOtro} ${r.numeroOtro}`,
      render: (r) => <span>{ICONO[r.tipoOtro] ?? ""} <Link to={`/interlocutores/${r.idOtro}`}>{r.nombreOtro}</Link></span> },
    { clave: "num", encabezado: "N.º", textoBusqueda: (r) => r.numeroOtro, render: (r) => r.numeroOtro },
    { clave: "rel", encabezado: "Relación", textoBusqueda: (r) => r.etiqueta, filtroLista: true, render: (r) => r.etiqueta },
    { clave: "part", encabezado: "% part.", valorOrden: (r) => r.porcentajeParticipacion ?? -1, render: (r) => pct(r.porcentajeParticipacion) },
    { clave: "voto", encabezado: "% voto", valorOrden: (r) => r.porcentajeVoto ?? -1, render: (r) => pct(r.porcentajeVoto) },
    { clave: "ctrl", encabezado: "% control", valorOrden: (r) => r.porcentajeControl ?? -1, render: (r) => pct(r.porcentajeControl) },
    { clave: "ppal", encabezado: "Principal", render: (r) => (r.esPrincipal ? "Sí" : "—") },
    { clave: "func", encabezado: "Función", textoBusqueda: (r) => r.funcion ?? "", render: (r) => r.funcion ?? "—" },
    { clave: "desde", encabezado: "Desde", valorOrden: (r) => r.fechaInicio, render: (r) => fmtF(r.fechaInicio) },
  ];

  const elegida = datos?.vigentes.find((r) => seleccion.has(r.idRelacion));

  async function inactivar() {
    if (!elegida) return;
    if (motivo.trim().length < 10) { setError("Indique el motivo (mínimo 10 caracteres)."); return; }
    setEnviando(true); setError(null);
    try {
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInterlocutor}/relaciones`,
        { cerrar: [elegida.idRelacion], motivo: motivo.trim() });
      setAviso(`Se solicitó inactivar la relación (gestión ${r.data.numero ?? `#${r.data.idGestion}`}). Queda pendiente de aprobación.`);
      setInactivando(false); setMotivo(""); setSeleccion(new Set());
      await cargar();
    } catch (e) { setError(mensajeError(e, "No se pudo solicitar la inactivación.")); }
    finally { setEnviando(false); }
  }

  if (!datos) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;

  return (
    <div>
      {error && <p className="mensaje-error">{error}</p>}
      {aviso && <div className="wiz__ok">{aviso}</div>}
      {motivoNo && <p className="nota">{motivoNo}: no se pueden solicitar cambios de relaciones por ahora.</p>}
      <TablaDatos
        columnas={columnas}
        filas={datos.vigentes}
        claveFila={(r) => r.idRelacion}
        seleccion={seleccion}
        onCambiarSeleccion={(s) => { setSeleccion(s); setInactivando(false); setAviso(null); }}
        mensajeVacio="Sin relaciones vigentes."
        placeholderBusqueda="Buscar relación..."
        filtrosPorColumna
        comandos={[
          { clave: "nueva", etiqueta: "Nueva relación", icono: "➕", requiereSeleccion: "ninguna", oculto: !tienePermiso("CLI_RELACION"),
            deshabilitado: !puede, onClick: () => navigate(`/interlocutores/${idInterlocutor}/relaciones/nueva`) },
          { clave: "editar", etiqueta: "Editar relación", icono: "✏️", requiereSeleccion: "una", oculto: !tienePermiso("CLI_RELACION"),
            deshabilitado: !puede, onClick: () => elegida && navigate(`/interlocutores/${idInterlocutor}/relaciones/${elegida.idRelacion}/editar`) },
          { clave: "inactivar", etiqueta: "Inactivar relación", icono: "⛔", requiereSeleccion: "una", oculto: !tienePermiso("CLI_RELACION"),
            deshabilitado: !puede, onClick: () => { setInactivando(true); setError(null); } },
        ]}
      />
      {inactivando && elegida && (
        <div className="wiz__alerta" style={{ marginTop: 12 }}>
          <p>Se solicitará inactivar la relación «{elegida.etiqueta}» con <b>{elegida.nombreOtro}</b>. La relación queda en el historial.</p>
          <label className="wiz-campo">
            <span>Motivo (mín. 10 caracteres)</span>
            <textarea rows={2} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </label>
          <button type="button" className="btn btn--primario" disabled={enviando} onClick={inactivar}>
            {enviando ? "Solicitando…" : "Solicitar inactivación"}
          </button>{" "}
          <button type="button" className="btn" disabled={enviando} onClick={() => { setInactivando(false); setMotivo(""); }}>Cancelar</button>
        </div>
      )}
      {datos.historial.length > 0 && (
        <div style={{ marginTop: 16 }}>
          <button type="button" className="btn" onClick={() => setVerHistorial((v) => !v)}>
            {verHistorial ? "Ocultar" : "Ver"} historial ({datos.historial.length})
          </button>
          {verHistorial && (
            <table className="tabla tabla--compacta" style={{ marginTop: 8 }}>
              <thead><tr><th>Interlocutor</th><th>Relación</th><th>Desde</th><th>Hasta</th></tr></thead>
              <tbody>
                {datos.historial.map((r) => (
                  <tr key={r.idRelacion}>
                    <td>{ICONO[r.tipoOtro] ?? ""} {r.nombreOtro}</td><td>{r.etiqueta}</td><td>{fmtF(r.fechaInicio)}</td><td>{fmtF(r.fechaFin)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  );
}
