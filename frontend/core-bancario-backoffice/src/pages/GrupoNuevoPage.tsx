import { useEffect, useState, type ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import type { CatalogoItemResponse, InterlocutorAltaGestionResponse, InterlocutorBusquedaResponse, UnidadItem } from "../api/tipos";

function Campo({ etiqueta, children, obligatorio }: { etiqueta: string; children: ReactNode; obligatorio?: boolean }) {
  return (
    <label className="wiz-campo">
      <span>{etiqueta}{obligatorio && " *"}</span>
      {children}
    </label>
  );
}

interface MiembroForm { id: number; numero: string; nombre: string; funcion: string }

/**
 * Alta de un Grupo (grupo económico, familiar, solidario, consorcio...). Se solicita como gestión CLI_ALTA.
 * Un grupo no tiene identificaciones, direcciones ni roles; su razón de ser son sus miembros (mínimo 2, personas u organizaciones).
 */
export function GrupoNuevoPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { tienePermiso } = usePermisos();

  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [paises, setPaises] = useState<CatalogoItemResponse[]>([]);
  const [unidades, setUnidades] = useState<UnidadItem[]>([]);
  const [cargado, setCargado] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [nombre, setNombre] = useState("");
  const [idTipo, setIdTipo] = useState(0);
  const [descripcion, setDescripcion] = useState("");
  const [formacion, setFormacion] = useState("");
  const [idPais, setIdPais] = useState(0);
  const [idUnidad, setIdUnidad] = useState(0);
  const [miembros, setMiembros] = useState<MiembroForm[]>([]);
  const [busqueda, setBusqueda] = useState("");
  const [encontrados, setEncontrados] = useState<InterlocutorBusquedaResponse[]>([]);
  const [errores, setErrores] = useState<string[]>([]);
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<InterlocutorAltaGestionResponse | null>(null);

  useEffect(() => {
    let cancelado = false;
    Promise.all([
      api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_GRUPO"),
      api.get<CatalogoItemResponse[]>("/api/catalogos/paises-reguladores"),
      api.get<UnidadItem[]>("/api/catalogos/unidades", { params: { para: "registros" } }),
    ]).then(([t, p, u]) => {
      if (cancelado) return;
      setTipos(t.data); setPaises(p.data); setUnidades(u.data);
      setIdPais(p.data.find((x) => x.esPredeterminado)?.id ?? p.data[0]?.id ?? 0);
      setIdUnidad(u.data.find((x) => x.esPredeterminado)?.id ?? 0);
      setCargado(true);
    }).catch((e) => !cancelado && setError(mensajeError(e, "No se pudieron cargar las listas. ¿Corrió grupo_ronda26.sql?")));
    return () => { cancelado = true; };
  }, [api]);

  useEffect(() => {
    if (busqueda.trim().length < 2) { setEncontrados([]); return; }
    let cancelado = false;
    const t = setTimeout(async () => {
      try {
        const r = await api.get<InterlocutorBusquedaResponse[]>("/api/interlocutores/buscar", { params: { q: busqueda.trim() } });
        if (!cancelado) setEncontrados(r.data.filter((x) => x.estado === "Activo" && !miembros.some((m) => m.id === x.idInterlocutor)));
      } catch { if (!cancelado) setEncontrados([]); }
    }, 300);
    return () => { cancelado = true; clearTimeout(t); };
  }, [api, busqueda, miembros]);

  function validar(): string[] {
    const e: string[] = [];
    if (!nombre.trim()) e.push("El nombre del grupo es obligatorio.");
    if (!idTipo) e.push("Elija el tipo de grupo.");
    if (!idPais) e.push("Elija el país regulador.");
    if (unidades.length > 0 && !idUnidad) e.push("Elija la unidad (sucursal).");
    if (miembros.length < 2) e.push("Un grupo necesita al menos 2 miembros.");
    return e;
  }

  async function solicitar() {
    const e = validar();
    setErrores(e);
    if (e.length > 0) return;
    setEnviando(true); setError(null);
    try {
      const r = await api.post<InterlocutorAltaGestionResponse>("/api/interlocutores", {
        idPaisRegulador: idPais, idUnidad: idUnidad || null,
        nombre1: "", apellido1: "", identificaciones: [], direcciones: [], contactos: [],
        tipoPersona: "Grupo",
        grupo: {
          nombre: nombre.trim(), idTipoGrupo: idTipo, descripcion: descripcion.trim() || null, fechaFormacion: formacion || null,
          miembros: miembros.map((m) => ({ idMiembro: m.id, funcion: m.funcion.trim() || null })),
        },
      });
      setResultado(r.data);
    } catch (err) {
      setError(mensajeError(err, "No se pudo solicitar el alta del grupo."));
    } finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_ALTA")) return <p className="mensaje-error">No tiene permiso para dar de alta interlocutores.</p>;

  if (resultado) {
    return (
      <div className="wiz">
        <div className="wiz__titulo"><div><h1>{resultado.aplicada ? "Grupo creado" : "Alta de grupo solicitada"}</h1></div></div>
        <div className="wiz__aviso">
          {resultado.aplicada
            ? <>El grupo «{nombre.trim()}» ya existe (gestión <strong>{resultado.numeroGestion}</strong>).</>
            : <>Se creó la gestión <strong>{resultado.numeroGestion ?? `#${resultado.idGestion}`}</strong>. El grupo se crea cuando se apruebe.</>}
        </div>
        <div style={{ display: "flex", gap: "0.5rem", marginTop: "1rem" }}>
          {resultado.interlocutor && <button type="button" onClick={() => navigate(`/interlocutores/${resultado.interlocutor!.idInterlocutor}`)}>Ver el grupo</button>}
          <button type="button" className="boton-secundario" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          <button type="button" className="boton-secundario" onClick={() => navigate("/interlocutores")}>Volver</button>
        </div>
      </div>
    );
  }

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>🔗 Nuevo grupo</h1>
          <div className="nota">Agrupa personas y organizaciones (grupo económico, familiar, solidario...). Requiere aprobación.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate("/interlocutores")}>Cancelar</button>
      </div>
      {error && <p className="mensaje-error">{error}</p>}
      {!cargado && !error && <p className="nota">Cargando...</p>}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <h2 style={{ marginTop: 0 }}>Datos del grupo</h2>
          <div className="wiz__rejilla">
            <Campo etiqueta="Nombre del grupo" obligatorio><input type="text" maxLength={200} value={nombre} onChange={(e) => setNombre(e.target.value)} /></Campo>
            <Campo etiqueta="Tipo de grupo" obligatorio>
              <select value={idTipo} onChange={(e) => setIdTipo(Number(e.target.value))}>
                <option value={0}>Seleccione...</option>
                {tipos.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
              </select>
            </Campo>
            <Campo etiqueta="Fecha de formación"><input type="date" value={formacion} onChange={(e) => setFormacion(e.target.value)} /></Campo>
            <Campo etiqueta="País regulador" obligatorio>
              <select value={idPais} onChange={(e) => setIdPais(Number(e.target.value))}>
                {paises.map((p) => <option key={p.id} value={p.id}>{p.nombre}</option>)}
              </select>
            </Campo>
            {unidades.length > 0 && (
              <Campo etiqueta="Unidad (sucursal)" obligatorio>
                <select value={idUnidad} onChange={(e) => setIdUnidad(Number(e.target.value))}>
                  <option value={0}>Seleccione...</option>
                  {unidades.map((u) => <option key={u.id} value={u.id}>{u.ruta}</option>)}
                </select>
              </Campo>
            )}
          </div>
          <Campo etiqueta="Descripción (opcional)"><textarea rows={2} maxLength={500} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} /></Campo>
        </div>
      </section>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <h2 style={{ marginTop: 0 }}>Miembros ({miembros.length})</h2>
          <p className="nota" style={{ marginTop: 0 }}>Mínimo 2. Busque personas u organizaciones ya registradas; la función es opcional (p. ej. sociedad controladora, subsidiaria, cabeza de familia).</p>
          <Campo etiqueta="Agregar miembro">
            <input type="text" value={busqueda} placeholder="Buscar por nombre, N.º o identificación" onChange={(e) => setBusqueda(e.target.value)} />
          </Campo>
          {encontrados.length > 0 && (
            <ul className="lista-checkbox" style={{ maxHeight: 180, overflowY: "auto", margin: "0.5rem 0" }}>
              {encontrados.map((x) => (
                <li key={x.idInterlocutor}>
                  <button type="button" className="wiz-enlace"
                    onClick={() => { setMiembros([...miembros, { id: x.idInterlocutor, numero: x.numero, nombre: x.nombre, funcion: "" }]); setBusqueda(""); setEncontrados([]); }}>
                    {x.numero} · {x.nombre}
                  </button>{" "}
                  <span className="nota">{x.identificaciones}</span>
                </li>
              ))}
            </ul>
          )}
          {miembros.length > 0 && (
            <table className="tabla tabla--compacta" style={{ marginTop: 8 }}>
              <thead><tr><th>Miembro</th><th>Función</th><th></th></tr></thead>
              <tbody>
                {miembros.map((m, i) => (
                  <tr key={m.id}>
                    <td>{m.numero} · {m.nombre}</td>
                    <td><input type="text" maxLength={200} value={m.funcion}
                      onChange={(e) => setMiembros(miembros.map((x, k) => (k === i ? { ...x, funcion: e.target.value } : x)))} /></td>
                    <td><button type="button" className="wiz-enlace" onClick={() => setMiembros(miembros.filter((_, k) => k !== i))}>Quitar</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>

      {errores.length > 0 && <div className="wiz__alerta">{errores.map((e) => <div key={e}>{e}</div>)}</div>}
      <div style={{ display: "flex", gap: "0.5rem" }}>
        <button type="button" disabled={!cargado || enviando} onClick={solicitar}>{enviando ? "Solicitando…" : "Solicitar alta del grupo"}</button>
        <Link to="/interlocutores" className="boton-secundario" style={{ padding: "0.45rem 0.9rem" }}>Cancelar</Link>
      </div>
    </div>
  );
}
