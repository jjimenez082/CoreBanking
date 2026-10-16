import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { useLeidas } from "../api/useNotificaciones";
import { mensajeError } from "../api/mensajes";
import type { NotificacionResponse } from "../api/tipos";

type Filtro = "todas" | "noLeidas" | "leidas";

const fmt = (iso: string) =>
  new Date(iso).toLocaleString("es-CR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });

/** App Notificaciones: historial completo (leidas y no leidas) del usuario. */
export function NotificacionesPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { leidas, marcarLeidas, marcarNoLeidas } = useLeidas();
  const [items, setItems] = useState<NotificacionResponse[]>([]);
  const [filtro, setFiltro] = useState<Filtro>("todas");
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    try {
      const r = await api.get<NotificacionResponse[]>("/api/notificaciones", { params: { historial: true } });
      setItems(r.data);
      setError(null);
    } catch (e) {
      setError(mensajeError(e, "No se pudieron cargar las notificaciones."));
    } finally {
      setCargando(false);
    }
  }, [api]);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  const noLeidas = items.filter((n) => !leidas.has(n.clave));
  const visibles = items.filter((n) => (filtro === "todas" ? true : filtro === "leidas" ? leidas.has(n.clave) : !leidas.has(n.clave)));

  return (
    <div>
      <h1>Notificaciones</h1>
      <p className="nota">Historial de los últimos 60 días. La campanita solo muestra las no leídas.</p>

      <div className="notif-barra">
        {(["todas", "noLeidas", "leidas"] as Filtro[]).map((f) => (
          <button key={f} type="button" className={`notif-filtro ${filtro === f ? "notif-filtro--activo" : ""}`} onClick={() => setFiltro(f)}>
            {f === "todas" ? `Todas (${items.length})` : f === "noLeidas" ? `No leídas (${noLeidas.length})` : `Leídas (${items.length - noLeidas.length})`}
          </button>
        ))}
        {noLeidas.length > 0 && (
          <button type="button" className="wiz-enlace" onClick={() => marcarLeidas(noLeidas.map((n) => n.clave))}>
            Marcar todas como leídas
          </button>
        )}
      </div>

      {error && <p className="mensaje-error">{error}</p>}
      {cargando && <p className="nota">Cargando...</p>}
      {!cargando && visibles.length === 0 && <p className="nota">No hay notificaciones en esta vista.</p>}

      <div className="notif-lista">
        {visibles.map((n) => {
          const leida = leidas.has(n.clave);
          return (
            <div key={n.clave} className={`notif-fila ${leida ? "" : "notif-fila--nueva"}`}>
              <button
                type="button"
                className="notif-fila__cuerpo"
                onClick={() => {
                  marcarLeidas([n.clave]);
                  navigate(n.ruta);
                }}
              >
                <strong>{n.titulo}</strong>
                <span>{n.detalle}</span>
                <span>{fmt(n.fecha)}</span>
              </button>
              <button type="button" className="wiz-enlace" onClick={() => (leida ? marcarNoLeidas([n.clave]) : marcarLeidas([n.clave]))}>
                {leida ? "Marcar como no leída" : "Marcar como leída"}
              </button>
            </div>
          );
        })}
      </div>
    </div>
  );
}
