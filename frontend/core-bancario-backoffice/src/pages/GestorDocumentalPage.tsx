import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError, fmtFecha, fmtFechaUtc } from "../api/mensajes";
import { Campo } from "../components/CamposWizard";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { PanelModal } from "../components/VentanaModal";
import { ArbolDocumentos } from "../documentos/ArbolDocumentos";
import { ReemplazarModal, ObsoletarModal, SelectorCliente, VersionesModal, type ClienteElegido } from "../documentos/AccionesDocumento";
import { descargarDocumento, etiquetaVigencia, fmtTamano } from "../documentos/documentos";
import type { DocumentoBusqueda, TipoDocumento } from "../api/tipos";

type Dialogo = "reemplazar" | "obsoletar" | "ver" | "expediente" | null;
type ClienteSel = ClienteElegido;

/**
 * App «Documentos de clientes» (Gestor documental): lista con casillas, barra de comandos y ficha, como los demas mantenimientos.
 * Agregar, reemplazar (nueva version) y obsoletar actuan sobre el expediente vivo sin gestion de aprobacion; quedan auditadas.
 * La evidencia de las gestiones se consulta pero no se modifica.
 */
export function GestorDocumentalPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const [params] = useSearchParams();
  const navigate = useNavigate();

  const [tipos, setTipos] = useState<TipoDocumento[]>([]);
  const [cliente, setCliente] = useState<ClienteSel | null>(() => {
    const id = Number(params.get("cliente"));
    return id > 0 ? { id, numero: params.get("numero") ?? "", nombre: params.get("nombre") ?? "" } : null;
  });
  const [idCarpeta, setIdCarpeta] = useState(0);
  const [idTipo, setIdTipo] = useState(0);
  const [estado, setEstado] = useState(params.get("estado") ?? "");
  const [filas, setFilas] = useState<DocumentoBusqueda[]>([]);
  const [cargando, setCargando] = useState(false);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [dialogo, setDialogo] = useState<Dialogo>(null);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);

  const puedeVer = tienePermiso("DOC_VER");

  useEffect(() => {
    let vivo = true;
    void (async () => {
      try { const r = await api.get<TipoDocumento[]>("/api/documentos/tipos"); if (vivo) setTipos(r.data); }
      catch (err) { if (vivo) setError(mensajeError(err, "No se pudo cargar el catálogo de tipos.")); }
    })();
    return () => { vivo = false; };
  }, [api]);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const r = await api.get<DocumentoBusqueda[]>("/api/documentos/buscar", {
        params: { idInterlocutor: cliente?.id, idCarpeta: idCarpeta || undefined, idTipoDocumento: idTipo || undefined, estado: estado || undefined },
      });
      setFilas(r.data); setSel(new Set());
    } catch (err) { setError(mensajeError(err, "No se pudo consultar los documentos.")); }
    finally { setCargando(false); }
  }, [api, cliente, idCarpeta, idTipo, estado]);

  useEffect(() => { if (puedeVer) void cargar(); }, [cargar, puedeVer]);

  const carpetas = useMemo(() => {
    const m = new Map<number, string>();
    tipos.filter((t) => !t.carpetaSistema).forEach((t) => m.set(t.idCarpeta, t.carpeta));
    return [...m.entries()];
  }, [tipos]);
  const tiposFiltro = useMemo(() => tipos.filter((t) => !t.carpetaSistema && (idCarpeta === 0 || t.idCarpeta === idCarpeta)), [tipos, idCarpeta]);

  const seleccionado = useMemo(() => filas.find((f) => sel.has(f.documento.idDocumento)) ?? null, [filas, sel]);
  const editable = seleccionado?.documento.origen === "Expediente" && seleccionado.documento.estado === "Vigente";

  function terminado(mensaje: string) { setDialogo(null); setExito(mensaje); setError(null); void cargar(); }

  const columnas: ColumnaTabla<DocumentoBusqueda>[] = [
    { clave: "cliente", encabezado: "Cliente", render: (r) => <><strong>{r.numeroCliente}</strong> <span className="nota">{r.nombreCliente}</span></>,
      textoBusqueda: (r) => `${r.numeroCliente} ${r.nombreCliente}`, textoExportar: (r) => `${r.numeroCliente} ${r.nombreCliente}` },
    { clave: "carpeta", encabezado: "Carpeta", render: (r) => r.carpeta, textoBusqueda: (r) => r.carpeta, filtroLista: true },
    { clave: "tipo", encabezado: "Tipo de documento", render: (r) => <>{r.documento.tipoNombre}{r.documento.restringido && <span className="docs-chip">Restringido</span>}</>,
      textoBusqueda: (r) => r.documento.tipoNombre, textoExportar: (r) => r.documento.tipoNombre },
    { clave: "archivo", encabezado: "Archivo",
      render: (r) => (
        <>
          <button type="button" className="wiz-enlace" onClick={(e) => { e.stopPropagation(); void descargarDocumento(api, r.documento).catch(() => setError("No se pudo descargar el archivo.")); }}>{r.documento.nombreArchivo}</button>
          <span className="nota"> · v{r.documento.version} · {fmtTamano(r.documento.tamano)}</span>
        </>),
      textoBusqueda: (r) => `${r.documento.nombreArchivo} ${r.documento.descripcion ?? ""} ${r.documento.claves.join(" ")}`, textoExportar: (r) => r.documento.nombreArchivo },
    { clave: "estado", encabezado: "Estado", filtroLista: true,
      render: (r) => { const c = etiquetaVigencia(r.documento); return c ? <span className={c.clase}>{c.texto}</span> : r.documento.estado; },
      textoBusqueda: (r) => (r.documento.estado === "Obsoleto" ? "Obsoleto" : r.documento.estadoVigencia === "Vencido" ? "Vencido" : r.documento.estadoVigencia === "PorVencer" ? "Por vencer" : r.documento.estado === "EnGestion" ? "Pendiente de aprobación" : "Vigente") },
    { clave: "vence", encabezado: "Vence", render: (r) => (r.documento.fechaVencimiento ? fmtFecha(r.documento.fechaVencimiento) : "—"),
      valorOrden: (r) => r.documento.fechaVencimiento ?? "9999", textoBusqueda: (r) => r.documento.fechaVencimiento ?? "" },
    { clave: "subido", encabezado: "Subido", render: (r) => `${fmtFechaUtc(r.documento.subidoEn)} · ${r.documento.subidoPor}`,
      valorOrden: (r) => r.documento.subidoEn, textoBusqueda: (r) => r.documento.subidoPor },
  ];

  if (!puedeVer) return <div><h1>Documentos de clientes</h1><p className="mensaje-error">No tiene permiso para consultar documentos.</p></div>;

  return (
    <div>
      <h1>Documentos de clientes</h1>
      <p className="nota">
        Expediente vivo de cada cliente. Agregar, reemplazar (nueva versión) y obsoletar se registran como una gestión de documentos: según la regla de
        aprobación se aplican de inmediato o quedan pendientes. La evidencia de las demás gestiones se consulta pero no se modifica.
      </p>
      {error && <p className="mensaje-error" role="alert">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <div className="docs-filtros" style={{ marginBottom: "0.8rem" }}>
        <Campo etiqueta="Cliente"><SelectorCliente valor={cliente} onElegir={setCliente} /></Campo>
        <Campo etiqueta="Carpeta">
          <select value={idCarpeta} onChange={(e) => { setIdCarpeta(Number(e.target.value)); setIdTipo(0); }}>
            <option value={0}>Todas</option>
            {carpetas.map(([id, nombre]) => <option key={id} value={id}>{nombre}</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Tipo de documento">
          <select value={idTipo} onChange={(e) => setIdTipo(Number(e.target.value))}>
            <option value={0}>Todos</option>
            {tiposFiltro.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
          </select>
        </Campo>
        <Campo etiqueta="Estado">
          <select value={estado} onChange={(e) => setEstado(e.target.value)}>
            <option value="">Vigentes y obsoletos</option>
            <option value="Vigente">Vigente</option>
            <option value="PorVencer">Por vencer</option>
            <option value="Vencido">Vencido</option>
            <option value="Obsoleto">Obsoleto</option>
            <option value="Pendiente">Pendiente de aprobación</option>
          </select>
        </Campo>
      </div>

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(r) => r.documento.idDocumento} seleccion={sel} onCambiarSeleccion={setSel}
        cargando={cargando} mensajeVacio="No hay documentos con esos criterios." filtrosPorColumna
        placeholderBusqueda="Buscar por cliente, archivo, descripción o palabra clave"
        exportar={{ nombreArchivo: "documentos_clientes", hoja: "Documentos" }}
        comandos={[
          { clave: "agregar", etiqueta: "Agregar documento", icono: "+", oculto: !tienePermiso("DOC_SUBIR"), onClick: () => navigate(cliente
            ? `/documentos/cargar?cliente=${cliente.id}&numero=${encodeURIComponent(cliente.numero)}&nombre=${encodeURIComponent(cliente.nombre)}&tipoPersona=${cliente.tipoPersona ?? ""}${params.get("volver") === "360" ? "&volver=360" : ""}`
            : "/documentos/cargar") },
          { clave: "ver", etiqueta: "Ver", icono: "👁", requiereSeleccion: "una", onClick: () => setDialogo("ver") },
          { clave: "reemplazar", etiqueta: "Reemplazar", icono: "⟳", requiereSeleccion: "una", oculto: !tienePermiso("DOC_REEMPLAZAR") || (!!seleccionado && !editable), onClick: () => setDialogo("reemplazar") },
          { clave: "obsoletar", etiqueta: "Obsoletar", icono: "⊘", requiereSeleccion: "una", oculto: !tienePermiso("DOC_OBSOLETAR") || (!!seleccionado && !editable), onClick: () => setDialogo("obsoletar") },
          { clave: "expediente", etiqueta: "Expediente del cliente", icono: "▥", requiereSeleccion: "una", onClick: () => setDialogo("expediente") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {dialogo === "reemplazar" && seleccionado && (
        <ReemplazarModal tipos={tipos} documento={seleccionado.documento} idInterlocutor={seleccionado.idInterlocutor} onCerrar={() => setDialogo(null)} onHecho={terminado} />
      )}
      {dialogo === "obsoletar" && seleccionado && <ObsoletarModal documento={seleccionado.documento} idInterlocutor={seleccionado.idInterlocutor} onCerrar={() => setDialogo(null)} onHecho={terminado} />}
      {dialogo === "ver" && seleccionado && (
        <VersionesModal documento={seleccionado.documento} cliente={`${seleccionado.numeroCliente} ${seleccionado.nombreCliente}`} onCerrar={() => setDialogo(null)} />
      )}
      {dialogo === "expediente" && seleccionado && (
        <PanelModal onCerrar={() => setDialogo(null)} ancho={820}>
          <h3>Expediente · {seleccionado.numeroCliente} {seleccionado.nombreCliente}</h3>
          <ArbolDocumentos idInterlocutor={seleccionado.idInterlocutor} />
          <div className="panel-formulario__acciones"><button type="button" className="boton-secundario" onClick={() => setDialogo(null)}>Cerrar</button></div>
        </PanelModal>
      )}
    </div>
  );
}
