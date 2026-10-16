# Core Bancario — Backoffice (React + Vite + TypeScript)

Primera pantalla web del Core Bancario. Hace login contra Keycloak
(Authorization Code + PKCE, realm `dev`) y consume `CoreBancario.Api` para:

- Ver un dashboard simple (cuántas gestiones están pendientes de aprobación).
- Listar la bandeja de gestiones pendientes (`apr.GESTION`).
- Dar de alta y consultar Interlocutores Persona Física.

## Por qué este stack

Se eligió **React + Vite + TypeScript** (en vez de Blazor o Angular) porque:

1. El equipo de desarrollo ya lo conoce — sin curva de aprendizaje.
2. Tiene el mejor ecosistema/proyección para lo que se planea más adelante:
   un agente de IA asistente dentro del core (tipo ElevenLabs) que explique
   a un usuario por qué se generó un cobro/comisión, con streaming de
   respuestas y componentes de chat — ese ecosistema madura primero y más
   rápido en React.
3. Mantiene el backend .NET desacoplado de quién lo consume: hoy esta UI,
   mañana un agente de IA como "tool-caller" directo sobre la misma API.

## Cómo correrlo

**Requisitos:** Node.js 18+ (probado con Node 22), y `CoreBancario.Api` y
Keycloak (realm `dev`) ya corriendo localmente.

```bash
cd frontend/core-bancario-backoffice
npm install
cp .env.example .env.local
```

Revisa `.env.local` y ajusta si tu API o Keycloak corren en otro
host/puerto (por ejemplo, `dotnet run` te puede asignar un puerto distinto
a 5003 — usa el que te muestre su consola):

```
VITE_KEYCLOAK_AUTHORITY=http://localhost:8080/realms/dev
VITE_KEYCLOAK_CLIENT_ID=core-bancario-frontend
VITE_API_BASE_URL=http://localhost:5003
```

Luego:

```bash
npm run dev
```

Abre `http://localhost:3000` (el puerto está fijado en `vite.config.ts`
porque debe coincidir exactamente con el `redirectUri`/`webOrigin`
configurado para el cliente `core-bancario-frontend` en el
`realm-export.json` de Keycloak: `http://localhost:3000/*`).

Al entrar, si no hay sesión, te manda automáticamente a la pantalla de
login de Keycloak. Inicia sesión con `qa.arquitecto` (o el usuario que
tengas configurado) y vuelves ya autenticado al Backoffice.

## Qué hace cada pantalla

- **Dashboard** (`/`) — saludo con el nombre y la compañía (claim
  `compania` del JWT) del usuario logueado, y el total de gestiones
  pendientes.
- **Gestiones pendientes** (`/gestiones`) — tabla de `apr.GESTION` en
  estado "Pendiente de Aprobación".
- **Clientes** (`/interlocutores`) — formulario de alta de Persona Física
  y un buscador por ID. **Nota:** la API todavía no expone un listado
  general de interlocutores (solo alta y consulta puntual por ID) — un
  endpoint de búsqueda/listado es el siguiente incremento natural del
  backend antes de que esta pantalla sea realmente usable en el día a día.

## Autenticación (cómo funciona por dentro)

- `src/auth/oidcConfig.ts` configura `react-oidc-context` (que envuelve
  `oidc-client-ts`) contra el `authority` y `client_id` de las variables de
  entorno — el flujo es Authorization Code + PKCE estándar, el mismo que
  usaría cualquier IdP compatible OIDC (Okta, Entra ID, ForgeRock), no algo
  específico de Keycloak.
- `src/components/RutaProtegida.tsx` redirige automáticamente al login si
  no hay sesión — no hay una pantalla de "login" propia, se usa la de
  Keycloak directamente (esto es estándar en flujos OIDC).
- `src/api/useApiClient.ts` inyecta el `access_token` del usuario actual
  como header `Authorization: Bearer ...` en cada llamada a la API vía un
  interceptor de axios.

**Nota sobre expiración de sesión:** este MVP no implementa refresh
automático de tokens todavía — si el token expira mientras usas la app,
las llamadas a la API empezarán a fallar con 401. Para producción habría
que agregar `automaticSilentRenew` (que `react-oidc-context` soporta) o
manejar el refresh explícitamente.

## Estructura

```
src/
├── main.tsx                 # AuthProvider + BrowserRouter + App
├── App.tsx                  # Rutas
├── auth/
│   └── oidcConfig.ts         # Configuración OIDC/PKCE contra Keycloak
├── api/
│   ├── client.ts              # Instancia base de axios
│   ├── useApiClient.ts        # Hook: axios + Bearer token inyectado
│   └── tipos.ts                # DTOs espejo de CoreBancario.Api
├── components/
│   ├── RutaProtegida.tsx      # Redirige a login si no hay sesión
│   └── Layout.tsx              # Header + nav + info de usuario
└── pages/
    ├── DashboardPage.tsx
    ├── GestionesPage.tsx
    └── InterlocutoresPage.tsx
```

## Troubleshooting

- **Pantalla en blanco o error de redirect_uri tras el login:** confirma
  que estás abriendo la app en `http://localhost:3000` exactamente (no
  `127.0.0.1`), porque Keycloak valida el `redirectUri` de forma estricta
  contra lo configurado en el cliente.
- **CORS al llamar a la API:** `CoreBancario.Api` ya trae CORS habilitado
  para `http://localhost:3000` (sección `Cors:OrigenesPermitidos` en su
  `appsettings.json`). Si corres el Backoffice en otro puerto/host, agrega
  ese origen ahí también.
