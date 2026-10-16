# Manual de subida — Core Bancario

Documento vivo. Lo vamos actualizando cada vez que agregamos un componente nuevo (otro microservicio, otra app de frontend, otra dependencia de infraestructura). Siempre levantar en el orden en que aparecen las secciones.

**Última actualización:** 2026-10-09 (motor de aprobación, reasignación, usuarios técnicos para integraciones, Swagger, pantallas de Seguridad y Usuarios con ficha) — componentes activos: SQL Server, Keycloak (Docker), API .NET (`CoreBancario.Api`), Backoffice React (`core-bancario-backoffice`).

**Estado:** Backoffice verificado de punta a punta — login vía Keycloak, Dashboard, bandeja de gestiones, RBAC real (SEG-002), módulo **Interlocutores** (launchpad, wizard de alta/edición, Consulta 360°, borradores, campana), **Administración** (Seguridad, Usuarios, Estructura organizativa, Listas de Valores, numeración, división territorial, monedas, tipo de cambio, feriados, parámetros, auditoría), **motor de aprobación** (reglas, grupos, reasignación) y **usuarios de integración** con client_id/secret y permisos por usuario técnico. Ver `Interlocutores_Modelo_y_API.md`, `Motor_de_Aprobacion.md` e `Interfaces_e_Integracion.md`.

---

## 0. Resumen rápido (cuando ya conoces los pasos)

| # | Componente | Comando | Verificación |
|---|---|---|---|
| 1 | SQL Server | (servicio de Windows, normalmente automático) | Conectar con SSMS |
| 2 | Docker Desktop | Abrir la app manualmente | Ícono de la ballena sin animación de carga |
| 3 | Keycloak | `cd infra\keycloak-dev` → `docker compose up -d` | http://localhost:8080 carga |
| 4 | API .NET | `cd backend\CoreBancario.Api` → `dotnet run` (la primera vez, o si cambian paquetes: `dotnet restore`) | `http://localhost:<puerto>/health` → `{"status":"ok"}`; `http://localhost:<puerto>/swagger` |
| 5 | Backoffice | `cd frontend\core-bancario-backoffice` → `npm run dev` (si cambian dependencias: `npm install` antes) | http://localhost:3000 redirige a Keycloak |

El puerto de la API varía entre corridas de `dotnet run` (la última vez fue `5003`) — confirma el puerto real en la consola y, si cambió, actualiza `VITE_API_BASE_URL` en `frontend/core-bancario-backoffice/.env.local` antes de abrir el Backoffice.

**URL de ingreso:** http://localhost:3000 (redirige a Keycloak, `localhost:8080`). Usuarios de pruebas: `qa.arquitecto`, `qa.analista`, `qa.servicioclieinte` / `qa.serviciocliente`.

---

## 1. SQL Server

- Es un servicio de Windows; normalmente arranca solo.
- Verificar: abrir **SSMS** y conectar al servidor local. Si no conecta, revisar en `services.msc` que `SQL Server (MSSQLSERVER)` (o tu instancia) esté "En ejecución".

### 1.1 Scripts de base de datos (`CoreBancario_DEV`)

Todos son re-ejecutables. Orden:

