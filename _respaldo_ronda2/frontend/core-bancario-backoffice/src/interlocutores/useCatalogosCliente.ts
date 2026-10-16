import { useCallback, useEffect, useRef, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import type { CatalogoItemResponse, DireccionConfig, UnidadItem } from "../api/tipos";
import type { CatalogosCliente } from "./formCliente";

const VACIO: CatalogosCliente = {
  tiposIdentificacion: [], instituciones: [], tiposDireccion: [], tiposContacto: [], paises: [], paisesDireccion: [], unidades: [], canales: [],
};

/** Carga una sola vez todas las listas de valores que usa el wizard de cliente. */
export function useCatalogosCliente() {
  const api = useApiClient();
  const [catalogos, setCatalogos] = useState<CatalogosCliente>(VACIO);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelado = false;
    const lista = (codigo: string) =>
      api.get<CatalogoItemResponse[]>(`/api/catalogos/listas/${codigo}`).then((r) => r.data);

    Promise.all([
      lista("TIPO_IDENTIFICACION_FISICA"),
      lista("INSTITUCION_EMISORA_ID"),
      lista("TIPO_DIRECCION"),
      lista("TIPO_CONTACTO"),
      api.get<CatalogoItemResponse[]>("/api/catalogos/paises-reguladores").then((r) => r.data),
      lista("PAIS"),
      api.get<UnidadItem[]>("/api/catalogos/unidades").then((r) => r.data),
      lista("CANAL").catch(() => [] as CatalogoItemResponse[]),   // si aun no corrieron administracion_transversal.sql
    ])
      .then(([tiposIdentificacion, instituciones, tiposDireccion, tiposContacto, paises, paisesDireccion, unidades, canales]) => {
        if (!cancelado) setCatalogos({ tiposIdentificacion, instituciones, tiposDireccion, tiposContacto, paises, paisesDireccion, unidades, canales });
      })
      .catch(() => {
        if (!cancelado) setError("No se pudieron cargar las listas de valores. Verifique que corrieron interlocutor_multiples.sql y division_territorial.sql.");
      })
      .finally(() => {
        if (!cancelado) setCargando(false);
      });

    return () => {
      cancelado = true;
    };
  }, [api]);

  return { catalogos, cargando, error };
}

/** Configuracion de direccion por pais (cache en memoria). Carga bajo demanda con `asegurar`. */
export function useConfigsDireccion() {
  const api = useApiClient();
  const [configs, setConfigs] = useState<Record<number, DireccionConfig>>({});
  const pedidos = useRef(new Set<number>());

  const asegurar = useCallback((idPais: number) => {
    if (!idPais || pedidos.current.has(idPais)) return;
    pedidos.current.add(idPais);
    api.get<DireccionConfig>("/api/catalogos/direccion/config", { params: { idPais } })
      .then((r) => setConfigs((c) => ({ ...c, [idPais]: r.data })))
      .catch(() => pedidos.current.delete(idPais));
  }, [api]);

  return { configs, asegurar };
}
