# Motor de reglas (GEN-008) — Ronda 2 de Créditos

Estado: **para revisión** (modelo + SQL `motor_reglas_ronda43.sql`). No hay código ni pantallas hasta tu aprobación.
Alineado con `Documento_Diseno_Core_Bancario.md` §2.5 y `DAD_Core_Bancario.md` §9.6. Esquema nuevo: `rul`.

## 1. Para qué sirve

Un motor único y configurable que responde preguntas del tipo *"dado este cliente / solicitud / crédito, ¿qué decisión o valor aplica?"* sin programar cada caso. Primeros usos:

- **Elegibilidad de producto**: la variante "HIP-001 Empleados" solo se ofrece si `cliente.es_empleado = Sí`.
- **Reglas de decisión** de la solicitud (Ronda 5): aprobar / rechazar / referir.
- **Asignaciones**: por ejemplo recargo de tasa o monto máximo según perfil (se consumirán en Rondas 3–4).

El motor **decide y explica**; no ejecuta efectos. Quien lo invoca (solicitud, crédito, cuenta) actúa según el resultado.

## 2. Conceptos

| Concepto | Qué es | Tabla |
|---|---|---|
| Contexto | El "objeto" que se evalúa: CLIENTE (activo); SOLICITUD_CREDITO, CREDITO, CUENTA, PAGO (reservados hasta que exista su dominio) | `rul.CONTEXTO` |
| Atributo | Dato que se puede consultar, con código punteado (`cliente.edad`), tipo (Texto, Número, Fecha, Booleano, Lista) y si es multivalor | `rul.ATRIBUTO`, `rul.CONTEXTO_ATRIBUTO` |
| Operador | =, <>, >, >=, <, <=, ENTRE, EN, NO EN, CONTIENE, COMIENZA, INCLUYE, VACÍO, NO VACÍO | `rul.OPERADOR` |
| Función | Transformación opcional del atributo antes de comparar (edad en años, días desde, sumar días, redondear, %, longitud, cantidad…) | `rul.FUNCION` |
| Condición | Pieza reutilizable: atributo [+ función] + operador + valor (fijo u otro atributo) | `rul.CONDICION` |
| Acción | Resultado de una regla: **Decisión** (Aprobar/Rechazar/Referir), **Asignar** (clave = valor) o **Mensaje** | `rul.ACCION` |
| Regla | Condiciones agrupadas (Y/O anidado) → acciones. Pertenece a una versión | `rul.REGLA`, `rul.GRUPO_CONDICION`, `rul.GRUPO_CONDICION_MIEMBRO`, `rul.REGLA_ACCION` |
| Conjunto de reglas | Agrupación versionada de reglas para un contexto (`AGRUPACION_REGLA` del DAD) | `rul.CONJUNTO_REGLAS`, `rul.CONJUNTO_REGLAS_VERSION` |
| Evaluación | Bitácora de cada ejecución real con entrada, resultado y traza | `rul.EVALUACION` |
| Caso de prueba | Entrada guardada con resultado esperado | `rul.CASO_PRUEBA` |

Las condiciones y acciones son **piezas de un pool por compañía**, reutilizables entre reglas. Se guardan **relacionalmente** (no como JSON) para poder buscar "¿dónde se usa esta condición?" y bloquear inactivaciones.

## 3. Cómo se evalúa

1. El dominio llama al motor con el conjunto y los datos del contexto (o el motor los lee del cliente).
2. Se usa la **versión Vigente** del conjunto a la fecha de proceso.
3. Las reglas activas se recorren por `orden`. Cada regla evalúa su árbol de grupos (Y/O, con "negar" por condición).
4. **Política de resolución** del conjunto:
   - *Primera coincidencia* (por defecto): se aplica la primera regla que se cumple.
   - *Acumular todas*: se aplican todas; si hay decisiones distintas prevalece **Rechazar > Referir > Aprobar**.
5. Una regla **sin condiciones** es la regla por defecto (se coloca al final).
6. Se devuelve: decisión, asignaciones, mensajes, regla(s) aplicada(s) y la **traza** (qué condición dio verdadero/falso con qué valor). Las evaluaciones reales se guardan; las de prueba no.

Un atributo sin valor hace que la condición sea falsa (salvo operador "no tiene valor"). Un valor faltante nunca produce error ni aprobación implícita.

## 4. Versionado y gobierno

