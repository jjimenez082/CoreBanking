import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { isAxiosError } from "axios";
import { useApiClient } from "../api/useApiClient";
import type { ApiErrorBody, BorradorResumenResponse } from "../api/tipos";
import { TablaDatos, type ColumnaTabla } from "../components/TablaDatos";

const fmt = (iso: string) => new Date(iso).toLocaleString("es-CR");

const COLUMNAS: ColumnaTabla<BorradorResumenResponse>[] = [
  { clave: "nombre", encabezado: "Cliente", render: (b) => b.nombre || "(sin nombre)", textoBusqueda: (b) => b.nombre ?? "" },
  { clave: "ident", encabezado: "Identificación", render: (b) => b.identificacion || "—", textoBusqueda: (b) => b.identificacion ?? "" },
  { clave: "paso", encabezado: "Último paso", render: (b) => `${b.paso} de 5` },
  { clave: "mod", encabezado: "Modificado", render: (b) => fmt(b.modificadoEn) },
  { clave: "vence", encabezado: "Vence", render: (b) => fmt(b.venceEn) },
];

/** Borradores de alta del usuario: privados y con vencimiento. */
export function BorradoresPage() {
  const api = useApiClient();
  const navigate = useNavigate();
  const [filas, setFilas] = useState<BorradorResumenResponse[]>([]);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string | number>>(new Set());

  const cargar = useCallback(async () => {
    setCargando(true);
    setError(null);
    try {
      const r = await api.get<BorradorResumenResponse[]>("/api/interlocutores/borradores");
      setFilas(r.data);
      setSeleccion(new Set());
    } catch {
      setError("No se pudieron cargar los borradores.");
    } finally {
      setCargando(false);
    }
  }, [api]);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  const id = seleccion.size === 1 ? Number(Array.from(seleccion)[0]) : null;

  async function eliminar() {
    if (seleccion.size === 0) return;
    if (!window.confirm(`¿Eliminar ${seleccion.size} borrador(es)? No se puede deshacer.`)) return;
    try {
      for (const k of seleccion) await api.delete(`/api/interlocutores/borradores/${k}`);
    } catch (err) {
      setError(isAxiosError<ApiErrorBody>(err) && err.response?.data?.mensaje ? err.response.data.mensaje : "No se pudo eliminar.");
    }
    await cargar();
  }

  return (
    <div>
      <h1>Mis borradores</h1>
      <p className="nota">Solo usted ve sus borradores. Vencen si no se vuelven a guardar dentro del plazo configurado.</p>
      {error && <p className="mensaje-error">{error}</p>}
      <TablaDatos
        columnas={COLUMNAS}
        filas={filas}
        claveFila={(b) => b.idBorrador}
        seleccion={seleccion}
        onCambiarSeleccion={setSeleccion}
        cargando={cargando}
        mensajeVacio="No tiene borradores."
        comandos={[
          { clave: "nuevo", etiqueta: "Nueva persona", icono: "+", onClick: () => navigate("/interlocutores/clientes/nuevo") },
          { clave: "nuevaOrg", etiqueta: "Nueva organización", icono: "+", onClick: () => navigate("/interlocutores/clientes/nuevo?tipo=juridica") },
          { clave: "continuar", etiqueta: "Continuar", icono: "▶", requiereSeleccion: "una", onClick: () => id !== null && navigate(`/interlocutores/clientes/nuevo?borrador=${id}`) },
          { clave: "eliminar", etiqueta: "Eliminar", icono: "🗑", requiereSeleccion: "varias", onClick: () => void eliminar() },
          { clave: "actualizar", etiqueta: "Actualizar", icono: "↻", onClick: () => void cargar() },
        ]}
      />
      <p className="nota"><Link to="/interlocutores">← Volver al inicio de Interlocutores</Link></p>
    </div>
  );
}
