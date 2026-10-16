# Interfaces y arquitectura de integración

**Creado:** 2026-10-09. Documento vivo: cada interfaz nueva (endpoint, evento, integración externa) se agrega aquí. Lo que está marcado **[Pendiente]** no existe todavía.

## 1. Visión

El core es **API-first**: toda operación se expone como API REST (JSON) y el Backoffice es un consumidor más. Los canales externos (CRM, cajas, banca en línea, onboarding digital, web, móvil) se conectan por la misma API con su propia identidad técnica, sujeta a los mismos permisos que una persona.

Principios:
- **Una identidad por consumidor**: personas entran con su usuario; cada integración tiene su propio client (`client_id` + `client_secret`).
- **Permisos iguales para todos**: personas e integraciones se evalúan con el mismo RBAC (roles → permisos). Sin permiso, la API responde `403 permiso_denegado`. No hay acceso total por ser integración.
- **Una base de datos por cliente (compañía)**: el token dice a qué compañía pertenece quien llama y la API se conecta a la base de esa compañía.
- **Todo queda auditado** (`aud.LOG_AUDITORIA`): quién (persona o integración), qué, cuándo y, si aplica, la gestión asociada.
- Los datos de negocio sensibles se cambian por **gestión** (maker-checker); ver `Motor_de_Aprobacion.md`.

## 2. Autenticación

Proveedor de identidad: **Keycloak** (realm `dev` en desarrollo), estándar OAuth 2.0 / OpenID Connect. La API solo valida tokens JWT (`Authorization: Bearer <access_token>`); no emite tokens.

| Quién llama | Flujo OAuth | Client | Qué recibe |
|---|---|---|---|
| Persona (Backoffice, pruebas manuales) | **Authorization Code + PKCE** | `core-bancario-frontend` (público) | Access token de la persona; con scope `offline_access` entrega refresh token (evita volver a pedir usuario y contraseña) |
| Integración (sistema a sistema) | **Client Credentials** | uno por integración: `{código compañía}-{identificador}` (ej. `dev-crm-ventas`), confidencial | Access token de la cuenta de servicio |
| Core → Keycloak (crear usuarios y clients) | Client Credentials | `core-bancario-backend` con roles de `realm-management` | Uso interno (Admin API) |

**Cuándo usar cada flujo:** Authorization Code cuando hay una persona que debe identificarse (el token lleva su identidad y se audita a su nombre). Client Credentials cuando no hay persona: el sistema actúa por sí mismo con la identidad técnica de la integración.

**Obtener token de una integración** (URL exacta en la pantalla *Usuarios de integración*, que muestra client_id, secret y URL una sola vez al crear o regenerar):
```
POST {Authority}/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=<client_id>&client_secret=<secret>
```
No enviar `scope`. El `access_token` se usa en `Authorization: Bearer ...` mientras no expire; luego se pide otro.

**Endpoints públicos** (sin token): `GET /health` y `/swagger`. Todo lo demás exige token.

## 3. Resolución de compañía y de actor (cada request)

Pipeline en orden: validación JWT → **TenantResolutionMiddleware** → **ActorResolutionMiddleware** → filtro de permiso del endpoint.

1. **Compañía (tenant):** el claim `compania` del token se busca en la base directorio (`CoreBancarioDir`, `dir.CLIENTE_SAAS`), que entrega la cadena de conexión de la base de esa compañía. En personas el claim sale de un atributo del usuario; en integraciones, de un mapper fijo que se crea junto con el client.
2. **Actor:**
   - **Persona:** el `sub` del token se busca en `sec.USUARIO.subject_iam` (usuario activo, tipo FISICO).
   - **Integración:** el `azp` / `client_id` se busca en `sec.USUARIO_INTEGRACION.client_id_iam`; la integración debe estar activa y su **usuario asociado** (usuario técnico, tipo INTEGRACION) también.
3. **Permisos:** se cargan los códigos de permiso de los roles del usuario (el de la persona, o el usuario técnico de la integración). Cada endpoint declara el permiso que exige.

