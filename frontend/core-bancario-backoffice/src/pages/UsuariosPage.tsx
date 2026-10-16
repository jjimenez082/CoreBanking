import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { usePermisos } from "../contexts/PermisosContext";
import type { ApiErrorBody, PerfilDatosResumen, RolResponse, UnidadItem, UsuarioAdmin, UsuarioCredencial, UsuarioPerfiles } from "../api/tipos";

function mensaje(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

interface FormUsuario {
  username: string; nombres: string; apellidos: string; correo: string;
  idUnidad: number; idSuperior: number; puesto: string; roles: string[]; perfiles: number[]; tipo: "FISICO" | "INTEGRACION";
}
const NUEVO: FormUsuario = { username: "", nombres: "", apellidos: "", correo: "", idUnidad: 0, idSuperior: 0, puesto: "", roles: [], perfiles: [], tipo: "FISICO" };

/**
 * Administración › Usuarios (permiso SEG_ADMINISTRACION): alta, cambio, activar/inactivar y restablecer contraseña.
 * El alta crea el usuario también en Keycloak (con contraseña temporal que se muestra una sola vez);
 * inactivar lo deshabilita allá y cierra sus sesiones.
 */
export function UsuariosPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<UsuarioAdmin[]>([]);
  const [roles, setRoles] = useState<RolResponse[]>([]);
  const { tienePermiso } = usePermisos();
  const puedePerfiles = tienePermiso("SEG_PERFILES");
  const [perfilesCat, setPerfilesCat] = useState<PerfilDatosResumen[]>([]);
  const [perfilesUsuarios, setPerfilesUsuarios] = useState<UsuarioPerfiles[]>([]);
  const [unidades, setUnidades] = useState<UnidadItem[]>([]);
  const [kcOk, setKcOk] = useState(true);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [credencial, setCredencial] = useState<UsuarioCredencial | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  // Ficha abierta: "ver" (solo lectura), "editar" o "nuevo". Con pestañas Información y Roles.
  const [panel, setPanel] = useState<"nuevo" | "editar" | "ver" | null>(null);
  const [pestana, setPestana] = useState<"info" | "roles" | "perfiles">("info");
  const [form, setForm] = useState<FormUsuario>(NUEVO);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const [u, r, un, est] = await Promise.all([
        api.get<UsuarioAdmin[]>("/api/usuarios"),
        api.get<RolResponse[]>("/api/seguridad/roles"),
        api.get<UnidadItem[]>("/api/catalogos/unidades"),
        api.get<{ keycloakConfigurado: boolean }>("/api/usuarios/estado-integracion"),
      ]);
      if (puedePerfiles) {
        const [pc, pu] = await Promise.all([api.get<PerfilDatosResumen[]>("/api/perfiles-datos"), api.get<UsuarioPerfiles[]>("/api/perfiles-datos/usuarios")]);
        setPerfilesCat(pc.data); setPerfilesUsuarios(pu.data);
      }
      setFilas(u.data); setRoles(r.data); setUnidades(un.data); setKcOk(est.data.keycloakConfigurado); setSel(new Set());
    } catch (e) { setError(mensaje(e, "No se pudieron cargar los usuarios.")); }
    finally { setCargando(false); }
  }, [api, puedePerfiles]);

  useEffect(() => { void cargar(); }, [cargar]);

  const columnas: ColumnaTabla<UsuarioAdmin>[] = useMemo(() => [
    { clave: "tipo", encabezado: "Tipo", render: (u) => (u.tipoUsuario === "INTEGRACION" ? "Integración" : "Físico"), textoBusqueda: (u) => (u.tipoUsuario === "INTEGRACION" ? "Integración" : "Físico") },
    { clave: "username", encabezado: "Usuario", render: (u) => u.username ?? "—", textoBusqueda: (u) => u.username ?? "" },
    {
      clave: "nombre", encabezado: "Nombre", textoBusqueda: (u) => u.nombreCompleto,
      render: (u) => <button type="button" className="boton-enlace-id" onClick={() => abrirVer(u)}>{u.nombreCompleto}</button>,
    },
    { clave: "correo", encabezado: "Correo", render: (u) => u.correo, textoBusqueda: (u) => u.correo },
    { clave: "unidad", encabezado: "Unidad", render: (u) => u.unidadNombre ?? "—", textoBusqueda: (u) => u.unidadNombre ?? "" },
    { clave: "puesto", encabezado: "Puesto", render: (u) => u.puesto ?? "—", textoBusqueda: (u) => u.puesto ?? "" },
    { clave: "superior", encabezado: "Superior", render: (u) => u.superiorNombre ?? "—", textoBusqueda: (u) => u.superiorNombre ?? "" },
    { clave: "roles", encabezado: "Roles", render: (u) => u.codigosRol.join(", ") || "—", textoBusqueda: (u) => u.codigosRol.join(" ") },
    { clave: "estado", encabezado: "Estado", render: (u) => u.estado, textoBusqueda: (u) => u.estado },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const seleccionado = filas.find((f) => f.idUsuario === idSel) ?? null;

  function abrirNuevo() {
    setExito(null); setCredencial(null); setErrorForm(null);
    setForm(NUEVO); setPestana("info");
    setPanel("nuevo");
  }

  function cargarForm(u: UsuarioAdmin) {
    // Usuarios anteriores a este mantenimiento no tienen nombres/apellidos separados: se parte del nombre completo.
    const [primero, ...resto] = u.nombreCompleto.split(" ");
    setForm({
      username: u.username ?? "",
      nombres: u.nombres ?? primero ?? "",
      apellidos: u.apellidos ?? resto.join(" "),
      correo: u.correo, idUnidad: u.idUnidad ?? 0, idSuperior: u.idSuperior ?? 0, puesto: u.puesto ?? "",
      roles: [...u.codigosRol], perfiles: [...(perfilesUsuarios.find((x) => x.idUsuario === u.idUsuario)?.perfiles ?? [])], tipo: u.tipoUsuario,
    });
  }

  function abrirVer(u: UsuarioAdmin) {
    setExito(null); setCredencial(null); setErrorForm(null);
    setSel(new Set([u.idUsuario]));
    cargarForm(u); setPestana("info");
    setPanel("ver");
  }

  function abrirEditar() {
    if (!seleccionado) return;
    setExito(null); setCredencial(null); setErrorForm(null);
    cargarForm(seleccionado); setPestana("info");
    setPanel("editar");
  }

  const tecnico = form.tipo === "INTEGRACION";

  const formRef = useRef<HTMLFormElement>(null);

  /** Valida los campos de la pestaña Información (aunque se esté en Roles) y guarda. */
  function intentarGuardar() {
    const f = formRef.current;
    if (f && !f.checkValidity()) {
      setPestana("info");
      setTimeout(() => f.reportValidity(), 0);
      return;
    }
    void guardar();
  }

  async function guardar(e?: FormEvent) {
    e?.preventDefault();
    setGuardando(true); setErrorForm(null);
    try {
      if (panel === "nuevo") {
        const r = await api.post<UsuarioCredencial>("/api/usuarios", {
          username: form.username, nombres: form.nombres, apellidos: tecnico ? "" : form.apellidos, correo: tecnico ? "" : form.correo, tipo: form.tipo,
          idUnidad: tecnico ? null : form.idUnidad || null, idSuperior: tecnico ? null : form.idSuperior || null,
          puesto: tecnico ? null : form.puesto.trim() || null, codigosRol: form.roles,
        });
        setCredencial(tecnico ? null : r.data);
        if (puedePerfiles && form.perfiles.length > 0) await api.put(`/api/perfiles-datos/usuarios/${r.data.idUsuario}`, { perfiles: form.perfiles });
        setExito("Usuario creado.");
      } else if (seleccionado) {
        await api.put(`/api/usuarios/${seleccionado.idUsuario}`, {
          nombres: form.nombres, apellidos: tecnico ? "" : form.apellidos, correo: tecnico ? seleccionado.correo : form.correo,
          idUnidad: tecnico ? null : form.idUnidad || null, idSuperior: tecnico ? null : form.idSuperior || null,
          puesto: tecnico ? null : form.puesto.trim() || null, codigosRol: form.roles,
        });
        if (puedePerfiles) {
          const antes = perfilesUsuarios.find((x) => x.idUsuario === seleccionado.idUsuario)?.perfiles ?? [];
          const igual = antes.length === form.perfiles.length && antes.every((x) => form.perfiles.includes(x));
          if (!igual) await api.put(`/api/perfiles-datos/usuarios/${seleccionado.idUsuario}`, { perfiles: form.perfiles });
        }
        setExito("Usuario actualizado.");
      }
      setPanel(null);
      await cargar();
    } catch (err) { setErrorForm(mensaje(err, "No se pudo guardar.")); }
    finally { setGuardando(false); }
  }

  async function cambiarEstado(estado: "Activo" | "Inactivo") {
    if (!seleccionado) return;
    if (estado === "Inactivo" && !window.confirm(seleccionado.tipoUsuario === "INTEGRACION"
      ? `¿Inactivar el usuario técnico ${seleccionado.nombreCompleto}? Las integraciones que lo usen dejarán de funcionar.`
      : `¿Inactivar a ${seleccionado.nombreCompleto}? No podrá ingresar y se cerrarán sus sesiones.`)) return;
    setExito(null); setError(null); setCredencial(null);
    try {
      await api.put(`/api/usuarios/${seleccionado.idUsuario}/estado`, { estado });
      setExito(estado === "Activo" ? "Usuario activado." : "Usuario inactivado.");
      await cargar();
    } catch (err) { setError(mensaje(err, "No se pudo cambiar el estado.")); }
  }

  async function restablecer() {
    if (!seleccionado) return;
    if (!window.confirm(`¿Restablecer la contraseña de ${seleccionado.nombreCompleto}? Se generará una temporal y deberá cambiarla al ingresar.`)) return;
    setExito(null); setError(null); setCredencial(null);
    try {
      const r = await api.post<UsuarioCredencial>(`/api/usuarios/${seleccionado.idUsuario}/restablecer-password`);
      setCredencial(r.data);
      setExito("Contraseña restablecida.");
    } catch (err) { setError(mensaje(err, "No se pudo restablecer la contraseña.")); }
  }

  const superiores = filas.filter((u) => u.estado === "Activo" && u.tipoUsuario === "FISICO" && (panel === "nuevo" || u.idUsuario !== seleccionado?.idUsuario));
  const editando = panel === "editar" || panel === "nuevo";

  const columnasRoles: ColumnaTabla<RolResponse>[] = [
    { clave: "codigo", encabezado: "Código", render: (r) => r.codigo, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Nombre", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
    { clave: "permisos", encabezado: "Permisos asignados", render: (r) => r.codigosObjetoAutorizacion.length, textoBusqueda: (r) => r.codigosObjetoAutorizacion.join(" ") },
  ];

  const columnasPerfiles: ColumnaTabla<PerfilDatosResumen>[] = [
    { clave: "codigo", encabezado: "Código", render: (r) => r.codigo, textoBusqueda: (r) => r.codigo },
    { clave: "nombre", encabezado: "Nombre", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "filas", encabezado: "Clientes que ve", render: (r) => r.filas, textoBusqueda: (r) => r.filas },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
  ];

  function volverALista() { setPanel(null); setErrorForm(null); }

  // ---------------------------------------------------------------- Ficha del usuario
  if (panel) {
    const nombreTitulo = panel === "nuevo" ? "Nuevo usuario" : (seleccionado?.nombreCompleto ?? "Usuario");
    const dato = (etiqueta: string, valor?: string | null) => (<><dt>{etiqueta}</dt><dd>{valor || "—"}</dd></>);
    return (
      <div>
        <p className="nota"><button type="button" className="boton-enlace" onClick={volverALista}>← Usuarios</button></p>
        <h1>{nombreTitulo}</h1>
        {errorForm && <p className="mensaje-error">{errorForm}</p>}

        <div>
          <div style={{ display: "flex", gap: "0.5rem" }}>
            {panel === "ver" && <button type="button" onClick={() => { setExito(null); setPanel("editar"); }}>Editar</button>}
            {editando && <button type="button" onClick={intentarGuardar} disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>}
            {editando && (
              <button type="button" className="boton-secundario" disabled={guardando}
                onClick={() => { if (panel === "nuevo") volverALista(); else { if (seleccionado) cargarForm(seleccionado); setErrorForm(null); setPanel("ver"); } }}>
                Cancelar
              </button>
            )}
          </div>

          <div className="v360__pestanas">
            <button type="button" className={`v360__pestana ${pestana === "info" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("info")}>Información</button>
            <button type="button" className={`v360__pestana ${pestana === "roles" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("roles")}>Roles ({form.roles.length})</button>
            {puedePerfiles && <button type="button" className={`v360__pestana ${pestana === "perfiles" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("perfiles")}>Perfiles de datos ({form.perfiles.length})</button>}
          </div>

          {pestana === "info" && panel === "ver" && (
            <dl className="ficha-datos" style={{ display: "grid", gridTemplateColumns: "max-content 1fr", gap: "0.25rem 1rem" }}>
              {dato("Tipo", tecnico ? "Integración (usuario técnico, no inicia sesión)" : "Físico (persona, inicia sesión)")}
              {dato(tecnico ? "Código" : "Usuario", form.username)}
              {dato(tecnico ? "Nombre" : "Nombres", form.nombres)}
              {!tecnico && (<>
                {dato("Apellidos", form.apellidos)}
                {dato("Correo", form.correo)}
                {dato("Unidad", seleccionado?.unidadNombre)}
                {dato("Puesto", form.puesto)}
                {dato("Superior directo", seleccionado?.superiorNombre)}
              </>)}
              {dato("Estado", seleccionado?.estado)}
            </dl>
          )}

          {editando && (
            <form ref={formRef} className="formulario" onSubmit={guardar} style={{ display: pestana === "info" ? undefined : "none" }}>
              <label>Tipo de usuario
                <select value={form.tipo} disabled={panel === "editar"} onChange={(e) => setForm({ ...form, tipo: e.target.value as FormUsuario["tipo"] })}>
                  <option value="FISICO">Físico (persona, inicia sesión)</option>
                  <option value="INTEGRACION">Integración (usuario técnico, no inicia sesión)</option>
                </select>
              </label>
              {tecnico && <p className="nota">Un usuario de integración no tiene contraseña ni cuenta en Keycloak: no puede ingresar a la plataforma. Se usa en «Usuarios de integración» (Usuario asociado) y sus permisos se dan con roles.</p>}
              {!tecnico && !kcOk && panel === "nuevo" && <p className="mensaje-error">Keycloak no está configurado: solo se pueden crear usuarios de tipo Integración.</p>}
              <label>{tecnico ? "Código del usuario técnico" : "Usuario (para ingresar)"}
                <input type="text" value={form.username} maxLength={50} disabled={panel === "editar"} required
                  pattern="[a-z0-9][a-z0-9._\-]{2,49}" title="3 a 50 caracteres: minúsculas, números, punto, guion o guion bajo"
                  onChange={(e) => setForm({ ...form, username: e.target.value.toLowerCase() })} />
              </label>
              <label>{tecnico ? "Nombre" : "Nombres"}
                <input type="text" value={form.nombres} maxLength={100} required onChange={(e) => setForm({ ...form, nombres: e.target.value })} />
              </label>
              {!tecnico && (<>
                <label>Apellidos
                  <input type="text" value={form.apellidos} maxLength={100} required onChange={(e) => setForm({ ...form, apellidos: e.target.value })} />
                </label>
                <label>Correo
                  <input type="email" value={form.correo} maxLength={200} required onChange={(e) => setForm({ ...form, correo: e.target.value })} />
                </label>
                <label>Unidad
                  <select value={form.idUnidad} onChange={(e) => setForm({ ...form, idUnidad: Number(e.target.value) })}>
                    <option value={0}>— Sin unidad —</option>
                    {panel === "editar" && form.idUnidad > 0 && !unidades.some((u) => u.id === form.idUnidad) && (
                      <option value={form.idUnidad}>{seleccionado?.unidadNombre} (inactiva)</option>
                    )}
                    {unidades.map((u) => (<option key={u.id} value={u.id}>{u.ruta}</option>))}
                  </select>
                </label>
                <label>Puesto
                  <input value={form.puesto} maxLength={100} placeholder="Ej. Analista de crédito"
                    onChange={(e) => setForm({ ...form, puesto: e.target.value })} />
                </label>
                <label>Superior directo
                  <select value={form.idSuperior} onChange={(e) => setForm({ ...form, idSuperior: Number(e.target.value) })}>
                    <option value={0}>— Sin superior —</option>
                    {panel === "editar" && form.idSuperior > 0 && !superiores.some((u) => u.idUsuario === form.idSuperior) && (
                      <option value={form.idSuperior}>{seleccionado?.superiorNombre} (inactivo)</option>
                    )}
                    {superiores.map((u) => (<option key={u.idUsuario} value={u.idUsuario}>{u.nombreCompleto}</option>))}
                  </select>
                </label>
              </>)}
            </form>
          )}

          {pestana === "roles" && (
            <section>
              <p className="nota">
                {editando ? "Marque los roles que tendrá el usuario." : "Roles existentes; los marcados son los que tiene el usuario."}
              </p>
              <TablaDatos
                columnas={columnasRoles} filas={roles} claveFila={(r) => r.codigo}
                seleccion={new Set(form.roles)}
                onCambiarSeleccion={(n) => { if (editando) setForm({ ...form, roles: Array.from(n).map(String) }); }}
                mensajeVacio="No hay roles." placeholderBusqueda="Buscar rol" comandos={[]}
              />
            </section>
          )}

          {pestana === "perfiles" && puedePerfiles && (
            <section>
              <p className="nota">
                {editando ? "Marque los perfiles de datos del usuario: definen qué clientes ve y con qué máscaras. " : "Perfiles de datos existentes; los marcados son los que tiene el usuario. "}
                Sin ningún perfil marcado, el usuario ve todos los clientes sin máscaras. Un usuario de integración actúa con los perfiles de su usuario técnico.
              </p>
              <TablaDatos
                columnas={columnasPerfiles} filas={perfilesCat.filter((p) => p.estado === "Activo" || form.perfiles.includes(p.idPerfilDatos))} claveFila={(r) => r.idPerfilDatos}
                seleccion={new Set(form.perfiles)}
                onCambiarSeleccion={(n) => { if (editando) setForm({ ...form, perfiles: Array.from(n).map(Number) }); }}
                mensajeVacio="No hay perfiles de datos." placeholderBusqueda="Buscar perfil" comandos={[]}
              />
            </section>
          )}
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------- Lista
  return (
    <div>
      <h1>Usuarios</h1>
      <p className="nota">
        Alta, cambio e inactivación de usuarios. Abra un usuario con su nombre para verlo, o márquelo y use «Editar». Los usuarios físicos se crean también en Keycloak
        con una contraseña temporal. El superior directo aprueba las gestiones de sus subordinados.
      </p>
      {!kcOk && (
        <p className="mensaje-error">
          La integración con Keycloak no está configurada: no se podrán crear ni inactivar usuarios físicos hasta completar Keycloak:Admin en appsettings (ver LEEME).
        </p>
      )}
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      {credencial && (
        <div className="v360__aviso">
          <strong>Credencial temporal de {credencial.username}:</strong>{" "}
          <code style={{ fontSize: "1.05rem" }}>{credencial.passwordTemporal}</code>{" "}
          <button type="button" className="boton-secundario" onClick={() => void navigator.clipboard?.writeText(credencial.passwordTemporal)}>Copiar</button>
          <div className="nota">Se muestra una sola vez; entréguela al usuario por un medio seguro. Deberá cambiarla en su primer ingreso.</div>
        </div>
      )}

      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idUsuario}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando} mensajeVacio="No hay usuarios."
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", onClick: abrirNuevo },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: abrirEditar },
          { clave: "activar", etiqueta: "Activar", icono: "✓", requiereSeleccion: "una", deshabilitado: (!kcOk && seleccionado?.tipoUsuario !== "INTEGRACION") || seleccionado?.estado === "Activo", onClick: () => void cambiarEstado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", icono: "⊘", requiereSeleccion: "una", deshabilitado: (!kcOk && seleccionado?.tipoUsuario !== "INTEGRACION") || seleccionado?.estado === "Inactivo", onClick: () => void cambiarEstado("Inactivo") },
          { clave: "password", etiqueta: "Restablecer contraseña", icono: "🔑", requiereSeleccion: "una", deshabilitado: !kcOk || seleccionado?.estado !== "Activo" || seleccionado?.tipoUsuario === "INTEGRACION", onClick: () => void restablecer() },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
    </div>
  );
}
