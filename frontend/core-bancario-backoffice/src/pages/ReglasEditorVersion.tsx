import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { VentanaModal } from "../components/VentanaModal";
import { mensajeError } from "../api/mensajes";
import type { ConjuntoReglas, ReglaAccion, ReglaCondicion, ReglaGrupoDetalle, ReglasVersionDetalle } from "../api/reglas";

interface GrupoE { operador: "Y" | "O"; miembros: { idCondicion: number; negar: boolean }[]; subgrupos: GrupoE[] }
interface ReglaE { clave: string; codigo: string; nombre: string; descripcion: string; activa: boolean; grupo: GrupoE; acciones: number[] }

const MAX_REGLAS = 50, MAX_PROFUNDIDAD = 4, MAX_CONDICIONES = 20;
const grupoVacio = (): GrupoE => ({ operador: "Y", miembros: [], subgrupos: [] });
const aGrupoE = (g: ReglaGrupoDetalle | null | undefined): GrupoE => g
  ? { operador: g.operador === "O" ? "O" : "Y", miembros: g.miembros.map((m) => ({ idCondicion: m.idCondicion, negar: m.negar })), subgrupos: g.subgrupos.map(aGrupoE) }
  : grupoVacio();
const contar = (g: GrupoE): number => g.miembros.length + g.subgrupos.reduce((s, x) => s + contar(x), 0);
let contadorClave = 0;
const nuevaClave = () => `r${++contadorClave}`;

