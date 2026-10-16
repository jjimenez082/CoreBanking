import { useMemo, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import { mensajeError, fmtFecha, fmtFechaUtc } from "../api/mensajes";
import { VentanaModal } from "../components/VentanaModal";
import { Campo } from "../components/CamposWizard";
import type { ClienteDocumento, DocumentoApi, DocumentoVersion, TipoDocumento } from "../api/tipos";
import { useEffect } from "react";
import { descargarDocumento, etiquetaVigencia, fmtTamano, mensajeSolicitud, quitarTemporales, solicitarDocumentos, subirTemporal } from "./documentos";

export interface ClienteElegido { id: number; numero: string; nombre: string; tipoPersona?: string }

/** Selector de cliente con busqueda (nombre, numero o identificacion). */
export function SelectorCliente({ valor, onElegir }: { valor: ClienteElegido | null; onElegir: (c: ClienteElegido | null) => void }) {
  const api = useApiClient();
  const [texto, setTexto] = useState("");
  const [sugeridos, setSugeridos] = useState<ClienteDocumento[]>([]);
  useEffect(() => {
    if (valor || texto.trim().length < 2) { setSugeridos([]); return; }
    const t = setTimeout(() => {
      void (async () => {
        try { setSugeridos((await api.get<ClienteDocumento[]>("/api/documentos/clientes", { params: { texto: texto.trim() } })).data); }
        catch { setSugeridos([]); }
      })();
    }, 300);
    return () => clearTimeout(t);
  }, [api, texto, valor]);

  if (valor)
    return (
      <div className="docs-cliente-elegido">
        <strong>{valor.numero}</strong> <span>{valor.nombre}</span>
        <button type="button" className="boton-secundario" onClick={() => { onElegir(null); setTexto(""); }}>Cambiar</button>
      </div>
    );
  return (
    <div>
      <input type="search" value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Escriba al menos 2 caracteres" />
      {sugeridos.length > 0 && (
        <ul className="docs-cliente-lista">
          {sugeridos.map((c) => (
            <li key={c.id}><button type="button" onClick={() => onElegir({ id: c.id, numero: c.numero, nombre: c.nombre, tipoPersona: c.tipoPersona })}>{c.numero} · {c.nombre} <span className="nota">({c.tipoPersona})</span></button></li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Reemplazar la version vigente de un documento: sube el archivo y pide la gestion (se aplica sola o queda pendiente segun la regla).
 * Las cargas nuevas, de uno o varios archivos, van por el asistente «Cargar documentos».
 */
export function ReemplazarModal({ documento, idInterlocutor, tipos, onCerrar, onHecho }: {
  documento: DocumentoApi; idInterlocutor: number; tipos: TipoDocumento[]; onCerrar: () => void; onHecho: (mensaje: string) => void;
}) {
  const api = useApiClient();
  const [archivo, setArchivo] = useState<File | null>(null);
  const [motivo, setMotivo] = useState("");
  const [emision, setEmision] = useState("");
  const [vencimiento, setVencimiento] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const tipo = useMemo(() => tipos.find((t) => t.id === documento.idTipoDocumento), [tipos, documento.idTipoDocumento]);
  const pideVence = tipo ? tipo.vencimiento !== "NoAplica" : false;

  async function enviar() {
    setError(null);
    if (!archivo) return setError("Seleccione un archivo.");
    if (!motivo.trim()) return setError("Indique el motivo del reemplazo.");
    if (tipo?.vencimiento === "Obligatorio" && !vencimiento) return setError(`El tipo «${tipo.nombre}» exige la fecha de vencimiento.`);
    if (emision && vencimiento && vencimiento < emision) return setError("La fecha de vencimiento no puede ser anterior a la de emisión.");
    setEnviando(true);
    let idTemporal: number | null = null;
    try {
      const doc = await subirTemporal(api, archivo, documento.idTipoDocumento, pideVence ? { emision, vencimiento } : {});
      idTemporal = doc.idDocumento;
      const r = await solicitarDocumentos(api, idInterlocutor, [{ tipo: "Reemplazo", idDocumento: idTemporal, idDocumentoDestino: documento.idDocumento, motivo: motivo.trim() }], [idTemporal]);
      onHecho(mensajeSolicitud(r));
    } catch (err) {
      if (idTemporal !== null) await quitarTemporales(api, [idTemporal]);
      setError(mensajeError(err, "No se pudo registrar el reemplazo."));
    } finally { setEnviando(false); }
  }

  return (
    <VentanaModal titulo={`Reemplazar · ${documento.tipoNombre}`} onCerrar={onCerrar} ancho={560}
      acciones={<button type="button" disabled={enviando} onClick={() => void enviar()}>{enviando ? "Enviando…" : "Reemplazar"}</button>}>
      {error && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>}
      <p className="nota">Versión actual: v{documento.version} · {documento.nombreArchivo}. Al aprobarse el reemplazo, esa versión queda en el historial y la nueva pasa a ser la vigente.</p>
      <div className="wiz__grupo">
        <Campo etiqueta="Archivo nuevo" obligatorio>
          <input type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(e) => setArchivo(e.target.files?.[0] ?? null)} />
        </Campo>
        {pideVence && (
          <>
            <Campo etiqueta="Fecha de emisión"><input type="date" value={emision} onChange={(e) => setEmision(e.target.value)} /></Campo>
            <Campo etiqueta="Fecha de vencimiento" obligatorio={tipo?.vencimiento === "Obligatorio"}>
              <input type="date" value={vencimiento} onChange={(e) => setVencimiento(e.target.value)} />
            </Campo>
          </>
        )}
        <Campo etiqueta="Motivo del reemplazo" obligatorio><input type="text" maxLength={300} value={motivo} onChange={(e) => setMotivo(e.target.value)} /></Campo>
      </div>
    </VentanaModal>
  );
}

export function ObsoletarModal({ documento, idInterlocutor, onCerrar, onHecho }: { documento: DocumentoApi; idInterlocutor: number; onCerrar: () => void; onHecho: (mensaje: string) => void }) {
  const api = useApiClient();
  const [motivo, setMotivo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar() {
    setError(null);
    if (!motivo.trim()) return setError("Indique el motivo.");
    setEnviando(true);
    try {
      const r = await solicitarDocumentos(api, idInterlocutor, [{ tipo: "Obsoleto", idDocumentoDestino: documento.idDocumento, motivo: motivo.trim() }], []);
      onHecho(mensajeSolicitud(r));
    } catch (err) {
      setError(mensajeError(err, "No se pudo registrar la solicitud."));
    } finally { setEnviando(false); }
  }

  return (
    <VentanaModal titulo={`Obsoletar · ${documento.tipoNombre}`} onCerrar={onCerrar} ancho={520}
      acciones={<button type="button" disabled={enviando} onClick={() => void enviar()}>{enviando ? "Enviando…" : "Solicitar obsolescencia"}</button>}>
      {error && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>}
      <p className="nota">«{documento.nombreArchivo}» dejará de estar vigente cuando se apruebe. No se borra: queda visible como obsoleto y con todo su historial.</p>
      <Campo etiqueta="Motivo" obligatorio><input type="text" maxLength={300} value={motivo} onChange={(e) => setMotivo(e.target.value)} /></Campo>
    </VentanaModal>
  );
}

/** Ficha del documento: datos de la version vigente e historial de versiones con descarga. */
export function VersionesModal({ documento, cliente, onCerrar }: { documento: DocumentoApi; cliente?: string; onCerrar: () => void }) {
  const api = useApiClient();
  const [versiones, setVersiones] = useState<DocumentoVersion[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let vivo = true;
    void (async () => {
      try {
        const r = await api.get<DocumentoVersion[]>(`/api/documentos/${documento.idDocumento}/versiones`);
        if (vivo) setVersiones(r.data);
      } catch (err) {
        if (vivo) setError(mensajeError(err, "No se pudo cargar el historial."));
      }
    })();
    return () => { vivo = false; };
  }, [api, documento.idDocumento]);

  return (
    <VentanaModal titulo={`${documento.tipoNombre}${cliente ? ` · ${cliente}` : ""}`} onCerrar={onCerrar} ancho={780}>
      {error && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>}
      <dl className="docs-ficha">
        <dt>Estado</dt><dd>{(() => { const c = etiquetaVigencia(documento); return c ? <span className={c.clase}>{c.texto}</span> : documento.estado; })()}</dd>
        <dt>Origen</dt><dd>{documento.origen === "Gestion" ? `Evidencia de la gestión ${documento.numeroGestion ?? ""}` : "Expediente del cliente"}</dd>
        <dt>Descripción</dt><dd>{documento.descripcion || "—"}</dd>
        <dt>Palabras clave</dt><dd>{documento.claves.length > 0 ? documento.claves.join(", ") : "—"}</dd>
        <dt>Emisión / vencimiento</dt><dd>{documento.fechaEmision ? fmtFecha(documento.fechaEmision) : "—"} / {documento.fechaVencimiento ? fmtFecha(documento.fechaVencimiento) : "—"}</dd>
        {documento.motivoObsoleto && (<><dt>Motivo de obsolescencia</dt><dd>{documento.motivoObsoleto}</dd></>)}
      </dl>
      <h4>Versiones</h4>
      {!versiones && !error && <p className="nota">Cargando…</p>}
      {versiones && (
        <table className="tabla tabla--compacta">
          <thead><tr><th>Versión</th><th>Archivo</th><th>Subido</th><th>Vence</th><th>Motivo</th><th>Gestión</th></tr></thead>
          <tbody>
            {versiones.map((v) => (
              <tr key={v.idVersion}>
                <td>v{v.numero}{v.esVigente && <span className="docs-chip">Vigente</span>}</td>
                <td>
                  <button type="button" className="wiz-enlace" onClick={() => void descargarDocumento(api, documento, v.idVersion).catch(() => setError("No se pudo descargar el archivo."))}>{v.nombreArchivo}</button>
                  <span className="nota"> · {fmtTamano(v.tamano)}</span>
                </td>
                <td>{fmtFechaUtc(v.subidoEn)} · {v.subidoPor}</td>
                <td>{v.fechaVencimiento ? fmtFecha(v.fechaVencimiento) : "—"}</td>
                <td>{v.motivoReemplazo ?? "—"}</td>
                <td>{v.numeroGestion ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </VentanaModal>
  );
}
