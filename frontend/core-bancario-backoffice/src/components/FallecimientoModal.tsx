import { useEffect, useState } from "react";
import { VentanaModal } from "./VentanaModal";
import { Campo } from "./CamposWizard";
import { useApiClient } from "../api/useApiClient";
import { mensajeError } from "../api/mensajes";
import type { GestionDetalleResponse, TipoDocumento } from "../api/tipos";
import { encabezadoAdjuntos, quitarTemporales, subirTemporal } from "../documentos/documentos";

const hoyIso = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };

/**
 * Registrar el fallecimiento de una persona. Es una gestión con aprobación y es irreversible al aprobarse; el acta de defunción
 * es el respaldo obligatorio y viaja como adjunto de la gestión.
 */
export function FallecimientoModal({ idInterlocutor, nombre, nacimiento, onCerrar, onHecho }: {
  idInterlocutor: number; nombre: string; nacimiento?: string | null; onCerrar: () => void; onHecho: (mensaje: string) => void;
}) {
  const api = useApiClient();
  const [idTipoActa, setIdTipoActa] = useState<number | null>(null);
  const [sinTipo, setSinTipo] = useState(false);
  const [fecha, setFecha] = useState("");
  const [observacion, setObservacion] = useState("");
  const [archivo, setArchivo] = useState<File | null>(null);
  const [confirmo, setConfirmo] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    let vivo = true;
    void api.get<TipoDocumento[]>("/api/documentos/tipos", { params: { tipoPersona: "Fisica" } })
      .then((r) => {
        if (!vivo) return;
        const t = r.data.find((x) => x.codigo === "ACTA_DEFUNCION");
        if (t) setIdTipoActa(t.id); else setSinTipo(true);
      }).catch(() => { if (vivo) setSinTipo(true); });
    return () => { vivo = false; };
  }, [api]);

  async function enviar() {
    setError(null);
    if (!fecha) return setError("Indique la fecha de fallecimiento.");
    if (fecha > hoyIso()) return setError("La fecha de fallecimiento no puede ser futura.");
    if (nacimiento && fecha < nacimiento.slice(0, 10)) return setError("La fecha de fallecimiento no puede ser anterior a la fecha de nacimiento.");
    if (!archivo) return setError("Adjunte el acta de defunción.");
    if (idTipoActa === null) return setError("No está disponible el tipo de documento «Acta de defunción».");
    if (!confirmo) return setError("Confirme que entiende que el registro es irreversible.");
    setEnviando(true);
    let idTemporal: number | null = null;
    try {
      const doc = await subirTemporal(api, archivo, idTipoActa);
      idTemporal = doc.idDocumento;
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${idInterlocutor}/fallecimiento`,
        { fechaFallecimiento: fecha, comentario: observacion.trim() || null }, encabezadoAdjuntos([idTemporal]));
      const numero = r.data.numero ?? r.data.idGestion;
      onHecho(r.data.estado === "Aplicada"
        ? `Fallecimiento registrado (gestión ${numero}).`
        : `Solicitud de fallecimiento enviada a aprobación (gestión ${numero}). El cliente cambia a Fallecido cuando se apruebe.`);
    } catch (err) {
      if (idTemporal !== null) await quitarTemporales(api, [idTemporal]);
      setError(mensajeError(err, "No se pudo registrar la solicitud de fallecimiento."));
    } finally { setEnviando(false); }
  }

  return (
    <VentanaModal titulo="Registrar fallecimiento" onCerrar={onCerrar} ancho={560}
      acciones={<button type="button" className="boton-peligro" disabled={enviando || sinTipo} onClick={() => void enviar()}>{enviando ? "Enviando…" : "Solicitar fallecimiento"}</button>}>
      {error && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>}
      {sinTipo && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">El tipo de documento «Acta de defunción» no está activo; actívelo en Carpetas y tipos de documento.</div>}
      <p className="nota">
        Cliente: <strong>{nombre}</strong>. La solicitud pasa por aprobación. Al aprobarse el cliente queda como <strong>Fallecido</strong>, se cierran sus membresías de grupo
        y ya no admite cambios, roles, relaciones, bloqueos, consentimientos ni fusión. Sus documentos siguen disponibles. <strong>No se puede revertir.</strong>
      </p>
      <div className="wiz__grupo">
        <Campo etiqueta="Fecha de fallecimiento" obligatorio><input type="date" value={fecha} max={hoyIso()} min={nacimiento?.slice(0, 10)} onChange={(e) => setFecha(e.target.value)} /></Campo>
        <Campo etiqueta="Acta de defunción" obligatorio><input type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(e) => setArchivo(e.target.files?.[0] ?? null)} /></Campo>
        <Campo etiqueta="Observación"><input type="text" maxLength={500} value={observacion} onChange={(e) => setObservacion(e.target.value)} /></Campo>
        <label className="docs-check"><input type="checkbox" checked={confirmo} onChange={(e) => setConfirmo(e.target.checked)} /> Entiendo que el registro del fallecimiento es irreversible.</label>
      </div>
    </VentanaModal>
  );
}
