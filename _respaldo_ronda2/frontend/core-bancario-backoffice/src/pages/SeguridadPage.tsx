import { useEffect, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type {
  ApiErrorBody,
  ObjetoAutorizacionResponse,
  RolCrearRequest,
  RolResponse,
  UsuarioResponse,
} from "../api/tipos";

const COLUMNAS_ROLES: ColumnaTabla<RolResponse>[] = [
  {
    clave: "codigo",
    encabezado: "Código",
    render: (r) => r.codigo,
    textoBusqueda: (r) => r.codigo,
  },
  {
    clave: "nombre",
    encabezado: "Nombre",
    render: (r) => r.nombre,
    textoBusqueda: (r) => r.nombre,
  },
  { clave: "estado", encabezado: "Estado", render: (r) => r.estado },
  {
    clave: "permisos",
    encabezado: "Permisos asignados",
    render: (r) => r.codigosObjetoAutorizacion.length,
    textoBusqueda: (r) => r.codigosObjetoAutorizacion.join(" "),
  },
];

const COLUMNAS_USUARIOS: ColumnaTabla<UsuarioResponse>[] = [
  {
    clave: "nombre",
    encabezado: "Nombre",
    render: (u) => u.nombreCompleto,
    textoBusqueda: (u) => u.nombreCompleto,
  },
  {
    clave: "correo",
    encabezado: "Correo",
    render: (u) => u.correo,
    textoBusqueda: (u) => u.correo,
  },
  { clave: "estado", encabezado: "Estado", render: (u) => u.estado },
  {
    clave: "roles",
    encabezado: "Roles asignados",
    render: (u) => u.codigosRol.join(", ") || "—",
    textoBusqueda: (u) => u.codigosRol.join(" "),
  },
];

function unica(seleccion: Set<string | number>): number | null {
  return seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
}

/**
 * Administracion del modulo de Seguridad (RBAC, SEG-002): roles, permisos de
 * cada rol y roles de cada usuario. Usa el patron estandar de mantenimiento
 * (TablaDatos): comandos arriba, checkbox por registro, buscador local.
 *
 * Protegida por SEG_ADMINISTRACION (ver sembrar_roles_clientes.sql).
 */
export function SeguridadPage() {
  const api = useApiClient();
  const { cargando: cargandoPermisos, tienePermiso } = usePermisos();
  const puedeAdministrar = tienePermiso("SEG_ADMINISTRACION");

  const [objetos, setObjetos] = useState<ObjetoAutorizacionResponse[]>([]);
  const [roles, setRoles] = useState<RolResponse[]>([]);
  const [usuarios, setUsuarios] = useState<UsuarioResponse[]>([]);
  const [cargandoDatos, setCargandoDatos] = useState(false);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);

  const [seleccionRoles, setSeleccionRoles] = useState<Set<string | number>>(new Set());
  const [seleccionUsuarios, setSeleccionUsuarios] = useState<Set<string | number>>(new Set());

  // --- Crear rol ---
  const [mostrarNuevoRol, setMostrarNuevoRol] = useState(false);
  const [nuevoCodigo, setNuevoCodigo] = useState("");
  const [nuevoNombre, setNuevoNombre] = useState("");
  const [creandoRol, setCreandoRol] = useState(false);
  const [errorCrearRol, setErrorCrearRol] = useState<string | null>(null);

  // --- Editor de permisos de un rol ---
  const [idRolEditando, setIdRolEditando] = useState<number | null>(null);
  const [objetosMarcados, setObjetosMarcados] = useState<Set<string>>(new Set());
  const [guardandoObjetosRol, setGuardandoObjetosRol] = useState(false);
  const [errorObjetosRol, setErrorObjetosRol] = useState<string | null>(null);

  // --- Editor de roles de un usuario ---
  const [idUsuarioEditando, setIdUsuarioEditando] = useState<number | null>(null);
  const [rolesMarcados, setRolesMarcados] = useState<Set<string>>(new Set());
  const [guardandoRolesUsuario, setGuardandoRolesUsuario] = useState(false);
  const [errorRolesUsuario, setErrorRolesUsuario] = useState<string | null>(null);

  async function cargarTodo() {
    setCargandoDatos(true);
    setErrorCarga(null);
    try {
      const [respObjetos, respRoles, respUsuarios] = await Promise.all([
        api.get<ObjetoAutorizacionResponse[]>("/api/seguridad/objetos-autorizacion"),
        api.get<RolResponse[]>("/api/seguridad/roles"),
        api.get<UsuarioResponse[]>("/api/seguridad/usuarios"),
      ]);
      setObjetos(respObjetos.data);
      setRoles(respRoles.data);
      setUsuarios(respUsuarios.data);
    } catch (err) {
      if (isAxiosError(err)) {
        setErrorCarga(`Error ${err.response?.status ?? ""}: ${err.message}`);
      } else {
        setErrorCarga("Error inesperado al cargar el modulo de Seguridad.");
      }
    } finally {
      setCargandoDatos(false);
    }
  }

  useEffect(() => {
    if (cargandoPermisos || !puedeAdministrar) return;
    cargarTodo();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cargandoPermisos, puedeAdministrar]);

  async function alCrearRol(evento: FormEvent) {
    evento.preventDefault();
    setCreandoRol(true);
    setErrorCrearRol(null);

    try {
      const cuerpo: RolCrearRequest = { codigo: nuevoCodigo, nombre: nuevoNombre };
      await api.post<RolResponse>("/api/seguridad/roles", cuerpo);
      setNuevoCodigo("");
      setNuevoNombre("");
      setMostrarNuevoRol(false);
      await cargarTodo();
    } catch (err) {
      if (isAxiosError<ApiErrorBody>(err) && err.response?.data?.mensaje) {
        setErrorCrearRol(err.response.data.mensaje);
      } else if (isAxiosError(err)) {
        setErrorCrearRol(`Error ${err.response?.status ?? ""}: ${err.message}`);
      } else {
        setErrorCrearRol("Error inesperado al crear el rol.");
      }
    } finally {
      setCreandoRol(false);
    }
  }

  function editarPermisosRol() {
    const id = unica(seleccionRoles);
    const rol = roles.find((r) => r.idRolAutorizacion === id);
    if (!rol) return;
    setIdRolEditando(rol.idRolAutorizacion);
    setObjetosMarcados(new Set(rol.codigosObjetoAutorizacion));
    setErrorObjetosRol(null);
  }

  function alternar(conjunto: Set<string>, codigo: string): Set<string> {
    const copia = new Set(conjunto);
    if (copia.has(codigo)) copia.delete(codigo);
    else copia.add(codigo);
    return copia;
  }

  async function guardarObjetosRol() {
    if (idRolEditando === null) return;
    setGuardandoObjetosRol(true);
    setErrorObjetosRol(null);

    try {
      await api.put<RolResponse>(`/api/seguridad/roles/${idRolEditando}/objetos`, {
        codigosObjetoAutorizacion: Array.from(objetosMarcados),
      });
      setIdRolEditando(null);
      await cargarTodo();
    } catch (err) {
      if (isAxiosError(err)) {
        setErrorObjetosRol(`Error ${err.response?.status ?? ""}: ${err.message}`);
      } else {
        setErrorObjetosRol("Error inesperado al guardar los permisos del rol.");
      }
    } finally {
      setGuardandoObjetosRol(false);
    }
  }

  function editarRolesUsuario() {
    const id = unica(seleccionUsuarios);
    const usuario = usuarios.find((u) => u.idUsuario === id);
    if (!usuario) return;
    setIdUsuarioEditando(usuario.idUsuario);
    setRolesMarcados(new Set(usuario.codigosRol));
    setErrorRolesUsuario(null);
  }

  async function guardarRolesUsuario() {
    if (idUsuarioEditando === null) return;
    setGuardandoRolesUsuario(true);
    setErrorRolesUsuario(null);

    try {
      await api.put<UsuarioResponse>(`/api/seguridad/usuarios/${idUsuarioEditando}/roles`, {
        codigosRol: Array.from(rolesMarcados),
      });
      setIdUsuarioEditando(null);
      await cargarTodo();
    } catch (err) {
      if (isAxiosError(err)) {
        setErrorRolesUsuario(`Error ${err.response?.status ?? ""}: ${err.message}`);
      } else {
        setErrorRolesUsuario("Error inesperado al guardar los roles del usuario.");
      }
    } finally {
      setGuardandoRolesUsuario(false);
    }
  }

  if (cargandoPermisos) {
    return (
      <div>
        <h1>Seguridad</h1>
        <p className="nota">Verificando permisos...</p>
      </div>
    );
  }

  if (!puedeAdministrar) {
    return (
      <div>
        <h1>Seguridad</h1>
        <p className="mensaje-error">
          No tienes el permiso <strong>SEG_ADMINISTRACION</strong> para ver esta sección. Pídele a
          un administrador que te asigne un rol con ese permiso (RBAC, SEG-002).
        </p>
      </div>
    );
  }

  const rolEditando = roles.find((r) => r.idRolAutorizacion === idRolEditando) ?? null;
  const usuarioEditando = usuarios.find((u) => u.idUsuario === idUsuarioEditando) ?? null;

  return (
    <div>
      <h1>Seguridad</h1>
      <p className="nota">
        Mantenimiento de roles, permisos y usuarios (RBAC, SEG-002). Selecciona un registro con su
        casilla y usa la barra de comandos.
      </p>

      {errorCarga && <p className="mensaje-error">{errorCarga}</p>}

      <section className="seccion">
        <h2>Roles</h2>
        <TablaDatos
          columnas={COLUMNAS_ROLES}
          filas={roles}
          claveFila={(r) => r.idRolAutorizacion}
          seleccion={seleccionRoles}
          onCambiarSeleccion={setSeleccionRoles}
          cargando={cargandoDatos}
          mensajeVacio="No hay roles creados."
          placeholderBusqueda="Buscar rol"
          comandos={[
            {
              clave: "nuevo",
              etiqueta: "Nuevo",
              icono: "+",
              onClick: () => setMostrarNuevoRol((v) => !v),
            },
            {
              clave: "editar",
              etiqueta: "Editar permisos",
              icono: "✎",
              requiereSeleccion: "una",
              onClick: editarPermisosRol,
            },
            { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: cargarTodo },
          ]}
        />

        {mostrarNuevoRol && (
          <div className="panel-formulario">
            <h3>Nuevo rol</h3>
            <form onSubmit={alCrearRol} className="formulario-en-linea">
              <input
                type="text"
                placeholder="Código (ej. ANALISTA_CREDITO)"
                value={nuevoCodigo}
                onChange={(e) => setNuevoCodigo(e.target.value)}
                required
              />
              <input
                type="text"
                placeholder="Nombre descriptivo"
                value={nuevoNombre}
                onChange={(e) => setNuevoNombre(e.target.value)}
                required
              />
              <button type="submit" disabled={creandoRol}>
                {creandoRol ? "Creando..." : "Crear rol"}
              </button>
              <button
                type="button"
                className="boton-secundario"
                onClick={() => setMostrarNuevoRol(false)}
              >
                Cancelar
              </button>
            </form>
            {errorCrearRol && <p className="mensaje-error">{errorCrearRol}</p>}
          </div>
        )}

        {rolEditando && (
          <div className="panel-formulario">
            <h3>
              Permisos del rol <strong>{rolEditando.codigo}</strong>
            </h3>
            <ul className="lista-checkbox">
              {objetos.map((objeto) => (
                <li key={objeto.codigo}>
                  <label>
                    <input
                      type="checkbox"
                      checked={objetosMarcados.has(objeto.codigo)}
                      onChange={() => setObjetosMarcados(alternar(objetosMarcados, objeto.codigo))}
                    />
                    <strong>{objeto.codigo}</strong> — {objeto.nombre}
                  </label>
                </li>
              ))}
            </ul>
            <div className="panel-formulario__acciones">
              <button type="button" onClick={guardarObjetosRol} disabled={guardandoObjetosRol}>
                {guardandoObjetosRol ? "Guardando..." : "Guardar permisos"}
              </button>
              <button
                type="button"
                className="boton-secundario"
                onClick={() => setIdRolEditando(null)}
              >
                Cancelar
              </button>
            </div>
            {errorObjetosRol && <p className="mensaje-error">{errorObjetosRol}</p>}
          </div>
        )}
      </section>

      <section className="seccion">
        <h2>Usuarios</h2>
        <TablaDatos
          columnas={COLUMNAS_USUARIOS}
          filas={usuarios}
          claveFila={(u) => u.idUsuario}
          seleccion={seleccionUsuarios}
          onCambiarSeleccion={setSeleccionUsuarios}
          cargando={cargandoDatos}
          mensajeVacio="No hay usuarios en esta compañía."
          placeholderBusqueda="Buscar usuario"
          comandos={[
            {
              clave: "editar",
              etiqueta: "Editar roles",
              icono: "✎",
              requiereSeleccion: "una",
              onClick: editarRolesUsuario,
            },
            { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: cargarTodo },
          ]}
        />

        {usuarioEditando && (
          <div className="panel-formulario">
            <h3>
              Roles de <strong>{usuarioEditando.nombreCompleto}</strong>
            </h3>
            <ul className="lista-checkbox">
              {roles.map((rol) => (
                <li key={rol.codigo}>
                  <label>
                    <input
                      type="checkbox"
                      checked={rolesMarcados.has(rol.codigo)}
                      onChange={() => setRolesMarcados(alternar(rolesMarcados, rol.codigo))}
                    />
                    <strong>{rol.codigo}</strong> — {rol.nombre}
                  </label>
                </li>
              ))}
            </ul>
            <div className="panel-formulario__acciones">
              <button type="button" onClick={guardarRolesUsuario} disabled={guardandoRolesUsuario}>
                {guardandoRolesUsuario ? "Guardando..." : "Guardar roles"}
              </button>
              <button
                type="button"
                className="boton-secundario"
                onClick={() => setIdUsuarioEditando(null)}
              >
                Cancelar
              </button>
            </div>
            {errorRolesUsuario && <p className="mensaje-error">{errorRolesUsuario}</p>}
          </div>
        )}
      </section>
    </div>
  );
}
