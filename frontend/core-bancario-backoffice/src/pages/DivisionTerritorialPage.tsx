import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type {
  ApiErrorBody, CatalogoItemResponse, DireccionConfig, DivisionAdmin, DivisionImportFila, DivisionImportResponse, NivelConfig,
} from "../api/tipos";

function mensaje(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

const Mensaje = ({ texto }: { texto: string }) => (
  <>{texto.split("\n").map((l, i) => (<span key={i}>{l}<br /></span>))}</>
);

/** Separa una linea CSV respetando comillas. Detecta ; o , como separador. */
function parsearCsv(texto: string): DivisionImportFila[] {
  const lineas = texto.replace(/^﻿/, "").split(/\r?\n/).filter((l) => l.trim() !== "");
  if (lineas.length === 0) return [];
  const sep = (lineas[0].match(/;/g)?.length ?? 0) > (lineas[0].match(/,/g)?.length ?? 0) ? ";" : ",";
  const partir = (l: string) => {
    const r: string[] = [];
    let cur = "", q = false;
    for (let i = 0; i < l.length; i++) {
      const c = l[i];
      if (q) { if (c === '"') { if (l[i + 1] === '"') { cur += '"'; i++; } else q = false; } else cur += c; }
      else if (c === '"') q = true;
      else if (c === sep) { r.push(cur); cur = ""; }
      else cur += c;
    }
    r.push(cur);
    return r.map((x) => x.trim());
  };
  const filas = lineas.map(partir);
  const tieneEncabezado = Number.isNaN(Number(filas[0][0]));
  return (tieneEncabezado ? filas.slice(1) : filas).map((c) => ({
    nivel: Number(c[0]), codigo: c[1] ?? "", codigoPadre: c[2] ? c[2] : null, nombre: c[3] ?? "",
  }));
}

/**
 * Administración › División territorial (permiso DIV_ADMINISTRACION).
 * Define por país cómo es la dirección (hasta 4 niveles, de lista o de texto, código postal)
 * y mantiene las divisiones (provincias, cantones, estados...) que alimentan las listas dependientes.
 */
export function DivisionTerritorialPage() {
  const api = useApiClient();
  const [paises, setPaises] = useState<CatalogoItemResponse[]>([]);
  const [idPais, setIdPais] = useState(0);
  const [pestana, setPestana] = useState<"estructura" | "divisiones" | "importar">("estructura");
  const [cfg, setCfg] = useState<DireccionConfig | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);

  useEffect(() => {
    api.get<CatalogoItemResponse[]>("/api/catalogos/listas/PAIS").then((r) => {
      setPaises(r.data);
      setIdPais((p) => p || r.data[0]?.id || 0);
    }).catch((e) => setError(mensaje(e, "No se pudo cargar la lista de países.")));
  }, [api]);

  const cargarCfg = useCallback(async () => {
    if (!idPais) return;
    try {
      const r = await api.get<DireccionConfig>(`/api/division-territorial/paises/${idPais}/config`);
      setCfg(r.data);
    } catch (e) { setError(mensaje(e, "No se pudo cargar la estructura.")); }
  }, [api, idPais]);

  useEffect(() => { setCfg(null); setError(null); setExito(null); void cargarCfg(); }, [cargarCfg]);

  return (
    <div>
      <h1>División territorial</h1>
      <p className="nota">
        Define la estructura de la dirección de cada país y las divisiones (provincia, cantón, estado...) que se ofrecen en listas dependientes.
        Un país sin estructura propia captura 4 niveles de texto libre.
      </p>
      <label className="wiz-campo" style={{ maxWidth: 320 }}>
        <span>País</span>
        <select value={idPais} onChange={(e) => setIdPais(Number(e.target.value))}>
          {paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
        </select>
      </label>

      <div className="v360__pestanas">
        {(["estructura", "divisiones", "importar"] as const).map((p) => (
          <button key={p} className={`v360__pestana ${pestana === p ? "v360__pestana--activa" : ""}`} onClick={() => setPestana(p)}>
            {p === "estructura" ? "Estructura" : p === "divisiones" ? "Divisiones" : "Importar CSV"}
          </button>
        ))}
      </div>

      {error && <p className="mensaje-error"><Mensaje texto={error} /></p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      {cfg && pestana === "estructura" && (
        <Estructura key={`${idPais}-${cfg.esPorDefecto}`} idPais={idPais} cfg={cfg} onGuardado={async () => { setExito("Estructura guardada."); setError(null); await cargarCfg(); }} onError={setError} />
      )}
      {cfg && pestana === "divisiones" && <Divisiones key={idPais} idPais={idPais} niveles={cfg.niveles.filter((n) => n.modo === "Lista")} />}
      {cfg && pestana === "importar" && <Importar key={idPais} idPais={idPais} niveles={cfg.niveles.filter((n) => n.modo === "Lista")} />}
    </div>
  );
}

// ---------------------------------------------------------------------------------------

function Estructura({ idPais, cfg, onGuardado, onError }: {
  idPais: number; cfg: DireccionConfig; onGuardado: () => Promise<void>; onError: (m: string | null) => void;
}) {
  const api = useApiClient();
  const [niveles, setNiveles] = useState<NivelConfig[]>(cfg.niveles);
  const [etiquetaCp, setEtiquetaCp] = useState(cfg.etiquetaCodigoPostal ?? "");
  const [patron, setPatron] = useState(cfg.patronCodigoPostal ?? "");
  const [cpObl, setCpObl] = useState(cfg.codigoPostalObligatorio);
  const [guardando, setGuardando] = useState(false);

  const act = (i: number, p: Partial<NivelConfig>) => setNiveles((ns) => ns.map((n, k) => (k === i ? { ...n, ...p } : n)));

  async function guardar(e: FormEvent) {
    e.preventDefault();
    setGuardando(true);
    onError(null);
    try {
      await api.put(`/api/division-territorial/paises/${idPais}/config`, {
        niveles, etiquetaCodigoPostal: etiquetaCp.trim() || null, patronCodigoPostal: patron.trim() || null,
        codigoPostalObligatorio: cpObl && !!etiquetaCp.trim(),
      });
      await onGuardado();
    } catch (err) { onError(mensaje(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  return (
    <section className="panel-formulario">
      <h3>Estructura de la dirección · {cfg.paisNombre}</h3>
      {cfg.esPorDefecto && <p className="nota">Este país aún no tiene estructura propia (se usan 4 niveles de texto libre). Al guardar se crea la suya.</p>}
      <p className="nota">
        Un nivel de <strong>lista</strong> se elige entre las divisiones registradas y depende del nivel anterior (debe seguir a otro nivel de lista).
        Un nivel de <strong>texto</strong> es de captura libre (p. ej. barrio o ciudad).
      </p>
      <form className="formulario" onSubmit={guardar}>
        <table className="tabla">
          <thead><tr><th>Nivel</th><th>Nombre</th><th>Tipo</th><th>Obligatorio</th></tr></thead>
          <tbody>
            {niveles.map((n, i) => (
              <tr key={n.nivel}>
                <td>{n.nivel}</td>
                <td><input type="text" maxLength={50} value={n.nombre} onChange={(e) => act(i, { nombre: e.target.value })} required /></td>
                <td>
                  <select value={n.modo} onChange={(e) => act(i, { modo: e.target.value as "Lista" | "Texto" })}>
                    <option value="Lista" disabled={i > 0 && niveles[i - 1].modo !== "Lista"}>Lista</option>
                    <option value="Texto">Texto libre</option>
                  </select>
                </td>
                <td><input type="checkbox" checked={n.esObligatorio} onChange={(e) => act(i, { esObligatorio: e.target.checked })} /></td>
              </tr>
            ))}
          </tbody>
        </table>
        <div style={{ display: "flex", gap: "0.5rem", margin: "0.5rem 0" }}>
          <button type="button" className="boton-secundario" disabled={niveles.length >= 4}
            onClick={() => setNiveles((ns) => [...ns, { nivel: ns.length + 1, nombre: "", modo: "Texto", esObligatorio: false }])}>+ Agregar nivel</button>
          <button type="button" className="boton-secundario" disabled={niveles.length <= 1}
            onClick={() => setNiveles((ns) => ns.slice(0, -1))}>Quitar último nivel</button>
        </div>

        <label>
          Código postal — etiqueta (vacío = el país no usa código postal)
          <input type="text" maxLength={50} value={etiquetaCp} onChange={(e) => setEtiquetaCp(e.target.value)} placeholder="Ej. ZIP code" />
        </label>
        <label>
          Formato (expresión regular, opcional)
          <input type="text" maxLength={200} value={patron} onChange={(e) => setPatron(e.target.value)} placeholder="Ej. ^\d{5}(-\d{4})?$" disabled={!etiquetaCp.trim()} />
        </label>
        <label className="wiz-fila__principal">
          <input type="checkbox" checked={cpObl} onChange={(e) => setCpObl(e.target.checked)} disabled={!etiquetaCp.trim()} /> Código postal obligatorio
        </label>
        <p className="nota">Cambiar la estructura no modifica las direcciones ya guardadas; se muestran con la estructura vigente del país.</p>
        <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar estructura"}</button>
      </form>
    </section>
  );
}

// ---------------------------------------------------------------------------------------

function Divisiones({ idPais, niveles }: { idPais: number; niveles: NivelConfig[] }) {
  const api = useApiClient();
  const [nivel, setNivel] = useState(niveles[0]?.nivel ?? 1);
  const [padres, setPadres] = useState<DivisionAdmin[]>([]);
  const [idPadre, setIdPadre] = useState(0);
  const [filas, setFilas] = useState<DivisionAdmin[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState({ codigo: "", nombre: "", estado: "Activo" });
  const [errorForm, setErrorForm] = useState<string | null>(null);

  const nombreNivel = (n: number) => niveles.find((x) => x.nivel === n)?.nombre ?? `Nivel ${n}`;

  // Padres posibles (nivel anterior) para filtrar / crear.
  useEffect(() => {
    setIdPadre(0);
    if (nivel <= 1) { setPadres([]); return; }
    api.get<DivisionAdmin[]>("/api/division-territorial/divisiones", { params: { idPais, nivel: nivel - 1 } })
      .then((r) => setPadres(r.data)).catch(() => setPadres([]));
  }, [api, idPais, nivel]);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const r = await api.get<DivisionAdmin[]>("/api/division-territorial/divisiones", {
        params: { idPais, nivel, idPadre: idPadre || undefined },
      });
      setFilas(r.data); setSel(new Set());
    } catch (e) { setError(mensaje(e, "No se pudieron cargar las divisiones.")); }
    finally { setCargando(false); }
  }, [api, idPais, nivel, idPadre]);

  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<DivisionAdmin>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Código", render: (r) => r.codigo, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: nombreNivel(nivel), render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    ...(nivel > 1 ? [{ clave: "padre", encabezado: nombreNivel(nivel - 1), render: (r: DivisionAdmin) => r.nombrePadre ?? "—", textoBusqueda: (r: DivisionAdmin) => r.nombrePadre ?? "" }] : []),
    { clave: "hijos", encabezado: "Subdivisiones", render: (r) => r.cantidadHijos },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [nivel, niveles]);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const seleccionada = filas.find((f) => f.idDivision === idSel) ?? null;

  async function guardar(e: FormEvent) {
    e.preventDefault();
    setErrorForm(null);
    try {
      if (panel === "nuevo") {
        await api.post("/api/division-territorial/divisiones", {
          idPais, nivel, idDivisionPadre: nivel > 1 ? idPadre || null : null, codigo: form.codigo, nombre: form.nombre,
        });
      } else if (seleccionada) {
        await api.put(`/api/division-territorial/divisiones/${seleccionada.idDivision}`, form);
      }
      setPanel(null);
      await cargar();
    } catch (err) { setErrorForm(mensaje(err, "No se pudo guardar.")); }
  }

  if (niveles.length === 0) return <p className="nota">Este país no tiene niveles de lista: no hay divisiones que mantener.</p>;

  return (
    <div>
      <div style={{ display: "flex", gap: "1rem", flexWrap: "wrap" }}>
        <label className="wiz-campo">
          <span>Nivel</span>
          <select value={nivel} onChange={(e) => { setNivel(Number(e.target.value)); setPanel(null); }}>
            {niveles.map((n) => (<option key={n.nivel} value={n.nivel}>{n.nivel} · {n.nombre}</option>))}
          </select>
        </label>
        {nivel > 1 && (
          <label className="wiz-campo">
            <span>Filtrar por {nombreNivel(nivel - 1)}</span>
            <select value={idPadre} onChange={(e) => setIdPadre(Number(e.target.value))}>
              <option value={0}>{idPadre === 0 ? "Todos (requerido para crear)" : "Todos"}</option>
              {padres.map((p) => (<option key={p.idDivision} value={p.idDivision}>{p.nombre}</option>))}
            </select>
          </label>
        )}
      </div>
      {error && <p className="mensaje-error">{error}</p>}
      {filas.length >= 500 && <p className="nota">Se muestran las primeras 500; use el filtro o el buscador.</p>}

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idDivision}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando} mensajeVacio="No hay divisiones."
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", deshabilitado: nivel > 1 && idPadre === 0,
            onClick: () => { setErrorForm(null); setForm({ codigo: "", nombre: "", estado: "Activo" }); setPanel("nuevo"); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una",
            onClick: () => { if (!seleccionada) return; setErrorForm(null); setForm({ codigo: seleccionada.codigo, nombre: seleccionada.nombre, estado: seleccionada.estado }); setPanel("editar"); } },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {panel && (
        <section className="panel-formulario">
          <h3>{panel === "nuevo" ? `Nuevo (${nombreNivel(nivel)})` : `Editar ${seleccionada?.nombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Código (único en el país)
              <input type="text" maxLength={20} value={form.codigo} onChange={(e) => setForm({ ...form, codigo: e.target.value })} required />
            </label>
            <label>Nombre
              <input type="text" maxLength={150} value={form.nombre} onChange={(e) => setForm({ ...form, nombre: e.target.value })} required />
            </label>
            {panel === "editar" && (
              <label>Estado
                <select value={form.estado} onChange={(e) => setForm({ ...form, estado: e.target.value })}>
                  <option>Activo</option><option>Inactivo</option>
                </select>
              </label>
            )}
            <p className="nota">Una división inactiva deja de ofrecerse en altas nuevas; las direcciones que ya la usan la conservan.</p>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit">Guardar</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </section>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------------------

function Importar({ idPais, niveles }: { idPais: number; niveles: NivelConfig[] }) {
  const api = useApiClient();
  const [texto, setTexto] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<DivisionImportResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const filas = useMemo(() => parsearCsv(texto), [texto]);

  async function enviar() {
    setEnviando(true); setError(null); setResultado(null);
    try {
      const r = await api.post<DivisionImportResponse>("/api/division-territorial/importar", { idPais, filas });
      setResultado(r.data);
    } catch (e) { setError(mensaje(e, "No se pudo importar.")); }
    finally { setEnviando(false); }
  }

  return (
    <section className="panel-formulario">
      <h3>Importar divisiones (CSV)</h3>
      <p className="nota">
        Columnas: <code>nivel;codigo;codigo_padre;nombre</code> (con o sin encabezado; separador <code>;</code> o <code>,</code>).
        Por cada código del país se <strong>crea</strong> la división o, si ya existe, se <strong>actualiza el nombre</strong>.
        Los padres se resuelven por código. Útil para completar o corregir (p. ej. tildes) el catálogo base.
        Niveles de lista de este país: {niveles.map((n) => `${n.nivel}=${n.nombre}`).join(", ") || "ninguno"}.
      </p>
      <input type="file" accept=".csv,text/csv,text/plain" onChange={async (e) => { const f = e.target.files?.[0]; if (f) setTexto(await f.text()); }} />
      <textarea rows={8} style={{ width: "100%", marginTop: "0.5rem" }} value={texto} onChange={(e) => setTexto(e.target.value)}
        placeholder={"nivel;codigo;codigo_padre;nombre\n1;1;;San José\n2;101;1;San José"} />
      <p className="nota">{filas.length} fila(s) leída(s).</p>
      <button disabled={enviando || filas.length === 0} onClick={() => void enviar()}>{enviando ? "Importando..." : "Importar"}</button>
      {error && <p className="mensaje-error">{error}</p>}
      {resultado && (
        <div style={{ marginTop: "0.75rem" }}>
          <p className="mensaje-exito">Creadas: {resultado.creadas} · Actualizadas: {resultado.actualizadas} · Con error: {resultado.errores.length}</p>
          {resultado.errores.length > 0 && (
            <ul className="mensaje-error">{resultado.errores.slice(0, 100).map((x, i) => (<li key={i}>{x}</li>))}</ul>
          )}
        </div>
      )}
    </section>
  );
}
