import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useApiClient } from "../api/useApiClient";
import { VentanaModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import { DuplicarPieza } from "./ReglasCondicionesPage";
import type { ReglaAccion } from "../api/reglas";

interface Form { codigo: string; nombre: string; descripcion: string; tipoAccion: "Decision" | "Asignar" | "Mensaje"; resultado: string; clave: string; valor: string; mensaje: string }
type Modo = "ver" | "editar" | "nuevo";
const TIPOS: { valor: Form["tipoAccion"]; texto: string; ayuda: string }[] = [
  { valor: "Decision", texto: "Decisión", ayuda: "Aprobar, rechazar o referir. Quien evalúa decide qué hacer con ella." },
  { valor: "Asignar", texto: "Asignar un valor", ayuda: "Devuelve un dato (por ejemplo recargo_tasa = 1.5) para que el proceso lo use." },
  { valor: "Mensaje", texto: "Mensaje", ayuda: "Un texto informativo para quien opera el caso." },
];

/** Pool de acciones (RUL_CONSULTA ver; RUL_ADMIN mantener). Fase 1: acciones puras; el módulo que evalúa ejecuta los efectos. */
export function ReglasAccionesPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("RUL_ADMIN");
  const [filas, setFilas] = useState<ReglaAccion[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [ficha, setFicha] = useState<{ id: number | null; modo: Modo } | null>(null);
  const [duplicar, setDuplicar] = useState<ReglaAccion | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { setFilas((await api.get<ReglaAccion[]>("/api/reglas/acciones")).data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar las acciones.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<ReglaAccion>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Acción", render: (r) => (
        <button type="button" className="boton-enlace-id" title="Consultar la acción" style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}
          onClick={() => { setExito(null); setFicha({ id: r.idAccion, modo: "ver" }); }}>{r.nombre}</button>
      ), textoBusqueda: (r) => r.nombre },
    { clave: "tipo", encabezado: "Tipo", render: (r) => TIPOS.find((t) => t.valor === r.tipoAccion)?.texto ?? r.tipoAccion, textoBusqueda: (r) => r.tipoAccion, filtroLista: true },
    { clave: "resumen", encabezado: "Qué hace", render: (r) => r.resumen, textoBusqueda: (r) => r.resumen },
    { clave: "usos", encabezado: "Versiones que la usan", render: (r) => r.usos, valorOrden: (r) => r.usos },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const acc = filas.find((x) => x.idAccion === idSel) ?? null;
  const accFicha = ficha?.id != null ? filas.find((x) => x.idAccion === ficha.id) ?? null : null;

  async function estado(e: "Activo" | "Inactivo") {
    if (!acc) return;
    setExito(null); setError(null);
    try { await api.put(`/api/reglas/acciones/${acc.idAccion}/estado`, { estado: e }); setExito(e === "Activo" ? "Acción activada." : "Acción inactivada."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Acciones</h1>
      <p className="nota">Lo que produce una regla cuando se cumple: una decisión (aprobar, rechazar, referir), un valor asignado o un mensaje. El módulo que evalúa (solicitud, crédito...) es quien actúa según el resultado.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idAccion} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay acciones." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nueva", icono: "+", oculto: !admin, onClick: () => { setExito(null); setFicha({ id: null, modo: "nuevo" }); } },
          { clave: "duplicar", etiqueta: "Duplicar", icono: "⧉", requiereSeleccion: "una", oculto: !admin, onClick: () => { if (acc) setDuplicar(acc); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: acc?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: acc?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />

      {ficha && (ficha.id == null || accFicha) && (
        <FichaAccion key={ficha.id ?? "nueva"} accion={accFicha} modoInicial={ficha.modo} admin={admin} onCerrar={() => setFicha(null)} onRefrescar={cargar}
          onDuplicar={(a) => { setFicha(null); setDuplicar(a); }} onCreada={async () => { setFicha(null); setExito("Acción creada."); await cargar(); }} />
      )}
      {duplicar && <DuplicarPieza titulo="Duplicar acción" origen={duplicar} url={`/api/reglas/acciones/${duplicar.idAccion}/duplicar`} onCerrar={() => setDuplicar(null)}
        onListo={async () => { setDuplicar(null); setExito("Acción duplicada."); await cargar(); }} />}
    </div>
  );
}

function FichaAccion({ accion, modoInicial, admin, onCerrar, onRefrescar, onCreada, onDuplicar }: {
  accion: ReglaAccion | null; modoInicial: Modo; admin: boolean; onCerrar: () => void; onRefrescar: () => Promise<void>; onCreada: () => Promise<void>; onDuplicar: (a: ReglaAccion) => void;
}) {
  const api = useApiClient();
  const formDe = (a: ReglaAccion): Form => ({ codigo: a.codigo, nombre: a.nombre, descripcion: a.descripcion ?? "", tipoAccion: a.tipoAccion as Form["tipoAccion"], resultado: a.resultado ?? "Aprobar", clave: a.clave ?? "", valor: a.valor ?? "", mensaje: a.mensaje ?? "" });
  const [modo, setModo] = useState<Modo>(modoInicial);
  const [form, setForm] = useState<Form>(() => (accion ? formDe(accion) : { codigo: "", nombre: "", descripcion: "", tipoAccion: "Decision", resultado: "Aprobar", clave: "", valor: "", mensaje: "" }));
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const t = form.tipoAccion;
    const cuerpo = {
      codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion.trim() || null, tipoAccion: t,
      resultado: t === "Decision" ? form.resultado : null, clave: t === "Asignar" ? form.clave.trim() : null, valor: t === "Asignar" ? form.valor.trim() : null,
      mensaje: t === "Asignar" ? null : form.mensaje.trim() || null,
    };
    try {
      if (modo === "nuevo") { await api.post("/api/reglas/acciones", cuerpo); await onCreada(); return; }
      if (accion) await api.put(`/api/reglas/acciones/${accion.idAccion}`, cuerpo);
      setAviso("Acción actualizada."); setModo("ver"); await onRefrescar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }
  const dato = (etiqueta: string, valor: ReactNode) => (<div className="info-fila"><span className="info-fila__etiqueta">{etiqueta}</span><span className="info-fila__valor">{valor}</span></div>);
  const bloqueada = !!accion?.congelada;

  return (
    <VentanaModal titulo={accion ? `${accion.codigo} · ${accion.nombre}` : "Nueva acción"} onCerrar={onCerrar} ancho={760}
      acciones={modo === "ver" && accion ? (
        <>
          {admin && <button type="button" disabled={bloqueada} title={bloqueada ? "Forma parte de una versión enviada a aprobación o publicada: duplíquela" : undefined}
            onClick={() => { setForm(formDe(accion)); setErrorForm(null); setAviso(null); setModo("editar"); }}>✎ Editar</button>}
          {admin && <button type="button" className="boton-secundario" onClick={() => onDuplicar(accion)}>⧉ Duplicar</button>}
        </>
      ) : undefined}>
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      {modo === "ver" && accion ? (
        <div>
          {dato("Código", accion.codigo)}
          {dato("Nombre", accion.nombre)}
          {dato("Descripción", accion.descripcion || "—")}
          {dato("Tipo", TIPOS.find((t) => t.valor === accion.tipoAccion)?.texto ?? accion.tipoAccion)}
          {dato("Qué hace", <strong>{accion.resumen}</strong>)}
          {dato("Versiones que la usan", accion.usos)}
          {dato("Estado", accion.estado)}
          {bloqueada && <p className="nota">Esta acción ya forma parte de una versión enviada a aprobación o publicada, por eso no se edita. Use «Duplicar» para crear una copia.</p>}
        </div>
      ) : (
        <>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código <input type="text" maxLength={50} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label className="formulario__ancho">Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <label className="formulario__ancho">Tipo de acción
              <select value={form.tipoAccion} onChange={(e) => setForm({ ...form, tipoAccion: e.target.value as Form["tipoAccion"] })}>
                {TIPOS.map((t) => (<option key={t.valor} value={t.valor}>{t.texto}</option>))}
              </select>
              <small className="nota">{TIPOS.find((t) => t.valor === form.tipoAccion)?.ayuda}</small>
            </label>
            {form.tipoAccion === "Decision" && (
              <label>Decisión
                <select value={form.resultado} onChange={(e) => setForm({ ...form, resultado: e.target.value })}>
                  <option value="Aprobar">Aprobar</option><option value="Referir">Referir (revisión manual)</option><option value="Rechazar">Rechazar</option>
                </select>
              </label>
            )}
            {form.tipoAccion === "Asignar" && (
              <>
                <label>Nombre del dato <input type="text" maxLength={100} value={form.clave} onChange={(e) => setForm({ ...form, clave: e.target.value })} required placeholder="recargo_tasa" /></label>
                <label>Valor <input type="text" maxLength={200} value={form.valor} onChange={(e) => setForm({ ...form, valor: e.target.value })} required /></label>
              </>
            )}
            {form.tipoAccion !== "Asignar" && (
              <label className="formulario__ancho">{form.tipoAccion === "Decision" ? "Motivo o mensaje (opcional)" : "Mensaje"}
                <input type="text" maxLength={500} value={form.mensaje} onChange={(e) => setForm({ ...form, mensaje: e.target.value })} required={form.tipoAccion === "Mensaje"} />
              </label>
            )}
            <div className="formulario__ancho" style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => { if (modo === "nuevo") onCerrar(); else { setModo("ver"); setErrorForm(null); } }}>Cancelar</button>
            </div>
          </form>
        </>
      )}
    </VentanaModal>
  );
}
