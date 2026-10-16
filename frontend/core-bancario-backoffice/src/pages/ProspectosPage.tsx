import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { fmtFecha, mensajeError } from "../api/mensajes";
import { useCatalogosCliente, useConfigsDireccion } from "../interlocutores/useCatalogosCliente";
import { DireccionCampos } from "../interlocutores/DireccionCampos";
import { aDtoDireccion, dirVacia, filaDirDesdeDto, formVacio, validarContacto, validarPaso, type FilaDir } from "../interlocutores/formCliente";
import { RevisionDuplicados } from "../components/RevisionDuplicados";
import type { CatalogoItemResponse, CoincidenciasResultado, IdentificacionValidacion, ProspectoFila } from "../api/tipos";

function Campo({ etiqueta, obligatorio, children }: { etiqueta: string; obligatorio?: boolean; children: ReactNode }) {
  return (
    <label className="wiz-campo">
      <span>{etiqueta}{obligatorio && <span className="wiz-campo__req" aria-hidden="true"> *</span>}</span>
      {children}
    </label>
  );
}

interface Form { direccion: FilaDir | null; nombre1: string; nombre2: string; apellido1: string; apellido2: string; fechaNacimiento: string; idTipo: number; numero: string; telefono: string; correo: string; canal: string }
const VACIO: Form = { direccion: null, nombre1: "", nombre2: "", apellido1: "", apellido2: "", fechaNacimiento: "", idTipo: 0, numero: "", telefono: "", correo: "", canal: "" };

/**
 * Interlocutores › Prospectos. Un prospecto no tiene afectacion: no genera gestion (solo auditoria).
 * Al capturar la identificacion se valida contra los interlocutores: si la persona ya es cliente, o ya esta registrada,
 * no se permite crear el prospecto y se muestra el enlace a su vista 360°.
 */
