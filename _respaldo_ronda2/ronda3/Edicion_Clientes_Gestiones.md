# Consulta y edición de clientes por Gestión

**Fecha:** 2026-10-05 (actualizado 2026-10-06). Principio: ningún cambio a datos de negocio se aplica directo; se **solicita como Gestión** y se aplica al **aprobarse**.

## Pantallas (Backoffice)
- **Clientes → Consultar:** ficha de solo lectura (descripciones, creado/modificado por, aviso de gestión pendiente). `GET /api/interlocutores/{id}/detalle` (`CLI_CONSULTA`).
- **Clientes → Editar:** abre el formulario con combos precargados; "Enviar a aprobación" crea la gestión con solo los campos que cambian. `POST /api/interlocutores/{id}/modificacion` (`CLI_EDICION`).
- **Gestiones:** bandeja (pendientes / todas) con orden y filtros por columna; el **número de gestión abre el detalle** (campo, valor anterior, valor nuevo) y **dentro del detalle** están **Aprobar**, **Rechazar** (motivo obligatorio) y **Devolver** (comentario obligatorio). `GET /api/gestiones`, `GET /api/gestiones/{id}`, `POST /api/gestiones/{id}/aprobar|rechazar|devolver` (`GES_APROBAR`).

## Estados de una gestión
Pendiente de Aprobacion → Aplicada | Rechazada | **Devuelta**. Una gestión **Devuelta** vuelve a su creador (bandeja + campanita, con el comentario). Él usa **Corregir y reenviar**: se abre la edición con lo ya pedido; al enviar se crea una gestión nueva y la devuelta pasa a **Corregida**. La devuelta no bloquea el registro (el índice único solo cuenta las pendientes).

## Reglas
- Tipo de gestión `CLI_MODIFICACION_DATOS` (modo Campos, aprobación obligatoria, 1 nivel). Campos editables por lista blanca en `GestionRepositorio` (cliente, identificaciones, direcciones con sus 3 líneas, contactos). Los campos de colecciones se codifican `ident[12].campo`, `dir[+1].campo`, `cont[7].estado`.
- Una sola gestión **pendiente por registro** (índice único filtrado `UX_GESTION_pendiente_por_registro`).
- Aprueba el superior del solicitante (o quien tenga `GES_APROBAR_TODAS`); quien solicita no aprueba lo suyo (409 `autoaprobacion_no_permitida`), salvo `Gestiones:PermitirAutoaprobacion=true` (solo DEV). El detalle y la bandeja traen `puedeResolver` y `esMia` calculados por el servidor.
- Al aprobar, en una sola transacción: se valida que cada dato vigente siga igual al "valor anterior" de la solicitud (si no, 409 `registro_cambio`), se aplica el cambio, se registra una línea por campo en `aud.LOG_AUDITORIA` (con `id_gestion`) y la gestión pasa a **Aplicada**. Identificación duplicada → 409.
- Rechazo: estado Rechazada, motivo guardado, nada se aplica. Devolución: estado Devuelta, `comentario_devolucion`, `devuelto_por`, `fecha_devolucion`; nada se aplica.

## Objetos de autorización
`CLI_EDICION` (API), `GES_APROBAR` (Transacción), `GES_APROBAR_TODAS`. Scripts: `edicion_clientes_gestion.sql`, `mejoras_ronda2.sql` (estado Devuelta/Corregida).

## Pendiente del motor de Gestiones
- Aprobador por tipo (usuario específico, grupo) y múltiples niveles.
- Notificaciones en tiempo real / correo y bandeja de "mis solicitudes" dedicada.
- Gestión de modo Transaccional y aplicadores para otras entidades (KYC, bloqueo, productos).
- Persona Jurídica y Grupo (hoy solo Persona Física).
