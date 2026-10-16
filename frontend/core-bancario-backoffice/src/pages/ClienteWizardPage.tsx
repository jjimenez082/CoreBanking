import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { RevisionDuplicados } from "../components/RevisionDuplicados";
import { Campo, SelectCat } from "../components/CamposWizard";
import { PerfilFinancieroPaso } from "../interlocutores/PerfilFinancieroPaso";
import { AdjuntosPanel } from "../documentos/AdjuntosPanel";
import { encabezadoAdjuntos } from "../documentos/documentos";
import { useCatalogosCliente, useConfigsDireccion } from "../interlocutores/useCatalogosCliente";
import { DireccionCampos } from "../interlocutores/DireccionCampos";
import {
  aContenidoBorrador,
  aRequestAlta,
  aRequestModificacion,
  calcularCambios,
  codigoTipoContacto,
  contVacio,
  desdeContenidoBorrador,
  desdeDetalle,
  aplicarGestion,
  dirVacia,
  filaDirDesdeDto,
  ejemploContacto,
  formVacio,
  tiposIdentificacionDe,
  nomVacio,
  nacVacia,
  actVacia,
  identVacia,
  primerPasoInvalido,
  textoDireccion,
  puedeGuardarBorrador,
  validarContacto,
  validarPaso,
  type FilaCont,
  type FilaDir,
  type FilaIdent,
  type FormCliente,
  type CatalogosCliente,
  reglasDeRoles,
  ETIQUETA_REQUISITO,
} from "../interlocutores/formCliente";
import type {
  ApiErrorBody,
  BorradorDetalleResponse,
  CatalogoItemResponse,
  GestionDetalleResponse,
  InterlocutorDetalleResponse,
  InterlocutorAltaGestionResponse,
  ProspectoFila,
  CoincidenciasResultado,
  RevisionDuplicados as RevisionDup,
  RolRequisitos,
} from "../api/tipos";

/**
 * Wizard de cliente (Persona Fisica), para alta y para edicion:
 *   1 Datos generales · 2 Identificaciones (1:n) · 3 Direcciones (1:n)
 *   4 Contacto (1:n)   · 5 Perfil financiero · 6 Revision
 *
 * Alta: crea el cliente al finalizar (el N.º se asigna en ese momento) y
 * permite guardar un borrador privado que vence.
 * Edicion: NO cambia el dato; envia una gestion de modificacion que otro
 * usuario aprueba (principio "todo cambio por gestion").
 */

const PASOS = [
  { n: 1, etiqueta: "Datos generales", sub: "Obligatorio" },
  { n: 2, etiqueta: "Identificaciones", sub: "Al menos una" },
  { n: 3, etiqueta: "Direcciones", sub: "Opcional" },
  { n: 4, etiqueta: "Contacto", sub: "Opcional" },
  { n: 5, etiqueta: "Perfil financiero", sub: "Opcional" },
  { n: 6, etiqueta: "Revisión", sub: "" },
] as const;

const fmtFecha = (iso?: string | null) => {
  if (!iso) return "—";
  const [a, m, d] = iso.slice(0, 10).split("-");
  return `${d}/${m}/${a}`;
};

function mensajesDeError(err: unknown, porDefecto: string): string[] {
  if (isAxiosError<ApiErrorBody>(err)) {
    const m = err.response?.data?.mensaje;
    if (m) return m.split("\n");
    return [`Error ${err.response?.status ?? ""}: ${err.message}`];
  }
  return [porDefecto];
}

// ----- Piezas de formulario --------------------------------------------------------------

function TarjetaFila({ titulo, principal, onPrincipal, onQuitar, auditoria, children }: {
  titulo: string; principal: boolean; onPrincipal: () => void; onQuitar: () => void; auditoria?: string; children: ReactNode;
}) {
  return (
    <div className="wiz-fila">
      <div className="wiz-fila__cabecera">
        <strong>{titulo}</strong>
        <div className="wiz-fila__acciones">
          <label className="wiz-fila__principal">
            <input type="checkbox" checked={principal} onChange={onPrincipal} /> Principal
          </label>
          <button type="button" className="wiz-enlace wiz-enlace--peligro" onClick={onQuitar}>Quitar</button>
        </div>
      </div>
      {children}
      <div className="wiz-fila__auditoria">{auditoria ?? "Se registrará el usuario y la fecha de creación al guardar."}</div>
    </div>
  );
}

// ----- Pagina -----------------------------------------------------------------------------

