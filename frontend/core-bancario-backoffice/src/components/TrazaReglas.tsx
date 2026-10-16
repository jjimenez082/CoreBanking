import type { EvaluacionResultado, TrazaRegla } from "../api/reglas";

const COLOR_DECISION: Record<string, string> = { Aprobar: "#15803d", Rechazar: "#b91c1c", Referir: "#b45309" };

/** Muestra por qué se llegó a un resultado: cada regla revisada y cada condición con el dato observado. */
export function TrazaReglas({ traza }: { traza: TrazaRegla[] }) {
  if (traza.length === 0) return <p className="nota">No se evaluó ninguna regla (la versión no tiene reglas activas).</p>;
  return (
    <div>
      {traza.map((r) => (
        <details key={r.regla} open={r.cumplida} style={{ marginBottom: "0.4rem" }}>
          <summary><strong>{r.cumplida ? "✔" : "✘"} {r.regla}</strong> · {r.nombre} <small style={{ opacity: 0.7 }}>{r.cumplida ? "se cumple" : "no se cumple"}</small></summary>
          {r.condiciones.length === 0 ? <p className="nota" style={{ marginLeft: "1.2rem" }}>Sin condiciones: regla por defecto.</p> : (
            <ul style={{ margin: "0.3rem 0 0.3rem 1.2rem", paddingLeft: "0.8rem" }}>
              {r.condiciones.map((c, i) => (
                <li key={`${c.condicion}-${i}`} style={{ color: c.resultado ? "#15803d" : "#6b7280" }}>
                  {c.resultado ? "✔" : "✘"} {c.negada ? "NO " : ""}<strong>{c.condicion}</strong> — {c.atributo}: dato <code>{c.observado ?? "sin dato"}</code>{c.esperado ? <> frente a <code>{c.esperado}</code></> : null}
                  {c.error && <span style={{ color: "#b91c1c" }}> · {c.error}</span>}
                </li>
              ))}
            </ul>
          )}
        </details>
      ))}
    </div>
  );
}

/** Resultado completo de una evaluación: decisión, valores asignados, mensajes, reglas aplicadas y explicación. */
export function ResultadoReglas({ r }: { r: EvaluacionResultado }) {
  const asign = Object.entries(r.asignaciones ?? {});
  return (
    <div>
      <p style={{ fontSize: "1.1rem", margin: "0.3rem 0" }}>
        Decisión:{" "}
        {r.decision ? <strong style={{ color: COLOR_DECISION[r.decision] ?? "inherit" }}>{r.decision}</strong> : <em>ninguna</em>}
        {!r.coincidioAlguna && <small style={{ marginLeft: "0.6rem", opacity: 0.7 }}>(ninguna regla se cumplió)</small>}
      </p>
      {r.reglasAplicadas.length > 0 && <p style={{ margin: "0.2rem 0" }}>Reglas aplicadas: <strong>{r.reglasAplicadas.join(", ")}</strong></p>}
      {asign.length > 0 && <p style={{ margin: "0.2rem 0" }}>Valores asignados: {asign.map(([k, v]) => <code key={k} style={{ marginRight: "0.6rem" }}>{k} = {v}</code>)}</p>}
      {r.mensajes.length > 0 && <ul style={{ margin: "0.2rem 0" }}>{r.mensajes.map((m, i) => (<li key={i}>{m}</li>))}</ul>}
      <h4 style={{ margin: "0.8rem 0 0.3rem" }}>Explicación</h4>
      <TrazaReglas traza={r.traza} />
    </div>
  );
}
