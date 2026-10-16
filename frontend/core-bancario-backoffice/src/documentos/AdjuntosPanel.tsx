import { useCallback, useEffect, useMemo, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import { mensajeError, fmtFecha } from "../api/mensajes";
import { VentanaModal } from "../components/VentanaModal";
import { Campo } from "../components/CamposWizard";
import type { DocumentoApi, TipoDocumento } from "../api/tipos";
import { descargarDocumento, etiquetaVigencia, fmtTamano } from "./documentos";

/**
 * Adjuntos de una gestion: el usuario sube los documentos que respaldan el cambio ANTES de enviarlo. Quedan como temporales
 * suyos y se ligan a la gestion cuando esta se crea (el formulario manda sus ids en el encabezado X-Adjuntos, ver encabezadoAdjuntos).
 * Un archivo subido y no enviado se puede quitar; ya enviado, forma parte de la evidencia y no se modifica.
 */
export function AdjuntosPanel({ tipoPersona, onCambio, ayuda }: {
  tipoPersona?: string; onCambio: (ids: number[]) => void; ayuda?: string;
}) {
  const api = useApiClient();
  const [tipos, setTipos] = useState<TipoDocumento[]>([]);
  const [docs, setDocs] = useState<DocumentoApi[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [abierto, setAbierto] = useState(false);

  useEffect(() => {
    let vivo = true;
    void (async () => {
      try {
        const [t, d] = await Promise.all([
          api.get<TipoDocumento[]>("/api/documentos/tipos", { params: { tipoPersona } }),
          api.get<DocumentoApi[]>("/api/documentos/temporales"),
        ]);
        if (!vivo) return;
        setTipos(t.data);
        setDocs(d.data);
      } catch (err) {
        if (vivo) setError(mensajeError(err, "No se pudieron cargar los adjuntos."));
      }
    })();
    return () => { vivo = false; };
  }, [api, tipoPersona]);

  useEffect(() => { onCambio(docs.map((d) => d.idDocumento)); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [docs]);

  async function quitar(d: DocumentoApi) {
    setError(null);
    try {
      await api.delete(`/api/documentos/temporales/${d.idDocumento}`);
      setDocs((l) => l.filter((x) => x.idDocumento !== d.idDocumento));
    } catch (err) { setError(mensajeError(err, "No se pudo quitar el archivo.")); }
  }

  async function bajar(d: DocumentoApi) {
    setError(null);
    try { await descargarDocumento(api, d); } catch { setError("No se pudo descargar el archivo."); }
  }

  return (
    <div className="docs-panel">
      <div className="docs-panel__cabecera">
        <div>
          <strong>Documentos de respaldo</strong>
          <div className="nota">{ayuda ?? "Adjunte los documentos que respaldan este cambio. Los verá quien apruebe la gestión; si se aprueba, quedan en el expediente del cliente (carpeta Gestiones)."}</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => setAbierto(true)}>Adjuntar documento</button>
      </div>
      {error && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>}
      {docs.length === 0 ? (
        <p className="nota">Sin documentos adjuntos.</p>
      ) : (
        <table className="tabla tabla--compacta">
          <thead><tr><th>Tipo</th><th>Archivo</th><th>Vence</th><th></th></tr></thead>
          <tbody>
            {docs.map((d) => {
              const v = etiquetaVigencia(d);
              return (
                <tr key={d.idDocumento}>
                  <td>{d.tipoNombre}{d.descripcion && <><br /><span className="nota">{d.descripcion}</span></>}</td>
                  <td><button type="button" className="wiz-enlace" onClick={() => void bajar(d)}>{d.nombreArchivo}</button> <span className="nota">· {fmtTamano(d.tamano)}</span></td>
                  <td>{d.fechaVencimiento ? fmtFecha(d.fechaVencimiento) : "—"} {v && <span className={v.clase}>{v.texto}</span>}</td>
                  <td><button type="button" className="wiz-enlace wiz-enlace--peligro" onClick={() => void quitar(d)}>Quitar</button></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      {abierto && (
        <SubirDocumentoModal tipos={tipos} onCerrar={() => setAbierto(false)} onSubido={(d) => { setDocs((l) => [...l, d]); setAbierto(false); }} />
      )}
    </div>
  );
}

function SubirDocumentoModal({ tipos, onCerrar, onSubido }: { tipos: TipoDocumento[]; onCerrar: () => void; onSubido: (d: DocumentoApi) => void }) {
  const api = useApiClient();
  const [archivo, setArchivo] = useState<File | null>(null);
  const [idTipo, setIdTipo] = useState(0);
  const [descripcion, setDescripcion] = useState("");
  const [claves, setClaves] = useState("");
  const [emision, setEmision] = useState("");
  const [vencimiento, setVencimiento] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [subiendo, setSubiendo] = useState(false);

  const tipo = useMemo(() => tipos.find((t) => t.id === idTipo), [tipos, idTipo]);
  const porCarpeta = useMemo(() => {
    const m = new Map<string, TipoDocumento[]>();
    for (const t of tipos) m.set(t.carpeta, [...(m.get(t.carpeta) ?? []), t]);
    return [...m.entries()];
  }, [tipos]);
  const pideVence = tipo ? tipo.vencimiento !== "NoAplica" : false;

  const enviar = useCallback(async () => {
    setError(null);
    if (!archivo) return setError("Seleccione un archivo.");
    if (!tipo) return setError("Seleccione el tipo de documento.");
    if (tipo.vencimiento === "Obligatorio" && !vencimiento) return setError(`El tipo «${tipo.nombre}» exige la fecha de vencimiento.`);
    if (emision && vencimiento && vencimiento < emision) return setError("La fecha de vencimiento no puede ser anterior a la de emisión.");
    const fd = new FormData();
    fd.append("archivo", archivo);
    fd.append("idTipoDocumento", String(tipo.id));
    fd.append("descripcion", descripcion);
    fd.append("claves", claves);
    if (pideVence) { fd.append("fechaEmision", emision); fd.append("fechaVencimiento", vencimiento); }
    setSubiendo(true);
    try {
      const r = await api.post<DocumentoApi>("/api/documentos/temporales", fd);
      onSubido(r.data);
    } catch (err) {
      setError(mensajeError(err, "No se pudo subir el archivo."));
    } finally { setSubiendo(false); }
  }, [api, archivo, tipo, descripcion, claves, emision, vencimiento, pideVence, onSubido]);

  return (
    <VentanaModal titulo="Adjuntar documento" onCerrar={onCerrar} ancho={560}
      acciones={<button type="button" disabled={subiendo} onClick={() => void enviar()}>{subiendo ? "Subiendo…" : "Adjuntar"}</button>}>
      {error && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>}
      <div className="wiz__grupo">
        <Campo etiqueta="Archivo" obligatorio>
          <input type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(e) => setArchivo(e.target.files?.[0] ?? null)} />
        </Campo>
        <Campo etiqueta="Tipo de documento" obligatorio>
          <select value={idTipo} onChange={(e) => setIdTipo(Number(e.target.value))}>
            <option value={0}>Seleccione…</option>
            {porCarpeta.map(([carpeta, lista]) => (
              <optgroup key={carpeta} label={carpeta}>
                {lista.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
              </optgroup>
            ))}
          </select>
        </Campo>
        {pideVence && (
          <>
            <Campo etiqueta="Fecha de emisión"><input type="date" value={emision} onChange={(e) => setEmision(e.target.value)} /></Campo>
            <Campo etiqueta="Fecha de vencimiento" obligatorio={tipo?.vencimiento === "Obligatorio"}>
              <input type="date" value={vencimiento} onChange={(e) => setVencimiento(e.target.value)} />
            </Campo>
          </>
        )}
        <Campo etiqueta="Descripción (opcional)"><input type="text" maxLength={300} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} /></Campo>
        <Campo etiqueta="Palabras clave (opcional, separadas por coma)"><input type="text" value={claves} onChange={(e) => setClaves(e.target.value)} /></Campo>
        {tipo?.confidencialidad === "Restringida" && <div className="nota">Este tipo de documento es restringido: solo lo ven los usuarios autorizados.</div>}
      </div>
    </VentanaModal>
  );
}
