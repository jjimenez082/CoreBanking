# Administración transversal — canal, monedas, tipo de cambio, feriados, parámetros, auditoría

**Actualizado:** 2026-10-06. Script: `administracion_transversal.sql` (re-ejecutable, sin `USE`; requiere los scripts 1-7 del Manual de subida).

## 1. Principios

- Todas las opciones administrativas **se auditan** (`aud.LOG_AUDITORIA`: quién, cuándo, entidad, registro, campo, valor anterior y nuevo, y la gestión cuando aplica) y se controlan con **permisos** (`sec.OBJETO_AUTORIZACION`), asignados por defecto al rol `ADMINISTRADOR_SEGURIDAD`; luego se reparten por rol desde Seguridad.
- Lo que cambia datos de negocio o con efecto contable (tipo de cambio) pasa por **gestión** con aprobación (por el superior directo o `GES_APROBAR_TODAS`, como el resto). Los catálogos y parámetros se cambian directo, con auditoría.

## 2. Canal

- Lista de valores `CANAL` (SUCURSAL, WEB, MOVIL, CRM, ONBOARDING_DIGITAL, API); se mantiene en Listas de Valores (permiso `LOV_CANAL`).
- **No es parte de la estructura organizativa**: la unidad dice *dónde* se atiende al cliente; el canal, *por dónde* llegó. Ambos se guardan (`id_unidad`, `id_canal_origen`).
- `cli.INTERLOCUTOR.id_canal_origen`: se fija en el alta. Backoffice: combo «Canal de origen» (por defecto Sucursal). API: el servidor usa el canal fijo del usuario de integración (`sec.USUARIO_INTEGRACION.id_canal`); si no tiene, el alta se rechaza. El valor enviado por quien llama se ignora en ese caso.
- Corregirlo después: edición del cliente → gestión con el campo `id_canal_origen` (se ve con nombres en la bandeja y queda en auditoría).
- Pantalla **Usuarios de integración** (`INT_ADMINISTRACION`): lista las cuentas de servicio y asigna su canal. Los clientes existentes quedan con canal Sucursal.

## 3. Monedas (`gen.MONEDA`, permiso `MON_ADMINISTRACION`)

Código ISO (3 letras, no editable), nombre, símbolo, decimales (0-6), estado y **moneda base** (una por compañía, índice único filtrado). Reglas: la base no se inactiva; la base solo puede cambiarse mientras no existan tipos de cambio; no se inactiva una moneda con cargas de tipo de cambio pendientes. Semilla: CRC (base) y USD si la compañía no tiene monedas. Combo para otras pantallas: `GET /api/catalogos/monedas`.

## 4. Tipo de cambio (`gen.TIPO_CAMBIO`, permisos `TC_CONSULTA` y `TC_CARGA`)

- Tres valores por moneda y fecha, contra la moneda base: **compra, venta y referencia** (decimal 18,6; compra ≤ venta). Una fila por (moneda, fecha de vigencia); **rige desde su fecha hasta la siguiente cargada**.
- **Carga manual con aprobación**: `POST /api/tipos-cambio/solicitud` crea la gestión `TC_CARGA` (entidad `TIPO_CAMBIO`, llave `idMoneda|fecha`; una pendiente por moneda y fecha). Al aprobarse se inserta (o corrige, si ya existía esa fecha, verificando que los valores anteriores no cambiaron) y se audita con el número de gestión.
- Control de variación: el parámetro `TC_VARIACION_MAXIMA_PORC` (10 por defecto; 0 = sin límite) rechaza al solicitar una referencia que varíe más que ese porcentaje respecto a la vigente anterior.
- `origen` = Manual; queda reservado para una fuente externa (p. ej. BCCR) que más adelante cree la misma gestión o cargue directo con su propio usuario de integración.
- API: `GET /api/tipos-cambio` (historial, filtros), `GET /api/tipos-cambio/vigentes?fecha=`.

## 5. Calendario de feriados (`gen.FERIADO`, permiso `CAL_ADMINISTRACION`)

Feriados por país (lista PAIS), con opción «se repite cada año». Los días no hábiles de la semana vienen del parámetro `DIAS_NO_HABILES` (SAB,DOM por defecto). Consultas abiertas a cualquier usuario autenticado, para cuentas y créditos: `GET /api/calendario/dia-habil?idPais=&fecha=` y `GET /api/calendario/proximo-habil?idPais=&desde=&dias=` (dias=0: primer día hábil desde la fecha; dias>0: suma esa cantidad de días hábiles).

## 6. Parámetros generales (`gen.PARAMETRO`, permiso `PAR_ADMINISTRACION`)

Código, nombre, descripción, categoría, tipo (Texto/Entero/Decimal/Booleano), valor y valor por defecto. Se cambia el valor (validado por tipo y por reglas del parámetro) o se restablece el defecto; cada cambio se audita. Semilla: `DIAS_NO_HABILES`, `ZONA_HORARIA`, `TC_VARIACION_MAXIMA_PORC`. Nuevos parámetros se agregan por script junto con la funcionalidad que los usa.

## 7. Consulta de auditoría (`AUD_CONSULTA`)

`GET /api/auditoria` con filtros (entidad, llave, usuario, acción, fechas, gestión, texto) y paginación de 50; `GET /api/auditoria/entidades`. Solo lectura. Limitaciones conocidas: los valores que son llaves de lista (países, tipos...) se muestran como id (los nombres están en el detalle de la gestión); las fechas se guardan en UTC y se muestran en la zona del navegador; la consulta misma no se audita (AUD-004 pendiente).

## 8. Entidades de auditoría nuevas

`MONEDA`, `TIPO_CAMBIO`, `FERIADO`, `PARAMETRO`, `USUARIO_INTEGRACION` (además de `INTERLOCUTOR`, `USUARIO`, `UNIDAD_ORG`).

## 9. Pendiente

Fuente externa de tipo de cambio; conversión de montos entre monedas (servicio que use compra/venta/referencia); calendario por sucursal; auditoría de consultas y de lecturas sensibles; asignar canal por producto en cuentas y créditos.
