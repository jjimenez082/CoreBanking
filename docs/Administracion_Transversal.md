# Administración transversal — canal, monedas, tipo de cambio, feriados, parámetros, auditoría

**Actualizado:** 2026-10-09. Script: `administracion_transversal.sql` (re-ejecutable, sin `USE`; requiere los scripts 1-7 del Manual de subida). Usuarios de integración: ver también `Interfaces_e_Integracion.md` y `mejoras_ronda12.sql`.

## 1. Principios

- Todas las opciones administrativas **se auditan** (`aud.LOG_AUDITORIA`: quién, cuándo, entidad, registro, campo, valor anterior y nuevo, y la gestión cuando aplica) y se controlan con **permisos** (`sec.OBJETO_AUTORIZACION`), asignados por defecto al rol `ADMINISTRADOR_SEGURIDAD`; luego se reparten por rol desde Seguridad.
- Lo que cambia datos de negocio o con efecto contable (tipo de cambio) pasa por **gestión** con aprobación (según las reglas de aprobación; ver `Motor_de_Aprobacion.md`). Los catálogos y parámetros se cambian directo, con auditoría.

## 2. Canal y usuarios de integración

- Lista de valores `CANAL` (SUCURSAL, WEB, MOVIL, CRM, ONBOARDING_DIGITAL, API); se mantiene en Listas de Valores (permiso `LOV_CANAL`).
- **No es parte de la estructura organizativa**: la unidad dice *dónde* se atiende al cliente; el canal, *por dónde* llegó. Ambos se guardan (`id_unidad`, `id_canal_origen`).
- `cli.INTERLOCUTOR.id_canal_origen`: se fija en el alta. Backoffice: **siempre Sucursal** y el usuario no lo elige (el servidor lo asigna). API: el servidor usa el canal fijo del usuario de integración (`sec.USUARIO_INTEGRACION.id_canal`); si no tiene, el alta se rechaza. El valor enviado por quien llama se ignora en ese caso.
- Corregirlo después: edición del cliente → gestión con el campo `id_canal_origen` (se ve con nombres en la bandeja y queda en auditoría).
- **Pantalla Usuarios de integración** (`INT_ADMINISTRACION`), cuentas de servicio con **client_id y client_secret** (OAuth 2.0 Client Credentials):
  - **Crear:** nombre, identificador, canal fijo y **usuario asociado** (obligatorio). Crea el client en Keycloak (con el claim `compania` en el token) y lo registra aquí. El `client_id` es `{código compañía}-{identificador}`. Muestra client_id, secret y URL del token **una sola vez**.
  - **Usuario asociado:** un usuario técnico (`sec.USUARIO` tipo INTEGRACION, activo, de la misma compañía). Sus roles determinan lo que la integración puede hacer: sin permiso, `403 permiso_denegado`. Varias integraciones pueden compartir un usuario o tener cada una el suyo. Se cambia con «Asignar usuario» (auditado, campo «Usuario asociado»).
  - **Regenerar secret** (el anterior deja de funcionar al instante), **activar/inactivar** (deshabilita el client en Keycloak; la API rechaza el token), **asignar canal**.
  - Si la integración o su usuario técnico están inactivos, la API rechaza el token (`403 identidad_no_registrada`).
  - API: `GET/POST /api/integraciones`, `POST /{id}/regenerar-secret`, `PUT /{id}/estado`, `PUT /{id}/canal`, `PUT /{id}/usuario`, `GET /usuarios-tecnicos`, `GET /estado-integracion`.
  - Los clientes existentes quedan con canal Sucursal.

## 3. Monedas (`gen.MONEDA`, permiso `MON_ADMINISTRACION`)

Código ISO (3 letras, no editable), nombre, símbolo, decimales (0-6), estado y **moneda base** (una por compañía, índice único filtrado). Reglas: la base no se inactiva; la base solo puede cambiarse mientras no existan tipos de cambio; no se inactiva una moneda con cargas de tipo de cambio pendientes. Semilla: CRC (base) y USD si la compañía no tiene monedas. Combo para otras pantallas: `GET /api/catalogos/monedas`.

## 4. Tipo de cambio (`gen.TIPO_CAMBIO`, permisos `TC_CONSULTA` y `TC_CARGA`)

- Tres valores por moneda y fecha, contra la moneda base: **compra, venta y valor contable** (columna `contable`; es el valor que usarán los cálculos contables) (decimal 18,6; compra ≤ venta). Una fila por (moneda, fecha de vigencia); **rige desde su fecha hasta la siguiente cargada**.
- **Carga manual con aprobación**: `POST /api/tipos-cambio/solicitud` crea la gestión `TC_CARGA` (entidad `TIPO_CAMBIO`, llave `idMoneda|fecha`; una pendiente por moneda y fecha). Al aprobarse se inserta (o corrige, si ya existía esa fecha, verificando que los valores anteriores no cambiaron) y se audita con el número de gestión. La solicita una persona (las integraciones aún no pueden solicitar gestiones).
- Control de variación: el parámetro `TC_VARIACION_MAXIMA_PORC` (10 por defecto; 0 = sin límite) rechaza al solicitar un valor contable que varíe más que ese porcentaje respecto a la vigente anterior.
- `origen` = Manual; queda reservado para una fuente externa (p. ej. BCCR) que más adelante cree la misma gestión o cargue directo con su propio usuario de integración.
- API: `GET /api/tipos-cambio` (historial, filtros), `GET /api/tipos-cambio/vigentes?fecha=`.

