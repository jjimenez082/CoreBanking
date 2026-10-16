import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import type {
  ApiErrorBody,
  CatalogoItemResponse,
  CoincidenciaFila,
  CoincidenciasResultado,
  DireccionResponse,
  FusionDireccionDecision,
  FusionVistaPreviaResponse,
  GestionDetalleResponse,
  InterlocutorBusquedaResponse,
  InterlocutorDetalleResponse,
} from "../api/tipos";

const fmtFecha = (iso?: string | null) => (iso ? iso.slice(0, 10).split("-").reverse().join("/") : "—");
const nombreDe = (f: InterlocutorDetalleResponse) => f.organizacion?.razonSocial ?? [f.nombre1, f.nombre2, f.apellido1, f.apellido2].filter(Boolean).join(" ");
const textoDireccion = (d: DireccionResponse) =>
  [d.paisNombre, ...d.niveles.map((n) => n.texto).filter(Boolean), d.detalle].filter(Boolean).join(", ");

interface Candidato { id: number; numero: string; nombre: string; detalle: string; porcentaje?: number }

/**
 * Fusion de clientes duplicados. Paso 1: elegir el otro cliente (sugeridos por parecido o busqueda). Paso 2: elegir cual se
 * conserva, revisar el antes/despues y decidir las direcciones. La fusion NO se aplica aqui: se solicita como gestion.
 */
