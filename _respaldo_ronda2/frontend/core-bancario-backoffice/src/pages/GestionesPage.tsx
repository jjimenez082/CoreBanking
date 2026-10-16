import { useEffect, useState } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, GestionDetalleResponse, GestionResponse } from "../api/tipos";

const COLUMNAS: ColumnaTabla<GestionResponse>[] = [
  { clave: "id", encabezado: "ID", render: (g) => g.idGestion, textoBusqueda: (g) => String(g.idGestion) },
  { clave: "tipo", encabezado: "Tipo", render: (g) => g.tipoGestion, textoBusqueda: (g) => g.tipoGestion },
  {
    clave: "entidad",
    encabezado: "Registro",
    render: (g) => `${g.entidadNegocio}${g.llaveRegistro ? ` #${g.llaveRegistro}` : ""}`,
    textoBusqueda: (g) => `${g.entidadNegocio} ${g.llaveRegistro}`,
  },
  { clave: "estado", encabezado: "Estado", render: (g) => g.estado, textoBusqueda: (g) => g.estado },
  { clave: "solicitante", encabezado: "Solicitado por", render: (g) => g.solicitadoPor, textoBusqueda: (g) => g.solicitadoPor },
  { clave: "fecha", encabezado: "Fecha", render: (g) => new Date(g.creadoEn).toLocaleString() },
];

const PENDIENTE = "Pendiente de Aprobacion";

