import { useCallback, useEffect, useMemo, useState, type DragEvent } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { mensajeError, fmtFecha } from "../api/mensajes";
import { Campo } from "../components/CamposWizard";
import { SelectorCliente, type ClienteElegido } from "../documentos/AccionesDocumento";
import { fmtTamano, mensajeSolicitud, quitarTemporales, solicitarDocumentos, subirTemporal } from "../documentos/documentos";
import type { ClienteDocumento, DocumentoBusqueda, DocumentoOperacion, LimitesDocumentos, SolicitudDocumentos, TipoDocumento } from "../api/tipos";

const LIMITES_POR_DEFECTO: LimitesDocumentos = { tamanoMaxMb: 10, extensiones: ["pdf", "jpg", "jpeg", "png"], maxArchivos: 10 };
const PASOS = [
  { n: 1, etiqueta: "Cliente", sub: "A quién pertenecen" },
  { n: 2, etiqueta: "Archivos", sub: "Uno o varios" },
  { n: 3, etiqueta: "Clasificar", sub: "Tipo y vencimiento" },
  { n: 4, etiqueta: "Confirmar", sub: "Revisar y guardar" },
];

interface Item {
  clave: number; archivo: File; idCarpeta: number; idTipo: number; sugerido: boolean;
  emision: string; vencimiento: string; descripcion: string; claves: string;
}
interface Resultado { solicitud: SolicitudDocumentos; resumen: { archivo: string; texto: string }[] }

const normalizar = (t: string) => t.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, " ").trim();
const extension = (n: string) => n.split(".").pop()?.toLowerCase() ?? "";

/** Sugiere el tipo cuyo nombre comparte mas palabras (>=4 letras) con el nombre del archivo; solo si hay un ganador claro. */
function sugerirTipo(nombreArchivo: string, tipos: TipoDocumento[]): TipoDocumento | null {
  const base = " " + normalizar(nombreArchivo.replace(/\.[^.]+$/, "")) + " ";
  let mejor: TipoDocumento | null = null, puntos = 0, empate = false;
  for (const t of tipos) {
    const p = normalizar(t.nombre).split(" ").filter((w) => w.length >= 4 && base.includes(w)).length;
    if (p > puntos) { mejor = t; puntos = p; empate = false; } else if (p === puntos && p > 0) empate = true;
  }
  return puntos > 0 && !empate ? mejor : null;
}

/**
 * Asistente «Cargar documentos»: 1) cliente, 2) uno o varios archivos, 3) clasificar cada uno, 4) confirmar.
 * Si el tipo es de documento unico y el cliente ya tiene uno vigente, el archivo entra como NUEVA VERSION (el anterior queda en el historial).
 * Todo viaja en una gestion: segun la regla de aprobacion se aplica sola o queda pendiente (los archivos se ven como «pendientes de aprobacion»).
 */
