import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, InterlocutorListaResponse, InterlocutorResponse } from "../api/tipos";

const TAMANO_PAGINA = 20;

const TIPOS: Record<string, { icono: string; etiqueta: string }> = {
  Fisica: { icono: "👤", etiqueta: "Persona" },
  Juridica: { icono: "🏢", etiqueta: "Organización" },
  Grupo: { icono: "🔗", etiqueta: "Grupo (organizaciones y/o personas)" },
};

const COLUMNAS: ColumnaTabla<InterlocutorResponse>[] = [
  {
    clave: "tipo", encabezado: "Tipo",
    render: (f) => {
      const t = TIPOS[f.tipoPersona] ?? { icono: "•", etiqueta: f.tipoPersona };
      return (<span title={t.etiqueta} aria-label={t.etiqueta} style={{ fontSize: "1.2em" }}>{t.icono}</span>);
    },
  },
  { clave: "numero", encabezado: "N.º", render: (f) => (<Link to={`/interlocutores/${f.idInterlocutor}`} title="Abrir la vista 360°">{f.numero}</Link>) },
  { clave: "nombre", encabezado: "Nombre completo", render: (f) => f.nombreCompleto },
  { clave: "identificacion", encabezado: "Identificación principal", render: (f) => f.numeroIdentificacion },
  { clave: "roles", encabezado: "Roles", render: (f) => f.roles || "—" },
  { clave: "estado", encabezado: "Estado", render: (f) => f.estado },
  { clave: "creado", encabezado: "Creado", render: (f) => new Date(f.creadoEn).toLocaleDateString() },
];

/** Listado de interlocutores. Todo registro se abre en la Consulta 360°. */
export function InterlocutoresConsultaPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const { cargando: cargandoPermisos, tienePermiso } = usePermisos();
  const [params, setParams] = useSearchParams();
  const texto = params.get("q") ?? "";
  const tipo = params.get("tipo") ?? "";

  const [pagina, setPagina] = useState(1);
  const [resultado, setResultado] = useState<InterlocutorListaResponse | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());

  const puedeConsultar = tienePermiso("CLI_CONSULTA");
  const puedeAlta = tienePermiso("CLI_ALTA");
  const puedeEditar = tienePermiso("CLI_EDICION");

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const r = await api.get<InterlocutorListaResponse>("/api/interlocutores", {
        params: { q: texto || undefined, tipo: tipo || undefined, pagina, tamanoPagina: TAMANO_PAGINA },
      });
      setResultado(r.data);
      setSeleccion(new Set());
    } catch (err) {
      setError(
        isAxiosError<ApiErrorBody>(err) && err.response?.data?.mensaje
          ? err.response.data.mensaje
          : "No se pudo cargar el listado.",
      );
    } finally {
      setCargando(false);
    }
  }, [api, texto, tipo, pagina]);

  useEffect(() => {
    if (puedeConsultar) void cargar();
  }, [puedeConsultar, cargar]);

  const id = seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
  const seleccionada = id !== null ? resultado?.items.find((x) => x.idInterlocutor === id) : undefined;
  const noModificable = !!seleccionada && seleccionada.estado !== "Activo";
  const totalPaginas = resultado ? Math.max(1, Math.ceil(resultado.total / resultado.tamanoPagina)) : 1;

  if (cargandoPermisos) return <p className="nota">Cargando...</p>;
  if (!puedeConsultar) return <p className="mensaje-error">No tiene permiso para consultar interlocutores.</p>;

  return (
    <div>
      <h1>Consulta de interlocutores</h1>
      {error && <p className="mensaje-error">{error}</p>}

      <div style={{ margin: "8px 0", display: "flex", alignItems: "center", gap: 8 }}>
        <label htmlFor="filtro-tipo" className="nota">Tipo:</label>
        <select id="filtro-tipo" value={tipo}
          onChange={(e) => {
            setPagina(1);
            const p: Record<string, string> = {};
            if (texto) p.q = texto;
            if (e.target.value) p.tipo = e.target.value;
            setParams(p);
          }}>
          <option value="">Todos</option>
          <option value="Fisica">Persona</option>
          <option value="Juridica">Organización</option>
          <option value="Grupo">Grupo</option>
        </select>
      </div>
      <TablaDatos
        key={texto}
        columnas={COLUMNAS}
        filas={resultado?.items ?? []}
        claveFila={(f) => f.idInterlocutor}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        mensajeVacio="No se encontraron interlocutores."
        placeholderBusqueda="Buscar por nombre, N.º o identificación"
        busquedaServidor={{
          valorInicial: texto,
          onBuscar: (t) => {
            setPagina(1);
            setParams({ ...(t.trim() ? { q: t.trim() } : {}), ...(tipo ? { tipo } : {}) });
          },
        }}
        comandos={[
          { clave: "nuevo", etiqueta: "Nueva persona", icono: "+", oculto: !puedeAlta, onClick: () => navigate("/interlocutores/clientes/nuevo") },
          { clave: "nuevaOrg", etiqueta: "Nueva organización", icono: "+", oculto: !puedeAlta, onClick: () => navigate("/interlocutores/clientes/nuevo?tipo=juridica") },
          { clave: "ver", etiqueta: "Ver 360°", icono: "👁", requiereSeleccion: "una", onClick: () => id !== null && navigate(`/interlocutores/${id}`) },
          {
            clave: "editar",
            etiqueta: "Modificar datos",
            icono: "✎",
            oculto: !puedeEditar,
            deshabilitado: noModificable,
            requiereSeleccion: "una",
            onClick: () => id !== null && navigate(`/interlocutores/clientes/${id}/modificar`),
          },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
        pie={
          resultado && (
            <div className="paginacion">
              <button type="button" disabled={pagina <= 1 || cargando} onClick={() => setPagina((p) => p - 1)}>← Anterior</button>
              <span className="nota">Página {resultado.pagina} de {totalPaginas} ({resultado.total} en total)</span>
              <button type="button" disabled={pagina >= totalPaginas || cargando} onClick={() => setPagina((p) => p + 1)}>Siguiente →</button>
            </div>
          )
        }
      />
      <p className="nota"><Link to="/interlocutores">← Volver al inicio de Interlocutores</Link></p>
    </div>
  );
}
