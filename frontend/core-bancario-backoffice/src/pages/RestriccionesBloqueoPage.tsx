import { useCallback, useEffect, useState } from "react";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { mensajeError } from "../api/mensajes";
import { VentanaModal } from "../components/VentanaModal";
import type { MatrizRestriccion, TipoBloqueoRestriccion } from "../api/tipos";

/**
 * Administración › Restricciones por tipo de bloqueo (permiso BLO_RESTRICCIONES).
 * Matriz: por cada tipo de bloqueo se marcan las acciones que impide mientras el cliente tenga un bloqueo vigente de ese tipo.
 * Es configuración: se guarda directo y queda en la auditoría. Los módulos de Cuentas y Créditos consultan esta matriz.
 */
export function RestriccionesBloqueoPage() {
  const api = useApiClient();
  const { tienePermiso } = usePermisos();
  const [matriz, setMatriz] = useState<MatrizRestriccion | null>(null);
  const [marcas, setMarcas] = useState<Record<number, Set<number>>>({});
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [sucio, setSucio] = useState(false);
  const [tipo, setTipo] = useState<{ id: number | null; codigo: string; nombre: string; descripcion: string } | null>(null);
  const [errorTipo, setErrorTipo] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    setError(null);
    try {
      const r = await api.get<MatrizRestriccion>("/api/bloqueos-restricciones");
      setMatriz(r.data);
      const m: Record<number, Set<number>> = {};
      r.data.tipos.forEach((t) => { m[t.idTipoBloqueo] = new Set(t.idsAcciones); });
      setMarcas(m); setSucio(false);
    } catch (e) { setError(mensajeError(e, "No se pudo cargar la matriz de restricciones.")); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);

  function abrirTipo(t?: TipoBloqueoRestriccion) {
    setErrorTipo(null);
    setTipo(t ? { id: t.idTipoBloqueo, codigo: t.codigo, nombre: t.nombre, descripcion: t.descripcion ?? "" } : { id: null, codigo: "", nombre: "", descripcion: "" });
  }

  async function guardarTipo() {
    if (!tipo) return;
    setGuardando(true); setErrorTipo(null);
    try {
      const cuerpo = { codigo: tipo.codigo, nombre: tipo.nombre, descripcion: tipo.descripcion.trim() || null };
      if (tipo.id === null) await api.post("/api/bloqueos-restricciones/tipos", cuerpo);
      else await api.put(`/api/bloqueos-restricciones/tipos/${tipo.id}`, cuerpo);
      // Solo se recarga la lista de tipos: las marcas sin guardar de la matriz se conservan.
      const r = await api.get<MatrizRestriccion>("/api/bloqueos-restricciones");
      setMatriz(r.data);
      setMarcas((prev) => { const m = { ...prev }; r.data.tipos.forEach((t) => { if (!m[t.idTipoBloqueo]) m[t.idTipoBloqueo] = new Set(t.idsAcciones); }); return m; });
      setExito(tipo.id === null ? "Tipo de bloqueo creado. Marque las acciones que debe impedir y guarde." : "Tipo de bloqueo actualizado.");
      setTipo(null);
    } catch (e) { setErrorTipo(mensajeError(e, "No se pudo guardar el tipo de bloqueo.")); }
    finally { setGuardando(false); }
  }

  function alternar(idTipo: number, idAccion: number) {
    setMarcas((prev) => {
      const s = new Set(prev[idTipo] ?? []);
      if (s.has(idAccion)) s.delete(idAccion); else s.add(idAccion);
      return { ...prev, [idTipo]: s };
    });
    setSucio(true); setExito(null);
  }

  async function guardar() {
    if (!matriz) return;
    setGuardando(true); setError(null); setExito(null);
    try {
      await api.put("/api/bloqueos-restricciones", {
        tipos: matriz.tipos.map((t) => ({ idTipoBloqueo: t.idTipoBloqueo, idsAcciones: Array.from(marcas[t.idTipoBloqueo] ?? []) })),
      });
      setExito("Restricciones guardadas."); await cargar();
    } catch (e) { setError(mensajeError(e, "No se pudieron guardar las restricciones.")); }
    finally { setGuardando(false); }
  }

  if (!tienePermiso("BLO_RESTRICCIONES")) return <p className="mensaje-error">No tiene permiso para administrar las restricciones por bloqueo.</p>;
  if (!matriz) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Restricciones por tipo de bloqueo</h1>
          <div className="nota">Marque las acciones que impide cada tipo de bloqueo mientras el cliente tenga uno vigente de ese tipo.</div>
        </div>
        <div style={{ display: "flex", gap: "1rem", alignItems: "center" }}>
          <button type="button" className="boton-secundario" style={{ whiteSpace: "nowrap" }} onClick={() => abrirTipo()}>Nuevo tipo de bloqueo</button>
          <button type="button" style={{ whiteSpace: "nowrap" }} disabled={!sucio || guardando} onClick={() => void guardar()}>{guardando ? "Guardando…" : "Guardar cambios"}</button>
        </div>
      </div>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <div className="wiz__aviso">{exito}</div>}
      <section className="wiz__tarjeta">
        <div className="wiz__cuerpo" style={{ overflowX: "auto" }}>
          <table className="tabla tabla--compacta">
            <thead>
              <tr><th>Tipo de bloqueo</th>{matriz.acciones.map((a) => <th key={a.id} style={{ textAlign: "center" }} title={a.descripcion ?? a.codigo}>{a.nombre}</th>)}</tr>
            </thead>
            <tbody>
              {matriz.tipos.map((t) => (
                <tr key={t.idTipoBloqueo}>
                  <td>
                    <button type="button" className="boton-enlace" style={{ fontWeight: 600, textAlign: "left" }} title="Editar tipo de bloqueo" onClick={() => abrirTipo(t)}>{t.nombre}</button>
                    {t.descripcion && <span className="cons-descripcion">{t.descripcion}</span>}
                  </td>
                  {matriz.acciones.map((a) => (
                    <td key={a.id} style={{ textAlign: "center" }}>
                      <input type="checkbox" aria-label={`${t.nombre}: ${a.nombre}`} checked={marcas[t.idTipoBloqueo]?.has(a.id) ?? false} onChange={() => alternar(t.idTipoBloqueo, a.id)} />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          <p className="nota" style={{ marginTop: "0.75rem" }}>
            Además, un cliente con KYC vencido no puede abrir productos nuevos (abrir cuentas y solicitar créditos), aunque no tenga bloqueos.
          </p>
          <p className="nota">
            Un tipo de bloqueo nuevo nace sin restricciones: marque las acciones que debe impedir. Las acciones las define el sistema porque
            cada una afecta a los productos. Para dejar de usar un tipo, inactívelo en Listas de valores (tipo de bloqueo de cliente).
          </p>
        </div>
      </section>

      {tipo && (
        <VentanaModal
          titulo={tipo.id === null ? "Nuevo tipo de bloqueo" : `Editar tipo de bloqueo ${tipo.codigo}`}
          onCerrar={() => setTipo(null)}
          acciones={<button type="button" disabled={guardando} onClick={() => void guardarTipo()}>{guardando ? "Guardando…" : "Guardar"}</button>}
        >
          <div className="wiz__rejilla">
            <label className="wiz-campo"><span>Código *</span>
              <input type="text" maxLength={50} value={tipo.codigo} disabled={tipo.id !== null} placeholder="Ej. LISTA_RESTRICTIVA"
                onChange={(e) => setTipo({ ...tipo, codigo: e.target.value.toUpperCase() })} />
            </label>
            <label className="wiz-campo"><span>Nombre *</span>
              <input type="text" maxLength={200} value={tipo.nombre} placeholder="Ej. Coincidencia en lista restrictiva" onChange={(e) => setTipo({ ...tipo, nombre: e.target.value })} />
            </label>
          </div>
          <label className="wiz-campo"><span>Descripción (cuándo se usa este bloqueo)</span>
            <textarea rows={2} style={{ width: "100%" }} maxLength={500} value={tipo.descripcion} onChange={(e) => setTipo({ ...tipo, descripcion: e.target.value })} />
          </label>
          {errorTipo && <p className="mensaje-error">{errorTipo}</p>}
        </VentanaModal>
      )}
    </div>
  );
}
