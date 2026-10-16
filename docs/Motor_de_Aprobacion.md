# Motor de aprobación (gestiones), grupos y reasignación

**Creado:** 2026-10-09. Scripts: `mejoras_ronda6.sql` a `mejoras_ronda11.sql`. Complementa `Edicion_Clientes_Gestiones.md`.

## 1. Idea general

Todo cambio a datos de negocio se **solicita como gestión** y se aplica al aprobarse. **Qué se necesita para aprobar** no está fijo en código: lo deciden **reglas paramétricas por tipo de gestión**, que se administran en pantalla.

Tipos de gestión actuales: `CLI_ALTA` (alta de cliente), `CLI_MODIFICACION_DATOS`, `TC_CARGA` (ingreso de tipo de cambio).

## 2. Reglas de aprobación (`apr.REGLA_APROBACION`)

- Cada regla pertenece a un tipo de gestión, tiene **prioridad** (menor = se evalúa primero), **condiciones** y **niveles**.
- **Gana la primera regla activa cuyas condiciones se cumplen todas.** Una regla sin condiciones es la regla por defecto y va al final.
- Condiciones = variable + operador (IGUAL / DIFERENTE) + valor (lista separada por comas). Variables:

| Variable | Qué evalúa |
|---|---|
| `CANAL` | Canal de origen de la operación (Backoffice = SUCURSAL; integraciones, su canal fijo) |
| `UNIDAD` | La unidad del solicitante **o cualquiera de sus unidades superiores** |
| `USUARIO` | El solicitante (persona o usuario técnico de la integración) |
| `PUESTO` | El puesto del solicitante (`sec.USUARIO.puesto`) |
| `TIPO_ACTOR` | `PERSONA` o `INTEGRACION` |

- Cada regla define **niveles** en orden. Tipo de aprobador por nivel:
  - `USUARIO`: una persona específica (solo ella aprueba).
  - `GRUPO`: cualquier miembro de un grupo de distribución.
  - `SUPERIOR`: el superior directo; en el segundo nivel, el superior del aprobador anterior, y así sucesivamente.
  - `AUTOMATICA`: se aprueba sola, dejando constancia de la regla.
- Los niveles se **copian a la gestión al crearla** (`apr.GESTION_APROBACION`): cambiar una regla después no altera las gestiones en curso.
- **Cadena de superiores:** un nivel `SUPERIOR` se omite (estado *Omitido*, con el motivo) si no hay más superiores o si ese superior ya intervino en la gestión; la gestión termina en el último nivel disponible. Un tipo de gestión puede permitir o no que el mismo usuario apruebe varios niveles (`permite_mismo_usuario_multiples_niveles`).
- Quien solicita no aprueba lo suyo (409 `autoaprobacion_no_permitida`), salvo `Gestiones:PermitirAutoaprobacion=true` (solo desarrollo).
- Quien tiene `GES_APROBAR_TODAS` puede resolver gestiones aunque no sea el aprobador asignado; sin superior, basta `GES_APROBAR`. `Gestiones:AprobacionPorSuperior=false` desactiva la regla del superior.

### Reglas iniciales
- `CLI_ALTA`: prioridad 20, canal SUCURSAL → aprobación automática (regla de desarrollo: el cliente se crea al instante pero queda la gestión); prioridad 999, por defecto → un nivel, superior del solicitante.
- `CLI_MODIFICACION_DATOS` y `TC_CARGA`: un nivel, superior del solicitante.

### Alta por integración
Hoy es **directa** (no pasa por gestión). Cuando una integración deba ser solicitante, su usuario técnico y la variable `TIPO_ACTOR = INTEGRACION` permitirán reglas propias (p. ej. aprobación automática para el CRM y revisión manual para el canal web).

## 3. Pantallas de administración

- **Reglas de aprobación** (`GES_REGLAS`): por tipo de gestión; crear, editar, activar/inactivar, **reordenar** (prioridad), eliminar (solo si ninguna gestión usó la regla; si no, inactivar) y **simular** (elegir un usuario y un canal y ver qué regla gana y quién aprobaría). Una regla nueva se inserta antes de la primera regla sin condiciones; las prioridades se renumeran de 10 en 10. Opción por tipo para permitir que un mismo usuario apruebe varios niveles.
- **Grupos de distribución** (`GES_GRUPOS`): listas de usuarios **independientes de la estructura organizativa**; sirven de aprobadores y, más adelante, de destinatarios de notificaciones. Solo personas (no usuarios técnicos). No se puede inactivar ni vaciar un grupo usado por una regla activa.
- Todo cambio de reglas, grupos y tipos de gestión queda en auditoría (`REGLA_APROBACION`, `GRUPO_USUARIOS`, `TIPO_GESTION`).

## 4. Bandeja y detalle

Cada gestión tiene **número de negocio** (rango de numeración `GESTION`, 8 dígitos por defecto; `Motor_de_Numeracion.md`). La bandeja ofrece los filtros *Mis gestiones pendientes*, *Todas mis gestiones* y *Todas las gestiones*, con orden y filtros por columna y exportación a Excel. En el detalle se ven los niveles, quién aprobó cada uno y los comentarios. Aprobar, rechazar y devolver están dentro del detalle; el comentario del aprobador admite hasta 1000 caracteres (`mejoras_ronda9.sql`). Solo aprueban **personas**: las integraciones no resuelven gestiones.

## 5. Cambio de asignación de aprobador (reasignación)

Cuando quien debe aprobar el nivel actual no está disponible (enfermedad, vacaciones, salida), alguien con permiso **`GES_REASIGNAR`** pasa sus gestiones pendientes a otro usuario (`mejoras_ronda8.sql`; el permiso se concede a los roles que ya tienen `GES_APROBAR_TODAS`).

- Se elige el aprobador actual, se listan sus gestiones pendientes (nivel actual asignado a él), se seleccionan una o varias, se escoge el nuevo aprobador y se escribe un **motivo obligatorio**.
- El nuevo aprobador debe estar activo y tener `GES_APROBAR` (solo personas).
- Solo cambia el **nivel pendiente**. `apr.GESTION_APROBACION.id_usuario_original` guarda el aprobador original: la cadena de superiores de los niveles siguientes **sigue partiendo del original**.
- Se valida gestión por gestión; las que no se pueden reasignar se devuelven con su motivo, y las demás se aplican. Cada cambio queda en auditoría con el motivo (entidad `GESTION`, campo `aprobador_nivel_N`).
- API: `GET /api/gestiones/reasignacion/aprobadores`, `GET /api/gestiones/reasignacion?idUsuario=`, `POST /api/gestiones/reasignacion`.

## 6. Trazabilidad

`aud.LOG_AUDITORIA` guarda quién (persona o integración), cuándo, entidad, campo, valores y la gestión. El detalle de una gestión muestra *Ingresada por* y *Aprobada/Rechazada/Devuelta por* con fecha; la cadena de correcciones (`mejoras_ronda3.sql`) enlaza devoluciones y reenvíos.

## 7. Pendiente

Filtro «Pendientes de mi aprobación» en la consulta de gestiones; notificaciones por correo y en tiempo real; aprobadores por monto; integración como solicitante de gestiones; gestiones de modo Transaccional y aplicadores para otras entidades (KYC, bloqueo, productos); delegación temporal automática (hoy la reasignación es manual).
