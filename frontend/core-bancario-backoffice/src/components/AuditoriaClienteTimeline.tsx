import { Fragment, useCallback, useEffect, useState } from "react";
import { GestionDetalleModal } from "./GestionDetalleModal";
import { useApiClient } from "../api/useApiClient";
import { fmtFechaHora, mensajeError } from "../api/mensajes";
import type { AuditoriaEvento, AuditoriaEventoPagina, AuditoriaPeriodos } from "../api/tipos";

const MESES = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "setiembre", "octubre", "noviembre", "diciembre"];
const p2 = (n: number) => String(n).padStart(2, "0");

// Horas en la zona del que consulta (la base guarda UTC). No es la zona de quien hizo el cambio.
const DESPLAZAMIENTO_MIN = -new Date().getTimezoneOffset();
function etiquetaZona(): string {
  const m = Math.abs(DESPLAZAMIENTO_MIN);
  const utc = `UTC${DESPLAZAMIENTO_MIN < 0 ? "-" : "+"}${p2(Math.floor(m / 60))}:${p2(m % 60)}`;
  let zona = "";
  try { zona = Intl.DateTimeFormat().resolvedOptions().timeZone; } catch { /* sin zona */ }
  return zona ? `${utc} · ${zona}` : utc;
}

interface EstadoDia { horaDesde: number; horaHasta: number; pagina: number; datos: AuditoriaEventoPagina | null; cargando: boolean }

/**
 * Historial de auditoría de un cliente como línea de tiempo: años > meses > días > eventos (encabezado y detalle).
 * Cada nivel se pide al abrirlo, así la pantalla nace limpia. Permiso CLI_AUDITORIA.
 */
