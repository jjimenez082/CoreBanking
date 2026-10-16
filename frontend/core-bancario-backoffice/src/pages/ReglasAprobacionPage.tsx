import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useState } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import type {
  ApiErrorBody, CatalogoItemResponse, CondicionRegla, GrupoCatalogo, NivelRegla, ReglaAdmin, SimulacionResultado, TipoGestionAdmin,
} from "../api/tipos";

interface UsuarioItem { id: number; nombre: string; username?: string | null }
interface UnidadItem { id: number; ruta: string }
interface Opcion { valor: string; texto: string }

const VARIABLES: { codigo: string; nombre: string }[] = [
  { codigo: "CANAL", nombre: "Canal" },
  { codigo: "UNIDAD", nombre: "Unidad del solicitante" },
  { codigo: "USUARIO", nombre: "Solicitante" },
  { codigo: "PUESTO", nombre: "Puesto del solicitante" },
  { codigo: "TIPO_ACTOR", nombre: "Tipo de actor" },
];
const OPERADORES = [
  { codigo: "IGUAL", nombre: "es" },
  { codigo: "DIFERENTE", nombre: "no es" },
  { codigo: "EN", nombre: "es uno de" },
];
const TIPOS_NIVEL = [
  { codigo: "SUPERIOR", nombre: "Superior del solicitante" },
  { codigo: "USUARIO", nombre: "Usuario específico" },
  { codigo: "GRUPO", nombre: "Grupo de distribución" },
  { codigo: "AUTOMATICA", nombre: "Aprobación automática" },
];
const TIPOS_ACTOR: Opcion[] = [{ valor: "PERSONA", texto: "Persona (backoffice)" }, { valor: "INTEGRACION", texto: "Integración (API)" }];

interface FormRegla { idRegla: number | null; nombre: string; condiciones: CondicionRegla[]; niveles: NivelRegla[] }

