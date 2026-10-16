import { useMemo, useState, type FormEvent, type ReactNode } from "react";

/**
 * Tabla de mantenimiento estandar del Backoffice (patron estilo Dynamics 365):
 *  - Barra de comandos arriba (Nuevo / Editar / Eliminar / Actualizar ...).
 *  - Checkbox por registro; los comandos actuan sobre lo seleccionado.
 *  - Buscador propio de la pantalla (solo filtra los registros que muestra).
 *
 * Dos modos de busqueda:
 *  - Local (default): filtra en el navegador las filas ya cargadas.
 *  - Servidor: si se pasa `busquedaServidor`, el texto se envia al padre con
 *    onBuscar al presionar Enter / lupa (ej. listados paginados grandes).
 *
 * Todas las pantallas de mantenimiento deben usar este componente para que
 * crear/editar/borrar se sienta igual en todo el sistema.
 */

export interface ColumnaTabla<T> {
  clave: string;
  encabezado: string;
  render: (fila: T) => ReactNode;
  /** Texto usado por el buscador local. Si falta, la columna no participa. */
  textoBusqueda?: (fila: T) => string;
  /** Valor para ordenar (numero o texto). Si falta se ordena por textoBusqueda. */
  valorOrden?: (fila: T) => string | number;
  /** El filtro de la columna es una lista con los valores distintos (ej. Estado) en vez de texto libre. */
  filtroLista?: boolean;
}

export interface ComandoTabla {
  clave: string;
  etiqueta: string;
  icono?: string;
  onClick: () => void;
  /** "ninguna": siempre activo. "una": exactamente 1 seleccionada. "varias": 1 o mas. */
  requiereSeleccion?: "ninguna" | "una" | "varias";
  deshabilitado?: boolean;
  oculto?: boolean;
}

interface Props<T> {
  columnas: ColumnaTabla<T>[];
  filas: T[];
  claveFila: (fila: T) => string | number;
  comandos: ComandoTabla[];
  /** Claves seleccionadas (controlado por el padre para poder actuar sobre ellas). */
  seleccion: Set<string | number>;
  onCambiarSeleccion: (seleccion: Set<string | number>) => void;
  cargando?: boolean;
  mensajeVacio?: string;
  placeholderBusqueda?: string;
  /** Si se define, la busqueda se delega al servidor en vez de filtrar localmente. */
  busquedaServidor?: { onBuscar: (texto: string) => void; valorInicial?: string };
  /** Contenido extra bajo la tabla (ej. paginacion). */
  pie?: ReactNode;
  /** Activa ordenamiento (clic en el encabezado) y filtros por columna. Solo para tablas con todas las filas cargadas. */
  filtrosPorColumna?: boolean;
}