/** Editor de las reglas de una versión: en Borrador se edita; en cualquier otro estado solo se consulta. */
export function EditorReglasVersion({ idVersion, conjunto, puedeEditar, onCerrar }: { idVersion: number; conjunto: ConjuntoReglas; puedeEditar: boolean; onCerrar: () => void }) {
  const api = useApiClient();
  const [det, setDet] = useState<ReglasVersionDetalle | null>(null);
  const [reglas, setReglas] = useState<ReglaE[]>([]);
  const [original, setOriginal] = useState("");
  const [conds, setConds] = useState<ReglaCondicion[]>([]);
  const [accs, setAccs] = useState<ReglaAccion[]>([]);
  const [comentario, setComentario] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    try {
      const [d, c, a] = await Promise.all([
        api.get<ReglasVersionDetalle>(`/api/reglas/versiones/${idVersion}`), api.get<ReglaCondicion[]>("/api/reglas/condiciones"), api.get<ReglaAccion[]>("/api/reglas/acciones"),
      ]);
      const lista: ReglaE[] = d.data.reglas.map((r) => ({ clave: nuevaClave(), codigo: r.codigo, nombre: r.nombre, descripcion: r.descripcion ?? "", activa: r.activa, grupo: aGrupoE(r.condiciones), acciones: r.acciones.map((x) => x.idAccion) }));
      setDet(d.data); setReglas(lista); setOriginal(JSON.stringify(lista.map(({ clave: _c, ...x }) => x))); setComentario(d.data.version.comentario ?? "");
      setConds(c.data); setAccs(a.data);
    } catch (e) { setError(mensajeError(e, "No se pudo cargar la versión.")); }
  }, [api, idVersion]);
  useEffect(() => { void cargar(); }, [cargar]);

  const editable = puedeEditar && det?.version.estado === "Borrador";
  const sucio = useMemo(() => JSON.stringify(reglas.map(({ clave: _c, ...x }) => x)) !== original, [reglas, original]);
  const mapaCond = useMemo(() => new Map(conds.map((c) => [c.idCondicion, c])), [conds]);
  const mapaAcc = useMemo(() => new Map(accs.map((a) => [a.idAccion, a])), [accs]);

  const cambiar = (clave: string, cambio: Partial<ReglaE>) => setReglas((s) => s.map((r) => (r.clave === clave ? { ...r, ...cambio } : r)));
  function mover(i: number, d: -1 | 1) {
    setReglas((s) => { const n = [...s]; const j = i + d; if (j < 0 || j >= n.length) return s; [n[i], n[j]] = [n[j], n[i]]; return n; });
  }
  function agregar() {
    setReglas((s) => {
      let n = s.length + 1; while (s.some((r) => r.codigo === `R${n}`)) n++;
      return [...s, { clave: nuevaClave(), codigo: `R${n}`, nombre: "", descripcion: "", activa: true, grupo: grupoVacio(), acciones: [] }];
    });
  }

  async function guardar() {
    setGuardando(true); setError(null); setExito(null);
    const grupo = (g: GrupoE): unknown => ({ operador: g.operador, miembros: g.miembros, subgrupos: g.subgrupos.map(grupo) });
    try {
      await api.put(`/api/reglas/versiones/${idVersion}/reglas`, {
        reglas: reglas.map((r) => ({ codigo: r.codigo.trim(), nombre: r.nombre.trim(), descripcion: r.descripcion.trim() || null, activa: r.activa, condiciones: grupo(r.grupo), acciones: r.acciones })), comentario,
      });
      setExito("Reglas guardadas."); await cargar();
    } catch (e) { setError(mensajeError(e, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  function cerrar() { if (editable && sucio && !window.confirm("Hay cambios sin guardar. ¿Cerrar de todos modos?")) return; onCerrar(); }

  const v = det?.version;
  const estadoTxt = v ? (v.estado === "Revision" ? "en revisión" : v.estado) : "";
  return (
    <VentanaModal titulo={`Reglas · ${conjunto.codigo} v${v?.numero ?? ""}${v ? ` (${estadoTxt})` : ""}`} onCerrar={cerrar} ancho={1000}
      acciones={editable ? <button type="button" onClick={() => void guardar()} disabled={guardando || !sucio}>{guardando ? "Guardando..." : "Guardar reglas"}</button> : undefined}>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      {!det && !error && <p>Cargando…</p>}
      {det && (
        <>
          <p className="nota">
            {conjunto.politicaResolucion === "PrimeraCoincidencia"
              ? "Se aplica la primera regla (de arriba hacia abajo) que se cumple. Una regla sin condiciones es la de por defecto y debe ir al final."
              : "Se aplican todas las reglas que se cumplen; si las decisiones difieren prevalece Rechazar sobre Referir y Referir sobre Aprobar."}
            {editable && <> Las condiciones y acciones se crean antes en <Link to="/reglas/condiciones">Condiciones</Link> y <Link to="/reglas/acciones">Acciones</Link>.</>}
          </p>
          {reglas.length === 0 && <p>Esta versión aún no tiene reglas.</p>}
          {reglas.map((r, i) => (
            <section key={r.clave} className="regla-tarjeta" style={{ border: "1px solid var(--color-borde)", borderRadius: 8, padding: "0.7rem 0.9rem", marginBottom: "0.8rem", opacity: r.activa ? 1 : 0.65 }}>
              <header style={{ display: "flex", gap: "0.5rem", alignItems: "center", flexWrap: "wrap" }}>
                <strong style={{ minWidth: "1.5rem" }}>{i + 1}.</strong>
                {editable ? (
                  <>
                    <input type="text" value={r.codigo} maxLength={50} onChange={(e) => cambiar(r.clave, { codigo: e.target.value })} aria-label="Código de la regla" style={{ width: "7rem" }} />
                    <input type="text" value={r.nombre} maxLength={150} placeholder="Nombre de la regla" onChange={(e) => cambiar(r.clave, { nombre: e.target.value })} aria-label="Nombre de la regla" style={{ flex: 1, minWidth: "12rem" }} />
                    <label style={{ display: "flex", gap: "0.3rem", alignItems: "center" }}><input type="checkbox" checked={r.activa} onChange={(e) => cambiar(r.clave, { activa: e.target.checked })} /> Activa</label>
                    <button type="button" className="boton-secundario" onClick={() => mover(i, -1)} disabled={i === 0} aria-label="Subir">↑</button>
                    <button type="button" className="boton-secundario" onClick={() => mover(i, 1)} disabled={i === reglas.length - 1} aria-label="Bajar">↓</button>
                    <button type="button" className="boton-secundario" onClick={() => setReglas((s) => s.filter((x) => x.clave !== r.clave))} aria-label="Quitar regla">🗑</button>
                  </>
                ) : (
                  <span><strong>{r.codigo}</strong> · {r.nombre}{!r.activa && " (inactiva)"}</span>
                )}
              </header>
              <h5 style={{ margin: "0.7rem 0 0.3rem" }}>Cuando</h5>
              {contar(r.grupo) === 0 && <p className="nota" style={{ margin: 0 }}>Sin condiciones: esta regla siempre se cumple (regla por defecto).</p>}
              <GrupoEditor grupo={r.grupo} onChange={(g) => cambiar(r.clave, { grupo: g })} conds={conds} mapa={mapaCond} editable={!!editable} nivel={1} esRaiz />
              <h5 style={{ margin: "0.7rem 0 0.3rem" }}>Entonces</h5>
              {r.acciones.length === 0 && <p className="nota" style={{ margin: 0 }}>Sin acciones.</p>}
              {r.acciones.map((idA, k) => {
                const a = mapaAcc.get(idA);
                return (
                  <div key={`${idA}-${k}`} style={{ display: "flex", gap: "0.5rem", alignItems: "center", marginBottom: "0.25rem" }}>
                    {editable ? (
                      <select value={idA} onChange={(e) => cambiar(r.clave, { acciones: r.acciones.map((x, j) => (j === k ? Number(e.target.value) : x)) })} style={{ flex: 1 }}>
                        {accs.filter((x) => x.estado === "Activo" || x.idAccion === idA).map((x) => (<option key={x.idAccion} value={x.idAccion}>{x.codigo} — {x.resumen}</option>))}
                      </select>
                    ) : <span>{a ? `${a.codigo} — ${a.resumen}` : `Acción ${idA}`}</span>}
                    {a?.estado === "Inactivo" && <small style={{ color: "#b45309" }}>inactiva</small>}
                    {editable && <button type="button" className="boton-secundario" onClick={() => cambiar(r.clave, { acciones: r.acciones.filter((_, j) => j !== k) })} aria-label="Quitar acción">✕</button>}
                  </div>
                );
              })}
              {editable && (
                <button type="button" className="boton-secundario" disabled={accs.filter((x) => x.estado === "Activo" && !r.acciones.includes(x.idAccion)).length === 0}
                  onClick={() => { const libre = accs.find((x) => x.estado === "Activo" && !r.acciones.includes(x.idAccion)); if (libre) cambiar(r.clave, { acciones: [...r.acciones, libre.idAccion] }); }}>+ Acción</button>
              )}
            </section>
          ))}
          {editable && (
            <div style={{ display: "flex", gap: "0.6rem", alignItems: "center", flexWrap: "wrap" }}>
              <button type="button" onClick={agregar} disabled={reglas.length >= MAX_REGLAS}>+ Nueva regla</button>
              <label style={{ flex: 1, minWidth: "16rem" }}>Comentario de la versión <input type="text" maxLength={500} value={comentario} onChange={(e) => setComentario(e.target.value)} style={{ width: "100%" }} /></label>
            </div>
          )}
          {editable && <p className="nota">Límites: {MAX_REGLAS} reglas por versión, {MAX_PROFUNDIDAD} niveles de grupos y {MAX_CONDICIONES} condiciones por regla.</p>}
        </>
      )}
    </VentanaModal>
  );
}

function GrupoEditor({ grupo, onChange, conds, mapa, editable, nivel, esRaiz, onQuitar }: {
  grupo: GrupoE; onChange: (g: GrupoE) => void; conds: ReglaCondicion[]; mapa: Map<number, ReglaCondicion>; editable: boolean; nivel: number; esRaiz?: boolean; onQuitar?: () => void;
}) {
  const total = contar(grupo);
  const hayTodasEnUso = (idActual?: number) => conds.filter((c) => (c.estado === "Activo" || c.idCondicion === idActual) && !grupo.miembros.some((m) => m.idCondicion === c.idCondicion && m.idCondicion !== idActual));
  const sinContenido = grupo.miembros.length === 0 && grupo.subgrupos.length === 0;
  if (!editable && sinContenido) return null;

  return (
    <div style={{ borderLeft: esRaiz ? "none" : "3px solid #c7d2fe", paddingLeft: esRaiz ? 0 : "0.7rem", margin: esRaiz ? 0 : "0.4rem 0" }}>
      {(grupo.miembros.length + grupo.subgrupos.length > 1 || !esRaiz) && (
        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center", marginBottom: "0.3rem" }}>
          {editable ? (
            <select value={grupo.operador} onChange={(e) => onChange({ ...grupo, operador: e.target.value as "Y" | "O" })}>
              <option value="Y">Se cumplen todas (Y)</option><option value="O">Se cumple alguna (O)</option>
            </select>
          ) : <strong>{grupo.operador === "Y" ? "Se cumplen todas:" : "Se cumple alguna de:"}</strong>}
          {!esRaiz && editable && onQuitar && <button type="button" className="boton-secundario" onClick={onQuitar}>Quitar grupo</button>}
        </div>
      )}
      {grupo.miembros.map((m, i) => {
        const c = mapa.get(m.idCondicion);
        return (
          <div key={`${m.idCondicion}-${i}`} style={{ display: "flex", gap: "0.5rem", alignItems: "center", marginBottom: "0.25rem" }}>
            {editable ? (
              <>
                <label style={{ display: "flex", gap: "0.25rem", alignItems: "center" }} title="Invierte el resultado de la condición"><input type="checkbox" checked={m.negar} onChange={(e) => onChange({ ...grupo, miembros: grupo.miembros.map((x, j) => (j === i ? { ...x, negar: e.target.checked } : x)) })} />NO</label>
                <select value={m.idCondicion} style={{ flex: 1 }} onChange={(e) => onChange({ ...grupo, miembros: grupo.miembros.map((x, j) => (j === i ? { ...x, idCondicion: Number(e.target.value) } : x)) })}>
                  {hayTodasEnUso(m.idCondicion).map((x) => (<option key={x.idCondicion} value={x.idCondicion}>{x.codigo} — {x.resumen}</option>))}
                </select>
                <button type="button" className="boton-secundario" onClick={() => onChange({ ...grupo, miembros: grupo.miembros.filter((_, j) => j !== i) })} aria-label="Quitar condición">✕</button>
              </>
            ) : <span>{m.negar ? "NO " : ""}{c ? `${c.codigo} — ${c.resumen}` : `Condición ${m.idCondicion}`}</span>}
            {c?.estado === "Inactivo" && <small style={{ color: "#b45309" }}>inactiva</small>}
          </div>
        );
      })}
      {grupo.subgrupos.map((s, i) => (
        <GrupoEditor key={i} grupo={s} conds={conds} mapa={mapa} editable={editable} nivel={nivel + 1}
          onChange={(g) => onChange({ ...grupo, subgrupos: grupo.subgrupos.map((x, j) => (j === i ? g : x)) })}
          onQuitar={() => onChange({ ...grupo, subgrupos: grupo.subgrupos.filter((_, j) => j !== i) })} />
      ))}
      {editable && (
        <div style={{ display: "flex", gap: "0.4rem", marginTop: "0.3rem" }}>
          <button type="button" className="boton-secundario" disabled={total >= MAX_CONDICIONES || hayTodasEnUso().length === 0}
            onClick={() => { const libre = hayTodasEnUso().find((c) => c.estado === "Activo"); if (libre) onChange({ ...grupo, miembros: [...grupo.miembros, { idCondicion: libre.idCondicion, negar: false }] }); }}>+ Condición</button>
          <button type="button" className="boton-secundario" disabled={nivel >= MAX_PROFUNDIDAD}
            onClick={() => onChange({ ...grupo, subgrupos: [...grupo.subgrupos, { operador: grupo.operador === "Y" ? "O" : "Y", miembros: [], subgrupos: [] }] })}>+ Grupo</button>
        </div>
      )}
    </div>
  );
}
