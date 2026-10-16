import { Fragment, useCallback, useEffect, useState, type FormEvent } from "react";
import { GestionDetalleModal } from "../components/GestionDetalleModal";
import { useApiClient } from "../api/useApiClient";
import { exportarExcel } from "../utils/exportarExcel";
import { fmtFechaHora, mensajeError } from "../api/mensajes";
import type { AuditoriaEntidad, AuditoriaRegistro, AuditoriaPagina, AuditoriaEvento, AuditoriaEventoPagina } from "../api/tipos";

interface UsuarioItem { id: number; nombre: string }
interface Filtros { entidad: string; llave: string; idUsuario: number; accion: string; desde: string; hasta: string; idGestion: string; texto: string }
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
const MAX_DIAS = 7;
/** Por defecto la bitácora general muestra los últimos 7 días (máximo permitido); buscar por N.º de registro o de gestión no tiene tope. */
function filtrosIniciales(): Filtros {
  const hoy = new Date(); const ini = new Date(); ini.setDate(hoy.getDate() - (MAX_DIAS - 1));
  return { entidad: "", llave: "", idUsuario: 0, accion: "", desde: iso(ini), hasta: iso(hoy), idGestion: "", texto: "" };
}

/**
 * Administración › Consulta de auditoría (permiso AUD_CONSULTA). Bitácora de cambios: quién, cuándo, qué entidad y registro,
 * qué campo cambió (valor anterior y nuevo) y la gestión que lo aprobó. Solo lectura. Las horas se muestran en la zona del navegador.
 */
