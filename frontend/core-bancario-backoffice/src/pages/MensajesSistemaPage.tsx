import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, MensajeSistemaResponse } from "../api/tipos";

const COLUMNAS: ColumnaTabla<MensajeSistemaResponse>[] = [
  { clave: "codigo", encabezado: "Código", render: (m) => <code>{m.codigo}</code>, textoBusqueda: (m) => m.codigo },
  { clave: "nombre", encabezado: "Nombre", render: (m) => m.nombre, textoBusqueda: (m) => m.nombre },
  { clave: "severidad", encabezado: "Tipo", render: (m) => ({ ERROR: "Error", WARNING: "Warning", INFORMATIVO: "Informativo" })[m.severidad], textoBusqueda: (m) => m.severidad },
  { clave: "resultado", encabezado: "Resultado", render: (m) => m.resultado, textoBusqueda: (m) => m.resultado },
  { clave: "http", encabezado: "HTTP", render: (m) => m.httpEstado },
  { clave: "texto", encabezado: "Mensaje", render: (m) => m.texto, textoBusqueda: (m) => m.texto },
  { clave: "mod", encabezado: "Personalizado", render: (m) => (m.modificado ? "Sí" : "—") },
];

function mensaje(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

/**
 * Administración › Mensajes del sistema (permiso MSG_ADMINISTRACION).
 * Maestro de los mensajes que devuelve la API a las integraciones. El código es estable (lo programa el integrador);
 * el texto se parametriza aquí. Marcadores: {identificacion}, {numeroCliente}, {numeroGestion}.
 */
export function MensajesSistemaPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<MensajeSistemaResponse[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());
  const [editando, setEditando] = useState(false);
  const [texto, setTexto] = useState("");
  const [guardando, setGuardando] = useState(false);
  const [errorForm, setErrorForm] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const r = await api.get<MensajeSistemaResponse[]>("/api/mensajes");
      setFilas(r.data);
      setSeleccion(new Set());
    } catch (err) {
      setError(mensaje(err, "No se pudieron cargar los mensajes."));
    } finally {
      setCargando(false);
    }
  }, [api]);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  const idSel = seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
  const sel = filas.find((f) => f.idMensaje === idSel) ?? null;

  function abrirEditar() {
    if (!sel) return;
    setExito(null);
    setErrorForm(null);
    setTexto(sel.texto);
    setEditando(true);
  }

  async function guardar(e: FormEvent) {
    e.preventDefault();
    if (!sel) return;
    setGuardando(true);
    setErrorForm(null);
    try {
      await api.put(`/api/mensajes/${sel.idMensaje}`, { texto });
      setExito("Mensaje actualizado.");
      setEditando(false);
      await cargar();
    } catch (err) {
      setErrorForm(mensaje(err, "No se pudo guardar el mensaje."));
    } finally {
      setGuardando(false);
    }
  }

  async function restaurar() {
    if (!sel) return;
    setExito(null);
    setError(null);
    try {
      await api.post(`/api/mensajes/${sel.idMensaje}/restaurar`);
      setExito("Se restauró el texto original.");
      await cargar();
    } catch (err) {
      setError(mensaje(err, "No se pudo restaurar el mensaje."));
    }
  }

  return (
    <div>
      <h1>Mensajes del sistema</h1>
      <p className="nota">
        Textos que la API devuelve a las integraciones. El código (ER_/WA_/IN_ + módulo + consecutivo), el tipo, el resultado y el HTTP son el contrato con el integrador y no cambian; solo el texto se puede ajustar.
        Marcadores disponibles: <code>{"{identificacion}"}</code>, <code>{"{numeroCliente}"}</code>, <code>{"{numeroGestion}"}</code>.
        Los mensajes de error nunca deben incluir datos de otros clientes.
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <TablaDatos
        columnas={COLUMNAS}
        filas={filas}
        claveFila={(f) => f.idMensaje}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        mensajeVacio="No hay mensajes configurados; corra mensajes_api_ronda16.sql."
        comandos={[
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: abrirEditar },
          { clave: "restaurar", etiqueta: "Restaurar original", icono: "↺", requiereSeleccion: "una", deshabilitado: !sel?.modificado, onClick: () => void restaurar() },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {editando && sel && (
        <PanelModal onCerrar={() => setEditando(false)}>
          <h3>Editar mensaje <code>{sel.codigo}</code> · {sel.nombre}</h3>
          {sel.descripcion && <p className="nota">{sel.descripcion}</p>}
          {errorForm && <p className="mensaje-error">{errorForm}</p>}
          <form className="formulario" onSubmit={guardar}>
            <label>
              Texto
              <textarea rows={3} maxLength={500} value={texto} onChange={(e) => setTexto(e.target.value)} required />
            </label>
            <p className="nota">Original: {sel.textoDefecto}</p>
            <div className="panel-formulario__acciones">
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setEditando(false)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}
