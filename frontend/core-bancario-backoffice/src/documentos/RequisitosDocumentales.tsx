import { useEffect, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import type { DocumentoRequerido } from "../api/tipos";
import { fmtFecha } from "../api/mensajes";

const TEXTO: Record<DocumentoRequerido["estado"], string> = { Cumple: "Vigente", PorVencer: "Por vencer", Vencido: "Vencido", Falta: "Falta" };
const CLASE: Record<DocumentoRequerido["estado"], string> = { Cumple: "docs-req--ok", PorVencer: "docs-req--aviso", Vencido: "docs-req--error", Falta: "docs-req--error" };
const ICONO: Record<DocumentoRequerido["estado"], string> = { Cumple: "✓", PorVencer: "!", Vencido: "✕", Falta: "✕" };

/**
 * Documentos que exigen los roles activos del cliente y su situacion (Vigente, Por vencer, Vencido o Falta). No muestra nada si
 * ningun rol exige documentos. `recarga` fuerza volver a consultar despues de una accion.
 */
export function RequisitosDocumentales({ idInterlocutor, recarga = 0 }: { idInterlocutor: number; recarga?: number }) {
  const api = useApiClient();
  const [items, setItems] = useState<DocumentoRequerido[]>([]);

  useEffect(() => {
    let vivo = true;
    void api.get<DocumentoRequerido[]>(`/api/interlocutores/${idInterlocutor}/documentos/requeridos`)
      .then((r) => { if (vivo) setItems(r.data); })
      .catch(() => { if (vivo) setItems([]); });
    return () => { vivo = false; };
  }, [api, idInterlocutor, recarga]);

  if (items.length === 0) return null;
  const pendientes = items.filter((i) => i.estado === "Falta" || i.estado === "Vencido").length;

  return (
    <section className={`docs-req ${pendientes > 0 ? "docs-req--con-pendientes" : ""}`} aria-label="Documentos requeridos por los roles del cliente">
      <header className="docs-req__titulo">
        <strong>Documentos requeridos</strong>
        <span className="nota" style={{ margin: 0 }}>{pendientes === 0 ? "Cumple con los documentos que exigen sus roles." : `${pendientes} sin cumplir.`}</span>
      </header>
      <ul className="docs-req__lista">
        {items.map((i) => (
          <li key={i.idTipoDocumento} className={`docs-req__item ${CLASE[i.estado]}`}>
            <span className="docs-req__icono" aria-hidden="true">{ICONO[i.estado]}</span>
            <span className="docs-req__nombre">{i.tipo}<small>{i.roles.join(", ")}</small></span>
            <span className="docs-req__estado">
              {TEXTO[i.estado]}{i.vence && i.estado !== "Falta" ? ` · ${i.estado === "Vencido" ? "venció" : "vence"} ${fmtFecha(i.vence)}` : ""}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
