import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import type { InterlocutorBusquedaResponse } from "../api/tipos";

/**
 * Busqueda global de la barra superior (estilo CRM): una sola caja que
 * encuentra registros (contratos, interlocutores) de las entidades que ya
 * exponen busqueda. Las pantallas/aplicaciones se buscan en BusquedaAplicaciones. Hoy: Interlocutores (abre siempre la Consulta 360°). Para sumar otra entidad (Cuentas,
 * Prestamos...) se agrega un bloque mas en `buscarRegistros`, respetando
 * su permiso de consulta.
 *
 * Cada pantalla conserva ademas su propio buscador local.
 */
export function BusquedaGlobal() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { tienePermiso } = usePermisos();

  const [texto, setTexto] = useState("");
  const [abierto, setAbierto] = useState(false);
  const [clientes, setClientes] = useState<InterlocutorBusquedaResponse[]>([]);
  const contenedor = useRef<HTMLDivElement>(null);

  const textoNormalizado = texto.trim().toLowerCase();
  const puedeBuscarClientes = tienePermiso("CLI_CONSULTA");

  // Busqueda de registros con debounce (evita una llamada por tecla).
  useEffect(() => {
    if (!puedeBuscarClientes || textoNormalizado.length < 2) {
      setClientes([]);
      return;
    }
    let cancelado = false;
    const temporizador = setTimeout(async () => {
      try {
        const respuesta = await api.get<InterlocutorBusquedaResponse[]>("/api/interlocutores/buscar", {
          params: { q: texto.trim() },
        });
        if (!cancelado) setClientes(respuesta.data);
      } catch {
        if (!cancelado) setClientes([]);
      }
    }, 300);
    return () => {
      cancelado = true;
      clearTimeout(temporizador);
    };
  }, [texto, textoNormalizado, puedeBuscarClientes, api]);

  // Cierra el panel al hacer clic fuera.
  useEffect(() => {
    function alClicFuera(evento: MouseEvent) {
      if (contenedor.current && !contenedor.current.contains(evento.target as Node)) {
        setAbierto(false);
      }
    }
    document.addEventListener("mousedown", alClicFuera);
    return () => document.removeEventListener("mousedown", alClicFuera);
  }, []);

  function ir(ruta: string) {
    setAbierto(false);
    setTexto("");
    navigate(ruta);
  }

  const hayResultados = clientes.length > 0;

  return (
    <div className="busqueda-global" ref={contenedor}>
      <input
        type="search"
        placeholder="Buscar contratos o interlocutores (nombre, N.º, identificación)"
        value={texto}
        onChange={(e) => {
          setTexto(e.target.value);
          setAbierto(true);
        }}
        onFocus={() => setAbierto(true)}
      />

      {abierto && textoNormalizado.length > 0 && (
        <div className="busqueda-global__panel">
          {clientes.length > 0 && (
            <>
              <div className="busqueda-global__grupo">Interlocutores</div>
              {clientes.map((c) => (
                <button
                  key={c.idInterlocutor}
                  type="button"
                  className="busqueda-global__resultado"
                  onClick={() => ir(`/interlocutores/${c.idInterlocutor}`)}
                >
                  {c.nombre}
                  <span className="busqueda-global__detalle">
                    N.º {c.numero} · {c.identificaciones} · {c.roles}
                  </span>
                </button>
              ))}
              <button
                type="button"
                className="busqueda-global__resultado"
                onClick={() => ir(`/interlocutores/consulta?q=${encodeURIComponent(texto.trim())}`)}
              >
                Mostrar más resultados en Interlocutores
              </button>
            </>
          )}

          {!hayResultados && (
            <div className="busqueda-global__grupo">Sin coincidencias</div>
          )}
        </div>
      )}
    </div>
  );
}
