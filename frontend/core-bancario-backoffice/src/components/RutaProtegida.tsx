import type { PropsWithChildren } from "react";
import { useAuth } from "react-oidc-context";
import { Logo } from "./Logo";
import { CLAVE_CIERRE_INACTIVIDAD } from "./SesionGuardian";

/**
 * Pantalla de transicion mostrada mientras se resuelve la sesion o se
 * redirige a Keycloak. No es un formulario de login propio: las
 * credenciales las recibe unicamente el proveedor de identidad (Keycloak u
 * otro IdP OIDC/JWKS compatible, SEG-009) — React nunca las toca. Esto es
 * por diseno de seguridad, no una limitacion temporal.
 */
function PantallaTransicion({
  mensaje,
  accion,
}: {
  mensaje: string;
  accion?: { etiqueta: string; onClick: () => void };
}) {
  return (
    <div className="pantalla-auth">
      <div className="pantalla-auth__marca">
        <Logo variante="claro" tamano="lg" subtitulo="BACKOFFICE" />
      </div>
      <div className="pantalla-auth__panel">
        <div className="pantalla-auth__spinner" aria-hidden="true" />
        <p className="pantalla-auth__mensaje">{mensaje}</p>
        {accion && <button onClick={accion.onClick}>{accion.etiqueta}</button>}
      </div>
    </div>
  );
}

export function RutaProtegida({ children }: PropsWithChildren) {
  const auth = useAuth();

  if (auth.isLoading) {
    return <PantallaTransicion mensaje="Verificando tu sesión..." />;
  }

  if (auth.error) {
    return (
      <PantallaTransicion
        mensaje={`Ocurrió un error de autenticación: ${auth.error.message}`}
        accion={{ etiqueta: "Reintentar", onClick: () => auth.signinRedirect() }}
      />
    );
  }

  if (!auth.isAuthenticated) {
    // Cierre por inactividad: no se vuelve a entrar solo; se avisa y el usuario decide ingresar.
    let porInactividad = false;
    try { porInactividad = sessionStorage.getItem(CLAVE_CIERRE_INACTIVIDAD) === "1"; } catch { /* sin storage */ }
    if (porInactividad) {
      return (
        <PantallaTransicion
          mensaje="Tu sesión se cerró por inactividad. Ingresa de nuevo con tus credenciales para continuar."
          accion={{
            etiqueta: "Ingresar",
            onClick: () => {
              try { sessionStorage.removeItem(CLAVE_CIERRE_INACTIVIDAD); } catch { /* sin storage */ }
              void auth.signinRedirect();
            },
          }}
        />
      );
    }
    auth.signinRedirect();
    return (
      <PantallaTransicion
        mensaje="Te estamos llevando a tu proveedor de identidad para iniciar sesión..."
        accion={{ etiqueta: "Ingresar", onClick: () => auth.signinRedirect() }}
      />
    );
  }

  return <>{children}</>;
}
