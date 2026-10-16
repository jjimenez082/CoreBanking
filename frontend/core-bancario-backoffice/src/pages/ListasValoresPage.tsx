import { PanelModal } from "../components/VentanaModal";
import { useEffect, useState, type FormEvent } from "react";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";
import type { ApiErrorBody, ListaItemResponse, ListaTipoResponse } from "../api/tipos";

const COLUMNAS_LISTAS: ColumnaTabla<ListaTipoResponse>[] = [
  { clave: "codigo", encabezado: "Código", render: (l) => l.codigo, textoBusqueda: (l) => l.codigo },
  { clave: "nombre", encabezado: "Nombre", render: (l) => l.nombre, textoBusqueda: (l) => l.nombre },
  { clave: "estado", encabezado: "Estado", render: (l) => l.estado },
  { clave: "sistema", encabezado: "De sistema", render: (l) => (l.esSistema ? "Sí" : "—") },
  { clave: "items", encabezado: "Valores", render: (l) => l.cantidadItems },
];

const COLUMNAS_VALORES: ColumnaTabla<ListaItemResponse>[] = [
  { clave: "orden", encabezado: "Orden", render: (i) => i.orden ?? "—" },
  { clave: "codigo", encabezado: "Código", render: (i) => i.codigo, textoBusqueda: (i) => i.codigo },
  { clave: "nombre", encabezado: "Nombre", render: (i) => i.nombre, textoBusqueda: (i) => i.nombre },
  { clave: "descripcion", encabezado: "Descripción", render: (i) => i.descripcion ?? "—", textoBusqueda: (i) => i.descripcion ?? "" },
  { clave: "estado", encabezado: "Estado", render: (i) => i.estado, textoBusqueda: (i) => i.estado },
  { clave: "sistema", encabezado: "De sistema", render: (i) => (i.esSistema ? "Sí" : "—") },
  { clave: "uso", encabezado: "En uso", render: (i) => (i.enUso ? "Sí" : "No") },
];

function mensajeError(err: unknown, porDefecto: string): string {
  if (isAxiosError<ApiErrorBody>(err)) {
    if (err.response?.data?.mensaje) return err.response.data.mensaje;
    return `Error ${err.response?.status ?? ""}: ${err.message}`;
  }
  return porDefecto;
}

function unica(seleccion: Set<string | number>): number | null {
  return seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;
}

type PanelLista = { modo: "nuevo" | "editar"; codigo: string; nombre: string; descripcion: string } | null;
type PanelValor = { modo: "nuevo" | "editar"; codigo: string; nombre: string; orden: string; descripcion: string } | null;

/**
 * Administracion de Listas de Valores (gen.LISTA_VALORES_TIPO / _ITEM).
 * Seguridad: LOV_ACCESO para entrar; LOV_<codigo> para mantener los valores de
 * una lista; LOV_ADMINISTRACION para todas las listas y para crear/borrar
 * listas. Los valores no se borran si ya se usaron: se desactivan.
 */
