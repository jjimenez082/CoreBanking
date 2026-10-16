import { useCallback, useEffect, useMemo, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import { mensajeError } from "../api/mensajes";
import type { RolRequisitos, RolRequisitosAdmin } from "../api/tipos";

type Tipo = "Fisica" | "Juridica";
const SIEMPRE: Record<Tipo, { seccion: string; etiquetas: string[] }[]> = {
  Fisica: [
    { seccion: "Datos personales", etiquetas: ["Primer nombre", "Primer apellido"] },
    { seccion: "Identificación", etiquetas: ["Tipo de identificación", "Número de identificación"] },
    { seccion: "Direcciones", etiquetas: ["Dirección exacta (línea 1) de cada dirección registrada"] },
  ],
  Juridica: [
    { seccion: "Datos de la organización", etiquetas: ["Razón social", "Forma jurídica", "País de constitución", "Estado legal"] },
    { seccion: "Identificación", etiquetas: ["Tipo de identificación", "Número de identificación"] },
    { seccion: "Direcciones", etiquetas: ["Dirección exacta (línea 1) de cada dirección registrada"] },
  ],
};

/**
 * Administración › Requisitos por rol (permiso ROL_ADMINISTRACION). Matriz campo × rol: un campo marcado es obligatorio para ese rol;
 * sin marcar es opcional. Aplica a altas y cambios de rol NUEVOS y a modificaciones (la persona cumple la unión de sus roles vigentes).
 * Todo cambio queda en la auditoría.
 */
export function RolesRequisitosPage() {
  const api = useApiClient();
  const [datos, setDatos] = useState<RolRequisitosAdmin | null>(null);
  const [filas, setFilas] = useState<RolRequisitos[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [tipo, setTipo] = useState<Tipo>("Fisica");

  const cargar = useCallback(async () => {
    try {
      const r = await api.get<RolRequisitosAdmin>(`/api/roles/requisitos?tipo=${tipo}`);
      setDatos(r.data); setFilas(r.data.roles); setError(null);
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los requisitos.")); }
  }, [api, tipo]);
  useEffect(() => { void cargar(); }, [cargar]);

  const secciones = useMemo(() => {
    const m = new Map<string, NonNullable<typeof datos>["campos"]>();
    (datos?.campos ?? []).forEach((c) => m.set(c.seccion, [...(m.get(c.seccion) ?? []), c]));
    return [...m.entries()];
  }, [datos]);

  const cambiar = (id: number, codigo: string, v: boolean) =>
    setFilas((fs) => fs.map((f) => (f.idTipoRol !== id ? f
      : { ...f, requeridos: v ? [...f.requeridos, codigo] : f.requeridos.filter((x) => x !== codigo) })));
  const modificadas = filas.filter((f) => {
    const o = datos?.roles.find((x) => x.idTipoRol === f.idTipoRol);
    if (!o) return true;
    return [...o.requeridos].sort().join("|") !== [...f.requeridos].sort().join("|");
  });

  async function guardar() {
    setGuardando(true); setError(null); setExito(null);
    try {
      const r = await api.put<RolRequisitosAdmin>("/api/roles/requisitos", {
        tipoPersona: tipo,
        roles: modificadas.map((f) => ({ idTipoRol: f.idTipoRol, requeridos: f.requeridos })),
      });
      setDatos(r.data); setFilas(r.data.roles); setExito("Requisitos guardados.");
    } catch (e) { setError(mensajeError(e, "No se pudieron guardar los requisitos.")); }
    finally { setGuardando(false); }
  }

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Requisitos por rol</h1>
          <div className="nota">Marque los campos obligatorios de cada rol; los no marcados son opcionales. «Dirección» significa al menos una; sus campos internos se parametrizan aparte. Los documentos que exige cada rol se definen en Carpetas y tipos de documento.</div>
        </div>
      </div>
      <div role="tablist" style={{ display: "flex", gap: 8, margin: "8px 0" }}>
        {(["Fisica", "Juridica"] as Tipo[]).map((t) => (
          <button key={t} type="button" role="tab" aria-selected={tipo === t}
            className={tipo === t ? "" : "boton-secundario"}
            disabled={guardando || (tipo !== t && modificadas.length > 0)}
            title={tipo !== t && modificadas.length > 0 ? "Guarde o descarte los cambios antes de cambiar de pestaña" : undefined}
            onClick={() => { setDatos(null); setFilas([]); setTipo(t); }}>
            {t === "Fisica" ? "Persona" : "Organización"}
          </button>
        ))}
      </div>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <section className="wiz__tarjeta">
        <div className="wiz__cuerpo" style={{ overflowX: "auto" }}>
          <table className="tabla tabla--compacta">
            <thead>
              <tr><th>Campo</th>{filas.map((f) => <th key={f.idTipoRol} style={{ textAlign: "center" }}>{f.nombre}</th>)}</tr>
            </thead>
            <tbody>
              {secciones.map(([sec, campos]) => {
                const fijos = SIEMPRE[tipo].find((x) => x.seccion === sec)?.etiquetas ?? [];
                return [
                  <tr key={`s-${sec}`}><th colSpan={filas.length + 1} style={{ textAlign: "left", background: "var(--color-fondo-suave, #f3f5f8)" }}>{sec}</th></tr>,
                  ...fijos.map((e) => (
                    <tr key={`f-${sec}-${e}`}>
                      <td>{e}</td>
                      <td colSpan={filas.length} style={{ textAlign: "center" }} className="nota">Siempre obligatorio</td>
                    </tr>
                  )),
                  ...campos.map((c) => (
                    <tr key={c.codigo}>
                      <td>{c.etiqueta}</td>
                      {filas.map((f) => (
                        <td key={f.idTipoRol} style={{ textAlign: "center" }}>
                          <input type="checkbox" checked={f.requeridos.includes(c.codigo)} aria-label={`${f.nombre}: ${c.etiqueta}`}
                            onChange={(e) => cambiar(f.idTipoRol, c.codigo, e.target.checked)} />
                        </td>
                      ))}
                    </tr>
                  )),
                ];
              })}
            </tbody>
          </table>
          {secciones.some(([sec]) => sec === "Cumplimiento (KYC)") && (
            <p className="nota">«Cumplimiento (KYC)» se verifica al asumir el rol sobre un cliente ya registrado: el KYC y las evaluaciones se completan después del alta, por la gestión de cumplimiento.</p>
          )}
          {secciones.some(([sec]) => sec === "Relaciones requeridas") && (
            <p className="nota">«Relaciones requeridas» se verifican al asumir el rol sobre un cliente ya registrado (en el alta aún no pueden existir, porque las relaciones se crean después).</p>
          )}
          <p className="nota">Los cambios no tocan datos ya registrados: se exigen al dar de alta, al agregar el rol y al modificar a la persona (cumple la unión de sus roles vigentes). Las gestiones pendientes se revalidan al aprobarse.</p>
          <button type="button" disabled={modificadas.length === 0 || guardando} onClick={guardar}>{guardando ? "Guardando…" : "Guardar cambios"}</button>
        </div>
      </section>
    </div>
  );
}
