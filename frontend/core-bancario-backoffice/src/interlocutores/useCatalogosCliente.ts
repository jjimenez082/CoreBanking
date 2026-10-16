import { useCallback, useEffect, useRef, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import type { CatalogoItemResponse, DireccionConfig, UnidadItem } from "../api/tipos";
import type { CatalogosCliente } from "./formCliente";

const VACIO: CatalogosCliente = {
  tiposIdentificacion: [], instituciones: [], tiposDireccion: [], tiposContacto: [], paises: [], paisesDireccion: [], unidades: [],
  tiposIdentificacionOrg: [], formasJuridicas: [], estadosLegales: [], actividades: [], sexos: [], estadosCiviles: [],
  condicionesLaborales: [], ocupaciones: [], tiposIngreso: [], periodicidades: [], origenesFondos: [], origenesPatrimonio: [],
  tiposTransaccion: [], frecuencias: [], tiposCuentaExterna: [], entidadesFinancieras: [], monedas: [],
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
      api.get<UnidadItem[]>("/api/catalogos/unidades", { params: { para: "registros" } }).then((r) => r.data),
      // Listas de organizaciones: si aun no corrio organizacion_ronda22.sql quedan vacias (el alta de personas sigue funcionando).
      lista("TIPO_IDENTIFICACION_JURIDICA").catch(() => [] as CatalogoItemResponse[]),
      lista("FORMA_JURIDICA").catch(() => [] as CatalogoItemResponse[]),
      lista("ESTADO_LEGAL_ORG").catch(() => [] as CatalogoItemResponse[]),
      lista("ACTIVIDAD_ECONOMICA").catch(() => [] as CatalogoItemResponse[]),
      // Listas de identidad de la persona: vacias si aun no corrio persona_identidad_ronda27.sql.
      lista("SEXO").catch(() => [] as CatalogoItemResponse[]),
      lista("ESTADO_CIVIL").catch(() => [] as CatalogoItemResponse[]),
      // Listas del perfil financiero: vacias si aun no corrio perfil_financiero_ronda29.sql.
      lista("CONDICION_LABORAL").catch(() => [] as CatalogoItemResponse[]),
      lista("OCUPACION").catch(() => [] as CatalogoItemResponse[]),
      lista("TIPO_INGRESO").catch(() => [] as CatalogoItemResponse[]),
      lista("PERIODICIDAD_INGRESO").catch(() => [] as CatalogoItemResponse[]),
      lista("ORIGEN_FONDOS").catch(() => [] as CatalogoItemResponse[]),
      lista("ORIGEN_PATRIMONIO").catch(() => [] as CatalogoItemResponse[]),
      lista("TIPO_TRANSACCION").catch(() => [] as CatalogoItemResponse[]),
      lista("FRECUENCIA_TRANSACCION").catch(() => [] as CatalogoItemResponse[]),
      lista("TIPO_CUENTA_EXTERNA").catch(() => [] as CatalogoItemResponse[]),
      lista("ENTIDAD_FINANCIERA").catch(() => [] as CatalogoItemResponse[]),
      api.get<{ idMoneda: number; codigo: string; nombre: string; esBase: boolean }[]>("/api/catalogos/monedas").then((r) => r.data).catch(() => []),
    ])
      .then(([tiposIdentificacion, instituciones, tiposDireccion, tiposContacto, paises, paisesDireccion, unidades,
        tiposIdentificacionOrg, formasJuridicas, estadosLegales, actividades, sexos, estadosCiviles,
        condicionesLaborales, ocupaciones, tiposIngreso, periodicidades, origenesFondos, origenesPatrimonio, tiposTransaccion, frecuencias, tiposCuentaExterna, entidadesFinancieras, monedas]) => {
        if (!cancelado) setCatalogos({ tiposIdentificacion, instituciones, tiposDireccion, tiposContacto, paises, paisesDireccion, unidades,
          tiposIdentificacionOrg, formasJuridicas, estadosLegales, actividades, sexos, estadosCiviles,
          condicionesLaborales, ocupaciones, tiposIngreso, periodicidades, origenesFondos, origenesPatrimonio, tiposTransaccion, frecuencias, tiposCuentaExterna, entidadesFinancieras,
          monedas: monedas.map((m) => ({ id: m.idMoneda, codigo: m.codigo, nombre: m.nombre, esBase: m.esBase })) });
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
