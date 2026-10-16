import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { PanelModal } from "../components/VentanaModal";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import type { CarpetaAdmin, RolDocumentosAdmin, TipoDocumentoAdmin } from "../api/tipos";

type Pestana = "carpetas" | "tipos" | "roles";

interface FormCarpeta { codigo: string; nombre: string; descripcion: string; orden: number; aplicaFisica: boolean; aplicaJuridica: boolean; aplicaGrupo: boolean }
const CARPETA_NUEVA: FormCarpeta = { codigo: "", nombre: "", descripcion: "", orden: 100, aplicaFisica: true, aplicaJuridica: true, aplicaGrupo: false };

interface FormTipo {
  codigo: string; nombre: string; descripcion: string; idCarpeta: number; orden: number; cardinalidad: "Unico" | "Multiple"; maximo: string;
  vencimiento: "Obligatorio" | "Opcional" | "NoAplica"; diasAviso: number; confidencialidad: "Normal" | "Restringida";
  aplicaFisica: boolean; aplicaJuridica: boolean; aplicaGrupo: boolean;
}
const TIPO_NUEVO: FormTipo = {
  codigo: "", nombre: "", descripcion: "", idCarpeta: 0, orden: 100, cardinalidad: "Multiple", maximo: "", vencimiento: "NoAplica", diasAviso: 30,
  confidencialidad: "Normal", aplicaFisica: true, aplicaJuridica: true, aplicaGrupo: false,
};

const aplicaTexto = (f: boolean, j: boolean, g: boolean) => [f && "Persona", j && "Organización", g && "Grupo"].filter(Boolean).join(", ") || "—";
const etiquetaVenc = (v: string) => (v === "Obligatorio" ? "Obligatorio" : v === "Opcional" ? "Opcional" : "No aplica");

/**
 * Gestor documental › Carpetas y tipos de documento (permiso DOC_ADMINISTRACION). Configuración de la compañía: se guarda directo y
 * queda en la auditoría. Nada se borra: se inactiva. La tercera pestaña define qué documento vigente exige cada rol.
 */
