import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useApiClient } from "../api/useApiClient";
import { PanelModal, VentanaModal } from "../components/VentanaModal";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import type { CatalogoItemResponse } from "../api/tipos";
import type { ReglaAtributo, ReglaCatalogos, ReglaCondicion, ReglaFuncion, ReglaOperador } from "../api/reglas";

interface Form {
  codigo: string; nombre: string; descripcion: string; idAtributo: number; idFuncion: number | null; parametroFuncion: string; idOperador: number;
  origenValor: "Valor" | "Atributo"; valor1: string; valor2: string; valoresLista: string; idAtributoValor: number | null;
}
type Modo = "ver" | "editar" | "nuevo";
const OPERADORES_MULTIVALOR = ["IGUAL", "DISTINTO", "TIENE", "CONTIENE", "EN", "NO_EN", "ES_VACIO", "NO_ES_VACIO"];

/** Tipo del valor a comparar: el del atributo, o el que devuelve la función aplicada. Un multivalor sin función se compara como texto. */
export function tipoEfectivo(attr: ReglaAtributo | undefined, fn: ReglaFuncion | undefined): string {
  if (!attr) return "Texto";
  if (fn) return fn.tipoRetorno;
  return attr.esMultivalor ? "Texto" : attr.tipoDato;
}

/** Pool de condiciones (RUL_CONSULTA ver; RUL_ADMIN mantener). */
export function ReglasCondicionesPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const admin = tienePermiso("RUL_ADMIN");
  const [filas, setFilas] = useState<ReglaCondicion[]>([]);
  const [cat, setCat] = useState<ReglaCatalogos | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [ficha, setFicha] = useState<{ id: number | null; modo: Modo } | null>(null);
  const [duplicar, setDuplicar] = useState<ReglaCondicion | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [c, k] = await Promise.all([api.get<ReglaCondicion[]>("/api/reglas/condiciones"), api.get<ReglaCatalogos>("/api/reglas/catalogos")]);
      setFilas(c.data); setCat(k.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar las condiciones.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<ReglaCondicion>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => <strong>{r.codigo}</strong>, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Condición", render: (r) => (
        <button type="button" className="boton-enlace-id" title="Consultar la condición" style={{ opacity: r.estado === "Activo" ? 1 : 0.55 }}
          onClick={() => { setExito(null); setFicha({ id: r.idCondicion, modo: "ver" }); }}>{r.nombre}</button>
      ), textoBusqueda: (r) => r.nombre },
    { clave: "resumen", encabezado: "Se cumple cuando", render: (r) => r.resumen, textoBusqueda: (r) => r.resumen },
    { clave: "attr", encabezado: "Atributo", render: (r) => r.atributoNombre, textoBusqueda: (r) => r.atributoNombre, filtroLista: true },
    { clave: "usos", encabezado: "Versiones que la usan", render: (r) => r.usos, valorOrden: (r) => r.usos },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const cond = filas.find((x) => x.idCondicion === idSel) ?? null;
  const condFicha = ficha?.id != null ? filas.find((x) => x.idCondicion === ficha.id) ?? null : null;

  async function estado(e: "Activo" | "Inactivo") {
    if (!cond) return;
    setExito(null); setError(null);
    try { await api.put(`/api/reglas/condiciones/${cond.idCondicion}/estado`, { estado: e }); setExito(e === "Activo" ? "Condición activada." : "Condición inactivada."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Condiciones</h1>
      <p className="nota">Piezas reutilizables que las reglas combinan con Y / O. Una condición compara un dato (opcionalmente transformado por una función) contra un valor u otro dato. Si una condición ya forma parte de una versión enviada a aprobación o publicada no se modifica: duplíquela y cambie la copia.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idCondicion} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="Aún no hay condiciones." filtrosPorColumna
        comandos={[
          { clave: "nuevo", etiqueta: "Nueva", icono: "+", oculto: !admin, onClick: () => { setExito(null); setFicha({ id: null, modo: "nuevo" }); } },
          { clave: "duplicar", etiqueta: "Duplicar", icono: "⧉", requiereSeleccion: "una", oculto: !admin, onClick: () => { if (cond) setDuplicar(cond); } },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !admin, deshabilitado: cond?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !admin, deshabilitado: cond?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />

      {ficha && cat && (ficha.id == null || condFicha) && (
        <FichaCondicion key={ficha.id ?? "nueva"} condicion={condFicha} modoInicial={ficha.modo} cat={cat} admin={admin}
          onCerrar={() => setFicha(null)} onRefrescar={cargar} onDuplicar={(c) => { setFicha(null); setDuplicar(c); }}
          onCreado={async () => { setFicha(null); setExito("Condición creada."); await cargar(); }} />
      )}
      {duplicar && <DuplicarPieza titulo="Duplicar condición" origen={duplicar} url={`/api/reglas/condiciones/${duplicar.idCondicion}/duplicar`} onCerrar={() => setDuplicar(null)}
        onListo={async () => { setDuplicar(null); setExito("Condición duplicada."); await cargar(); }} />}
    </div>
  );
}

/** Modal para duplicar una condición o una acción: pide código y nombre nuevos. */
export function DuplicarPieza({ titulo, origen, url, onCerrar, onListo }: { titulo: string; origen: { codigo: string; nombre: string }; url: string; onCerrar: () => void; onListo: () => Promise<void> }) {
  const api = useApiClient();
  const [codigo, setCodigo] = useState(`${origen.codigo}-COPIA`);
  const [nombre, setNombre] = useState(`${origen.nombre} (copia)`);
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  async function enviar(e: FormEvent) {
    e.preventDefault(); setEnviando(true); setError(null);
    try { await api.post(url, { codigo, nombre }); await onListo(); }
    catch (err) { setError(mensajeError(err, "No se pudo duplicar.")); }
    finally { setEnviando(false); }
  }
  return (
    <PanelModal onCerrar={onCerrar}>
      <h3>{titulo}</h3>
      <p className="nota">Se crea una copia nueva con las mismas características de <strong>{origen.codigo}</strong>.</p>
      {error && <p className="mensaje-error">{error}</p>}
      <form className="formulario" onSubmit={enviar}>
        <label>Código <input type="text" maxLength={50} value={codigo} onChange={(e) => setCodigo(e.target.value)} required /></label>
        <label>Nombre <input type="text" maxLength={150} value={nombre} onChange={(e) => setNombre(e.target.value)} required /></label>
        <div className="formulario__ancho" style={{ display: "flex", gap: "0.5rem" }}>
          <button type="submit" disabled={enviando}>{enviando ? "Duplicando..." : "Duplicar"}</button>
          <button type="button" className="boton-secundario" onClick={onCerrar}>Cancelar</button>
        </div>
      </form>
    </PanelModal>
  );
}

// ------------------------------------------------------------------------------------------------ Ficha
function FichaCondicion({ condicion, modoInicial, cat, admin, onCerrar, onRefrescar, onCreado, onDuplicar }: {
  condicion: ReglaCondicion | null; modoInicial: Modo; cat: ReglaCatalogos; admin: boolean;
  onCerrar: () => void; onRefrescar: () => Promise<void>; onCreado: () => Promise<void>; onDuplicar: (c: ReglaCondicion) => void;
}) {
  const api = useApiClient();
  const vacio = (): Form => ({ codigo: "", nombre: "", descripcion: "", idAtributo: 0, idFuncion: null, parametroFuncion: "", idOperador: 0, origenValor: "Valor", valor1: "", valor2: "", valoresLista: "", idAtributoValor: null });
  const formDe = (c: ReglaCondicion): Form => ({
    codigo: c.codigo, nombre: c.nombre, descripcion: c.descripcion ?? "", idAtributo: c.idAtributo, idFuncion: c.idFuncion ?? null, parametroFuncion: c.parametroFuncion ?? "", idOperador: c.idOperador,
    origenValor: c.origenValor === "Atributo" ? "Atributo" : "Valor", valor1: c.valor1 ?? "", valor2: c.valor2 ?? "", valoresLista: (c.valoresLista ?? "").split("|").join(", "), idAtributoValor: c.idAtributoValor ?? null,
  });
  const [modo, setModo] = useState<Modo>(modoInicial);
  const [form, setForm] = useState<Form>(() => (condicion ? formDe(condicion) : vacio()));
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [listaLov, setListaLov] = useState<CatalogoItemResponse[] | null>(null);

  const attr = cat.atributos.find((a) => a.idAtributo === form.idAtributo);
  const fn = cat.funciones.find((f) => f.idFuncion === form.idFuncion);
  const tipo = tipoEfectivo(attr, fn);
  const multi = !!attr?.esMultivalor && !fn;
  const operador = cat.operadores.find((o) => o.idOperador === form.idOperador);
  const aridad = operador?.aridad ?? 1;

  const funcionesPosibles = !attr ? [] : cat.funciones.filter((f) => (attr.esMultivalor ? f.codigo === "CANTIDAD" : f.tipoEntrada === (attr.tipoDato === "Lista" ? "Texto" : attr.tipoDato) && f.codigo !== "CANTIDAD"));
  const operadoresPosibles = cat.operadores.filter((o) => (multi ? OPERADORES_MULTIVALOR.includes(o.codigo) : o.tiposDato.includes(tipo)));
  const atributosComparables = cat.atributos.filter((a) => a.idAtributo !== form.idAtributo && !a.esMultivalor && (a.tipoDato === "Lista" ? "Texto" : a.tipoDato) === (tipo === "Lista" ? "Texto" : tipo));

  // Si el atributo tiene una lista de valores del sistema, se ofrece como combo (si la lista no existe, se escribe el valor).
  useEffect(() => {
    setListaLov(null);
    if (!attr?.listaCodigo || fn) return;
    let vivo = true;
    api.get<CatalogoItemResponse[]>(`/api/catalogos/listas/${attr.listaCodigo}`).then((r) => { if (vivo) setListaLov(r.data); }).catch(() => undefined);
    return () => { vivo = false; };
  }, [api, attr?.listaCodigo, fn]);

  function cambiarAtributo(id: number) {
    setForm({ ...form, idAtributo: id, idFuncion: null, parametroFuncion: "", idOperador: 0, origenValor: "Valor", valor1: "", valor2: "", valoresLista: "", idAtributoValor: null });
  }
  function cambiarFuncion(id: number | null) {
    setForm({ ...form, idFuncion: id, parametroFuncion: "", idOperador: 0, origenValor: "Valor", valor1: "", valor2: "", valoresLista: "", idAtributoValor: null });
  }

  async function guardar(e: FormEvent) {
    e.preventDefault(); setGuardando(true); setErrorForm(null);
    const cuerpo = {
      codigo: form.codigo, nombre: form.nombre, descripcion: form.descripcion.trim() || null, idAtributo: form.idAtributo, idFuncion: form.idFuncion,
      parametroFuncion: fn && fn.cantidadParametros > 0 ? form.parametroFuncion.trim() : null, idOperador: form.idOperador,
      origenValor: aridad === 1 ? form.origenValor : "Valor",
      valor1: aridad >= 1 && aridad !== 9 && form.origenValor === "Valor" ? form.valor1.trim() : null,
      valor2: aridad === 2 ? form.valor2.trim() : null,
      valoresLista: aridad === 9 ? form.valoresLista.split(/[,;\n|]/).map((x) => x.trim()).filter(Boolean).join("|") : null,
      idAtributoValor: aridad === 1 && form.origenValor === "Atributo" ? form.idAtributoValor : null,
    };
    try {
      if (modo === "nuevo") { await api.post("/api/reglas/condiciones", cuerpo); await onCreado(); return; }
      if (condicion) await api.put(`/api/reglas/condiciones/${condicion.idCondicion}`, cuerpo);
      setAviso("Condición actualizada."); setModo("ver"); await onRefrescar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }
  const dato = (etiqueta: string, valor: ReactNode) => (<div className="info-fila"><span className="info-fila__etiqueta">{etiqueta}</span><span className="info-fila__valor">{valor}</span></div>);

  function campoValor(valor: string, poner: (v: string) => void) {
    if (tipo === "Fecha") return <input type="date" value={valor} onChange={(e) => poner(e.target.value)} required />;
    if (tipo === "Booleano") return (
      <select value={valor} onChange={(e) => poner(e.target.value)} required>
        <option value="">— Seleccione —</option><option value="true">Sí</option><option value="false">No</option>
      </select>
    );
    if (listaLov && listaLov.length > 0 && !multi) return (
      <select value={valor} onChange={(e) => poner(e.target.value)} required>
        <option value="">— Seleccione —</option>
        {listaLov.map((i) => (<option key={i.id} value={i.codigo}>{i.nombre}</option>))}
      </select>
    );
    return <input type="text" inputMode={tipo === "Numero" ? "decimal" : "text"} maxLength={200} value={valor} onChange={(e) => poner(e.target.value)} required />;
  }

  const bloqueada = !!condicion?.congelada;
  return (
    <VentanaModal titulo={condicion ? `${condicion.codigo} · ${condicion.nombre}` : "Nueva condición"} onCerrar={onCerrar} ancho={820}
      acciones={modo === "ver" && condicion ? (
        <>
          {admin && <button type="button" disabled={bloqueada} title={bloqueada ? "Forma parte de una versión enviada a aprobación o publicada: duplíquela" : undefined}
            onClick={() => { setForm(formDe(condicion)); setErrorForm(null); setAviso(null); setModo("editar"); }}>✎ Editar</button>}
          {admin && <button type="button" className="boton-secundario" onClick={() => onDuplicar(condicion)}>⧉ Duplicar</button>}
        </>
      ) : undefined}>
      {aviso && <p className="mensaje-exito">{aviso}</p>}
      {modo === "ver" && condicion ? (
        <div>
          {dato("Código", condicion.codigo)}
          {dato("Nombre", condicion.nombre)}
          {dato("Descripción", condicion.descripcion || "—")}
          {dato("Se cumple cuando", <strong>{condicion.resumen}</strong>)}
          {dato("Atributo", `${condicion.atributoNombre} (${condicion.atributoCodigo})`)}
          {dato("Versiones que la usan", condicion.usos)}
          {dato("Estado", condicion.estado)}
          {bloqueada && <p className="nota">Esta condición ya forma parte de una versión enviada a aprobación o publicada, por eso no se edita. Use «Duplicar» para crear una copia y cámbiela en el borrador.</p>}
        </div>
      ) : (
        <>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código <input type="text" maxLength={50} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required /></label>
            <label>Nombre <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required /></label>
            <label className="formulario__ancho">Descripción <input type="text" maxLength={500} value={form.descripcion} onChange={(e) => setForm({ ...form, descripcion: e.target.value })} /></label>
            <label>Dato a evaluar
              <select value={form.idAtributo || ""} onChange={(e) => cambiarAtributo(Number(e.target.value))} required>
                <option value="">— Seleccione —</option>
                {cat.atributos.map((a) => (<option key={a.idAtributo} value={a.idAtributo}>{a.nombre}{a.esMultivalor ? " (varios valores)" : ""}</option>))}
              </select>
            </label>
            <label>Transformar con una función
              <select value={form.idFuncion ?? ""} onChange={(e) => cambiarFuncion(e.target.value ? Number(e.target.value) : null)} disabled={!attr}>
                <option value="">— Ninguna (usar el dato tal cual) —</option>
                {funcionesPosibles.map((f) => (<option key={f.idFuncion} value={f.idFuncion}>{f.nombre}</option>))}
              </select>
              {fn?.descripcion && <small className="nota">{fn.descripcion}</small>}
            </label>
            {fn && fn.cantidadParametros > 0 && (
              <label>Parámetro de la función <input type="text" inputMode="decimal" value={form.parametroFuncion} onChange={(e) => setForm({ ...form, parametroFuncion: e.target.value })} required style={{ width: "8rem" }} /></label>
            )}
            <label>Condición
              <select value={form.idOperador || ""} onChange={(e) => setForm({ ...form, idOperador: Number(e.target.value), valor1: "", valor2: "", valoresLista: "", origenValor: "Valor", idAtributoValor: null })} required disabled={!attr}>
                <option value="">— Seleccione —</option>
                {operadoresPosibles.map((o: ReglaOperador) => (<option key={o.idOperador} value={o.idOperador}>{o.nombre}</option>))}
              </select>
            </label>
            {aridad === 1 && !multi && (
              <label>Comparar contra
                <select value={form.origenValor} onChange={(e) => setForm({ ...form, origenValor: e.target.value as "Valor" | "Atributo", valor1: "", idAtributoValor: null })}>
                  <option value="Valor">Un valor fijo</option>
                  <option value="Atributo">Otro dato</option>
                </select>
              </label>
            )}
            {aridad === 1 && form.origenValor === "Valor" && <label>Valor {campoValor(form.valor1, (v) => setForm({ ...form, valor1: v }))}</label>}
            {aridad === 1 && form.origenValor === "Atributo" && (
              <label>Otro dato
                <select value={form.idAtributoValor ?? ""} onChange={(e) => setForm({ ...form, idAtributoValor: e.target.value ? Number(e.target.value) : null })} required>
                  <option value="">— Seleccione —</option>
                  {atributosComparables.map((a) => (<option key={a.idAtributo} value={a.idAtributo}>{a.nombre}</option>))}
                </select>
              </label>
            )}
            {aridad === 2 && (
              <>
                <label>Desde {campoValor(form.valor1, (v) => setForm({ ...form, valor1: v }))}</label>
                <label>Hasta {campoValor(form.valor2, (v) => setForm({ ...form, valor2: v }))}</label>
              </>
            )}
            {aridad === 9 && (
              <label className="formulario__ancho">Valores (separados por coma)
                <input type="text" maxLength={1000} value={form.valoresLista} onChange={(e) => setForm({ ...form, valoresLista: e.target.value })} required placeholder="CR, PA, GT" />
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
