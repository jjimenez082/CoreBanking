import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { fmtFecha, mensajeError } from "../api/mensajes";
import type { CatalogoItemResponse, DireccionResponse, GestionDetalleResponse, RolIdentificacion, RolRequisitos, UnidadItem } from "../api/tipos";
import { ETIQUETA_REQUISITO } from "../interlocutores/formCliente";

const textoDireccion = (d: DireccionResponse) =>
  [d.paisNombre, ...d.niveles.map((n) => n.texto).filter(Boolean), d.detalle].filter(Boolean).join(", ");

/**
 * Alta de una persona con un rol distinto de Cliente (Firmante, Beneficiario, Fiador, Codeudor, Autorizado). Mismo enfoque que Prospectos:
 * primero la identificación. Si ya es cliente se muestran sus datos (solo lectura) y se solicita únicamente el rol (gestión CLI_ROL);
 * si no existe, se continúa al asistente de alta, que exige los datos configurados para el rol (gestión CLI_ALTA). Ambos pasan por aprobación.
 */
export function TerceroRolPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const codigoRol = (params.get("rol") ?? "").toUpperCase();
  const { tienePermiso } = usePermisos();

  const [roles, setRoles] = useState<RolRequisitos[]>([]);
  const [tipos, setTipos] = useState<CatalogoItemResponse[]>([]);
  const [idTipo, setIdTipo] = useState(0);
  const [numero, setNumero] = useState("");
  const [val, setVal] = useState<RolIdentificacion | null>(null);
  const [validando, setValidando] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<GestionDetalleResponse | null>(null);
  const [unidades, setUnidades] = useState<UnidadItem[]>([]);
  const [idUnidadEmp, setIdUnidadEmp] = useState(0);
  const [numeroEmp, setNumeroEmp] = useState("");

  useEffect(() => {
    api.get<RolRequisitos[]>("/api/interlocutores/roles/requisitos").then((r) => setRoles(r.data))
      .catch((e) => setError(mensajeError(e, "No se pudieron cargar los roles.")));
    api.get<CatalogoItemResponse[]>("/api/catalogos/listas/TIPO_IDENTIFICACION_FISICA").then((r) => setTipos(r.data))
      .catch((e) => setError(mensajeError(e, "No se pudieron cargar los tipos de identificación.")));
    api.get<UnidadItem[]>("/api/catalogos/unidades", { params: { para: "registros" } }).then((r) => setUnidades(r.data)).catch(() => setUnidades([]));
  }, [api]);

  const rol = useMemo(() => roles.find((r) => r.codigo === codigoRol) ?? null, [roles, codigoRol]);
  const exige = rol ? rol.requeridos.map((c) => ETIQUETA_REQUISITO[c]).filter(Boolean).join(", ") : "";

  async function validar() {
    if (!rol) return;
    setValidando(true); setError(null); setVal(null);
    try {
      const r = await api.get<RolIdentificacion>("/api/interlocutores/roles/validar-identificacion", {
        params: { idTipo, numero: numero.trim(), idTipoRol: rol.idTipoRol },
      });
      setVal(r.data);
    } catch (e) { setError(mensajeError(e, "No se pudo validar la identificación.")); }
    finally { setValidando(false); }
  }

  async function solicitarRol() {
    if (!rol || !val?.idInterlocutor) return;
    setEnviando(true); setError(null);
    try {
      const r = await api.post<GestionDetalleResponse>(`/api/interlocutores/${val.idInterlocutor}/roles`, {
        agregar: [rol.idTipoRol], quitar: [], motivo: `Alta como ${rol.nombre} de una persona ya registrada como cliente`,
        empleado: rol.codigo === "EMPLEADO" ? { idUnidad: idUnidadEmp, numeroEmpleado: numeroEmp.trim() } : null,
      });
      setResultado(r.data);
    } catch (e) { setError(mensajeError(e, "No se pudo solicitar el rol.")); }
    finally { setEnviando(false); }
  }

  if (!tienePermiso("CLI_ALTA")) return <p className="mensaje-error">No tiene permiso para registrar personas.</p>;
  if (roles.length > 0 && !rol) return <p className="mensaje-error">El rol «{codigoRol}» no existe o está inactivo.</p>;

  if (resultado) {
    return (
      <div className="wiz">
        <div className="wiz__titulo"><div><h1>Rol solicitado</h1></div></div>
        <div className="wiz__aviso">
          Se creó la gestión <strong>{resultado.numero ?? `#${resultado.idGestion}`}</strong> para agregar el rol {rol?.nombre}. El rol no se asigna hasta que se apruebe
          {resultado.estado === "Aplicada" ? " (la regla de aprobación la aplicó de inmediato)." : "."}
        </div>
        <div style={{ display: "flex", gap: "0.5rem", marginTop: "1rem" }}>
          <button type="button" onClick={() => navigate("/gestiones/consulta")}>Ir a Gestiones</button>
          <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${val?.idInterlocutor}`)}>Ver el cliente</button>
        </div>
      </div>
    );
  }

  const c = val?.cliente ?? null;
  const nombreCli = c ? [c.nombre1, c.nombre2, c.apellido1, c.apellido2].filter(Boolean).join(" ") : "";
  const bloquea = val && !["NUEVO", "PUEDE_AGREGAR"].includes(val.estado);

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>Nuevo {rol?.nombre ?? "..."}</h1>
          <div className="nota">
            Primero la identificación: si la persona ya es cliente solo se le agrega el rol; si no, se piden sus datos.
            {rol && exige ? ` El rol exige: ${exige}.` : ""} Todo pasa por aprobación.
          </div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => navigate("/interlocutores")}>Cancelar</button>
      </div>

      <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
        <div className="wiz__cuerpo">
          <h2 style={{ marginTop: 0 }}>Identificación</h2>
          <div className="wiz__rejilla">
            <label className="wiz-campo">
              <span>Tipo de identificación<span className="wiz-campo__req" aria-hidden="true"> *</span></span>
              <select value={idTipo} onChange={(e) => { setIdTipo(Number(e.target.value)); setVal(null); }}>
                <option value={0}>Seleccione...</option>
                {tipos.map((t) => (<option key={t.id} value={t.id}>{t.nombre}</option>))}
              </select>
            </label>
            <label className="wiz-campo">
              <span>Número de identificación<span className="wiz-campo__req" aria-hidden="true"> *</span></span>
              <input type="text" value={numero} onChange={(e) => { setNumero(e.target.value); setVal(null); }}
                onKeyDown={(e) => { if (e.key === "Enter" && idTipo && numero.trim()) void validar(); }} />
            </label>
          </div>
          <div style={{ marginTop: "0.75rem" }}>
            <button type="button" disabled={!rol || !idTipo || !numero.trim() || validando} onClick={validar}>{validando ? "Buscando…" : "Buscar"}</button>
          </div>
        </div>
      </section>

      {error && <p className="mensaje-error">{error}</p>}

      {val && (
        <section className="wiz__tarjeta" style={{ marginBottom: "1rem" }}>
          <div className="wiz__cuerpo">
            <div className={bloquea ? "wiz__alerta" : "wiz__aviso"}>{val.mensaje}</div>

            {val.estado === "FUSIONADO" && val.idSobreviviente && (
              <p><Link to={`/interlocutores/${val.idSobreviviente}`}>Abrir el cliente que se conservó</Link></p>
            )}
            {val.estado === "FALTAN_DATOS" && val.idInterlocutor && tienePermiso("CLI_EDICION") && (
              <p><Link to={`/interlocutores/clientes/${val.idInterlocutor}/modificar`}>Modificar datos del cliente</Link></p>
            )}

            {c && (
              <div style={{ marginTop: "0.75rem" }}>
                <h2 style={{ marginTop: 0 }}>Cliente N.º {c.numero} — {nombreCli}</h2>
                <p className="nota">Solo lectura. {val.estado === "PUEDE_AGREGAR" ? "Al guardar se graba únicamente el rol." : ""}</p>
                <table className="tabla tabla--compacta">
                  <tbody>
                    <tr><th>Fecha de nacimiento</th><td>{c.mascaras?.textos.fechaNacimiento ?? fmtFecha(c.fechaNacimiento)}</td></tr>
                    <tr><th>Roles actuales</th><td>{c.roles.map((r) => r.nombre).join(", ") || "—"}</td></tr>
                    <tr><th>Identificaciones</th><td>{c.identificaciones.map((i) => `${i.tipoNombre} ${i.numero}`).join(" · ") || "—"}</td></tr>
                    <tr><th>Direcciones</th><td>{c.direcciones.map(textoDireccion).join(" | ") || "—"}</td></tr>
                    <tr><th>Contactos</th><td>{c.contactos.map((x) => `${x.tipoNombre}: ${x.valor}`).join(" · ") || "—"}</td></tr>
                  </tbody>
                </table>
              </div>
            )}

            <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem", alignItems: "center" }}>
              {val.estado === "PUEDE_AGREGAR" && rol?.codigo === "EMPLEADO" && (
                <div className="wiz__rejilla" style={{ width: "100%" }}>
                  <label className="wiz-campo">
                    <span>Unidad del empleado (sucursal o departamento)<span className="wiz-campo__req" aria-hidden="true"> *</span></span>
                    <select value={idUnidadEmp} onChange={(e) => setIdUnidadEmp(Number(e.target.value))}>
                      <option value={0}>Seleccione...</option>
                      {unidades.map((u) => (<option key={u.id} value={u.id}>{u.ruta}</option>))}
                    </select>
                  </label>
                  <label className="wiz-campo">
                    <span>Número de empleado<span className="wiz-campo__req" aria-hidden="true"> *</span></span>
                    <input type="text" maxLength={30} value={numeroEmp} onChange={(e) => setNumeroEmp(e.target.value)} />
                  </label>
                </div>
              )}
              {val.estado === "PUEDE_AGREGAR" && (
                <>
                  <button type="button" disabled={!val.puedeSolicitar || enviando || (rol?.codigo === "EMPLEADO" && (!idUnidadEmp || !numeroEmp.trim()))} onClick={solicitarRol}>
                    {enviando ? "Solicitando…" : `Solicitar rol ${rol?.nombre}`}
                  </button>
                  {!val.puedeSolicitar && <span className="nota">No tiene permiso para solicitar cambios de rol (CLI_ROL).</span>}
                </>
              )}
              {val.estado === "NUEVO" && rol && (
                <button type="button" onClick={() => navigate(`/interlocutores/terceros/registro?rol=${rol.codigo}&tipoId=${idTipo}&numId=${encodeURIComponent(numero.trim())}`)}>
                  Continuar con el registro
                </button>
              )}
            </div>
          </div>
        </section>
      )}
    </div>
  );
}
