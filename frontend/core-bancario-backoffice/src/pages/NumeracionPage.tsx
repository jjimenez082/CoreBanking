import { PanelModal } from "../components/VentanaModal";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type {
  ApiErrorBody,
  RangoNumeracionCrearRequest,
  RangoNumeracionEditarRequest,
  RangoNumeracionResponse,
} from "../api/tipos";

/** Objetos que hoy tienen numeración (CUENTA y CRÉDITO entran con su rango por producto cuando existan los productos). */
const OBJETOS = [
  { codigo: "CLIENTE", nombre: "Cliente" },
  { codigo: "PROSPECTO", nombre: "Prospecto" },
  { codigo: "SOLICITUD_CREDITO", nombre: "Solicitud de crédito" },
  { codigo: "GESTION", nombre: "Gestión (aprobaciones)" },
];

const fmt = (n: number) => n.toLocaleString("es-CR");

const COLUMNAS: ColumnaTabla<RangoNumeracionResponse>[] = [
  { clave: "objeto", encabezado: "Objeto", render: (r) => r.objetoNombre, textoBusqueda: (r) => r.objetoNombre },
  { clave: "longitud", encabezado: "Dígitos", render: (r) => r.longitud },
  { clave: "rango", encabezado: "Rango", render: (r) => `${fmt(r.numeroDesde)} – ${fmt(r.numeroHasta)}` },
  { clave: "ultimo", encabezado: "Último asignado", render: (r) => (r.ultimoAsignado >= r.numeroDesde ? fmt(r.ultimoAsignado) : "—") },
  { clave: "proximo", encabezado: "Próximo", render: (r) => r.proximo ?? "—" },
  { clave: "disp", encabezado: "Disponibles", render: (r) => fmt(r.disponibles) },
  { clave: "bloque", encabezado: "Bloque", render: (r) => r.tamanoBloque },
  { clave: "estado", encabezado: "Estado", render: (r) => r.estado, textoBusqueda: (r) => r.estado },
];

interface FormRango {
  objeto: string;
  longitud: number;
  numeroDesde: number;
  numeroHasta: number;
  tamanoBloque: number;
  estado: string;
}

const NUEVO: FormRango = { objeto: "", longitud: 8, numeroDesde: 1, numeroHasta: 99999999, tamanoBloque: 1, estado: "Activo" };

