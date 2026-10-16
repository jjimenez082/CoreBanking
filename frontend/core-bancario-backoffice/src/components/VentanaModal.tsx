import { useEffect, type ReactNode } from "react";

/**
 * Ventana modal estándar de los mantenimientos (misma apariencia que el detalle de una gestión). Todo mantenimiento que se
 * abre desde una ficha o una lista (solicitar un cambio, otorgar, bloquear...) usa esta ventana en lugar de desplegar
 * formularios debajo de la página, para que la experiencia sea la misma en todo el sistema.
 */
export function VentanaModal({ titulo, onCerrar, acciones, ancho, children }: {
  titulo: string; onCerrar: () => void; acciones?: ReactNode; ancho?: number; children: ReactNode;
}) {
  useEffect(() => {
    const alTeclear = (e: KeyboardEvent) => { if (e.key === "Escape") onCerrar(); };
    window.addEventListener("keydown", alTeclear);
    return () => window.removeEventListener("keydown", alTeclear);
  }, [onCerrar]);

  return (
    <div className="gestion-modal" role="dialog" aria-modal="true" aria-label={titulo}>
      <div className="gestion-modal__caja" style={ancho ? { width: `min(${ancho}px, 100%)` } : undefined}>
        <h3>{titulo}</h3>
        <div className="gestion-modal__cuerpo">{children}</div>
        <div className="gestion-modal__acciones">
          {acciones}
          <button type="button" className="boton-secundario" onClick={onCerrar} style={{ marginLeft: "auto" }}>Cerrar</button>
        </div>
      </div>
    </div>
  );
}

/**
 * Envuelve un formulario de mantenimiento existente (título, errores, campos y botones Guardar / Cancelar) en la ventana modal
 * estándar. Se cierra con la tecla Escape o haciendo clic en la ✕; el cierre equivale al botón Cancelar del formulario.
 */
export function PanelModal({ onCerrar, ancho, children }: { onCerrar: () => void; ancho?: number; children: ReactNode }) {
  useEffect(() => {
    const alTeclear = (e: KeyboardEvent) => { if (e.key === "Escape") onCerrar(); };
    window.addEventListener("keydown", alTeclear);
    return () => window.removeEventListener("keydown", alTeclear);
  }, [onCerrar]);

  return (
    <div className="gestion-modal" role="dialog" aria-modal="true">
      <div className="gestion-modal__caja panel-formulario panel-formulario--modal" style={ancho ? { width: `min(${ancho}px, 100%)` } : undefined}>
        <button type="button" className="panel-modal__cerrar" aria-label="Cerrar" onClick={onCerrar}>✕</button>
        <div className="gestion-modal__cuerpo">{children}</div>
      </div>
    </div>
  );
}