export function ClienteWizardPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { id } = useParams();
  const [params] = useSearchParams();
  const { tienePermiso, cargando: cargandoPermisos } = usePermisos();
  const { catalogos: cat, cargando: cargandoCat, error: errorCat } = useCatalogosCliente();
  const { configs, asegurar: asegurarConfig } = useConfigsDireccion();
  // Niveles de lista obligatorios que no tienen opciones bajo el padre elegido (no se exigen).
  const sinOpciones = useRef(new Set<string>()).current;
  const marcarSinOpciones = (clave: string, nivel: number, sin: boolean) => {
    const k = `${clave}.${nivel}`;
    if (sin) sinOpciones.add(k); else sinOpciones.delete(k);
  };

  const esEditar = id !== undefined;
  const idCliente = esEditar ? Number(id) : null;
  const idBorradorUrl = params.get("borrador");
  // ?prospecto=N : promover un prospecto a cliente (se precargan sus datos; al aprobarse queda Convertido).
  const idProspectoUrl = params.get("prospecto") && !params.get("gestion") ? Number(params.get("prospecto")) : null;
  const [idProspecto, setIdProspecto] = useState<number | null>(idProspectoUrl);
  // ?gestion=N : corregir una gestion devuelta (se parte de lo que ya se habia pedido).
  const idGestionCorregir = esEditar && params.get("gestion") ? Number(params.get("gestion")) : null;
  // ?gestion=N en un alta: corregir un alta devuelta (se parte de lo que ya se habia pedido).
  const idGestionAlta = !esEditar && params.get("gestion") ? Number(params.get("gestion")) : null;
  const [gestionDevuelta, setGestionDevuelta] = useState<GestionDetalleResponse | null>(null);

  const [form, setForm] = useState<FormCliente>(() => formVacio(params.get("tipo") === "juridica" ? "Juridica" : "Fisica"));
  // ?rol=<CODIGO> : alta de una persona que solo asumira ese rol (Firmante, Fiador...). Los datos que exige salen de Administracion > Requisitos por rol.
  const codigoRolUrl = !esEditar ? params.get("rol") : null;
  const [roles, setRoles] = useState<RolRequisitos[]>([]);
  const [original, setOriginal] = useState<FormCliente | null>(null);
  const [ficha, setFicha] = useState<InterlocutorDetalleResponse | null>(null);
  const [paso, setPaso] = useState(1);
  const [adjuntos, setAdjuntos] = useState<number[]>([]);   // ids de documentos temporales que viajan con la gestion
  const [maxPaso, setMaxPaso] = useState(1);
  const [mostrarErrores, setMostrarErrores] = useState(false);
  const [cargandoInicial, setCargandoInicial] = useState(esEditar || !!idBorradorUrl || !!idGestionAlta || !!idProspectoUrl);
  const [errorInicial, setErrorInicial] = useState<string | null>(null);

  const [idBorrador, setIdBorrador] = useState<number | null>(idBorradorUrl ? Number(idBorradorUrl) : null);
  const [infoBorrador, setInfoBorrador] = useState<string | null>(null);
  const [comentario, setComentario] = useState("");
  const [confirmandoCancelar, setConfirmandoCancelar] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [duplicados, setDuplicados] = useState<CoincidenciasResultado | null>(null);
  const [erroresApi, setErroresApi] = useState<string[]>([]);
  const [resultado, setResultado] = useState<{ titulo: string; texto: string; codigo: string; idVer: number | null } | null>(null);

  const permisoNecesario = esEditar ? "CLI_EDICION" : "CLI_ALTA";

  // Carga inicial: ficha vigente (editar) o borrador (?borrador=).
  useEffect(() => {
    let cancelado = false;
    async function cargar() {
      try {
        if (esEditar) {
          const r = await api.get<InterlocutorDetalleResponse>(`/api/interlocutores/${idCliente}/detalle`);
          if (cancelado) return;
          const f = desdeDetalle(r.data);
          setFicha(r.data);
          setOriginal(f);
          if (idGestionCorregir) {
            const g = await api.get<GestionDetalleResponse>(`/api/gestiones/${idGestionCorregir}`);
            if (cancelado) return;
            if (g.data.estado !== "Devuelta" || !g.data.esMia || g.data.llaveCruda !== String(idCliente))
              throw new Error("La gestión indicada ya no está devuelta para su corrección.");
            setGestionDevuelta(g.data);
            setForm(aplicarGestion(f, g.data.campos));
            setComentario(g.data.comentario ?? "");
          } else {
            setForm(f);
          }
          setMaxPaso(6);
        } else if (idGestionAlta) {
          const g = await api.get<GestionDetalleResponse>(`/api/gestiones/${idGestionAlta}`);
          if (cancelado) return;
          if (g.data.estado !== "Devuelta" || !g.data.esMia || g.data.entidadNegocio !== "INTERLOCUTOR_ALTA")
            throw new Error("La gestión indicada ya no está devuelta para su corrección.");
          setGestionDevuelta(g.data);
          setForm(aplicarGestion(formVacio("Fisica"), g.data.campos));
          const vinculo = g.data.campos.find((c) => c.nombreCampo === "id_prospecto")?.nuevoCrudo;
          if (vinculo) setIdProspecto(Number(vinculo));
          setMaxPaso(6);
        } else if (idBorradorUrl) {
          const r = await api.get<BorradorDetalleResponse>(`/api/interlocutores/borradores/${idBorradorUrl}`);
          if (cancelado) return;
          setForm(desdeContenidoBorrador(r.data.contenido));
          setPaso(r.data.paso);
          setMaxPaso(r.data.paso);
          setInfoBorrador(`Borrador recuperado · vence el ${new Date(r.data.venceEn).toLocaleDateString()}`);
        }
      } catch (err) {
        if (!cancelado) setErrorInicial((err instanceof Error && !isAxiosError(err) ? err.message : mensajesDeError(err, "No se pudo cargar la información.").join(" ")));
      } finally {
        if (!cancelado) setCargandoInicial(false);
      }
    }
    if (esEditar || idBorradorUrl || idGestionAlta) cargar();
    return () => {
      cancelado = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, idBorradorUrl, idGestionAlta, api]);

  // Promocion de un prospecto: se precargan sus datos (sucursal, nombre, identificacion, direccion, telefono y correo).
  useEffect(() => {
    if (!idProspectoUrl || cargandoCat || esEditar) return;
    let cancelado = false;
    (async () => {
      try {
        const r = await api.get<ProspectoFila>(`/api/prospectos/${idProspectoUrl}`);
        if (cancelado) return;
        const p = r.data;
        if (p.estado !== "Prospecto") throw new Error(`El prospecto está en estado ${p.estado}; solo un prospecto vigente puede promoverse.`);
        if (p.idGestionPendiente) throw new Error(`El prospecto ya tiene una promoción pendiente de aprobación (gestión ${p.numeroGestionPendiente}).`);
        const tipoCont = (codigo: string) => cat.tiposContacto.find((t) => t.codigo === codigo)?.id ?? 0;
        setForm((f) => ({
          ...f,
          idUnidad: p.idUnidad ?? f.idUnidad,
          nombre1: p.nombre1 ?? p.nombre, nombre2: p.nombre2 ?? "", apellido1: p.apellido1 ?? "", apellido2: p.apellido2 ?? "",
          fechaNacimiento: (p.fechaNacimiento ?? "").slice(0, 10),
          identificaciones: p.idTipoIdentificacion && p.numeroIdentificacion
            ? [{ ...identVacia(true), idTipoIdentificacion: p.idTipoIdentificacion, numero: p.numeroIdentificacion }] : [],
          direcciones: p.direccion ? [filaDirDesdeDto(p.direccion)] : [],
          contactos: [
            ...(p.telefono ? [{ ...contVacio(tipoCont("TELEFONO_MOVIL"), true), valor: p.telefono }] : []),
            ...(p.correo ? [{ ...contVacio(tipoCont("CORREO"), true), valor: p.correo }] : []),
          ],
        }));
        setIdProspecto(p.idProspecto);
        setMaxPaso(6);
        setInfoBorrador(`Promoción del prospecto ${p.nombre}: complete los datos que faltan (país regulador, institución emisora, etc.)`);
      } catch (err) {
        if (!cancelado) setErrorInicial(err instanceof Error && !isAxiosError(err) ? err.message : mensajesDeError(err, "No se pudo cargar el prospecto.").join(" "));
      } finally {
        if (!cancelado) setCargandoInicial(false);
      }
    })();
    return () => { cancelado = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idProspectoUrl, cargandoCat]);

  // Unidad predeterminada (la del usuario que da el alta) en altas nuevas.
  useEffect(() => {
    if (esEditar || form.idUnidad) return;
    const pre = cat.unidades.find((u) => u.esPredeterminado);
    if (pre) setForm((f) => (f.idUnidad ? f : { ...f, idUnidad: pre.id }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cat.unidades]);

  // Pais regulador predeterminado (el de la compania) en altas nuevas.
  useEffect(() => {
    if (esEditar || form.idPaisRegulador) return;
    const pre = cat.paises.find((p) => p.esPredeterminado);
    if (pre) setForm((f) => ({ ...f, idPaisRegulador: pre.id }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cat.paises]);

  // Pais por defecto de una direccion nueva: el que coincide con el pais regulador (por codigo) o el primero de la lista.
  const paisPorDefecto = useMemo(() => {
    const cod = cat.paises.find((p) => p.id === form.idPaisRegulador)?.codigo?.toUpperCase();
    const lista = cat.paisesDireccion;
    return (cod ? lista.find((p) => p.codigo?.toUpperCase() === cod || (cod.length === 3 && p.codigo?.toUpperCase() === cod.slice(0, 2)))?.id : undefined)
      ?? lista[0]?.id ?? 0;
  }, [cat.paises, cat.paisesDireccion, form.idPaisRegulador]);

  // Configuracion de cada pais usado en las direcciones.
  useEffect(() => {
    form.direcciones.forEach((d) => asegurarConfig(d.idPais));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.direcciones.map((d) => d.idPais).join(",")]);

  useEffect(() => {
    let vivo = true;
    (async () => {
      try {
        const r = await api.get<RolRequisitos[]>(`/api/interlocutores/roles/requisitos?tipo=${form.tipoPersona === "Juridica" ? "Juridica" : "Fisica"}`);
        if (vivo) setRoles(r.data);
      } catch { /* sin roles: el alta sigue como Cliente */ }
    })();
    return () => { vivo = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [esEditar, form.tipoPersona]);

  // ?tipoId=&numId= : identificacion ya validada en la pantalla previa del alta por rol (se precarga como principal).
  const tipoIdUrl = !esEditar ? Number(params.get("tipoId") ?? 0) : 0;
  const numIdUrl = !esEditar ? params.get("numId") ?? "" : "";
  useEffect(() => {
    if (!tipoIdUrl || !numIdUrl || cargandoInicial) return;
    setForm((f) => (f.identificaciones.length > 0 ? f : { ...f, identificaciones: [{ ...identVacia(true), idTipoIdentificacion: tipoIdUrl, numero: numIdUrl }] }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tipoIdUrl, numIdUrl, cargandoInicial]);

  // Rol de la URL -> formulario (si el borrador o la gestion devuelta ya trae su rol, manda ese).
  useEffect(() => {
    if (!codigoRolUrl || cargandoInicial || roles.length === 0) return;
    const r = roles.find((x) => x.codigo === codigoRolUrl.toUpperCase());
    if (r && r.codigo !== "CLIENTE") setForm((f) => (f.rolId ? f : { ...f, rolId: r.idTipoRol }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [codigoRolUrl, cargandoInicial, roles]);

  const rolActual = !esEditar && form.rolId ? roles.find((x) => x.idTipoRol === form.rolId) ?? null : null;
  const requisitos = rolActual && rolActual.codigo !== "CLIENTE" ? rolActual : null;

  // Rol Empleado: el alta pide su unidad (sucursal o departamento) y su número de empleado.
  useEffect(() => {
    if (esEditar) return;
    const esEmpleado = rolActual?.codigo === "EMPLEADO";
    setForm((f) => (esEmpleado ? (f.empleado ? f : { ...f, empleado: { idUnidad: 0, numero: "" } }) : (f.empleado ? { ...f, empleado: undefined } : f)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rolActual?.codigo, esEditar]);

  // Reglas: alta = rol elegido (o Cliente); modificar = unión de los roles vigentes de la persona.
  const reglas = useMemo(() => {
    if (roles.length === 0) return null;
    if (esEditar) {
      const ids = (ficha?.roles ?? []).filter((r) => r.estado !== "Inactivo" && r.estado !== "INACTIVO").map((r) => r.idTipoRol);
      return reglasDeRoles(roles, ids, form.tipoPersona);
    }
    const id = form.rolId || roles.find((r) => r.codigo === "CLIENTE")?.idTipoRol || 0;
    return reglasDeRoles(roles, [id], form.tipoPersona);
  }, [roles, esEditar, ficha, form.rolId, form.tipoPersona]);
  const pide = (c: string) => (reglas ? reglas.has(c) : false);
  const exige = requisitos
    ? requisitos.requeridos.map((c) => ETIQUETA_REQUISITO[c]).filter(Boolean).join(", ")
    : "";

  const errores = useMemo(
    // eslint-disable-next-line react-hooks/exhaustive-deps
    () => (mostrarErrores ? validarPaso(paso, form, cat, configs, sinOpciones, reglas) : {}),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [mostrarErrores, paso, form, cat, configs, reglas],
  );
  const cambios = useMemo(() => (original ? calcularCambios(original, form, cat, configs) : []), [original, form, cat, configs]);

  // ----- Actualizaciones del formulario -----
  const set = <K extends keyof FormCliente>(k: K, v: FormCliente[K]) => setForm((f) => ({ ...f, [k]: v }));

  const actIdent = (clave: string, parche: Partial<FilaIdent>) =>
    setForm((f) => ({ ...f, identificaciones: f.identificaciones.map((x) => (x.clave === clave ? { ...x, ...parche } : x)) }));
  const actDir = (clave: string, parche: Partial<FilaDir>) =>
    setForm((f) => ({ ...f, direcciones: f.direcciones.map((x) => (x.clave === clave ? { ...x, ...parche } : x)) }));
  const actCont = (clave: string, parche: Partial<FilaCont>) =>
    setForm((f) => ({ ...f, contactos: f.contactos.map((x) => (x.clave === clave ? { ...x, ...parche } : x)) }));

  function quitarIdent(clave: string) {
    setForm((f) => {
      const resto = f.identificaciones.filter((x) => x.clave !== clave);
      const habiaPrincipal = f.identificaciones.find((x) => x.clave === clave)?.esPrincipal;
      if (habiaPrincipal && resto.length > 0 && !resto.some((x) => x.esPrincipal)) resto[0] = { ...resto[0], esPrincipal: true };
      return { ...f, identificaciones: resto };
    });
  }

  function marcarContactoPrincipal(c: FilaCont) {
    setForm((f) => ({
      ...f,
      contactos: f.contactos.map((x) =>
        x.clave === c.clave ? { ...x, esPrincipal: !x.esPrincipal } : x.idTipoContacto === c.idTipoContacto ? { ...x, esPrincipal: false } : x),
    }));
  }

  // ----- Navegacion -----
  function irA(n: number) {
    setPaso(n);
    setMostrarErrores(false);
    setConfirmandoCancelar(false);
    setErroresApi([]);
  }

  function siguiente() {
    const e = validarPaso(paso, form, cat, configs, sinOpciones, reglas);
    if (Object.keys(e).length > 0) {
      setMostrarErrores(true);
      return;
    }
    const n = paso + 1;
    setPaso(n);
    setMaxPaso((m) => Math.max(m, n));
    setMostrarErrores(false);
    setErroresApi([]);
  }

  async function guardarBorrador(): Promise<boolean> {
    setErroresApi([]);
    try {
      const r = await api.put<{ idBorrador: number; venceEn?: string }>("/api/interlocutores/borradores", {
        idBorrador, paso, contenido: aContenidoBorrador(form),
      });
      setIdBorrador(r.data.idBorrador);
      const hora = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
      setInfoBorrador(`Borrador guardado a las ${hora}${r.data.venceEn ? ` · vence el ${new Date(r.data.venceEn).toLocaleDateString()}` : ""}`);
      return true;
    } catch (err) {
      setErroresApi(mensajesDeError(err, "No se pudo guardar el borrador."));
      return false;
    }
  }

  async function descartar() {
    if (!esEditar && idBorrador === null) {
      navigate("/interlocutores");
      return;
    }
    navigate(esEditar ? `/interlocutores/${idCliente}` : "/interlocutores/borradores");
  }

  function finalizar() { void enviar(null); }

  async function enviar(revision: RevisionDup | null) {
    const invalido = primerPasoInvalido(form, cat, configs, sinOpciones, reglas);
    if (invalido !== null) {
      setPaso(invalido);
      setMostrarErrores(true);
      return;
    }
    setEnviando(true);
    setErroresApi([]);
    try {
      if (esEditar) {
        const r = await api.post<GestionDetalleResponse>(
          `/api/interlocutores/${idCliente}/modificacion`, aRequestModificacion(form, comentario, gestionDevuelta?.idGestion ?? null), encabezadoAdjuntos(adjuntos));
        setResultado({
          titulo: "Cambio enviado a aprobación",
          texto: "Se creó la gestión de modificación. Los datos se actualizarán cuando otro usuario con permiso la apruebe.",
          codigo: `Gestión ${r.data.numero ?? r.data.idGestion}`, idVer: idCliente,
        });
      } else {
        const r = await api.post<InterlocutorAltaGestionResponse>("/api/interlocutores", aRequestAlta(form, idBorrador, gestionDevuelta?.idGestion ?? null, idProspecto, revision), encabezadoAdjuntos(adjuntos));
        const gestionTxt = `Gestión ${r.data.numeroGestion ?? r.data.idGestion}`;
        if (r.data.aplicada && r.data.interlocutor) {
          setResultado({
            titulo: "Cliente creado",
            texto: `El cliente quedó registrado (${gestionTxt}, aprobación automática). Su número fue asignado al crearse y el borrador (si había) se eliminó.`,
            codigo: `N.º de cliente ${r.data.interlocutor.numero}`, idVer: r.data.interlocutor.idInterlocutor,
          });
        } else {
          setResultado({
            titulo: "Alta enviada a aprobación",
            texto: idProspecto
              ? "Se creó la gestión de alta (promoción del prospecto). El cliente se creará y el prospecto quedará convertido cuando se apruebe el último nivel."
              : "Se creó la gestión de alta. El cliente y su número se crearán cuando se apruebe el último nivel.",
            codigo: gestionTxt, idVer: null,
          });
        }
      }
    } catch (err) {
      if (isAxiosError(err) && err.response?.status === 409 && err.response.data?.error === "posibles_duplicados") {
        setDuplicados(err.response.data.datos as CoincidenciasResultado);
      } else {
        setErroresApi(mensajesDeError(err, "Error inesperado al guardar."));
      }
    } finally {
      setEnviando(false);
    }
  }

  function reiniciar() {
    const tipoNuevo = form.tipoPersona;
    setForm(formVacio(tipoNuevo));
    setPaso(1);
    setMaxPaso(1);
    setIdBorrador(null);
    setIdProspecto(null);
    setInfoBorrador(null);
    setResultado(null);
    setMostrarErrores(false);
    setComentario("");
    const pre = cat.paises.find((p) => p.esPredeterminado);
    const preUnidad = cat.unidades.find((u) => u.esPredeterminado);
    if (pre) setForm((f) => ({ ...f, idPaisRegulador: pre.id, idUnidad: preUnidad?.id ?? 0 }));
    navigate(tipoNuevo === "Juridica" ? "/interlocutores/clientes/nuevo?tipo=juridica" : "/interlocutores/clientes/nuevo", { replace: true });
  }

  // ----- Guardas de pantalla -----
  if (cargandoPermisos || cargandoCat || cargandoInicial) return <p className="nota">Cargando...</p>;
  if (!tienePermiso(permisoNecesario))
    return <p className="mensaje-error">No tiene permiso para {esEditar ? "modificar" : "crear"} clientes.</p>;
  if (errorCat) return <p className="mensaje-error">{errorCat}</p>;
  if (errorInicial) return <p className="mensaje-error">{errorInicial}</p>;

  if (esEditar && ficha && (ficha.estado !== "Activo" || ficha.idGestionPendiente)) {
    return (
      <div>
        <h1>Modificar cliente</h1>
        <p className="mensaje-error">
          {ficha.estado !== "Activo"
            ? `El cliente está '${ficha.estado}' y no se puede modificar.`
            : `El cliente ya tiene la gestión ${ficha.numeroGestionPendiente ?? ficha.idGestionPendiente} pendiente de aprobación; debe resolverse antes de solicitar otro cambio.`}
        </p>
        <Link to={`/interlocutores/${ficha.idInterlocutor}`}>← Volver a la vista 360°</Link>
      </div>
    );
  }

  if (duplicados) {
    return (
      <RevisionDuplicados
        resultado={duplicados} exigirMotivo
        contexto={idProspecto ? "Antes de promover el prospecto, revise si la persona ya está registrada." : "Antes de crear el cliente, revise si la persona ya está registrada."}
        onGuardarBorrador={!esEditar && !idProspecto && puedeGuardarBorrador(form) ? guardarBorrador : undefined}
        onVolver={() => setDuplicados(null)}
        onMisma={(c) => navigate(c.origen === "Prospecto" ? "/interlocutores/prospectos" : `/interlocutores/${c.id}`)}
        onContinuar={(motivo) => { setDuplicados(null); void enviar({ confirmado: true, motivo }); }}
      />
    );
  }

  if (resultado) {
    return (
      <section className="wiz-exito">
        <div className="wiz-exito__icono" aria-hidden="true">✓</div>
        <h1>{resultado.titulo}</h1>
        <p>{resultado.texto}</p>
        <div className="wiz-exito__codigo">{resultado.codigo}</div>
        <div className="wiz-exito__acciones">
          {resultado.idVer !== null && (
            <button type="button" className="boton-secundario" onClick={() => navigate(`/interlocutores/${resultado.idVer}`)}>
              Ver vista 360°
            </button>
          )}
          {!esEditar && <button type="button" onClick={reiniciar}>Crear otro cliente</button>}
          {(esEditar || resultado.idVer === null) && <button type="button" onClick={() => navigate("/gestiones")}>Ir a Gestiones</button>}
        </div>
      </section>
    );
  }

  const nombreCompleto = [form.nombre1, form.nombre2, form.apellido1, form.apellido2].filter((x) => x.trim()).join(" ");
  const nombrePais = cat.paises.find((p) => p.id === form.idPaisRegulador)?.nombre ?? "—";
  const nombreDe = (lista: CatalogoItemResponse[], idItem?: number | null) => lista.find((x) => x.id === idItem)?.nombre ?? "—";
  const ultimo = paso === 6;
  const esOrg = form.tipoPersona === "Juridica";
  const tiposId = tiposIdentificacionDe(cat, form.tipoPersona);
  const nombreMostrar = esOrg ? form.razonSocial : nombreCompleto;

  return (
    <div className="wiz">
      <div className="wiz__titulo">
        <div>
          <h1>{esEditar ? `Modificar cliente N.º ${ficha?.numero} — ${nombreMostrar}` : requisitos ? `Nuevo ${rolActual!.nombre}` : esOrg ? "Nueva organización" : "Nueva persona"}</h1>
          <div className="nota">
            {esEditar ? (esOrg ? "Organización" : "Persona Física") : `${esOrg ? "Organización" : "Persona Física"} · complete los pasos; los marcados con * son obligatorios`}
            {requisitos && ` · Rol ${rolActual!.nombre}${exige ? `: exige ${exige}` : ""}. Los campos marcados con * son obligatorios para este rol.`}
          </div>
        </div>
        {infoBorrador && <span className="wiz__chip">{infoBorrador}</span>}
      </div>

      {esEditar && (form.enmascarados?.length ?? 0) > 0 && (
        <div className="wiz__aviso">
          Según su perfil de datos, esta ficha muestra enmascarado: <strong>{form.enmascarados!.join(", ")}</strong>. Si no modifica esos datos se conservan
          sin cambios; si escribe un valor nuevo, ese valor reemplaza al anterior.
        </div>
      )}

      {gestionDevuelta && (
        <div className="wiz__aviso wiz__aviso--devuelta">
          <strong>Gestión {gestionDevuelta.numero ?? gestionDevuelta.idGestion} devuelta por {gestionDevuelta.devueltoPor}:</strong>{" "}
          {gestionDevuelta.comentarioDevolucion}
          <div className="nota">Ajuste lo que se pide y envíe de nuevo: se creará una nueva gestión (corrección n.º {gestionDevuelta.numeroCorreccion + 1}) y esta quedará como corregida.</div>
        </div>
      )}
      {esEditar && (
        <div className="wiz__aviso">
          Todo cambio a un cliente se envía como <strong>gestión de aprobación</strong>. El dato no cambia hasta que otro usuario la apruebe.
        </div>
      )}

      <ol className="wiz__pasos">
        {PASOS.map((p) => {
          const actual = p.n === paso;
          const visitado = p.n <= maxPaso;
          const hecho = visitado && p.n < paso;
          return (
            <li key={p.n} className="wiz__paso-item">
              <button
                type="button"
                className={`wiz__paso ${actual ? "wiz__paso--actual" : ""} ${hecho ? "wiz__paso--hecho" : ""}`}
                disabled={!visitado}
                aria-current={actual ? "step" : undefined}
                onClick={() => irA(p.n)}
              >
                <span className="wiz__paso-num">{hecho ? "✓" : p.n}</span>
                <span className="wiz__paso-texto">
                  <span className="wiz__paso-etiqueta">{p.n === 6 && esEditar ? "Revisión y envío" : p.etiqueta}</span>
                  <span className="wiz__paso-sub">{p.n === 6 ? (esEditar ? "Enviar a aprobación" : "Confirmar y enviar") : p.sub}</span>
                </span>
              </button>
              <span className={`wiz__paso-linea ${p.n < paso ? "wiz__paso-linea--hecha" : ""}`} />
            </li>
          );
        })}
      </ol>

      <section className="wiz__tarjeta">
        <div className="wiz__cuerpo">
          {mostrarErrores && Object.keys(errores).length > 0 && (
            <div role="alert" className="wiz__alerta">Revise los campos marcados antes de continuar.{errores.general ? ` ${errores.general}` : ""}</div>
          )}
          {erroresApi.length > 0 && (
            <div role="alert" className="wiz__alerta">
              {erroresApi.map((m, i) => (<div key={i}>{m}</div>))}
            </div>
          )}

          {/* ---- Paso 1 ---- */}
          {paso === 1 && esOrg && (
            <div className="wiz__grupo">
              <h2>Datos de la organización</h2>
              <div className="wiz__rejilla">
                <Campo etiqueta="País regulador" obligatorio error={errores.idPaisRegulador}>
                  <select value={form.idPaisRegulador} onChange={(e) => set("idPaisRegulador", Number(e.target.value))}>
                    <option value={0}>Seleccione...</option>
                    {cat.paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
                  </select>
                </Campo>
                {(cat.unidades.length > 0 || form.idUnidad > 0) && (
                  <Campo etiqueta="Unidad (sucursal)" obligatorio={cat.unidades.length > 0} error={errores.idUnidad}>
                    <select value={form.idUnidad} onChange={(e) => set("idUnidad", Number(e.target.value))}>
                      <option value={0}>Seleccione...</option>
                      {cat.unidades.map((u) => (<option key={u.id} value={u.id}>{u.ruta}</option>))}
                    </select>
                  </Campo>
                )}
                <Campo etiqueta="Razón social" obligatorio error={errores.razonSocial}>
                  <input type="text" value={form.razonSocial} onChange={(e) => set("razonSocial", e.target.value)} />
                </Campo>
                <Campo etiqueta="Forma jurídica" obligatorio error={errores.idFormaJuridica}>
                  <SelectCat items={cat.formasJuridicas} valor={form.idFormaJuridica} onCambiar={(v) => set("idFormaJuridica", v)} />
                </Campo>
                <Campo etiqueta="País de constitución" obligatorio error={errores.idPaisConstitucion}>
                  <SelectCat items={cat.paisesDireccion} valor={form.idPaisConstitucion} onCambiar={(v) => set("idPaisConstitucion", v)} />
                </Campo>
                <Campo etiqueta="Fecha de constitución" obligatorio={pide("ORG_FECHA_CONSTITUCION")} error={errores.fechaConstitucion}>
                  <input type="date" value={form.fechaConstitucion} onChange={(e) => set("fechaConstitucion", e.target.value)} />
                </Campo>
                <Campo etiqueta="Referencia registral" obligatorio={pide("ORG_REFERENCIA_REGISTRAL")} error={errores.referenciaRegistral}>
                  <input type="text" value={form.referenciaRegistral} onChange={(e) => set("referenciaRegistral", e.target.value)} />
                </Campo>
                <Campo etiqueta="Estado legal" obligatorio error={errores.idEstadoLegal}>
                  <SelectCat items={cat.estadosLegales} valor={form.idEstadoLegal} onCambiar={(v) => set("idEstadoLegal", v)} />
                </Campo>
                {cat.estadosLegales.find((x) => x.id === form.idEstadoLegal)?.codigo === "DISUELTA" && (
                  <Campo etiqueta="Fecha de disolución" error={errores.fechaDisolucion}>
                    <input type="date" value={form.fechaDisolucion} onChange={(e) => set("fechaDisolucion", e.target.value)} />
                  </Campo>
                )}
              </div>

              <div className="wiz__encabezado">
                <div>
                  <h2>Nombres comerciales{pide("ORG_NOMBRE_COMERCIAL") ? " *" : ""}</h2>
                  <div className="nota">Nombres con los que opera la organización (opcional salvo que el rol lo exija).</div>
                </div>
                <button type="button" className="boton-secundario" onClick={() => setForm((f) => ({ ...f, nombresComerciales: [...f.nombresComerciales, nomVacio()] }))}>
                  + Agregar nombre comercial
                </button>
              </div>
              {errores.nombresComerciales && <div className="wiz__alerta" role="alert">{errores.nombresComerciales}</div>}
              {form.nombresComerciales.map((n) => (
                <div key={n.clave} className="wiz-fila">
                  <div className="wiz__rejilla">
                    <Campo etiqueta="Nombre comercial" obligatorio error={errores[`${n.clave}.nombre`]}>
                      <input type="text" value={n.nombre}
                        onChange={(e) => setForm((f) => ({ ...f, nombresComerciales: f.nombresComerciales.map((x) => x.clave === n.clave ? { ...x, nombre: e.target.value } : x) }))} />
                    </Campo>
                  </div>
                  <div className="wiz-fila__acciones">
                    <button type="button" className="wiz-enlace wiz-enlace--peligro"
                      onClick={() => setForm((f) => ({ ...f, nombresComerciales: f.nombresComerciales.filter((x) => x.clave !== n.clave) }))}>Quitar</button>
                  </div>
                </div>
              ))}

              <div className="wiz__encabezado">
                <div>
                  <h2>Actividades económicas{pide("ORG_ACTIVIDAD") ? " *" : ""}</h2>
                  <div className="nota">Marque una como principal.</div>
                </div>
                <button type="button" className="boton-secundario" onClick={() => setForm((f) => ({ ...f, actividades: [...f.actividades, actVacia(f.actividades.length === 0)] }))}>
                  + Agregar actividad
                </button>
              </div>
              {errores.actividades && <div className="wiz__alerta" role="alert">{errores.actividades}</div>}
              {form.actividades.map((a, n) => (
                <TarjetaFila
                  key={a.clave}
                  titulo={`Actividad ${n + 1}${a.actividadNombre ? ` · ${a.actividadNombre}` : ""}`}
                  principal={a.esPrincipal}
                  onPrincipal={() => setForm((f) => ({ ...f, actividades: f.actividades.map((x) => ({ ...x, esPrincipal: x.clave === a.clave })) }))}
                  onQuitar={() => setForm((f) => ({ ...f, actividades: f.actividades.filter((x) => x.clave !== a.clave) }))}
                >
                  <div className="wiz__rejilla">
                    <Campo etiqueta="Actividad económica" obligatorio error={errores[`${a.clave}.actividad`]}>
                      <SelectCat items={cat.actividades} valor={a.idActividad} respaldoNombre={a.actividadNombre}
                        onCambiar={(v) => setForm((f) => ({ ...f, actividades: f.actividades.map((x) => x.clave === a.clave ? { ...x, idActividad: v } : x) }))} />
                    </Campo>
                    <Campo etiqueta="Descripción" error={errores[`${a.clave}.descripcion`]}>
                      <input type="text" value={a.descripcion}
                        onChange={(e) => setForm((f) => ({ ...f, actividades: f.actividades.map((x) => x.clave === a.clave ? { ...x, descripcion: e.target.value } : x) }))} />
                    </Campo>
                    <Campo etiqueta="Fecha de inicio">
                      <input type="date" value={a.fechaInicio}
                        onChange={(e) => setForm((f) => ({ ...f, actividades: f.actividades.map((x) => x.clave === a.clave ? { ...x, fechaInicio: e.target.value } : x) }))} />
                    </Campo>
                  </div>
                </TarjetaFila>
              ))}
            </div>
          )}
          {paso === 1 && !esOrg && (
            <div className="wiz__grupo">
              <h2>Datos personales</h2>
              <div className="wiz__rejilla">
                <Campo etiqueta="País regulador" obligatorio error={errores.idPaisRegulador}>
                  <select value={form.idPaisRegulador} onChange={(e) => set("idPaisRegulador", Number(e.target.value))}>
                    <option value={0}>Seleccione...</option>
                    {cat.paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
                  </select>
                </Campo>
                {(cat.unidades.length > 0 || form.idUnidad > 0) && (
                  <Campo etiqueta="Unidad (sucursal)" obligatorio={cat.unidades.length > 0} error={errores.idUnidad}>
                    <select value={form.idUnidad} onChange={(e) => set("idUnidad", Number(e.target.value))}>
                      <option value={0}>Seleccione...</option>
                      {form.idUnidad > 0 && !cat.unidades.some((u) => u.id === form.idUnidad) && (
                        <option value={form.idUnidad}>{form.unidadNombre ?? form.idUnidad} (inactiva)</option>
                      )}
                      {cat.unidades.map((u) => (<option key={u.id} value={u.id}>{u.ruta}</option>))}
                    </select>
                  </Campo>
                )}
                {form.empleado && (<>
                  <Campo etiqueta="Unidad del empleado (sucursal o departamento)" obligatorio error={errores.empUnidad}>
                    <select value={form.empleado.idUnidad} onChange={(e) => setForm((f) => ({ ...f, empleado: { idUnidad: Number(e.target.value), numero: f.empleado?.numero ?? "" } }))}>
                      <option value={0}>Seleccione...</option>
                      {cat.unidades.map((u) => (<option key={u.id} value={u.id}>{u.ruta}</option>))}
                    </select>
                  </Campo>
                  <Campo etiqueta="Número de empleado" obligatorio error={errores.empNumero}>
                    <input type="text" maxLength={30} value={form.empleado.numero} onChange={(e) => setForm((f) => ({ ...f, empleado: { idUnidad: f.empleado?.idUnidad ?? 0, numero: e.target.value } }))} />
                  </Campo>
                </>)}
                <Campo etiqueta="Primer nombre" obligatorio error={errores.nombre1}>
                  <input type="text" value={form.nombre1} onChange={(e) => set("nombre1", e.target.value)} />
                </Campo>
                <Campo etiqueta="Segundo nombre" obligatorio={pide("PERSONA_NOMBRE2")} error={errores.nombre2}>
                  <input type="text" value={form.nombre2} onChange={(e) => set("nombre2", e.target.value)} />
                </Campo>
                <Campo etiqueta="Primer apellido" obligatorio error={errores.apellido1}>
                  <input type="text" value={form.apellido1} onChange={(e) => set("apellido1", e.target.value)} />
                </Campo>
                <Campo etiqueta="Segundo apellido" obligatorio={pide("PERSONA_APELLIDO2")} error={errores.apellido2}>
                  <input type="text" value={form.apellido2} onChange={(e) => set("apellido2", e.target.value)} />
                </Campo>
                <Campo etiqueta="Fecha de nacimiento" obligatorio={pide("PERSONA_FECHA_NACIMIENTO")} error={errores.fechaNacimiento}>
                  <input type="date" value={form.fechaNacimiento} onChange={(e) => set("fechaNacimiento", e.target.value)} />
                </Campo>
                <Campo etiqueta="Nombre conocido" obligatorio={pide("PERSONA_NOMBRE_CONOCIDO")} error={errores.nombreConocido}>
                  <input type="text" maxLength={100} placeholder="Alias o nombre con el que se le conoce" value={form.nombreConocido} onChange={(e) => set("nombreConocido", e.target.value)} />
                </Campo>
                <Campo etiqueta="País de nacimiento" obligatorio={pide("PERSONA_PAIS_NACIMIENTO")} error={errores.idPaisNacimiento}>
                  <SelectCat items={cat.paisesDireccion} valor={form.idPaisNacimiento} onCambiar={(v) => set("idPaisNacimiento", v)} />
                </Campo>
                <Campo etiqueta="Sexo" obligatorio={pide("PERSONA_SEXO")} error={errores.idSexo}>
                  <SelectCat items={cat.sexos} valor={form.idSexo} onCambiar={(v) => set("idSexo", v)} />
                </Campo>
                <Campo etiqueta="Estado civil" obligatorio={pide("PERSONA_ESTADO_CIVIL")} error={errores.idEstadoCivil}>
                  <SelectCat items={cat.estadosCiviles} valor={form.idEstadoCivil} onCambiar={(v) => set("idEstadoCivil", v)} />
                </Campo>
              </div>
              <div className="wiz__encabezado" style={{ marginTop: 12 }}>
                <div>
                  <h2>Nacionalidades{pide("PERSONA_NACIONALIDAD") ? " *" : ""}</h2>
                  <div className="nota">Una persona puede tener más de una.</div>
                </div>
                <button type="button" className="boton-secundario" onClick={() => setForm((f) => ({ ...f, nacionalidades: [...f.nacionalidades, nacVacia()] }))}>
                  + Agregar nacionalidad
                </button>
              </div>
              {errores.nacionalidades && <div className="wiz__alerta" role="alert">{errores.nacionalidades}</div>}
              {form.nacionalidades.map((n) => (
                <div key={n.clave} className="wiz-fila">
                  <div className="wiz__rejilla">
                    <Campo etiqueta="País de la nacionalidad" obligatorio error={errores[`${n.clave}.pais`]}>
                      <SelectCat items={cat.paisesDireccion} valor={n.idPais}
                        onCambiar={(v) => setForm((f) => ({ ...f, nacionalidades: f.nacionalidades.map((x) => (x.clave === n.clave ? { ...x, idPais: v } : x)) }))} />
                    </Campo>
                  </div>
                  <div className="wiz-fila__acciones">
                    <button type="button" className="wiz-enlace wiz-enlace--peligro"
                      onClick={() => setForm((f) => ({ ...f, nacionalidades: f.nacionalidades.filter((x) => x.clave !== n.clave) }))}>Quitar</button>
                  </div>
                </div>
              ))}
            </div>
          )}

          {/* ---- Paso 2: identificaciones ---- */}
          {paso === 2 && (
            <div className="wiz__grupo">
              <div className="wiz__encabezado">
                <div>
                  <h2>Identificaciones del cliente</h2>
                  <div className="nota">Un cliente puede tener varias (cédula, pasaporte, DIMEX…). Se requiere al menos una; marque la principal.</div>
                </div>
                <button type="button" className="boton-secundario" onClick={() =>
                  setForm((f) => ({ ...f, identificaciones: [...f.identificaciones, identVacia(f.identificaciones.length === 0)] }))}>
                  + Agregar identificación
                </button>
              </div>
              {form.identificaciones.length === 0 && (
                <div className="wiz__vacio">Aún no hay identificaciones. Use «Agregar identificación» para registrar la primera.</div>
              )}
              {form.identificaciones.map((i, n) => (
                <TarjetaFila
                  key={i.clave}
                  titulo={`Identificación ${n + 1}${i.tipoNombre ? ` · ${i.tipoNombre}` : ""}`}
                  principal={i.esPrincipal}
                  onPrincipal={() => setForm((f) => ({ ...f, identificaciones: f.identificaciones.map((x) => ({ ...x, esPrincipal: x.clave === i.clave })) }))}
                  onQuitar={() => quitarIdent(i.clave)}
                  auditoria={i.auditoria}
                >
                  <div className="wiz__rejilla">
                    <Campo etiqueta="Tipo de identificación" obligatorio error={errores[`${i.clave}.tipo`]}>
                      <SelectCat items={tiposId} valor={i.idTipoIdentificacion} respaldoNombre={i.tipoNombre}
                        onCambiar={(v) => actIdent(i.clave, { idTipoIdentificacion: v })} />
                    </Campo>
                    <Campo etiqueta="Número" obligatorio error={errores[`${i.clave}.numero`]}>
                      <input type="text" value={i.numero} onChange={(e) => actIdent(i.clave, { numero: e.target.value })} />
                    </Campo>
                    <Campo etiqueta="Institución emisora" obligatorio={pide("IDENT_INSTITUCION")} error={errores[`${i.clave}.institucion`]}>
                      <SelectCat items={cat.instituciones} valor={i.idInstitucionEmisora} respaldoNombre={i.institucionNombre}
                        onCambiar={(v) => actIdent(i.clave, { idInstitucionEmisora: v || null })} />
                    </Campo>
                    <Campo etiqueta="Fecha de emisión" obligatorio={pide("IDENT_FECHA_EMISION")} error={errores[`${i.clave}.emision`]}>
                      <input type="date" value={i.fechaEmision ?? ""} onChange={(e) => actIdent(i.clave, { fechaEmision: e.target.value })} />
                    </Campo>
                    <Campo etiqueta="Fecha de vencimiento" obligatorio={pide("IDENT_FECHA_VENCIMIENTO")} error={errores[`${i.clave}.vencimiento`]}>
                      <input type="date" value={i.fechaVencimiento ?? ""} onChange={(e) => actIdent(i.clave, { fechaVencimiento: e.target.value })} />
                    </Campo>
                  </div>
                </TarjetaFila>
              ))}
            </div>
          )}

          {/* ---- Paso 3: direcciones ---- */}
          {paso === 3 && (
            <div className="wiz__grupo">
              <div className="wiz__encabezado">
                <div>
                  <h2>Direcciones del cliente</h2>
                  <div className="nota">Agregue las direcciones que necesite y asigne un tipo a cada una. {pide("DIRECCION") ? "Este rol exige al menos una dirección." : "Este paso es opcional."}</div>
                </div>
                <button type="button" className="boton-secundario" onClick={() =>
                  setForm((f) => ({
                    ...f,
                    direcciones: [...f.direcciones, dirVacia(f.direcciones.length === 0,
                      cat.tiposDireccion[Math.min(f.direcciones.length, cat.tiposDireccion.length - 1)]?.id ?? 0,
                      paisPorDefecto)],
                  }))}>
                  + Agregar dirección
                </button>
              </div>
              {form.direcciones.length === 0 && (
                <div className="wiz__vacio">Aún no hay direcciones. Use «Agregar dirección» para registrar la primera.</div>
              )}
              {form.direcciones.map((d, n) => (
                <TarjetaFila
                  key={d.clave}
                  titulo={`Dirección ${n + 1}${d.tipoNombre ? ` · ${d.tipoNombre}` : ""}`}
                  principal={d.esPrincipal}
                  onPrincipal={() => setForm((f) => ({ ...f, direcciones: f.direcciones.map((x) => ({ ...x, esPrincipal: x.clave === d.clave ? !x.esPrincipal : false })) }))}
                  onQuitar={() => setForm((f) => ({ ...f, direcciones: f.direcciones.filter((x) => x.clave !== d.clave) }))}
                  auditoria={d.auditoria}
                >
                  <div className="wiz__rejilla">
                    <Campo etiqueta="Tipo de dirección" obligatorio error={errores[`${d.clave}.tipo`]}>
                      <SelectCat items={cat.tiposDireccion} valor={d.idTipoDireccion} respaldoNombre={d.tipoNombre}
                        onCambiar={(v) => actDir(d.clave, { idTipoDireccion: v })} />
                    </Campo>
                  </div>
                  <DireccionCampos d={d} cfg={configs[d.idPais]} paises={cat.paisesDireccion} errores={errores}
                    onCambiar={(p) => actDir(d.clave, p)} onSinOpciones={marcarSinOpciones}
                    exigirNiveles={pide("DIR_NIVELES")} exigirCp={pide("DIR_CODIGO_POSTAL")} />
                  <Campo etiqueta="Dirección exacta (línea 1)" obligatorio error={errores[`${d.clave}.detalle`]}>
                    <input type="text" maxLength={500} value={d.detalle} onChange={(e) => actDir(d.clave, { detalle: e.target.value })} />
                  </Campo>
                  <Campo etiqueta="Dirección exacta (línea 2)" obligatorio={pide("DIR_LINEA2")} error={errores[`${d.clave}.detalle2`]}>
                    <input type="text" maxLength={200} value={d.detalle2 ?? ""} onChange={(e) => actDir(d.clave, { detalle2: e.target.value })} />
                  </Campo>
                  <Campo etiqueta="Dirección exacta (línea 3)" obligatorio={pide("DIR_LINEA3")} error={errores[`${d.clave}.detalle3`]}>
                    <input type="text" maxLength={200} value={d.detalle3 ?? ""} onChange={(e) => actDir(d.clave, { detalle3: e.target.value })} />
                  </Campo>
                </TarjetaFila>
              ))}
            </div>
          )}

          {/* ---- Paso 4: contacto ---- */}
          {paso === 4 && (
            <div className="wiz__grupo">
              <div className="wiz__encabezado">
                <div>
                  <h2>Medios de contacto</h2>
                  <div className="nota">Teléfonos, correo electrónico y WhatsApp. El formato se valida según el tipo. {pide("CORREO") || pide("TELEFONO") ? `Este rol exige: ${[pide("CORREO") && "correo electrónico", pide("TELEFONO") && "teléfono"].filter(Boolean).join(" y ")}.` : "Este paso es opcional."}</div>
                </div>
                <button type="button" className="boton-secundario" onClick={() =>
                  setForm((f) => ({ ...f, contactos: [...f.contactos, contVacio(cat.tiposContacto[0]?.id ?? 0, false)] }))}>
                  + Agregar medio de contacto
                </button>
              </div>
              {form.contactos.length === 0 && (
                <div className="wiz__vacio">Aún no hay medios de contacto. Use «Agregar medio de contacto».</div>
              )}
              {form.contactos.map((c) => {
                const codigo = codigoTipoContacto(cat, c.idTipoContacto);
                const errorFila = errores[`${c.clave}.valor`];
                // Mientras escribe: error de formato apenas hay texto; "valido" cuando cumple.
                const enVivo = c.valor.trim() ? validarContacto(codigo, c.valor) : null;
                const mensaje = errorFila ?? (c.valor.trim() ? enVivo ?? undefined : undefined);
                const valido = c.valor.trim() !== "" && enVivo === null;
                return (
                  <div key={c.clave} className="wiz-fila wiz-fila--contacto">
                    <div className="wiz-fila__contacto-campos">
                      <Campo etiqueta="Tipo" error={errores[`${c.clave}.tipo`]}>
                        <SelectCat items={cat.tiposContacto} valor={c.idTipoContacto} respaldoNombre={c.tipoNombre}
                          onCambiar={(v) => actCont(c.clave, { idTipoContacto: v })} />
                      </Campo>
                      <Campo etiqueta={codigo === "CORREO" ? "Correo electrónico" : "Número / dato"} error={mensaje}>
                        <input
                          type="text"
                          value={c.valor}
                          placeholder={ejemploContacto(codigo)}
                          className={valido ? "wiz-input--valido" : ""}
                          onChange={(e) => actCont(c.clave, { valor: e.target.value })}
                        />
                      </Campo>
                      {valido && <span className="wiz-campo__ok">Formato válido</span>}
                    </div>
                    <div className="wiz-fila__acciones">
                      <label className="wiz-fila__principal">
                        <input type="checkbox" checked={c.esPrincipal} onChange={() => marcarContactoPrincipal(c)} /> Principal de su tipo
                      </label>
                      <button type="button" className="wiz-enlace wiz-enlace--peligro"
                        onClick={() => setForm((f) => ({ ...f, contactos: f.contactos.filter((x) => x.clave !== c.clave) }))}>
                        Quitar
                      </button>
                    </div>
                    <div className="wiz-fila__auditoria">{c.auditoria ?? "Se registrará el usuario y la fecha de creación al guardar."}</div>
                  </div>
                );
              })}
            </div>
          )}

          {/* ---- Paso 5: perfil financiero ---- */}
          {paso === 5 && <PerfilFinancieroPaso form={form} setForm={setForm} cat={cat} errores={errores} pide={pide} />}

          {/* ---- Paso 6: revision ---- */}
          {paso === 6 && (
            <div className="wiz__grupo">
              <div>
                <h2>{esEditar ? "Revise los cambios antes de enviarlos" : "Revise la información del cliente"}</h2>
                <div className="nota">
                  {esEditar
                    ? "Se enviará una gestión de aprobación con las diferencias detalladas abajo."
                    : "Use «Editar» en cada bloque si necesita corregir algo. Nada se guarda hasta finalizar."}
                </div>
              </div>

              {esEditar && (
                <div className="wiz__cambios">
                  <strong>Cambios que se enviarán a aprobación</strong>
                  {cambios.length === 0 && <span className="nota">Todavía no hay diferencias con los datos actuales. Vuelva a un paso y modifique algún dato.</span>}
                  {cambios.map((c, k) => (
                    <div key={k} className="wiz__resumen-linea">
                      <span className="nota">{c.etiqueta}</span>
                      <span><span className="wiz__tachado">{c.antes}</span> → <strong>{c.despues}</strong></span>
                    </div>
                  ))}
                  <Campo etiqueta="Comentario para el aprobador (opcional)">
                    <input type="text" value={comentario} onChange={(e) => setComentario(e.target.value)} />
                  </Campo>
                </div>
              )}

              <BloqueResumen titulo="Datos generales" onEditar={() => irA(1)} lineas={esOrg ? [
                ["Razón social", form.razonSocial || "—"],
                ["Forma jurídica", nombreDe(cat.formasJuridicas, form.idFormaJuridica)],
                ["País de constitución", nombreDe(cat.paisesDireccion, form.idPaisConstitucion)],
                ["Fecha de constitución", fmtFecha(form.fechaConstitucion)],
                ["Referencia registral", form.referenciaRegistral || "—"],
                ["Estado legal", nombreDe(cat.estadosLegales, form.idEstadoLegal)],
                ["País regulador", nombrePais],
                ["Nombres comerciales", form.nombresComerciales.map((n) => n.nombre).filter(Boolean).join(", ") || "—"],
                ["Actividades económicas", form.actividades.map((a) => `${nombreDe(cat.actividades, a.idActividad)}${a.esPrincipal ? " (principal)" : ""}`).join("; ") || "—"],
              ] : [
                ["Nombre completo", nombreCompleto || "—"],
                ["País regulador", nombrePais],
                ["Fecha de nacimiento", fmtFecha(form.fechaNacimiento)],
                ["Nombre conocido", form.nombreConocido || "—"],
                ["País de nacimiento", nombreDe(cat.paisesDireccion, form.idPaisNacimiento)],
                ["Nacionalidades", form.nacionalidades.map((n) => nombreDe(cat.paisesDireccion, n.idPais)).join(", ") || "—"],
                ["Sexo", nombreDe(cat.sexos, form.idSexo)],
                ["Estado civil", nombreDe(cat.estadosCiviles, form.idEstadoCivil)],
              ]} />
              <BloqueResumen titulo={`Identificaciones (${form.identificaciones.length})`} onEditar={() => irA(2)}
                lineas={form.identificaciones.map((i) => [
                  `${nombreDe([...cat.tiposIdentificacion, ...cat.tiposIdentificacionOrg], i.idTipoIdentificacion)}${i.esPrincipal ? " (principal)" : ""}`,
                  `${i.numero} · ${nombreDe(cat.instituciones, i.idInstitucionEmisora)} · emitida ${fmtFecha(i.fechaEmision)} · ${i.fechaVencimiento ? `vence ${fmtFecha(i.fechaVencimiento)}` : "sin vencimiento"}`,
                ])} />
              <BloqueResumen titulo={`Direcciones (${form.direcciones.length})`} onEditar={() => irA(3)}
                lineas={form.direcciones.map((d) => [
                  `${nombreDe(cat.tiposDireccion, d.idTipoDireccion)}${d.esPrincipal ? " (principal)" : ""}`,
                  textoDireccion(d, cat),
                ])} />
              <BloqueResumen titulo={`Medios de contacto (${form.contactos.length})`} onEditar={() => irA(4)}
                lineas={form.contactos.map((c) => [
                  `${nombreDe(cat.tiposContacto, c.idTipoContacto)}${c.esPrincipal ? " (principal)" : ""}`, c.valor,
                ])} />

              <BloqueResumen titulo="Perfil financiero" onEditar={() => irA(5)} lineas={lineasPerfil(form, cat)} />

              {tienePermiso("DOC_ADJUNTAR") && <AdjuntosPanel tipoPersona={form.tipoPersona} onCambio={setAdjuntos} />}

              {!esEditar && <div className="wiz__aviso">El número de cliente se asigna al finalizar, justo antes de crear el registro definitivo.</div>}
            </div>
          )}
        </div>

        {confirmandoCancelar && (
          <div role="alertdialog" className="wiz__confirmar">
            <span>
              {esEditar
                ? "¿Descartar los cambios? Los datos del cliente no se modificarán."
                : idBorrador !== null
                  ? "¿Salir sin finalizar? El borrador guardado se conserva hasta su vencimiento."
                  : "¿Salir sin guardar? Se perderán los datos ingresados."}
            </span>
            <button type="button" className="boton-secundario" onClick={() => setConfirmandoCancelar(false)}>Seguir editando</button>
            <button type="button" className="wiz__peligro" onClick={descartar}>Descartar</button>
          </div>
        )}

        <div className="wiz__pie">
          <div className="wiz__pie-grupo">
            <button type="button" className="wiz-enlace" onClick={() => setConfirmandoCancelar(true)}>Cancelar</button>
            {!esEditar && (
              <button type="button" className="boton-secundario" disabled={!puedeGuardarBorrador(form)} onClick={guardarBorrador}
                title={puedeGuardarBorrador(form) ? "" : "Requiere al menos el primer nombre y una identificación con tipo y número"}>
                Guardar borrador
              </button>
            )}
          </div>
          <div className="wiz__pie-grupo">
            {paso > 1 && <button type="button" className="boton-secundario" onClick={() => irA(paso - 1)}>← Atrás</button>}
            {ultimo ? (
              <button type="button" onClick={finalizar} disabled={enviando || (esEditar && cambios.length === 0)}>
                {enviando ? "Enviando..." : esEditar ? "Enviar a aprobación" : "Finalizar y enviar alta"}
              </button>
            ) : (
              <button type="button" onClick={siguiente}>Siguiente →</button>
            )}
          </div>
        </div>
      </section>
    </div>
  );
}

function BloqueResumen({ titulo, lineas, onEditar }: { titulo: string; lineas: [string, string][]; onEditar: () => void }) {
  return (
    <div className="wiz__bloque">
      <div className="wiz__bloque-cabecera">
        <strong>{titulo}</strong>
        <button type="button" className="wiz-enlace" onClick={onEditar}>Editar</button>
      </div>
      <div className="wiz__bloque-cuerpo">
        {lineas.length === 0 && <span className="nota">Sin registros</span>}
        {lineas.map(([e, v], k) => (
          <div key={k} className="wiz__resumen-linea">
            <span className="nota">{e}</span>
            <span>{v}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Lineas del resumen del perfil financiero (solo lo que tiene datos). */
function lineasPerfil(f: FormCliente, cat: CatalogosCliente): [string, string][] {
  const p = f.perfil;
  const e = p.economico;
  const nom = (lista: CatalogoItemResponse[], id?: number | null) => lista.find((x) => x.id === id)?.nombre ?? "—";
  const mon = (id: number) => cat.monedas.find((m) => m.id === id)?.codigo ?? "";
  const conMoneda = (v: string, id: number) => (v.trim() ? `${v.trim()} ${mon(id)}`.trim() : "—");
  const l: [string, string][] = [];
  if (p.residenciasFiscales.length > 0)
    l.push(["Residencia fiscal", p.residenciasFiscales.map((r) => `${nom(cat.paisesDireccion, r.idPais)}${r.identificadorTributario.trim() ? ` (${r.identificadorTributario.trim()})` : ""}${r.esPrincipal ? " · principal" : ""}`).join("; ")]);
  if (f.tipoPersona === "Fisica") {
    if (e.idCondicionLaboral) l.push(["Condición laboral", nom(cat.condicionesLaborales, e.idCondicionLaboral)]);
    if (e.idOcupacion) l.push(["Ocupación", nom(cat.ocupaciones, e.idOcupacion)]);
    if (e.empleador.trim()) l.push(["Empleador o negocio", e.empleador.trim()]);
    if (e.gastosEstimados.trim()) l.push(["Gastos estimados", conMoneda(e.gastosEstimados, e.idMonedaBalance)]);
  } else if (e.patrimonioDeclarado.trim()) l.push(["Patrimonio declarado", conMoneda(e.patrimonioDeclarado, e.idMonedaBalance)]);
  if (e.activosDeclarados.trim()) l.push(["Activos declarados", conMoneda(e.activosDeclarados, e.idMonedaBalance)]);
  if (e.pasivosDeclarados.trim()) l.push(["Pasivos declarados", conMoneda(e.pasivosDeclarados, e.idMonedaBalance)]);
  if (e.fechaCorte) l.push(["Fecha de corte", fmtFecha(e.fechaCorte)]);
  if (p.ingresos.length > 0)
    l.push(["Ingresos", p.ingresos.map((i) => `${nom(cat.tiposIngreso, i.idTipoIngreso)} ${conMoneda(i.monto, i.idMoneda)} (${nom(cat.periodicidades, i.idPeriodicidad)})`).join("; ")]);
  const fondos = p.origenes.filter((o) => o.clase === "Fondos").map((o) => nom(cat.origenesFondos, o.idOrigen));
  if (fondos.length > 0) l.push(["Origen de fondos", fondos.join(", ")]);
  const patri = p.origenes.filter((o) => o.clase === "Patrimonio").map((o) => nom(cat.origenesPatrimonio, o.idOrigen));
  if (patri.length > 0) l.push(["Origen del patrimonio", patri.join(", ")]);
  if (p.perfilTransaccional.length > 0)
    l.push(["Transacciones esperadas", p.perfilTransaccional.map((t) => `${nom(cat.tiposTransaccion, t.idTipoTransaccion)} · ${nom(cat.frecuencias, t.idFrecuencia)}${t.montoMinimo.trim() ? ` · ${conMoneda(t.montoMinimo, t.idMoneda)} a ${conMoneda(t.montoMaximo, t.idMoneda)}` : ""}`).join("; ")]);
  if (p.cuentasExternas.length > 0)
    l.push(["Cuentas externas", p.cuentasExternas.map((x) => `${nom(cat.entidadesFinancieras, x.idEntidad)} · ${x.numeroCuenta.trim()} · ${nom(cat.tiposCuentaExterna, x.idTipoCuenta)}${x.idMoneda ? ` · ${mon(x.idMoneda)}` : ""}`).join("; ")]);
  return l;
}
