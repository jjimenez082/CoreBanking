import type { ReactNode } from "react";
import type { CatalogoItemResponse } from "../api/tipos";

/** Piezas de formulario compartidas por el asistente de cliente y sus pasos. */

export function Campo({ etiqueta, obligatorio, error, children }: { etiqueta: string; obligatorio?: boolean; error?: string; children: ReactNode }) {
  return (
    <label className={`wiz-campo ${error ? "wiz-campo--error" : ""}`}>
      <span>
        {etiqueta}
        {obligatorio && <span className="wiz-campo__req" aria-hidden="true"> *</span>}
      </span>
      {children}
      {error && <span className="wiz-campo__error">{error}</span>}
    </label>
  );
}

export function SelectCat({
  items, valor, onCambiar, respaldoNombre,
}: { items: CatalogoItemResponse[]; valor: number | null | undefined; onCambiar: (id: number) => void; respaldoNombre?: string }) {
  const enLista = valor ? items.some((i) => i.id === valor) : true;
  return (
    <select value={valor ?? 0} onChange={(e) => onCambiar(Number(e.target.value))}>
      <option value={0}>Seleccione...</option>
      {!enLista && valor ? <option value={valor}>{respaldoNombre ?? valor} (inactivo)</option> : null}
      {items.map((i) => (
        <option key={i.id} value={i.id}>{i.nombre}</option>
      ))}
    </select>
  );
}