## 5. Calendario de feriados (`gen.FERIADO`, permiso `CAL_ADMINISTRACION`)

Feriados por país (lista PAIS), con opción «se repite cada año». Los días no hábiles de la semana vienen del parámetro `DIAS_NO_HABILES` (SAB,DOM por defecto). Consultas abiertas a cualquier usuario autenticado, para cuentas y créditos: `GET /api/calendario/dia-habil?idPais=&fecha=` y `GET /api/calendario/proximo-habil?idPais=&desde=&dias=` (dias=0: primer día hábil desde la fecha; dias>0: suma esa cantidad de días hábiles).

## 6. Parámetros generales (`gen.PARAMETRO`, permiso `PAR_ADMINISTRACION`)

Código, nombre, descripción, categoría, tipo (Texto/Entero/Decimal/Booleano), valor y valor por defecto. Se cambia el valor (validado por tipo y por reglas del parámetro) o se restablece el defecto; cada cambio se audita. Semilla: `DIAS_NO_HABILES`, `ZONA_HORARIA`, `TC_VARIACION_MAXIMA_PORC`. Nuevos parámetros se agregan por script junto con la funcionalidad que los usa.

## 7. Consulta de auditoría (`AUD_CONSULTA`)

`GET /api/auditoria` con filtros (entidad, llave, usuario, acción, fechas, gestión, texto) y paginación de 50; `GET /api/auditoria/entidades` (devuelve código y nombre). Solo lectura.

- **Nombres funcionales (ronda 5):** en la base se guarda el nombre técnico del campo y los valores como id; al mostrarlos se traducen (`AuditoriaLegible`): campo («Unidad (sucursal)», «Dirección nueva #1 · Tipo», «Usuario asociado», «Permisos»), valor (ids de listas, unidades, usuarios y monedas por su descripción; 1/0 como Sí/No) y entidad («Cliente» en lugar de `INTERLOCUTOR`). Aplica también al historial.
- **Altas:** crear un registro dependiente de un cliente (dirección, identificación, contacto) por gestión deja **una sola línea «Alta»** con el resumen del registro, en lugar de una línea «Cambio» por campo. Script `mejoras_ronda5.sql` pasa a «Alta» las líneas antiguas de registros nuevos.
- **Quién actuó:** si actuó una integración, el registro guarda el usuario técnico y la integración (`id_usuario_integracion`).
- **Pantalla (2026-10-08):** tabla con scroll horizontal, columnas sin cortar el texto y **exportación a Excel**.
- Limitaciones conocidas: el filtro de texto busca por el nombre técnico del campo; las fechas se guardan en UTC y se muestran en la zona del navegador; la consulta misma no se audita (AUD-004 pendiente); el filtro de usuario solo lista personas.

## 8. Entidades de auditoría

`INTERLOCUTOR`, `USUARIO`, `UNIDAD_ORG`, `MONEDA`, `TIPO_CAMBIO`, `FERIADO`, `PARAMETRO`, `USUARIO_INTEGRACION`, `ROL` (desde 2026-10-09: alta de rol y cambios de permisos), `REGLA_APROBACION`, `GRUPO_USUARIOS`, `TIPO_GESTION`, `GESTION` (incluye reasignaciones de aprobador).

## 9. Pendiente

Fuente externa de tipo de cambio; conversión de montos entre monedas (servicio que use compra/venta/valor contable); calendario por sucursal; auditoría de consultas y de lecturas sensibles; asignar canal por producto en cuentas y créditos.

**Exportación de tablas:** la exportación a **Excel** ya existe en las tablas principales (`TablaDatos`: auditoría, gestiones, etc.). Queda por definir: formatos CSV y TXT, exportar todo el resultado del filtro en tablas paginadas, permiso propio, auditoría de la exportación (datos sensibles) y respeto de enmascaramientos.

## Ronda de mejoras (2026-10-06, script `mejoras_ronda2.sql`)
- Botón del tipo de cambio: **«Ingreso de tipo de cambio»** (permiso `TC_CARGA`, renombrado). El formulario valida los números antes de enviar (acepta `520,50` o `1.234,56`); el servidor acepta números como texto y los errores de binding se muestran con su detalle (antes salía solo «Error 400»).
- `gen.TIPO_CAMBIO.referencia` → `contable` (se actualizan también los detalles de gestión y la auditoría ya registrados).
- Gestiones **Devueltas**: ver `Interlocutores_Modelo_y_API.md` (sección Gestiones). El ingreso de tipo de cambio devuelto se corrige desde la bandeja (`/tipo-cambio?corregir=N`).
- Administración se abre como **launchpad** de aplicaciones (`/administracion`), igual que Interlocutores; el menú lateral muestra un solo acceso. Cada mosaico se ve solo con su permiso. Se declaran en `zonasAdministracion.ts`.
- Edición de rol: permisos como **árbol** por categoría (casilla de categoría marca todos sus permisos, búsqueda, expandir/contraer).
- Sesión: se corrigió la causa de los cierres «en cualquier momento» (un interceptor de axios por cada token renovado hacía que el más viejo pisara el token vigente). Ahora hay un único interceptor; un 401 aislado intenta renovar el token antes de pedir reingreso.