**Usuario técnico:** `sec.USUARIO` con `tipo_usuario = 'INTEGRACION'`. No tiene contraseña ni cuenta en Keycloak y **no puede iniciar sesión** (el login lo rechaza). Se crea en *Administración → Usuarios* con tipo «Integración». Cada integración tiene un *Usuario asociado* obligatorio; varias integraciones pueden compartir uno o cada una tener el suyo (p. ej. CRM, Cajas y Banca en línea con permisos distintos). Los permisos se dan con roles, igual que a una persona. Migración: `mejoras_ronda12.sql` crea un usuario técnico por integración existente con el rol `INTEGRACION_ALTA_CLIENTES` (solo `CLI_ALTA`).

**Canal:** cada integración tiene un **canal fijo** (lista `CANAL`). Cuando da de alta un cliente, el servidor asigna ese canal; el valor que envíe quien llama se ignora. Sin canal, el alta se rechaza. El canal no forma parte de la estructura organizativa.

## 4. Autorización

- Permisos = objetos de autorización (`sec.OBJETO_AUTORIZACION`: `CLI_ALTA`, `CLI_CONSULTA`, `CLI_EDICION`, `GES_APROBAR`...) agrupados en roles; los roles se asignan a usuarios (personas o técnicos).
- **Denegado por defecto:** una integración nueva no puede hacer nada hasta que su usuario técnico tenga un rol con el permiso. Ejemplo: alta de clientes → `CLI_ALTA`; consulta → `CLI_CONSULTA`.
- **Enmascaramiento de datos [Pendiente]:** cuando se construya, aplicará igual a integraciones, porque su usuario técnico es un usuario más con roles.
- Operaciones de aprobación (`aprobar`, `rechazar`, `devolver`) no las ejecutan integraciones: las resuelven personas.

## 5. Catálogo de endpoints

La fuente de verdad es **Swagger / OpenAPI**: `GET /swagger` (interfaz con botón *Authorize* que pide un access token) y `/swagger/v1/swagger.json`. Este documento solo resume los grupos y el permiso que exigen.

| Grupo | Rutas base | Permiso |
|---|---|---|
| Interlocutores (clientes) | `/api/interlocutores` | `CLI_ALTA`, `CLI_CONSULTA`, `CLI_EDICION` según operación |
| Gestiones | `/api/gestiones` | consulta autenticada; aprobar/rechazar/devolver `GES_APROBAR`; reasignación `GES_REASIGNAR` |
| Reglas y grupos de aprobación | `/api/aprobacion` | `GES_REGLAS`, `GES_GRUPOS` |
| Seguridad (roles y permisos) | `/api/seguridad` | `SEG_ADMINISTRACION` |
| Usuarios | `/api/usuarios` | `SEG_ADMINISTRACION` |
| Estructura organizativa | `/api/organizacion/unidades` | `ORG_ADMINISTRACION` |
| Usuarios de integración | `/api/integraciones` | `INT_ADMINISTRACION` |
| Monedas, tipo de cambio, feriados, parámetros | `/api/monedas`, `/api/tipos-cambio`, `/api/feriados`, `/api/parametros`, `/api/calendario` | permisos propios (ver `Administracion_Transversal.md`) |
| Auditoría | `/api/auditoria` | `AUD_CONSULTA` |
| Listas de valores y catálogos | `/api/listas-valores`, `/api/catalogos` | `LOV_*`; catálogos solo autenticado |
| Numeración, división territorial | `/api/numeracion`, `/api/division-territorial` | `NUM_ADMINISTRACION`, `DIV_ADMINISTRACION` |
| Sesión del usuario y notificaciones | `/api/yo`, `/api/notificaciones` | autenticado |

**Alta de cliente por integración:** `POST /api/interlocutores` con permiso `CLI_ALTA`. Hoy el alta por integración es **directa** (equivale a aprobación automática; el cliente se crea al instante). Desde el Backoffice, el alta nace como gestión y la regla de aprobación decide si se aprueba sola o espera a una persona. El usuario técnico deja listo el camino para que una integración sea solicitante de gestiones **[Pendiente]**.

## 6. Convenciones

- **JSON**, propiedades en camelCase, fechas ISO 8601 (se guardan en UTC).
- **Errores de negocio:** cuerpo `{ "error": "<codigo>", "mensaje": "<texto legible>" }` con el código HTTP que corresponda.
- **Códigos HTTP:**

