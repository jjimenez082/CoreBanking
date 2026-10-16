import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { GestionDatos, resumenCampos } from "./GestionDatos";
import { AdjuntosDeGestion } from "../documentos/ArbolDocumentos";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import type { GestionDetalleResponse } from "../api/tipos";

export const PENDIENTE = "Pendiente de Aprobacion";
export const DEVUELTA = "Devuelta";

export const claseEstado = (e: string) =>
  e === PENDIENTE ? "pendiente" : e === DEVUELTA ? "devuelta" : e === "Aplicada" ? "aplicada" : e === "Rechazada" ? "rechazada" : "";

export const textoEstado = (e: string) => (e === PENDIENTE ? "Pendiente de aprobación" : e);


type Pestana = "resumen" | "datos" | "adjuntos" | "aprobacion";
type Accion = "aprobar" | "rechazar" | "devolver" | null;

export const num = (g: { numero?: string | null; idGestion: number }) => g.numero ?? String(g.idGestion);

export function textoRegistro(entidad: string, llave: string): string {
  if (entidad === "INTERLOCUTOR") return `Cliente N.º ${llave}`;
  if (entidad === "INTERLOCUTOR_ALTA") return llave.startsWith("Nuevo:") ? `Alta · ${llave.slice(7)}` : `Cliente N.º ${llave}`;
  if (entidad === "INTERLOCUTOR_KYC") return `KYC del cliente N.º ${llave}`;
  if (entidad === "INTERLOCUTOR_CONSENTIMIENTO") return `Consentimientos del cliente N.º ${llave}`;
  if (entidad === "INTERLOCUTOR_FALLECIMIENTO") return `Fallecimiento del cliente N.º ${llave}`;
  if (entidad === "INTERLOCUTOR_BLOQUEO") return `Bloqueo del cliente N.º ${llave.split("|")[0]}`;
  return `${entidad}${llave ? ` #${llave}` : ""}`;
}

/** dd/mm/yyyy hh:mm:ss en hora local; el servidor guarda UTC sin zona. */
export function fmtFechaHora(s?: string | null): string {
  if (!s) return "";
  const d = new Date(/(Z|[+-]\d\d:?\d\d)$/.test(s) ? s : `${s}Z`);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}


/**
 * Detalle de una gestion en ventana modal (Resumen / Datos / Historial de la aprobacion) con las acciones
 * Aprobar, Devolver, Rechazar y Corregir. Se puede abrir desde cualquier pantalla sin salir de ella.
 * onResuelta se invoca tras aprobar / rechazar / devolver; onCerrar al pulsar Cerrar.
 */
