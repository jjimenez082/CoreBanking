import { PanelModal, VentanaModal } from "../components/VentanaModal";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { mensajeError } from "../api/mensajes";
import type { CatalogoItemResponse, IntegracionCredencial, IntegracionFila, UsuarioItem } from "../api/tipos";

/**
 * Administración › Usuarios de integración (permiso INT_ADMINISTRACION). Cada integración (CRM, onboarding digital, web, móvil...)
 * tiene un canal fijo: cuando da de alta un cliente por la API, el canal de origen lo asigna el servidor, no lo decide quien llama.
 */
export function IntegracionesPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<IntegracionFila[]>([]);
  const [canales, setCanales] = useState<CatalogoItemResponse[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());
  const [editando, setEditando] = useState(false);
  const [idCanal, setIdCanal] = useState(0);
  const [idUsuario, setIdUsuario] = useState(0);
  const [editandoUsuario, setEditandoUsuario] = useState(false);
  const [tecnicos, setTecnicos] = useState<UsuarioItem[]>([]);
  const [errorForm, setErrorForm] = useState<string | null>(null);
  const [nueva, setNueva] = useState<{ identificador: string; nombre: string; idCanal: number; idUsuario: number } | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [credencial, setCredencial] = useState<IntegracionCredencial | null>(null);
  const [keycloakOk, setKeycloakOk] = useState(true);
  const [codigoCompania, setCodigoCompania] = useState("");

  const cargar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const [i, c, t] = await Promise.all([
        api.get<IntegracionFila[]>("/api/integraciones"),
        api.get<CatalogoItemResponse[]>("/api/catalogos/listas/CANAL"),
        api.get<UsuarioItem[]>("/api/integraciones/usuarios-tecnicos"),
      ]);
      setFilas(i.data); setCanales(c.data); setTecnicos(t.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudieron cargar los usuarios de integración.")); }
    finally { setCargando(false); }
  }, [api]);
  useEffect(() => { void cargar(); }, [cargar]);
  useEffect(() => {
    api.get<{ keycloakConfigurado: boolean; codigoCompania: string }>("/api/integraciones/estado-integracion").then((r) => { setKeycloakOk(r.data.keycloakConfigurado); setCodigoCompania(r.data.codigoCompania); }).catch(() => undefined);
  }, [api]);

  const columnas: ColumnaTabla<IntegracionFila>[] = useMemo(() => [
    { clave: "nombre", encabezado: "Integración", render: (r) => r.nombre, textoBusqueda: (r) => r.nombre },
    { clave: "client", encabezado: "Client ID (Keycloak)", render: (r) => <code>{r.clientId}</code>, textoBusqueda: (r) => r.clientId },
    { clave: "canal", encabezado: "Canal fijo", render: (r) => r.canalNombre ?? <em>Sin canal (no puede dar altas)</em>, textoBusqueda: (r) => r.canalNombre ?? "" },
    { clave: "usuario", encabezado: "Usuario asociado", render: (r) => r.usuarioNombre ?? <em>Sin usuario (no puede operar)</em>, textoBusqueda: (r) => r.usuarioNombre ?? "" },
    { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
  ], []);

  const idSel = sel.size === 1 ? Number(Array.from(sel)[0]) : null;
  const fila = filas.find((f) => f.idUsuarioIntegracion === idSel) ?? null;

  async function crear(e: FormEvent) {
    e.preventDefault();
    if (!nueva) return;
    setGuardando(true); setErrorForm(null); setCredencial(null);
    try {
      const r = await api.post<IntegracionCredencial>("/api/integraciones", nueva);
      setCredencial(r.data); setExito("Integración creada. Entregue el client_id y el secret a quien la va a usar."); setNueva(null);
      await cargar();
    } catch (err) { setErrorForm(mensajeError(err, "No se pudo crear la integración.")); }
    finally { setGuardando(false); }
  }

  async function regenerar() {
    if (!fila) return;
    if (!window.confirm(`¿Regenerar el secret de ${fila.nombre}? El secret actual dejará de funcionar de inmediato y la integración dejará de conectarse hasta que use el nuevo.`)) return;
    setExito(null); setError(null); setCredencial(null);
    try {
      const r = await api.post<IntegracionCredencial>(`/api/integraciones/${fila.idUsuarioIntegracion}/regenerar-secret`);
      setCredencial(r.data); setExito("Secret regenerado.");
    } catch (err) { setError(mensajeError(err, "No se pudo regenerar el secret.")); }
  }

  async function cambiarEstado(estado: "Activo" | "Inactivo") {
    if (!fila) return;
    if (estado === "Inactivo" && !window.confirm(`¿Inactivar ${fila.nombre}? Dejará de poder llamar a la API.`)) return;
    setExito(null); setError(null);
    try {
      await api.put(`/api/integraciones/${fila.idUsuarioIntegracion}/estado`, { estado });
      setExito(estado === "Activo" ? "Integración activada." : "Integración inactivada."); await cargar();
    } catch (err) { setError(mensajeError(err, "No se pudo cambiar el estado.")); }
  }

  const copiar = (t: string) => void navigator.clipboard?.writeText(t);

  async function guardar(e: FormEvent) {
    e.preventDefault();
    if (!fila) return;
    setErrorForm(null);
    try { await api.put(`/api/integraciones/${fila.idUsuarioIntegracion}/canal`, { idCanal }); setExito("Canal asignado."); setEditando(false); await cargar(); }
    catch (err) { setErrorForm(mensajeError(err, "No se pudo asignar el canal.")); }
  }

  async function guardarUsuario(e: FormEvent) {
    e.preventDefault();
    if (!fila) return;
    setErrorForm(null);
    try { await api.put(`/api/integraciones/${fila.idUsuarioIntegracion}/usuario`, { idUsuario }); setExito("Usuario asociado actualizado."); setEditandoUsuario(false); await cargar(); }
    catch (err) { setErrorForm(mensajeError(err, "No se pudo asignar el usuario.")); }
  }

  return (
    <div>
      <h1>Usuarios de integración</h1>
      <p className="nota">
        Cuentas de servicio que llaman a la API con <strong>client_id y client_secret</strong> (OAuth 2.0 Client Credentials). Al crearlas se registran también en Keycloak,
        con el código de la compañía en el token. El <strong>usuario asociado</strong> (usuario técnico de tipo Integración, definido en Usuarios) determina qué puede hacer: sus permisos salen de sus roles, y sin permiso la API responde 403. Varias integraciones pueden compartir un usuario o tener cada una el suyo. El canal fijo se asigna al cliente al darlo de alta por esa integración. Los canales se mantienen en Listas de Valores (CANAL).
      </p>
      {!keycloakOk && <p className="mensaje-error">Keycloak:Admin no está configurado en la API: no se pueden crear integraciones desde esta pantalla.</p>}
      {credencial && (
        <VentanaModal titulo="Credenciales de la integración" onCerrar={() => setCredencial(null)}>
        <div className="tarjeta-resultado">
          <p style={{ marginTop: 0 }}><strong>Credenciales de la integración.</strong> El secret se muestra <strong>una sola vez</strong>: cópielo ahora y entréguelo por un medio seguro.</p>
          <p><strong>Client ID:</strong> <code>{credencial.clientId}</code> <button type="button" className="boton-secundario" onClick={() => copiar(credencial.clientId)}>Copiar</button></p>
          <p><strong>Client Secret:</strong> <code style={{ fontSize: "1.05rem" }}>{credencial.clientSecret}</code> <button type="button" className="boton-secundario" onClick={() => copiar(credencial.clientSecret)}>Copiar</button></p>
          <p><strong>URL del token:</strong> <code>{credencial.tokenUrl}</code> <button type="button" className="boton-secundario" onClick={() => copiar(credencial.tokenUrl)}>Copiar</button></p>
          <p className="nota" style={{ marginBottom: 0 }}>Uso: POST a la URL del token con <code>grant_type=client_credentials</code>, <code>client_id</code> y <code>client_secret</code>; luego llamar a la API con <code>Authorization: Bearer &lt;access_token&gt;</code>.</p>
        </div>
        </VentanaModal>
      )}
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}
      <TablaDatos
        columnas={columnas} filas={filas} claveFila={(f) => f.idUsuarioIntegracion}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay usuarios de integración registrados."
        comandos={[
          { clave: "nueva", etiqueta: "Nueva integración", icono: "+", deshabilitado: !keycloakOk, onClick: () => { setExito(null); setErrorForm(null); setCredencial(null); setEditando(false); setEditandoUsuario(false); setNueva({ identificador: "", nombre: "", idCanal: 0, idUsuario: 0 }); } },
          { clave: "canal", etiqueta: "Asignar canal", icono: "✎", requiereSeleccion: "una", onClick: () => { if (!fila) return; setExito(null); setErrorForm(null); setIdCanal(fila.idCanal ?? 0); setEditandoUsuario(false); setEditando(true); } },
          { clave: "usuario", etiqueta: "Asignar usuario", icono: "👤", requiereSeleccion: "una", onClick: () => { if (!fila) return; setExito(null); setErrorForm(null); setIdUsuario(fila.idUsuario ?? 0); setEditando(false); setEditandoUsuario(true); } },
          { clave: "regenerar", etiqueta: "Regenerar secret", icono: "⟳", requiereSeleccion: "una", deshabilitado: !keycloakOk, onClick: () => void regenerar() },
          { clave: "activar", etiqueta: "Activar", requiereSeleccion: "una", oculto: fila?.estado !== "Inactivo", onClick: () => void cambiarEstado("Activo") },
          { clave: "inactivar", etiqueta: "Inactivar", requiereSeleccion: "una", oculto: fila?.estado !== "Activo", onClick: () => void cambiarEstado("Inactivo") },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
      {nueva && (
        <PanelModal onCerrar={() => setNueva(null)}>
          <h3>Nueva integración</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={crear}>
            <label>Nombre
              <input value={nueva.nombre} maxLength={200} required placeholder="Ej. CRM de ventas" onChange={(e) => setNueva({ ...nueva, nombre: e.target.value })} />
            </label>
            <label>Identificador
              <input value={nueva.identificador} maxLength={50} required placeholder="Ej. crm-ventas"
                onChange={(e) => setNueva({ ...nueva, identificador: e.target.value.toLowerCase().replace(/[^a-z0-9_-]/g, "") })} />
            </label>
            <p className="nota">Minúsculas, números, guion o guion bajo. El Client ID final lleva delante el código de la compañía{codigoCompania ? <>: <code>{codigoCompania}-{nueva.identificador || "…"}</code></> : "."}</p>
            <label>Canal fijo
              <select value={nueva.idCanal} onChange={(e) => setNueva({ ...nueva, idCanal: Number(e.target.value) })} required>
                <option value={0}>Seleccione...</option>
                {canales.map((c) => (<option key={c.id} value={c.id}>{c.nombre}</option>))}
              </select>
            </label>
            <label>Usuario asociado
              <select value={nueva.idUsuario} onChange={(e) => setNueva({ ...nueva, idUsuario: Number(e.target.value) })} required>
                <option value={0}>Seleccione...</option>
                {tecnicos.map((t) => (<option key={t.id} value={t.id}>{t.nombre}</option>))}
              </select>
            </label>
            {tecnicos.length === 0 && <p className="mensaje-error">No hay usuarios técnicos activos. Créelos en Usuarios con tipo «Integración».</p>}
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={guardando || !nueva.idCanal || !nueva.idUsuario}>{guardando ? "Creando..." : "Crear integración"}</button>
              <button type="button" className="boton-secundario" onClick={() => setNueva(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
      {editandoUsuario && fila && (
        <PanelModal onCerrar={() => setEditandoUsuario(false)}>
          <h3>Usuario asociado de {fila.nombre}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardarUsuario}>
            <label>Usuario técnico
              <select value={idUsuario} onChange={(e) => setIdUsuario(Number(e.target.value))} required>
                <option value={0}>Seleccione...</option>
                {tecnicos.map((t) => (<option key={t.id} value={t.id}>{t.nombre}</option>))}
              </select>
            </label>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={!idUsuario}>Guardar</button>
              <button type="button" className="boton-secundario" onClick={() => setEditandoUsuario(false)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
      {editando && fila && (
        <PanelModal onCerrar={() => setEditando(false)}>
          <h3>Canal de {fila.nombre}</h3>
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>Canal
              <select value={idCanal} onChange={(e) => setIdCanal(Number(e.target.value))} required>
                <option value={0}>Seleccione...</option>
                {canales.map((c) => (<option key={c.id} value={c.id}>{c.nombre}</option>))}
              </select>
            </label>
            <div style={{ display: "flex", gap: "0.5rem" }}>
              <button type="submit" disabled={!idCanal}>Guardar</button>
              <button type="button" className="boton-secundario" onClick={() => setEditando(false)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}
