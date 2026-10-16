import { useEffect, useState, type ReactNode } from "react";
import { GestionDetalleModal } from "../components/GestionDetalleModal";
import { RelacionesTab } from "../components/RelacionesTab";
import { AuditoriaClienteTimeline } from "../components/AuditoriaClienteTimeline";
import { ArbolDocumentos } from "../documentos/ArbolDocumentos";
import { RequisitosDocumentales } from "../documentos/RequisitosDocumentales";
import { FallecimientoModal } from "../components/FallecimientoModal";
import { CAMPO, DatoProtegido, estaEnmascarado, puedeRevelar, useRevelar } from "../components/DatoProtegido";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { fmtFechaUtc } from "../api/mensajes";
import type {
  ApiErrorBody,
  CumplimientoResponse,
  GestionResumenResponse,
  Interlocutor360Response,
} from "../api/tipos";

/**
 * Consulta 360° de un INTERLOCUTOR (unica app de consulta, sin importar el rol).
 * Muestra datos generales y roles; si es cliente, sus productos; gestiones
 * pendientes y cerradas; y relaciones. Las acciones rapidas dependen de los
 * roles y permisos; las que aun no existen se ven deshabilitadas.
 */

type Pestana = "resumen" | "productos" | "gestiones" | "relaciones" | "documentos" | "datos" | "auditoria";

