# Seguridad: usuarios, roles y estructura organizativa

**Actualizado:** 2026-10-09. Scripts: `seguridad_organizacion.sql`, `mejoras_ronda6.sql` (puesto y grupos), `mejoras_ronda12.sql` (tipo de usuario y usuario técnico).

## 1. Decisiones
- **Estructura organizativa = árbol de unidades** (`sec.DEPARTAMENTO`, con código, tipo y responsable). El tipo (Sucursal, Agencia, Departamento, Equipo) sale de la lista `TIPO_UNIDAD_ORG`, editable en Listas de Valores. Una unidad puede inactivarse solo sin subunidades ni usuarios activos. `permite_registros` indica en qué unidades se pueden registrar clientes (`mejoras_ronda4.sql`).
- **Unidad en los registros:** `cli.INTERLOCUTOR.id_unidad` (sucursal del cliente; por defecto la del usuario que da el alta; obligatoria si existen unidades activas; cambios por Gestión). Cuentas y créditos usarán el mismo patrón.
- **Canales (WEB, MÓVIL...) no son parte de la estructura**: son un atributo de cada operación (lista `CANAL`). Una unidad virtual «Banca Digital» sirve para dar dueño a clientes captados digitalmente.
- **Tipo de usuario (2026-10-09):** `sec.USUARIO.tipo_usuario`:
  - **FISICO**: persona; inicia sesión; se crea también en Keycloak.
  - **INTEGRACION**: **usuario técnico**; no tiene contraseña, correo real ni cuenta en Keycloak y **no puede iniciar sesión** (la resolución del usuario en el login excluye este tipo). Existe para darle permisos a las integraciones por medio de roles. No aparece como superior, aprobador, responsable de unidad ni miembro de grupo. El tipo no se cambia después de crear el usuario.
- **Usuarios físicos:** se crean desde el Backoffice **en Keycloak** (Admin API, cuenta de servicio de `core-bancario-backend`) y en `sec.USUARIO` (enlace por `subject_iam`). Contraseña temporal aleatoria mostrada una sola vez; el usuario debe cambiarla al ingresar. Inactivar deshabilita en Keycloak y cierra sus sesiones.
- **Usuarios técnicos:** se crean en la misma pantalla con tipo «Integración» (código, nombre y roles). `subject_iam` guarda un marcador único `svc:<guid>` que nunca coincide con un token. Cada integración (ver `Interfaces_e_Integracion.md`) tiene un **Usuario asociado** obligatorio; la API evalúa los permisos de la integración con los roles de ese usuario. No se puede inactivar un usuario técnico que usen integraciones activas.
- **Integraciones sin acceso total:** desde 2026-10-09 una integración no tiene acceso total; solo puede lo que permitan los roles de su usuario técnico (`403 permiso_denegado` si no). Rol inicial `INTEGRACION_ALTA_CLIENTES` (solo `CLI_ALTA`), asignado por `mejoras_ronda12.sql` a los usuarios técnicos creados para las integraciones existentes.
- **Superior directo:** `sec.USUARIO.id_superior`. Es el aprobador por omisión de las gestiones de sus subordinados (nivel de tipo `SUPERIOR` del motor de aprobación; ver `Motor_de_Aprobacion.md`), o quien tenga `GES_APROBAR_TODAS`; sin superior basta `GES_APROBAR`. Desactivable con `Gestiones:AprobacionPorSuperior=false`. La campana solo avisa de lo que el usuario puede resolver.
- **Puesto:** `sec.USUARIO.puesto`, variable disponible para las reglas de aprobación.
- **Grupos de distribución** (`sec.GRUPO_USUARIOS`): listas de usuarios independientes de la estructura; ver `Motor_de_Aprobacion.md`.

