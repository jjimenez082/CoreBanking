import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "react-oidc-context";
import { useApiClient } from "../api/useApiClient";
import { usePermisos } from "../contexts/PermisosContext";
import type { GestionResponse } from "../api/tipos";

export function DashboardPage() {
  const auth = useAuth();
  const api = useApiClient();
  const { cargando: cargandoPermisos, codigosPermiso } = usePermisos();
  const [totalPendientes, setTotalPendientes] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelado = false;

    api
      .get<GestionResponse[]>("/api/gestiones/pendientes")
      .then((respuesta) => {
        if (!cancelado) setTotalPendientes(respuesta.data.length);
      })
      .catch((err) => {
        if (!cancelado) setError(err.message);
      });

    return () => {
      cancelado = true;
    };
  }, [api]);

  const compania = (auth.user?.profile as Record<string, unknown> | undefined)?.compania as
    | string
    | undefined;

  return (
    <div>
      <h1>Dashboard</h1>
      <p>
        Sesión de <strong>{auth.user?.profile.name}</strong>
        {compania ? (
          <>
            {" "}
            en la compañía <strong>{compania}</strong>
          </>
        ) : null}
      </p>

      <div className="tarjetas">
        <Link to="/gestiones" className="tarjeta tarjeta--enlace" aria-label="Ir a la pantalla de gestiones">
          <div className="tarjeta__valor">
            {error ? "—" : totalPendientes ?? "…"}
          </div>
          <div className="tarjeta__etiqueta">Gestiones pendientes <span aria-hidden="true">→</span></div>
        </Link>
      </div>

      {error && (
        <p className="mensaje-error">
          No se pudo consultar la bandeja de gestiones: {error}
        </p>
      )}

      <section className="seccion">
        <h2>Tus permisos (RBAC)</h2>
        {cargandoPermisos ? (
          <p className="nota">Cargando...</p>
        ) : codigosPermiso.length === 0 ? (
          <p className="nota">
            No tienes ningún permiso asignado todavía — pídele a un administrador que te
            asigne un rol en sec.USUARIO_ROL.
          </p>
        ) : (
          <p className="nota">{codigosPermiso.join(" · ")}</p>
        )}
      </section>
    </div>
  );
}