function mensaje(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

/**
 * Administración › Reglas de aprobación (permiso GES_REGLAS).
 * Por tipo de gestión: reglas ordenadas por prioridad. Gana la primera ACTIVA cuyas condiciones se cumplan todas; sus niveles
 * definen quién aprueba. Si ninguna aplica, se usa un nivel con el superior directo. Los cambios solo afectan gestiones nuevas.
 */
export function ReglasAprobacionPage() {
  const api = useApiClient();
  const [tipos, setTipos] = useState<TipoGestionAdmin[]>([]);
  const [idTipo, setIdTipo] = useState<number>(0);
  const [reglas, setReglas] = useState<ReglaAdmin[]>([]);
  const [canales, setCanales] = useState<CatalogoItemResponse[]>([]);
  const [unidades, setUnidades] = useState<UnidadItem[]>([]);
  const [usuarios, setUsuarios] = useState<UsuarioItem[]>([]);
  const [grupos, setGrupos] = useState<GrupoCatalogo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [form, setForm] = useState<FormRegla | null>(null);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [sim, setSim] = useState<{ idUsuario: number; canal: string; resultado: SimulacionResultado | null; error: string | null } | null>(null);

  const tipo = tipos.find((t) => t.idTipoGestion === idTipo) ?? null;

  const cargarTipos = useCallback(async () => {
    try {
      const r = await api.get<TipoGestionAdmin[]>("/api/aprobacion/tipos");
      setTipos(r.data);
      setIdTipo((actual) => actual || r.data[0]?.idTipoGestion || 0);
    } catch (e) { setError(mensaje(e, "No se pudieron cargar los tipos de gestión.")); }
  }, [api]);

  const cargarReglas = useCallback(async (id: number) => {
    if (!id) { setReglas([]); return; }
    try {
      const r = await api.get<ReglaAdmin[]>("/api/aprobacion/reglas", { params: { idTipoGestion: id } });
      setReglas(r.data);
    } catch (e) { setError(mensaje(e, "No se pudieron cargar las reglas.")); }
  }, [api]);

  const cargarCatalogos = useCallback(async () => {
    try {
      const [c, un, us, g] = await Promise.all([
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/CANAL"),
        api.get<UnidadItem[]>("/api/catalogos/unidades"),
        api.get<UsuarioItem[]>("/api/catalogos/usuarios"),
        api.get<GrupoCatalogo[]>("/api/aprobacion/grupos-activos"),
      ]);
      setCanales(c.data); setUnidades(un.data); setUsuarios(us.data); setGrupos(g.data);
    } catch (e) { setError(mensaje(e, "No se pudieron cargar los catálogos.")); }
  }, [api]);

  useEffect(() => { void cargarTipos(); void cargarCatalogos(); }, [cargarTipos, cargarCatalogos]);
  useEffect(() => { setForm(null); setSim(null); void cargarReglas(idTipo); }, [idTipo, cargarReglas]);

  async function refrescar() { await Promise.all([cargarTipos(), cargarReglas(idTipo)]); }

  async function accion(fn: () => Promise<unknown>, ok: string, fallo: string) {
    setError(null); setExito(null);
    try { await fn(); setExito(ok); await refrescar(); } catch (e) { setError(mensaje(e, fallo)); }
  }

  const cambiarRepetir = (v: boolean) =>
    accion(() => api.put(`/api/aprobacion/tipos/${idTipo}/permite-repetir`, { permiteRepetir: v }), "Parámetro actualizado.", "No se pudo actualizar el parámetro.");

  function mover(idx: number, delta: number) {
    const ids = reglas.map((r) => r.idRegla);
    const j = idx + delta;
    if (j < 0 || j >= ids.length) return;
    [ids[idx], ids[j]] = [ids[j], ids[idx]];
    void accion(() => api.put("/api/aprobacion/reglas/orden", { idTipoGestion: idTipo, idsReglas: ids }), "Orden actualizado.", "No se pudo reordenar.");
  }
  const cambiarEstado = (r: ReglaAdmin) =>
    accion(() => api.put(`/api/aprobacion/reglas/${r.idRegla}/estado`, { estado: r.estado === "Activo" ? "Inactivo" : "Activo" }),
      r.estado === "Activo" ? "Regla inactivada." : "Regla activada.", "No se pudo cambiar el estado.");
  function eliminar(r: ReglaAdmin) {
    if (!window.confirm(`¿Eliminar la regla «${r.nombre}»?`)) return;
    void accion(() => api.delete(`/api/aprobacion/reglas/${r.idRegla}`), "Regla eliminada.", "No se pudo eliminar la regla.");
  }

  function opcionesDe(variable: string): Opcion[] | null {
    switch (variable) {
      case "CANAL": return canales.map((c) => ({ valor: c.codigo, texto: c.nombre }));
      case "UNIDAD": return unidades.map((u) => ({ valor: String(u.id), texto: u.ruta }));
      case "USUARIO": return usuarios.map((u) => ({ valor: String(u.id), texto: u.nombre }));
      case "TIPO_ACTOR": return TIPOS_ACTOR;
      default: return null;
    }
  }

  function nuevaRegla() {
    setExito(null); setErrorForm(null);
    setForm({ idRegla: null, nombre: "", condiciones: [], niveles: [{ tipo: "SUPERIOR" }] });
  }
  function editar(r: ReglaAdmin) {
    setExito(null); setErrorForm(null);
    setForm({
      idRegla: r.idRegla, nombre: r.nombre,
      condiciones: r.condiciones.map((c) => ({ variable: c.variable, operador: c.operador, valor: c.valor })),
      niveles: r.niveles.map((n) => ({ tipo: n.tipo, idUsuario: n.idUsuario ?? null, idGrupo: n.idGrupo ?? null })),
    });
  }

  async function guardar() {
    if (!form) return;
    setGuardando(true); setErrorForm(null);
    const cuerpo = {
      idTipoGestion: idTipo, nombre: form.nombre,
      condiciones: form.condiciones,
      niveles: form.niveles.map((n) => ({ tipo: n.tipo, idUsuario: n.tipo === "USUARIO" ? n.idUsuario : null, idGrupo: n.tipo === "GRUPO" ? n.idGrupo : null })),
    };
    try {
      if (form.idRegla) await api.put(`/api/aprobacion/reglas/${form.idRegla}`, cuerpo);
      else await api.post("/api/aprobacion/reglas", cuerpo);
      setExito(form.idRegla ? "Regla actualizada. Aplica a las gestiones nuevas." : "Regla creada. Quedó antes de la regla general; use ↑ ↓ para ajustar su prioridad.");
      setForm(null); await refrescar();
    } catch (e) { setErrorForm(mensaje(e, "No se pudo guardar la regla.")); }
    finally { setGuardando(false); }
  }

  function setCond(i: number, parcial: Partial<CondicionRegla>) {
    setForm((f) => f && ({ ...f, condiciones: f.condiciones.map((c, k) => (k === i ? { ...c, ...parcial } : c)) }));
  }
  function setNivel(i: number, parcial: Partial<NivelRegla>) {
    setForm((f) => f && ({ ...f, niveles: f.niveles.map((n, k) => (k === i ? { ...n, ...parcial } : n)) }));
  }
  function moverNivel(i: number, d: number) {
    setForm((f) => {
      if (!f) return f;
      const j = i + d; if (j < 0 || j >= f.niveles.length) return f;
      const n = [...f.niveles]; [n[i], n[j]] = [n[j], n[i]];
      return { ...f, niveles: n };
    });
  }

  function valorCondicion(c: CondicionRegla, i: number) {
    const ops = opcionesDe(c.variable);
    if (!ops) {
      return <input value={c.valor} maxLength={500} placeholder={c.operador === "EN" ? "Separe con comas" : "Valor"} onChange={(e) => setCond(i, { valor: e.target.value })} />;
    }
    const actuales = c.valor.split(",").map((x) => x.trim()).filter(Boolean);
    if (c.operador === "EN") {
      return (
        <select multiple size={Math.min(5, Math.max(3, ops.length))} value={actuales}
          onChange={(e) => setCond(i, { valor: Array.from(e.target.selectedOptions).map((o) => o.value).join(",") })}>
          {ops.map((o) => <option key={o.valor} value={o.valor}>{o.texto}</option>)}
        </select>
      );
    }
    return (
      <select value={actuales[0] ?? ""} onChange={(e) => setCond(i, { valor: e.target.value })}>
        <option value="">— Seleccione —</option>
        {ops.map((o) => <option key={o.valor} value={o.valor}>{o.texto}</option>)}
      </select>
    );
  }

  async function probar() {
    if (!sim || !sim.idUsuario || !sim.canal) { setSim((s) => s && ({ ...s, error: "Seleccione el solicitante y el canal." })); return; }
    try {
      const r = await api.post<SimulacionResultado>("/api/aprobacion/reglas/simular", { idTipoGestion: idTipo, idUsuario: sim.idUsuario, canal: sim.canal });
      setSim({ ...sim, resultado: r.data, error: null });
    } catch (e) { setSim({ ...sim, resultado: null, error: mensaje(e, "No se pudo simular.") }); }
  }

  const resumenCond = (r: ReglaAdmin) =>
    r.condiciones.length === 0 ? "Siempre (sin condiciones)"
      : r.condiciones.map((c) => `${VARIABLES.find((v) => v.codigo === c.variable)?.nombre ?? c.variable} ${OPERADORES.find((o) => o.codigo === c.operador)?.nombre ?? c.operador} ${c.valorTexto ?? c.valor}`).join("  Y  ");

  return (
    <div>
      <h1>Reglas de aprobación</h1>
      <p className="nota">
        Para cada tipo de gestión, las reglas se evalúan en orden: gana la primera <strong>activa</strong> cuyas condiciones se cumplan todas.
        Si ninguna aplica se usa un nivel con el superior directo. Los cambios solo afectan a las gestiones que se creen desde ahora.
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <div className="formulario" style={{ maxWidth: "46rem" }}>
        <label>Tipo de gestión
          <select value={idTipo} onChange={(e) => setIdTipo(Number(e.target.value))}>
            {tipos.map((t) => (
              <option key={t.idTipoGestion} value={t.idTipoGestion}>{t.nombre} ({t.reglasActivas} activa{t.reglasActivas === 1 ? "" : "s"})</option>
            ))}
          </select>
        </label>
        {tipo && (
          <label style={{ flexDirection: "row", display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <input type="checkbox" checked={tipo.permiteRepetir} onChange={(e) => void cambiarRepetir(e.target.checked)} />
            Permitir que un mismo usuario apruebe más de un nivel de esta gestión
          </label>
        )}
      </div>

      <div style={{ margin: "0.75rem 0", display: "flex", gap: "0.5rem" }}>
        <button type="button" onClick={nuevaRegla} disabled={!idTipo}>+ Nueva regla</button>
        <button type="button" className="boton-secundario" disabled={!idTipo}
          onClick={() => setSim({ idUsuario: 0, canal: canales[0]?.codigo ?? "", resultado: null, error: null })}>Probar regla</button>
      </div>

      {reglas.length === 0 && <p className="nota">Este tipo de gestión no tiene reglas: se usa el superior directo.</p>}
      {reglas.map((r, i) => (
        <section key={r.idRegla} className="panel-formulario" style={{ opacity: r.estado === "Activo" ? 1 : 0.6 }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: "1rem", flexWrap: "wrap" }}>
            <h3 style={{ margin: 0 }}>{i + 1}. {r.nombre} <span className="nota">({r.estado}{r.gestionesUsandola > 0 ? ` · ${r.gestionesUsandola} gestión(es)` : ""})</span></h3>
            <div style={{ display: "flex", gap: "0.35rem", flexWrap: "wrap" }}>
              <button type="button" className="boton-secundario" disabled={i === 0} title="Subir prioridad" onClick={() => mover(i, -1)}>↑</button>
              <button type="button" className="boton-secundario" disabled={i === reglas.length - 1} title="Bajar prioridad" onClick={() => mover(i, 1)}>↓</button>
              <button type="button" className="boton-secundario" onClick={() => editar(r)}>Editar</button>
              <button type="button" className="boton-secundario" onClick={() => void cambiarEstado(r)}>{r.estado === "Activo" ? "Inactivar" : "Activar"}</button>
              <button type="button" className="boton-secundario" disabled={r.gestionesUsandola > 0} title={r.gestionesUsandola > 0 ? "Ya fue usada: inactívela" : "Eliminar"} onClick={() => eliminar(r)}>Eliminar</button>
            </div>
          </div>
          <p style={{ margin: "0.4rem 0 0.2rem" }}><strong>Cuando:</strong> {resumenCond(r)}</p>
          <p style={{ margin: 0 }}><strong>Aprueban:</strong> {r.niveles.map((n) => `${n.nivel}. ${n.destino}`).join("  →  ")}</p>
        </section>
      ))}

      {sim && (
        <PanelModal onCerrar={() => setSim(null)}>
          <h3>Probar regla — {tipo?.nombre}</h3>
          <p className="nota">Simula quién aprobaría una gestión de este tipo, sin crear nada.</p>
          <div className="formulario">
            <label>Solicitante
              <select value={sim.idUsuario} onChange={(e) => setSim({ ...sim, idUsuario: Number(e.target.value), resultado: null })}>
                <option value={0}>— Seleccione —</option>
                {usuarios.map((u) => <option key={u.id} value={u.id}>{u.nombre}</option>)}
              </select>
            </label>
            <label>Canal
              <select value={sim.canal} onChange={(e) => setSim({ ...sim, canal: e.target.value, resultado: null })}>
                {canales.map((c) => <option key={c.codigo} value={c.codigo}>{c.nombre}</option>)}
              </select>
            </label>
          </div>
          <div className="panel-formulario__acciones">
            <button type="button" onClick={() => void probar()}>Simular</button>
            <button type="button" className="boton-secundario" onClick={() => setSim(null)}>Cerrar</button>
          </div>
          {sim.error && <p className="mensaje-error">{sim.error}</p>}
          {sim.resultado && (
            <div style={{ marginTop: "0.75rem" }}>
              <p><strong>{sim.resultado.resumen}</strong></p>
              {sim.resultado.reglas.map((r) => (
                <div key={r.idRegla} style={{ margin: "0.3rem 0" }}>
                  <span>{r.idRegla === sim.resultado!.idReglaAplicada ? "✅" : r.aplica ? "➖" : "❌"} {r.nombre}</span>
                  {r.idRegla !== sim.resultado!.idReglaAplicada && r.aplica && <span className="nota"> (también cumple, pero gana una regla anterior)</span>}
                  <ul style={{ margin: "0.1rem 0 0 1.2rem" }}>
                    {r.condiciones.length === 0 ? <li>Sin condiciones</li> : r.condiciones.map((c, k) => <li key={k}>{c.cumple ? "✓" : "✗"} {c.texto}</li>)}
                  </ul>
                </div>
              ))}
              <p style={{ marginBottom: 0 }}><strong>Aprobadores de {sim.resultado.solicitante}:</strong></p>
              <ol style={{ marginTop: "0.2rem" }}>
                {sim.resultado.niveles.map((n) => (
                  <li key={n.nivel}>{n.aprobador}{n.nota ? <span className="nota"> — {n.nota}</span> : null}</li>
                ))}
              </ol>
            </div>
          )}
        </PanelModal>
      )}

      {form && (
        <PanelModal onCerrar={() => setForm(null)}>
          <h3>{form.idRegla ? "Editar regla" : "Nueva regla"} — {tipo?.nombre}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <div className="formulario">
            <label>Nombre de la regla
              <input value={form.nombre} maxLength={200} onChange={(e) => setForm({ ...form, nombre: e.target.value })} />
            </label>
          </div>

          <h4>Condiciones <span className="nota">(todas deben cumplirse; sin condiciones = aplica siempre)</span></h4>
          {form.condiciones.map((c, i) => (
            <div key={i} style={{ display: "flex", gap: "0.5rem", alignItems: "flex-start", margin: "0.35rem 0", flexWrap: "wrap" }}>
              <select value={c.variable} onChange={(e) => setCond(i, { variable: e.target.value, valor: "" })}>
                {VARIABLES.map((v) => <option key={v.codigo} value={v.codigo}>{v.nombre}</option>)}
              </select>
              <select value={c.operador} onChange={(e) => setCond(i, { operador: e.target.value, valor: "" })}>
                {OPERADORES.map((o) => <option key={o.codigo} value={o.codigo}>{o.nombre}</option>)}
              </select>
              {valorCondicion(c, i)}
              <button type="button" className="boton-secundario" onClick={() => setForm({ ...form, condiciones: form.condiciones.filter((_, k) => k !== i) })}>Quitar</button>
            </div>
          ))}
          <button type="button" className="boton-secundario"
            onClick={() => setForm({ ...form, condiciones: [...form.condiciones, { variable: "CANAL", operador: "IGUAL", valor: "" }] })}>+ Agregar condición</button>

          <h4>Niveles de aprobación <span className="nota">(en orden; máximo 10)</span></h4>
          {form.niveles.map((n, i) => (
            <div key={i} style={{ display: "flex", gap: "0.5rem", alignItems: "center", margin: "0.35rem 0", flexWrap: "wrap" }}>
              <strong>Nivel {i + 1}</strong>
              <select value={n.tipo} onChange={(e) => setNivel(i, { tipo: e.target.value, idUsuario: null, idGrupo: null })}>
                {TIPOS_NIVEL.map((t) => <option key={t.codigo} value={t.codigo}>{t.nombre}</option>)}
              </select>
              {n.tipo === "USUARIO" && (
                <select value={n.idUsuario ?? 0} onChange={(e) => setNivel(i, { idUsuario: Number(e.target.value) || null })}>
                  <option value={0}>— Usuario —</option>
                  {usuarios.map((u) => <option key={u.id} value={u.id}>{u.nombre}</option>)}
                </select>
              )}
              {n.tipo === "GRUPO" && (
                <select value={n.idGrupo ?? 0} onChange={(e) => setNivel(i, { idGrupo: Number(e.target.value) || null })}>
                  <option value={0}>— Grupo —</option>
                  {grupos.map((g) => <option key={g.idGrupo} value={g.idGrupo}>{g.nombre}</option>)}
                </select>
              )}
              <button type="button" className="boton-secundario" disabled={i === 0} onClick={() => moverNivel(i, -1)}>↑</button>
              <button type="button" className="boton-secundario" disabled={i === form.niveles.length - 1} onClick={() => moverNivel(i, 1)}>↓</button>
              <button type="button" className="boton-secundario" disabled={form.niveles.length === 1} onClick={() => setForm({ ...form, niveles: form.niveles.filter((_, k) => k !== i) })}>Quitar</button>
            </div>
          ))}
          <button type="button" className="boton-secundario" disabled={form.niveles.length >= 10}
            onClick={() => setForm({ ...form, niveles: [...form.niveles, { tipo: "SUPERIOR" }] })}>+ Agregar nivel</button>

          <div className="panel-formulario__acciones">
            <button type="button" disabled={guardando} onClick={() => void guardar()}>{guardando ? "Guardando..." : "Guardar"}</button>
            <button type="button" className="boton-secundario" onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </PanelModal>
      )}
    </div>
  );
}
