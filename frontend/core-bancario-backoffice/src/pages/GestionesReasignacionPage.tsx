import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, ReasignacionFilaResponse, ReasignacionResultadoResponse, UsuarioAprobadorResponse } from "../api/tipos";

function fmtFechaHora(s: string): string {
  const d = new Date(/(Z|[+-]\d\d:?\d\d)$/.test(s) ? s : `${s}Z`);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

/**
 * App "Cambio de asignación de aprobador": se elige al aprobador que no está disponible, se marcan las gestiones
 * pendientes que tiene asignadas y se pasan a otro usuario con permiso de aprobar. Exige motivo y queda en auditoría.
 */
export function GestionesReasignacionPage() {
  const api = useApiClient();
  const [aprobadores, setAprobadores] = useState<UsuarioAprobadorResponse[]>([]);
  const [idActual, setIdActual] = useState<number>(0);
  const [idNuevo, setIdNuevo] = useState<number>(0);
  const [motivo, setMotivo] = useState("");
  const [filas, setFilas] = useState<ReasignacionFilaResponse[]>([]);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());
  const [cargando, setCargando] = useState(false);
  const [procesando, setProcesando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  function msg(err: unknown, porDefecto: string) {
    if (isAxiosError<ApiErrorBody>(err)) return err.response?.data?.mensaje ?? porDefecto;
    return porDefecto;
  }

  useEffect(() => {
    api.get<UsuarioAprobadorResponse[]>("/api/gestiones/reasignacion/aprobadores")
      .then((r) => setAprobadores(r.data))
      .catch((e) => setError(msg(e, "No se pudo cargar la lista de aprobadores.")));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function cargar(id: number) {
    setError(null);
    setSeleccion(new Set());
    if (!id) { setFilas([]); return; }
    setCargando(true);
    try {
      const r = await api.get<ReasignacionFilaResponse[]>("/api/gestiones/reasignacion", { params: { idUsuario: id } });
      setFilas(r.data);
    } catch (e) {
      setError(msg(e, "No se pudieron cargar las gestiones."));
    } finally {
      setCargando(false);
    }
  }

  async function reasignar(ids: number[]) {
    setError(null);
    setAviso(null);
    setProcesando(true);
    try {
      const r = await api.post<ReasignacionResultadoResponse>("/api/gestiones/reasignacion", {
        idsGestion: ids, idUsuarioNuevo: idNuevo, motivo,
      });
      const nuevo = aprobadores.find((a) => a.idUsuario === idNuevo)?.nombre ?? "";
      const rech = r.data.rechazadas;
      if (r.data.reasignadas > 0) setAviso(`${r.data.reasignadas} gestión(es) reasignada(s) a ${nuevo}.`);
      if (rech.length > 0) setError(rech.map((x) => `${x.gestion ? `Gestión ${x.gestion}: ` : ""}${x.motivo}`).join("\n"));
      setMotivo("");
      await cargar(idActual);
    } catch (e) {
      setError(msg(e, "No se pudo reasignar."));
    } finally {
      setProcesando(false);
    }
  }

  const columnas: ColumnaTabla<ReasignacionFilaResponse>[] = useMemo(() => [
    { clave: "n", encabezado: "N.º", render: (g) => g.numero, textoBusqueda: (g) => g.numero, valorOrden: (g) => g.idGestion },
    { clave: "tipo", encabezado: "Tipo", render: (g) => g.tipo, textoBusqueda: (g) => g.tipo, filtroLista: true },
    { clave: "reg", encabezado: "Registro", render: (g) => g.registro, textoBusqueda: (g) => g.registro },
    { clave: "sol", encabezado: "Ingresó", render: (g) => g.solicitadoPor, textoBusqueda: (g) => g.solicitadoPor, filtroLista: true },
    { clave: "niv", encabezado: "Nivel", render: (g) => `${g.nivel} de ${g.nivelesRequeridos}`, valorOrden: (g) => g.nivel },
    { clave: "fec", encabezado: "Fecha", render: (g) => fmtFechaHora(g.creadoEn), valorOrden: (g) => g.creadoEn },
  ], []);

  const listo = idNuevo !== 0 && idNuevo !== idActual && motivo.trim() !== "";
  const idsSel = filas.filter((f) => seleccion.has(f.idGestion)).map((f) => f.idGestion);

  return (
    <div>
      <h1>Cambio de asignación de aprobador</h1>
      <p className="nota">
        Si un aprobador no está disponible (enfermedad, vacaciones, salida), elíjalo, marque sus gestiones pendientes y pásalas a otro usuario con permiso
        de aprobar. Solo cambia el nivel que está pendiente; los niveles siguientes siguen la estructura organizativa original. Cada cambio queda en la
        auditoría con su motivo. <Link to="/gestiones">← Volver a Gestiones</Link>
      </p>

      {error && <p className="mensaje-error" style={{ whiteSpace: "pre-line" }}>{error}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}

      <div className="formulario-en-linea" style={{ marginBottom: "1rem" }}>
        <label>Aprobador actual
          <select value={idActual} onChange={(e) => { const v = Number(e.target.value); setIdActual(v); void cargar(v); }}>
            <option value={0}>— Seleccione —</option>
            {aprobadores.map((a) => <option key={a.idUsuario} value={a.idUsuario}>{a.nombre}</option>)}
          </select>
        </label>
        <label>Nuevo aprobador
          <select value={idNuevo} onChange={(e) => setIdNuevo(Number(e.target.value))}>
            <option value={0}>— Seleccione —</option>
            {aprobadores.filter((a) => a.idUsuario !== idActual).map((a) => <option key={a.idUsuario} value={a.idUsuario}>{a.nombre}</option>)}
          </select>
        </label>
        <label>Motivo (obligatorio)
          <input type="text" maxLength={300} value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Ej.: incapacidad médica" />
        </label>
      </div>

      <TablaDatos
        columnas={columnas}
        filas={filas}
        claveFila={(g) => g.idGestion}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        filtrosPorColumna
        mensajeVacio={idActual ? "Este usuario no tiene gestiones pendientes de aprobar." : "Seleccione el aprobador actual para ver sus gestiones."}
        placeholderBusqueda="Buscar gestión"
        comandos={[
          {
            clave: "sel", etiqueta: "Reasignar seleccionadas", icono: "⇄", requiereSeleccion: "varias",
            deshabilitado: !listo || procesando, onClick: () => void reasignar(idsSel),
          },
          {
            clave: "todas", etiqueta: "Reasignar todas", icono: "⇉",
            deshabilitado: !listo || procesando || filas.length === 0,
            onClick: () => window.confirm(`¿Reasignar las ${filas.length} gestiones pendientes?`) && void reasignar(filas.map((f) => f.idGestion)),
          },
          { clave: "act", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar(idActual) },
        ]}
      />
    </div>
  );
}