export function AuditoriaClienteTimeline({ idInterlocutor }: { idInterlocutor: number }) {
  const api = useApiClient();
  const [incluirFusionados, setIncluirFusionados] = useState(false);
  const [anios, setAnios] = useState<AuditoriaPeriodos | null>(null);
  const [meses, setMeses] = useState<Record<string, AuditoriaPeriodos>>({});
  const [dias, setDias] = useState<Record<string, AuditoriaPeriodos>>({});
  const [eventos, setEventos] = useState<Record<string, EstadoDia>>({});
  const [abiertos, setAbiertos] = useState<Set<string>>(new Set());
  const [detalleAbierto, setDetalleAbierto] = useState<Set<string>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [idGestionVer, setIdGestionVer] = useState<number | null>(null);

  const base = `/api/interlocutores/${idInterlocutor}/auditoria`;

  const pedirPeriodos = useCallback(async (nivel: string, anio?: number, mes?: number) => {
    const r = await api.get<AuditoriaPeriodos>(`${base}/periodos`, {
      params: { nivel, anio, mes, desplazamientoMin: DESPLAZAMIENTO_MIN, incluirFusionados },
    });
    return r.data;
  }, [api, base, incluirFusionados]);

  // Al cambiar el cliente o la casilla de fusionados se reinicia todo.
  useEffect(() => {
    let vivo = true;
    setCargando(true); setError(null);
    setMeses({}); setDias({}); setEventos({}); setAbiertos(new Set()); setDetalleAbierto(new Set());
    pedirPeriodos("anio").then((d) => { if (vivo) setAnios(d); })
      .catch((e) => { if (vivo) setError(mensajeError(e, "No se pudo consultar la auditoría del cliente.")); })
      .finally(() => { if (vivo) setCargando(false); });
    return () => { vivo = false; };
  }, [pedirPeriodos]);

  async function cargarEventos(clave: string, y: number, m: number, d: number, horaDesde: number, horaHasta: number, pagina: number) {
    setEventos((prev) => ({ ...prev, [clave]: { horaDesde, horaHasta, pagina, datos: prev[clave]?.datos ?? null, cargando: true } }));
    try {
      const r = await api.get<AuditoriaEventoPagina>(`${base}/eventos`, {
        params: { fecha: `${y}-${p2(m)}-${p2(d)}`, horaDesde, horaHasta, desplazamientoMin: DESPLAZAMIENTO_MIN, incluirFusionados, pagina, tamanoPagina: 25 },
      });
      setEventos((prev) => ({ ...prev, [clave]: { horaDesde, horaHasta, pagina, datos: r.data, cargando: false } }));
    } catch (e) {
      setError(mensajeError(e, "No se pudo consultar los eventos del día."));
      setEventos((prev) => ({ ...prev, [clave]: { horaDesde, horaHasta, pagina, datos: prev[clave]?.datos ?? null, cargando: false } }));
    }
  }

  async function alternarAnio(y: number) {
    const k = `${y}`;
    const nuevo = new Set(abiertos);
    if (nuevo.has(k)) { nuevo.delete(k); setAbiertos(nuevo); return; }
    nuevo.add(k); setAbiertos(nuevo);
    if (!meses[k]) { try { const d = await pedirPeriodos("mes", y); setMeses((prev) => ({ ...prev, [k]: d })); } catch (e) { setError(mensajeError(e, "No se pudo consultar los meses.")); } }
  }
  async function alternarMes(y: number, m: number) {
    const k = `${y}-${m}`;
    const nuevo = new Set(abiertos);
    if (nuevo.has(k)) { nuevo.delete(k); setAbiertos(nuevo); return; }
    nuevo.add(k); setAbiertos(nuevo);
    if (!dias[k]) { try { const d = await pedirPeriodos("dia", y, m); setDias((prev) => ({ ...prev, [k]: d })); } catch (e) { setError(mensajeError(e, "No se pudo consultar los días.")); } }
  }
  async function alternarDia(y: number, m: number, d: number) {
    const k = `${y}-${m}-${d}`;
    const nuevo = new Set(abiertos);
    if (nuevo.has(k)) { nuevo.delete(k); setAbiertos(nuevo); return; }
    nuevo.add(k); setAbiertos(nuevo);
    if (!eventos[k]) await cargarEventos(k, y, m, d, 0, 24, 1);
  }
  function alternarDetalle(clave: string) {
    setDetalleAbierto((prev) => { const n = new Set(prev); if (n.has(clave)) n.delete(clave); else n.add(clave); return n; });
  }

  const estilos = { nodo: "tl__nodo", hijos: "tl__hijos" };

  return (
    <div className="tl">
      <p className="nota">
        Horas en <strong>su zona horaria ({etiquetaZona()})</strong>, no necesariamente la del usuario que hizo el cambio.
        Por ejemplo, un cambio hecho a las 4:00 pm en Panamá se muestra a las 3:00 pm si usted consulta desde Costa Rica.
      </p>
      {error && <p className="mensaje-error">{error}</p>}

      {anios && (
        <p className="tl__resumen">
          {anios.totalEventos === 0
            ? "Este cliente todavía no tiene movimientos de auditoría."
            : <>Último movimiento: <strong>{fmtFechaHora(anios.ultimoMovimiento)}</strong> · {anios.totalEventos.toLocaleString("es-CR")} evento(s)</>}
        </p>
      )}
      {anios && anios.fusionados.length > 0 && (
        <label className="tl__check">
          <input type="checkbox" checked={incluirFusionados} onChange={(e) => setIncluirFusionados(e.target.checked)} />
          Incluir el historial de los clientes fusionados en este: {anios.fusionados.map((f) => `N.º ${f.numero}`).join(", ")}
        </label>
      )}
      {cargando && !anios && <p className="nota">Cargando...</p>}

      <ul className="tl__lista">
        {anios?.periodos.map((a) => {
          const ka = `${a.valor}`;
          return (
            <li key={ka} className={estilos.nodo}>
              <button type="button" className="tl__boton tl__boton--anio" aria-expanded={abiertos.has(ka)} onClick={() => void alternarAnio(a.valor)}>
                <span>{abiertos.has(ka) ? "▾" : "▸"} {a.valor}</span><span className="tl__cuenta">{a.cantidad} evento(s)</span>
              </button>
              {abiertos.has(ka) && (
                <ul className={estilos.hijos}>
                  {!meses[ka] && <li className="nota">Cargando...</li>}
                  {meses[ka]?.periodos.map((m) => {
                    const km = `${a.valor}-${m.valor}`;
                    return (
                      <li key={km} className={estilos.nodo}>
                        <button type="button" className="tl__boton tl__boton--mes" aria-expanded={abiertos.has(km)} onClick={() => void alternarMes(a.valor, m.valor)}>
                          <span>{abiertos.has(km) ? "▾" : "▸"} {MESES[m.valor - 1]}</span><span className="tl__cuenta">{m.cantidad} evento(s)</span>
                        </button>
                        {abiertos.has(km) && (
                          <ul className={estilos.hijos}>
                            {!dias[km] && <li className="nota">Cargando...</li>}
                            {dias[km]?.periodos.map((d) => {
                              const kd = `${a.valor}-${m.valor}-${d.valor}`;
                              const est = eventos[kd];
                              const totalPag = est?.datos ? Math.max(1, Math.ceil(est.datos.total / est.datos.tamanoPagina)) : 1;
                              return (
                                <li key={kd} className={estilos.nodo}>
                                  <button type="button" className="tl__boton tl__boton--dia" aria-expanded={abiertos.has(kd)} onClick={() => void alternarDia(a.valor, m.valor, d.valor)}>
                                    <span>{abiertos.has(kd) ? "▾" : "▸"} {p2(d.valor)}/{p2(m.valor)}/{a.valor}</span><span className="tl__cuenta">{d.cantidad} evento(s)</span>
                                  </button>
                                  {abiertos.has(kd) && (
                                    <div className="tl__dia">
                                      <div className="tl__horas">
                                        <label>Desde las
                                          <select value={est?.horaDesde ?? 0} onChange={(e) => {
                                            const hd = Number(e.target.value);
                                            void cargarEventos(kd, a.valor, m.valor, d.valor, hd, Math.max(hd + 1, est?.horaHasta ?? 24), 1);
                                          }}>
                                            {Array.from({ length: 24 }, (_, h) => (<option key={h} value={h}>{p2(h)}:00</option>))}
                                          </select>
                                        </label>
                                        <label>hasta las
                                          <select value={est?.horaHasta ?? 24} onChange={(e) => void cargarEventos(kd, a.valor, m.valor, d.valor, est?.horaDesde ?? 0, Number(e.target.value), 1)}>
                                            {Array.from({ length: 24 }, (_, h) => h + 1).filter((h) => h > (est?.horaDesde ?? 0)).map((h) => (<option key={h} value={h}>{p2(h)}:00</option>))}
                                          </select>
                                        </label>
                                      </div>
                                      {est?.cargando && !est.datos && <p className="nota">Cargando...</p>}
                                      {est?.datos && est.datos.items.length === 0 && <p className="nota">Sin eventos en ese rango de horas.</p>}
                                      {est?.datos && est.datos.items.length > 0 && (
                                        <table className="tabla tabla--compacta">
                                          <thead><tr><th style={{ width: "2rem" }} /><th>Hora</th><th>Usuario</th><th>Cliente</th><th>Acción</th><th>Resumen</th><th>Gestión</th></tr></thead>
                                          <tbody>
                                            {est.datos.items.map((ev: AuditoriaEvento) => {
                                              const kev = `${kd}|${ev.clave}`;
                                              const ab = detalleAbierto.has(kev);
                                              return (
                                                <Fragment key={ev.clave}>
                                                  <tr style={{ cursor: "pointer" }} onClick={() => alternarDetalle(kev)}>
                                                    <td><button type="button" className="boton-secundario" aria-expanded={ab} style={{ padding: "0 0.4rem" }}
                                                      onClick={(e) => { e.stopPropagation(); alternarDetalle(kev); }}>{ab ? "▾" : "▸"}</button></td>
                                                    <td>{new Date(ev.fechaHora.endsWith("Z") ? ev.fechaHora : `${ev.fechaHora}Z`).toLocaleTimeString("es-CR")}</td>
                                                    <td>{ev.usuario}</td>
                                                    <td>{ev.llave === String(idInterlocutor) ? "Este cliente" : "Cliente fusionado"}</td>
                                                    <td>{ev.accion}</td>
                                                    <td>{ev.resumen}</td>
                                                    <td onClick={(e) => e.stopPropagation()}>{ev.idGestion ? <button type="button" className="boton-enlace-id" title="Ver el detalle de la gestión" onClick={() => setIdGestionVer(ev.idGestion!)}>{ev.numeroGestion ?? `#${ev.idGestion}`}</button> : "—"}</td>
                                                  </tr>
                                                  {ab && (
                                                    <tr><td /><td colSpan={6}>
                                                      <table className="tabla tabla--compacta" style={{ width: "100%" }}>
                                                        <thead><tr><th>Acción</th><th>Campo</th><th>Valor anterior</th><th>Valor nuevo</th></tr></thead>
                                                        <tbody>
                                                          {ev.detalle.map((x) => (<tr key={x.idLog}><td>{x.accion}</td><td>{x.campo ?? "—"}</td><td>{x.anterior ?? "—"}</td><td>{x.nuevo ?? "—"}</td></tr>))}
                                                        </tbody>
                                                      </table>
                                                    </td></tr>
                                                  )}
                                                </Fragment>
                                              );
                                            })}
                                          </tbody>
                                        </table>
                                      )}
                                      {est?.datos && totalPag > 1 && (
                                        <div className="formulario-en-linea" style={{ alignItems: "center", marginTop: "0.5rem" }}>
                                          <button type="button" className="boton-secundario" disabled={est.pagina <= 1 || est.cargando}
                                            onClick={() => void cargarEventos(kd, a.valor, m.valor, d.valor, est.horaDesde, est.horaHasta, est.pagina - 1)}>‹ Anterior</button>
                                          <span>Página {est.pagina} de {totalPag}</span>
                                          <button type="button" className="boton-secundario" disabled={est.pagina >= totalPag || est.cargando}
                                            onClick={() => void cargarEventos(kd, a.valor, m.valor, d.valor, est.horaDesde, est.horaHasta, est.pagina + 1)}>Siguiente ›</button>
                                        </div>
                                      )}
                                    </div>
                                  )}
                                </li>
                              );
                            })}
                          </ul>
                        )}
                      </li>
                    );
                  })}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
      {idGestionVer !== null && <GestionDetalleModal idGestion={idGestionVer} onCerrar={() => setIdGestionVer(null)} onResuelta={() => setIdGestionVer(null)} />}
    </div>
  );
}
