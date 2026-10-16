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
}: Props<T>) {
  const [texto, setTexto] = useState(busquedaServidor?.valorInicial ?? "");

  const filasVisibles = useMemo(() => {
    if (busquedaServidor || !texto.trim()) return filas;
    const buscado = texto.trim().toLowerCase();
    return filas.filter((fila) =>
      columnas.some((c) => c.textoBusqueda?.(fila).toLowerCase().includes(buscado)),
    );
  }, [filas, columnas, texto, busquedaServidor]);

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
              <th key={c.clave}>{c.encabezado}</th>
            ))}
          </tr>
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
