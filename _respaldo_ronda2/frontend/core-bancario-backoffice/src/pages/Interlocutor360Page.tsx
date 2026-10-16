import { useEffect, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import type {
  ApiErrorBody,
  GestionResumenResponse,
  Interlocutor360Response,
} from "../api/tipos";

/**
 * Consulta 360° de un INTERLOCUTOR (unica app de consulta, sin importar el rol).
 * Muestra datos generales y roles; si es cliente, sus productos; gestiones
 * pendientes y cerradas; y relaciones. Las acciones rapidas dependen de los
 * roles y permisos; las que aun no existen se ven deshabilitadas.
 */

type Pestana = "resumen" | "productos" | "gestiones" | "relaciones" | "datos";

const fmtFecha = (iso?: string | null) => (iso ? new Date(iso).toLocaleDateString("es-CR") : "—");
const fmtFechaHora = (iso?: string | null) => (iso ? new Date(iso).toLocaleString("es-CR") : "—");
const fmtMonto = (n?: number | null) => (n == null ? "—" : n.toLocaleString("es-CR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }));

function Tarjeta({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <section className="seccion">
      <h2>{titulo}</h2>
      {children}
    </section>
  );
}

function TablaGestiones({ filas, vacio }: { filas: GestionResumenResponse[]; vacio: string }) {
  if (filas.length === 0) return <p className="nota">{vacio}</p>;
  return (
    <table className="tabla">
      <thead>
        <tr><th>Gestión</th><th>Tipo</th><th>Estado</th><th>Solicitó</th><th>Fecha</th><th>Campos</th></tr>
      </thead>
      <tbody>
        {filas.map((g) => (
          <tr key={g.idGestion}>
            <td>#{g.idGestion}</td>
            <td>{g.tipo}</td>
            <td>{g.estado}</td>
            <td>{g.solicitadoPor}</td>
            <td>{fmtFechaHora(g.fechaResolucion ?? g.creadoEn)}</td>
            <td>{g.camposCambiados}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function Interlocutor360Page() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const { tienePermiso } = usePermisos();
  const [datos, setDatos] = useState<Interlocutor360Response | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pestana, setPestana] = useState<Pestana>("resumen");

  useEffect(() => {
    let cancelado = false;
    setDatos(null);
    setError(null);
    api
      .get<Interlocutor360Response>(`/api/interlocutores/${id}/360`)
      .then((r) => !cancelado && setDatos(r.data))
      .catch((err) => {
        if (cancelado) return;
        if (isAxiosError<ApiErrorBody>(err) && err.response?.status === 404) setError("El interlocutor no existe.");
        else if (isAxiosError(err) && err.response?.status === 403) setError("No tiene permiso para consultar interlocutores.");
        else setError("No se pudo cargar la vista 360°.");
      });
    return () => {
      cancelado = true;
    };
  }, [api, id]);

  if (error) {
    return (
      <div>
        <p className="mensaje-error">{error}</p>
        <Link to="/interlocutores/consulta">← Volver a la consulta</Link>
      </div>
    );
  }
  if (!datos) return <p className="nota">Cargando...</p>;

  const f = datos.ficha;
  const nombre = [f.nombre1, f.nombre2, f.apellido1, f.apellido2].filter(Boolean).join(" ");
  const esCliente = f.roles.some((r) => r.codigo === "CLIENTE" && r.estado === "Activo");
  const principal = f.identificaciones.find((i) => i.esPrincipal && true) ?? f.identificaciones[0];
  const correo = f.contactos.find((c) => c.tipoCodigo === "CORREO" && c.esPrincipal) ?? f.contactos.find((c) => c.tipoCodigo === "CORREO");
  const movil = f.contactos.find((c) => c.tipoCodigo === "TELEFONO_MOVIL" && c.esPrincipal) ?? f.contactos.find((c) => c.tipoCodigo === "TELEFONO_MOVIL");

  const activo = f.estado === "Activo";
  const puedeModificar = esCliente && tienePermiso("CLI_EDICION") && activo && !f.idGestionPendiente;
  const motivoNoModificar = !activo
    ? "El interlocutor no está activo"
    : f.idGestionPendiente
      ? `Ya tiene la gestión #${f.idGestionPendiente} pendiente`
      : "";

  const pasivos = datos.productos.filter((p) => p.clase === "Pasivo");
  const activos = datos.productos.filter((p) => p.clase === "Activo");
  const enMora = activos.filter((p) => (p.diasMora ?? 0) > 0).length;

  const pestanas: { clave: Pestana; etiqueta: string; oculta?: boolean }[] = [
    { clave: "resumen", etiqueta: "Resumen" },
    { clave: "productos", etiqueta: `Productos (${datos.productos.length})`, oculta: !esCliente },
    { clave: "gestiones", etiqueta: `Gestiones (${datos.gestionesPendientes.length + datos.gestionesCerradas.length})` },
    { clave: "relaciones", etiqueta: `Relaciones (${datos.relaciones.length})` },
    { clave: "datos", etiqueta: "Datos completos" },
  ];

  return (
    <div>
      <div className="v360__cabecera">
        <div className="v360__titulo">
          <h1>{nombre}</h1>
          <div className="v360__meta">
            <span>N.º {f.numero}</span>
            <span>·</span>
            <span>{principal ? `${principal.tipoNombre} ${principal.numero}` : "Sin identificación"}</span>
            <span className={`insignia ${activo ? "insignia--ok" : "insignia--error"}`}>{f.estado}</span>
            {f.roles.filter((r) => r.estado === "Activo").map((r) => (
              <span key={r.codigo} className="insignia">{r.nombre}</span>
            ))}
            {f.idGestionPendiente && <span className="insignia insignia--alerta">Gestión #{f.idGestionPendiente} pendiente</span>}
          </div>
        </div>
        <div className="v360__acciones">
          {esCliente && tienePermiso("CLI_EDICION") && (
            <button type="button" disabled={!puedeModificar} title={motivoNoModificar}
              onClick={() => navigate(`/interlocutores/clientes/${f.idInterlocutor}/modificar`)}>
              ✎ Modificar datos
            </button>
          )}
          {esCliente && <button type="button" className="boton-secundario" disabled title="Disponible con el módulo de Cuentas">Abrir cuenta</button>}
          {esCliente && <button type="button" className="boton-secundario" disabled title="Disponible con el módulo de Préstamos">Solicitar crédito</button>}
          <button type="button" className="boton-secundario" onClick={() => navigate("/interlocutores/consulta")}>Volver</button>
        </div>
      </div>

      <div className="v360__pestanas" role="tablist">
        {pestanas.filter((p) => !p.oculta).map((p) => (
          <button key={p.clave} type="button" role="tab" aria-selected={pestana === p.clave}
            className={`v360__pestana ${pestana === p.clave ? "v360__pestana--activa" : ""}`}
            onClick={() => setPestana(p.clave)}>
            {p.etiqueta}
          </button>
        ))}
      </div>

      {f.idGestionPendiente && (
        <div className="v360__aviso">
          Hay cambios pendientes de aprobación (gestión #{f.idGestionPendiente}). Los datos mostrados son los vigentes.{" "}
          {tienePermiso("GES_APROBAR") && <Link to="/gestiones">Ir a Gestiones</Link>}
        </div>
      )}

      {pestana === "resumen" && (
        <div className="v360__rejilla">
          <Tarjeta titulo="Datos generales">
            <dl className="ficha">
              <dt>Tipo de persona</dt><dd>{f.tipoPersona}</dd>
              <dt>País regulador</dt><dd>{f.paisReguladorNombre}</dd>
              <dt>Unidad (sucursal)</dt><dd>{f.unidadNombre ?? "—"}</dd>
              <dt>Canal de origen</dt><dd>{f.canalOrigenNombre ?? "—"}</dd>
              <dt>Fecha de nacimiento</dt><dd>{fmtFecha(f.fechaNacimiento)}</dd>
              <dt>Correo</dt><dd>{correo?.valor ?? "—"}</dd>
              <dt>Teléfono móvil</dt><dd>{movil?.valor ?? "—"}</dd>
              <dt>Cliente desde</dt><dd>{fmtFecha(f.creadoEn)}</dd>
            </dl>
          </Tarjeta>
          {esCliente && (
            <Tarjeta titulo="Posición con la entidad">
              <dl className="ficha">
                <dt>Cuentas (pasivos)</dt><dd>{pasivos.length}</dd>
                <dt>Créditos (activos)</dt><dd>{activos.length}</dd>
                <dt>Créditos en mora</dt><dd>{enMora}</dd>
              </dl>
              {datos.productos.length === 0 && <p className="nota">Aún no tiene productos contratados.</p>}
            </Tarjeta>
          )}
          <Tarjeta titulo="Gestiones pendientes">
            <TablaGestiones filas={datos.gestionesPendientes} vacio="Sin gestiones pendientes." />
          </Tarjeta>
          <Tarjeta titulo="Relaciones">
            {datos.relaciones.length === 0 ? <p className="nota">Sin relaciones registradas.</p> : (
              <ul className="lista-checkbox">
                {datos.relaciones.slice(0, 5).map((r, k) => (
                  <li key={k}><Link to={`/interlocutores/${r.idInterlocutorRelacionado}`}>{r.nombre}</Link> — {r.tipoRelacion} ({r.sentido})</li>
                ))}
              </ul>
            )}
          </Tarjeta>
        </div>
      )}

      {pestana === "productos" && esCliente && (
        <>
          {(["Pasivo", "Activo"] as const).map((clase) => {
            const filas = clase === "Pasivo" ? pasivos : activos;
            return (
              <Tarjeta key={clase} titulo={clase === "Pasivo" ? "Cuentas (pasivos)" : "Créditos (activos)"}>
                {filas.length === 0 ? <p className="nota">Sin productos.</p> : (
                  <table className="tabla">
                    <thead>
                      <tr><th>Producto</th><th>N.º</th><th>Estado</th><th>Saldo</th>{clase === "Activo" && <><th>Cuota</th><th>Próximo pago</th><th>Mora (días)</th></>}</tr>
                    </thead>
                    <tbody>
                      {filas.map((p) => (
                        <tr key={p.numero}>
                          <td>{p.producto}</td><td>{p.numero}</td><td>{p.estado}</td><td>{fmtMonto(p.saldo)}</td>
                          {clase === "Activo" && <><td>{fmtMonto(p.cuota)}</td><td>{fmtFecha(p.proximoPago)}</td><td>{p.diasMora ?? 0}</td></>}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </Tarjeta>
            );
          })}
        </>
      )}

      {pestana === "gestiones" && (
        <>
          <Tarjeta titulo="Pendientes"><TablaGestiones filas={datos.gestionesPendientes} vacio="Sin gestiones pendientes." /></Tarjeta>
          <Tarjeta titulo="Cerradas"><TablaGestiones filas={datos.gestionesCerradas} vacio="Sin gestiones cerradas." /></Tarjeta>
        </>
      )}

      {pestana === "relaciones" && (
        <Tarjeta titulo="Relaciones con otros interlocutores">
          {datos.relaciones.length === 0 ? <p className="nota">Sin relaciones registradas.</p> : (
            <table className="tabla">
              <thead><tr><th>Interlocutor</th><th>N.º</th><th>Roles</th><th>Relación</th><th>Sentido</th><th>% part.</th></tr></thead>
              <tbody>
                {datos.relaciones.map((r, k) => (
                  <tr key={k}>
                    <td><Link to={`/interlocutores/${r.idInterlocutorRelacionado}`}>{r.nombre}</Link></td>
                    <td>{r.numero}</td><td>{r.roles}</td><td>{r.tipoRelacion}</td><td>{r.sentido}</td>
                    <td>{r.porcentajeParticipacion ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Tarjeta>
      )}

      {pestana === "datos" && (
        <>
          <Tarjeta titulo="Identificaciones">
            <table className="tabla">
              <thead><tr><th>Tipo</th><th>Número</th><th>Institución emisora</th><th>Emisión</th><th>Vencimiento</th><th>Principal</th><th>Auditoría</th></tr></thead>
              <tbody>
                {f.identificaciones.map((i) => (
                  <tr key={i.id}>
                    <td>{i.tipoNombre}</td><td>{i.numero}</td><td>{i.institucionNombre ?? "—"}</td>
                    <td>{fmtFecha(i.fechaEmision)}</td><td>{fmtFecha(i.fechaVencimiento)}</td><td>{i.esPrincipal ? "Sí" : ""}</td>
                    <td className="nota">{i.creadoPor} · {fmtFechaHora(i.creadoEn)}{i.modificadoPor ? ` / mod. ${i.modificadoPor} · ${fmtFechaHora(i.modificadoEn)}` : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Tarjeta>
          <Tarjeta titulo="Direcciones">
            {f.direcciones.length === 0 ? <p className="nota">Sin direcciones.</p> : (
              <table className="tabla">
                <thead><tr><th>Tipo</th><th>Dirección</th><th>Principal</th><th>Auditoría</th></tr></thead>
                <tbody>
                  {f.direcciones.map((d) => (
                    <tr key={d.id}>
                      <td>{d.tipoNombre}</td>
                      <td>{[d.paisNombre, ...d.niveles.map((n) => n.texto), d.codigoPostal, d.detalle].filter(Boolean).join(", ")}</td>
                      <td>{d.esPrincipal ? "Sí" : ""}</td>
                      <td className="nota">{d.creadoPor} · {fmtFechaHora(d.creadoEn)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Tarjeta>
          <Tarjeta titulo="Medios de contacto">
            {f.contactos.length === 0 ? <p className="nota">Sin medios de contacto.</p> : (
              <table className="tabla">
                <thead><tr><th>Tipo</th><th>Valor</th><th>Principal</th><th>Auditoría</th></tr></thead>
                <tbody>
                  {f.contactos.map((c) => (
                    <tr key={c.id}>
                      <td>{c.tipoNombre}</td><td>{c.valor}</td><td>{c.esPrincipal ? "Sí" : ""}</td>
                      <td className="nota">{c.creadoPor} · {fmtFechaHora(c.creadoEn)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Tarjeta>
        </>
      )}
    </div>
  );
}