1. `modelo_fisico_clientes_mvp1.sql` y `modelo_fisico_transversal_minimo.sql` — solo si la BD es nueva.
2. `listas_valores_admin.sql` — administración de Listas de Valores (agrega `es_sistema`).
3. `edicion_clientes_gestion.sql` — soporte de gestión de modificación.
4. `interlocutor_multiples.sql` — **(2026-10-05)** tablas de identificaciones, direcciones, contactos y borradores; migra la identificación única como principal y elimina las columnas viejas de `cli.INTERLOCUTOR`; crea las LOV nuevas y los permisos `LOV_*`. **Hacer respaldo antes.**
5. `numeracion_rangos.sql` — **(2026-10-05)** motor de numeración: tabla de rangos, columna `numero` en `cli.INTERLOCUTOR` (numera a los clientes existentes), permiso `NUM_ADMINISTRACION`. Sin `USE`: correr con `CoreBancario_DEV` seleccionada.
6. `division_territorial.sql` — **(2026-10-05)** direcciones por país: lista PAIS, estructura de dirección por país, división territorial (CR y USA), migra las direcciones existentes a Costa Rica y elimina provincia/cantón/distrito. Requiere los scripts 3 y 4. **Hacer respaldo antes.** Ver `Division_Territorial_y_Direcciones.md`.
7. `seguridad_organizacion.sql` — **(2026-10-06)** estructura organizativa (unidades), username/nombres en usuarios, `id_unidad` en `cli.INTERLOCUTOR`; permisos `ORG_ADMINISTRACION` y `GES_APROBAR_TODAS`. **Hacer respaldo antes.** Ver `Seguridad_Usuarios_y_Organizacion.md` (incluye la configuración de Keycloak necesaria para crear usuarios).
8. `administracion_transversal.sql` — **(2026-10-06)** lista CANAL y `id_canal_origen` en `cli.INTERLOCUTOR`, `id_canal` en `sec.USUARIO_INTEGRACION`, tablas `gen.MONEDA` (semilla CRC base + USD), `gen.TIPO_CAMBIO`, `gen.FERIADO`, `gen.PARAMETRO` (semilla de 3 parámetros), tipo de gestión `TC_CARGA` y los permisos `MON_ADMINISTRACION`, `TC_CONSULTA`, `TC_CARGA`, `CAL_ADMINISTRACION`, `PAR_ADMINISTRACION`, `AUD_CONSULTA`, `INT_ADMINISTRACION`, `LOV_CANAL`. **Hacer respaldo antes.** Ver `Administracion_Transversal.md`.
9. `mejoras_ronda2.sql` — **(2026-10-06)** `detalle2`/`detalle3` en direcciones (3 líneas de dirección exacta), estado **Devuelta/Corregida** y datos de devolución en `apr.GESTION` (`Devuelto` en `apr.GESTION_APROBACION`), renombre `referencia`→`contable` en `gen.TIPO_CAMBIO` y permiso `TC_CARGA` renombrado a «Ingreso de tipo de cambio». Requiere el script 8. **Hacer respaldo antes.**
10. `mejoras_ronda3.sql` — trazabilidad de correcciones de gestiones (`id_gestion_origen`, `id_gestion_correccion`, `numero_correccion`). Requiere el 9.
11. `mejoras_ronda4.sql` — `sec.DEPARTAMENTO.permite_registros` (unidades donde se registran clientes).
12. `mejoras_ronda5.sql` — auditoría: pasa a «Alta» las líneas antiguas de registros dependientes nuevos.
13. `mejoras_ronda6.sql` — **motor de aprobación (fase 1)**: grupos de distribución, puesto del usuario, reglas de aprobación (reglas, condiciones, niveles), niveles en cada gestión y reglas iniciales de `CLI_MODIFICACION_DATOS` y `TC_CARGA`. Ver `Motor_de_Aprobacion.md`.
14. `mejoras_ronda7.sql` — número de negocio de las gestiones (rango de numeración `GESTION`).
15. `mejoras_ronda8.sql` — **reasignación de aprobador**: permiso `GES_REASIGNAR` (para los roles con `GES_APROBAR_TODAS`) y `id_usuario_original` en `apr.GESTION_APROBACION`.
16. `mejoras_ronda9.sql` — comentario del aprobador de hasta 1000 caracteres.
17. `mejoras_ronda10.sql` — tipo de gestión `CLI_ALTA` (el alta del Backoffice nace como gestión) con sus reglas iniciales.
18. `mejoras_ronda11.sql` — permisos `GES_REGLAS` y `GES_GRUPOS` (pantallas de reglas y grupos) para `ADMINISTRADOR_SEGURIDAD`.
19. `mejoras_ronda12.sql` — **(2026-10-09)** tipo de usuario (`sec.USUARIO.tipo_usuario`: FISICO | INTEGRACION), usuario asociado de cada integración (`sec.USUARIO_INTEGRACION.id_usuario`), rol `INTEGRACION_ALTA_CLIENTES` (solo `CLI_ALTA`) y un usuario técnico por cada integración existente. **Desde esta ronda las integraciones no tienen acceso total**: su acceso depende de los roles de su usuario técnico. **Hacer respaldo antes.** Ver `Seguridad_Usuarios_y_Organizacion.md` e `Interfaces_e_Integracion.md`.

---

## 2. Docker Desktop

- No arranca solo — abrirlo manualmente y esperar a que el ícono de la ballena deje de animarse.

---

## 3. Keycloak (contenedor de desarrollo, SEG-010)

```
cd infra\keycloak-dev
docker compose up -d
```

**Verificación:** http://localhost:8080 carga; en el Admin Console el realm `dev` conserva el usuario `qa.arquitecto` y el cliente `core-bancario-frontend`. Si no levanta: `docker compose logs -f`.