export function AuditoriaPage() {
  const api = useApiClient();
  const [entidades, setEntidades] = useState<AuditoriaEntidad[]>([]);
  const [usuarios, setUsuarios] = useState<UsuarioItem[]>([]);
  const [borrador, setBorrador] = useState<Filtros>(filtrosIniciales);
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciales);
  const [pagina, setPagina] = useState(1);
  const [datos, setDatos] = useState<AuditoriaEventoPagina | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [idGestionVer, setIdGestionVer] = useState<number | null>(null);
  const [abiertos, setAbiertos] = useState<Set<string>>(new Set());
  const [exportando, setExportando] = useState(false);

  useEffect(() => {
    api.get<AuditoriaEntidad[]>("/api/auditoria/entidades").then((r) => setEntidades(r.data)).catch(() => undefined);
    api.get<UsuarioItem[]>("/api/catalogos/usuarios").then((r) => setUsuarios(r.data)).catch(() => undefined);
  }, [api]);

  const buscar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const r = await api.get<AuditoriaEventoPagina>("/api/auditoria/eventos", {
        params: {
          entidad: filtros.entidad || undefined, llave: filtros.llave || undefined, idUsuario: filtros.idUsuario || undefined,
          accion: filtros.accion || undefined, desde: filtros.desde || undefined, hasta: filtros.hasta || undefined,
          idGestion: filtros.idGestion || undefined, texto: filtros.texto || undefined, pagina, tamanoPagina: 25,
        },
      });
      setDatos(r.data); setAbiertos(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudo consultar la auditoría.")); }
    finally { setCargando(false); }
  }, [api, filtros, pagina]);
  useEffect(() => { void buscar(); }, [buscar]);

  // «Exportar todo»: trae todas las páginas del filtro actual (máximo 5.000 filas).
  const LIMITE_EXPORT = 5000;
  async function obtenerTodo(): Promise<AuditoriaRegistro[]> {
    const todas: AuditoriaRegistro[] = [];
    for (let p = 1; todas.length < LIMITE_EXPORT; p++) {
      const r = await api.get<AuditoriaPagina>("/api/auditoria", {
        params: {
          entidad: filtros.entidad || undefined, llave: filtros.llave || undefined, idUsuario: filtros.idUsuario || undefined,
          accion: filtros.accion || undefined, desde: filtros.desde || undefined, hasta: filtros.hasta || undefined,
          idGestion: filtros.idGestion || undefined, texto: filtros.texto || undefined, pagina: p, tamanoPagina: 200,
        },
      });
      todas.push(...r.data.items);
      if (todas.length >= r.data.total || r.data.items.length === 0) break;
    }
    return todas.slice(0, LIMITE_EXPORT);
  }

  async function exportarTodo() {
    setExportando(true); setError(null);
    try {
      const filas = await obtenerTodo();
      await exportarExcel("auditoria", "Auditoría",
        ["Fecha y hora", "Usuario", "Entidad", "Acción", "Campo", "Valor anterior", "Valor nuevo", "Gestión"],
        filas.map((r) => [fmtFechaHora(r.fechaHora), r.usuario, `${r.entidadNombre} #${r.llave}`, r.accion, r.campo ?? "", r.anterior ?? "", r.nuevo ?? "",
          r.idGestion ? (r.numeroGestion ?? `#${r.idGestion}`) : ""]));
    } catch (e) { setError(mensajeError(e, "No se pudo exportar la auditoría.")); }
    finally { setExportando(false); }
  }

  function alternar(clave: string) {
    setAbiertos((prev) => { const n = new Set(prev); if (n.has(clave)) n.delete(clave); else n.add(clave); return n; });
  }

  function aplicar(e: FormEvent) { e.preventDefault(); setPagina(1); setFiltros(borrador); }
  function limpiar() { const f = filtrosIniciales(); setBorrador(f); setPagina(1); setFiltros(f); }

  const totalPaginas = datos ? Math.max(1, Math.ceil(datos.total / datos.tamanoPagina)) : 1;

  return (
    <div>
      <h1>Consulta de auditoría</h1>
      <p className="nota">
        Bitácora de cambios, un renglón por operación (despliegue la flecha para ver todos los campos). Las altas de registros (por ejemplo una dirección o identificación nueva de un cliente) se muestran como «Alta».
        Las consultas de datos sensibles (cuando alguien pulsa «Ver» en un dato enmascarado) se registran con la acción «Consulta». Aquí el rango máximo es de 7 días (sin tope si busca por N.º de registro o de gestión); el historial completo de un cliente está en la pestaña «Auditoría» de su ficha. Las horas se muestran en su zona horaria.
      </p>
      {error && <p className="mensaje-error">{error}</p>}

      <form className="formulario" onSubmit={aplicar} style={{ marginBottom: "1rem" }}>
        <label>Entidad
          <select value={borrador.entidad} onChange={(e) => setBorrador({ ...borrador, entidad: e.target.value })}>
            <option value="">Todas</option>
            {entidades.map((x) => (<option key={x.codigo} value={x.codigo}>{x.nombre}</option>))}
          </select>
        </label>
        <label>N.º / llave del registro
          <input type="text" value={borrador.llave} onChange={(e) => setBorrador({ ...borrador, llave: e.target.value })} />
        </label>
        <label>Usuario
          <select value={borrador.idUsuario} onChange={(e) => setBorrador({ ...borrador, idUsuario: Number(e.target.value) })}>
            <option value={0}>Todos</option>
            {usuarios.map((u) => (<option key={u.id} value={u.id}>{u.nombre}</option>))}
          </select>
        </label>
        <label>Acción
          <select value={borrador.accion} onChange={(e) => setBorrador({ ...borrador, accion: e.target.value })}>
            <option value="">Todas</option><option>Alta</option><option>Cambio</option><option>Baja</option><option>Consulta</option>
          </select>
        </label>
        <label>Desde
          <input type="date" value={borrador.desde} onChange={(e) => setBorrador({ ...borrador, desde: e.target.value })} />
        </label>
        <label>Hasta
          <input type="date" value={borrador.hasta} onChange={(e) => setBorrador({ ...borrador, hasta: e.target.value })} />
        </label>
        <label>Gestión N.º
          <input type="number" min={1} value={borrador.idGestion} onChange={(e) => setBorrador({ ...borrador, idGestion: e.target.value })} />
        </label>
        <label>Texto (campo o valor)
          <input type="text" value={borrador.texto} onChange={(e) => setBorrador({ ...borrador, texto: e.target.value })} />
        </label>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button type="submit">Buscar</button>
          <button type="button" className="boton-secundario" onClick={limpiar}>Limpiar</button>
        </div>
      </form>

      <div className="tabla-datos__barra" style={{ display: "flex", gap: "0.5rem", marginBottom: "0.5rem" }}>
        <button type="button" className="boton-secundario" disabled={cargando} onClick={() => void buscar()}>↻ Actualizar</button>
        <button type="button" className="boton-secundario" disabled={cargando || exportando} onClick={() => void exportarTodo()}
          title="Exporta todos los campos del filtro, una fila por campo">⇩ {exportando ? "Exportando..." : "Exportar todo (detalle)"}</button>
        {datos && datos.items.length > 0 && (
          <button type="button" className="boton-secundario"
            onClick={() => setAbiertos(abiertos.size === 0 ? new Set(datos.items.map((x) => x.clave)) : new Set())}>
            {abiertos.size === 0 ? "Expandir todo" : "Contraer todo"}
          </button>
        )}
      </div>

      <div className="tabla-datos__scroll">
        <table className="tabla tabla-datos__tabla">
          <thead>
            <tr><th style={{ width: "2rem" }} /><th>Fecha y hora</th><th>Usuario</th><th>Entidad</th><th>Acción</th><th>Resumen</th><th>Gestión</th></tr>
          </thead>
          <tbody>
            {cargando && !datos ? (
              <tr><td colSpan={7} className="nota">Cargando...</td></tr>
            ) : !datos || datos.items.length === 0 ? (
              <tr><td colSpan={7} className="nota">No hay registros de auditoría para el filtro.</td></tr>
            ) : datos.items.map((ev: AuditoriaEvento) => {
              const abierto = abiertos.has(ev.clave);
              return (
                <Fragment key={ev.clave}>
                  <tr style={{ cursor: "pointer" }} onClick={() => alternar(ev.clave)}>
                    <td>
                      <button type="button" className="boton-secundario" aria-expanded={abierto} aria-label={abierto ? "Ocultar detalle" : "Ver detalle"}
                        style={{ padding: "0 0.4rem" }} onClick={(e) => { e.stopPropagation(); alternar(ev.clave); }}>{abierto ? "▾" : "▸"}</button>
                    </td>
                    <td>{fmtFechaHora(ev.fechaHora)}</td>
                    <td>{ev.usuario}</td>
                    <td>{ev.entidadNombre} #{ev.llave}</td>
                    <td>{ev.accion}</td>
                    <td>{ev.resumen}</td>
                    <td onClick={(e) => e.stopPropagation()}>
                      {ev.idGestion ? <button type="button" className="boton-enlace-id" title="Ver el detalle de la gestión" onClick={() => setIdGestionVer(ev.idGestion!)}>{ev.numeroGestion ?? `#${ev.idGestion}`}</button> : "—"}
                    </td>
                  </tr>
                  {abierto && (
                    <tr>
                      <td />
                      <td colSpan={6} style={{ padding: "0.25rem 0.5rem 0.75rem" }}>
                        <table className="tabla" style={{ width: "100%" }}>
                          <thead><tr><th>Acción</th><th>Campo</th><th>Valor anterior</th><th>Valor nuevo</th></tr></thead>
                          <tbody>
                            {ev.detalle.map((d) => (
                              <tr key={d.idLog}>
                                <td>{d.accion}</td><td>{d.campo ?? "—"}</td><td>{d.anterior ?? "—"}</td><td>{d.nuevo ?? "—"}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>

      {datos && (
        <div className="formulario-en-linea" style={{ alignItems: "center", marginTop: "0.5rem" }}>
          <button type="button" className="boton-secundario" disabled={pagina <= 1 || cargando} onClick={() => setPagina(pagina - 1)}>‹ Anterior</button>
          <span>Página {pagina} de {totalPaginas} · {datos.total.toLocaleString("es-CR")} evento(s)</span>
          <button type="button" className="boton-secundario" disabled={pagina >= totalPaginas || cargando} onClick={() => setPagina(pagina + 1)}>Siguiente ›</button>
        </div>
      )}
      {idGestionVer !== null && <GestionDetalleModal idGestion={idGestionVer} onCerrar={() => setIdGestionVer(null)} onResuelta={() => setIdGestionVer(null)} />}
    </div>
  );
}
