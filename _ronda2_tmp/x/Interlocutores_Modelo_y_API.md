# Interlocutores — modelo, API y pantallas

**Actualizado:** 2026-10-05. Reemplaza el modelo de cliente con una sola identificación (`cli.INTERLOCUTOR.id_tipo_identificacion` / `numero_identificacion`).

## 1. Decisiones de diseño

- El menú es **Interlocutores** (no "Clientes"). Un interlocutor es el supertipo; los **roles** (`cli.INTERLOCUTOR_ROL`, LOV `TIPO_ROL_INTERLOCUTOR`: CLIENTE, BENEFICIARIO, FIRMANTE, FIADOR, CODEUDOR, AUTORIZADO) dicen qué es para la entidad. Prospecto sigue en `cli.INTERLOCUTOR_PROSPECTO` (pendiente).
- Identificaciones, direcciones y contactos son **1:n**, con baja lógica (`estado` Activo/Inactivo) y auditoría (`creado_por/en`, `modificado_por/en`).
- **Todo cambio a un interlocutor existente va por Gestión** y lo aprueba otro usuario. El alta del cliente se crea directo (con borrador opcional).
- El **número** (`cli.INTERLOCUTOR.numero`) se asigna al finalizar el alta con el motor de rangos (ver `Motor_de_Numeracion.md`); la llave interna es `id_interlocutor`.
- **Canal de origen** (`cli.INTERLOCUTOR.id_canal_origen`, LOV `CANAL`): por dónde nació el cliente (Sucursal, Web, Móvil, CRM, Onboarding digital, API). Es independiente de la **unidad** (`id_unidad`, sucursal): un cliente nace en la sucursal 100 *por* el canal CRM. Se fija en el alta (en el Backoffice se elige, por defecto Sucursal; por API lo asigna el servidor según el canal fijo del usuario de integración) y luego solo cambia por gestión (campo `id_canal_origen`, auditado). Ver `Administracion_Transversal.md`.
- Combos respaldados por Listas de Valores (LOV): tipo de identificación, institución emisora, tipo de dirección, tipo de contacto.

## 2. Tablas nuevas (script `interlocutor_multiples.sql`, idempotente)

| Tabla | Contenido | Índices relevantes |
|---|---|---|
| `cli.INTERLOCUTOR_IDENTIFICACION` | tipo, número, institución emisora, fecha emisión, fecha vencimiento, principal | 1 principal activa por interlocutor; único activo (compañía, tipo, número) — CLI-020 |
| `cli.INTERLOCUTOR_DIRECCION` | tipo, **país**, niveles 1-4 (lista o texto según el país), código postal, detalle, principal — ver `Division_Territorial_y_Direcciones.md` | 1 principal activa por interlocutor |
| `cli.INTERLOCUTOR_CONTACTO` | tipo (LOV), valor, principal | 1 principal por (interlocutor, tipo) |
| `cli.INTERLOCUTOR_BORRADOR` | JSON del wizard, paso, vencimiento | solo su propietario lo ve |

Migración: la identificación única existente pasa como principal; luego se eliminan `id_tipo_identificacion`, `numero_identificacion`, `UQ_INTERLOCUTOR_identificacion` y su FK. Los interlocutores existentes reciben el rol CLIENTE. Se crean los permisos `LOV_<codigo>` de las listas nuevas para ADMINISTRADOR_SEGURIDAD.

LOV nuevas: `INSTITUCION_EMISORA_ID`, `TIPO_DIRECCION`, `TIPO_CONTACTO`, `TIPO_ROL_INTERLOCUTOR`; se suman PASAPORTE y LICENCIA_CONDUCIR a `TIPO_IDENTIFICACION_FISICA`. Los códigos de contacto son valores de sistema (`es_sistema=1`).

## 3. Validaciones (backend y wizard)

- Nombre 1, apellido 1 y fecha de nacimiento válidos; país regulador existente.
- Al menos 1 identificación, exactamente 1 principal; tipo, institución y emisión obligatorios; vencimiento posterior a emisión; sin tipo+número repetido en la solicitud ni duplicado activo en la compañía (409).
- Direcciones: país y niveles según su configuración (`Division_Territorial_y_Direcciones.md`), detalle obligatorio, máximo 1 principal. Contactos: máximo 1 principal por tipo y formato por código: CORREO (regex), TELEFONO_MOVIL / TELEFONO_FIJO / WHATSAPP (8 dígitos, `+506` opcional).

## 4. API (`/api/interlocutores`)

