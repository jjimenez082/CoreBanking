import { useState } from "react";
import { usePermisos } from "../contexts/PermisosContext";
import type { CoincidenciaFila, CoincidenciasResultado } from "../api/tipos";

const fmt = (iso?: string | null) => (iso ? iso.slice(0, 10).split("-").reverse().join("/") : "—");

/**
 * Control de calidad de datos: muestra las personas ya registradas que se parecen a la que se esta registrando, para que
 * quien registra decida: es la misma persona (se va a su registro) o no lo es (se continua indicando el motivo).
 * `exigirMotivo` = false para el prospecto (solo aviso).
 */
export function RevisionDuplicados({
  resultado, exigirMotivo, contexto, onVolver, onMisma, onContinuar, onGuardarBorrador,
}: {
  resultado: CoincidenciasResultado;
  exigirMotivo: boolean;
  contexto: string;
  onVolver: () => void;
  onMisma: (c: CoincidenciaFila) => void;
  onContinuar: (motivo: string) => void;
  /** Si se indica, ofrece guardar lo digitado como borrador antes de ir al registro existente. Devuelve true si se guardo. */
  onGuardarBorrador?: () => Promise<boolean>;
}) {
  const { tienePermiso } = usePermisos();
  const [motivo, setMotivo] = useState("");
  const [confirmando, setConfirmando] = useState<CoincidenciaFila | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [falloBorrador, setFalloBorrador] = useState(false);
  const hayFuerte = resultado.items.some((i) => i.fuerte);
  // El bloqueo aplica al alta/promocion de clientes (exigirMotivo); al crear un prospecto solo es un aviso.
  const bloqueado = exigirMotivo && hayFuerte && resultado.fuerteBloquea && !tienePermiso("CLI_DUPLICADO_AUTORIZAR");
  const motivoOk = !exigirMotivo || motivo.trim().length >= 10;

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Posibles personas ya registradas</h1>
          <div className="nota">{contexto}</div>
        </div>
      </div>

      <div className={hayFuerte ? "wiz__alerta" : "wiz__aviso"}>
        {hayFuerte
          ? "Hay una coincidencia fuerte: es muy probable que se trate de la misma persona."
          : `Se encontraron coincidencias iguales o mayores al ${resultado.umbralAlerta} %.`}{" "}
        Revise antes de continuar.
      </div>

      <section className="wiz__tarjeta">
        <div className="wiz__cuerpo" style={{ overflowX: "auto" }}>
          <table className="tabla">
            <thead>
              <tr><th>Coincidencia</th><th>Registro</th><th>Nombre</th><th>Nacimiento</th><th>Identificaciones</th><th>Por qué</th><th /></tr>
            </thead>
            <tbody>
              {resultado.items.map((c) => (
                <tr key={`${c.origen}-${c.id}`}>
                  <td><strong style={{ color: c.fuerte ? "var(--color-error)" : undefined }}>{c.porcentaje} %</strong>{c.fuerte && " · fuerte"}</td>
                  <td>{c.origen}{c.numero ? ` ${c.numero}` : ""}<div className="nota">{c.roles || c.estado}</div></td>
                  <td>{c.nombre}</td>
                  <td>{fmt(c.fechaNacimiento)}</td>
                  <td>{c.identificaciones || "—"}</td>
                  <td>{c.motivos.join("; ")}</td>
                  <td style={{ whiteSpace: "nowrap" }}>
                    <button type="button" className="wiz-enlace" onClick={() => setConfirmando(c)}>Ir al registro</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <div style={{ marginTop: "1.25rem", maxWidth: 640 }}>
            <label className="wiz-campo">
              <span>{exigirMotivo ? "Si NO es la misma persona, indique por qué" : "Si NO es la misma persona, puede indicar por qué (opcional)"}{exigirMotivo && <span className="wiz-campo__req"> *</span>}</span>
              <textarea rows={3} maxLength={400} value={motivo} onChange={(e) => setMotivo(e.target.value)}
                placeholder="Ej.: Es un homónimo; la fecha de nacimiento y la cédula verificadas en el documento son distintas." />
              {exigirMotivo && motivo.trim().length > 0 && motivo.trim().length < 10 && <span className="wiz-campo__error">Mínimo 10 caracteres.</span>}
            </label>
            {bloqueado && (
              <p className="mensaje-error">Con una coincidencia fuerte solo un usuario con permiso para autorizar duplicados puede continuar.</p>
            )}
          </div>
        </div>
        {confirmando && (
          <div role="alertdialog" className="wiz__confirmar">
            <span>
              Va a salir de este registro para abrir a <strong>{confirmando.nombre}</strong>
              {confirmando.numero ? ` (${confirmando.origen} ${confirmando.numero})` : ` (${confirmando.origen})`}. {onGuardarBorrador
                ? <>Puede guardar lo digitado como borrador para retomarlo después, o salir sin guardar (<strong>perderá los datos que está digitando</strong>).</>
                : <strong>Perderá los datos que está digitando.</strong>}
              {falloBorrador && <> <strong style={{ color: "var(--color-error)" }}>No se pudo guardar el borrador.</strong></>}
            </span>
            <button type="button" className="boton-secundario" disabled={guardando} onClick={() => { setConfirmando(null); setFalloBorrador(false); }}>Seguir aquí</button>
            {onGuardarBorrador && (
              <button type="button" disabled={guardando} onClick={async () => {
                setGuardando(true); setFalloBorrador(false);
                const ok = await onGuardarBorrador();
                setGuardando(false);
                if (ok) onMisma(confirmando); else setFalloBorrador(true);
              }}>{guardando ? "Guardando…" : "Guardar borrador e ir al registro"}</button>
            )}
            <button type="button" className="wiz__peligro" disabled={guardando} onClick={() => onMisma(confirmando)}>{onGuardarBorrador ? "Salir sin guardar" : "Sí, ir al registro existente"}</button>
          </div>
        )}
        <div className="wiz__pie">
          <div className="wiz__pie-grupo">
            <button type="button" className="wiz-enlace" onClick={onVolver}>← Volver a corregir</button>
          </div>
          <div className="wiz__pie-grupo">
            <button type="button" disabled={bloqueado || !motivoOk} onClick={() => onContinuar(motivo.trim())}>No es la misma persona, continuar</button>
          </div>
        </div>
      </section>
    </div>
  );
}
