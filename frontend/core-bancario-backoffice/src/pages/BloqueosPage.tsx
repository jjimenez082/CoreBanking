import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError, fmtFechaUtc } from "../api/mensajes";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { BloqueoListadoItemResponse, InterlocutorListaResponse } from "../api/tipos";

const COLUMNAS: ColumnaTabla<BloqueoListadoItemResponse>[] = [
  { clave: "numero", encabezado: "N.º cliente", render: (f) => <Link to={`/interlocutores/${f.idInterlocutor}`} title="Abrir la vista 360°">{f.numero}</Link>, textoBusqueda: (f) => f.numero, valorOrden: (f) => f.numero },
  { clave: "nombre", encabezado: "Cliente", render: (f) => f.nombre, textoBusqueda: (f) => f.nombre, valorOrden: (f) => f.nombre },
  { clave: "tipo", encabezado: "Tipo de bloqueo", render: (f) => f.tipo, textoBusqueda: (f) => f.tipo, filtroLista: true },
  { clave: "comentario", encabezado: "Comentario", render: (f) => f.comentario, textoBusqueda: (f) => f.comentario },
  { clave: "desde", encabezado: "Bloqueado desde", render: (f) => fmtFechaUtc(f.fechaBloqueo), textoBusqueda: (f) => fmtFechaUtc(f.fechaBloqueo), valorOrden: (f) => f.fechaBloqueo },
  { clave: "por", encabezado: "Solicitó", render: (f) => f.bloqueadoPor, textoBusqueda: (f) => f.bloqueadoPor, filtroLista: true },
];

/**
 * App «Bloqueos de clientes»: lista de clientes con algún bloqueo vigente y acceso a la pantalla de bloqueos de cada cliente.
 * Bloquear y desbloquear se solicitan por gestión con aprobación (pantalla por cliente).
 */
export function BloqueosPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { tienePermiso } = usePermisos();
  const [filas, setFilas] = useState<BloqueoListadoItemResponse[]>([]);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());
  const [buscar, setBuscar] = useState("");
  const [candidatos, setCandidatos] = useState<InterlocutorListaResponse["items"] | null>(null);
  const [buscando, setBuscando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { setFilas((await api.get<BloqueoListadoItemResponse[]>("/api/interlocutores/bloqueos")).data); }
    catch (e) { setError(mensajeError(e, "No se pudo cargar la lista de bloqueos.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  async function buscarCliente() {
    if (!buscar.trim()) return;
    setBuscando(true); setError(null);
    try { setCandidatos((await api.get<InterlocutorListaResponse>("/api/interlocutores", { params: { q: buscar.trim(), pagina: 1, tamanoPagina: 8 } })).data.items); }
    catch (e) { setError(mensajeError(e, "No se pudo buscar el cliente.")); }
    finally { setBuscando(false); }
  }

  const seleccionada = seleccion.size === 1 ? filas.find((f) => f.idBloqueo === Number(Array.from(seleccion)[0])) : undefined;

  if (!tienePermiso("CLI_BLOQUEO_VER")) return <p className="mensaje-error">No tiene permiso para consultar los bloqueos de clientes.</p>;

  return (
    <div>
      <h1>Bloqueos de clientes</h1>
      <p className="nota">Un cliente está bloqueado mientras tenga al menos un bloqueo vigente. Bloquear y desbloquear se solicitan por gestión y requieren aprobación.</p>
      {error && <p className="mensaje-error">{error}</p>}

      {tienePermiso("CLI_BLOQUEO") && (
        <section className="seccion">
          <h2>Bloquear un cliente</h2>
          <div className="bloq-busqueda">
            <input type="text" placeholder="Buscar por nombre, N.º o identificación" value={buscar}
              onChange={(e) => setBuscar(e.target.value)} onKeyDown={(e) => e.key === "Enter" && void buscarCliente()} />
            <button type="button" className="boton-secundario" disabled={buscando || !buscar.trim()} onClick={() => void buscarCliente()}>{buscando ? "Buscando…" : "Buscar"}</button>
          </div>
          {candidatos && (candidatos.length === 0 ? <p className="nota">No se encontraron clientes.</p> : (
            <table className="tabla tabla--compacta">
              <thead><tr><th>N.º</th><th>Nombre</th><th>Identificación</th><th>Estado</th><th /></tr></thead>
              <tbody>{candidatos.map((c) => (
                <tr key={c.idInterlocutor}>
                  <td>{c.numero}</td><td>{c.nombreCompleto}</td><td>{c.numeroIdentificacion}</td><td>{c.estado}</td>
                  <td><button type="button" onClick={() => navigate(`/interlocutores/${c.idInterlocutor}/bloqueos`)}>Gestionar bloqueos</button></td>
                </tr>))}</tbody>
            </table>
          ))}
        </section>
      )}

      <h2 style={{ marginTop: "2rem", marginBottom: "0.25rem" }}>Clientes con bloqueos vigentes</h2>
      <TablaDatos
        columnas={COLUMNAS}
        filas={filas}
        claveFila={(f) => f.idBloqueo}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        mensajeVacio="No hay clientes bloqueados."
        placeholderBusqueda="Buscar por cliente, tipo o comentario"
        filtrosPorColumna
        comandos={[
          { clave: "ver", etiqueta: "Ver 360°", icono: "👁", requiereSeleccion: "una", onClick: () => seleccionada && navigate(`/interlocutores/${seleccionada.idInterlocutor}`) },
          { clave: "gestionar", etiqueta: "Gestionar bloqueos", icono: "⛔", requiereSeleccion: "una", onClick: () => seleccionada && navigate(`/interlocutores/${seleccionada.idInterlocutor}/bloqueos`) },
        ]}
      />
    </div>
  );
}