## 2. Reglas
- Usuario: 3-50 caracteres `[a-z0-9._-]`; correo válido y único (en usuarios técnicos el correo es un marcador interno `<codigo>@integracion.local`); superior activo, de tipo físico, distinto del usuario y sin ciclos; unidad activa.
- No se puede inactivar: a uno mismo, al último administrador de seguridad activo (los usuarios técnicos no cuentan como administradores), a quien tenga subordinados activos, sea responsable de unidades activas o tenga gestiones pendientes.
- Cambios de roles (desde el usuario o desde el rol) no pueden dejar al sistema sin administradores de seguridad.
- Si falla el registro local tras crear en Keycloak, se elimina el usuario recién creado (compensación).
- Todo cambio queda en `aud.LOG_AUDITORIA` (entidades `USUARIO`, `UNIDAD_ORG` y `ROL`).

## 3. Roles y permisos (pantalla Seguridad)
- La pantalla muestra solo la **lista de roles** (código, nombre, estado, permisos asignados, cantidad de usuarios). El **código del rol es un hipervínculo** que abre la ficha en solo lectura; «Editar» está en la ficha y en la barra de la lista (entra directo a editar).
- La ficha del rol tiene dos pestañas: **Rol** (datos y árbol de permisos por categoría) y **Usuarios** (quién tiene el rol; en edición se agregan y quitan usuarios, personas o técnicos). Todo se guarda junto con «Guardar».
- **Auditoría de roles (2026-10-09):** crear un rol queda como alta; un cambio de permisos queda como **un solo renglón** (campo «Permisos», «Anterior» = permisos quitados, «Nuevo» = permisos agregados).

## 4. API
- Unidades (`ORG_ADMINISTRACION`): `GET/POST /api/organizacion/unidades`, `PUT /{id}`, `PUT /{id}/estado`. Catálogos (autenticado): `GET /api/catalogos/unidades`, `GET /api/catalogos/usuarios` (solo personas activas).
- Usuarios (`SEG_ADMINISTRACION`): `GET/POST /api/usuarios` (el cuerpo trae `tipo`: FISICO o INTEGRACION), `GET/PUT /{id}`, `PUT /{id}/estado`, `PUT /{id}/roles`, `POST /{id}/restablecer-password` (no aplica a usuarios técnicos), `GET /estado-integracion`.
- Seguridad (`SEG_ADMINISTRACION`): `GET /api/seguridad/objetos-autorizacion`, `GET/POST /api/seguridad/roles`, `GET /roles/{id}`, `PUT /roles/{id}/objetos`.

## 5. Configuración de Keycloak (una vez)
1. Cliente `core-bancario-backend`: *Client authentication* ON y *Service accounts roles* ON; copiar el secreto de la pestaña Credentials.
2. Pestaña *Service accounts roles* → *Assign role* → filtrar por clientes → `realm-management`: `manage-users`, `view-users`, `query-users`, y para crear y administrar clients de integración: `manage-clients`, `view-clients`, `query-clients`.
3. Realm settings → General → *Unmanaged Attributes* = Enabled (para guardar el atributo `compania` que usa el token). El usuario nuevo se crea con ese atributo = código del cliente SaaS (p. ej. DEV).
4. `appsettings.Development.json`: `"Keycloak": { "Admin": { "BaseUrl": "http://localhost:8080", "Realm": "dev", "ClientId": "core-bancario-backend", "ClientSecret": "<secreto>" } }` (no versionar el secreto).

## 6. Pantallas (Administración)
**Usuarios**: tabla con columna Tipo; el **nombre es un hipervínculo** que abre la ficha en solo lectura (con «Editar»); «Editar» de la barra entra directo a editar. La ficha tiene dos pestañas: **Información** y **Roles** (todos los roles existentes en tabla, con casilla en los que tiene el usuario). Nuevo, activar/inactivar y restablecer contraseña. **Estructura organizativa** (árbol con nueva/editar/activar/inactivar). El wizard de cliente pide la **Unidad (sucursal)**; el 360° la muestra.

## 7. Pendiente
Visibilidad de datos por unidad, purga/expiración de contraseñas y MFA (política en Keycloak), validar `iss`+`sub` y abstraer el proveedor de identidad, tema visual del login de Keycloak, enmascaramiento de datos por rol (aplicará también a usuarios técnicos).
