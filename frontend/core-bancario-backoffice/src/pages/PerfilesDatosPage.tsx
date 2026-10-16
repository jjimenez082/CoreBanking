import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { PanelModal } from "../components/VentanaModal";
import { mensajeError } from "../api/mensajes";
import type {
  MascaraAdmin, MascaraGuardar, PerfilCatalogo, PerfilDatosDetalle, PerfilDatosGuardar, PerfilDatosResumen, PerfilFiltro, PerfilCampo, PerfilUsuario,
} from "../api/tipos";

type Pestana = "perfiles" | "mascaras";

const TIPOS_MASCARA: Record<string, string> = {
  PARCIAL: "Dejar visibles los primeros y/o últimos caracteres",
  CORREO: "Correo (primera letra y dominio)",
  RANGO: "Rango de magnitud (montos)",
  ANIO: "Solo el año (fechas)",
  OCULTO: "Ocultar todo (••••)",
};

/**
 * Administración › Perfiles de datos (permiso SEG_PERFILES). El ROL decide qué pantallas y opciones ve el usuario; el PERFIL decide qué clientes
 * (filas) ve dentro de ellas y cómo se muestran sus datos sensibles (máscaras). Un usuario sin perfil lo ve todo; con varios perfiles se suman
 * las filas y, por campo, gana la máscara que más revela. Todo cambio queda en la auditoría.
 */