function mensajeError(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

/**
 * Bandeja de Gestiones: los cambios a datos de negocio se solicitan como
 * gestion y se aplican al aprobarse. Ver el detalle (campo / valor anterior /
 * valor nuevo), aprobar (aplica el cambio) o rechazar (motivo obligatorio).
 * Aprobar/rechazar exige GES_APROBAR; quien solicita no puede aprobar lo suyo.
 */
export function GestionesPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const puedeAprobar = tienePermiso("GES_APROBAR");

  const [gestiones, setGestiones] = useState<GestionResponse[]>([]);
  const [verTodas, setVerTodas] = useState(false);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());

  const [detalle, setDetalle] = useState<GestionDetalleResponse | null>(null);
  const [rechazando, setRechazando] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [procesando, setProcesando] = useState(false);

  async function cargar() {
    setCargando(true);
    setError(null);
    try {
      const r = await api.get<GestionResponse[]>("/api/gestiones", {
        params: { estado: verTodas ? "todas" : "pendientes" },
      });
      setGestiones(r.data);
      setSeleccion(new Set());
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al cargar las gestiones."));
    } finally {
      setCargando(false);
    }
  }

  useEffect(() => {
    cargar();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [verTodas]);

  const idSeleccionada = seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
  const seleccionada = gestiones.find((g) => g.idGestion === idSeleccionada) ?? null;
  const esPendiente = seleccionada?.estado === PENDIENTE;

  async function verDetalle(id: number | null = idSeleccionada) {
    if (id === null) return;
    setError(null);
    setAviso(null);
    setRechazando(false);
    setMotivo("");
    try {
      const r = await api.get<GestionDetalleResponse>(`/api/gestiones/${id}`);
      setDetalle(r.data);
    } catch (err) {
      setError(mensajeError(err, "No se pudo cargar la gestión."));
    }
  }

  async function aprobar() {
    if (idSeleccionada === null) return;
    if (!window.confirm(`¿Aprobar la gestión #${idSeleccionada}? El cambio se aplicará de inmediato.`)) return;
    setProcesando(true);
    setError(null);
    setAviso(null);
    try {
      await api.post(`/api/gestiones/${idSeleccionada}/aprobar`);
      setAviso(`Gestión #${idSeleccionada} aprobada y aplicada.`);
      setDetalle(null);
      await cargar();
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al aprobar."));
    } finally {
      setProcesando(false);
    }
  }

  async function confirmarRechazo() {
    if (idSeleccionada === null) return;
    setProcesando(true);
    setError(null);
    setAviso(null);
    try {
      await api.post(`/api/gestiones/${idSeleccionada}/rechazar`, { motivo });
      setAviso(`Gestión #${idSeleccionada} rechazada.`);
      setDetalle(null);
      setRechazando(false);
      setMotivo("");
      await cargar();
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al rechazar."));
    } finally {
      setProcesando(false);
    }
  }

  return (
    <div>
      <h1>Gestiones</h1>
      <p className="nota">
        Los cambios a datos de negocio se solicitan como gestión y se aplican al aprobarse. Marca una
        gestión para ver su detalle, aprobarla o rechazarla.
      </p>

      {error && <p className="mensaje-error">{error}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}

      <TablaDatos
        columnas={COLUMNAS}
        filas={gestiones}
        claveFila={(g) => g.idGestion}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando || procesando}
        mensajeVacio={verTodas ? "No hay gestiones." : "No hay gestiones pendientes en este momento."}
        placeholderBusqueda="Buscar gestión"
        comandos={[
          { clave: "detalle", etiqueta: "Ver detalle", icono: "👁", requiereSeleccion: "una", onClick: () => verDetalle() },
          {
            clave: "aprobar",
            etiqueta: "Aprobar",
            icono: "✓",
            oculto: !puedeAprobar,
            requiereSeleccion: "una",
            deshabilitado: !esPendiente,
            onClick: aprobar,
          },
          {
            clave: "rechazar",
            etiqueta: "Rechazar",
            icono: "✕",
            oculto: !puedeAprobar,
            requiereSeleccion: "una",
            deshabilitado: !esPendiente,
            onClick: async () => {
              await verDetalle();
              setRechazando(true);
            },
          },
          {
            clave: "filtro",
            etiqueta: verTodas ? "Ver solo pendientes" : "Ver todas",
            icono: "⇄",
            onClick: () => setVerTodas((v) => !v),
          },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: cargar },
        ]}
      />

      {detalle && (
        <section className="panel-formulario">
          <h3>
            Gestión #{detalle.idGestion} — {detalle.tipoGestion}
          </h3>
          <dl className="ficha">
            <dt>Registro</dt>
            <dd>
              {detalle.entidadNegocio} #{detalle.llaveRegistro}
            </dd>
            <dt>Estado</dt>
            <dd>{detalle.estado}</dd>
            <dt>Solicitada por</dt>
            <dd>
              {detalle.solicitadoPor} el {new Date(detalle.creadoEn).toLocaleString()}
            </dd>
            {detalle.comentario && (
              <>
                <dt>Comentario</dt>
                <dd>{detalle.comentario}</dd>
              </>
            )}
            {detalle.fechaResolucion && (
              <>
                <dt>Resuelta</dt>
                <dd>{new Date(detalle.fechaResolucion).toLocaleString()}</dd>
              </>
            )}
            {detalle.motivoRechazo && (
              <>
                <dt>Motivo del rechazo</dt>
                <dd>{detalle.motivoRechazo}</dd>
              </>
            )}
          </dl>

          <table className="tabla">
            <thead>
              <tr>
                <th>Campo</th>
                <th>Valor anterior</th>
                <th>Valor nuevo</th>
              </tr>
            </thead>
            <tbody>
              {detalle.campos.map((c) => (
                <tr key={c.nombreCampo}>
                  <td>{c.etiqueta}</td>
                  <td>{c.valorAnterior ?? "—"}</td>
                  <td>
                    <strong>{c.valorNuevo ?? "—"}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {rechazando && puedeAprobar && detalle.estado === PENDIENTE && (
            <div className="formulario-en-linea" style={{ marginTop: "1rem" }}>
              <input
                type="text"
                placeholder="Motivo del rechazo (obligatorio)"
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
              />
              <button type="button" onClick={confirmarRechazo} disabled={procesando || motivo.trim() === ""}>
                Confirmar rechazo
              </button>
              <button type="button" className="boton-secundario" onClick={() => setRechazando(false)}>
                Cancelar
              </button>
            </div>
          )}

          <div className="panel-formulario__acciones">
            <button type="button" className="boton-secundario" onClick={() => setDetalle(null)}>
              Cerrar
            </button>
          </div>
        </section>
      )}
    </div>
  );
}
