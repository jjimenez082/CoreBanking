import { useEffect, useMemo, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import { mensajeError, fmtFecha, fmtFechaUtc } from "../api/mensajes";
import type { ArbolDocumentos as Arbol, CarpetaDocumentos, DocumentoApi } from "../api/tipos";
import { descargarDocumento, etiquetaVigencia, fmtTamano } from "./documentos";

/** Acciones que el Gestor documental habilita sobre el arbol (la ficha 360 no pasa ninguna: solo lectura). */
export interface AccionesArbol {
  puedeSubir: boolean; puedeReemplazar: boolean; puedeObsoletar: boolean;
  onAgregar: (carpeta: CarpetaDocumentos) => void;
  onReemplazar: (d: DocumentoApi) => void;
  onObsoletar: (d: DocumentoApi) => void;
  onVersiones: (d: DocumentoApi) => void;
}

const extDe = (n: string) => (n.split(".").pop() ?? "").toLowerCase();
const Ext = ({ nombre }: { nombre: string }) => {
  const e = extDe(nombre);
  return <span className={`docs-ext ${e === "pdf" ? "docs-ext--pdf" : "docs-ext--img"}`} aria-hidden="true">{e === "jpeg" ? "JPG" : e.toUpperCase().slice(0, 4)}</span>;
};

/**
 * Arbol de documentos de un cliente. Sin `acciones` es solo lectura (ver y descargar), como en la ficha 360: agregar, reemplazar y
 * obsoletar se piden desde el Gestor documental (cada uno es una gestion).
 * `recarga` cambia cuando hay que volver a pedir el arbol (despues de una accion).
 */
export function ArbolDocumentos({ idInterlocutor, acciones, recarga = 0 }: { idInterlocutor: number; acciones?: AccionesArbol; recarga?: number }) {
  const api = useApiClient();
  const [arbol, setArbol] = useState<Arbol | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cerradas, setCerradas] = useState<Set<string>>(new Set(["GESTIONES"]));
  const [verObsoletos, setVerObsoletos] = useState(false);

  useEffect(() => {
    let vivo = true;
    void (async () => {
      try {
        const r = await api.get<Arbol>(`/api/interlocutores/${idInterlocutor}/documentos`);
        if (vivo) setArbol(r.data);
      } catch (err) {
        if (vivo) setError(mensajeError(err, "No se pudo cargar el árbol de documentos."));
      }
    })();
    return () => { vivo = false; };
  }, [api, idInterlocutor, recarga]);

  const alternar = (k: string) => setCerradas((s) => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); return n; });
  async function bajar(d: DocumentoApi) {
    setError(null);
    try { await descargarDocumento(api, d); } catch { setError("No se pudo descargar el archivo."); }
  }

  const todos = useMemo(() => (arbol ? arbol.carpetas.flatMap((c) => [...c.documentos, ...c.subcarpetas.flatMap((s) => s.documentos)]) : []), [arbol]);
  const resumen = useMemo(() => ({
    vigentes: todos.filter((d) => d.estado === "Vigente" && d.estadoVigencia !== "Vencido").length,
    porVencer: todos.filter((d) => d.estado === "Vigente" && d.estadoVigencia === "PorVencer").length,
    vencidos: todos.filter((d) => d.estado === "Vigente" && d.estadoVigencia === "Vencido").length,
    pendientes: todos.filter((d) => d.estado === "EnGestion").length,
    obsoletos: todos.filter((d) => d.estado === "Obsoleto").length,
  }), [todos]);

  if (error && !arbol) return <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>;
  if (!arbol) return <p className="nota">Cargando documentos…</p>;

  const visibles = (l: DocumentoApi[]) => (verObsoletos ? l : l.filter((d) => d.estado !== "Obsoleto"));

  const fila = (d: DocumentoApi) => {
    const v = etiquetaVigencia(d);
    return (
      <li key={d.idDocumento} className={`docs-doc ${d.estado === "Obsoleto" ? "docs-doc--obsoleto" : ""}`}>
        <Ext nombre={d.nombreArchivo} />
        <div className="docs-doc__cuerpo">
          <button type="button" className="docs-doc__nombre" onClick={() => void bajar(d)} title="Descargar">{d.nombreArchivo}</button>
          <div className="docs-doc__sub">{d.tipoNombre} · v{d.version} · {fmtTamano(d.tamano)}</div>
          <div className="docs-doc__meta">
            Subido {fmtFechaUtc(d.subidoEn)} por {d.subidoPor}
            {d.fechaVencimiento && <> · vence {fmtFecha(d.fechaVencimiento)}</>}
            {d.numeroGestion && d.origen === "Expediente" && <> · gestión {d.numeroGestion}</>}
            {d.fusionadoDesde && <> · fusionado desde el cliente N.º {d.fusionadoDesde}</>}
          </div>
          {d.descripcion && <div className="docs-doc__meta">{d.descripcion}</div>}
          {d.claves.length > 0 && <div className="docs-doc__claves">{d.claves.map((k) => <span key={k}>{k}</span>)}</div>}
          {d.motivoObsoleto && <div className="docs-doc__meta">Motivo: {d.motivoObsoleto}</div>}
          {acciones && (
            <div className="docs-arbol__acciones">
              <button type="button" className="wiz-enlace" onClick={() => acciones.onVersiones(d)}>Versiones</button>
              {d.origen === "Expediente" && d.estado === "Vigente" && acciones.puedeReemplazar && (
                <button type="button" className="wiz-enlace" onClick={() => acciones.onReemplazar(d)}>Reemplazar</button>)}
              {d.origen === "Expediente" && d.estado === "Vigente" && acciones.puedeObsoletar && (
                <button type="button" className="wiz-enlace wiz-enlace--peligro" onClick={() => acciones.onObsoletar(d)}>Obsoletar</button>)}
            </div>
          )}
        </div>
        <div className="docs-doc__estado">
          {v && <span className={v.clase}>{v.texto}</span>}
          {d.restringido && <span className="docs-chip docs-chip--restringido">Restringido</span>}
        </div>
      </li>
    );
  };

  const conDocs = arbol.carpetas.map((c) => {
    const propios = visibles(c.documentos);
    const subs = c.subcarpetas.map((x) => ({ ...x, documentos: visibles(x.documentos) })).filter((x) => x.documentos.length > 0);
    return { c, propios, subs, total: propios.length + subs.reduce((n, x) => n + x.documentos.length, 0) };
  });
  const llenas = conDocs.filter((x) => x.total > 0);
  const vacias = conDocs.filter((x) => x.total === 0);

  return (
    <div className="docs-arbol">
      {error && <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>}
      <div className="docs-resumen">
        <span className="docs-resumen__item"><strong>{resumen.vigentes}</strong> vigente(s)</span>
        {resumen.porVencer > 0 && <span className="docs-resumen__item docs-resumen__item--aviso"><strong>{resumen.porVencer}</strong> por vencer</span>}
        {resumen.vencidos > 0 && <span className="docs-resumen__item docs-resumen__item--error"><strong>{resumen.vencidos}</strong> vencido(s)</span>}
        {resumen.pendientes > 0 && <span className="docs-resumen__item docs-resumen__item--aviso"><strong>{resumen.pendientes}</strong> pendiente(s) de aprobación</span>}
        {resumen.obsoletos > 0 && (
          <label className="docs-resumen__item docs-resumen__item--check">
            <input type="checkbox" checked={verObsoletos} onChange={(e) => setVerObsoletos(e.target.checked)} /> Ver {resumen.obsoletos} obsoleto(s)
          </label>
        )}
      </div>

      {llenas.length === 0 && <div className="wiz__vacio">Este cliente aún no tiene documentos.</div>}

      {llenas.map(({ c, propios, subs, total }) => {
        const abierta = !cerradas.has(c.codigo);
        return (
          <section key={c.codigo} className="docs-carpeta">
            <header className="docs-carpeta__cab">
              <button type="button" className="docs-carpeta__titulo" onClick={() => alternar(c.codigo)} aria-expanded={abierta}>
                <span className="docs-carpeta__chevron" aria-hidden="true">{abierta ? "▾" : "▸"}</span>
                <span aria-hidden="true">{c.esSistema ? "🗂" : "📁"}</span> {c.nombre}
                <span className="docs-carpeta__cuenta">{total}</span>
              </button>
              {acciones?.puedeSubir && !c.esSistema && (
                <button type="button" className="wiz-enlace" onClick={() => acciones.onAgregar(c)}>＋ Agregar</button>
              )}
            </header>
            {abierta && (
              <div className="docs-carpeta__cuerpo">
                {c.esSistema && <p className="docs-carpeta__nota">Evidencia de las gestiones aprobadas. No se modifica.</p>}
                {propios.length > 0 && <ul className="docs-lista">{propios.map(fila)}</ul>}
                {subs.map((x) => {
                  const k = `${c.codigo}:${x.idGestion}`;
                  const ab = !cerradas.has(k);
                  return (
                    <div key={k} className="docs-subcarpeta">
                      <button type="button" className="docs-subcarpeta__titulo" onClick={() => alternar(k)} aria-expanded={ab}>
                        <span aria-hidden="true">{ab ? "▾" : "▸"}</span> {x.nombre} <span className="docs-carpeta__cuenta">{x.documentos.length}</span>
                      </button>
                      {ab && <ul className="docs-lista">{x.documentos.map(fila)}</ul>}
                    </div>
                  );
                })}
              </div>
            )}
          </section>
        );
      })}

      {vacias.length > 0 && (
        <div className="docs-vacias">
          <span>Sin documentos:</span>
          {vacias.map(({ c }) => (
            acciones?.puedeSubir && !c.esSistema
              ? <button key={c.codigo} type="button" className="docs-vacias__chip docs-vacias__chip--accion" onClick={() => acciones.onAgregar(c)}>＋ {c.nombre}</button>
              : <span key={c.codigo} className="docs-vacias__chip">{c.nombre}</span>
          ))}
        </div>
      )}
    </div>
  );
}