| Código | Cuándo |
|---|---|
| 200 / 201 / 202 / 204 | Éxito. 201 al crear; 202 cuando el cambio queda pendiente de aprobación; 204 en cambios sin cuerpo |
| 400 | Datos inválidos (`datos_invalidos`), token sin claim `compania` (`token_sin_compania`) |
| 401 | Token ausente, inválido o vencido |
| 403 | Sin permiso (`permiso_denegado`, con el permiso faltante en `codigoPermiso`); identidad válida pero no registrada o inactiva (`identidad_no_registrada`); compañía inexistente o inactiva (`cliente_no_encontrado_o_inactivo`) |
| 404 | Registro inexistente (`no_encontrado`) |
| 409 | Conflicto de negocio: duplicados (`identificacion_duplicada`), una sola gestión pendiente por registro, autoaprobación no permitida, dato cambiado desde la solicitud (`registro_cambio`) |

- **Idempotencia y `correlation_id` [Pendiente]:** el DAD (ADR-009) exige APIs atómicas e idempotentes con identificador de correlación; aún no está implementado en los endpoints actuales.
- **Versionado:** hoy una sola versión (`v1` en Swagger). Regla propuesta: cambios compatibles (campos opcionales nuevos) no cambian la versión; un cambio incompatible se publica bajo `/api/v2/...` y `v1` se mantiene durante un período de convivencia acordado con cada integración **[Por definir]**.

## 7. Eventos y notificaciones

- **Hoy:** sin bus de eventos. Las notificaciones de la campana se derivan en consulta (gestiones por aprobar, gestiones propias resueltas, borradores por vencer) y se consultan cada 60 s.
- **[Pendiente]** Event Mesh con patrón outbox (DAD, sección 9.11): eventos como `GestionCreada`, `GestionAprobada`, `ClienteCreado`; webhooks o mensajería hacia integraciones; notificaciones por correo/SMS a personas y grupos de distribución.

## 8. Integraciones registradas

Se administran en *Administración → Usuarios de integración* (`INT_ADMINISTRACION`): nombre, client_id, canal fijo, usuario asociado, estado. Acciones: crear (devuelve client_id y secret **una sola vez**), regenerar secret (el anterior deja de funcionar al instante), activar/inactivar (deshabilita el client en Keycloak), cambiar canal, cambiar usuario asociado. Cada cambio queda en auditoría (entidad `USUARIO_INTEGRACION`).

Integraciones previstas (a definir con cada consumidor): CRM, cajas/sucursal, banca en línea, onboarding digital, web y móvil, fuente externa de tipo de cambio.

Buenas prácticas para quien integra: guardar el secret en un gestor de secretos (no en código ni en capturas); pedir un token y reutilizarlo hasta que venza; tratar `403` como falta de permiso (se corrige asignando rol al usuario técnico, no reintentando); no enviar el canal, lo fija el servidor.

## 9. Ambientes

| Ambiente | Estado |
|---|---|
| Desarrollo | SQL Server local (`CoreBancario_DEV`, directorio `CoreBancarioDir`), Keycloak en Docker (realm `dev`), API con `dotnet run` (puerto variable; ver Manual de subida), Backoffice en `http://localhost:3000` |
| QA / Producción | **[Pendiente]** URLs, realm por ambiente, gestión de secretos, CORS, TLS y publicación de Swagger (en producción debe quedar restringido o deshabilitado) |

## 10. Pruebas manuales (Bruno / SoapUI / Swagger)

- **Persona:** autenticación OAuth 2.0 *Authorization Code* con PKCE; Callback URL `http://localhost:3000/*` está permitida en el client; scope `openid offline_access` para obtener refresh token.
- **Integración:** *Client Credentials*; dejar el campo Scope vacío; el Client Secret va solo en su campo.
- **Swagger:** `Authorize` → pegar el access token.
- Si la respuesta es `403 permiso_denegado`, falta asignar un rol con ese permiso al usuario (persona o técnico de la integración).

## 11. Pendiente de esta área

Idempotencia y `correlation_id`; eventos/webhooks; versionado formal; enmascaramiento de datos por rol; límites de uso (rate limiting); iss+sub como llave de identidad y abstracción del proveedor de identidad (para no atar el core a Keycloak); campo de tenant/pool en el directorio; ambientes QA y producción; integración de tipo de cambio externa.