export function PerfilesDatosPage() {
  const [pestana, setPestana] = useState<Pestana>("perfiles");
  return (
    <div>
      <h1>Perfiles de datos y máscaras</h1>
      <p className="nota">
        El <strong>rol</strong> decide qué pantallas ve el usuario; el <strong>perfil de datos</strong> decide qué clientes ve dentro de ellas y con qué
        máscara aparecen sus datos sensibles. Un usuario sin perfil ve todo. Con varios perfiles se suman los clientes visibles y, en cada campo, gana la
        máscara que más revela.
      </p>
      <div className="v360__pestanas" role="tablist">
        {([["perfiles", "Perfiles"], ["mascaras", "Máscaras"]] as [Pestana, string][]).map(([k, t]) => (
          <button key={k} type="button" role="tab" aria-selected={pestana === k}
            className={`v360__pestana ${pestana === k ? "v360__pestana--activa" : ""}`} onClick={() => setPestana(k)}>{t}</button>
        ))}
      </div>
      {pestana === "perfiles" && <Perfiles />}
      {pestana === "mascaras" && <Mascaras />}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Perfiles

interface FormPerfil { codigo: string; nombre: string; descripcion: string; filtros: PerfilFiltro[]; campos: PerfilCampo[] }
const PERFIL_NUEVO: FormPerfil = { codigo: "", nombre: "", descripcion: "", filtros: [], campos: [] };

function Perfiles() {
  const api = useApiClient();
  const [filas, setFilas] = useState<PerfilDatosResumen[]>([]);
  const [cat, setCat] = useState<PerfilCatalogo | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState<FormPerfil>(PERFIL_NUEVO);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [usuariosPerfil, setUsuariosPerfil] = useState<PerfilUsuario[]>([]);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [p, c] = await Promise.all([api.get<PerfilDatosResumen[]>("/api/perfiles-datos"), api.get<PerfilCatalogo>("/api/perfiles-datos/catalogo")]);
      setFilas(p.data); setCat(c.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los perfiles.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<PerfilDatosResumen>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Nombre", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "filas", encabezado: "Clientes que ve", render: (r) => r.filas, textoBusqueda: (r) => r.filas },
    { clave: "campos", encabezado: "Campos enmascarados", render: (r) => r.campos },
    { clave: "usuarios", encabezado: "Usuarios", render: (r) => r.usuarios },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const p = filas.find((f) => f.idPerfilDatos === idSel) ?? null;

  async function abrirEditar() {
    if (!p) return;
    setExito(null); setErrorForm(null);
    try {
      const r = await api.get<PerfilDatosDetalle>(`/api/perfiles-datos/${p.idPerfilDatos}`);
      setForm({ codigo: r.data.codigo, nombre: r.data.nombre, descripcion: r.data.descripcion ?? "", filtros: r.data.filtros, campos: r.data.campos });
      setUsuariosPerfil(r.data.usuarios ?? []);
      setPanel("editar");
    } catch (e) { setError(mensajeError(e, "No se pudo abrir el perfil.")); }
  }

  async function accion(fn: () => Promise<unknown>, ok: string) {
    setExito(null); setError(null);
    try { await fn(); setExito(ok); await cargar(); }
    catch (e) { setError(mensajeError(e, "No se pudo completar la acción.")); }
  }

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const cuerpo: PerfilDatosGuardar = { codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion.trim() || null, filtros: form.filtros, campos: form.campos };
    try {
      if (panel === "nuevo") await api.post("/api/perfiles-datos", cuerpo);
      else if (p) await api.put(`/api/perfiles-datos/${p.idPerfilDatos}`, cuerpo);
      setExito(panel === "nuevo" ? "Perfil creado." : "Perfil actualizado.");
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar el perfil.")); }
    finally { setGuardando(false); }
  }

  // --- edición de reglas de filas
  const atributosLibres = (cat?.atributos ?? []).filter((a) => !form.filtros.some((f) => f.atributo === a.codigo));
  const agregarFiltro = (atributo: string) => {
    const a = cat?.atributos.find((x) => x.codigo === atributo);
    if (!a) return;
    setForm({ ...form, filtros: [...form.filtros, { atributo, modo: "INCLUIR", valores: [] }] });
  };
  const cambiarFiltro = (i: number, parcial: Partial<PerfilFiltro>) => setForm({ ...form, filtros: form.filtros.map((f, k) => (k === i ? { ...f, ...parcial } : f)) });
  const quitarFiltro = (i: number) => setForm({ ...form, filtros: form.filtros.filter((_, k) => k !== i) });
  const alternarValor = (i: number, v: string) => {
    const f = form.filtros[i];
    cambiarFiltro(i, { valores: f.valores.includes(v) ? f.valores.filter((x) => x !== v) : [...f.valores, v] });
  };

  // --- edición de campos enmascarados
  const campoDe = (codigo: string) => form.campos.find((c) => c.campo === codigo);
  const fijarCampo = (codigo: string, idMascara: number | null, permiteRevelar?: boolean) => {
    const resto = form.campos.filter((c) => c.campo !== codigo);
    if (idMascara === null) { setForm({ ...form, campos: resto }); return; }
    const actual = campoDe(codigo);
    setForm({ ...form, campos: [...resto, { campo: codigo, idMascara, permiteRevelar: permiteRevelar ?? actual?.permiteRevelar ?? false }] });
  };

  return (
    <div>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idPerfilDatos}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay perfiles de datos. Use «Nuevo»."
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", onClick: () => { setExito(null); setErrorForm(null); setForm(PERFIL_NUEVO); setPanel("nuevo"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: () => void abrirEditar() },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: p?.estado === "Activo", onClick: () => p && void accion(() => api.put(`/api/perfiles-datos/${p.idPerfilDatos}/estado`, { estado: "Activo" }), "Perfil activado.") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: p?.estado === "Inactivo", onClick: () => p && void accion(() => api.put(`/api/perfiles-datos/${p.idPerfilDatos}/estado`, { estado: "Inactivo" }), "Perfil inactivado: sus usuarios dejan de tener esta restricción.") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
      {panel && cat && (
        <PanelModal onCerrar={() => setPanel(null)} ancho={820}>
          <h3>{panel === "nuevo" ? "Nuevo perfil de datos" : `Editar ${form.codigo}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código
              <input type="text" maxLength={50} value={form.codigo} disabled={panel === "editar"} onChange={(e) => setForm({ ...form, codigo: e.target.value.toUpperCase() })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={100} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            <label style={{ gridColumn: "1 / -1" }}>Descripción
              <input type="text" maxLength={300} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} />
            </label>

            <fieldset style={{ gridColumn: "1 / -1" }}>
              <legend>Clientes que puede ver</legend>
              <p className="nota">Sin reglas, el perfil ve todos los clientes. Con varias reglas deben cumplirse todas.</p>
              {form.filtros.map((f, i) => {
                const a = cat.atributos.find((x) => x.codigo === f.atributo);
                return (
                  <div key={f.atributo} style={{ border: "1px solid var(--color-borde)", borderRadius: 6, padding: "0.6rem", marginBottom: "0.5rem" }}>
                    <div style={{ display: "flex", gap: "0.75rem", alignItems: "center", flexWrap: "wrap" }}>
                      <strong>{a?.etiqueta ?? f.atributo}</strong>
                      <select value={f.modo} onChange={(e) => cambiarFiltro(i, { modo: e.target.value as PerfilFiltro["modo"] })}>
                        <option value="INCLUIR">Ver solo</option>
                        <option value="EXCLUIR">Ver todos excepto</option>
                      </select>
                      <button type="button" className="boton-secundario" onClick={() => quitarFiltro(i)}>Quitar regla</button>
                    </div>
                    <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap", marginTop: "0.4rem" }}>
                      {(a?.valores ?? []).map((v) => (
                        <label key={v.valor} style={{ flexDirection: "row", alignItems: "center", gap: "0.35rem" }}>
                          <input type="checkbox" checked={f.valores.includes(v.valor)} onChange={() => alternarValor(i, v.valor)} /> {v.nombre}
                        </label>
                      ))}
                    </div>
                  </div>
                );
              })}
              {atributosLibres.length > 0 && (
                <select value="" onChange={(e) => e.target.value && agregarFiltro(e.target.value)}>
                  <option value="">+ Agregar regla…</option>
                  {atributosLibres.map((a) => <option key={a.codigo} value={a.codigo}>{a.etiqueta}</option>)}
                </select>
              )}
            </fieldset>

            <fieldset style={{ gridColumn: "1 / -1" }}>
              <legend>Datos sensibles (máscara por campo)</legend>
              <p className="nota">Un campo sin máscara se ve completo. «Permitir ver» agrega un botón <em>Ver</em> que muestra el dato real y deja constancia en la bitácora.</p>
              <table className="tabla tabla--compacta">
                <thead><tr><th>Campo</th><th>Máscara</th><th>Permitir ver</th></tr></thead>
                <tbody>
                  {cat.campos.map((c) => {
                    const actual = campoDe(c.codigo);
                    const opciones = cat.mascaras.filter((m) => c.tiposMascara.includes(m.tipo));
                    return (
                      <tr key={c.codigo}>
                        <td title={c.descripcion}>{c.etiqueta}</td>
                        <td>
                          <select value={actual?.idMascara ?? ""} onChange={(e) => fijarCampo(c.codigo, e.target.value ? Number(e.target.value) : null)}>
                            <option value="">Sin máscara (se ve completo)</option>
                            {opciones.map((m) => <option key={m.idMascara} value={m.idMascara}>{m.nombre} — {m.ejemplo}</option>)}
                          </select>
                        </td>
                        <td>
                          <input type="checkbox" disabled={!actual} checked={actual?.permiteRevelar ?? false}
                            onChange={(e) => actual && fijarCampo(c.codigo, actual.idMascara, e.target.checked)} />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </fieldset>

            {panel === "editar" && (
              <fieldset style={{ gridColumn: "1 / -1" }}>
                <legend>Usuarios con este perfil ({usuariosPerfil.length})</legend>
                <p className="nota">Solo consulta. Los perfiles se asignan desde Usuarios → ficha del usuario → pestaña «Perfiles de datos».</p>
                {usuariosPerfil.length === 0
                  ? <span className="nota">Ningún usuario tiene este perfil.</span>
                  : <ul style={{ margin: 0, paddingLeft: "1.2rem" }}>{usuariosPerfil.map((u) => <li key={u.idUsuario}><strong>{u.codigo || "—"}</strong> · {u.nombre}</li>)}</ul>}
              </fieldset>
            )}

            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------ Máscaras

interface FormMascara { codigo: string; nombre: string; descripcion: string; tipo: string; visiblesInicio: number; visiblesFin: number; caracter: string }
const MASCARA_NUEVA: FormMascara = { codigo: "", nombre: "", descripcion: "", tipo: "PARCIAL", visiblesInicio: 0, visiblesFin: 4, caracter: "*" };

function Mascaras() {
  const api = useApiClient();
  const [filas, setFilas] = useState<MascaraAdmin[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nueva" | "editar" | null>(null);
  const [form, setForm] = useState<FormMascara>(MASCARA_NUEVA);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { const r = await api.get<MascaraAdmin[]>("/api/perfiles-datos/mascaras"); setFilas(r.data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar las máscaras.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<MascaraAdmin>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Nombre", render: (r) => <>{r.nombre}{r.esSistema && <span className="insignia" style={{ marginLeft: 6 }}>Sistema</span>}</>, textoBusqueda: (r) => r.nombre },
    { clave: "tipo", encabezado: "Tipo", render: (r) => TIPOS_MASCARA[r.tipo] ?? r.tipo },
    { clave: "ejemplo", encabezado: "Ejemplo", render: (r) => <code>{r.ejemplo}</code> },
    { clave: "uso", encabezado: "Perfiles", render: (r) => r.perfilesQueLaUsan },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const m = filas.find((f) => f.idMascara === idSel) ?? null;

  async function accion(fn: () => Promise<unknown>, ok: string) {
    setExito(null); setError(null);
    try { await fn(); setExito(ok); await cargar(); }
    catch (e) { setError(mensajeError(e, "No se pudo completar la acción.")); }
  }

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const cuerpo: MascaraGuardar = { codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion.trim() || null, tipo: form.tipo, visiblesInicio: form.visiblesInicio, visiblesFin: form.visiblesFin, caracter: form.caracter || "*" };
    try {
      if (panel === "nueva") await api.post("/api/perfiles-datos/mascaras", cuerpo);
      else if (m) await api.put(`/api/perfiles-datos/mascaras/${m.idMascara}`, cuerpo);
      setExito(panel === "nueva" ? "Máscara creada." : "Máscara actualizada.");
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar la máscara.")); }
    finally { setGuardando(false); }
  }

  return (
    <div>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idMascara}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay máscaras."
        comandos={[
          { clave: "nueva", etiqueta: "Nueva", icono: "+", onClick: () => { setExito(null); setErrorForm(null); setForm(MASCARA_NUEVA); setPanel("nueva"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", deshabilitado: !m || m.esSistema, onClick: () => { if (!m) return; setExito(null); setErrorForm(null); setForm({ codigo: m.codigo, nombre: m.nombre, descripcion: m.descripcion ?? "", tipo: m.tipo, visiblesInicio: m.visiblesInicio, visiblesFin: m.visiblesFin, caracter: m.caracter }); setPanel("editar"); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: m?.estado === "Activo", onClick: () => m && void accion(() => api.put(`/api/perfiles-datos/mascaras/${m.idMascara}/estado`, { estado: "Activo" }), "Máscara activada.") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: !m || m.esSistema || m.estado === "Inactivo", onClick: () => m && void accion(() => api.put(`/api/perfiles-datos/mascaras/${m.idMascara}/estado`, { estado: "Inactivo" }), "Máscara inactivada.") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nueva" ? "Nueva máscara" : `Editar ${form.codigo}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código
              <input type="text" maxLength={50} value={form.codigo} disabled={panel === "editar"} onChange={(e) => setForm({ ...form, codigo: e.target.value.toUpperCase() })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={100} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            <label style={{ gridColumn: "1 / -1" }}>Descripción
              <input type="text" maxLength={300} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} />
            </label>
            <label style={{ gridColumn: "1 / -1" }}>Tipo
              <select value={form.tipo} onChange={(e) => setForm({ ...form, tipo: e.target.value })}>
                {Object.entries(TIPOS_MASCARA).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
              </select>
            </label>
            {form.tipo === "PARCIAL" && (<>
              <label>Caracteres visibles al inicio
                <input type="number" min={0} max={20} value={form.visiblesInicio} onChange={(e) => setForm({ ...form, visiblesInicio: Number(e.target.value) })} />
              </label>
              <label>Caracteres visibles al final
                <input type="number" min={0} max={20} value={form.visiblesFin} onChange={(e) => setForm({ ...form, visiblesFin: Number(e.target.value) })} />
              </label>
            </>)}
            {(form.tipo === "PARCIAL" || form.tipo === "CORREO") && (
              <label>Carácter de relleno
                <input type="text" maxLength={1} value={form.caracter} onChange={(e) => setForm({ ...form, caracter: e.target.value })} />
              </label>
            )}
            <p className="nota" style={{ gridColumn: "1 / -1" }}>
              Solo se enmascaran letras y dígitos; los separadores (guiones, espacios) se conservan. Si el dato es más corto que lo que se deja visible, se oculta completo.
            </p>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}