export function DocumentosAdminPage() {
  const api = useApiClient();
  const [pestana, setPestana] = useState<Pestana>("carpetas");
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [carpetas, setCarpetas] = useState<CarpetaAdmin[]>([]);
  const [tipos, setTipos] = useState<TipoDocumentoAdmin[]>([]);
  const [cargando, setCargando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [c, t] = await Promise.all([
        api.get<CarpetaAdmin[]>("/api/documentos/admin/carpetas"),
        api.get<TipoDocumentoAdmin[]>("/api/documentos/admin/tipos"),
      ]);
      setCarpetas(c.data); setTipos(t.data);
    } catch (e) { setError(mensajeError(e, "No se pudo cargar la configuración documental.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  return (
    <div>
      <h1>Carpetas y tipos de documento</h1>
      <p className="nota">
        Configure cómo se organiza el expediente del cliente. Los cambios se guardan de inmediato y quedan en la auditoría; los documentos ya cargados se conservan
        (las carpetas y los tipos se inactivan, no se borran).
      </p>
      <div role="tablist" style={{ display: "flex", gap: 8, margin: "8px 0 12px" }}>
        {([["carpetas", "Carpetas"], ["tipos", "Tipos de documento"], ["roles", "Documentos exigidos por rol"]] as [Pestana, string][]).map(([k, t]) => (
          <button key={k} type="button" role="tab" aria-selected={pestana === k} className={pestana === k ? "" : "boton-secundario"}
            onClick={() => { setPestana(k); setError(null); setExito(null); }}>{t}</button>
        ))}
      </div>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      {pestana === "carpetas" && <Carpetas carpetas={carpetas} cargando={cargando} recargar={cargar} onError={setError} onExito={setExito} />}
      {pestana === "tipos" && <Tipos tipos={tipos} carpetas={carpetas} cargando={cargando} recargar={cargar} onError={setError} onExito={setExito} />}
      {pestana === "roles" && <RolesDocumentos onError={setError} onExito={setExito} />}
    </div>
  );
}

interface PropsLista { cargando: boolean; recargar: () => Promise<void>; onError: (m: string | null) => void; onExito: (m: string | null) => void }

function Carpetas({ carpetas, cargando, recargar, onError, onExito }: PropsLista & { carpetas: CarpetaAdmin[] }) {
  const api = useApiClient();
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nueva" | "editar" | null>(null);
  const [form, setForm] = useState<FormCarpeta>(CARPETA_NUEVA);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const c = carpetas.find((x) => x.idCarpeta === idSel) ?? null;

  const columnas: ColumnaTabla<CarpetaAdmin>[] = useMemo(() => [
    { clave: "orden", encabezado: "Orden", render: (r) => r.orden },
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Carpeta", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "aplica", encabezado: "Aplica a", render: (r) => aplicaTexto(r.aplicaFisica, r.aplicaJuridica, r.aplicaGrupo) },
    { clave: "tipos", encabezado: "Tipos activos", render: (r) => r.cantidadTipos },
    { clave: "origen", encabezado: "Origen", render: (r) => (r.esSistema ? "Del sistema" : "Configurable") },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
  ], []);

  async function accion(fn: () => Promise<unknown>, ok: string) {
    onExito(null); onError(null);
    try { await fn(); onExito(ok); setSel(new Set()); await recargar(); }
    catch (e) { onError(mensajeError(e, "No se pudo completar la acción.")); }
  }

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    try {
      if (panel === "nueva") await api.post("/api/documentos/admin/carpetas", form);
      else if (c) await api.put(`/api/documentos/admin/carpetas/${c.idCarpeta}`, form);
      onExito(panel === "nueva" ? "Carpeta creada." : "Carpeta actualizada.");
      setPanel(null); setSel(new Set()); await recargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  return (
    <>
      <TablaDatos
        columnas={columnas} filas={carpetas} claveFila={(f) => f.idCarpeta} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay carpetas."
        comandos={[
          { clave: "nueva", etiqueta: "Nueva", icono: "+", onClick: () => { onExito(null); setErrorForm(null); setForm(CARPETA_NUEVA); setPanel("nueva"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", deshabilitado: !c || c.esSistema, onClick: () => {
            if (!c) return; onExito(null); setErrorForm(null);
            setForm({ codigo: c.codigo, nombre: c.nombre, descripcion: c.descripcion ?? "", orden: c.orden, aplicaFisica: c.aplicaFisica, aplicaJuridica: c.aplicaJuridica, aplicaGrupo: c.aplicaGrupo });
            setPanel("editar");
          } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: !c || c.esSistema || c.estado === "Activo", onClick: () => c && void accion(() => api.put(`/api/documentos/admin/carpetas/${c.idCarpeta}/estado`, { estado: "Activo" }), "Carpeta activada.") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: !c || c.esSistema || c.estado === "Inactivo", onClick: () => c && void accion(() => api.put(`/api/documentos/admin/carpetas/${c.idCarpeta}/estado`, { estado: "Inactivo" }), "Carpeta inactivada.") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void recargar() },
        ]}
      />
      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nueva" ? "Nueva carpeta" : `Editar ${c?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código
              <input type="text" maxLength={40} value={form.codigo} disabled={panel === "editar"} onChange={(e) => setForm({ ...form, codigo: e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, "") })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={100} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            <label>Descripción
              <input type="text" maxLength={300} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} />
            </label>
            <label>Orden
              <input type="number" min={0} max={9999} value={form.orden} onChange={(e) => setForm({ ...form, orden: Number(e.target.value) })} required />
            </label>
            <fieldset className="docs-aplica">
              <legend>Aplica a</legend>
              <label className="docs-check"><input type="checkbox" checked={form.aplicaFisica} onChange={(e) => setForm({ ...form, aplicaFisica: e.target.checked })} /> Persona</label>
              <label className="docs-check"><input type="checkbox" checked={form.aplicaJuridica} onChange={(e) => setForm({ ...form, aplicaJuridica: e.target.checked })} /> Organización</label>
              <label className="docs-check"><input type="checkbox" checked={form.aplicaGrupo} onChange={(e) => setForm({ ...form, aplicaGrupo: e.target.checked })} /> Grupo</label>
            </fieldset>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </>
  );
}

function Tipos({ tipos, carpetas, cargando, recargar, onError, onExito }: PropsLista & { tipos: TipoDocumentoAdmin[]; carpetas: CarpetaAdmin[] }) {
  const api = useApiClient();
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState<FormTipo>(TIPO_NUEVO);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const t = tipos.find((x) => x.idTipoDocumento === idSel) ?? null;
  const carpetasUsables = carpetas.filter((c) => !c.esSistema && (c.estado === "Activo" || c.idCarpeta === form.idCarpeta));

  const columnas: ColumnaTabla<TipoDocumentoAdmin>[] = useMemo(() => [
    { clave: "carpeta", encabezado: "Carpeta", render: (r) => r.carpeta, textoBusqueda: (r) => r.carpeta },
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Tipo de documento", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "card", encabezado: "Cardinalidad", render: (r) => (r.cardinalidad === "Unico" ? "Único vigente" : r.maximo ? `Múltiple (máx. ${r.maximo})` : "Múltiple"), textoBusqueda: (r) => r.cardinalidad },
    { clave: "venc", encabezado: "Vencimiento", render: (r) => (r.vencimiento === "NoAplica" ? "No aplica" : `${etiquetaVenc(r.vencimiento)} · aviso ${r.diasAviso} d`) },
    { clave: "conf", encabezado: "Confidencialidad", render: (r) => r.confidencialidad },
    { clave: "aplica", encabezado: "Aplica a", render: (r) => aplicaTexto(r.aplicaFisica, r.aplicaJuridica, r.aplicaGrupo) },
    { clave: "uso", encabezado: "En uso", render: (r) => `${r.documentosVigentes} vigentes${r.rolesQueLoExigen ? ` · ${r.rolesQueLoExigen} rol(es) lo exigen` : ""}` },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
  ], []);

  async function accion(fn: () => Promise<unknown>, ok: string) {
    onExito(null); onError(null);
    try { await fn(); onExito(ok); setSel(new Set()); await recargar(); }
    catch (e) { onError(mensajeError(e, "No se pudo completar la acción.")); }
  }

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const cuerpo = { ...form, maximo: form.cardinalidad === "Multiple" && form.maximo.trim() !== "" ? Number(form.maximo) : null, diasAviso: form.vencimiento === "NoAplica" ? 0 : form.diasAviso };
    try {
      if (panel === "nuevo") await api.post("/api/documentos/admin/tipos", cuerpo);
      else if (t) await api.put(`/api/documentos/admin/tipos/${t.idTipoDocumento}`, cuerpo);
      onExito(panel === "nuevo" ? "Tipo de documento creado." : "Tipo de documento actualizado.");
      setPanel(null); setSel(new Set()); await recargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  return (
    <>
      <TablaDatos
        columnas={columnas} filas={tipos} claveFila={(f) => f.idTipoDocumento} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay tipos de documento."
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", onClick: () => {
            onExito(null); setErrorForm(null);
            setForm({ ...TIPO_NUEVO, idCarpeta: carpetas.find((c) => !c.esSistema && c.estado === "Activo")?.idCarpeta ?? 0 }); setPanel("nuevo");
          } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: () => {
            if (!t) return; onExito(null); setErrorForm(null);
            setForm({ codigo: t.codigo, nombre: t.nombre, descripcion: t.descripcion ?? "", idCarpeta: t.idCarpeta, orden: t.orden, cardinalidad: t.cardinalidad, maximo: t.maximo?.toString() ?? "",
              vencimiento: t.vencimiento, diasAviso: t.diasAviso, confidencialidad: t.confidencialidad, aplicaFisica: t.aplicaFisica, aplicaJuridica: t.aplicaJuridica, aplicaGrupo: t.aplicaGrupo });
            setPanel("editar");
          } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: !t || t.estado === "Activo", onClick: () => t && void accion(() => api.put(`/api/documentos/admin/tipos/${t.idTipoDocumento}/estado`, { estado: "Activo" }), "Tipo activado.") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: !t || t.estado === "Inactivo", onClick: () => t && void accion(() => api.put(`/api/documentos/admin/tipos/${t.idTipoDocumento}/estado`, { estado: "Inactivo" }), "Tipo inactivado.") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void recargar() },
        ]}
      />
      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nuevo" ? "Nuevo tipo de documento" : `Editar ${t?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código
              <input type="text" maxLength={40} value={form.codigo} disabled={panel === "editar"} onChange={(e) => setForm({ ...form, codigo: e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, "") })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={120} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            <label>Descripción
              <input type="text" maxLength={300} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} />
            </label>
            <label>Carpeta
              <select value={form.idCarpeta} onChange={(e) => setForm({ ...form, idCarpeta: Number(e.target.value) })} required>
                <option value={0} disabled>Seleccione…</option>
                {carpetasUsables.map((c) => <option key={c.idCarpeta} value={c.idCarpeta}>{c.nombre}</option>)}
              </select>
            </label>
            <label>Cardinalidad
              <select value={form.cardinalidad} onChange={(e) => setForm({ ...form, cardinalidad: e.target.value as FormTipo["cardinalidad"], maximo: e.target.value === "Unico" ? "" : form.maximo })}>
                <option value="Unico">Único vigente (subir otro equivale a reemplazar; ej. cédula)</option>
                <option value="Multiple">Múltiple (cada carga es un documento nuevo; ej. contratos)</option>
              </select>
            </label>
            {form.cardinalidad === "Multiple" && (
              <label>Máximo de documentos vigentes (opcional)
                <input type="number" min={1} max={1000} value={form.maximo} onChange={(e) => setForm({ ...form, maximo: e.target.value })} placeholder="Sin máximo" />
              </label>
            )}
            <label>Vencimiento
              <select value={form.vencimiento} onChange={(e) => setForm({ ...form, vencimiento: e.target.value as FormTipo["vencimiento"] })}>
                <option value="NoAplica">No aplica</option>
                <option value="Opcional">Opcional</option>
                <option value="Obligatorio">Obligatorio</option>
              </select>
            </label>
            {form.vencimiento !== "NoAplica" && (
              <label>Días de aviso previo al vencimiento
                <input type="number" min={0} max={365} value={form.diasAviso} onChange={(e) => setForm({ ...form, diasAviso: Number(e.target.value) })} required />
              </label>
            )}
            <label>Confidencialidad
              <select value={form.confidencialidad} onChange={(e) => setForm({ ...form, confidencialidad: e.target.value as FormTipo["confidencialidad"] })}>
                <option value="Normal">Normal</option>
                <option value="Restringida">Restringida (solo roles con acceso a documentos restringidos)</option>
              </select>
            </label>
            <label>Orden
              <input type="number" min={0} max={9999} value={form.orden} onChange={(e) => setForm({ ...form, orden: Number(e.target.value) })} required />
            </label>
            <fieldset className="docs-aplica">
              <legend>Aplica a</legend>
              <label className="docs-check"><input type="checkbox" checked={form.aplicaFisica} onChange={(e) => setForm({ ...form, aplicaFisica: e.target.checked })} /> Persona</label>
              <label className="docs-check"><input type="checkbox" checked={form.aplicaJuridica} onChange={(e) => setForm({ ...form, aplicaJuridica: e.target.checked })} /> Organización</label>
              <label className="docs-check"><input type="checkbox" checked={form.aplicaGrupo} onChange={(e) => setForm({ ...form, aplicaGrupo: e.target.checked })} /> Grupo</label>
            </fieldset>
            <p className="nota">Si el cambio requiere revisión humana, se define en Reglas de aprobación (gestión «Documentos del expediente del cliente»), no en el tipo.</p>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </>
  );
}