function mensaje(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

/**
 * Administración › Rangos de numeración (permiso NUM_ADMINISTRACION).
 * El número de negocio de cada objeto sale de un rango por compañía: longitud fija,
 * sin dígito verificador, con huecos aceptados. Un rango que ya emitió números no
 * cambia su longitud ni su inicio y su final solo puede crecer.
 */
export function NumeracionPage() {
  const api = useApiClient();
  const [filas, setFilas] = useState<RangoNumeracionResponse[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exito, setExito] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());

  const [panel, setPanel] = useState<"nuevo" | "editar" | null>(null);
  const [form, setForm] = useState<FormRango>(NUEVO);
  const [guardando, setGuardando] = useState(false);
  const [errorForm, setErrorForm] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const r = await api.get<RangoNumeracionResponse[]>("/api/numeracion/rangos");
      setFilas(r.data);
      setSeleccion(new Set());
    } catch (err) {
      setError(mensaje(err, "No se pudieron cargar los rangos."));
    } finally {
      setCargando(false);
    }
  }, [api]);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  const idSel = seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
  const seleccionado = filas.find((f) => f.idRangoNumeracion === idSel) ?? null;
  const yaEmitio = seleccionado ? seleccionado.ultimoAsignado >= seleccionado.numeroDesde : false;
  const objetosLibres = OBJETOS.filter((o) => !filas.some((f) => f.objeto === o.codigo && f.estado === "Activo"));

  function abrirNuevo() {
    setExito(null);
    setErrorForm(null);
    setForm({ ...NUEVO, objeto: objetosLibres[0]?.codigo ?? "" });
    setPanel("nuevo");
  }

  function abrirEditar() {
    if (!seleccionado) return;
    setExito(null);
    setErrorForm(null);
    setForm({
      objeto: seleccionado.objeto,
      longitud: seleccionado.longitud,
      numeroDesde: seleccionado.numeroDesde,
      numeroHasta: seleccionado.numeroHasta,
      tamanoBloque: seleccionado.tamanoBloque,
      estado: seleccionado.estado,
    });
    setPanel("editar");
  }

  async function guardar(e: FormEvent) {
    e.preventDefault();
    setGuardando(true);
    setErrorForm(null);
    try {
      if (panel === "nuevo") {
        const cuerpo: RangoNumeracionCrearRequest = {
          objeto: form.objeto, longitud: form.longitud, numeroDesde: form.numeroDesde,
          numeroHasta: form.numeroHasta, tamanoBloque: form.tamanoBloque,
        };
        await api.post("/api/numeracion/rangos", cuerpo);
        setExito("Rango creado.");
      } else if (seleccionado) {
        const cuerpo: RangoNumeracionEditarRequest = {
          longitud: form.longitud, numeroDesde: form.numeroDesde, numeroHasta: form.numeroHasta,
          tamanoBloque: form.tamanoBloque, estado: form.estado,
        };
        await api.put(`/api/numeracion/rangos/${seleccionado.idRangoNumeracion}`, cuerpo);
        setExito("Rango actualizado.");
      }
      setPanel(null);
      await cargar();
    } catch (err) {
      setErrorForm(mensaje(err, "No se pudo guardar el rango."));
    } finally {
      setGuardando(false);
    }
  }

  const num = (v: string) => (v === "" ? 0 : Number(v));
  const bloqueado = panel === "editar" && yaEmitio;
  const ejemplo = form.numeroDesde > 0 && form.longitud > 0 ? String(form.numeroDesde).padStart(form.longitud, "0") : "";

  return (
    <div>
      <h1>Rangos de numeración</h1>
      <p className="nota">
        El número de negocio se asigna al final del proceso, justo antes de crear el registro. Longitud fija por objeto,
        rangos por compañía, sin dígito verificador; pueden quedar huecos.
      </p>
      {error && <p className="mensaje-error">{error}</p>}
      {exito && <p className="mensaje-exito">{exito}</p>}

      <TablaDatos
        columnas={COLUMNAS}
        filas={filas}
        claveFila={(f) => f.idRangoNumeracion}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        mensajeVacio="No hay rangos configurados."
        comandos={[
          { clave: "nuevo", etiqueta: "Nuevo", icono: "+", deshabilitado: objetosLibres.length === 0, onClick: abrirNuevo },
          { clave: "editar", etiqueta: "Editar", icono: "✎", requiereSeleccion: "una", onClick: abrirEditar },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />

      {panel && (
        <PanelModal onCerrar={() => setPanel(null)}>
          <h3>{panel === "nuevo" ? "Nuevo rango" : `Editar rango de ${seleccionado?.objetoNombre}`}</h3>
          {errorForm && <p className="mensaje-error">{errorForm.split("\n").map((l, i) => <span key={i}>{l}<br /></span>)}</p>}
          {bloqueado && (
            <p className="nota">
              Este rango ya emitió números: la longitud y el número inicial no se pueden cambiar, y el final solo puede crecer (mínimo {fmt(seleccionado!.ultimoAsignado)}).
            </p>
          )}
          <form className="formulario" onSubmit={guardar}>
            {panel === "nuevo" && (
              <label>
                Objeto
                <select value={form.objeto} onChange={(e) => setForm({ ...form, objeto: e.target.value })} required>
                  {objetosLibres.map((o) => (<option key={o.codigo} value={o.codigo}>{o.nombre}</option>))}
                </select>
              </label>
            )}
            <label>
              Longitud (dígitos)
              <input type="number" min={1} max={18} value={form.longitud} disabled={bloqueado}
                onChange={(e) => setForm({ ...form, longitud: num(e.target.value) })} />
            </label>
            <label>
              Número inicial
              <input type="number" min={1} value={form.numeroDesde} disabled={bloqueado}
                onChange={(e) => setForm({ ...form, numeroDesde: num(e.target.value) })} />
            </label>
            <label>
              Número final
              <input type="number" min={1} value={form.numeroHasta}
                onChange={(e) => setForm({ ...form, numeroHasta: num(e.target.value) })} />
            </label>
            <label>
              Tamaño de bloque (cache)
              <input type="number" min={1} max={100000} value={form.tamanoBloque}
                onChange={(e) => setForm({ ...form, tamanoBloque: num(e.target.value) })} />
            </label>
            {panel === "editar" && (
              <label>
                Estado
                <select value={form.estado} onChange={(e) => setForm({ ...form, estado: e.target.value })}>
                  <option value="Activo">Activo</option>
                  <option value="Inactivo">Inactivo</option>
                </select>
              </label>
            )}
            <p className="nota">
              Primer número: <strong>{ejemplo || "—"}</strong>. Un bloque mayor a 1 acelera cargas masivas, pero los números
              no usados al reiniciar la API quedan como hueco; con varias instancias de la API déjelo en 1.
            </p>
            <div className="panel-formulario__acciones">
              <button type="submit" disabled={guardando}>{guardando ? "Guardando..." : "Guardar"}</button>
              <button type="button" className="boton-secundario" onClick={() => setPanel(null)}>Cancelar</button>
            </div>
          </form>
        </PanelModal>
      )}
    </div>
  );
}
