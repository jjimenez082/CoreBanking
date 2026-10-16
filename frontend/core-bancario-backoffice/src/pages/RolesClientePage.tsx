import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { fmtFechaUtc, mensajeError } from "../api/mensajes";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { GestionDetalleResponse, RolCliente, RolesClienteResponse, UnidadItem } from "../api/tipos";

type Modo = "agregar" | "quitar" | null;

/**
 * Roles de un cliente. Consultar requiere CLI_ROL_CONSULTA; solicitar cambios requiere ademas CLI_ROL.
 * Patron estandar: lista con casilla + barra de comandos (Agregar rol / Quitar rol). El cambio NO se aplica aqui:
 * se solicita como gestion CLI_ROL (con motivo) y requiere aprobacion. Quitar un rol lo inactiva y lo deja en el historial.
 * Si un rol exige datos que el cliente no tiene (p. ej. dirección, correo y teléfono), primero se completan con «Modificar datos».
 */
export function RolesClientePage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const idCliente = Number(id);
  const { tienePermiso } = usePermisos();
  const puedeSolicitarPermiso = tienePermiso("CLI_ROL");

  const [datos, setDatos] = useState<RolesClienteResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());
  const [modo, setModo] = useState<Modo>(null);
  const [agregar, setAgregar] = useState<Set<number>>(new Set());
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<GestionDetalleResponse | null>(null);
  const [unidades, setUnidades] = useState<UnidadItem[]>([]);
  const [idUnidadEmp, setIdUnidadEmp] = useState(0);
  const [numeroEmp, setNumeroEmp] = useState("");

  useEffect(() => {
    api.get<UnidadItem[]>("/api/catalogos/unidades", { params: { para: "registros" } }).then((r) => setUnidades(r.data)).catch(() => setUnidades([]));
  }, [api]);

  useEffect(() => {
    let cancelado = false;
    api.get<RolesClienteResponse>(`/api/interlocutores/${idCliente}/roles`)
      .then((r) => !cancelado && setDatos(r.data))
      .catch((e) => !cancelado && setError(mensajeError(e, "No se pudieron cargar los roles del cliente.")));
    return () => { cancelado = true; };
  }, [api, idCliente]);

  const bloqueado = !datos || datos.estado !== "Activo" || !!datos.numeroGestionPendiente;
  const motivoBloqueo = !datos ? "" : datos.estado !== "Activo" ? `El cliente está «${datos.estado}»` :
    datos.numeroGestionPendiente ? `El cliente tiene la gestión ${datos.numeroGestionPendiente} pendiente de aprobación` : "";

  const seleccionados: RolCliente[] = useMemo(
    () => (datos?.roles ?? []).filter((r) => seleccion.has(r.idTipoRol)), [datos, seleccion]);
  const quitables = seleccionados.length > 0 && seleccionados.every((r) => r.estado === "Activo" && r.codigo !== "CLIENTE");
  const activos = (datos?.roles ?? []).filter((r) => r.estado === "Activo");
  const quedarian = activos.length - (modo === "quitar" ? seleccionados.length : 0);

  const columnas: ColumnaTabla<RolCliente>[] = [
    { clave: "rol", encabezado: "Rol", textoBusqueda: (r) => r.nombre, render: (r) => <strong>{r.nombre}</strong> },
    { clave: "estado", encabezado: "Estado", filtroLista: true, textoBusqueda: (r) => r.estado,
      render: (r) => <span className={`insignia ${r.estado === "Activo" ? "insignia--ok" : ""}`}>{r.estado}</span> },
    { clave: "desde", encabezado: "Desde", valorOrden: (r) => r.fechaAsignacion, render: (r) => fmtFechaUtc(r.fechaAsignacion) },
    { clave: "nota", encabezado: "Observación", render: (r) => (r.codigo === "CLIENTE" ? <span className="nota">No se puede quitar desde aquí</span> : (r.detalle ?? "")) },
  ];

  function abrir(m: Modo) { setModo(m); setMotivo(""); setAgregar(new Set()); setIdUnidadEmp(0); setNumeroEmp(""); setError(null); }

  const rolEmpleado = (datos?.disponibles ?? []).find((r) => r.codigo === "EMPLEADO");
  const agregaEmpleado = !!rolEmpleado && modo === "agregar" && agregar.has(rolEmpleado.idTipoRol);
  const faltaEmpleado = agregaEmpleado && (!idUnidadEmp || !numeroEmp.trim());

  async function solicitar() {
    if (motivo.trim().length < 10) { setError("Indique el motivo (mínimo 10 caracteres)."); return; }
    setEnviando(true); setError(null);
    try {
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${idCliente}/roles`, {
        agregar: modo === "agregar" ? Array.from(agregar) : [],
        quitar: modo === "quitar" ? seleccionados.map((x) => x.idTipoRol) : [],
        motivo: motivo.trim(),
        empleado: agregaEmpleado ? { idUnidad: idUnidadEmp, numeroEmpleado: numeroEmp.trim() } : null,
      });
      setResultado(r.data);
    } catch (e) {
      setError(mensajeError(e, "No se pudo solicitar el cambio de roles."));
    } finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_ROL_CONSULTA")) return <p className="mensaje-error">No tiene permiso para consultar los roles de clientes.</p>;
  if (!datos) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;

  if (resultado) {
    return (
      <div className="wiz">
        <div className="wiz__titulo"><div><h1>Cambio de roles solicitado</h1></div></div>
        <div className="wiz__aviso">
          Se creó la gestión <strong>{resultado.numero ?? `#${resultado.idGestion}`}</strong>. Los roles no cambian hasta que se apruebe
          {resultado.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
        </div>
        <div style={{ display: "flex", gap: "0.5rem", marginTop: "1rem" }}>
          <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idCliente}`)}>Volver al cliente</button>
        </div>
      </div>
    );
  }

  const ocultoCambios = !puedeSolicitarPermiso;
  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Roles · cliente N.º {datos.numero}</h1>
          <div className="nota">
            {puedeSolicitarPermiso ? "Todo cambio de roles se solicita por gestión y requiere aprobación." : "Solo consulta: no tiene permiso para solicitar cambios de roles."}
          </div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idCliente}`)}>Volver al cliente</button>
      </div>

      {motivoBloqueo && <div className="wiz__alerta">{motivoBloqueo}: no admite cambios de rol por ahora.</div>}
      {error && <p className="mensaje-error">{error}</p>}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <h2 style={{ marginTop: 0 }}>Roles del cliente</h2>
          <TablaDatos
            columnas={columnas}
            filas={datos.roles}
            claveFila={(r) => r.idTipoRol}
            seleccion={seleccion}
            onCambiarSeleccion={(s) => { setSeleccion(s); if (modo === "quitar") setModo(null); }}
            mensajeVacio="El cliente no tiene roles."
            placeholderBusqueda="Buscar rol..."
            filtrosPorColumna
            comandos={[
              { clave: "agregar", etiqueta: "Agregar rol", icono: "➕", requiereSeleccion: "ninguna", oculto: ocultoCambios,
                deshabilitado: bloqueado || datos.disponibles.length === 0, onClick: () => abrir("agregar") },
              { clave: "quitar", etiqueta: "Quitar rol", icono: "⛔", requiereSeleccion: "varias", oculto: ocultoCambios,
                deshabilitado: bloqueado || !quitables, onClick: () => abrir("quitar") },
            ]}
          />
          {seleccionados.length > 0 && !quitables && !ocultoCambios && (
            <p className="nota">Solo se pueden quitar roles activos distintos de Cliente Básico.</p>
          )}
        </div>
      </section>

      {modo === "agregar" && (
        <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
          <div className="wiz__cuerpo">
            <h2 style={{ marginTop: 0 }}>Agregar rol</h2>
            <ul className="lista-checkbox">
              {datos.disponibles.map((r) => {
                const faltan = r.faltantes.length > 0;
                return (
                  <li key={r.idTipoRol}>
                    <label>
                      <input type="checkbox" checked={agregar.has(r.idTipoRol)} disabled={faltan}
                        onChange={() => { const s = new Set(agregar); if (s.has(r.idTipoRol)) s.delete(r.idTipoRol); else s.add(r.idTipoRol); setAgregar(s); }} />{" "}
                      <strong>{r.nombre}</strong>
                      {(r.codigo === "CLIENTE_DEUDOR" || r.codigo === "CLIENTE_CUENTAHABIENTE") &&
                        <span className="nota"> (requiere Cliente Básico; habilita {r.codigo === "CLIENTE_DEUDOR" ? "créditos" : "cuentas"})</span>}
                    </label>
                    {faltan && (
                      <div className="nota" style={{ marginLeft: "1.6rem" }}>
                        Le falta registrar: {r.faltantes.join(", ")}.{" "}
                        {tienePermiso("CLI_EDICION") && <Link to={`/interlocutores/clientes/${idCliente}/modificar`}>Modificar datos</Link>}
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>
            {agregaEmpleado && (
              <div className="wiz__rejilla" style={{ marginTop: "0.75rem" }}>
                <label className="wiz-campo">
                  <span>Unidad del empleado (sucursal o departamento)<span className="wiz-campo__req" aria-hidden="true"> *</span></span>
                  <select value={idUnidadEmp} onChange={(e) => setIdUnidadEmp(Number(e.target.value))}>
                    <option value={0}>Seleccione...</option>
                    {unidades.map((u) => (<option key={u.id} value={u.id}>{u.ruta}</option>))}
                  </select>
                </label>
                <label className="wiz-campo">
                  <span>Número de empleado<span className="wiz-campo__req" aria-hidden="true"> *</span></span>
                  <input type="text" maxLength={30} value={numeroEmp} onChange={(e) => setNumeroEmp(e.target.value)} />
                </label>
              </div>
            )}
            <Motivo valor={motivo} onCambiar={setMotivo} />
            <Acciones etiqueta="Solicitar rol" enviando={enviando} deshabilitado={agregar.size === 0 || motivo.trim().length < 10 || faltaEmpleado} onSolicitar={solicitar} onCancelar={() => setModo(null)} />
          </div>
        </section>
      )}

      {modo === "quitar" && (
        <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
          <div className="wiz__cuerpo">
            <h2 style={{ marginTop: 0 }}>Quitar rol</h2>
            <p>Se solicitará inactivar: <strong>{seleccionados.map((r) => r.nombre).join(", ")}</strong>. El rol queda en el historial con su fecha de baja.</p>
            {quedarian < 1 && <p className="mensaje-error">El cliente debe conservar al menos un rol activo.</p>}
            <Motivo valor={motivo} onCambiar={setMotivo} />
            <Acciones etiqueta="Solicitar inactivación" enviando={enviando} deshabilitado={quedarian < 1 || motivo.trim().length < 10} onSolicitar={solicitar} onCancelar={() => setModo(null)} />
          </div>
        </section>
      )}

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <h2 style={{ marginTop: 0 }}>Historial de roles</h2>
          {datos.historial.length === 0 ? <p className="nota">Sin historial.</p> : (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Rol</th><th>Desde</th><th>Hasta</th></tr></thead>
              <tbody>
                {datos.historial.map((p, i) => (
                  <tr key={`${p.idTipoRol}-${i}`}>
                    <td>{p.nombre}</td><td>{fmtFechaUtc(p.desde)}</td>
                    <td>{p.hasta ? fmtFechaUtc(p.hasta) : <span className="insignia insignia--ok">Vigente</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="nota">Cuando existan productos, aquí se indicará también de cuáles es fiador, codeudor, etc.</p>
        </div>
      </section>
    </div>
  );
}

function Motivo({ valor, onCambiar }: { valor: string; onCambiar: (v: string) => void }) {
  return (
    <label className="wiz-campo" style={{ marginTop: "0.75rem" }}>
      <span>Motivo (mínimo 10 caracteres)</span>
      <textarea rows={3} style={{ width: "100%", maxWidth: 640 }} maxLength={500} value={valor} onChange={(e) => onCambiar(e.target.value)} />
    </label>
  );
}

function Acciones({ etiqueta, enviando, deshabilitado, onSolicitar, onCancelar }:
  { etiqueta: string; enviando: boolean; deshabilitado: boolean; onSolicitar: () => void; onCancelar: () => void }) {
  return (
    <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem" }}>
      <button type="button" disabled={deshabilitado || enviando} onClick={onSolicitar}>{enviando ? "Solicitando…" : etiqueta}</button>
      <button type="button" className="boton-secundario" disabled={enviando} onClick={onCancelar}>Cancelar</button>
    </div>
  );
}
