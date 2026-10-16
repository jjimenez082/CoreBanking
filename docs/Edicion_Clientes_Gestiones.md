# Consulta y edición de clientes por Gestión

**Fecha:** 2026-10-05 (actualizado 2026-10-09). Principio: ningún cambio a datos de negocio se aplica directo; se **solicita como Gestión** y se aplica al **aprobarse**. Quién aprueba lo deciden las **reglas de aprobación** (ver `Motor_de_Aprobacion.md`). Excepción actual: el alta de cliente por **integración** (API) es directa.

## Pantallas (Backoffice)
- **Clientes → Consultar:** ficha de solo lectura (descripciones, creado/modificado por, aviso de gestión pendiente). `GET /api/interlocutores/{id}/detalle` (`CLI_CONSULTA`).
- **Clientes → Editar:** abre el formulario con combos precargados; "Enviar a aprobación" crea la gestión con solo los campos que cambian. `POST /api/interlocutores/{id}/modificacion` (`CLI_EDICION`).
- **Clientes → Nuevo:** el alta del Backoffice también nace como gestión (`CLI_ALTA`); según la regla aplicable el cliente se crea de inmediato (aprobación automática) o al aprobarse (`202 Accepted` mientras está pendiente).
- **Gestiones:** bandeja con filtros *Mis gestiones pendientes*, *Todas mis gestiones* y *Todas las gestiones*, orden y filtros por columna y exportación a Excel; el **número de gestión abre el detalle** (campo, valor anterior, valor nuevo, niveles de aprobación) y **dentro del detalle** están **Aprobar**, **Rechazar** (motivo obligatorio) y **Devolver** (comentario obligatorio). `GET /api/gestiones`, `GET /api/gestiones/{id}`, `POST /api/gestiones/{id}/aprobar|rechazar|devolver` (`GES_APROBAR`).

## Estados de una gestión
Pendiente de Aprobacion → Aplicada | Rechazada | **Devuelta**. Una gestión **Devuelta** vuelve a su creador (bandeja + campanita, con el comentario). Él usa **Corregir y reenviar**: se abre la edición con lo ya pedido; al enviar se crea una gestión nueva y la devuelta pasa a **Corregida**. La devuelta no bloquea el registro (el índice único solo cuenta las pendientes).

## Trazabilidad (script `mejoras_ronda3.sql`)
- **Cadena de correcciones** en `apr.GESTION`: `id_gestion_origen` (la gestión que ésta corrige), `id_gestion_correccion` (la gestión que corrigió a ésta) y `numero_correccion` (0 = original, 1 = primera corrección, 2...). Sirve para detectar gestiones devueltas varias veces; el detalle lo resalta desde la 2.ª y los números enlazan entre sí. Un rechazo es definitivo (no se reenvía), por eso el contador cuenta devoluciones.
- **Quién**: toda gestión muestra *Ingresada por* (`id_usuario_solicitante`) y quién la resolvió con su tipo — *Aprobada por*, *Rechazada por* o *Devuelta por* — más la fecha, tomado de `apr.GESTION_APROBACION` (también para las gestiones anteriores al script). La bandeja trae las columnas *Ingresó*, *Resolvió* y *Corrección*.
- **Número de negocio** de la gestión (`apr.GESTION.numero`, rango `GESTION`; `mejoras_ronda7.sql`).

## Reglas
- Tipos de gestión: `CLI_ALTA`, `CLI_MODIFICACION_DATOS` (modo Campos, aprobación obligatoria) y `TC_CARGA`. Campos editables por lista blanca en `GestionRepositorio` (cliente, identificaciones, direcciones con sus 3 líneas, contactos). Los campos de colecciones se codifican `ident[12].campo`, `dir[+1].campo`, `cont[7].estado`.
- Una sola gestión **pendiente por registro** (índice único filtrado `UX_GESTION_pendiente_por_registro`).
- **Niveles y aprobadores:** los define la regla de aprobación que gane (usuario, grupo, superior o automática; uno o varios niveles; ver `Motor_de_Aprobacion.md`). Quien solicita no aprueba lo suyo (409 `autoaprobacion_no_permitida`), salvo `Gestiones:PermitirAutoaprobacion=true` (solo DEV). Quien tiene `GES_APROBAR_TODAS` puede resolver aunque no sea el aprobador asignado. El detalle y la bandeja traen `puedeResolver` y `esMia` calculados por el servidor.
- Solo **personas** solicitan y resuelven gestiones desde el Backoffice; un usuario técnico (integración) no aprueba.
- Al aprobar el **último nivel**, en una sola transacción: se valida que cada dato vigente siga igual al "valor anterior" de la solicitud (si no, 409 `registro_cambio`), se aplica el cambio, se registra una línea por campo en `aud.LOG_AUDITORIA` (con `id_gestion`) y la gestión pasa a **Aplicada**. Identificación duplicada → 409.
- Rechazo: estado Rechazada, motivo guardado, nada se aplica. Devolución: estado Devuelta, `comentario_devolucion`, `devuelto_por`, `fecha_devolucion`; nada se aplica. Cada aprobador puede dejar un comentario (hasta 1000 caracteres) en su nivel.
- **Reasignación de aprobador** (`GES_REASIGNAR`): ver `Motor_de_Aprobacion.md`.

## Objetos de autorización
`CLI_EDICION` (API), `GES_APROBAR` (Transacción), `GES_APROBAR_TODAS`, `GES_REASIGNAR`, `GES_REGLAS`, `GES_GRUPOS`. Scripts: `edicion_clientes_gestion.sql`, `mejoras_ronda2.sql` a `mejoras_ronda11.sql`.

## Pendiente del motor de Gestiones
- Notificaciones en tiempo real / correo y bandeja de "mis solicitudes" dedicada; filtro «Pendientes de mi aprobación».
- Integración como solicitante de gestiones (alta por API sujeta a reglas).
- Gestión de modo Transaccional y aplicadores para otras entidades (KYC, bloqueo, productos).
- Persona Jurídica y Grupo (hoy solo Persona Física).