**Para crear usuarios e integraciones desde el Backoffice:** el cliente `core-bancario-backend` necesita *Service accounts roles* con los roles de `realm-management` indicados en `Seguridad_Usuarios_y_Organizacion.md` (sección 5), incluidos `manage-clients`, `view-clients` y `query-clients` para integraciones. Cada integración creada aparece como un client propio (`{código compañía}-{identificador}`) con *Client authentication* y *Service accounts* activos.

---

## 4. API .NET (`CoreBancario.Api`)

```
cd backend\CoreBancario.Api
dotnet restore        (primera vez, o si cambió el .csproj: Swagger usa Swashbuckle.AspNetCore)
dotnet run
```

Esperar la línea `Now listening on: http://localhost:xxxx` (el puerto puede variar).

**Verificación:**
```powershell
Invoke-RestMethod -Uri "http://localhost:<puerto>/health"
```
Debe responder `status: ok`, `servicio: CoreBancario.Api`. Documentación interactiva de la API (Swagger): `http://localhost:<puerto>/swagger`; el botón *Authorize* pide un access token. Comandos de prueba completos en `API_Minima_Backend_DotNet.md`.

**Configuración (`appsettings.json`):** además de `Keycloak`, `ConnectionStrings` y `Cors`, existen `"Borradores": { "DiasVigencia": 30 }` (vigencia de los borradores del wizard) y `Gestiones:PermitirAutoaprobacion` / `Gestiones:AprobacionPorSuperior` (ver `Motor_de_Aprobacion.md`; la autoaprobación es solo para pruebas).

**CORS:** la API necesita el middleware de CORS habilitado (ver "Problemas conocidos").

**RBAC (SEG-002):** los endpoints exigen permisos reales (`CLI_ALTA`, `CLI_CONSULTA`, `CLI_EDICION`, `GES_APROBAR`, `SEG_ADMINISTRACION`, `LOV_*`...). Sin roles en `sec.USUARIO_ROL` la API responde `403 permiso_denegado`. **Esto aplica también a las integraciones**: se evalúan con los roles de su usuario técnico.

### 4.1 Sembrar roles de prueba (una sola vez, o al recrear la BD)

```
sqlcmd -S localhost -d CoreBancario_DEV -i sembrar_roles_clientes.sql
```

Crea `ANALISTA_CLIENTES`, `OFICIAL_CUMPLIMIENTO` y `ADMINISTRADOR_SEGURIDAD` (con `SEG_ADMINISTRACION`), y se los asigna a `qa.arquitecto`. El `SELECT` final debe mostrar 6 filas. Es seguro volver a correrlo. Después, roles y permisos se administran desde la pantalla de Seguridad.

---

## 5. Backoffice (`core-bancario-backoffice`, React + Vite)

```
cd frontend\core-bancario-backoffice
npm install           (si se agregaron dependencias; la exportación a Excel usa write-excel-file)
npm run dev
```

Si el puerto de la API cambió, actualizar `VITE_API_BASE_URL` en `.env.local`.

**Antes del primer uso:** copiar `.env.example` a `.env.local` (no viaja en el zip). Vite solo lee `.env.local` al arrancar.

**Verificación:**
- http://localhost:3000 redirige a la transición de marca (`/BD Core`) y luego a Keycloak.
- Ingresar con `qa.arquitecto`; carga el Dashboard con la bandeja de gestiones y "Tus permisos (RBAC)".
- Menú lateral: Dashboard, **Interlocutores**, Gestiones y el grupo **Administración** (según permiso).
- Arriba: búsqueda global, campana de notificaciones y usuario.

### 5.1 Seguridad (roles y permisos)

**Administración → Seguridad** (requiere `SEG_ADMINISTRACION`): lista de roles. El código del rol abre su ficha (solo lectura, con «Editar»); «Editar» de la barra entra directo a editar. La ficha tiene las pestañas **Rol** (permisos en árbol) y **Usuarios** (quién tiene el rol; en edición se agregan o quitan). Todo se guarda con «Guardar» y los cambios de permisos quedan en auditoría.

### 5.2 Usuarios y estructura organizativa

**Administración → Usuarios** (`SEG_ADMINISTRACION`): el nombre abre la ficha (pestañas **Información** y **Roles**); «Editar» de la barra entra directo. Tipo de usuario **Físico** (persona, se crea en Keycloak con contraseña temporal) o **Integración** (usuario técnico, no inicia sesión; sirve de «usuario asociado» de las integraciones). **Estructura organizativa** (`ORG_ADMINISTRACION`). Requieren la configuración de Keycloak descrita en `Seguridad_Usuarios_y_Organizacion.md`.

