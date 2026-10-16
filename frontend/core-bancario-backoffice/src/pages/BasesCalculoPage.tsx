import { useCallback, useEffect, useMemo, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import { usePermisos } from "../contexts/PermisosContext";
import type { BaseCalculo } from "../api/tiposCreditos";

/** Pricing Studio › Bases de cálculo (permiso PRD_CONSULTA para ver, IND_ADMIN para activar/inactivar). Catálogo con comportamiento en código. */
export function BasesCalculoPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const [filas, setFilas] = useState<BaseCalculo[]>([]);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try { const r = await api.get<BaseCalculo[]>("/api/bases-calculo"); setFilas(r.data); setSel(new Set()); }
    catch (e) { setError(mensajeError(e, "No se pudieron cargar las bases de cálculo.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<BaseCalculo>[] = useMemo(() => [
    { clave: "codigo", encabezado: "Base", render: (r) => <strong>{r.nombre}</strong>, textoBusqueda: (r) => `${r.codigo} ${r.nombre}` },
    { clave: "conteo", encabezado: "Conteo de días", render: (r) => (r.tipoConteo === "Comercial" ? "Comercial (meses de 30 días)" : "Real (días calendario)"), textoBusqueda: (r) => r.tipoConteo },
    { clave: "anio", encabezado: "Año", render: (r) => (r.usaAnioReal ? "365 o 366 según el año" : `${r.diasAnioFijo} días`) },
    { clave: "desc", encabezado: "Descripción", render: (r) => r.descripcion ?? "", textoBusqueda: (r) => r.descripcion ?? "" },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado, filtroLista: true },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const b = filas.find((x) => x.idBaseCalculo === idSel) ?? null;
  async function estado(e: "Activo" | "Inactivo") {
    if (!b) return;
    setExito(null); setError(null);
    try { await api.put(`/api/bases-calculo/${b.idBaseCalculo}/estado`, { estado: e }); setExito(e === "Activo" ? "Base activada." : "Base inactivada."); await cargar(); }
    catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  return (
    <div>
      <h1>Bases de cálculo</h1>
      <p className="nota">Cómo se cuentan los días y el año al calcular intereses. Su fórmula está en el sistema; aquí solo se activan o inactivan las que ofrece la entidad.</p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos columnas={columnas} filas={filas} claveFila={(x) => x.idBaseCalculo} seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        comandos={[
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", oculto: !tienePermiso("IND_ADMIN"), deshabilitado: b?.estado === "Activo", onClick: () => void estado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", oculto: !tienePermiso("IND_ADMIN"), deshabilitado: b?.estado === "Inactivo", onClick: () => void estado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]} />
    </div>
  );
}