function RolesDocumentos({ onError, onExito }: { onError: (m: string | null) => void; onExito: (m: string | null) => void }) {
  const api = useApiClient();
  const [tipoPersona, setTipoPersona] = useState<"Fisica" | "Juridica">("Fisica");
  const [datos, setDatos] = useState<RolDocumentosAdmin | null>(null);
  const [filas, setFilas] = useState<Record<number, number[]>>({});
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    try {
      const r = await api.get<RolDocumentosAdmin>(`/api/documentos/admin/roles?tipoPersona=${tipoPersona}`);
      setDatos(r.data); setFilas(Object.fromEntries(r.data.roles.map((x) => [x.idTipoRol, x.idsTipoDocumento]))); onError(null);
    } catch (e) { onError(mensajeError(e, "No se pudo cargar la configuración.")); }
  }, [api, tipoPersona, onError]);
  useEffect(() => { void cargar(); }, [cargar]);

  const modificados = (datos?.roles ?? []).filter((r) => [...r.idsTipoDocumento].sort().join("|") !== [...(filas[r.idTipoRol] ?? [])].sort().join("|"));

  const cambiar = (idRol: number, idTipo: number, v: boolean) =>
    setFilas((f) => ({ ...f, [idRol]: v ? [...(f[idRol] ?? []), idTipo] : (f[idRol] ?? []).filter((x) => x !== idTipo) }));

  async function guardar() {
    setGuardando(true); onError(null); onExito(null);
    try {
      await api.put("/api/documentos/admin/roles", { tipoPersona, roles: modificados.map((r) => ({ idTipoRol: r.idTipoRol, idsTipoDocumento: filas[r.idTipoRol] ?? [] })) });
      onExito("Documentos exigidos guardados."); await cargar();
    } catch (e) { onError(mensajeError(e, "No se pudieron guardar los cambios.")); }
    finally { setGuardando(false); }
  }

  if (!datos) return <p className="nota">Cargando…</p>;
  // Un tipo que ya estaba exigido pero se inactivo no aparece en la matriz: se muestra aparte para poder quitarlo.
  const huerfanos = new Set<number>();
  datos.roles.forEach((r) => r.idsTipoDocumento.forEach((id) => { if (!datos.tipos.some((t) => t.idTipoDocumento === id)) huerfanos.add(id); }));

  return (
    <section className="wiz__tarjeta">
      <div className="wiz__cuerpo" style={{ overflowX: "auto" }}>
        <p className="nota">
          Marque el documento que cada rol exige tener <strong>vigente</strong> en el expediente (no vencido ni obsoleto). Se verifica al agregar el rol a un cliente
          ya registrado y se muestra en la pestaña Documentos de su ficha 360; si el documento vence después, el requisito aparece sin cumplir.
        </p>
        <div role="tablist" style={{ display: "flex", gap: 8, margin: "8px 0" }}>
          {(["Fisica", "Juridica"] as const).map((t) => (
            <button key={t} type="button" role="tab" aria-selected={tipoPersona === t} className={tipoPersona === t ? "" : "boton-secundario"}
              disabled={guardando || (tipoPersona !== t && modificados.length > 0)}
              title={tipoPersona !== t && modificados.length > 0 ? "Guarde o descarte los cambios antes de cambiar de pestaña" : undefined}
              onClick={() => { setDatos(null); setTipoPersona(t); }}>{t === "Fisica" ? "Persona" : "Organización"}</button>
          ))}
        </div>
        {datos.tipos.length === 0 ? <p className="nota">No hay tipos de documento activos que apliquen a este tipo de interlocutor.</p> : (
          <table className="tabla tabla--compacta">
            <thead><tr><th>Documento</th>{datos.roles.map((r) => <th key={r.idTipoRol} style={{ textAlign: "center" }}>{r.nombre}</th>)}</tr></thead>
            <tbody>
              {datos.tipos.map((tp, i) => [
                (i === 0 || datos.tipos[i - 1].carpeta !== tp.carpeta) && (
                  <tr key={`c-${tp.carpeta}`}><th colSpan={datos.roles.length + 1} style={{ textAlign: "left", background: "var(--color-fondo-suave, #f3f5f8)" }}>{tp.carpeta}</th></tr>),
                <tr key={tp.idTipoDocumento}>
                  <td>{tp.nombre}</td>
                  {datos.roles.map((r) => (
                    <td key={r.idTipoRol} style={{ textAlign: "center" }}>
                      <input type="checkbox" aria-label={`${r.nombre}: ${tp.nombre}`} checked={(filas[r.idTipoRol] ?? []).includes(tp.idTipoDocumento)}
                        onChange={(e) => cambiar(r.idTipoRol, tp.idTipoDocumento, e.target.checked)} />
                    </td>
                  ))}
                </tr>,
              ])}
            </tbody>
          </table>
        )}
        {huerfanos.size > 0 && <p className="nota">Hay requisitos sobre tipos inactivos o que ya no aplican; actívelos o vuelva a abrirlos para quitarlos.</p>}
        <button type="button" disabled={modificados.length === 0 || guardando} onClick={guardar}>{guardando ? "Guardando…" : "Guardar cambios"}</button>
      </div>
    </section>
  );
}