### 5.3 Usuarios de integración

**Administración → Usuarios de integración** (`INT_ADMINISTRACION`). Orden recomendado para conectar un sistema:
1. Crear el usuario técnico en **Usuarios** (tipo Integración) y darle los roles con los permisos que necesita (p. ej. `INTEGRACION_ALTA_CLIENTES`; para consultar clientes, un rol con `CLI_CONSULTA`).
2. Crear la integración (nombre, identificador, canal fijo, usuario asociado). Copiar el client_id, el secret y la URL del token: **el secret se muestra una sola vez**.
3. En el sistema que integra (o en Bruno/SoapUI): `POST` a la URL del token con `grant_type=client_credentials`, `client_id` y `client_secret` (sin `scope`); llamar a la API con `Authorization: Bearer <access_token>`.
Si la API responde `403 permiso_denegado`, falta un permiso en los roles del usuario técnico. Ver `Interfaces_e_Integracion.md`.

### 5.4 Gestiones, reglas de aprobación, grupos y reasignación

- **Gestiones:** bandeja con filtros *Mis gestiones pendientes / Todas mis gestiones / Todas las gestiones*, orden, filtros por columna y exportación a Excel; el número abre el detalle donde se aprueba, rechaza o devuelve.
- **Reglas de aprobación** (`GES_REGLAS`) y **Grupos de distribución** (`GES_GRUPOS`): ver `Motor_de_Aprobacion.md`. Incluye el simulador de reglas.
- **Cambio de asignación de aprobador** (`GES_REASIGNAR`): pasa gestiones pendientes de un aprobador ausente a otro, con motivo obligatorio.

### 5.5 Listas de Valores

**Administración → Listas de Valores** (requiere `LOV_ACCESO`; cada lista tiene su permiso `LOV_<codigo>`). Alimenta los combos del sistema (tipo de identificación, institución emisora, tipo de dirección, tipo de contacto, roles de interlocutor, canal...). Los ítems con `es_sistema=1` no se pueden borrar ni cambiar de código.

### 5.6 Rangos de numeración y división territorial

**Rangos de numeración** (`NUM_ADMINISTRACION`): longitud, inicio, fin y bloque del número de cliente, prospecto, solicitud de crédito y gestión. Ver `Motor_de_Numeracion.md`. **División territorial** (`DIV_ADMINISTRACION`): estructura de dirección por país, divisiones e importación CSV. Ver `Division_Territorial_y_Direcciones.md`.

### 5.7 Administración transversal

**Administración →** Monedas, Tipo de cambio, Calendario de feriados, Parámetros generales, Consulta de auditoría y Usuarios de integración (cada uno con su permiso). El canal de origen del cliente aparece en el wizard y en la Consulta 360°. Ver `Administracion_Transversal.md`.

### 5.8 Sesión

El Backoffice renueva el token solo y cierra la sesión tras 15 min de inactividad (`VITE_INACTIVIDAD_MINUTOS` en `.env.local`, aviso 60 s antes). Si la API responde 401 se muestra «Tu sesión venció».

### 5.9 Patrón estándar de pantallas de mantenimiento

Las pantallas de listado usan `TablaDatos`: barra de comandos arriba, casilla por registro, buscador propio, scroll horizontal, ordenar/filtrar por columna y exportar a Excel. El nombre o código del registro es un **hipervínculo** que abre su ficha (pestañas, solo lectura con «Editar»); «Editar» en la barra entra directo a editar. La **búsqueda global** de la barra superior encuentra módulos y, con `CLI_CONSULTA`, interlocutores (nombre, N.º o identificación) y **siempre abre la Consulta 360°**. Para agregar un módulo al menú basta con agregarlo a `src/navegacion.ts` con su permiso.

### 5.10 Interlocutores (2026-10-05)

Menú **Interlocutores** → launchpad por zonas (Consulta, Clientes, Prospectos, Beneficiarios, Firmantes); los mosaicos se filtran por permisos.
- **Nuevo cliente:** wizard de 5 pasos con N identificaciones, direcciones y contactos validados por tipo. "Guardar borrador" (privado, vence a los 30 días). El alta nace como gestión: según la regla de aprobación el cliente se crea de inmediato o al aprobarse. El N.º se asigna al crearse.
- **Consulta 360°:** datos generales y roles; si es cliente, productos (vacío hasta que existan Cuentas/Créditos); gestiones pendientes y cerradas; relaciones; datos completos con auditoría.
- **Modificar datos:** mismo wizard; envía una gestión con las diferencias que otro usuario aprueba en **Gestiones**. Un interlocutor admite una sola gestión pendiente.
- **Campana:** gestiones por aprobar, gestiones propias resueltas (14 días) y borradores por vencer (≤3 días). Lo "leído" se guarda en el navegador.

