# Keycloak de desarrollo — Core Bancario (SEG-010)

Instancia local de Keycloak para desarrollar y probar el MVP v1 (dominio Clientes) contra un IAM real, cumpliendo el estándar definido en SEG-009 (OAuth 2.0 + OpenID Connect, JWT firmado, validación vía JWKS).

**Importante:** esto es una elección de herramienta de desarrollo, no una decisión de arquitectura. El backend valida tokens OIDC/JWT estándar — cualquier otro proveedor OIDC-compliant (Okta, Entra ID, ForgeRock, Auth0) funcionaría igual sin cambiar el core (ver ARQ-016).

## Cómo levantarlo

```bash
cd keycloak-dev
docker compose up -d
```

Espera unos segundos a que arranque, luego:

- **Consola de administración:** http://localhost:8080 → usuario `admin` / clave `admin`
- El realm `dev` ya viene importado automáticamente (no hay que crearlo a mano).

## Qué trae preconfigurado el realm `dev`

| Elemento | Detalle |
|---|---|
| Nombre del realm | `dev` — sigue la convención de ARQ-020 (nombre de realm = `codigo_cliente` en minúsculas). Coincide con `dir.CLIENTE_SAAS.realm_iam = 'dev'` y con la base de datos `CoreBancario_DEV` ya creadas. |
| Cliente `core-bancario-frontend` | Público, `Authorization Code + PKCE` (SEG-009) — para la pantalla/portal |
| Cliente `core-bancario-backend` | Confidencial, `Client Credentials` (SEG-009) — para llamadas sistema-a-sistema; secreto de desarrollo `dev-secret-change-me` (**cámbialo si esto sale de tu máquina**) |
| Claims personalizados | `compania` (multi-tenant Nivel 2, ARQ-019) y `nivel_riesgo`, agregados al token de ambos clientes |
| Roles de realm | `usuario_core`, `administrador_core` — punto de partida para RBAC (9.1) |
| Usuario de prueba | `qa.arquitecto` / `Password123!` (pide cambio de clave en el primer login), con `compania=DEV` |

## Cómo probar que emite un token válido

Flujo `Client Credentials` (sistema a sistema), el más simple para probar desde la terminal:

```bash
curl -X POST http://localhost:8080/realms/dev/protocol/openid-connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=core-bancario-backend" \
  -d "client_secret=dev-secret-change-me"
```

La respuesta trae un `access_token` JWT — se puede decodificar en https://jwt.io (o cualquier librería JWT) para confirmar que lleva los claims esperados.

Para el flujo interactivo (`Authorization Code + PKCE`) del frontend, se necesita una app que arme la URL de autorización — eso ya corresponde al desarrollo del frontend, no a este stack.

## Enlazar este Keycloak con la base de datos ya construida (sec.USUARIO / sec.USUARIO_INTEGRACION)

La capa transversal (`modelo_fisico_transversal_minimo.sql`) ya está corrida contra `CoreBancario_DEV`, así que este es el paso que cierra el círculo: hacer que `sec.USUARIO.subject_iam` y `sec.USUARIO_INTEGRACION.client_id_iam` apunten a identidades reales de este Keycloak, en vez de quedar vacíos.

**1. Obtener el `sub` del usuario de prueba** (el identificador único que Keycloak le asigna — es lo que `subject_iam` espera, SEG-010):

- Consola admin → realm `dev` → *Users* → `qa.arquitecto` → el campo **ID** que se ve en la URL o en la pantalla de detalle es el `sub`.
- O decodificando el `access_token` de un login interactivo en https://jwt.io — el claim `sub` es ese valor.

**2. Insertar el usuario en `sec.USUARIO`** (contra `CoreBancario_DEV`), usando ese `sub`:

```sql
USE CoreBancario_DEV;
GO

INSERT INTO sec.USUARIO (id_compania, subject_iam, nombre_completo, correo, creado_por)
VALUES (
    1,                                  -- id_compania de tu COMPANIA de prueba (gen.COMPANIA)
    N'<pegar-aqui-el-sub-de-qa.arquitecto>',
    N'QA Arquitecto',
    N'qa.arquitecto@example.com',
    N'sistema'
);
GO
```

**3. Registrar la cuenta de servicio del backend en `sec.USUARIO_INTEGRACION`** (el cliente `core-bancario-backend` de este realm, para llamadas sistema-a-sistema):

```sql
INSERT INTO sec.USUARIO_INTEGRACION (id_compania, client_id_iam, nombre, creado_por)
VALUES (1, N'core-bancario-backend', N'Backend Core Bancario (Client Credentials)', N'sistema');
GO
```

Con esto, cuando el backend valide un JWT (de `qa.arquitecto` o del cliente de servicio) puede resolver `subject_iam`/`client_id_iam` contra `sec.USUARIO`/`sec.USUARIO_INTEGRACION` y saber exactamente quién está llamando — la pieza que faltaba para que RBAC (`sec.USUARIO_ROL`) tenga sentido.

## Siguiente paso natural

Con la base de datos y Keycloak ya enlazados, lo que sigue es el backend (.NET) que efectivamente valide el JWT contra el JWKS de este realm (`http://localhost:8080/realms/dev/protocol/openid-connect/certs`) y resuelva la conexión a `CoreBancario_DEV` vía `dir.CLIENTE_SAAS` (mecanismo de enrutamiento, DAD 6.4) — la API mínima de `INTERLOCUTOR` que veníamos planeando.

Los roles de ejemplo (`usuario_core`, `administrador_core`) se deben ir reemplazando por roles reales del dominio Clientes (ej. "Analista KYC", "Oficial de Cumplimiento") cuando se carguen en `sec.ROL_AUTORIZACION` — pendiente ya señalado en `Modelo_Fisico_Transversal_Minimo.md`.

## Tema de la pantalla de login (core-bancario)

La pantalla de ingreso usa el tema `themes/core-bancario/login`: panel navy con el wordmark /BD Core y formulario claro, igual que la entrada del Backoffice. Hereda del tema `keycloak` y solo agrega `css/core-bancario.css`.

- **Instalación:** `docker-compose.yml` ya monta la carpeta en `/opt/keycloak/themes/core-bancario`. Recrea el contenedor: `docker compose up -d --force-recreate`.
- **Activarlo en el realm:** si el realm `dev` ya existía (el import solo corre la primera vez), entra a la consola admin → realm `dev` → *Realm settings* → *Themes* → **Login theme = core-bancario** → Save. Un realm nuevo importado con `realm-export.json` ya lo trae.
- **Idioma (opcional):** *Realm settings* → *Localization* → activar *Internationalization*, agregar Español y dejarlo como idioma por defecto, para que el formulario salga en español.
- **Producción:** copiar la misma carpeta a `/opt/keycloak/themes/` de la instancia compartida y asignar el tema a cada realm de cliente (cambiando el logo `img/logo-bd-core.svg` si el cliente tiene el suyo).
