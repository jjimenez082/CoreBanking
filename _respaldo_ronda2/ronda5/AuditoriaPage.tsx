import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import { fmtFechaHora, mensajeError } from "../api/mensajes";
import type { AuditoriaRegistro, AuditoriaPagina } from "../api/tipos";

interface UsuarioItem { id: number; nombre: string }
interface Filtros { entidad: string; llave: string; idUsuario: number; accion: string; desde: string; hasta: string; idGestion: string; texto: string }
const SIN_FILTROS: Filtros = { entidad: "", llave: "", idUsuario: 0, accion: "", desde: "", hasta: "", idGestion: "", texto: "" };

/**
 * Administración › Consulta de auditoría (permiso AUD_CONSULTA). Bitácora de cambios: quién, cuándo, qué entidad y registro,
 * qué campo cambió (valor anterior y nuevo) y la gestión que lo aprobó. Solo lectura. Las horas se muestran en la zona del navegador.
 */
export function AuditoriaPage() {
  const api = useApiClient();
  const [entidades, setEntidades] = useState<string[]>([]);
  const [usuarios, setUsuarios] = useState<UsuarioItem[]>([]);
  const [borrador, setBorrador] = useState<Filtros>(SIN_FILTROS);
  const [filtros, setFiltros] = useState<Filtros>(SIN_FILTROS);
  const [pagina, setPagina] = useState(1);
  const [datos, setDatos] = useState<AuditoriaPagina | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sel, setSel] = useState<Set<string | number>>(new Set());

  useEffect(() => {
    api.get<string[]>("/api/auditoria/entidades").then((r) => setEntidades(r.data)).catch(() => undefined);
    api.get<UsuarioItem[]>("/api/catalogos/usuarios").then((r) => setUsuarios(r.data)).catch(() => undefined);
  }, [api]);

  const buscar = useCallback(async () => {
    setCargando(true); setError(null);
    try {
      const r = await api.get<AuditoriaPagina>("/api/auditoria", {
        params: {
          entidad: filtros.entidad || undefined, llave: filtros.llave || undefined, idUsuario: filtros.idUsuario || undefined,
          accion: filtros.accion || undefined, desde: filtros.desde || undefined, hasta: filtros.hasta || undefined,
          idGestion: filtros.idGestion || undefined, texto: filtros.texto || undefined, pagina, tamanoPagina: 50,
        },
      });
      setDatos(r.data); setSel(new Set());
    } catch (e) { setError(mensajeError(e, "No se pudo consultar la auditoría.")); }
    finally { setCargando(false); }
  }, [api, filtros, pagina]);
  useEffect(() => { void buscar(); }, [buscar]);

  const columnas: ColumnaTabla<AuditoriaRegistro>[] = useMemo(() => [
    { clave: "fecha", encabezado: "Fecha y hora", render: (r) => fmtFechaHora(r.fechaHora) },
    { clave: "usuario", encabezado: "Usuario", render: (r) => r.usuario, textoBusqueda: (r) => r.usuario },
    { clave: "entidad", encabezado: "Entidad", render: (r) => `${r.entidad} #${r.llave}`, textoBusqueda: (r) => `${r.entidad} ${r.llave}` },
    { clave: "accion", encabezado: "Acción", render: (r) => r.accion, textoBusqueda: (r) => r.accion },
    { clave: "campo", encabezado: "Campo", render: (r) => r.campo ?? "—", textoBusqueda: (r) => r.campo ?? "" },
    { clave: "ant", encabezado: "Valor anterior", render: (r) => r.anterior ?? "—", textoBusqueda: (r) => r.anterior ?? "" },
    { clave: "nue", encabezado: "Valor nuevo", render: (r) => r.nuevo ?? "—", textoBusqueda: (r) => r.nuevo ?? "" },
    { clave: "ges", encabezado: "Gestión", render: (r) => (r.idGestion ? <Link to="/gestiones">#{r.idGestion}</Link> : "—") },
  ], []);

  function aplicar(e: FormEvent) { e.preventDefault(); setPagina(1); setFiltros(borrador); }
  function limpiar() { setBorrador(SIN_FILTROS); setPagina(1); setFiltros(SIN_FILTROS); }

  const totalPaginas = datos ? Math.max(1, Math.ceil(datos.total / datos.tamanoPagina)) : 1;

  return (
    <div>
      <h1>Consulta de auditoría</h1>
      <p className="nota">
        Bitácora de cambios. En los cambios a clientes y similares, los valores que son listas se muestran como id; el detalle con nombres está en la gestión.
        Las acciones de consulta no se registran todavía.
      </p>
      {error && <p className="mensaje-error">{error}</p>}

      <form className="formulario" onSubmit={aplicar} style={{ marginBottom: "1rem" }}>
        <label>Entidad
          <select value={borrador.entidad} onChange={(e) => setBorrador({ ...borrador, entidad: e.target.value })}>
            <option value="">Todas</option>
            {entidades.map((x) => (<option key={x} value={x}>{x}</option>))}
          </select>
        </label>
        <label>N.º / llave del registro
          <input type="text" value={borrador.llave} onChange={(e) => setBorrador({ ...borrador, llave: e.target.value })} />
        </label>
        <label>Usuario
          <select value={borrador.idUsuario} onChange={(e) => setBorrador({ ...borrador, idUsuario: Number(e.target.value) })}>
            <option value={0}>Todos</option>
            {usuarios.map((u) => (<option key={u.id} value={u.id}>{u.nombre}</option>))}
          </select>
        </label>
        <label>Acción
          <select value={borrador.accion} onChange={(e) => setBorrador({ ...borrador, accion: e.target.value })}>
            <option value="">Todas</option><option>Alta</option><option>Cambio</option><option>Baja</option>
          </select>
        </label>
        <label>Desde
          <input type="date" value={borrador.desde} onChange={(e) => setBorrador({ ...borrador, desde: e.target.value })} />
        </label>
        <label>Hasta
          <input type="date" value={borrador.hasta} onChange={(e) => setBorrador({ ...borrador, hasta: e.target.value })} />
        </label>
        <label>Gestión N.º
          <input type="number" min={1} value={borrador.idGestion} onChange={(e) => setBorrador({ ...borrador, idGestion: e.target.value })} />
        </label>
        <label>Texto (campo o valor)
          <input type="text" value={borrador.texto} onChange={(e) => setBorrador({ ...borrador, texto: e.target.value })} />
        </label>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button type="submit">Buscar</button>
          <button type="button" className="boton-secundario" onClick={limpiar}>Limpiar</button>
        </div>
      </form>

      <TablaDatos
        columnas={columnas} filas={datos?.items ?? []} claveFila={(f) => f.idLog}
        seleccion={sel} onCambiarSeleccion={setSel} cargando={cargando}
        mensajeVacio="No hay registros de auditoría para el filtro."
        comandos={[{ clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void buscar() }]}
        pie={datos && (
          <div className="formulario-en-linea" style={{ alignItems: "center", marginTop: "0.5rem" }}>
            <button type="button" className="boton-secundario" disabled={pagina <= 1 || cargando} onClick={() => setPagina(pagina - 1)}>‹ Anterior</button>
            <span>Página {pagina} de {totalPaginas} · {datos.total.toLocaleString("es-CR")} registro(s)</span>
            <button type="button" className="boton-secundario" disabled={pagina >= totalPaginas || cargando} onClick={() => setPagina(pagina + 1)}>Siguiente ›</button>
          </div>
        )}
      />
    </div>
  );
}
