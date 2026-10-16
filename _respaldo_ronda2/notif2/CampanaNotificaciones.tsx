import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useLeidas } from "../api/useNotificaciones";
import { useApiClient } from "../api/useApiClient";
import type { NotificacionResponse } from "../api/tipos";

/**
 * Campana de notificaciones junto al usuario. Las notificaciones las deriva el
 * backend (gestiones por aprobar, gestiones propias resueltas, borradores por
 * vencer); aqui solo se listan las no leidas, el historial esta en la app Notificaciones.
 */
const INTERVALO_MS = 60_000;

const fmt = (iso: string) =>
  new Date(iso).toLocaleString("es-CR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });

export function CampanaNotificaciones() {
  const api = useApiClient();
  const navigate = useNavigate();
  const [items, setItems] = useState<NotificacionResponse[]>([]);
  const { leidas, marcarLeidas } = useLeidas();
  const [abierto, setAbierto] = useState(false);
  const contenedor = useRef<HTMLDivElement>(null);

  const cargar = useCallback(async () => {
    try {
      const r = await api.get<NotificacionResponse[]>("/api/notificaciones");
      setItems(r.data);
    } catch {
      /* la campana nunca debe romper la pantalla */
    }
  }, [api]);

  useEffect(() => {
    void cargar();
    const t = setInterval(() => void cargar(), INTERVALO_MS);
    return () => clearInterval(t);
  }, [cargar]);

  useEffect(() => {
    function fuera(e: MouseEvent) {
      if (contenedor.current && !contenedor.current.contains(e.target as Node)) setAbierto(false);
    }
    document.addEventListener("mousedown", fuera);
    return () => document.removeEventListener("mousedown", fuera);
  }, []);

  // La campanita solo muestra lo no leido; lo leido queda en la app Notificaciones.
  const pendientes = items.filter((n) => !leidas.has(n.clave));
  const noLeidas = pendientes.length;

  function abrir(n: NotificacionResponse) {
    marcarLeidas([n.clave]);
    setAbierto(false);
    navigate(n.ruta);
  }

  return (
    <div className="campana" ref={contenedor}>
      <button
        type="button"
        className="campana__boton"
        aria-label={`Notificaciones${noLeidas ? `, ${noLeidas} sin leer` : ""}`}
        onClick={() => {
          setAbierto((a) => !a);
          if (!abierto) void cargar();
        }}
      >
        🔔
        {noLeidas > 0 && <span className="campana__contador">{noLeidas > 9 ? "9+" : noLeidas}</span>}
      </button>

      {abierto && (
        <div className="campana__panel">
          <div className="campana__cabecera">
            <span>Notificaciones</span>
            {noLeidas > 0 && (
              <button type="button" className="wiz-enlace" onClick={() => marcarLeidas(pendientes.map((n) => n.clave))}>
                Marcar todas como leídas
              </button>
            )}
          </div>
          {pendientes.length === 0 && <div className="campana__vacio">No tiene notificaciones nuevas.</div>}
          {pendientes.map((n) => (
            <button
              key={n.clave}
              type="button"
              className="campana__item campana__item--nueva"
              onClick={() => abrir(n)}
            >
              <strong>{n.titulo}</strong>
              <span>{n.detalle}</span>
              <span>{fmt(n.fecha)}</span>
            </button>
          ))}
          <button
            type="button"
            className="campana__pie"
            onClick={() => {
              setAbierto(false);
              navigate("/notificaciones");
            }}
          >
            Ver todas las notificaciones
          </button>
        </div>
      )}
    </div>
  );
}
