import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { ArbolPermisos } from "../components/ArbolPermisos";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import type { ObjetoAutorizacionResponse, RolCrearRequest, RolResponse, UsuarioAdmin } from "../api/tipos";

type Modo = "ver" | "editar";
type Pestana = "rol" | "usuarios";

/**
 * Seguridad (RBAC, SEG-002): lista de roles. El nombre del rol abre su ficha (solo lectura, con «Editar»);
 * «Editar» en la barra de comandos entra directo a editar. La ficha tiene dos pestañas:
 *   - Rol: datos y permisos.
 *   - Usuarios: quién tiene el rol; en edición se pueden agregar o quitar usuarios.
 * Todo se guarda junto con «Guardar». Los usuarios se administran en su propia pantalla (Usuarios).
 */
export function SeguridadPage() {
  const api = useApiClient();
  const { cargando: cargandoPermisos, tienePermiso } = usePermisos();
  const puedeAdministrar = tienePermiso("SEG_ADMINISTRACION");

  const [objetos, setObjetos] = useState<ObjetoAutorizacionResponse[]>([]);
  const [roles, setRoles] = useState<RolResponse[]>([]);
  const [usuarios, setUsuarios] = useState<UsuarioAdmin[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());

  const [mostrarNuevo, setMostrarNuevo] = useState(false);
  const [nuevoCodigo, setNuevoCodigo] = useState("");
  const [nuevoNombre, setNuevoNombre] = useState("");
  const [creando, setCreando] = useState(false);
  const [errorNuevo, setErrorNuevo] = useState<string | null>(null);
  const [mostrarCopia, setMostrarCopia] = useState(false);
  const [copiaNombre, setCopiaNombre] = useState("");
  const [copiando, setCopiando] = useState(false);
  const [errorCopia, setErrorCopia] = useState<string | null>(null);

  // Ficha abierta
  const [idRol, setIdRol] = useState<number | null>(null);
  const [modo, setModo] = useState<Modo>("ver");
  const [pestana, setPestana] = useState<Pestana>("rol");
  const [permisos, setPermisos] = useState<Set<string>>(new Set());
  const [miembros, setMiembros] = useState<Set<number>>(new Set());
  const [aAgregar, setAAgregar] = useState(0);
  const [selMiembros, setSelMiembros] = useState<Set<string | number>>(new Set());
  const [guardando, setGuardando] = useState(false);
  const [errorFicha, setErrorFicha] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [o, r, u] = await Promise.all([
        api.get<ObjetoAutorizacionResponse[]>("/api/seguridad/objetos-autorizacion"),
        api.get<RolResponse[]>("/api/seguridad/roles"),
        api.get<UsuarioAdmin[]>("/api/usuarios"),
      ]);
      setObjetos(o.data); setRoles(r.data); setUsuarios(u.data);
    } catch (e) { setError(mensajeError(e, "No se pudo cargar el módulo de Seguridad.")); }
    finally { setCargando(false); }
  }, [api]);

  useEffect(() => {
    if (cargandoPermisos || !puedeAdministrar) return;
    void cargar();
  }, [cargandoPermisos, puedeAdministrar, cargar]);

  const rol = roles.find((r) => r.idRolAutorizacion === idRol) ?? null;
  const usuariosConRol = (codigo: string) => usuarios.filter((u) => u.codigosRol.includes(codigo));

  function abrir(r: RolResponse, m: Modo) {
    setExito(null); setErrorFicha(null);
    setIdRol(r.idRolAutorizacion); setModo(m); setPestana("rol");
    setPermisos(new Set(r.codigosObjetoAutorizacion));
    setMiembros(new Set(usuariosConRol(r.codigo).map((u) => u.idUsuario)));
    setAAgregar(0); setSelMiembros(new Set());
  }

  function cerrar() { setIdRol(null); setSel(new Set()); }

  function cancelarEdicion() {
    if (rol) abrir(rol, "ver");
  }

  const columnas: ColumnaTabla<RolResponse>[] = useMemo(() => [
    {
      clave: "codigo", encabezado: "Código", textoBusqueda: (r) => r.codigo,
      render: (r) => <button type="button" className="boton-enlace-id" onClick={() => abrir(r, "ver")}>{r.codigo}</button>,
    },
    { clave: "nombre", encabezado: "Nombre", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
    { clave: "permisos", encabezado: "Permisos asignados", render: (r) => r.codigosObjetoAutorizacion.length, textoBusqueda: (r) => r.codigosObjetoAutorizacion.join(" ") },
    { clave: "usuarios", encabezado: "Usuarios", render: (r) => usuariosConRol(r.codigo).length, textoBusqueda: (r) => usuariosConRol(r.codigo).map((u) => u.nombreCompleto).join(" ") },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [usuarios, roles]);

  async function crearRol(e: FormEvent) {
    e.preventDefault();
    setCreando(true); setErrorNuevo(null);
    try {
      const cuerpo: RolCrearRequest = { codigo: nuevoCodigo, nombre: nuevoNombre };
      await api.post<RolResponse>("/api/seguridad/roles", cuerpo);
      setNuevoCodigo(""); setNuevoNombre(""); setMostrarNuevo(false);
      setExito("Rol creado. Ábralo para asignarle permisos.");
      await cargar();
    } catch (err) { setErrorNuevo(mensajeError(err, "No se pudo crear el rol.")); }
    finally { setCreando(false); }
  }

  async function copiarRol(e: FormEvent) {
    e.preventDefault();
    if (!filaSel) return;
    setCopiando(true); setErrorCopia(null);
    try {
      const r = await api.post<RolResponse>(`/api/seguridad/roles/${filaSel.idRolAutorizacion}/copiar`, { nombre: copiaNombre.trim() });
      setMostrarCopia(false); setCopiaNombre(""); setSel(new Set());
      setExito(`Rol «${r.data.nombre}» creado (código ${r.data.codigo}) con los ${r.data.codigosObjetoAutorizacion.length} permisos de ${filaSel.codigo}. Ábralo para ajustarlos.`);
      await cargar();
    } catch (err) { setErrorCopia(mensajeError(err, "No se pudo copiar el rol.")); }
    finally { setCopiando(false); }
  }

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const filaSel = roles.find((r) => r.idRolAutorizacion === idSel) ?? null;

  async function guardar() {
    if (!rol) return;
    setGuardando(true); setErrorFicha(null);
    try {
      const antes = new Set(usuariosConRol(rol.codigo).map((u) => u.idUsuario));
      const agregados = [...miembros].filter((id) => !antes.has(id));
      const quitados = [...antes].filter((id) => !miembros.has(id));

      // Quitar primero (puede liberar la regla del ultimo administrador), luego agregar.
      for (const id of [...quitados, ...agregados]) {
        const u = usuarios.find((x) => x.idUsuario === id);
        if (!u) continue;
        const codigos = quitados.includes(id) ? u.codigosRol.filter((c) => c !== rol.codigo) : [...u.codigosRol, rol.codigo];
        await api.put(`/api/usuarios/${id}/roles`, { codigosRol: codigos });
      }
      const cambioPermisos = permisos.size !== rol.codigosObjetoAutorizacion.length || [...permisos].some((p) => !rol.codigosObjetoAutorizacion.includes(p));
      if (cambioPermisos) await api.put(`/api/seguridad/roles/${rol.idRolAutorizacion}/objetos`, { codigosObjetoAutorizacion: [...permisos] });

      setExito("Rol guardado.");
      await cargar();
      setModo("ver");
    } catch (err) { setErrorFicha(mensajeError(err, "No se pudo guardar el rol.")); await cargar(); }
    finally { setGuardando(false); }
  }

  if (cargandoPermisos) return (<div><h1>Seguridad</h1><p className="nota">Verificando permisos...</p></div>);
  if (!puedeAdministrar) return (
    <div>
      <h1>Seguridad</h1>
      <p className="mensaje-error">No tienes el permiso <strong>SEG_ADMINISTRACION</strong> para ver esta sección. Pídele a un administrador que te asigne un rol con ese permiso (RBAC, SEG-002).</p>
    </div>
  );

  // ---------------------------------------------------------------- Ficha del rol
  if (rol) {
    const editando = modo === "editar";
    const filasMiembros = usuarios.filter((u) => miembros.has(u.idUsuario));
    const candidatos = usuarios.filter((u) => u.estado === "Activo" && !miembros.has(u.idUsuario));
    const colsMiembros: ColumnaTabla<UsuarioAdmin>[] = [
      { clave: "nombre", encabezado: "Nombre", render: (u) => u.nombreCompleto, textoBusqueda: (u) => u.nombreCompleto },
      { clave: "tipo", encabezado: "Tipo", render: (u) => (u.tipoUsuario === "INTEGRACION" ? "Integración" : "Físico"), textoBusqueda: (u) => u.tipoUsuario },
      { clave: "usuario", encabezado: "Usuario", render: (u) => u.username ?? "—", textoBusqueda: (u) => u.username ?? "" },
      { clave: "unidad", encabezado: "Unidad", render: (u) => u.unidadNombre ?? "—", textoBusqueda: (u) => u.unidadNombre ?? "" },
      { clave: "estado", encabezado: "Estado", render: (u) => u.estado, textoBusqueda: (u) => u.estado },
    ];

    return (
      <div>
        <p className="nota"><button type="button" className="boton-enlace" onClick={cerrar}>← Roles</button></p>
        <h1>{rol.nombre} <span className="nota">({rol.codigo})</span></h1>
        {error && <p className="mensaje-error">{error}</p>}
        {exito && <p className="mensaje-exito">{exito}</p>}
        {errorFicha && <p className="mensaje-error">{errorFicha}</p>}

        <div style={{ display: "flex", gap: "0.5rem" }}>
          {!editando && <button type="button" onClick={() => { setExito(null); setModo("editar"); }}>Editar</button>}
          {editando && <button type="button" onClick={() => void guardar()} disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>}
          {editando && <button type="button" className="boton-secundario" onClick={cancelarEdicion} disabled={guardando}>Cancelar</button>}
        </div>

        <div className="v360__pestanas">
          <button type="button" className={`v360__pestana ${pestana === "rol" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("rol")}>Rol</button>
          <button type="button" className={`v360__pestana ${pestana === "usuarios" ? "v360__pestana--activa" : ""}`} onClick={() => setPestana("usuarios")}>Usuarios ({miembros.size})</button>
        </div>

        {pestana === "rol" && (
          <section>
            <dl className="ficha-datos" style={{ display: "grid", gridTemplateColumns: "max-content 1fr", gap: "0.25rem 1rem" }}>
              <dt>Código</dt><dd>{rol.codigo}</dd>
              <dt>Nombre</dt><dd>{rol.nombre}</dd>
              <dt>Estado</dt><dd>{rol.estado}</dd>
            </dl>
            <h3>Permisos</h3>
            <ArbolPermisos objetos={objetos} marcados={permisos} onCambiar={setPermisos} soloLectura={!editando} />
          </section>
        )}

        {pestana === "usuarios" && (
          <section>
            {editando && (
              <div className="formulario-en-linea" style={{ marginBottom: "0.75rem" }}>
                <select value={aAgregar} onChange={(e) => setAAgregar(Number(e.target.value))}>
                  <option value={0}>Seleccione un usuario para agregar...</option>
                  {candidatos.map((u) => (
                    <option key={u.idUsuario} value={u.idUsuario}>{u.nombreCompleto}{u.tipoUsuario === "INTEGRACION" ? " (integración)" : ""}</option>
                  ))}
                </select>
                <button type="button" disabled={!aAgregar} onClick={() => { setMiembros(new Set([...miembros, aAgregar])); setAAgregar(0); }}>Agregar</button>
              </div>
            )}
            <TablaDatos
              columnas={colsMiembros} filas={filasMiembros} claveFila={(u) => u.idUsuario}
              seleccion={selMiembros} onCambiarSeleccion={setSelMiembros}
              mensajeVacio="Ningún usuario tiene este rol." placeholderBusqueda="Buscar usuario"
              comandos={editando ? [
                {
                  clave: "quitar", etiqueta: "Quitar del rol", icono: "−", requiereSeleccion: "varias",
                  onClick: () => { const n = new Set(miembros); selMiembros.forEach((k) => n.delete(Number(k))); setMiembros(n); setSelMiembros(new Set()); },
                },
              ] : []}
            />
            {editando && <p className="nota">Los cambios de usuarios se aplican al presionar Guardar.</p>}
          </section>
        )}
      </div>
    );
  }

  // ---------------------------------------------------------------- Lista de roles
  return (
    <div>
      <h1>Seguridad</h1>
      <p className="nota">
        Roles y sus permisos (RBAC, SEG-002). Abra un rol con su código para verlo, o márquelo y use «Editar». Los usuarios se mantienen en la pantalla Usuarios.
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <TablaDatos
        columnas={columnas} filas={roles} claveFila={(r) => r.idRolAutorizacion}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay roles creados." placeholderBusqueda="Buscar rol"
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", onClick: () => { setMostrarCopia(false); setMostrarNuevo((v) => !v); } },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: () => { if (filaSel) abrir(filaSel, "editar"); } },
          { clave: "copiar", etiqueta: "Copiar rol", icono: "⧉", requiereSeleccion: "una",
            onClick: () => { if (filaSel) { setMostrarNuevo(false); setErrorCopia(null); setExito(null); setCopiaNombre(""); setMostrarCopia(true); } } },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {mostrarCopia && filaSel && (
        <div className="panel-formulario">
          <h3>Copiar rol {filaSel.codigo}</h3>
          <p className="nota">Se crea un rol nuevo con los {filaSel.codigosObjetoAutorizacion.length} permisos de «{filaSel.nombre}». El código se genera a partir del nombre y los usuarios no se copian.</p>
          <form onSubmit={copiarRol} className="formulario-en-linea">
            <input type="text" autoFocus maxLength={200} placeholder="Nombre del nuevo rol" value={copiaNombre} onChange={(e) => setCopiaNombre(e.target.value)} required />
            <button type="submit" disabled={copiando || copiaNombre.trim() === ""}>{copiando ? "Copiando..." : "Crear copia"}</button>
            <button type="button" className="boton-secundario" onClick={() => setMostrarCopia(false)}>Cancelar</button>
          </form>
          {errorCopia && <p className="mensaje-error">{errorCopia}</p>}
        </div>
      )}

      {mostrarNuevo && (
        <div className="panel-formulario">
          <h3>Nuevo rol</h3>
          <form onSubmit={crearRol} className="formulario-en-linea">
            <input type="text" placeholder="Código (ej. ANALISTA_CREDITO)" value={nuevoCodigo} onChange={(e) => setNuevoCodigo(e.target.value)} required />
            <input type="text" placeholder="Nombre descriptivo" value={nuevoNombre} onChange={(e) => setNuevoNombre(e.target.value)} required />
            <button type="submit" disabled={creando}>{creando ? "Creando..." : "Crear rol"}</button>
            <button type="button" className="boton-secundario" onClick={() => setMostrarNuevo(false)}>Cancelar</button>
          </form>
          {errorNuevo && <p className="mensaje-error">{errorNuevo}</p>}
        </div>
      )}
    </div>
  );
}
