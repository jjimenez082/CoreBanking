import { useCallback, useEffect, useRef, useState } from "react";
import { useAuth } from "react-oidc-context";
import { EVENTO_SESION_VENCIDA } from "../api/client";

const MIN_INACTIVIDAD = Number(import.meta.env.VITE_INACTIVIDAD_MINUTOS ?? 15);
const SEG_AVISO = 60; // segundos de aviso antes de cerrar por inactividad
const EVENTOS_ACTIVIDAD = ["mousemove", "mousedown", "keydown", "scroll", "touchstart"] as const;

type Estado = "activa" | "aviso" | "vencida";

/**
 * Control de sesion del Backoffice:
 *  - Inactividad: tras MIN_INACTIVIDAD minutos sin uso avisa 60 s y luego cierra la sesion.
 *  - Token vencido: si la API responde 401 o la renovacion silenciosa falla, pide reingresar.
 * El token se renueva solo mientras haya actividad (automaticSilentRenew).
 */
export function SesionGuardian() {
  const auth = useAuth();
  const [estado, setEstado] = useState<Estado>("activa");
  const [restante, setRestante] = useState(SEG_AVISO);
  const ultima = useRef(Date.now());
  const estadoRef = useRef<Estado>("activa");
  estadoRef.current = estado;

  const marcarActividad = useCallback(() => {
    if (estadoRef.current === "activa") ultima.current = Date.now();
  }, []);

  // Actividad del usuario
  useEffect(() => {
    EVENTOS_ACTIVIDAD.forEach((e) => window.addEventListener(e, marcarActividad, { passive: true }));
    return () => EVENTOS_ACTIVIDAD.forEach((e) => window.removeEventListener(e, marcarActividad));
  }, [marcarActividad]);

  // Reloj de inactividad (cada segundo)
  useEffect(() => {
    const id = window.setInterval(() => {
      if (estadoRef.current === "vencida") return;
      const inactivo = (Date.now() - ultima.current) / 1000;
      const limite = MIN_INACTIVIDAD * 60;
      if (inactivo >= limite + SEG_AVISO) {
        void auth.removeUser().then(() => setEstado("vencida"));
        setEstado("vencida");
      } else if (inactivo >= limite) {
        setEstado("aviso");
        setRestante(Math.max(0, Math.ceil(limite + SEG_AVISO - inactivo)));
      }
    }, 1000);
    return () => window.clearInterval(id);
  }, [auth]);

  // 401 de la API o fallo al renovar el token
  useEffect(() => {
    const vencida = () => setEstado("vencida");
    // Un 401 aislado no cierra la sesion: primero se intenta renovar el token; solo si el IdP lo rechaza se pide reingresar.
    let renovando = false;
    const alRecibir401 = () => {
      if (renovando || estadoRef.current === "vencida") return;
      renovando = true;
      auth.signinSilent()
        .then((u) => { if (!u) vencida(); })
        .catch(vencida)
        .finally(() => { renovando = false; });
    };
    window.addEventListener(EVENTO_SESION_VENCIDA, alRecibir401);
    const quitar = auth.events.addSilentRenewError(vencida);
    const quitar2 = auth.events.addAccessTokenExpired(() => {
      // Con actividad reciente se espera la renovacion; si no, queda como vencida.
      if (estadoRef.current !== "activa") setEstado("vencida");
    });
    return () => {
      window.removeEventListener(EVENTO_SESION_VENCIDA, alRecibir401);
      quitar();
      quitar2();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth.events]);

  if (estado === "activa") return null;

  return (
    <div className="sesion-modal" role="alertdialog" aria-modal="true">
      <div className="sesion-modal__caja">
        {estado === "aviso" ? (
          <>
            <h3>¿Sigues ahí?</h3>
            <p>Por inactividad tu sesión se cerrará en <strong>{restante}</strong> segundos.</p>
            <button
              onClick={() => {
                ultima.current = Date.now();
                setEstado("activa");
              }}
            >
              Continuar en la sesión
            </button>
          </>
        ) : (
          <>
            <h3>Tu sesión venció</h3>
            <p>Por seguridad cerramos la sesión. Ingresa de nuevo para continuar; lo que no hayas guardado en pantalla puede perderse (los borradores guardados se conservan).</p>
            <button onClick={() => void auth.signinRedirect()}>Ingresar de nuevo</button>
          </>
        )}
      </div>
    </div>
  );
}
