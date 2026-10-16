import { useEffect, useState } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import type { CatalogoItemResponse } from "../api/tipos";

interface Props {
  /** Ruta del catalogo, ej. "/api/catalogos/listas/TIPO_IDENTIFICACION_FISICA". */
  url: string;
  /** Id seleccionado (null = sin seleccion). */
  valor: number | null;
  onCambiar: (id: number | null) => void;
  required?: boolean;
  disabled?: boolean;
}

/**
 * Combo box alimentado por un catalogo de la API (/api/catalogos/...). Solo
 * recibe valores Activos (lo desactivado en Listas de Valores no se ofrece en
 * registros nuevos). Si el catalogo marca una opcion como predeterminada y aun
 * no hay seleccion, la preselecciona.
 */
export function ComboCatalogo({ url, valor, onCambiar, required, disabled }: Props) {
  const api = useApiClient();
  const [items, setItems] = useState<CatalogoItemResponse[]>([]);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelado = false;
    setCargando(true);
    setError(null);
    api
      .get<CatalogoItemResponse[]>(url)
      .then((r) => {
        if (cancelado) return;
        setItems(r.data);
      })
      .catch((err) => {
        if (cancelado) return;
        setError(isAxiosError(err) ? `Error ${err.response?.status ?? ""} al cargar la lista` : "Error al cargar la lista");
      })
      .finally(() => {
        if (!cancelado) setCargando(false);
      });
    return () => {
      cancelado = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [url, api]);

  // Preselecciona la opcion predeterminada cuando no hay seleccion (tambien
  // despues de limpiar el formulario).
  useEffect(() => {
    if (valor !== null) return;
    const predeterminado = items.find((i) => i.esPredeterminado);
    if (predeterminado) onCambiar(predeterminado.id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [valor, items]);

  return (
    <>
      <select
        value={valor ?? ""}
        required={required}
        disabled={disabled || cargando}
        onChange={(e) => onCambiar(e.target.value === "" ? null : Number(e.target.value))}
      >
        <option value="">{cargando ? "Cargando..." : "Seleccione..."}</option>
        {items.map((i) => (
          <option key={i.id} value={i.id}>
            {i.nombre}
          </option>
        ))}
      </select>
      {error && <span className="mensaje-error">{error}</span>}
      {!error && !cargando && items.length === 0 && (
        <span className="nota">La lista no tiene valores activos.</span>
      )}
    </>
  );
}
