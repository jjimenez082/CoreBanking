import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import type { GestionDetalleResponse, GestionResponse } from "../api/tipos";

const PENDIENTE = "Pendiente de Aprobacion";
const DEVUELTA = "Devuelta";

const claseEstado = (e: string) =>
  e === PENDIENTE ? "pendiente" : e === DEVUELTA ? "devuelta" : e === "Aplicada" ? "aplicada" : e === "Rechazada" ? "rechazada" : "";

const textoEstado = (e: string) => (e === PENDIENTE ? "Pendiente de aprobación" : e);

type Accion = "rechazar" | "devolver" | null;

/**
 * Bandeja de Gestiones: los cambios a datos de negocio se solicitan como gestión y se aplican al aprobarse.
 * Se abre el detalle con el ID (o «Ver detalle»); dentro de la pantalla de visualización están las acciones:
 * Aprobar (aplica el cambio), Rechazar (motivo) y Devolver (comentario: vuelve al solicitante para que lo corrija).
 * Quien recibe una gestión devuelta ve «Corregir», que abre la edición con lo que ya había pedido.
 * Las tablas se pueden ordenar (clic en el encabezado) y filtrar por columna.
 */
export function GestionesPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const { tienePermiso } = usePermisos();
  const puedeAprobar = tienePermiso("GES_APROBAR");

  const [gestiones, setGestiones] = useState<GestionResponse[]>([]);
  const [verTodas, setVerTodas] = useState(false);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());

  const [detalle, setDetalle] = useState<GestionDetalleResponse | null>(null);
  const [errorDetalle, setErrorDetalle] = useState<string | null>(null);
  const [accion, setAccion] = useState<Accion>(null);
  const [texto, setTexto] = useState("");
  const [procesando, setProcesando] = useState(false);

  async function cargar() {
    setCargando(true);
    setError(null);
    try {
      const r = await api.get<GestionResponse[]>("/api/gestiones", { params: { estado: verTodas ? "todas" : "pendientes" } });
      setGestiones(r.data);
      setSeleccion(new Set());
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al cargar las gestiones."));
    } finally {
      setCargando(false);
    }
  }

  useEffect(() => {
    void cargar();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [verTodas]);

  async function verDetalle(id: number) {
    setError(null);
    setAviso(null);
    setErrorDetalle(null);
    setAccion(null);
    setTexto("");
    try {
      const r = await api.get<GestionDetalleResponse>(`/api/gestiones/${id}`);
      setDetalle(r.data);
    } catch (err) {
      setError(mensajeError(err, "No se pudo cargar la gestión."));
    }
  }

  // ?ver=N (desde la campanita y los enlaces): abre el detalle directamente.
  useEffect(() => {
    const ver = params.get("ver");
    if (ver) {
      void verDetalle(Number(ver));
      setParams({}, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  function cerrar() {
    setDetalle(null);
    setAccion(null);
    setTexto("");
    setErrorDetalle(null);
  }

  async function ejecutar(ruta: "aprobar" | "rechazar" | "devolver", cuerpo?: object, mensajeOk?: string) {
    if (!detalle) return;
    setProcesando(true);
    setErrorDetalle(null);
    try {
      await api.post(`/api/gestiones/${detalle.idGestion}/${ruta}`, cuerpo);
      setAviso(mensajeOk ?? `Gestión #${detalle.idGestion} resuelta.`);
      cerrar();
      await cargar();
    } catch (err) {
      setErrorDetalle(mensajeError(err, "Error inesperado al procesar la gestión."));
    } finally {
      setProcesando(false);
    }
  }

  function corregir(d: GestionDetalleResponse) {
    if (d.entidadNegocio === "INTERLOCUTOR") navigate(`/interlocutores/clientes/${d.llaveCruda}/modificar?gestion=${d.idGestion}`);
    else if (d.entidadNegocio === "TIPO_CAMBIO") navigate(`/tipo-cambio?corregir=${d.idGestion}`);
  }

  const columnas: ColumnaTabla<GestionResponse>[] = useMemo(() => [
    {
      clave: "id", encabezado: "ID",
      render: (g) => (<button type="button" className="boton-enlace-id" onClick={() => void verDetalle(g.idGestion)}>#{g.idGestion}</button>),
      textoBusqueda: (g) => String(g.idGestion), valorOrden: (g) => g.idGestion,
    },
    { clave: "tipo", encabezado: "Tipo", render: (g) => g.tipoGestion, textoBusqueda: (g) => g.tipoGestion, filtroLista: true },
    {
      clave: "entidad", encabezado: "Registro",
      render: (g) => `${g.entidadNegocio}${g.llaveRegistro ? ` #${g.llaveRegistro}` : ""}`,
      textoBusqueda: (g) => `${g.entidadNegocio} ${g.llaveRegistro}`,
    },
    {
      clave: "estado", encabezado: "Estado",
      render: (g) => <span className={`estado-gestion estado-gestion--${claseEstado(g.estado)}`}>{textoEstado(g.estado)}</span>,
      textoBusqueda: (g) => textoEstado(g.estado), filtroLista: true,
    },
    { clave: "solicitante", encabezado: "Ingresó", render: (g) => g.solicitadoPor, textoBusqueda: (g) => g.solicitadoPor, filtroLista: true },
    {
      clave: "resolvio", encabezado: "Resolvió",
      render: (g) => (g.resueltaPor ? `${g.resolucionTipo}: ${g.resueltaPor}` : "—"),
      textoBusqueda: (g) => (g.resueltaPor ? `${g.resolucionTipo} ${g.resueltaPor}` : ""),
    },
    {
      clave: "correccion", encabezado: "Corrección",
      render: (g) => (g.numeroCorreccion > 0
        ? <span className={g.numeroCorreccion >= 2 ? "estado-gestion estado-gestion--devuelta" : undefined} title={g.idGestionOrigen ? `Corrige la gestión #${g.idGestionOrigen}` : undefined}>n.º {g.numeroCorreccion}</span>
        : "—"),
      textoBusqueda: (g) => (g.numeroCorreccion > 0 ? String(g.numeroCorreccion) : ""), valorOrden: (g) => g.numeroCorreccion,
    },
    {
      clave: "fecha", encabezado: "Fecha", render: (g) => new Date(g.creadoEn).toLocaleString(),
      textoBusqueda: (g) => new Date(g.creadoEn).toLocaleString(), valorOrden: (g) => new Date(g.creadoEn).getTime(),
    },
  // eslint-disable-next-line react-hooks/exhaustive-deps
  ], []);

  const idSeleccionada = seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
  const puedeResolver = !!detalle?.puedeResolver && puedeAprobar;
  const puedeCorregir = !!detalle && detalle.estado === DEVUELTA && detalle.esMia;

  return (
    <div>
      <h1>Gestiones</h1>
      <p className="nota">
        Los cambios a datos de negocio se solicitan como gestión y se aplican al aprobarse. Abra una gestión con su número (o márquela y use
        «Ver detalle») para aprobarla, rechazarla o devolverla al solicitante. Puede ordenar y filtrar por columna.
      </p>

      {error && <p className="mensaje-error">{error}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}

      <TablaDatos
        columnas={columnas}
        filas={gestiones}
        claveFila={(g) => g.idGestion}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        filtrosPorColumna
        mensajeVacio={verTodas ? "No hay gestiones." : "No hay gestiones pendientes en este momento."}
        placeholderBusqueda="Buscar gestión"
        comandos={[
          { clave: "detalle", etiqueta: "Ver detalle", icono: "👁", requiereSeleccion: "una", onClick: () => idSeleccionada !== null && void verDetalle(idSeleccionada) },
          { clave: "filtro", etiqueta: verTodas ? "Ver solo pendientes" : "Ver todas", icono: "⇄", onClick: () => setVerTodas((v) => !v) },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {detalle && (
        <div className="gestion-modal" role="dialog" aria-modal="true" aria-label={`Gestión ${detalle.idGestion}`}>
          <div className="gestion-modal__caja">
            <h3>Gestión #{detalle.idGestion} — {detalle.tipoGestion}</h3>
            {errorDetalle && <p className="mensaje-error" style={{ whiteSpace: "pre-line" }}>{errorDetalle}</p>}
            <dl className="ficha">
              <dt>Registro</dt>
              <dd>{detalle.entidadNegocio} #{detalle.llaveRegistro}</dd>
              <dt>Estado</dt>
              <dd><span className={`estado-gestion estado-gestion--${claseEstado(detalle.estado)}`}>{textoEstado(detalle.estado)}</span></dd>
              <dt>Ingresada por</dt>
              <dd>{detalle.solicitadoPor} el {new Date(detalle.creadoEn).toLocaleString()}</dd>
              {detalle.resolucionTipo && detalle.resueltaPor && (
                <>
                  <dt>{detalle.resolucionTipo === "Aprobó" ? "Aprobada por" : detalle.resolucionTipo === "Rechazó" ? "Rechazada por" : "Devuelta por"}</dt>
                  <dd>{detalle.resueltaPor}{detalle.resueltaEn ? ` el ${new Date(detalle.resueltaEn.endsWith("Z") ? detalle.resueltaEn : `${detalle.resueltaEn}Z`).toLocaleString()}` : ""}</dd>
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
                  <dd><button type="button" className="boton-enlace-id" onClick={() => void verDetalle(detalle.idGestionOrigen!)}>#{detalle.idGestionOrigen}</button></dd>
                </>
              )}
              {detalle.idGestionCorreccion && (
                <>
                  <dt>Corregida por la gestión</dt>
                  <dd><button type="button" className="boton-enlace-id" onClick={() => void verDetalle(detalle.idGestionCorreccion!)}>#{detalle.idGestionCorreccion}</button></dd>
                </>
              )}
              {detalle.comentario && (<><dt>Comentario</dt><dd>{detalle.comentario}</dd></>)}
              {detalle.fechaResolucion && (<><dt>Resuelta</dt><dd>{new Date(detalle.fechaResolucion).toLocaleString()}</dd></>)}
              {detalle.motivoRechazo && (<><dt>Motivo del rechazo</dt><dd>{detalle.motivoRechazo}</dd></>)}
            </dl>

            {detalle.comentarioDevolucion && (
              <div className="gestion-modal__devolucion">
                <strong>Comentario de la devolución:</strong>{" "}
                {detalle.comentarioDevolucion}
              </div>
            )}

            <table className="tabla">
              <thead><tr><th>Campo</th><th>Valor anterior</th><th>Valor nuevo</th></tr></thead>
              <tbody>
                {detalle.campos.map((c) => (
                  <tr key={c.nombreCampo}>
                    <td>{c.etiqueta}</td>
                    <td>{c.valorAnterior ?? "—"}</td>
                    <td><strong>{c.valorNuevo ?? "—"}</strong></td>
                  </tr>
                ))}
              </tbody>
            </table>

            {accion && puedeResolver && (
              <div className="formulario-en-linea" style={{ marginTop: "1rem" }}>
                <input
                  type="text" autoFocus maxLength={1000}
                  placeholder={accion === "rechazar" ? "Motivo del rechazo (obligatorio)" : "Qué debe corregir o aclarar el solicitante (obligatorio)"}
                  value={texto} onChange={(e) => setTexto(e.target.value)}
                />
                <button
                  type="button" disabled={procesando || texto.trim() === ""}
                  onClick={() => accion === "rechazar"
                    ? void ejecutar("rechazar", { motivo: texto }, `Gestión #${detalle.idGestion} rechazada.`)
                    : void ejecutar("devolver", { comentario: texto }, `Gestión #${detalle.idGestion} devuelta al solicitante.`)}
                >
                  {accion === "rechazar" ? "Confirmar rechazo" : "Confirmar devolución"}
                </button>
                <button type="button" className="boton-secundario" onClick={() => { setAccion(null); setTexto(""); }}>Cancelar</button>
              </div>
            )}

            <div className="gestion-modal__acciones">
              {puedeResolver && !accion && (
                <>
                  <button type="button" disabled={procesando}
                    onClick={() => window.confirm(`¿Aprobar la gestión #${detalle.idGestion}? El cambio se aplicará de inmediato.`)
                      && void ejecutar("aprobar", undefined, `Gestión #${detalle.idGestion} aprobada y aplicada.`)}>
                    ✓ Aprobar
                  </button>
                  <button type="button" className="boton-aviso" disabled={procesando} onClick={() => { setAccion("devolver"); setTexto(""); }}>↩ Devolver</button>
                  <button type="button" className="boton-peligro" disabled={procesando} onClick={() => { setAccion("rechazar"); setTexto(""); }}>✕ Rechazar</button>
                </>
              )}
              {puedeCorregir && (<button type="button" onClick={() => corregir(detalle)}>✎ Corregir y reenviar</button>)}
              <button type="button" className="boton-secundario" onClick={cerrar} style={{ marginLeft: "auto" }}>Cerrar</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