export function CargarDocumentosPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  // Si se llego desde la ficha 360, Cancelar y el cierre regresan a su pestana Documentos.
  const vuelta360 = params.get("volver") === "360" && Number(params.get("cliente")) > 0 ? `/interlocutores/${Number(params.get("cliente"))}?pestana=documentos` : null;

  const [cliente, setCliente] = useState<ClienteElegido | null>(() => {
    const id = Number(params.get("cliente"));
    return id > 0 ? { id, numero: params.get("numero") ?? "", nombre: params.get("nombre") ?? "", tipoPersona: params.get("tipoPersona") || undefined } : null;
  });
  const [paso, setPaso] = useState(() => (Number(params.get("cliente")) > 0 ? 2 : 1));
  const [tiposTodos, setTiposTodos] = useState<TipoDocumento[]>([]);
  const [vigentes, setVigentes] = useState<DocumentoBusqueda[]>([]);
  const [items, setItems] = useState<Item[]>([]);
  const [motivo, setMotivo] = useState("Actualización del documento");
  const [mostrarErrores, setMostrarErrores] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<Resultado | null>(null);
  const [limites, setLimites] = useState<LimitesDocumentos>(LIMITES_POR_DEFECTO);
  const [siguienteClave, setSiguienteClave] = useState(1);
  const [arrastrando, setArrastrando] = useState(false);

  useEffect(() => {
    let vivo = true;
    void api.get<LimitesDocumentos>("/api/documentos/limites").then((r) => { if (vivo) setLimites(r.data); }).catch(() => undefined);
    return () => { vivo = false; };
  }, [api]);

  // Si se llego con el cliente por enlace (sin tipo de persona), se completa consultandolo.
  useEffect(() => {
    if (!cliente || cliente.tipoPersona) return;
    let vivo = true;
    void (async () => {
      try {
        const r = await api.get<ClienteDocumento[]>("/api/documentos/clientes", { params: { texto: cliente.numero || cliente.nombre } });
        const c = r.data.find((x) => x.id === cliente.id);
        if (vivo && c) setCliente({ ...cliente, tipoPersona: c.tipoPersona });
      } catch { /* sin tipo de persona se ofrecen todos los tipos y el servidor valida */ }
    })();
    return () => { vivo = false; };
  }, [api, cliente]);

  // Catalogo de tipos aplicables al cliente y sus documentos vigentes (para detectar reemplazos y topes).
  useEffect(() => {
    if (!cliente) return;
    let vivo = true;
    void (async () => {
      try {
        const [t, d] = await Promise.all([
          api.get<TipoDocumento[]>("/api/documentos/tipos", { params: { tipoPersona: cliente.tipoPersona || undefined } }),
          api.get<DocumentoBusqueda[]>("/api/documentos/buscar", { params: { idInterlocutor: cliente.id } }),
        ]);
        if (!vivo) return;
        setTiposTodos(t.data.filter((x) => !x.carpetaSistema));
        setVigentes(d.data.filter((x) => x.documento.estado === "Vigente"));
      } catch (err) { if (vivo) setError(mensajeError(err, "No se pudo cargar el catálogo de documentos del cliente.")); }
    })();
    return () => { vivo = false; };
  }, [api, cliente?.id, cliente?.tipoPersona]); // eslint-disable-line react-hooks/exhaustive-deps

  const carpetas = useMemo(() => {
    const m = new Map<number, string>();
    tiposTodos.forEach((t) => m.set(t.idCarpeta, t.carpeta));
    return [...m.entries()];
  }, [tiposTodos]);
  const tipoDe = useCallback((id: number) => tiposTodos.find((t) => t.id === id), [tiposTodos]);
  const vigenteDeTipo = useCallback((idTipo: number) => vigentes.filter((v) => v.documento.idTipoDocumento === idTipo), [vigentes]);

  function agregarArchivos(lista: FileList | File[]) {
    const nuevos: Item[] = [];
    const avisos: string[] = [];
    let clave = siguienteClave;
    for (const f of Array.from(lista)) {
      if (!limites.extensiones.includes(extension(f.name))) { avisos.push(`«${f.name}»: solo se permiten ${limites.extensiones.join(", ")}.`); continue; }
      if (f.size > limites.tamanoMaxMb * 1024 * 1024) { avisos.push(`«${f.name}» supera los ${limites.tamanoMaxMb} MB.`); continue; }
      if (f.size === 0) { avisos.push(`«${f.name}» está vacío.`); continue; }
      if (items.some((i) => i.archivo.name === f.name && i.archivo.size === f.size) || nuevos.some((i) => i.archivo.name === f.name && i.archivo.size === f.size)) {
        avisos.push(`«${f.name}» ya está en la lista.`); continue;
      }
      if (items.length + nuevos.length >= limites.maxArchivos) { avisos.push(`Máximo ${limites.maxArchivos} archivos por carga.`); break; }
      const s = sugerirTipo(f.name, tiposTodos);
      nuevos.push({ clave: clave++, archivo: f, idCarpeta: s?.idCarpeta ?? 0, idTipo: s?.id ?? 0, sugerido: !!s, emision: "", vencimiento: "", descripcion: "", claves: "" });
    }
    setSiguienteClave(clave);
    setItems((a) => [...a, ...nuevos]);
    setError(avisos.length > 0 ? avisos.join(" ") : null);
  }
  const cambiar = (clave: number, cambios: Partial<Item>) => setItems((a) => a.map((i) => (i.clave === clave ? { ...i, ...cambios } : i)));
  const quitar = (clave: number) => setItems((a) => a.filter((i) => i.clave !== clave));

  function alSoltar(e: DragEvent<HTMLDivElement>) { e.preventDefault(); setArrastrando(false); if (e.dataTransfer.files.length > 0) agregarArchivos(e.dataTransfer.files); }

  /** Errores por archivo en el paso 3 (clave -> mensaje). */
  const erroresItem = useMemo(() => {
    const e: Record<number, string> = {};
    const unicos = new Map<number, number>();
    const porTipo = new Map<number, number>();
    for (const i of items) {
      if (i.idTipo) porTipo.set(i.idTipo, (porTipo.get(i.idTipo) ?? 0) + 1);
    }
    for (const i of items) {
      const t = tipoDe(i.idTipo);
      if (!t) { e[i.clave] = "Seleccione el tipo de documento."; continue; }
      if (t.vencimiento === "Obligatorio" && !i.vencimiento) { e[i.clave] = `«${t.nombre}» exige la fecha de vencimiento.`; continue; }
      if (i.emision && i.vencimiento && i.vencimiento < i.emision) { e[i.clave] = "El vencimiento no puede ser anterior a la emisión."; continue; }
      if (t.cardinalidad === "Unico") {
        if (unicos.has(t.id)) e[i.clave] = `«${t.nombre}» admite un solo documento vigente: ya hay otro archivo de este tipo en la carga.`;
        unicos.set(t.id, i.clave);
      } else if (t.maximo !== null && vigenteDeTipo(t.id).length + (porTipo.get(t.id) ?? 0) > t.maximo) {
        e[i.clave] = `«${t.nombre}» admite máximo ${t.maximo} documentos vigentes; el cliente ya tiene ${vigenteDeTipo(t.id).length}.`;
      }
    }
    return e;
  }, [items, tipoDe, vigenteDeTipo]);

  const reemplazos = items.filter((i) => tipoDe(i.idTipo)?.cardinalidad === "Unico" && vigenteDeTipo(i.idTipo).length > 0);

  function siguiente() {
    setError(null);
    if (paso === 1) { if (!cliente) return setError("Seleccione el cliente."); setPaso(2); return; }
    if (paso === 2) { if (items.length === 0) return setError("Agregue al menos un archivo."); setPaso(3); return; }
    if (paso === 3) {
      setMostrarErrores(true);
      if (Object.keys(erroresItem).length > 0) return setError("Revise los archivos marcados antes de continuar.");
      if (reemplazos.length > 0 && !motivo.trim()) return setError("Indique el motivo de los reemplazos.");
      setPaso(4);
    }
  }

  async function confirmar() {
    if (!cliente) return;
    setEnviando(true); setError(null);
    const subidos: number[] = [];
    try {
      const ops: DocumentoOperacion[] = [];
      const resumen: { archivo: string; texto: string }[] = [];
      for (const i of items) {
        const t = tipoDe(i.idTipo)!;
        const previo = t.cardinalidad === "Unico" ? vigenteDeTipo(t.id)[0] : undefined;
        let doc;
        try {
          doc = await subirTemporal(api, i.archivo, t.id, {
            emision: t.vencimiento !== "NoAplica" ? i.emision : "", vencimiento: t.vencimiento !== "NoAplica" ? i.vencimiento : "",
            descripcion: previo ? "" : i.descripcion, claves: previo ? "" : i.claves,
          });
        } catch (err) {
          throw new Error(`«${i.archivo.name}»: ${mensajeError(err, "no se pudo subir el archivo.")}`);
        }
        subidos.push(doc.idDocumento);
        if (previo) {
          ops.push({ tipo: "Reemplazo", idDocumento: doc.idDocumento, idDocumentoDestino: previo.documento.idDocumento, motivo: motivo.trim() });
          resumen.push({ archivo: i.archivo.name, texto: `Nueva versión (v${previo.documento.version + 1}) de ${t.nombre}` });
        } else {
          ops.push({ tipo: "Alta", idDocumento: doc.idDocumento });
          resumen.push({ archivo: i.archivo.name, texto: `Agregado como ${t.nombre}` });
        }
      }
      const solicitud = await solicitarDocumentos(api, cliente.id, ops, subidos);
      setResultado({ solicitud, resumen });
    } catch (err) {
      await quitarTemporales(api, subidos);
      setError(err instanceof Error && err.message.startsWith("«") ? err.message : mensajeError(err, "No se pudo registrar la carga."));
      setPaso(3); setMostrarErrores(true);
    } finally { setEnviando(false); }
  }

  function reiniciar() { setItems([]); setResultado(null); setPaso(cliente ? 2 : 1); setMostrarErrores(false); setError(null); setMotivo("Actualización del documento"); }

  // ---------------------------------------------------------------- resultado
  if (resultado) {
    const { solicitud, resumen } = resultado;
    return (
      <section className="wiz-exito">
        <div className="wiz-exito__icono" aria-hidden="true">{solicitud.aplicada ? "✓" : "⏳"}</div>
        <h1>{solicitud.aplicada ? "Documentos guardados" : "Enviado a aprobación"}</h1>
        <p>Cliente {cliente?.numero} · {cliente?.nombre}</p>
        <p className="nota">{mensajeSolicitud(solicitud)}{!solicitud.aplicada && " Mientras tanto los archivos se ven en el expediente como «pendientes de aprobación»."}</p>
        <div className="wiz-exito__codigo">{solicitud.numero}</div>
        <ul style={{ textAlign: "left", maxWidth: 640, margin: "0 auto 1rem", listStyle: "none", padding: 0 }}>
          {resumen.map((r) => <li key={r.archivo}>✓ <strong>{r.archivo}</strong> — {r.texto}</li>)}
        </ul>
        <div className="wiz-exito__acciones">
          <button type="button" className="boton-secundario" onClick={() => navigate(`/gestiones/consulta?ver=${solicitud.idGestion}`)}>Ver la gestión</button>
          <button type="button" className="boton-secundario" onClick={() => navigate(vuelta360 ?? `/documentos/expediente?cliente=${cliente!.id}&numero=${encodeURIComponent(cliente!.numero)}&nombre=${encodeURIComponent(cliente!.nombre)}`)}>{vuelta360 ? "Volver al cliente" : "Ver documentos del cliente"}</button>
          <button type="button" onClick={reiniciar}>Cargar más documentos</button>
        </div>
      </section>
    );
  }

  // ---------------------------------------------------------------- pasos
  return (
    <div>
      <div className="wiz__titulo">
        <div>
          <h1>Cargar documentos</h1>
          <p className="nota">Suba uno o varios archivos al expediente del cliente. Se registra como una gestión: según la regla de aprobación se aplica de inmediato o queda pendiente de aprobación.</p>
        </div>
        {cliente && paso > 1 && <span className="wiz__chip">{cliente.numero} · {cliente.nombre}</span>}
      </div>

      <ol className="wiz__pasos">
        {PASOS.map((p) => {
          const actual = p.n === paso, hecho = p.n < paso;
          return (
            <li key={p.n} className="wiz__paso-item">
              <button type="button" disabled={!(hecho && p.n >= (params.get("cliente") ? 2 : 1))} onClick={() => setPaso(p.n)}
                className={`wiz__paso ${actual ? "wiz__paso--actual" : ""} ${hecho ? "wiz__paso--hecho" : ""}`} aria-current={actual ? "step" : undefined}>
                <span className="wiz__paso-num">{hecho ? "✓" : p.n}</span>
                <span className="wiz__paso-texto"><span className="wiz__paso-etiqueta">{p.etiqueta}</span><span className="wiz__paso-sub">{p.sub}</span></span>
              </button>
              <span className={`wiz__paso-linea ${p.n < paso ? "wiz__paso-linea--hecha" : ""}`} />
            </li>
          );
        })}
      </ol>

      <section className="wiz__tarjeta">
        <div className="wiz__cuerpo">
          {error && <div role="alert" className="wiz__alerta">{error}</div>}

          {paso === 1 && (
            <div className="wiz__grupo">
              <h2>¿A qué cliente pertenecen los documentos?</h2>
              <Campo etiqueta="Cliente" obligatorio><SelectorCliente valor={cliente} onElegir={setCliente} /></Campo>
            </div>
          )}

          {paso === 2 && (
            <div className="wiz__grupo">
              <h2>Seleccione los archivos</h2>
              <div className={`docs-soltar ${arrastrando ? "docs-soltar--activo" : ""}`}
                onDragOver={(e) => { e.preventDefault(); setArrastrando(true); }} onDragLeave={() => setArrastrando(false)} onDrop={alSoltar}>
                <p>Arrastre los archivos aquí o</p>
                <label className="docs-soltar__boton">
                  Elegir archivos
                  <input type="file" multiple accept=".pdf,.jpg,.jpeg,.png" hidden
                    onChange={(e) => { if (e.target.files) agregarArchivos(e.target.files); e.target.value = ""; }} />
                </label>
                <p className="nota">{limites.extensiones.join(", ").toUpperCase()} · hasta {limites.tamanoMaxMb} MB cada uno · máximo {limites.maxArchivos} archivos por carga</p>
              </div>
              {items.length > 0 && (
                <ul className="docs-archivos">
                  {items.map((i) => (
                    <li key={i.clave}>
                      <span>📄 <strong>{i.archivo.name}</strong> <span className="nota">{fmtTamano(i.archivo.size)}</span></span>
                      <button type="button" className="wiz-enlace wiz-enlace--peligro" onClick={() => quitar(i.clave)}>Quitar</button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {paso === 3 && (
            <div className="wiz__grupo">
              <h2>Clasifique cada archivo</h2>
              {items.map((i) => {
                const t = tipoDe(i.idTipo);
                const previo = t?.cardinalidad === "Unico" ? vigenteDeTipo(t.id)[0] : undefined;
                const tiposCarpeta = tiposTodos.filter((x) => x.idCarpeta === i.idCarpeta);
                const err = mostrarErrores ? erroresItem[i.clave] : undefined;
                return (
                  <div key={i.clave} className={`docs-tarjeta ${err ? "docs-tarjeta--error" : ""}`}>
                    <div className="docs-tarjeta__cab">
                      <span>📄 <strong>{i.archivo.name}</strong> <span className="nota">{fmtTamano(i.archivo.size)}</span></span>
                      <button type="button" className="wiz-enlace wiz-enlace--peligro" onClick={() => quitar(i.clave)}>Quitar</button>
                    </div>
                    <div className="wiz__rejilla">
                      <Campo etiqueta="Carpeta" obligatorio>
                        <select value={i.idCarpeta} onChange={(e) => cambiar(i.clave, { idCarpeta: Number(e.target.value), idTipo: 0, sugerido: false })}>
                          <option value={0}>Seleccione…</option>
                          {carpetas.map(([id, n]) => <option key={id} value={id}>{n}</option>)}
                        </select>
                      </Campo>
                      <Campo etiqueta="Tipo de documento" obligatorio>
                        <select value={i.idTipo} disabled={!i.idCarpeta} onChange={(e) => cambiar(i.clave, { idTipo: Number(e.target.value), sugerido: false })}>
                          <option value={0}>Seleccione…</option>
                          {tiposCarpeta.map((x) => <option key={x.id} value={x.id}>{x.nombre}</option>)}
                        </select>
                      </Campo>
                      {t && t.vencimiento !== "NoAplica" && (
                        <>
                          <Campo etiqueta="Fecha de emisión"><input type="date" value={i.emision} onChange={(e) => cambiar(i.clave, { emision: e.target.value })} /></Campo>
                          <Campo etiqueta="Fecha de vencimiento" obligatorio={t.vencimiento === "Obligatorio"}>
                            <input type="date" value={i.vencimiento} onChange={(e) => cambiar(i.clave, { vencimiento: e.target.value })} />
                          </Campo>
                        </>
                      )}
                    </div>
                    {i.sugerido && <div className="nota">Tipo sugerido por el nombre del archivo; cámbielo si no corresponde.</div>}
                    {previo && (
                      <div className="wiz__aviso" style={{ marginTop: "0.5rem", marginBottom: 0 }}>
                        El cliente ya tiene «{t!.nombre}» vigente ({previo.documento.nombreArchivo}, v{previo.documento.version}). Este archivo lo <strong>reemplazará como nueva versión</strong>; la anterior queda en el historial.
                      </div>
                    )}
                    {t?.confidencialidad === "Restringida" && <div className="nota">Tipo restringido: solo lo ven los usuarios autorizados.</div>}
                    {!previo && (
                      <details className="docs-mas">
                        <summary>Más datos (opcional)</summary>
                        <div className="wiz__rejilla" style={{ marginTop: "0.5rem" }}>
                          <Campo etiqueta="Descripción"><input type="text" maxLength={300} value={i.descripcion} onChange={(e) => cambiar(i.clave, { descripcion: e.target.value })} /></Campo>
                          <Campo etiqueta="Palabras clave (separadas por coma)"><input type="text" value={i.claves} onChange={(e) => cambiar(i.clave, { claves: e.target.value })} /></Campo>
                        </div>
                      </details>
                    )}
                    {err && <div className="wiz__alerta" role="alert" style={{ marginTop: "0.5rem", marginBottom: 0 }}>{err}</div>}
                  </div>
                );
              })}
              {reemplazos.length > 0 && (
                <Campo etiqueta={`Motivo de ${reemplazos.length === 1 ? "el reemplazo" : "los reemplazos"}`} obligatorio>
                  <input type="text" maxLength={300} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
                </Campo>
              )}
            </div>
          )}

          {paso === 4 && (
            <div className="wiz__grupo">
              <h2>Revise y confirme</h2>
              <table className="tabla tabla--compacta">
                <thead><tr><th>Archivo</th><th>Carpeta · Tipo</th><th>Acción</th><th>Vence</th></tr></thead>
                <tbody>
                  {items.map((i) => {
                    const t = tipoDe(i.idTipo)!;
                    const previo = t.cardinalidad === "Unico" ? vigenteDeTipo(t.id)[0] : undefined;
                    return (
                      <tr key={i.clave}>
                        <td>{i.archivo.name} <span className="nota">{fmtTamano(i.archivo.size)}</span></td>
                        <td>{t.carpeta} · {t.nombre}</td>
                        <td>{previo ? `Nueva versión (v${previo.documento.version + 1})` : "Documento nuevo"}</td>
                        <td>{i.vencimiento ? fmtFecha(i.vencimiento) : "—"}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
              {reemplazos.length > 0 && <p className="nota">Motivo de los reemplazos: {motivo}</p>}
            </div>
          )}
        </div>

        <div className="wiz__pie">
          <div className="wiz__pie-grupo">
            <button type="button" className="wiz-enlace" onClick={() => navigate(vuelta360 ?? "/documentos")}>Cancelar</button>
          </div>
          <div className="wiz__pie-grupo">
            {paso > (params.get("cliente") ? 2 : 1) && <button type="button" className="boton-secundario" disabled={enviando} onClick={() => setPaso(paso - 1)}>← Atrás</button>}
            {paso < 4
              ? <button type="button" onClick={siguiente}>Siguiente →</button>
              : <button type="button" disabled={enviando} onClick={() => void confirmar()}>{enviando ? "Enviando…" : `Enviar ${items.length} documento(s)`}</button>}
          </div>
        </div>
      </section>
    </div>
  );
}
