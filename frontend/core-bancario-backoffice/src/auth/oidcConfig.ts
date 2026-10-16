import type { AuthProviderProps } from "react-oidc-context";

/**
 * Configuracion OIDC contra Keycloak (realm 'dev', SEG-009/ARQ-016).
 * El Backoffice no depende de Keycloak en particular: react-oidc-context
 * habla el estandar OIDC/Authorization Code + PKCE, asi que cualquier IdP
 * compatible (Okta, Entra ID, ForgeRock) funcionaria cambiando solo estas
 * variables de entorno.
 */
export const oidcConfig: AuthProviderProps = {
  authority: import.meta.env.VITE_KEYCLOAK_AUTHORITY as string,
  client_id: import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string,
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,

  // El cliente 'core-bancario-frontend' es publico (sin client secret) y
  // usa Authorization Code + PKCE (pkce.code.challenge.method=S256) —
  // react-oidc-context/oidc-client-ts manejan el code_verifier/challenge
  // automaticamente, no hay que armarlos a mano.
  response_type: "code",
  scope: "openid profile email",

  // Renueva el token en segundo plano (refresh token) antes de que venza. Si el IdP
  // ya no acepta la renovacion (sesion SSO vencida), SesionGuardian avisa y pide reingresar.
  automaticSilentRenew: true,

  // Limpia el "?code=...&state=..." de la URL despues del login.
  onSigninCallback: () => {
    window.history.replaceState({}, document.title, window.location.pathname);
  },
};