export function FusionClientesPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const idBase = Number(id);
  const { tienePermiso } = usePermisos();

  const [base, setBase] = useState<InterlocutorDetalleResponse | null>(null);
  const [sugeridos, setSugeridos] = useState<CoincidenciaFila[]>([]);
  const [texto, setTexto] = useState("");
  const [encontrados, setEncontrados] = useState<InterlocutorBusquedaResponse[]>([]);
  const [otro, setOtro] = useState<Candidato | null>(null);
  const [conservar, setConservar] = useState<"base" | "otro">("base");
  const [vista, setVista] = useState<FusionVistaPreviaResponse | null>(null);
  const [cargandoVista, setCargandoVista] = useState(false);
  const [decisiones, setDecisiones] = useState<Record<number, FusionDireccionDecision>>({});
  const [tiposDireccion, setTiposDireccion] = useState<CatalogoItemResponse[]>([]);
  const [motivo, setMotivo] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<GestionDetalleResponse | null>(null);

  // Cliente de partida + sugeridos + catalogo de tipos de direccion
  useEffect(() => {
    let cancelado = false;
    api.get<InterlocutorDetalleResponse>(`/api/interlocutores/${idBase}`)
      .then((r) => !cancelado && setBase(r.data))
      .catch(() => !cancelado && setError("No se pudo cargar el cliente."));
    api.get<CoincidenciasResultado>(`/api/interlocutores/${idBase}/posibles-duplicados`)
      .then((r) => !cancelado && setSugeridos(r.data.items))
      .catch(() => undefined);
    api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_DIRECCION")
      .then((r) => !cancelado && setTiposDireccion(r.data))
      .catch(() => undefined);
    return () => { cancelado = true; };
  }, [api, idBase]);

  // Busqueda libre del otro cliente
  useEffect(() => {
    if (texto.trim().length < 2) { setEncontrados([]); return; }
    let cancelado = false;
    const t = setTimeout(async () => {
      try {
        const r = await api.get<InterlocutorBusquedaResponse[]>("/api/interlocutores/buscar", { params: { q: texto.trim() } });
        if (!cancelado) setEncontrados(r.data.filter((x) => x.idInterlocutor !== idBase && x.estado === "Activo"));
      } catch { if (!cancelado) setEncontrados([]); }
    }, 300);
    return () => { cancelado = true; clearTimeout(t); };
  }, [texto, api, idBase]);

  const idSobreviviente = otro ? (conservar === "base" ? idBase : otro.id) : null;
  const idDuplicado = otro ? (conservar === "base" ? otro.id : idBase) : null;

  // Vista previa cada vez que cambia la pareja o cual se conserva
  useEffect(() => {
    if (idSobreviviente == null || idDuplicado == null) { setVista(null); return; }
    let cancelado = false;
    setCargandoVista(true);
    setError(null);
    api.post<FusionVistaPreviaResponse>("/api/interlocutores/fusion/vista-previa", { idSobreviviente, idDuplicado })
      .then((r) => {
        if (cancelado) return;
        setVista(r.data);
        const d: Record<number, FusionDireccionDecision> = {};
        r.data.plan.direcciones.forEach((p) => {
          d[p.idDireccion] = { idDireccion: p.idDireccion, incorporar: p.incorporar, idTipoDireccion: p.idTipoDireccionSugerido };
        });
        setDecisiones(d);
      })
      .catch((err) => {
        if (cancelado) return;
        setVista(null);
        setError(isAxiosError<ApiErrorBody>(err) ? err.response?.data?.mensaje ?? "No se pudo calcular la vista previa." : "No se pudo calcular la vista previa.");
      })
      .finally(() => !cancelado && setCargandoVista(false));
    return () => { cancelado = true; };
  }, [api, idSobreviviente, idDuplicado]);

  const candidatos: Candidato[] = useMemo(() => {
    const lista = new Map<number, Candidato>();
    sugeridos.forEach((s) => lista.set(s.id, {
      id: s.id, numero: s.numero ?? "", nombre: s.nombre, porcentaje: s.porcentaje,
      detalle: [s.identificaciones, s.fuerte ? "coincidencia fuerte" : ""].filter(Boolean).join(" · "),
    }));
    encontrados.forEach((e) => { if (!lista.has(e.idInterlocutor)) lista.set(e.idInterlocutor, {
      id: e.idInterlocutor, numero: e.numero, nombre: e.nombre, detalle: e.identificaciones,
    }); });
    return [...lista.values()];
  }, [sugeridos, encontrados]);

  const puedeSolicitar = !!vista && vista.impedimentos.length === 0 && motivo.trim().length >= 10 && !enviando && tienePermiso("CLI_FUSION");

  async function solicitar() {
    if (!vista || idSobreviviente == null || idDuplicado == null) return;
    setEnviando(true);
    setError(null);
    try {
      const r = await api.post<GestionDetalleResponse>("/api/interlocutores/fusion", {
        idSobreviviente, idDuplicado, motivo: motivo.trim(), direcciones: Object.values(decisiones),
      });
      setResultado(r.data);
    } catch (err) {
      setError(isAxiosError<ApiErrorBody>(err) ? err.response?.data?.mensaje ?? "No se pudo solicitar la fusión." : "No se pudo solicitar la fusión.");
    } finally {
      setEnviando(false);
    }
  }

  if (!tienePermiso("CLI_FUSION")) return <p className="mensaje-error">No tiene permiso para solicitar fusiones de clientes.</p>;
  if (!base) return error ? <p className="mensaje-error">{error}</p> : <p className="nota">Cargando...</p>;

  if (resultado) {
    return (
      <div className="wiz">
        <div className="wiz__titulo"><div><h1>Fusión solicitada</h1></div></div>
        <div className="wiz__aviso">
          Se creó la gestión de fusión <strong>{resultado.numero ?? `#${resultado.idGestion}`}</strong>. Los datos no se mueven hasta que se apruebe
          {resultado.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
        </div>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idSobreviviente ?? idBase}`)}>Volver al cliente</button>
        </div>
      </div>
    );
  }

  const baseNombre = nombreDe(base);

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Fusionar clientes duplicados</h1>
          <div className="nota">Cliente de partida: N.º {base.numero} · {baseNombre}</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${idBase}`)}>Cancelar</button>
      </div>

      <div className="wiz__aviso">
        La fusión se solicita como gestión y requiere aprobación. El cliente que se absorbe <strong>no se elimina</strong>: queda como «Fusionado»
        y apunta al cliente que se conserva. Sus datos pasan al que se conserva, que mantiene sus propias marcas de «principal».
      </div>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <h2 style={{ marginTop: 0 }}>1. ¿Con qué cliente se fusiona?</h2>
          <input type="search" placeholder="Buscar por nombre, número de cliente o identificación…" value={texto}
            onChange={(e) => setTexto(e.target.value)} style={{ width: "100%", maxWidth: 480 }} />
          {candidatos.length === 0 ? (
            <p className="nota">{sugeridos.length === 0 ? "No hay clientes parecidos sugeridos; use la búsqueda." : "Sin resultados."}</p>
          ) : (
            <table className="tabla tabla--compacta" style={{ marginTop: "0.75rem" }}>
              <thead><tr><th>Parecido</th><th>Cliente</th><th>Nombre</th><th>Identificaciones</th><th /></tr></thead>
              <tbody>
                {candidatos.map((c) => (
                  <tr key={c.id} style={otro?.id === c.id ? { background: "#eef3ff" } : undefined}>
                    <td>{c.porcentaje != null ? `${c.porcentaje} %` : "—"}</td>
                    <td>{c.numero}</td>
                    <td>{c.nombre}</td>
                    <td>{c.detalle}</td>
                    <td><button type="button" className="boton-secundario" onClick={() => setOtro(c)}>{otro?.id === c.id ? "Elegido" : "Elegir"}</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>

      {otro && (
        <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
          <div className="wiz__cuerpo">
            <h2 style={{ marginTop: 0 }}>2. ¿Cuál cliente se conserva?</h2>
            <label style={{ display: "block", marginBottom: "0.4rem" }}>
              <input type="radio" name="conservar" checked={conservar === "base"} onChange={() => setConservar("base")} />{" "}
              Conservar <strong>N.º {base.numero} · {baseNombre}</strong> y absorber <strong>N.º {otro.numero} · {otro.nombre}</strong>
            </label>
            <label style={{ display: "block" }}>
              <input type="radio" name="conservar" checked={conservar === "otro"} onChange={() => setConservar("otro")} />{" "}
              Conservar <strong>N.º {otro.numero} · {otro.nombre}</strong> y absorber <strong>N.º {base.numero} · {baseNombre}</strong>
            </label>
          </div>
        </section>
      )}

      {cargandoVista && <p className="nota">Calculando la fusión…</p>}
      {error && <p className="mensaje-error">{error}</p>}

      {vista && (
        <>
          {vista.impedimentos.length > 0 && (
            <div className="wiz__alerta">
              <strong>No se puede fusionar:</strong>
              <ul style={{ margin: "0.3rem 0 0 1rem" }}>{vista.impedimentos.map((m, k) => <li key={k}>{m}</li>)}</ul>
            </div>
          )}
          {vista.advertencias.length > 0 && (
            <div className="wiz__aviso" style={{ background: "#fff6e5", borderColor: "#f2d49b", color: "#7a4a00" }}>
              <strong>Revise antes de continuar:</strong>
              <ul style={{ margin: "0.3rem 0 0 1rem" }}>{vista.advertencias.map((m, k) => <li key={k}>{m}</li>)}</ul>
            </div>
          )}

          <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
            <div className="wiz__cuerpo" style={{ overflowX: "auto" }}>
              <h2 style={{ marginTop: 0 }}>3. Antes de la fusión</h2>
              <table className="tabla tabla--compacta">
                <thead>
                  <tr><th /><th>Se conserva · N.º {vista.sobreviviente.numero}</th><th>Se absorbe · N.º {vista.duplicado.numero}</th></tr>
                </thead>
                <tbody>
                  <tr><td>Nombre</td><td>{nombreDe(vista.sobreviviente)}</td><td>{nombreDe(vista.duplicado)}</td></tr>
                  <tr><td>Nacimiento</td><td>{fmtFecha(vista.sobreviviente.fechaNacimiento)}</td><td>{fmtFecha(vista.duplicado.fechaNacimiento)}</td></tr>
                  <tr><td>Roles</td>
                    <td>{vista.sobreviviente.roles.map((r) => r.nombre).join(", ") || "—"}</td>
                    <td>{vista.duplicado.roles.map((r) => r.nombre).join(", ") || "—"}</td></tr>
                  <tr><td>Identificaciones</td>
                    <td>{vista.sobreviviente.identificaciones.map((i) => `${i.tipoNombre} ${i.numero}`).join(" · ") || "—"}</td>
                    <td>{vista.duplicado.identificaciones.map((i) => `${i.tipoNombre} ${i.numero}`).join(" · ") || "—"}</td></tr>
                  <tr><td>Contactos</td>
                    <td>{vista.sobreviviente.contactos.map((c) => c.valor).join(" · ") || "—"}</td>
                    <td>{vista.duplicado.contactos.map((c) => c.valor).join(" · ") || "—"}</td></tr>
                  <tr><td>Direcciones</td>
                    <td>{vista.sobreviviente.direcciones.map((d) => `${d.tipoNombre}: ${textoDireccion(d)}`).join(" | ") || "—"}</td>
                    <td>{vista.duplicado.direcciones.map((d) => `${d.tipoNombre}: ${textoDireccion(d)}`).join(" | ") || "—"}</td></tr>
                </tbody>
              </table>
            </div>
          </section>

          <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
            <div className="wiz__cuerpo" style={{ overflowX: "auto" }}>
              <h2 style={{ marginTop: 0 }}>4. Qué pasa con los datos del cliente absorbido</h2>

              <h3>Identificaciones</h3>
              <p className="nota">
                {vista.duplicado.identificaciones.length === 0 ? "No tiene." :
                  `Pasan al cliente que se conserva: ${vista.duplicado.identificaciones.map((i) => `${i.tipoNombre} ${i.numero}`).join(", ")}. La principal sigue siendo la del cliente que se conserva.`}
              </p>

              <h3>Direcciones</h3>
              {vista.plan.direcciones.length === 0 ? <p className="nota">No tiene direcciones activas.</p> : (
                <table className="tabla tabla--compacta">
                  <thead><tr><th>Incorporar</th><th>Dirección</th><th>Tipo actual</th><th>Tipo al incorporar</th><th /></tr></thead>
                  <tbody>
                    {vista.plan.direcciones.map((p) => {
                      const d = vista.duplicado.direcciones.find((x) => x.id === p.idDireccion);
                      const dec = decisiones[p.idDireccion];
                      if (!d || !dec) return null;
                      return (
                        <tr key={p.idDireccion}>
                          <td><input type="checkbox" checked={dec.incorporar}
                            onChange={(e) => setDecisiones({ ...decisiones, [p.idDireccion]: { ...dec, incorporar: e.target.checked } })} /></td>
                          <td>{textoDireccion(d)}</td>
                          <td>{d.tipoNombre}</td>
                          <td>
                            <select disabled={!dec.incorporar} value={dec.idTipoDireccion ?? ""}
                              onChange={(e) => setDecisiones({ ...decisiones, [p.idDireccion]: { ...dec, idTipoDireccion: Number(e.target.value) } })}>
                              {tiposDireccion.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
                            </select>
                          </td>
                          <td className="nota">
                            {p.conflicto ? "El cliente que se conserva ya tiene una dirección de ese tipo; se propone «Otra». " : ""}
                            {!p.incorporar && !dec.incorporar ? "Parece repetida: se descarta." : ""}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              )}

              <h3>Contactos</h3>
              {vista.plan.contactos.length === 0 ? <p className="nota">No tiene contactos activos.</p> : (
                <table className="tabla tabla--compacta">
                  <thead><tr><th>Contacto</th><th>Qué pasa</th></tr></thead>
                  <tbody>
                    {vista.plan.contactos.map((p) => {
                      const c = vista.duplicado.contactos.find((x) => x.id === p.idContacto);
                      return (
                        <tr key={p.idContacto}>
                          <td>{c ? `${c.tipoNombre}: ${c.valor}` : `#${p.idContacto}`}</td>
                          <td>{p.accion === "TRASLADAR" ? `Se traslada${p.seraPrincipal ? " como principal (el cliente que se conserva no tenía)" : ""}` : "Ya lo tiene el cliente que se conserva; no se duplica"}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              )}

              <h3>Roles</h3>
              {vista.plan.roles.length === 0 ? <p className="nota">No tiene roles activos.</p> : (
                <ul className="lista-checkbox">
                  {vista.plan.roles.map((r) => (
                    <li key={r.idTipoRol}>{r.nombre}: {r.accion === "TRASLADAR" ? "se traslada" : r.accion === "REACTIVAR" ? "se reactiva en el cliente que se conserva" : "ya lo tiene el cliente que se conserva"}</li>
                  ))}
                </ul>
              )}

              {vista.plan.otros.length > 0 && (
                <>
                  <h3>Otros datos que pasan al cliente que se conserva</h3>
                  <ul className="lista-checkbox">{vista.plan.otros.map((o) => <li key={o.concepto}>{o.concepto}: {o.cantidad}</li>)}</ul>
                </>
              )}
              <p className="nota">Cuando existan Cuentas y Créditos, también se trasladarán las titularidades y productos del cliente absorbido.</p>
            </div>
          </section>

          <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
            <div className="wiz__cuerpo">
              <h2 style={{ marginTop: 0 }}>5. Motivo y solicitud</h2>
              <textarea rows={3} style={{ width: "100%", maxWidth: 640 }} maxLength={500} value={motivo}
                placeholder="Explique por qué se trata de la misma persona (mínimo 10 caracteres)"
                onChange={(e) => setMotivo(e.target.value)} />
              <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem" }}>
                <button type="button" disabled={!puedeSolicitar} onClick={solicitar}>{enviando ? "Solicitando…" : "Solicitar fusión"}</button>
                <Link to={`/interlocutores/${idBase}`} className="boton-secundario" style={{ padding: "0.45rem 0.9rem" }}>Cancelar</Link>
              </div>
            </div>
          </section>
        </>
      )}
    </div>
  );
}