/** Lista de adjuntos de una gestion (para el aprobador, dentro del detalle de la gestion). */
export function AdjuntosDeGestion({ idGestion }: { idGestion: number }) {
  const api = useApiClient();
  const [docs, setDocs] = useState<DocumentoApi[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let vivo = true;
    void (async () => {
      try {
        const r = await api.get<DocumentoApi[]>(`/api/gestiones/${idGestion}/adjuntos`);
        if (vivo) setDocs(r.data);
      } catch (err) {
        if (vivo) setError(mensajeError(err, "No se pudieron cargar los adjuntos."));
      }
    })();
    return () => { vivo = false; };
  }, [api, idGestion]);

  if (error) return <div className="wiz__aviso wiz__aviso--devuelta" role="alert">{error}</div>;
  if (!docs) return <p className="nota">Cargando adjuntos…</p>;
  if (docs.length === 0) return <p className="nota">Esta gestión no tiene documentos adjuntos.</p>;
  return (
    <ul className="docs-arbol__lista">
      {docs.map((d) => {
        const v = etiquetaVigencia(d);
        return (
          <li key={d.idDocumento} className="docs-arbol__doc">
            <button type="button" className="wiz-enlace" onClick={() => void descargarDocumento(api, d).catch(() => setError("No se pudo descargar el archivo."))}>{d.nombreArchivo}</button>
            <span className="nota"> · {d.tipoNombre} · {fmtTamano(d.tamano)} · {d.subidoPor}</span>
            {d.fechaVencimiento && <span className="nota"> · vence {fmtFecha(d.fechaVencimiento)}</span>}
            {v && <span className={v.clase}>{v.texto}</span>}
            {d.restringido && <span className="docs-chip docs-chip--restringido">Restringido</span>}
            {d.estado === "Descartado" && <span className="docs-chip docs-chip--obsoleto">Descartado</span>}
            {d.descripcion && <div className="nota">{d.descripcion}</div>}
          </li>
        );
      })}
    </ul>
  );
}