const fmtFecha = (iso?: string | null) => (iso ? new Date(iso).toLocaleDateString("es-CR") : "—");
const fmtFechaHora = (iso?: string | null) => (iso ? new Date(iso).toLocaleString("es-CR") : "—");
const fmtMonto = (n?: number | null) => (n == null ? "—" : n.toLocaleString("es-CR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }));

function Tarjeta({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <section className="seccion">
      <h2>{titulo}</h2>
      <div className="tabla-scroll">{children}</div>
    </section>
  );
}

function TablaGestiones({ filas, vacio, onVer }: { filas: GestionResumenResponse[]; vacio: string; onVer: (idGestion: number) => void }) {
  if (filas.length === 0) return <p className="nota">{vacio}</p>;
  return (
    <table className="tabla tabla--compacta">
      <thead>
        <tr><th>Gestión</th><th>Tipo</th><th>Estado</th><th>Solicitó</th><th>Fecha</th><th>Campos</th></tr>
      </thead>
      <tbody>
        {filas.map((g) => (
          <tr key={g.idGestion}>
            <td>
              <button type="button" className="boton-enlace-id" title="Ver el detalle de la gestión" onClick={() => onVer(g.idGestion)}>
                {g.numero ?? `#${g.idGestion}`}
              </button>
            </td>
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
  const [params] = useSearchParams();
  const pestanaInicial = params.get("pestana");
  const [pestana, setPestana] = useState<Pestana>(
    (["resumen", "productos", "gestiones", "relaciones", "documentos", "datos", "auditoria"] as const).find((x) => x === pestanaInicial) ?? "resumen");

  const [idGestionVer, setIdGestionVer] = useState<number | null>(null);
  const [recarga, setRecarga] = useState(0);
  const [verFallecimiento, setVerFallecimiento] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const [fallecimiento, setFallecimiento] = useState<{ fecha: string | null; numeroGestion: string | null } | null>(null);

  useEffect(() => { setDatos(null); }, [id]);

  useEffect(() => {
    let cancelado = false;
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
  }, [api, id, recarga]);

  const estadoCliente = datos?.ficha.estado;
  useEffect(() => {
    if (estadoCliente !== "Fallecido") { setFallecimiento(null); return; }
    let vivo = true;
    void api.get<{ fecha: string | null; numeroGestion: string | null }>(`/api/interlocutores/${id}/fallecimiento`)
      .then((r) => { if (vivo) setFallecimiento(r.status === 200 ? r.data : null); })
      .catch(() => { if (vivo) setFallecimiento(null); });
    return () => { vivo = false; };
  }, [api, id, estadoCliente, recarga]);

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
  const org = f.organizacion ?? null;
  const esOrg = f.tipoPersona === "Juridica" && !!org;
  const grupo = f.grupo ?? null;
  const esGrupo = f.tipoPersona === "Grupo";
  const nombre = esGrupo ? (grupo?.nombre ?? "Grupo") : esOrg ? org!.razonSocial : [f.nombre1, f.nombre2, f.apellido1, f.apellido2].filter(Boolean).join(" ");
  const esCliente = f.roles.some((r) => r.codigo === "CLIENTE" && r.estado === "Activo");
  const principal = f.identificaciones.find((i) => i.esPrincipal && true) ?? f.identificaciones[0];
  const correo = f.contactos.find((c) => c.tipoCodigo === "CORREO" && c.esPrincipal) ?? f.contactos.find((c) => c.tipoCodigo === "CORREO");
  const movil = f.contactos.find((c) => c.tipoCodigo === "TELEFONO_MOVIL" && c.esPrincipal) ?? f.contactos.find((c) => c.tipoCodigo === "TELEFONO_MOVIL");

  const activo = f.estado === "Activo";
  const fusionado = f.estado === "Fusionado";
  const fallecido = f.estado === "Fallecido";
  const tituloFallecido = "El cliente está fallecido; no admite cambios";
  const puedeModificar = esCliente && tienePermiso("CLI_EDICION") && activo && !f.idGestionPendiente;
  const motivoNoModificar = fallecido
    ? tituloFallecido
    : !activo
    ? "El interlocutor no está activo"
    : f.idGestionPendiente
      ? `Ya tiene la gestión #${f.idGestionPendiente} pendiente`
      : "";

  const pasivos = datos.productos.filter((p) => p.clase === "Pasivo");
  const activos = datos.productos.filter((p) => p.clase === "Activo");
  const enMora = activos.filter((p) => (p.diasMora ?? 0) > 0).length;

  // El numero entre parentesis solo aparece cuando hay algo que atender: productos activos, gestiones pendientes y relaciones activas.
  const productosActivos = datos.productos.filter((p) => !["cancelado", "cancelada", "cerrado", "cerrada", "inactivo", "inactiva"].includes(p.estado.toLowerCase())).length;
  const conteo = (nombre: string, n: number) => (n > 0 ? `${nombre} (${n})` : nombre);

  const pestanas: { clave: Pestana; etiqueta: string; oculta?: boolean }[] = [
    { clave: "resumen", etiqueta: "Resumen" },
    { clave: "datos", etiqueta: "Datos completos" },
    { clave: "productos", etiqueta: conteo("Productos", productosActivos), oculta: !esCliente },
    { clave: "gestiones", etiqueta: conteo("Gestiones", datos.gestionesPendientes.length) },
    { clave: "relaciones", etiqueta: conteo("Relaciones", datos.relaciones.length) },
    { clave: "documentos", etiqueta: "Documentos", oculta: !tienePermiso("DOC_VER") },
    { clave: "auditoria", etiqueta: "Auditoría", oculta: !tienePermiso("CLI_AUDITORIA") },
  ];

  return (
    <div>
      <div className="v360__cabecera">
        <div className="v360__titulo">
          <h1>{esGrupo && "🔗 "}{nombre}</h1>
          <div className="v360__meta">
            <span>N.º {f.numero}</span>
            {!esGrupo && <><span>·</span><span>{principal
              ? <DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={CAMPO.identificacion} idItem={principal.id}
                  mostrado={`${principal.tipoNombre} ${principal.numero}`} real={(v) => `${principal.tipoNombre} ${v.numero}`} />
              : "Sin identificación"}</span></>}
            {esGrupo && grupo?.tipoGrupo && <><span>·</span><span>{grupo.tipoGrupo}</span></>}
            <span className={`insignia ${activo ? "insignia--ok" : "insignia--error"}`}>{f.estado}</span>
            {f.roles.filter((r) => r.estado === "Activo").map((r) => (
              <span key={r.codigo} className="insignia" title={[r.desde ? `Desde ${fmtFechaUtc(r.desde)}` : null, r.detalle].filter(Boolean).join(" · ") || undefined}>{r.nombre}</span>
            ))}
            {f.bloqueado && <span className="insignia insignia--alerta" title="Cliente con bloqueos vigentes">⛔ Bloqueado: {(f.bloqueos ?? []).map((b) => b.tipo).join(", ")}</span>}
            {f.kycVigencia && f.kycVigencia.situacion !== "Vigente" && f.kycVigencia.situacion !== "Sin KYC" && (
              <span className={`insignia ${f.kycVigencia.situacion === "Vencido" ? "insignia--error" : "insignia--alerta"}`} title={f.kycVigencia.mensaje ?? undefined}>KYC {f.kycVigencia.situacion.toLowerCase()}</span>
            )}
            {f.idGestionPendiente && <span className="insignia insignia--alerta">Gestión #{f.idGestionPendiente} pendiente</span>}
          </div>
        </div>
        <div className="v360__acciones">
          {fusionado ? (
            // Un cliente fusionado es solo un alias: no admite cambios, bloqueos, KYC, roles ni productos; todo se hace sobre el cliente que se conservó.
            <>
              {f.idSobreviviente && (
                <button type="button" onClick={() => navigate(`/interlocutores/${f.idSobreviviente}`)}>
                  Ir al cliente N.º {f.numeroSobreviviente}
                </button>
              )}
              <button type="button" className="boton-secundario" onClick={() => navigate("/interlocutores/consulta")}>Volver</button>
            </>
          ) : (<>
          {esCliente && tienePermiso("CLI_EDICION") && (
            <button type="button" disabled={!puedeModificar} title={motivoNoModificar}
              onClick={() => navigate(`/interlocutores/clientes/${f.idInterlocutor}/modificar`)}>
              ✎ Modificar datos
            </button>
          )}
          {(f.tipoPersona === "Fisica" || f.tipoPersona === "Juridica") && tienePermiso("CLI_BLOQUEO_VER") && (
            <button type="button" className="boton-secundario" disabled={fallecido} title={fallecido ? tituloFallecido : undefined} onClick={() => navigate(`/interlocutores/${f.idInterlocutor}/bloqueos`)}>
              ⛔ Bloqueos
            </button>
          )}
          {(f.tipoPersona === "Fisica" || f.tipoPersona === "Juridica") && tienePermiso("CLI_CONSENTIMIENTO_VER") && (
            <button type="button" className="boton-secundario" disabled={fallecido} title={fallecido ? tituloFallecido : undefined} onClick={() => navigate(`/interlocutores/${f.idInterlocutor}/consentimientos`)}>
              ☑ Consentimientos
            </button>
          )}
          {(f.tipoPersona === "Fisica" || f.tipoPersona === "Juridica") && tienePermiso("CLI_KYC") && (
            <button type="button" className="boton-secundario" disabled={!activo || !!f.idGestionPendiente}
              title={fallecido ? tituloFallecido : !activo ? "El interlocutor no está activo" : f.idGestionPendiente ? `Ya tiene la gestión #${f.idGestionPendiente} pendiente` : "Actualizar KYC, riesgo, PEP y sanciones"}
              onClick={() => navigate(`/interlocutores/${f.idInterlocutor}/cumplimiento`)}>
              ✔ Cumplimiento / KYC
            </button>
          )}
          {esGrupo && activo && tienePermiso("CLI_EDICION") && (
            <>
              <button type="button" disabled={!!f.idGestionPendiente}
                title={f.idGestionPendiente ? `Ya tiene la gestión #${f.idGestionPendiente} pendiente` : "Modificar nombre, tipo, descripción o fecha del grupo"}
                onClick={() => navigate(`/interlocutores/${f.idInterlocutor}/grupo/editar`)}>
                ✎ Modificar grupo
              </button>
              <button type="button" className="boton-secundario" disabled={!!f.idGestionPendiente}
                title={f.idGestionPendiente ? `Ya tiene la gestión #${f.idGestionPendiente} pendiente` : "Cerrar las membresías y dar el grupo por disuelto"}
                onClick={() => navigate(`/interlocutores/${f.idInterlocutor}/grupo/disolver`)}>
                ⛔ Disolver grupo
              </button>
            </>
          )}
          {tienePermiso("CLI_FUSION") && activo && !esGrupo && (
            <button type="button" className="boton-secundario" disabled={!!f.idGestionPendiente}
              title={f.idGestionPendiente ? `Ya tiene la gestión #${f.idGestionPendiente} pendiente` : "Fusionar con otro cliente duplicado"}
              onClick={() => navigate(`/interlocutores/${f.idInterlocutor}/fusion`)}>
              ⇄ Fusionar duplicado
            </button>
          )}
          {tienePermiso("CLI_ROL_CONSULTA") && !esGrupo && (
            <button type="button" className="boton-secundario" disabled={fallecido}
              title={fallecido ? tituloFallecido : tienePermiso("CLI_ROL") ? "Ver, agregar o quitar roles (Firmante, Fiador, Deudor...)" : "Ver los roles del cliente"}
              onClick={() => navigate(`/interlocutores/${f.idInterlocutor}/roles`)}>
              {tienePermiso("CLI_ROL") ? "⚙ Gestionar roles" : "👁 Ver roles"}
            </button>
          )}
          {f.tipoPersona === "Fisica" && activo && tienePermiso("CLI_FALLECIMIENTO") && (
            <button type="button" className="boton-secundario" disabled={!!f.idGestionPendiente}
              title={f.idGestionPendiente ? `Ya tiene la gestión #${f.idGestionPendiente} pendiente` : "Registrar el fallecimiento de la persona (requiere aprobación y el acta de defunción)"}
              onClick={() => { setAviso(null); setVerFallecimiento(true); }}>
              ✝ Registrar fallecimiento
            </button>
          )}
          {esCliente && <button type="button" className="boton-secundario" disabled title="Disponible con el módulo de Cuentas">Abrir cuenta</button>}
          {esCliente && <button type="button" className="boton-secundario" disabled title="Disponible con el módulo de Préstamos">Solicitar crédito</button>}
          <button type="button" className="boton-secundario" onClick={() => navigate("/interlocutores/consulta")}>Volver</button>
          </>)}
        </div>
      </div>

      {aviso && <p className="mensaje-exito">{aviso}</p>}
      {f.estado === "Fallecido" && (
        <div className="v360__fallecido" role="status">
          <strong>Cliente fallecido</strong>
          {fallecimiento?.fecha ? ` el ${new Date(`${fallecimiento.fecha}T00:00:00`).toLocaleDateString("es-CR")}` : ""}
          {fallecimiento?.numeroGestion ? ` · gestión ${fallecimiento.numeroGestion}` : ""}
          <span className="nota" style={{ margin: 0 }}> No admite cambios, roles, relaciones, bloqueos, consentimientos ni fusión; sus documentos siguen disponibles.</span>
        </div>
      )}
      {verFallecimiento && (
        <FallecimientoModal idInterlocutor={f.idInterlocutor} nombre={nombre} nacimiento={f.fechaNacimiento} onCerrar={() => setVerFallecimiento(false)}
          onHecho={(m) => { setVerFallecimiento(false); setAviso(m); setRecarga((x) => x + 1); }} />
      )}

      <div className="v360__pestanas" role="tablist">
        {pestanas.filter((p) => !p.oculta).map((p) => (
          <button key={p.clave} type="button" role="tab" aria-selected={pestana === p.clave}
            className={`v360__pestana ${pestana === p.clave ? "v360__pestana--activa" : ""}`}
            onClick={() => setPestana(p.clave)}>
            {p.etiqueta}
          </button>
        ))}
      </div>

      {f.estado === "Fusionado" && f.idSobreviviente && (
        <div className="v360__aviso">
          Este cliente fue fusionado en el cliente <Link to={`/interlocutores/${f.idSobreviviente}`}>N.º {f.numeroSobreviviente}</Link>. Sus datos
          ahora viven allí; este registro se conserva solo como alias.
        </div>
      )}

      {f.kycVigencia && (f.kycVigencia.situacion === "Vencido" || f.kycVigencia.situacion === "Por vencer") && (
        <div className={f.kycVigencia.situacion === "Vencido" ? "wiz__alerta" : "v360__aviso"}>
          <strong>KYC {f.kycVigencia.situacion.toLowerCase()}.</strong> {f.kycVigencia.mensaje}
          {f.kycVigencia.situacion === "Vencido" && f.kycVigencia.fechaBloqueoPrevista && <> Si no se actualiza, se solicitará el bloqueo del cliente a partir del {fmtFechaUtc(f.kycVigencia.fechaBloqueoPrevista)}.</>}
          {tienePermiso("CLI_KYC") && <> <Link to={`/interlocutores/${f.idInterlocutor}/cumplimiento`}>Actualizar KYC</Link></>}
        </div>
      )}

      {f.idGestionPendiente && (
        <div className="v360__aviso">
          Hay cambios pendientes de aprobación (gestión #{f.idGestionPendiente}). Los datos mostrados son los vigentes.{" "}
          {tienePermiso("GES_APROBAR") && <Link to="/gestiones/consulta">Ir a Gestiones</Link>}
        </div>
      )}

      {pestana === "resumen" && (
        <div className="v360__rejilla">
          <Tarjeta titulo="Datos generales">
            <dl className="ficha">
              <dt>Tipo de interlocutor</dt><dd>{esGrupo ? "Grupo" : esOrg ? "Organización" : "Persona"}</dd>
              {esGrupo && grupo && <><dt>Tipo de grupo</dt><dd>{grupo.tipoGrupo ?? "—"}</dd>
              <dt>Miembros vigentes</dt><dd>{grupo.cantidadMiembros}</dd></>}
              {esOrg && <><dt>Forma jurídica</dt><dd>{org!.formaJuridicaNombre ?? "—"}</dd>
              <dt>Estado legal</dt><dd>{org!.estadoLegalNombre ?? "—"}</dd></>}
              <dt>País regulador</dt><dd>{f.paisReguladorNombre}</dd>
              <dt>Unidad (sucursal)</dt><dd>{f.unidadNombre ?? "—"}</dd>
              <dt>Canal de origen</dt><dd>{f.canalOrigenNombre ?? "—"}</dd>
              {esGrupo
                ? <><dt>Fecha de formación</dt><dd>{fmtFecha(grupo?.fechaFormacion)}</dd>
                    {grupo?.descripcion && <><dt>Descripción</dt><dd>{grupo.descripcion}</dd></>}</>
                : esOrg
                  ? <><dt>Fecha de constitución</dt><dd>{fmtFecha(org!.fechaConstitucion)}</dd></>
                  : <><dt>Fecha de nacimiento</dt><dd>
                      <DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={CAMPO.fechaNacimiento}
                        mostrado={f.mascaras?.textos.fechaNacimiento ?? fmtFecha(f.fechaNacimiento)} real={(v) => fmtFecha(v.fechaNacimiento)} /></dd>
                    {f.identidad?.nombreConocido && <><dt>Nombre conocido</dt><dd>{f.identidad.nombreConocido}</dd></>}</>}
              {!esGrupo && <><dt>Correo</dt><dd>{correo
                ? <DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={CAMPO.correo} idItem={correo.id} mostrado={correo.valor} real={(v) => v.valor} />
                : "—"}</dd>
              <dt>Teléfono móvil</dt><dd>{movil
                ? <DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={CAMPO.telefono} idItem={movil.id} mostrado={movil.valor} real={(v) => v.valor} />
                : "—"}</dd></>}
              <dt>{esGrupo ? "Creado" : "Cliente desde"}</dt><dd>{fmtFecha(f.creadoEn)}</dd>
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
            <TablaGestiones filas={datos.gestionesPendientes} vacio="Sin gestiones pendientes." onVer={setIdGestionVer} />
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

      {pestana === "auditoria" && tienePermiso("CLI_AUDITORIA") && (
        <AuditoriaClienteTimeline idInterlocutor={f.idInterlocutor} />
      )}

      {pestana === "documentos" && tienePermiso("DOC_VER") && (
        <Tarjeta titulo="Documentos del cliente">
          <div className="docs-barra">
            <p className="nota" style={{ margin: 0 }}>Consulta del expediente. Agregar, reemplazar u obsoletar documentos se hace desde el Gestor documental.</p>
            <div className="docs-barra__acciones">
              <Link className="boton-secundario boton-enlace" to={`/documentos/expediente?cliente=${f.idInterlocutor}&numero=${encodeURIComponent(String(f.numero ?? ""))}&nombre=${encodeURIComponent(nombre)}&tipoPersona=${esGrupo ? "Grupo" : esOrg ? "Juridica" : "Fisica"}&volver=360`}>Abrir en el Gestor documental</Link>
              {tienePermiso("DOC_SUBIR") && !fusionado && (
                <Link className="boton-enlace" to={`/documentos/cargar?cliente=${f.idInterlocutor}&numero=${encodeURIComponent(String(f.numero ?? ""))}&nombre=${encodeURIComponent(nombre)}&tipoPersona=${esGrupo ? "Grupo" : esOrg ? "Juridica" : "Fisica"}&volver=360`}>＋ Cargar documentos</Link>
              )}
            </div>
          </div>
          <RequisitosDocumentales idInterlocutor={f.idInterlocutor} />
          <ArbolDocumentos idInterlocutor={f.idInterlocutor} />
        </Tarjeta>
      )}

      {pestana === "gestiones" && (
        <>
          <Tarjeta titulo="Pendientes"><TablaGestiones filas={datos.gestionesPendientes} vacio="Sin gestiones pendientes." onVer={setIdGestionVer} /></Tarjeta>
          <Tarjeta titulo="Cerradas"><TablaGestiones filas={datos.gestionesCerradas} vacio="Sin gestiones cerradas." onVer={setIdGestionVer} /></Tarjeta>
        </>
      )}

      {pestana === "relaciones" && (
        <Tarjeta titulo="Relaciones con otros interlocutores">
          <RelacionesTab idInterlocutor={f.idInterlocutor} activo={activo} />
        </Tarjeta>
      )}

      {pestana === "datos" && (
        <>
          <Tarjeta titulo="Roles e historial">
            {(f.historialRoles ?? []).length === 0 ? <p className="nota">Sin roles registrados.</p> : (
              <table className="tabla">
                <thead><tr><th>Rol</th><th>Desde</th><th>Hasta</th></tr></thead>
                <tbody>
                  {(f.historialRoles ?? []).map((p, i) => (
                    <tr key={`${p.idTipoRol}-${i}`}>
                      <td>{p.nombre}</td><td>{fmtFechaUtc(p.desde)}</td>
                      <td>{p.hasta ? fmtFechaUtc(p.hasta) : <span className="insignia insignia--ok">Vigente</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Tarjeta>
          {esOrg && (
            <Tarjeta titulo="Datos de la organización">
              <dl className="ficha">
                <dt>Razón social</dt><dd>{org!.razonSocial}</dd>
                <dt>Forma jurídica</dt><dd>{org!.formaJuridicaNombre ?? "—"}</dd>
                <dt>País de constitución</dt><dd>{org!.paisConstitucionNombre ?? "—"}</dd>
                <dt>Fecha de constitución</dt><dd>{fmtFecha(org!.fechaConstitucion)}</dd>
                <dt>Referencia registral</dt><dd>{org!.referenciaRegistral ?? "—"}</dd>
                <dt>Estado legal</dt><dd>{org!.estadoLegalNombre ?? "—"}</dd>
                {org!.fechaDisolucion && <><dt>Fecha de disolución</dt><dd>{fmtFecha(org!.fechaDisolucion)}</dd></>}
                <dt>Nombres comerciales</dt><dd>{org!.nombresComerciales.map((n) => n.nombre).join(", ") || "—"}</dd>
              </dl>
              <h4>Actividades económicas</h4>
              {org!.actividades.length === 0 ? <p className="nota">Sin actividades registradas.</p> : (
                <table className="tabla">
                  <thead><tr><th>Actividad</th><th>Descripción</th><th>Inicio</th><th>Principal</th></tr></thead>
                  <tbody>
                    {org!.actividades.map((a) => (
                      <tr key={a.id}><td>{a.actividadNombre}</td><td>{a.descripcion ?? "—"}</td><td>{fmtFecha(a.fechaInicio)}</td><td>{a.esPrincipal ? "Sí" : ""}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Tarjeta>
          )}
          {f.tipoPersona === "Fisica" && f.identidad && (
            <Tarjeta titulo="Identidad de la persona">
              <dl className="ficha">
                <dt>Nombre conocido</dt><dd>{f.identidad.nombreConocido ?? "—"}</dd>
                <dt>Sexo</dt><dd>{f.identidad.sexoNombre ?? "—"}</dd>
                <dt>Estado civil</dt><dd>{f.identidad.estadoCivilNombre ?? "—"}</dd>
                <dt>País de nacimiento</dt><dd>{f.identidad.paisNacimientoNombre ?? "—"}</dd>
                <dt>Nacionalidades</dt><dd>{f.identidad.nacionalidades.map((n) => n.paisNombre).join(", ") || "—"}</dd>
              </dl>
            </Tarjeta>
          )}
          {(f.tipoPersona === "Fisica" || f.tipoPersona === "Juridica") && f.perfilFinanciero && <TarjetaPerfilFinanciero f={f} />}
          {(f.tipoPersona === "Fisica" || f.tipoPersona === "Juridica") && tienePermiso("CLI_KYC_VER") && (
            <TarjetaCumplimiento idInterlocutor={f.idInterlocutor} puedeActualizar={tienePermiso("CLI_KYC") && activo && !f.idGestionPendiente} recarga={recarga} />
          )}
          {esGrupo && grupo && (
            <Tarjeta titulo="Datos del grupo">
              <dl className="ficha">
                <dt>Nombre</dt><dd>{grupo.nombre}</dd>
                <dt>Tipo de grupo</dt><dd>{grupo.tipoGrupo ?? "—"}</dd>
                <dt>Fecha de formación</dt><dd>{fmtFecha(grupo.fechaFormacion)}</dd>
                <dt>Descripción</dt><dd>{grupo.descripcion ?? "—"}</dd>
                <dt>Miembros vigentes</dt><dd>{grupo.cantidadMiembros} (se gestionan en la pestaña Relaciones)</dd>
              </dl>
            </Tarjeta>
          )}
          {!esGrupo && (<>
          <Tarjeta titulo="Identificaciones">
            <table className="tabla">
              <thead><tr><th>Tipo</th><th>Número</th><th>Institución emisora</th><th>Emisión</th><th>Vencimiento</th><th>Principal</th><th>Auditoría</th></tr></thead>
              <tbody>
                {f.identificaciones.map((i) => (
                  <tr key={i.id}>
                    <td>{i.tipoNombre}</td>
                    <td><DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={CAMPO.identificacion} idItem={i.id} mostrado={i.numero} real={(v) => v.numero} /></td>
                    <td>{i.institucionNombre ?? "—"}</td>
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
                      <td><DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={CAMPO.direccion} idItem={d.id}
                        mostrado={[d.paisNombre, ...d.niveles.map((n) => n.texto), d.codigoPostal, d.detalle, d.detalle2, d.detalle3].filter(Boolean).join(", ")}
                        real={(v) => [d.paisNombre, ...d.niveles.map((n) => n.texto), d.codigoPostal, v.detalle, v.detalle2, v.detalle3].filter(Boolean).join(", ")} /></td>
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
                      <td>{c.tipoNombre}</td>
                      <td><DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={c.tipoCodigo === "CORREO" || (c.tipoCodigo !== "TELEFONO" && c.valor.includes("@")) ? CAMPO.correo : CAMPO.telefono}
                        idItem={c.id} mostrado={c.valor} real={(v) => v.valor} /></td>
                      <td>{c.esPrincipal ? "Sí" : ""}</td>
                      <td className="nota">{c.creadoPor} · {fmtFechaHora(c.creadoEn)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Tarjeta>
          </>)}
        </>
      )}
      {idGestionVer !== null && (
        <GestionDetalleModal
          idGestion={idGestionVer}
          onCerrar={() => setIdGestionVer(null)}
          onResuelta={() => { setIdGestionVer(null); setRecarga((n) => n + 1); }}
        />
      )}
    </div>
  );
}

const dinero = (v?: number | null, codigo?: string | null) =>
  v == null ? "—" : `${v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}${codigo ? ` ${codigo}` : ""}`;

/** Perfil financiero del cliente: residencia fiscal, perfil económico, ingresos, orígenes y perfil transaccional. */
function TarjetaPerfilFinanciero({ f }: { f: Interlocutor360Response["ficha"] }) {
  const pf = f.perfilFinanciero!;
  const e = pf.economico;
  const esOrg = f.tipoPersona === "Juridica";
  const vacio = !e && pf.residenciasFiscales.length === 0 && pf.ingresos.length === 0 && pf.origenes.length === 0 && pf.perfilTransaccional.length === 0 && (pf.cuentasExternas ?? []).length === 0;
  const fondos = pf.origenes.filter((o) => o.clase === "Fondos").map((o) => o.origenNombre).join(", ");
  const patrimonio = pf.origenes.filter((o) => o.clase === "Patrimonio").map((o) => o.origenNombre).join(", ");

  // Montos enmascarados por el perfil de datos: se muestran como texto (rango) y, si el perfil lo permite, se pueden ver completos.
  const { revelar, cargando: revelando } = useRevelar(f.idInterlocutor);
  const [reales, setReales] = useState<Record<string, string | null> | null>(null);
  const textos = f.mascaras?.textos ?? {};
  const enmascarados = estaEnmascarado(f.mascaras, CAMPO.montos);
  const hay = (clave: string, v?: number | null) => v != null || !!textos[clave];
  const monto = (clave: string, v: number | null | undefined, codigo?: string | null) =>
    reales && reales[clave] != null ? dinero(Number(reales[clave]), codigo)
      : enmascarados && textos[clave] ? `${textos[clave]}${codigo ? ` ${codigo}` : ""}` : dinero(v, codigo);
  return (
    <Tarjeta titulo="Perfil financiero">
      {enmascarados && puedeRevelar(f.mascaras, CAMPO.montos) && (
        <p style={{ margin: "0 0 0.5rem" }}>
          <button type="button" className="boton-secundario" disabled={revelando}
            onClick={async () => { if (reales) { setReales(null); return; } const v = await revelar(CAMPO.montos); if (v) setReales(v); }}>
            {reales ? "Ocultar montos" : "Ver montos"}
          </button>
        </p>
      )}
      {vacio ? <span className="nota">Sin información de perfil financiero registrada.</span> : (
        <>
          <dl className="ficha">
            {pf.residenciasFiscales.length > 0 && (
              <><dt>Residencia fiscal</dt>
                <dd>{pf.residenciasFiscales.map((r) => `${r.paisNombre}${r.identificadorTributario ? ` (${r.identificadorTributario})` : ""}${r.esPrincipal ? " · principal" : ""}`).join("; ")}</dd></>
            )}
            {!esOrg && e?.condicionLaboralNombre && <><dt>Condición laboral</dt><dd>{e.condicionLaboralNombre}</dd></>}
            {!esOrg && e?.ocupacionNombre && <><dt>Ocupación</dt><dd>{e.ocupacionNombre}</dd></>}
            {!esOrg && e?.empleador && <><dt>Empleador o negocio</dt><dd>{e.empleador}</dd></>}
            {!esOrg && hay("eco.gastos", e?.gastosEstimados) && <><dt>Gastos estimados</dt><dd>{monto("eco.gastos", e?.gastosEstimados, e?.monedaBalanceCodigo)}</dd></>}
            {hay("eco.activos", e?.activosDeclarados) && <><dt>Activos declarados</dt><dd>{monto("eco.activos", e?.activosDeclarados, e?.monedaBalanceCodigo)}</dd></>}
            {hay("eco.pasivos", e?.pasivosDeclarados) && <><dt>Pasivos declarados</dt><dd>{monto("eco.pasivos", e?.pasivosDeclarados, e?.monedaBalanceCodigo)}</dd></>}
            {esOrg && hay("eco.patrimonio", e?.patrimonioDeclarado) && <><dt>Patrimonio declarado</dt><dd>{monto("eco.patrimonio", e?.patrimonioDeclarado, e?.monedaBalanceCodigo)}</dd></>}
            {e?.fechaCorte && <><dt>Fecha de corte</dt><dd>{e.fechaCorte.slice(0, 10)}</dd></>}
            {fondos && <><dt>Origen de fondos</dt><dd>{fondos}</dd></>}
            {patrimonio && <><dt>Origen del patrimonio</dt><dd>{patrimonio}</dd></>}
          </dl>
          {pf.ingresos.length > 0 && (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Ingreso</th><th>Monto</th><th>Periodicidad</th></tr></thead>
              <tbody>{pf.ingresos.map((i) => (<tr key={i.id}><td>{i.tipoNombre}</td><td>{monto(`ing.${i.id}`, i.monto, i.monedaCodigo)}</td><td>{i.periodicidadNombre}</td></tr>))}</tbody>
            </table>
          )}
          {pf.perfilTransaccional.length > 0 && (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Transacción esperada</th><th>Frecuencia</th><th>Rango esperado</th><th>Origen → Destino</th></tr></thead>
              <tbody>{pf.perfilTransaccional.map((t) => (
                <tr key={t.id}><td>{t.tipoNombre}</td><td>{t.frecuenciaNombre}</td><td>{!hay(`tra.${t.id}.min`, t.montoMinimo) || !hay(`tra.${t.id}.max`, t.montoMaximo) ? "—" : `${monto(`tra.${t.id}.min`, t.montoMinimo, t.monedaCodigo)} a ${monto(`tra.${t.id}.max`, t.montoMaximo, t.monedaCodigo)}`}</td>
                  <td>{t.paisOrigenNombre ?? "—"} → {t.paisDestinoNombre ?? "—"}</td></tr>))}</tbody>
            </table>
          )}
          {(pf.cuentasExternas ?? []).length > 0 && (
            <table className="tabla tabla--compacta">
              <thead><tr><th>Entidad</th><th>IBAN</th><th>Tipo</th><th>Moneda</th></tr></thead>
              <tbody>{(pf.cuentasExternas ?? []).map((x) => (<tr key={x.id}><td>{x.entidadNombre}</td>
                <td><DatoProtegido idInterlocutor={f.idInterlocutor} mascaras={f.mascaras} campo={CAMPO.cuentaExterna} idItem={x.id} mostrado={x.numeroCuenta} real={(v) => v.numeroCuenta} /></td><td>{x.tipoCuentaNombre}</td><td>{x.monedaCodigo ?? "—"}</td></tr>))}</tbody>
            </table>
          )}
        </>
      )}
    </Tarjeta>
  );
}

/** KYC y cumplimiento del cliente: estado, riesgo, segmentos, motivos, expedientes y evaluaciones PEP / sanciones vigentes. */
function TarjetaCumplimiento({ idInterlocutor, puedeActualizar, recarga }: { idInterlocutor: number; puedeActualizar: boolean; recarga: number }) {
  const api = useApiClient();
  const navigate = useNavigate();
  const [c, setC] = useState<CumplimientoResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let cancelado = false;
    api.get<CumplimientoResponse>(`/api/interlocutores/${idInterlocutor}/cumplimiento`)
      .then((r) => !cancelado && setC(r.data))
      .catch(() => !cancelado && setError("No se pudo cargar el cumplimiento."));
    return () => { cancelado = true; };
  }, [api, idInterlocutor, recarga]);

  const k = c?.kyc;
  const vigente = (tipo: "PEP" | "Sanciones") => c?.evaluaciones.find((e) => e.tipo === tipo && e.vigente);
  const pep = vigente("PEP");
  const san = vigente("Sanciones");
  const vacio = !k && (c?.motivosRiesgo.length ?? 0) === 0 && (c?.expedientes.length ?? 0) === 0 && (c?.evaluaciones.length ?? 0) === 0;
  return (
    <Tarjeta titulo="Cumplimiento y KYC">
      {error ? <span className="mensaje-error">{error}</span> : !c ? <span className="nota">Cargando...</span> : (
        <>
          {vacio ? <span className="nota">Sin información de KYC registrada.</span> : (
            <dl className="ficha">
              <dt>Estado KYC</dt><dd>{k?.estadoKyc ?? "—"}</dd>
              {c.vigencia && c.vigencia.situacion !== "Sin KYC" && (
                <><dt>Vigencia</dt><dd><span className={`insignia ${c.vigencia.situacion === "Vencido" ? "insignia--error" : c.vigencia.situacion === "Por vencer" ? "insignia--alerta" : "insignia--ok"}`}>{c.vigencia.situacion}</span>
                  {c.vigencia.diasParaRevision != null && <span className="nota" style={{ marginLeft: "0.5rem" }}>{c.vigencia.diasParaRevision >= 0 ? `faltan ${c.vigencia.diasParaRevision} días` : `vencido hace ${-c.vigencia.diasParaRevision} días`}</span>}</dd></>
              )}
              <dt>Nivel de debida diligencia</dt><dd>{k?.nivelDebidaDiligencia ?? "—"}</dd>
              <dt>Clasificación de riesgo</dt><dd>{k?.clasificacionRiesgo ?? "—"}{c.motivosRiesgo.length > 0 && ` · ${c.motivosRiesgo.map((m) => m.motivo).join(", ")}`}</dd>
              <dt>Última / próxima revisión</dt><dd>{fmtFecha(k?.fechaUltimaRevision)} / {fmtFecha(k?.fechaProximaRevision)}</dd>
              <dt>Segmentos</dt><dd>{[k?.segmentoCliente, k?.segmentoPricing, k?.segmentoRiesgo].filter(Boolean).join(" · ") || "—"}</dd>
              <dt>Evaluación PEP vigente</dt><dd>{pep ? `${pep.resultado} (${fmtFecha(pep.fecha)}, ${pep.fuente})` : "—"}</dd>
              <dt>Evaluación de sanciones vigente</dt><dd>{san ? `${san.resultado} (${fmtFecha(san.fecha)}, ${san.fuente})` : "—"}</dd>
              {c.expedientes.length > 0 && <><dt>Expediente</dt><dd>{c.expedientes.map((x) => `${x.referencia}${x.sistema ? ` (${x.sistema})` : ""}`).join("; ")}</dd></>}
            </dl>
          )}
          {puedeActualizar && (
            <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idInterlocutor}/cumplimiento`)}>Actualizar cumplimiento</button>
          )}
        </>
      )}
    </Tarjeta>
  );
}