| Método y ruta | Permiso | Descripción |
|---|---|---|
| `POST /` | CLI_ALTA | Alta (transaccional); borra el borrador del propietario; rol CLIENTE |
| `GET /` | CLI_CONSULTA | Listado paginado con `q` |
| `GET /buscar?q=` | CLI_CONSULTA | Búsqueda global (top 8: nombre, N.º, identificación) |
| `GET /{id}` o `/{id}/detalle` | CLI_CONSULTA | Ficha con roles y colecciones |
| `GET /{id}/360` | CLI_CONSULTA | Ficha + gestiones pendientes/cerradas + relaciones + productos (vacío hasta Cuentas/Créditos) |
| `POST /{id}/modificacion` | CLI_EDICION | Crea una gestión con las diferencias; no cambia el dato |
| `GET/PUT/DELETE /borradores` | CLI_ALTA | Borradores privados; vigencia `Borradores:DiasVigencia` (30), renovable al guardar |
| `GET /api/notificaciones` | autenticado | Notificaciones derivadas (ver §6) |

## 5. Gestión de modificación

Detalle en `apr.GESTION_DETALLE.nombre_campo`: `nombre1` (campo del padre), `ident[12].numero`, `dir[+1].detalle` (ítem nuevo n.º 1), `cont[7].estado` Activo→Inactivo (baja).

Al aprobar se aplica en este orden: campos del padre → principal→No → bajas → otras modificaciones → altas (con principal 0) → principal→Sí. Se verifica que el valor actual siga igual al "anterior" de la gestión (control de concurrencia) y se escribe una línea por campo en `aud.LOG_AUDITORIA`. Solo puede haber una gestión pendiente por interlocutor.

## 6. Notificaciones (campana)

Sin tabla por ahora; se derivan: gestiones pendientes para quien aprueba; gestiones propias resueltas en los últimos 14 días; borradores propios que vencen en ≤3 días. El estado "leída" se guarda en el navegador (localStorage, por usuario). Consulta cada 60 s.

## 7. Pantallas del Backoffice

- `/interlocutores` — launchpad por zonas (Consulta, Clientes, Prospectos, Beneficiarios, Firmantes); los mosaicos se filtran por permisos. Prospecto/Beneficiario/Firmante: "Próximamente".
- `/interlocutores/consulta` — listado; "Ver 360°", "Modificar datos", "Nuevo cliente".
- `/interlocutores/:id` — **Consulta 360°**, única app de consulta para cualquier rol. Pestañas: Resumen, Productos (solo clientes), Gestiones, Relaciones, Datos completos. Acciones rápidas según rol y permiso; la búsqueda global siempre abre esta vista.
- `/interlocutores/clientes/nuevo` y `/interlocutores/clientes/:id/modificar` — wizard de 5 pasos (Datos generales, Identificaciones, Direcciones, Contacto, Revisión). `?borrador=<id>` retoma un borrador. En edición la revisión muestra el antes/después y envía la gestión.
- `/interlocutores/borradores` — borradores propios (continuar/eliminar).
- Campana junto al usuario logueado.

## 8. Pendiente

Prospecto, Beneficiario y Firmante; Cuentas y Créditos (llenan la pestaña Productos); permisos por sección del 360°; tabla de notificaciones y tiempo real; job de purga de borradores (hoy se purgan al listar); KYC y bloqueo.

## Ronda de mejoras (2026-10-06)
- **Canal de origen**: el alta desde el Backoffice siempre es Sucursal; el request ya no trae `idCanalOrigen` (alta ni modificación). Integraciones: canal fijo del usuario de integración.
- **Dirección exacta en 3 líneas**: `detalle` (obligatoria, 500), `detalle2` y `detalle3` (opcionales, 200). Se editan por gestión como campos `dir[...].detalle|detalle2|detalle3`.
- **Gestiones — Devolver**: `POST /api/gestiones/{id}/devolver` `{ comentario }` (`GES_APROBAR`, comentario obligatorio). La gestión pasa a **Devuelta**, aparece en la bandeja y la campanita de quien la creó con el comentario. «Corregir y reenviar» abre la edición con lo que ya había pedido (`/interlocutores/clientes/{id}/modificar?gestion=N`); al reenviar (`idGestionDevuelta`) se crea una gestión nueva y la devuelta queda **Corregida**. En el 360° las devueltas cuentan como pendientes.
- Aprobar / Rechazar / Devolver están **dentro del detalle** de la gestión (se abre con el número). `puedeResolver` y `esMia` los calcula el servidor (superior, `GES_APROBAR_TODAS`, autoaprobación DEV) para ofrecer solo lo permitido. La bandeja se puede ordenar y filtrar por columna.
- Corrección: el error «El campo 'dir[+1].nivel1_id' no es editable por gestion» venía de que el patrón de campos hijo no admitía dígitos (`nivel1_id`); corregido.
- Corrección: «Siguiente» no avanzaba en Direcciones porque la validación del paso no recibía la configuración del país.