export function TablaDatos<T>({
  columnas,
  filas,
  claveFila,
  comandos,
  seleccion,
  onCambiarSeleccion,
  cargando = false,
  mensajeVacio = "No hay registros.",
  placeholderBusqueda = "Búsqueda rápida",
  busquedaServidor,
  pie,
  filtrosPorColumna = false,
}: Props<T>) {
  const [texto, setTexto] = useState(busquedaServidor?.valorInicial ?? "");

  const [filtros, setFiltros] = useState<Record<string, string>>({});
  const [orden, setOrden] = useState<{ clave: string; asc: boolean } | null>(null);

  const textoDe = (c: ColumnaTabla<T>, fila: T) => (c.textoBusqueda ? c.textoBusqueda(fila) : String(c.valorOrden?.(fila) ?? ""));
  const ordenable = (c: ColumnaTabla<T>) => filtrosPorColumna && !!(c.valorOrden || c.textoBusqueda);

  const filasVisibles = useMemo(() => {
    let r = filas;
    if (!busquedaServidor && texto.trim()) {
      const buscado = texto.trim().toLowerCase();
      r = r.filter((fila) => columnas.some((c) => c.textoBusqueda?.(fila).toLowerCase().includes(buscado)));
    }
    if (filtrosPorColumna) {
      for (const c of columnas) {
        const f = (filtros[c.clave] ?? "").trim().toLowerCase();
        if (!f) continue;
        r = r.filter((fila) => (c.filtroLista ? textoDe(c, fila).toLowerCase() === f : textoDe(c, fila).toLowerCase().includes(f)));
      }
      const col = orden ? columnas.find((c) => c.clave === orden.clave) : undefined;
      if (col && orden) {
        const valor = (fila: T) => (col.valorOrden ? col.valorOrden(fila) : textoDe(col, fila));
        r = [...r].sort((a, b) => {
          const va = valor(a), vb = valor(b);
          const cmp = typeof va === "number" && typeof vb === "number"
            ? va - vb
            : String(va).localeCompare(String(vb), "es", { numeric: true, sensitivity: "base" });
          return orden.asc ? cmp : -cmp;
        });
      }
    }
    return r;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filas, columnas, texto, busquedaServidor, filtros, orden, filtrosPorColumna]);

  const hayFiltros = Object.values(filtros).some((v) => v.trim() !== "");
  function alternarOrden(clave: string) {
    setOrden((o) => (!o || o.clave !== clave ? { clave, asc: true } : o.asc ? { clave, asc: false } : null));
  }

  const clavesVisibles = filasVisibles.map(claveFila);
  const todasMarcadas =
    clavesVisibles.length > 0 && clavesVisibles.every((k) => seleccion.has(k));

  function alternarTodas() {
    const nueva = new Set(seleccion);
    if (todasMarcadas) {
      clavesVisibles.forEach((k) => nueva.delete(k));
    } else {
      clavesVisibles.forEach((k) => nueva.add(k));
    }
    onCambiarSeleccion(nueva);
  }

  function alternarFila(clave: string | number) {
    const nueva = new Set(seleccion);
    if (nueva.has(clave)) nueva.delete(clave);
    else nueva.add(clave);
    onCambiarSeleccion(nueva);
  }

  function comandoHabilitado(c: ComandoTabla): boolean {
    if (c.deshabilitado || cargando) return false;
    const n = seleccion.size;
    if (c.requiereSeleccion === "una") return n === 1;
    if (c.requiereSeleccion === "varias") return n >= 1;
    return true;
  }

  function alEnviarBusqueda(evento: FormEvent) {
    evento.preventDefault();
    busquedaServidor?.onBuscar(texto);
  }

  return (
    <div className="tabla-datos">
      <div className="tabla-datos__barra">
        <div className="tabla-datos__comandos" role="toolbar">
          {comandos
            .filter((c) => !c.oculto)
            .map((c) => (
              <button
                key={c.clave}
                type="button"
                className="tabla-datos__comando"
                disabled={!comandoHabilitado(c)}
                onClick={c.onClick}
              >
                {c.icono && <span aria-hidden="true">{c.icono}</span>}
                {c.etiqueta}
              </button>
            ))}
          {seleccion.size > 0 && (
            <span className="tabla-datos__contador">{seleccion.size} seleccionado(s)</span>
          )}
        </div>

        <form className="tabla-datos__busqueda" onSubmit={alEnviarBusqueda}>
          <input
            type="search"
            placeholder={placeholderBusqueda}
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
          />
        </form>
      </div>

      <table className="tabla tabla-datos__tabla">
        <thead>
          <tr>
            <th className="tabla-datos__celda-check">
              <input
                type="checkbox"
                aria-label="Seleccionar todas"
                checked={todasMarcadas}
                onChange={alternarTodas}
                disabled={clavesVisibles.length === 0}
              />
            </th>
            {columnas.map((c) => (
              <th key={c.clave} aria-sort={orden?.clave === c.clave ? (orden.asc ? "ascending" : "descending") : undefined}>
                {ordenable(c) ? (
                  <button type="button" className="tabla-datos__orden" onClick={() => alternarOrden(c.clave)} title="Ordenar">
                    {c.encabezado}
                    <span aria-hidden="true">{orden?.clave === c.clave ? (orden.asc ? " ▲" : " ▼") : " ↕"}</span>
                  </button>
                ) : c.encabezado}
              </th>
            ))}
          </tr>
          {filtrosPorColumna && (
            <tr className="tabla-datos__filtros">
              <th className="tabla-datos__celda-check">
                {hayFiltros && (
                  <button type="button" className="tabla-datos__limpiar" title="Quitar filtros" onClick={() => setFiltros({})}>✕</button>
                )}
              </th>
              {columnas.map((c) => (
                <th key={c.clave}>
                  {!(c.textoBusqueda || c.valorOrden) ? null : c.filtroLista ? (
                    <select value={filtros[c.clave] ?? ""} onChange={(e) => setFiltros((f) => ({ ...f, [c.clave]: e.target.value }))} aria-label={`Filtrar ${c.encabezado}`}>
                      <option value="">Todos</option>
                      {Array.from(new Set(filas.map((fila) => textoDe(c, fila)))).sort().map((v) => (<option key={v} value={v}>{v}</option>))}
                    </select>
                  ) : (
                    <input type="search" placeholder="Filtrar" value={filtros[c.clave] ?? ""}
                      onChange={(e) => setFiltros((f) => ({ ...f, [c.clave]: e.target.value }))} aria-label={`Filtrar ${c.encabezado}`} />
                  )}
                </th>
              ))}
            </tr>
          )}
        </thead>
        <tbody>
          {cargando && filasVisibles.length === 0 ? (
            <tr>
              <td colSpan={columnas.length + 1} className="nota">
                Cargando...
              </td>
            </tr>
          ) : filasVisibles.length === 0 ? (
            <tr>
              <td colSpan={columnas.length + 1} className="nota">
                {mensajeVacio}
              </td>
            </tr>
          ) : (
            filasVisibles.map((fila) => {
              const clave = claveFila(fila);
              const marcada = seleccion.has(clave);
              return (
                <tr key={clave} className={marcada ? "tabla-datos__fila--marcada" : ""}>
                  <td className="tabla-datos__celda-check">
                    <input
                      type="checkbox"
                      aria-label="Seleccionar fila"
                      checked={marcada}
                      onChange={() => alternarFila(clave)}
                    />
                  </td>
                  {columnas.map((c) => (
                    <td key={c.clave}>{c.render(fila)}</td>
                  ))}
                </tr>
              );
            })
          )}
        </tbody>
      </table>

      {pie}
    </div>
  );
}