export function GestionDetalleModal({ idGestion, onCerrar, onResuelta }: { idGestion: number; onCerrar: () => void; onResuelta?: (mensaje: string) => void }) {
  const api = useApiClient();
  const navigate = useNavigate();
  const { tienePermiso } = usePermisos();
  const puedeAprobar = tienePermiso("GES_APROBAR");
  const [detalle, setDetalle] = useState<GestionDetalleResponse | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  const [errorDetalle, setErrorDetalle] = useState<string | null>(null);
  const [pestana, setPestana] = useState<Pestana>("resumen");
  const [accion, setAccion] = useState<Accion>(null);
  const [texto, setTexto] = useState("");
  const [procesando, setProcesando] = useState(false);

  async function cargarDetalle(id: number) {
    setErrorCarga(null); setErrorDetalle(null); setAccion(null); setTexto("");
    try {
      const r = await api.get<GestionDetalleResponse>(`/api/gestiones/${id}`);
      setDetalle(r.data);
      // Quien debe resolver va directo a revisar los datos; los demas empiezan por el resumen.
      setPestana(r.data.puedeResolver ? "datos" : "resumen");
    } catch (err) {
      setErrorCarga(mensajeError(err, "No se pudo cargar la gestión."));
    }
  }

  useEffect(() => { void cargarDetalle(idGestion); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [idGestion]);

  async function ejecutar(ruta: "aprobar" | "rechazar" | "devolver", cuerpo?: object, mensajeOk?: string) {
    if (!detalle) return;
    setProcesando(true);
    setErrorDetalle(null);
    try {
      await api.post(`/api/gestiones/${detalle.idGestion}/${ruta}`, cuerpo);
      onResuelta?.(mensajeOk ?? `Gestión ${num(detalle)} resuelta.`);
    } catch (err) {
      setErrorDetalle(mensajeError(err, "Error inesperado al procesar la gestión."));
    } finally {
      setProcesando(false);
    }
  }

  function corregir(d: GestionDetalleResponse) {
    if (d.entidadNegocio === "INTERLOCUTOR") navigate(`/interlocutores/clientes/${d.llaveCruda}/modificar?gestion=${d.idGestion}`);
    else if (d.entidadNegocio === "INTERLOCUTOR_ALTA") navigate(`/interlocutores/clientes/nuevo?gestion=${d.idGestion}`);
    else if (d.entidadNegocio === "TIPO_CAMBIO") navigate(`/tipo-cambio?corregir=${d.idGestion}`);
  }

  const puedeResolver = !!detalle?.puedeResolver && puedeAprobar;
  const puedeCorregir = !!detalle && detalle.estado === DEVUELTA && detalle.esMia;

  if (errorCarga) {
    return (
      <div className="gestion-modal" role="dialog" aria-modal="true">
        <div className="gestion-modal__caja">
          <p className="mensaje-error">{errorCarga}</p>
          <div className="gestion-modal__acciones"><button type="button" className="boton-secundario" onClick={onCerrar} style={{ marginLeft: "auto" }}>Cerrar</button></div>
        </div>
      </div>
    );
  }

  return (
    detalle && (
        <div className="gestion-modal" role="dialog" aria-modal="true" aria-label={`Gestión ${detalle.idGestion}`}>
          <div className="gestion-modal__caja">
            <h3>Gestión {num(detalle)} — {detalle.tipoGestion}</h3>
            {errorDetalle && <p className="mensaje-error" style={{ whiteSpace: "pre-line" }}>{errorDetalle}</p>}
            <div className="gestion-tabs" role="tablist">
              <button type="button" role="tab" aria-selected={pestana === "resumen"} className={pestana === "resumen" ? "activo" : ""} onClick={() => setPestana("resumen")}>Resumen</button>
              <button type="button" role="tab" aria-selected={pestana === "datos"} className={pestana === "datos" ? "activo" : ""} onClick={() => setPestana("datos")}>
                Datos <span className="gestion-datos__cuenta">{detalle.campos.length}</span>
              </button>
              <button type="button" role="tab" aria-selected={pestana === "adjuntos"} className={pestana === "adjuntos" ? "activo" : ""} onClick={() => setPestana("adjuntos")}>
                Adjuntos
              </button>
              <button type="button" role="tab" aria-selected={pestana === "aprobacion"} className={pestana === "aprobacion" ? "activo" : ""} onClick={() => setPestana("aprobacion")}>
                Historial de la aprobación <span className="gestion-datos__cuenta">{(detalle.niveles?.length ?? 0) + 1}</span>
              </button>
            </div>
            <div className="gestion-modal__cuerpo">
              {pestana === "resumen" && (
                <>
            <dl className="ficha">
              <dt>Registro</dt>
              <dd>
                {(detalle.entidadNegocio === "INTERLOCUTOR" || detalle.entidadNegocio === "INTERLOCUTOR_KYC" || detalle.entidadNegocio === "INTERLOCUTOR_BLOQUEO" || detalle.entidadNegocio === "INTERLOCUTOR_CONSENTIMIENTO" || detalle.entidadNegocio === "INTERLOCUTOR_DOCUMENTO" || detalle.entidadNegocio === "INTERLOCUTOR_FALLECIMIENTO" || (detalle.entidadNegocio === "INTERLOCUTOR_ALTA" && !detalle.llaveCruda.startsWith("ALTA-"))) && detalle.llaveCruda
                  ? <button type="button" className="boton-enlace-id" title="Abrir la vista 360°" onClick={() => navigate(`/interlocutores/${detalle.llaveCruda.split("|")[0]}`)}>{textoRegistro(detalle.entidadNegocio, detalle.llaveRegistro)}</button>
                  : textoRegistro(detalle.entidadNegocio, detalle.llaveRegistro)}
              </dd>
              <dt>Estado</dt>
              <dd><span className={`estado-gestion estado-gestion--${claseEstado(detalle.estado)}`}>{textoEstado(detalle.estado)}</span></dd>
              {detalle.regla && (
                <>
                  <dt>Regla aplicada</dt>
                  <dd>{detalle.regla}</dd>
                </>
              )}
              {detalle.numeroCorreccion > 0 && (
                <>
                  <dt>Corrección</dt>
                  <dd>
                    N.º {detalle.numeroCorreccion}
                    {detalle.numeroCorreccion >= 2 && <> — <strong>ya fue devuelta {detalle.numeroCorreccion} veces</strong></>}
                  </dd>
                </>
              )}
              {detalle.idGestionOrigen && (
                <>
                  <dt>Corrige la gestión</dt>
                  <dd><button type="button" className="boton-enlace-id" onClick={() => void cargarDetalle(detalle.idGestionOrigen!)}>{detalle.numeroOrigen ?? detalle.idGestionOrigen}</button></dd>
                </>
              )}
              {detalle.idGestionCorreccion && (
                <>
                  <dt>Corregida por la gestión</dt>
                  <dd><button type="button" className="boton-enlace-id" onClick={() => void cargarDetalle(detalle.idGestionCorreccion!)}>{detalle.numeroCorreccionRef ?? detalle.idGestionCorreccion}</button></dd>
                </>
              )}
              <dt>Solicitado por</dt>
              <dd>{detalle.solicitadoPor} · {fmtFechaHora(detalle.creadoEn)}</dd>
              {resumenCampos(detalle.campos) && (
                <>
                  <dt>Contenido</dt>
                  <dd>{resumenCampos(detalle.campos)} <button type="button" className="boton-enlace-id" onClick={() => setPestana("datos")}>Ver datos</button></dd>
                </>
              )}
              {detalle.estado.startsWith("Pendiente") && (detalle.nivelesRequeridos ?? 0) > 0 && (
                <>
                  <dt>Avance</dt>
                  <dd>Nivel {detalle.nivelActual} de {detalle.nivelesRequeridos}{detalle.pendienteDe ? <> · pendiente de {detalle.pendienteDe}</> : null}</dd>
                </>
              )}
            </dl>

            {detalle.comentarioDevolucion && (
              <div className="gestion-modal__devolucion">
                <strong>Comentario de la devolución:</strong>{" "}
                {detalle.comentarioDevolucion}
              </div>
            )}

                </>
              )}
              {pestana === "datos" && <GestionDatos campos={detalle.campos} />}
              {pestana === "adjuntos" && <AdjuntosDeGestion idGestion={detalle.idGestion} />}
              {pestana === "aprobacion" && (
            <table className="tabla gestion-historial">
              <thead><tr><th>Nivel</th><th>Aprobador</th><th>Estado</th><th>Fecha y hora</th><th>Comentario</th></tr></thead>
              <tbody>
                <tr>
                  <td>Solicitud</td>
                  <td>{detalle.solicitadoPor}</td>
                  <td>Ingresó</td>
                  <td>{fmtFechaHora(detalle.creadoEn)}</td>
                  <td>{detalle.comentario ?? "—"}</td>
                </tr>
                {(detalle.niveles ?? []).map((n) => {
                  const resuelto = n.estado === "Aprobado" || n.estado === "Rechazado" || n.estado === "Devuelto";
                  const quien = n.tipo === "AUTOMATICA" ? "Sistema (automática)" : (resuelto ? n.aprobador : null) ?? n.asignado ?? "Cualquier aprobador";
                  const texto = n.estado === "Aprobado" ? "Aprobó" : n.estado === "Rechazado" ? "Rechazó" : n.estado === "Devuelto" ? "Devolvió"
                    : n.estado === "Omitido" ? "Omitido" : "Pendiente";
                  return (
                    <tr key={n.nivel}>
                      <td>{n.nivel}</td>
                      <td>{quien}</td>
                      <td>{texto}</td>
                      <td>{resuelto && n.fecha ? fmtFechaHora(n.fecha) : "—"}</td>
                      <td>{n.comentario ?? "—"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
              )}
            </div>

            {accion && puedeResolver && (
              <div className="formulario-en-linea" style={{ marginTop: "1rem" }}>
                <input
                  type="text" autoFocus maxLength={accion === "devolver" ? 1000 : 500}
                  placeholder={accion === "aprobar" ? "Comentario (opcional)"
                    : accion === "rechazar" ? "Motivo del rechazo (obligatorio)" : "Qué debe corregir o aclarar el solicitante (obligatorio)"}
                  value={texto} onChange={(e) => setTexto(e.target.value)}
                />
                <button
                  type="button" disabled={procesando || (accion !== "aprobar" && texto.trim() === "")}
                  onClick={() => accion === "aprobar"
                    ? void ejecutar("aprobar", { comentario: texto.trim() === "" ? null : texto.trim() }, `Gestión ${num(detalle)} aprobada.`)
                    : accion === "rechazar"
                      ? void ejecutar("rechazar", { motivo: texto }, `Gestión ${num(detalle)} rechazada.`)
                      : void ejecutar("devolver", { comentario: texto }, `Gestión ${num(detalle)} devuelta al solicitante.`)}
                >
                  {accion === "aprobar" ? "Confirmar aprobación" : accion === "rechazar" ? "Confirmar rechazo" : "Confirmar devolución"}
                </button>
                <button type="button" className="boton-secundario" onClick={() => { setAccion(null); setTexto(""); }}>Cancelar</button>
                {accion === "aprobar" && <small className="nota">Si es el último nivel, el cambio se aplica de inmediato.</small>}
              </div>
            )}

            <div className="gestion-modal__acciones">
              {puedeResolver && !accion && (
                <>
                  <button type="button" disabled={procesando} onClick={() => { setAccion("aprobar"); setTexto(""); }}>
                    ✓ Aprobar
                  </button>
                  {detalle.entidadNegocio !== "INTERLOCUTOR_DOCUMENTO" && detalle.entidadNegocio !== "INTERLOCUTOR_FALLECIMIENTO" && (<button type="button" className="boton-aviso" disabled={procesando} onClick={() => { setAccion("devolver"); setTexto(""); }}>↩ Devolver</button>)}
                  <button type="button" className="boton-peligro" disabled={procesando} onClick={() => { setAccion("rechazar"); setTexto(""); }}>✕ Rechazar</button>
                </>
              )}
              {puedeCorregir && (<button type="button" onClick={() => corregir(detalle)}>✎ Corregir y reenviar</button>)}
              <button type="button" className="boton-secundario" onClick={onCerrar} style={{ marginLeft: "auto" }}>Cerrar</button>
            </div>
          </div>
        </div>
      )  );
}