---

## Problemas conocidos al subir el ambiente

| Síntoma | Causa | Solución |
|---|---|---|
| `dotnet run` dice "You must install or update .NET" | Falta el runtime `Microsoft.NETCore.App 8.0.x` | `winget install Microsoft.DotNet.Runtime.8` (y `AspNetCore.8` si falta); confirmar con `dotnet --list-runtimes` |
| "No authority or metadataUrl configured on settings" | No existe `.env.local` | Copiar `.env.example` a `.env.local`, completar los 3 valores, reiniciar `npm run dev` |
| Consola del navegador: `net::ERR_FAILED` / "blocked by CORS policy" | Falta CORS en la API | En `Program.cs`, `builder.Services.AddCors(...)` con `WithOrigins("http://localhost:3000").AllowAnyHeader().AllowAnyMethod()` y `app.UseCors();` antes de `app.UseAuthentication()`; en `appsettings.json`, `"Cors": { "OrigenesPermitidos": [ "http://localhost:3000" ] }`. Reiniciar `dotnet run` |
| Endpoint responde `403 permiso_denegado` | El usuario (o el usuario técnico de la integración) no tiene el permiso requerido | Correr `sembrar_roles_clientes.sql` o asignar el rol desde Seguridad / Usuarios |
| Una integración que funcionaba ahora recibe 403 | Desde `mejoras_ronda12.sql` las integraciones dependen de los roles de su usuario técnico | Darle al usuario técnico el rol con el permiso (p. ej. `CLI_CONSULTA`) |
| `403 identidad_no_registrada` en una integración | Integración o usuario técnico inactivo, o sin usuario asociado | Activarlos o asignar el usuario asociado en Usuarios de integración |
| No aparece Administración / Seguridad | Falta `SEG_ADMINISTRACION` | Igual que arriba |
| `sembrar_roles_clientes.sql` falla con `CK_OBJETO_AUTORIZACION_tipo` o `creado_por ... NULL` | Versión vieja del script | Usar la versión corregida (2026-10-01) |
| Login pide cambiar contraseña | Contraseña `temporary: true` | Desmarcar "Temporary" en el Admin Console |
| Keycloak no levanta / puerto 8080 ocupado | Otro proceso lo usa o Docker no está listo | Esperar a Docker; `netstat -ano \| findstr :8080` |
| El Backoffice nuevo da errores 404/500 contra la API vieja (o al revés) | Backoffice y API deben subirse juntos | Actualizar ambos con la misma entrega y correr los scripts pendientes (sección 1.1) |
| `interlocutor_multiples.sql` falla por una tabla/columna inexistente | Faltan scripts previos | Correr antes `listas_valores_admin.sql` y `edicion_clientes_gestion.sql` (sección 1.1) |
| `dotnet build` falla por paquetes (Swashbuckle) | No se hizo restore | `dotnet restore` en la carpeta de la API |
| El Backoffice no compila o falta «Exportar a Excel» | Faltan dependencias nuevas | `npm install`, reiniciar `npm run dev`, Ctrl+F5 |
| Bruno, Client Credentials: error 400 | El campo Scope tiene texto (p. ej. el secret) | Dejar Scope vacío; el secret solo en Client Secret |

---

## Pendientes / próximos componentes

- Tema visual del login de Keycloak (`/BD Core`) y login federado opcional con Microsoft Entra ID.
- Prospecto, Beneficiario y Firmante; KYC y bloqueo (`CLI_KYC`, `CLI_BLOQUEO`).
- Rango de numeración por producto (cuentas y créditos), job de purga de borradores, tabla de notificaciones y tiempo real.
- Servicios de **Cuentas** y **Préstamos**.
- Procesos batch (comisiones/intereses).
- Integración como solicitante de gestiones; enmascaramiento de datos; idempotencia y `correlation_id` en la API; ambientes QA/producción.
- Asistente de alta de compañía (aplicación aparte) y campo de pool/tenant en el directorio.

Cada componente nuevo agrega su propia sección de arranque y verificación en este documento.