export function ProspectosPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { tienePermiso } = usePermisos();
  const puedePromover = tienePermiso("CLI_ALTA");
  const { catalogos: cat } = useCatalogosCliente();
  const { configs, asegurar: asegurarConfig } = useConfigsDireccion();
  const [errDir, setErrDir] = useState<Record<string, string>>({});
  const sinOpciones = useMemo(() => new Set<string>(), []);
  const marcarSinOpciones = (clave: string, nivel: number, sin: boolean) => { const k = `${clave}.${nivel}`; if (sin) sinOpciones.add(k); else sinOpciones.delete(k); };
  const puedeCrear = tienePermiso("PRO_ALTA");
  const puedeEditar = tienePermiso("PRO_EDICION");

  const [filas, setFilas] = useState<ProspectoFila[]>([]);
  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nuevo" | "ver" | "editar" | null>(null);
  const [actual, setActual] = useState<ProspectoFila | null>(null);
  const [form, setForm] = useState<Form>(VACIO);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [val, setVal] = useState<IdentificacionValidacion | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [dup, setDup] = useState<CoincidenciasResultado | null>(null);

  useEffect(() => {
    api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_IDENTIFICACION_FISICA")
      .then((r) => setTipos(r.data)).catch((e) => setError(mensajeError(e, "No se pudo cargar los tipos de identificación.")));
  }, [api]);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { const r = await api.get<ProspectoFila[]>("/api/prospectos"); setFilas(r.data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar los prospectos.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const abrir = (p: ProspectoFila, modo: "ver" | "editar") => {
    setExito(null); setErrorForm(null); setVal(null); setActual(p);
    setErrDir({}); setForm({ direccion: p.direccion ? filaDirDesdeDto(p.direccion) : null, nombre1: p.nombre1 ?? p.nombre, nombre2: p.nombre2 ?? "", apellido1: p.apellido1 ?? "", apellido2: p.apellido2 ?? "", fechaNacimiento: (p.fechaNacimiento ?? "").slice(0, 10), idTipo: p.idTipoIdentificacion ?? 0, numero: p.numeroIdentificacion ?? "", telefono: p.telefono ?? "", correo: p.correo ?? "", canal: p.canalOrigen ?? "" });
    setPanel(modo);
  };

  useEffect(() => { if (form.direccion?.idPais) asegurarConfig(form.direccion.idPais); // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.direccion?.idPais]);

  const columnas: ColumnaTabla<ProspectoFila>[] = useMemo(() => [
    { clave: "nombre", encabezado: "Prospecto", render: (r) => <button type="button" className="boton-enlace-id" onClick={() => abrir(r, "ver")}>{r.nombre}</button>, textoBusqueda: (r) => r.nombre },
    { clave: "ident", encabezado: "Identificación", render: (r) => (r.numeroIdentificacion ? `${r.tipoIdentificacion ?? ""} ${r.numeroIdentificacion}` : "—"), textoBusqueda: (r) => r.numeroIdentificacion ?? "" },
    { clave: "tel", encabezado: "Teléfono", render: (r) => r.telefono ?? "—" },
    { clave: "correo", encabezado: "Correo", render: (r) => r.correo ?? "—", textoBusqueda: (r) => r.correo ?? "" },
    { clave: "unidad", encabezado: "Sucursal", render: (r) => r.unidadNombre ?? "—", textoBusqueda: (r) => r.unidadNombre ?? "" },
    { clave: "estado", encabezado: "Estado", render: (r) => (r.idGestionPendiente ? `Promoción en aprobación (${r.numeroGestionPendiente})` : r.estado), textoBusqueda: (r) => r.estado },
    { clave: "fecha", encabezado: "Registro", render: (r) => fmtFecha(r.fechaRegistro) },
  // eslint-disable-next-line react-hooks/exhaustive-deps
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const f = filas.find((x) => x.idProspecto === idSel) ?? null;

  async function validar() {
    setVal(null);
    if (!form.idTipo || !form.numero.trim()) return;
    try {
      const r = await api.get<IdentificacionValidacion>("/api/interlocutores/validar-identificacion", { params: { idTipo: form.idTipo, numero: form.numero.trim() } });
      setVal(r.data);
    } catch (e) { setErrorForm(mensajeError(e, "No se pudo validar la identificación.")); }
  }

  const bloqueado = panel === "nuevo" && !!val && (val.existePersona || val.estadoProspecto === "Prospecto");

  function guardar(e: FormEvent) { e.preventDefault(); void ejecutarGuardar(false); }

  async function ejecutarGuardar(duplicadosRevisados: boolean) {
    setErrorForm(null);
    // Contacto y direccion (misma validacion que el cliente).
    const errs: string[] = [];
    if (form.telefono.trim()) { const m = validarContacto("TELEFONO_MOVIL", form.telefono); if (m) errs.push(`Teléfono: ${m}`); }
    if (form.correo.trim()) { const m = validarContacto("CORREO", form.correo); if (m) errs.push(`Correo: ${m}`); }
    if (!form.direccion) errs.push("Registre la dirección del prospecto.");
    else {
      const ed = validarPaso(3, { ...formVacio(), direcciones: [form.direccion] }, cat, configs, sinOpciones);
      setErrDir(ed);
      if (Object.keys(ed).length > 0) errs.push("Revise los campos marcados en la dirección.");
    }
    if (errs.length > 0) { setErrorForm(errs.join(" ")); return; }
    setGuardando(true);
    const direccion = aDtoDireccion(form.direccion!);
    try {
      // Control de calidad de datos: al crear, se avisa si ya hay personas parecidas (cliente, otro rol o prospecto).
      if (panel === "nuevo" && !duplicadosRevisados) {
        const r = await api.post<CoincidenciasResultado>("/api/interlocutores/coincidencias", {
          nombre1: form.nombre1, nombre2: form.nombre2, apellido1: form.apellido1, apellido2: form.apellido2,
          fechaNacimiento: form.fechaNacimiento || null,
          identificaciones: form.idTipo && form.numero.trim() ? [{ idTipoIdentificacion: form.idTipo, numero: form.numero.trim() }] : [],
          telefonos: form.telefono.trim() ? [form.telefono] : [], correos: form.correo.trim() ? [form.correo] : [],
        });
        if (r.data.items.length > 0) { setDup(r.data); return; }
      }
      if (panel === "nuevo") {
        await api.post("/api/prospectos", {
          nombre1: form.nombre1, nombre2: form.nombre2, apellido1: form.apellido1, apellido2: form.apellido2,
          fechaNacimiento: form.fechaNacimiento || null, idTipoIdentificacion: form.idTipo || null, numeroIdentificacion: form.numero || null,
          telefono: form.telefono, correo: form.correo, canalOrigen: form.canal, direccion,
        });
        setExito("Prospecto creado.");
      } else if (actual) {
        await api.put(`/api/prospectos/${actual.idProspecto}`, { nombre1: form.nombre1, nombre2: form.nombre2, apellido1: form.apellido1, apellido2: form.apellido2, fechaNacimiento: form.fechaNacimiento || null, telefono: form.telefono, correo: form.correo, canalOrigen: form.canal, direccion });
        setExito("Prospecto actualizado.");
      }
      setPanel(null); await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  async function cambiarEstado(estado: "Descartado" | "Prospecto") {
    if (!f) return;
    setExito(null); setError(null);
    try { await api.put(`/api/prospectos/${f.idProspecto}/estado`, { estado }); setExito(estado === "Descartado" ? "Prospecto descartado." : "Prospecto reactivado."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  const soloLectura = panel === "ver";

  if (dup) {
    return (
      <RevisionDuplicados
        resultado={dup} exigirMotivo={false}
        contexto="Antes de registrar el prospecto, revise si la persona ya está registrada. Es solo un aviso."
        onVolver={() => setDup(null)}
        onMisma={(c) => { setDup(null); setPanel(null); navigate(c.origen === "Prospecto" ? "/interlocutores/prospectos" : `/interlocutores/${c.id}`); }}
        onContinuar={() => { setDup(null); void ejecutarGuardar(true); }}
      />
    );
  }

  // Formulario en pantalla propia (igual que el alta de cliente): sin la lista arriba, botones en el pie.
  if (panel) {
    return (
      <div className="wiz">
        <div className="wiz__titulo">
          <div>
            <h1>{panel === "nuevo" ? "Nuevo prospecto" : actual?.nombre}</h1>
            <div className="nota">{panel === "ver" ? "Consulta del prospecto" : panel === "editar" ? "Modificar prospecto" : "Persona con interés que aún no es cliente. No requiere aprobación."}</div>
          </div>
        </div>
        {errorForm && <div role="alert" className="wiz__alerta">{errorForm}</div>}
        <section className="wiz__tarjeta">
          <form onSubmit={guardar}>
            <div className="wiz__cuerpo" style={{ display: "flex", flexDirection: "column", gap: "1.5rem" }}>
            <div className="wiz__grupo">
              <h2>Identificación</h2>
              <div className="wiz__rejilla">
                <Campo etiqueta="Tipo de identificación">
                  <select value={form.idTipo} disabled={panel !== "nuevo"} onChange={(e) => { setForm({ ...form, idTipo: Number(e.target.value) }); setVal(null); }}>
                    <option value={0}>Sin identificación</option>
                    {tipos.map((t) => (<option key={t.id} value={t.id}>{t.nombre}</option>))}
                  </select>
                </Campo>
                <Campo etiqueta="Número de identificación">
                  <input type="text" maxLength={50} value={form.numero} disabled={panel !== "nuevo"}
                    onChange={(e) => { setForm({ ...form, numero: e.target.value }); setVal(null); }} onBlur={() => void validar()} />
                </Campo>
              </div>
              {panel === "nuevo" && val?.existePersona && (
                <p className="mensaje-error">
                  {val.esCliente ? "Esta persona ya es cliente" : "Esta persona ya está registrada"}: {val.numeroInterlocutor} — {val.nombre}.{" "}
                  <Link to={`/interlocutores/${val.idInterlocutor}`}>Ver vista 360°</Link>. No se puede crear como prospecto.
                </p>
              )}
              {panel === "nuevo" && !val?.existePersona && val?.estadoProspecto === "Prospecto" && (
                <p className="mensaje-error">Ya existe un prospecto con esa identificación ({val.nombreProspecto}).</p>
              )}
              {panel === "nuevo" && val && !val.existePersona && val.estadoProspecto === "Descartado" && (
                <p className="nota">Hay un prospecto descartado con esta identificación ({val.nombreProspecto}). Puede reactivarlo desde la lista.</p>
              )}
            </div>

            <div className="wiz__grupo">
              <h2>Datos personales</h2>
              <div className="wiz__rejilla">
                <Campo etiqueta="Primer nombre" obligatorio>
                  <input type="text" maxLength={100} value={form.nombre1} disabled={soloLectura} onChange={(e) => setForm({ ...form, nombre1: e.target.value })} required />
                </Campo>
                <Campo etiqueta="Segundo nombre">
                  <input type="text" maxLength={100} value={form.nombre2} disabled={soloLectura} onChange={(e) => setForm({ ...form, nombre2: e.target.value })} />
                </Campo>
                <Campo etiqueta="Primer apellido" obligatorio>
                  <input type="text" maxLength={100} value={form.apellido1} disabled={soloLectura} onChange={(e) => setForm({ ...form, apellido1: e.target.value })} required />
                </Campo>
                <Campo etiqueta="Segundo apellido">
                  <input type="text" maxLength={100} value={form.apellido2} disabled={soloLectura} onChange={(e) => setForm({ ...form, apellido2: e.target.value })} />
                </Campo>
                <Campo etiqueta="Fecha de nacimiento">
                  <input type="date" value={form.fechaNacimiento} disabled={soloLectura} onChange={(e) => setForm({ ...form, fechaNacimiento: e.target.value })} />
                </Campo>
              </div>
            </div>

            <div className="wiz__grupo">
              <h2>Sucursal</h2>
              <p className="nota">
                {panel === "nuevo"
                  ? "Se asigna automáticamente la sucursal de su usuario."
                  : `Sucursal: ${actual?.unidadNombre ?? "— sin sucursal —"}`}
              </p>
            </div>

            <div className="wiz__grupo">
              <h2>Dirección</h2>
              {actual?.idGestionPendiente && (
                <p className="nota">Este prospecto tiene una promoción a cliente pendiente de aprobación (gestión {actual.numeroGestionPendiente}); no se puede modificar mientras tanto. Al convertirse, esta dirección, el correo y el teléfono pasan a ser los primeros del cliente.</p>
              )}
              {form.direccion && (
                <fieldset disabled={soloLectura} style={{ border: 0, padding: 0, margin: 0 }}>
                  <div className="wiz__rejilla">
                    <Campo etiqueta="Tipo de dirección" obligatorio>
                      <select value={form.direccion.idTipoDireccion} onChange={(e) => setForm({ ...form, direccion: { ...form.direccion!, idTipoDireccion: Number(e.target.value) } })}>
                        <option value={0}>Seleccione...</option>
                        {cat.tiposDireccion.map((t) => (<option key={t.id} value={t.id}>{t.nombre}</option>))}
                      </select>
                      {errDir[`${form.direccion.clave}.tipo`] && <span className="wiz-campo__error">{errDir[`${form.direccion.clave}.tipo`]}</span>}
                    </Campo>
                  </div>
                  <DireccionCampos d={form.direccion} cfg={configs[form.direccion.idPais]} paises={cat.paisesDireccion} errores={errDir}
                    onCambiar={(p) => setForm((x) => ({ ...x, direccion: { ...x.direccion!, ...p } }))} onSinOpciones={marcarSinOpciones} />
                  <Campo etiqueta="Dirección exacta (línea 1)" obligatorio>
                    <input type="text" maxLength={500} value={form.direccion.detalle} onChange={(e) => setForm({ ...form, direccion: { ...form.direccion!, detalle: e.target.value } })} />
                    {errDir[`${form.direccion.clave}.detalle`] && <span className="wiz-campo__error">{errDir[`${form.direccion.clave}.detalle`]}</span>}
                  </Campo>
                  <Campo etiqueta="Dirección exacta (línea 2)">
                    <input type="text" maxLength={200} value={form.direccion.detalle2 ?? ""} onChange={(e) => setForm({ ...form, direccion: { ...form.direccion!, detalle2: e.target.value } })} />
                  </Campo>
                  <Campo etiqueta="Dirección exacta (línea 3)">
                    <input type="text" maxLength={200} value={form.direccion.detalle3 ?? ""} onChange={(e) => setForm({ ...form, direccion: { ...form.direccion!, detalle3: e.target.value } })} />
                  </Campo>
                </fieldset>
              )}
              {!form.direccion && soloLectura && <p className="nota">Sin dirección registrada (prospecto anterior). Edítelo para agregarla.</p>}
              {!form.direccion && !soloLectura && (
                <button type="button" className="boton-secundario" onClick={() => setForm({ ...form, direccion: dirVacia(true, cat.tiposDireccion[0]?.id ?? 0, cat.paisesDireccion[0]?.id ?? 0) })}>+ Agregar dirección</button>
              )}
            </div>

            <div className="wiz__grupo">
              <h2>Contacto</h2>
              <div className="wiz__rejilla">
                <Campo etiqueta="Teléfono">
                  <input type="text" maxLength={30} value={form.telefono} disabled={soloLectura} onChange={(e) => setForm({ ...form, telefono: e.target.value })} />
                </Campo>
                <Campo etiqueta="Correo electrónico">
                  <input type="email" maxLength={200} value={form.correo} disabled={soloLectura} onChange={(e) => setForm({ ...form, correo: e.target.value })} />
                </Campo>
                <Campo etiqueta="Cómo llegó (origen)">
                  <input type="text" maxLength={50} value={form.canal} disabled={soloLectura} onChange={(e) => setForm({ ...form, canal: e.target.value })} />
                </Campo>
              </div>
            </div>


            </div>
            <div className="wiz__pie">
              {soloLectura ? (
                <>
                  <div className="wiz__pie-grupo">
                    <button type="button" className="wiz-enlace" onClick={() => setPanel(null)}>← Volver a la lista</button>
                  </div>
                  <div className="wiz__pie-grupo">
                    {actual?.estado === "Convertido" && actual.idInterlocutor && <Link to={`/interlocutores/${actual.idInterlocutor}`}>Ver cliente</Link>}
                    {puedePromover && actual?.estado === "Prospecto" && !actual?.idGestionPendiente && (
                      <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/clientes/nuevo?prospecto=${actual.idProspecto}`)}>Promover a cliente</button>
                    )}
                    {puedeEditar && actual?.estado !== "Convertido" && !actual?.idGestionPendiente && <button type="button" onClick={() => setPanel("editar")}>Editar</button>}
                  </div>
                </>
              ) : (
                <>
                  <div className="wiz__pie-grupo">
                    <button type="button" className="wiz-enlace" onClick={() => setPanel(null)}>Cancelar</button>
                  </div>
                  <div className="wiz__pie-grupo">
                    <button type="submit" disabled={guardando || bloqueado}>{guardando ? "Guardando..." : "Guardar"}</button>
                  </div>
                </>
              )}
            </div>
          </form>
        </section>
      </div>
    );
  }

  return (
    <div>
      <h1>Prospectos</h1>
      <p className="nota">Personas con interés que aún no son clientes. No requieren aprobación. Si la persona ya está registrada como cliente u otro rol, no se puede crear como prospecto.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(x) => x.idProspecto}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay prospectos registrados."
        comandos={[
          ...(puedeCrear ? [{ clave: "nuevo", etiqueta: "Nuevo", icono: "+", onClick: () => { setExito(null); setErrorForm(null); setVal(null); setActual(null); setErrDir({}); setForm({ ...VACIO, idTipo: tipos[0]?.id ?? 0, direccion: dirVacia(true, cat.tiposDireccion[0]?.id ?? 0, cat.paisesDireccion[0]?.id ?? 0) }); setPanel("nuevo"); } }] : []),
          ...(puedeEditar ? [
            { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una" as const, deshabilitado: f?.estado === "Convertido" || !!f?.idGestionPendiente, onClick: () => { if (f) abrir(f, "editar"); } },
            { clave: "descartar", etiqueta: "Descartar", icono: "⊘", requiereSeleccion: "una" as const, deshabilitado: f?.estado !== "Prospecto" || !!f?.idGestionPendiente, onClick: () => void cambiarEstado("Descartado") },
            { clave: "reactivar", etiqueta: "Reactivar", icono: "✓", requiereSeleccion: "una" as const, deshabilitado: f?.estado !== "Descartado", onClick: () => void cambiarEstado("Prospecto") },
          ] : []),
          ...(puedePromover ? [{ clave: "promover", etiqueta: "Promover a cliente", icono: "⇧", requiereSeleccion: "una" as const, deshabilitado: f?.estado !== "Prospecto" || !!f?.idGestionPendiente, onClick: () => { if (f) navigate(`/interlocutores/clientes/nuevo?prospecto=${f.idProspecto}`); } }] : []),
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

    </div>
  );
}