- Ciclo igual al de producto comercial: **Borrador → Revisión → Programado/Vigente → Expirado**; una sola Vigente y una sola en edición por conjunto.
- Publicar es la gestión **`RUL_VERSION_CONJUNTO`** (aprobación obligatoria, 1 nivel). Fecha efectiva = máx(desde, hoy).
- Un borrador nuevo es **copia profunda** de la versión anterior.
- Las piezas del pool usadas por una versión que ya no es Borrador son **inmutables**: para cambiarlas se duplica y se modifica la copia.
- No se puede inactivar una condición, acción o conjunto en uso (se consulta por referencias; `rul.CONJUNTO_REFERENCIA` registra a los consumidores, por ahora `PRODUCTO_COMERCIAL`).
- Antes de publicar se corren los **casos de prueba**: si alguno falla se advierte pero no se bloquea.

## 5. Elegibilidad en producto comercial

Nueva columna opcional `prd.PRODUCTO_COMERCIAL.id_conjunto_elegibilidad` (contexto CLIENTE). Un producto/variante sin conjunto es elegible para todos. La evaluación real ocurre en las Rondas 4–5 (cotizador y solicitud); en esta ronda solo se configura y se prueba.

## 6. Límites de complejidad (validados en código)

50 reglas por versión · 4 niveles de anidamiento de grupos · 20 condiciones por regla. Ajustables por constante; no son restricciones de BD.

## 7. Pantallas previstas (tras tu aprobación)

Dentro del Launchpad, nuevo tile **Reglas** (RUL-0000), con el patrón lista + ficha + barra de comandos:

1. **Condiciones** y **Acciones** (pool): lista, ficha de solo lectura con Editar, activar/inactivar, "se usa en".
2. **Conjuntos de reglas**: ficha con pestañas *Datos / Reglas / Pruebas*; botón *Versiones* (como productos).
3. **Editor de reglas** (en el borrador): reglas ordenadas, constructor visual de grupos Y/O con condiciones del pool, acciones asociadas.
4. **Simulador**: ingresar datos o elegir un cliente real, ver decisión y traza; guardar como caso de prueba.
5. **Evaluaciones**: consulta de la bitácora con su explicación.

## 8. Seeds incluidos en el SQL

- Contextos: CLIENTE (activo) + 4 reservados.
- Atributos de cliente: tipo de persona, edad, fecha de nacimiento, estado, antigüedad (meses), KYC vigente, tiene bloqueo, es empleado, roles (multivalor), sucursal, canal de origen, país; y `sistema.fecha_actual`.
- 14 operadores y 12 funciones.
- LOV `POLITICA_RESOLUCION_REGLAS`, tipo de gestión `RUL_VERSION_CONJUNTO`, permisos `RUL_CONSULTA`, `RUL_ADMIN`, `RUL_PUBLICAR`, `RUL_EVALUAR` (asignados a ADMINISTRADOR_SEGURIDAD).

## 9. Decisiones y supuestos (confírmame o corrige)

| # | Supuesto | Alternativa |
|---|---|---|
| R-1 | Fase 1 solo con acciones **puras** (Decisión, Asignar, Mensaje); el dominio ejecuta los efectos | Incluir ya cambio de estado, eventos, notificaciones |
| R-2 | **Sin scripting** (el DAD lo menciona como tipo de acción) | Habilitarlo más adelante con sandbox |
| R-3 | Solo el contexto **CLIENTE** activo; los demás se activan con su ronda | Definir ya atributos de solicitud/crédito |
| R-4 | `es_empleado`, `kyc_vigente`, `tiene_bloqueo`, `antiguedad_meses` se **derivan** del modelo de clientes; si `es_empleado` aún no tiene fuente, se define como atributo del cliente (rol/indicador) en la Ronda 5 | Marcarlo manualmente en la ficha del cliente |
| R-5 | Atributos de tipo Lista referencian el código de LOV (sin FK dura) | FK a `gen.LISTA_VALORES_TIPO` |
| R-6 | Política por defecto: primera coincidencia; en conflicto gana el más restrictivo | Prioridad configurable por regla |
| R-7 | `apr.REGLA_APROBACION` (motor de aprobación) **no se migra** en esta ronda | Unificarlo en `rul` después |
| R-8 | Evaluaciones reales se guardan siempre (entrada/resultado/traza en JSON) | Guardar solo resumen |
| R-9 | Límites de complejidad 50/4/20 | Otros valores |

## 10. Fuera de alcance

Ejecución de efectos, scripting, migración de reglas de aprobación, reglas sobre contextos reservados, importación masiva desde Excel, simulación masiva sobre cartera.

## 11. Orden de trabajo propuesto

1. Revisas este documento y el SQL; ajustamos.
2. Ejecutas `motor_reglas_ronda43.sql`.
3. Backend (repositorio, motor de evaluación, endpoints, versionado con gestión) y pruebas del evaluador.
4. Pantallas (pool, conjuntos, editor, simulador, evaluaciones).