export function ListasValoresPage() {
  const api = useApiClient();
  const { cargando: cargandoPermisos, tienePermiso } = usePermisos();
  const puedeEntrar = tienePermiso("LOV_ACCESO");
  const esAdmin = tienePermiso("LOV_ADMINISTRACION");

  const [listas, setListas] = useState<ListaTipoResponse[]>([]);
  const [items, setItems] = useState<ListaItemResponse[]>([]);
  const [cargandoListas, setCargandoListas] = useState(false);
  const [cargandoItems, setCargandoItems] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const [seleccionListas, setSeleccionListas] = useState<Set<string | number>>(new Set());
  const [seleccionItems, setSeleccionItems] = useState<Set<string | number>>(new Set());

  const [panelLista, setPanelLista] = useState<PanelLista>(null);
  const [panelValor, setPanelValor] = useState<PanelValor>(null);
  const [guardando, setGuardando] = useState(false);

  const idListaActiva = unica(seleccionListas);
  const listaActiva = listas.find((l) => l.idListaValoresTipo === idListaActiva) ?? null;

  async function cargarListas() {
    setCargandoListas(true);
    setError(null);
    try {
      const r = await api.get<ListaTipoResponse[]>("/api/listas-valores/tipos");
      setListas(r.data);
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al cargar las listas."));
    } finally {
      setCargandoListas(false);
    }
  }

  async function cargarItems(idLista: number) {
    setCargandoItems(true);
    try {
      const r = await api.get<ListaItemResponse[]>(`/api/listas-valores/tipos/${idLista}/items`);
      setItems(r.data);
      setSeleccionItems(new Set());
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al cargar los valores."));
      setItems([]);
    } finally {
      setCargandoItems(false);
    }
  }

  useEffect(() => {
    if (cargandoPermisos || !puedeEntrar) return;
    cargarListas();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cargandoPermisos, puedeEntrar]);

  // Al elegir exactamente una lista se cargan sus valores.
  useEffect(() => {
    setPanelValor(null);
    if (idListaActiva === null) {
      setItems([]);
      return;
    }
    cargarItems(idListaActiva);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idListaActiva]);

  // Ejecuta una accion por cada registro seleccionado y resume los fallos.
  async function enLote(
    claves: Set<string | number>,
    accion: (clave: number) => Promise<unknown>,
    etiqueta: (clave: number) => string,
    resumenOk: string,
  ) {
    setError(null);
    setAviso(null);
    const fallos: string[] = [];
    for (const k of Array.from(claves)) {
      const clave = Number(k);
      try {
        await accion(clave);
      } catch (err) {
        fallos.push(`${etiqueta(clave)}: ${mensajeError(err, "error inesperado")}`);
      }
    }
    if (fallos.length > 0) setError(fallos.join(" | "));
    else setAviso(resumenOk);
  }

  // ----- Listas -----
  function abrirNuevaLista() {
    setPanelLista({ modo: "nuevo", codigo: "", nombre: "", descripcion: "" });
  }

  function abrirEditarLista() {
    if (!listaActiva) return;
    setPanelLista({
      modo: "editar",
      codigo: listaActiva.codigo,
      nombre: listaActiva.nombre,
      descripcion: listaActiva.descripcion ?? "",
    });
  }

  async function guardarLista(e: FormEvent) {
    e.preventDefault();
    if (!panelLista) return;
    setGuardando(true);
    setError(null);
    setAviso(null);
    try {
      if (panelLista.modo === "nuevo") {
        await api.post("/api/listas-valores/tipos", {
          codigo: panelLista.codigo,
          nombre: panelLista.nombre,
          descripcion: panelLista.descripcion || null,
        });
        setAviso(
          "Lista creada. Asigne su permiso LOV_" +
            panelLista.codigo.trim().toUpperCase().replace(/ /g, "_") +
            " a los roles que deban mantenerla (pantalla Seguridad).",
        );
      } else if (listaActiva) {
        await api.put(`/api/listas-valores/tipos/${listaActiva.idListaValoresTipo}`, {
          nombre: panelLista.nombre,
          descripcion: panelLista.descripcion || null,
        });
        setAviso("Lista actualizada.");
      }
      setPanelLista(null);
      await cargarListas();
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al guardar la lista."));
    } finally {
      setGuardando(false);
    }
  }

  async function cambiarEstadoListas(estado: "Activo" | "Inactivo") {
    await enLote(
      seleccionListas,
      (id) => api.post(`/api/listas-valores/tipos/${id}/estado`, { estado }),
      (id) => listas.find((l) => l.idListaValoresTipo === id)?.codigo ?? String(id),
      estado === "Activo" ? "Listas activadas." : "Listas desactivadas.",
    );
    await cargarListas();
    if (idListaActiva !== null) await cargarItems(idListaActiva);
  }

  async function borrarListas() {
    if (!window.confirm(`¿Eliminar ${seleccionListas.size} lista(s)? Esta acción no se puede deshacer.`)) return;
    await enLote(
      seleccionListas,
      (id) => api.delete(`/api/listas-valores/tipos/${id}`),
      (id) => listas.find((l) => l.idListaValoresTipo === id)?.codigo ?? String(id),
      "Listas eliminadas.",
    );
    setSeleccionListas(new Set());
    await cargarListas();
  }

  // ----- Valores -----
  const puedeMantenerLista = !!listaActiva && (esAdmin || tienePermiso(`LOV_${listaActiva.codigo}`));

  function abrirNuevoValor() {
    setPanelValor({ modo: "nuevo", codigo: "", nombre: "", orden: "", descripcion: "" });
  }

  function abrirEditarValor() {
    const id = unica(seleccionItems);
    const item = items.find((i) => i.idListaValoresItem === id);
    if (!item) return;
    setPanelValor({
      modo: "editar",
      codigo: item.codigo,
      nombre: item.nombre,
      orden: item.orden?.toString() ?? "",
      descripcion: item.descripcion ?? "",
    });
  }

  async function guardarValor(e: FormEvent) {
    e.preventDefault();
    if (!panelValor || idListaActiva === null) return;
    setGuardando(true);
    setError(null);
    setAviso(null);
    const orden = panelValor.orden.trim() === "" ? null : Number(panelValor.orden);
    try {
      if (panelValor.modo === "nuevo") {
        await api.post(`/api/listas-valores/tipos/${idListaActiva}/items`, {
          codigo: panelValor.codigo,
          nombre: panelValor.nombre,
          orden,
          descripcion: panelValor.descripcion.trim() || null,
        });
        setAviso("Valor creado.");
      } else {
        const id = unica(seleccionItems);
        await api.put(`/api/listas-valores/items/${id}`, { nombre: panelValor.nombre, orden, descripcion: panelValor.descripcion.trim() || null });
        setAviso("Valor actualizado.");
      }
      setPanelValor(null);
      await cargarItems(idListaActiva);
      await cargarListas();
    } catch (err) {
      setError(mensajeError(err, "Error inesperado al guardar el valor."));
    } finally {
      setGuardando(false);
    }
  }

  async function cambiarEstadoValores(estado: "Activo" | "Inactivo") {
    await enLote(
      seleccionItems,
      (id) => api.post(`/api/listas-valores/items/${id}/estado`, { estado }),
      (id) => items.find((i) => i.idListaValoresItem === id)?.codigo ?? String(id),
      estado === "Activo" ? "Valores activados." : "Valores desactivados.",
    );
    if (idListaActiva !== null) await cargarItems(idListaActiva);
  }

  async function borrarValores() {
    if (!window.confirm(`¿Eliminar ${seleccionItems.size} valor(es)? Solo se pueden borrar los que no se han usado.`)) return;
    await enLote(
      seleccionItems,
      (id) => api.delete(`/api/listas-valores/items/${id}`),
      (id) => items.find((i) => i.idListaValoresItem === id)?.codigo ?? String(id),
      "Valores eliminados.",
    );
    if (idListaActiva !== null) await cargarItems(idListaActiva);
    await cargarListas();
  }

  if (cargandoPermisos) {
    return (
      <div>
        <h1>Listas de Valores</h1>
        <p className="nota">Verificando permisos...</p>
      </div>
    );
  }

  if (!puedeEntrar) {
    return (
      <div>
        <h1>Listas de Valores</h1>
        <p className="mensaje-error">
          No tienes el permiso <strong>LOV_ACCESO</strong> para ver esta sección. Pídele a un
          administrador que te asigne un rol con ese permiso.
        </p>
      </div>
    );
  }

  return (
    <div>
      <h1>Listas de Valores</h1>
      <p className="nota">
        Catálogos que alimentan los combos y filtros del sistema. Un valor que ya se usó no se borra:
        se desactiva y deja de ofrecerse en registros nuevos. Solo ves las listas para las que tienes
        permiso.
      </p>

      {error && <p className="mensaje-error">{error}</p>}
      {aviso && <p className="mensaje-exito">{aviso}</p>}

      <section className="seccion">
        <h2>Listas</h2>
        <TablaDatos
          columnas={COLUMNAS_LISTAS}
          filas={listas}
          claveFila={(l) => l.idListaValoresTipo}
          seleccion={seleccionListas}
          onCambiarSeleccion={setSeleccionListas}
          cargando={cargandoListas}
          mensajeVacio="No hay listas visibles para tu usuario."
          placeholderBusqueda="Buscar lista"
          comandos={[
            { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !esAdmin, onClick: abrirNuevaLista },
            { clave: "editar", etiqueta: "Editar", icono: "✎", oculto: !esAdmin, requiereSeleccion: "una", onClick: abrirEditarLista },
            { clave: "activar", etiqueta: "Activar", icono: "✓", oculto: !esAdmin, requiereSeleccion: "varias", onClick: () => cambiarEstadoListas("Activo") },
            { clave: "desactivar", etiqueta: "Desactivar", icono: "⊘", oculto: !esAdmin, requiereSeleccion: "varias", onClick: () => cambiarEstadoListas("Inactivo") },
            { clave: "eliminar", etiqueta: "Eliminar", icono: "🗑", oculto: !esAdmin, requiereSeleccion: "varias", onClick: borrarListas },
            { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: cargarListas },
          ]}
        />

        {panelLista && (
          <PanelModal onCerrar={() => setPanelLista(null)}>
            <h3>{panelLista.modo === "nuevo" ? "Nueva lista" : `Editar lista ${panelLista.codigo}`}</h3>
            {error && <p className="mensaje-error">{error}</p>}
            <form onSubmit={guardarLista} className="formulario-en-linea">
              <input
                type="text"
                placeholder="Código (ej. TIPO_CLIENTE)"
                value={panelLista.codigo}
                disabled={panelLista.modo === "editar"}
                onChange={(e) => setPanelLista({ ...panelLista, codigo: e.target.value })}
                required
              />
              <input
                type="text"
                placeholder="Nombre"
                value={panelLista.nombre}
                onChange={(e) => setPanelLista({ ...panelLista, nombre: e.target.value })}
                required
              />
              <input
                type="text"
                placeholder="Descripción (opcional)"
                value={panelLista.descripcion}
                onChange={(e) => setPanelLista({ ...panelLista, descripcion: e.target.value })}
              />
              <button type="submit" disabled={guardando}>
                {guardando ? "Guardando..." : "Guardar"}
              </button>
              <button type="button" className="boton-secundario" onClick={() => setPanelLista(null)}>
                Cancelar
              </button>
            </form>
          </PanelModal>
        )}
      </section>

      <section className="seccion">
        <h2>{listaActiva ? `Valores de ${listaActiva.nombre}` : "Valores"}</h2>
        {!listaActiva ? (
          <p className="nota">Marca una lista (una sola) para ver y mantener sus valores.</p>
        ) : (
          <>
            <TablaDatos
              columnas={COLUMNAS_VALORES}
              filas={items}
              claveFila={(i) => i.idListaValoresItem}
              seleccion={seleccionItems}
              onCambiarSeleccion={setSeleccionItems}
              cargando={cargandoItems}
              mensajeVacio="Esta lista no tiene valores."
              placeholderBusqueda="Buscar valor"
              comandos={[
                { clave: "nuevo", etiqueta: "Nuevo", icono: "+", oculto: !puedeMantenerLista, onClick: abrirNuevoValor },
                { clave: "editar", etiqueta: "Editar", icono: "✎", oculto: !puedeMantenerLista, requiereSeleccion: "una", onClick: abrirEditarValor },
                { clave: "activar", etiqueta: "Activar", icono: "✓", oculto: !puedeMantenerLista, requiereSeleccion: "varias", onClick: () => cambiarEstadoValores("Activo") },
                { clave: "desactivar", etiqueta: "Desactivar", icono: "⊘", oculto: !puedeMantenerLista, requiereSeleccion: "varias", onClick: () => cambiarEstadoValores("Inactivo") },
                { clave: "eliminar", etiqueta: "Eliminar", icono: "🗑", oculto: !puedeMantenerLista, requiereSeleccion: "varias", onClick: borrarValores },
                { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => cargarItems(listaActiva.idListaValoresTipo) },
              ]}
            />

            {panelValor && (
              <PanelModal onCerrar={() => setPanelValor(null)}>
                <h3>{panelValor.modo === "nuevo" ? "Nuevo valor" : `Editar valor ${panelValor.codigo}`}</h3>
                {error && <p className="mensaje-error">{error}</p>}
            <form onSubmit={guardarValor} className="formulario-en-linea">
                  <input
                    type="text"
                    placeholder="Código (ej. CEDULA_FISICA)"
                    value={panelValor.codigo}
                    disabled={panelValor.modo === "editar"}
                    onChange={(e) => setPanelValor({ ...panelValor, codigo: e.target.value })}
                    required
                  />
                  <input
                    type="text"
                    placeholder="Nombre"
                    value={panelValor.nombre}
                    onChange={(e) => setPanelValor({ ...panelValor, nombre: e.target.value })}
                    required
                  />
                  <input
                    type="number"
                    placeholder="Orden"
                    value={panelValor.orden}
                    onChange={(e) => setPanelValor({ ...panelValor, orden: e.target.value })}
                  />
                  <input
                    type="text"
                    placeholder="Descripción (texto explicativo, opcional)"
                    maxLength={500}
                    style={{ minWidth: "22rem" }}
                    value={panelValor.descripcion}
                    onChange={(e) => setPanelValor({ ...panelValor, descripcion: e.target.value })}
                  />
                  <button type="submit" disabled={guardando}>
                    {guardando ? "Guardando..." : "Guardar"}
                  </button>
                  <button type="button" className="boton-secundario" onClick={() => setPanelValor(null)}>
                    Cancelar
                  </button>
                </form>
              </PanelModal>
            )}
          </>
        )}
      </section>
    </div>
  );
}
