import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "react-oidc-context";
import { useApiClient } from "../api/useApiClient";
import type { NotificacionResponse } from "../api/tipos";

/**
 * Campana de notificaciones junto al usuario. Las notificaciones las deriva el
 * backend (gestiones por aprobar, gestiones propias resueltas, borradores por
 * vencer); el estado "leida" vive en este navegador, por usuario.
 */
const INTERVALO_MS = 60_000;

function leerLeidas(clave: string): Set<string> {
  try {
    const crudo = localStorage.getItem(clave);
    return new Set(crudo ? (JSON.parse(crudo) as string[]) : []);
  } catch {
    return new Set();
  }
}

function guardarLeidas(clave: string, leidas: Set<string>) {
  try {
    // Se limita el tamano para que no crezca indefinidamente.
    localStorage.setItem(clave, JSON.stringify([...leidas].slice(-300)));
  } catch {
    /* sin almacenamiento: la campana sigue funcionando sin recordar lo leido */
  }
}

const fmt = (iso: string) =>
  new Date(iso).toLocaleString("es-CR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });

export function CampanaNotificaciones() {
  const api = useApiClient();
  const auth = useAuth();
  const navigate = useNavigate();
  const clave = `notificaciones-leidas:${auth.user?.profile.sub ?? "anon"}`;

  const [items, setItems] = useState<NotificacionResponse[]>([]);
  const [leidas, setLeidas] = useState<Set<string>>(() => leerLeidas(clave));
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

  const noLeidas = items.filter((n) => !leidas.has(n.clave)).length;

  function marcar(claves: string[]) {
    const nueva = new Set(leidas);
    claves.forEach((c) => nueva.add(c));
    setLeidas(nueva);
    guardarLeidas(clave, nueva);
  }

  function abrir(n: NotificacionResponse) {
    marcar([n.clave]);
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
              <button type="button" className="wiz-enlace" onClick={() => marcar(items.map((n) => n.clave))}>
                Marcar todas como leídas
              </button>
            )}
          </div>
          {items.length === 0 && <div className="campana__vacio">No tiene notificaciones.</div>}
          {items.map((n) => (
            <button
              key={n.clave}
              type="button"
              className={`campana__item ${leidas.has(n.clave) ? "" : "campana__item--nueva"}`}
              onClick={() => abrir(n)}
            >
              <strong>{n.titulo}</strong>
              <span>{n.detalle}</span>
              <span>{fmt(n.fecha)}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
