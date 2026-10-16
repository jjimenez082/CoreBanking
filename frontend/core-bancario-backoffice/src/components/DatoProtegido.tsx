import { useState, type ReactNode } from "react";
import { useApiClient } from "../api/useApiClient";
import { mensajeError } from "../api/mensajes";
import type { MascarasInfoResponse, RevelarResponse } from "../api/tipos";

/** Campos sensibles enmascarables (códigos del catálogo del backend). */
export const CAMPO = {
  identificacion: "CLI_IDENTIFICACION", correo: "CLI_CORREO", telefono: "CLI_TELEFONO", direccion: "CLI_DIRECCION",
  fechaNacimiento: "CLI_FECHA_NACIMIENTO", montos: "PF_MONTOS", cuentaExterna: "CE_NUMERO_CUENTA",
} as const;

export const estaEnmascarado = (m: MascarasInfoResponse | null | undefined, campo: string) => !!m?.campos.some((c) => c.campo === campo);
export const puedeRevelar = (m: MascarasInfoResponse | null | undefined, campo: string) => !!m?.campos.some((c) => c.campo === campo && c.puedeRevelar);

/** Pide el valor real de un dato enmascarado; el servidor lo registra en la bitácora de consulta de datos sensibles. */
export function useRevelar(idInterlocutor: number) {
  const api = useApiClient();
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function revelar(campo: string, idItem?: number | null): Promise<Record<string, string | null> | null> {
    setCargando(true); setError(null);
    try {
      const r = await api.post<RevelarResponse>(`/api/interlocutores/${idInterlocutor}/revelar`, { campo, idItem: idItem ?? null });
      return r.data.valores;
    } catch (e) { setError(mensajeError(e, "No se pudo mostrar el dato.")); return null; }
    finally { setCargando(false); }
  }
  return { revelar, cargando, error };
}

/**
 * Muestra un dato que puede estar enmascarado por el perfil de datos del usuario. Si el perfil lo permite aparece «Ver», que pide el valor real
 * (queda registrado quién lo vio y cuándo) y permite volver a ocultarlo.
 */
export function DatoProtegido({ idInterlocutor, mascaras, campo, idItem, mostrado, real }: {
  idInterlocutor: number;
  mascaras: MascarasInfoResponse | null | undefined;
  campo: string;
  idItem?: number | null;
  /** Lo que llegó del servidor (enmascarado si corresponde). */
  mostrado: ReactNode;
  /** Convierte los valores reales (según el campo: numero, valor, detalle..., fechaNacimiento, numeroCuenta) en lo que se muestra. */
  real: (v: Record<string, string | null>) => ReactNode;
}) {
  const { revelar, cargando, error } = useRevelar(idInterlocutor);
  const [valores, setValores] = useState<Record<string, string | null> | null>(null);
  if (!estaEnmascarado(mascaras, campo)) return <>{mostrado}</>;
  return (
    <span>
      {valores ? real(valores) : mostrado}
      {puedeRevelar(mascaras, campo) && (
        <button type="button" className="boton-secundario" style={{ marginLeft: 8, padding: "0 0.5rem", fontSize: "0.75rem" }} disabled={cargando}
          title={valores ? "Volver a ocultar" : "Mostrar el dato completo (queda registrado)"}
          onClick={async () => { if (valores) { setValores(null); return; } const v = await revelar(campo, idItem); if (v) setValores(v); }}>
          {valores ? "Ocultar" : "Ver"}
        </button>
      )}
      {error && <span className="mensaje-error" style={{ marginLeft: 8 }}>{error}</span>}
    </span>
  );
}
