import { useEffect, useMemo, useState } from "react";
import { GestionDetalleModal, PENDIENTE, DEVUELTA, claseEstado, textoEstado, num, textoRegistro, fmtFechaHora } from "../components/GestionDetalleModal";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import type { GestionResponse } from "../api/tipos";


/**
 * Bandeja de Gestiones: los cambios a datos de negocio se solicitan como gestión y se aplican al aprobarse.
 * Se abre el detalle con el ID (o «Ver detalle»); dentro de la pantalla de visualización están las acciones:
 * Aprobar (aplica el cambio), Rechazar (motivo) y Devolver (comentario: vuelve al solicitante para que lo corrija).
 * Quien recibe una gestión devuelta ve «Corregir», que abre la edición con lo que ya había pedido.
 * Las tablas se pueden ordenar (clic en el encabezado) y filtrar por columna.
 */
export function GestionesPage({ modo = "consulta" }: { modo?: "consulta" | "aprobacion" }) {
  const api = useApiClient();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  // Pantalla de origen (notificaciones, campanita): al cerrar el detalle se vuelve ahi.
  const [volverA, setVolverA] = useState<string | null>(null);

  const [gestiones, setGestiones] = useState<GestionResponse[]>([]);
  // Filtro rapido de la consulta: mis pendientes (que yo ingresé y siguen en trámite o devueltas), todas las mías, o todas.
  const [filtro, setFiltro] = useState<"mis_pendientes" | "mis" | "todas">("mis_pendientes");
  // Consulta: todas las gestiones. Aprobación: solo las que al usuario le toca resolver ahora.
  const verTodas = modo === "consulta";
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());

  const [idVer, setIdVer] = useState<number | null>(null);

  async function cargar() {
    setCargando(true);
    setError(null);
    try {
      const r = await api.get<GestionResponse[]>("/api/gestiones", { params: { estado: verTodas ? "todas" : "pendientes" } });
      setGestiones(modo === "aprobacion" ? r.data.filter((g) => g.puedeResolver) : r.data);
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
  }, [modo]);

  function verDetalle(id: number) {
    setError(null);
    setAviso(null);
    setIdVer(id);
  }

  // ?ver=N (desde la campanita y los enlaces): abre el detalle directamente.
  useEffect(() => {
    const ver = params.get("ver");
    if (ver) {
      const origen = (location.state as { volverA?: string } | null)?.volverA;
      setVolverA(origen ?? null);
      verDetalle(Number(ver));
      setParams({}, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);

  function cerrarYVolver() {
    setIdVer(null);
    if (volverA) {
      const destino = volverA;
      setVolverA(null);
      navigate(destino);
    }
  }

  const columnas: ColumnaTabla<GestionResponse>[] = useMemo(() => [
    {
      clave: "id", encabezado: "N.º",
      render: (g) => (<button type="button" className="boton-enlace-id" onClick={() => verDetalle(g.idGestion)}>{num(g)}</button>),
      textoBusqueda: (g) => num(g), valorOrden: (g) => g.idGestion,
    },
    { clave: "tipo", encabezado: "Tipo", render: (g) => g.tipoGestion, textoBusqueda: (g) => g.tipoGestion, filtroLista: true },
    {
      clave: "entidad", encabezado: "Registro",
      render: (g) => textoRegistro(g.entidadNegocio, g.llaveRegistro),
      textoBusqueda: (g) => textoRegistro(g.entidadNegocio, g.llaveRegistro),
    },
    {
      clave: "estado", encabezado: "Estado",
      render: (g) => <span className={`estado-gestion estado-gestion--${claseEstado(g.estado)}`}>{textoEstado(g.estado)}</span>,
      textoBusqueda: (g) => textoEstado(g.estado), filtroLista: true,
    },
    { clave: "solicitante", encabezado: "Ingresó", render: (g) => g.solicitadoPor, textoBusqueda: (g) => g.solicitadoPor, filtroLista: true },
    {
      clave: "resolvio", encabezado: "Resolvió",
      render: (g) => (g.resolucionTipo ? `${g.resolucionTipo}: ${g.resueltaPor ?? "Sistema"}` : "—"),
      textoBusqueda: (g) => (g.resolucionTipo ? `${g.resolucionTipo} ${g.resueltaPor ?? "Sistema"}` : ""),
    },
    {
      clave: "nivel", encabezado: "Nivel",
      render: (g) => ((g.nivelesRequeridos ?? 1) > 1 || g.pendienteDe
        ? `${g.nivelActual ?? 1} de ${g.nivelesRequeridos ?? 1}${g.pendienteDe ? ` · ${g.pendienteDe}` : ""}`
        : "—"),
      textoBusqueda: (g) => g.pendienteDe ?? "",
    },
    {
      clave: "correccion", encabezado: "Corrección",
      render: (g) => (g.numeroCorreccion > 0
        ? <span className={g.numeroCorreccion >= 2 ? "estado-gestion estado-gestion--devuelta" : undefined} title={g.idGestionOrigen ? `Corrige la gestión ${g.numeroOrigen ?? g.idGestionOrigen}` : undefined}>n.º {g.numeroCorreccion}</span>
        : "—"),
      textoBusqueda: (g) => (g.numeroCorreccion > 0 ? String(g.numeroCorreccion) : ""), valorOrden: (g) => g.numeroCorreccion,
    },
    {
      clave: "fecha", encabezado: "Fecha", render: (g) => fmtFechaHora(g.creadoEn),
      textoBusqueda: (g) => fmtFechaHora(g.creadoEn), valorOrden: (g) => new Date(/(Z|[+-]\d\d:?\d\d)$/.test(g.creadoEn) ? g.creadoEn : `${g.creadoEn}Z`).getTime(),
    },
  // eslint-disable-next-line react-hooks/exhaustive-deps
  ], []);

  const esPendiente = (g: GestionResponse) => g.estado === PENDIENTE || g.estado === DEVUELTA;
  const conteos = useMemo(() => ({
    mis_pendientes: gestiones.filter((g) => g.esMia && esPendiente(g)).length,
    mis: gestiones.filter((g) => g.esMia).length,
    todas: gestiones.length,
  }), [gestiones]);
  const filasMostradas = useMemo(() => {
    if (!verTodas || filtro === "todas") return gestiones;
    return gestiones.filter((g) => g.esMia && (filtro === "mis" || esPendiente(g)));
  }, [gestiones, filtro, verTodas]);

  const idSeleccionada = seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
  return (
    <div>
      <h1>{modo === "aprobacion" ? "Aprobación de gestiones" : "Consulta de gestiones"}</h1>
      <p className="nota">
        {modo === "aprobacion"
          ? "Gestiones que le toca resolver en este momento. Abra una gestión con su número (o márquela y use «Ver detalle») para aprobarla, rechazarla o devolverla al solicitante."
          : "Todas las gestiones y su recorrido de aprobación. Abra una gestión con su número (o márquela y use «Ver detalle») para ver sus niveles; si le toca resolverla, también puede hacerlo desde ahí."}
        {" "}Puede ordenar y filtrar por columna.
      </p>

      {error && <p className="mensaje-error">{error}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}

      {verTodas && (
        <div className="filtros-rapidos" role="tablist" aria-label="Filtro de gestiones">
          {([
            ["mis_pendientes", "Mis gestiones pendientes"],
            ["mis", "Todas mis gestiones"],
            ["todas", "Todas las gestiones"],
          ] as const).map(([clave, etiqueta]) => (
            <button key={clave} type="button" role="tab" aria-selected={filtro === clave}
              className={`filtro-rapido${filtro === clave ? " filtro-rapido--activo" : ""}`}
              onClick={() => { setFiltro(clave); setSeleccion(new Set()); }}>
              {etiqueta}<span className="filtro-rapido__n">{conteos[clave]}</span>
            </button>
          ))}
        </div>
      )}

      <TablaDatos
        columnas={columnas}
        filas={filasMostradas}
        exportar={{ nombreArchivo: verTodas ? "gestiones" : "gestiones_por_aprobar", hoja: "Gestiones" }}
        claveFila={(g) => g.idGestion}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        filtrosPorColumna
        mensajeVacio={!verTodas ? "No tiene gestiones pendientes de aprobar en este momento."
          : filtro === "mis_pendientes" ? "No tiene gestiones suyas pendientes. Use «Todas mis gestiones» o «Todas las gestiones» para ver más."
          : "No hay gestiones."}
        placeholderBusqueda="Buscar gestión"
        comandos={[
          { clave: "detalle", etiqueta: "Ver detalle", icono: "👁", requiereSeleccion: "una", onClick: () => idSeleccionada !== null && verDetalle(idSeleccionada) },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {idVer !== null && (
        <GestionDetalleModal
          idGestion={idVer}
          onCerrar={cerrarYVolver}
          onResuelta={(m) => { setAviso(m); setIdVer(null); void cargar(); }}
        />
      )}
    </div>
  );
}
