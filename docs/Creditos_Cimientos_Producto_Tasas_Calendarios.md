# Créditos – Ronda 1: Cimientos (producto, tasas de referencia, calendarios)

Estado: **propuesta para revisión** (modelo físico en `scripts_v2/creditos_cimientos_ronda42.sql`; aún sin código backend ni pantallas).
Alcance regulatorio: Costa Rica (SUGEF/CONASSIF) y Panamá (SBP). Backlog origen: CRE-001..004, PRC-002/003/005, GEN-005 (fase 1), GEN-018, PRD-001.

## 1. Plan de rondas de Créditos

| Ronda | Contenido | Backlog |
|---|---|---|
| **1. Cimientos** (esta) | Producto operativo y comercial con versiones, parámetros, tasas de referencia, bases de cálculo, calendarios, categorías regulatorias | PRC-002/003/005, CRE-001..004, GEN-005 |
| 2. Motor de reglas | Pool de reglas en secuencia, con versionado y vigencia; base común de elegibilidad, bundles y aprobación | GEN-008 |
| 3. **Pricing Studio** | Pantalla y motor de configuración de precio: componentes económicos (monto fijo / % sobre base tipada), conversión de moneda trazable, variantes con overrides por instancia, ciclo de publicación de versiones, simulador de prueba pre-liberación | PRC-003/004/007 (prueba) |
| 4. Motor de cálculo y cotizador | Planes de pago (Nivelada, Alemán, Americana, Revolvente, Personalizada), bases de cálculo, convención de ajuste, simulador de crédito y cotización real persistida como evidencia | CRE-003, CRE-024, PRC-007 |
| 5. Solicitud y aprobación | SOLICITUD_CREDITO, gestión de aprobación (autoaprobación si no hay workflow), promoción de la cotización sin recalcular | |
| 6. Líneas de crédito | Sublímites jerárquicos, interlocutores con % de participación, reserva/captura/liberación de cupo, auxiliar externo (tarjetas) | CRE-025 |
| 7. Crédito, desembolso y cuotas | CREDITO, DESEMBOLSO, CUOTA / CUOTA_DETALLE con fechas resueltas, fijación de versión según estrategia | |
| 8. Pagos y prelación | Aplicación de pagos, prelación configurable, parciales/anticipados, reversos | PAG-001 |
| 9. Mora y devengo | Intereses corrientes y moratorios, devengo (línea recta/EIR), revisión de tasa variable; proceso EOD diferido | CRE-016 |
| 10. Cartera y provisiones | Clasificación de cartera, etapas NIIF 9, categorías regulatorias, castigo | CRE-023 |
| 11. Garantías, bundles y reestructuras | Garantías (alcance a líneas), bundles con calificación por reglas y re-validación en cada uso, reestructuras/refinanciamiento | CRE-022, PRC-006 |

Nota: los contadores de eventos ligados a pricing (CTA-013) se atienden en la ronda de Cuentas.

Cada ronda: documento + SQL → tu revisión → código.

## 2. Conceptos

**Producto operativo** = lo que el producto *es y puede hacer* (estructura): dominio (Crédito/Cuenta), categoría, categoría regulatoria, monedas permitidas, tipo de persona, calendario y **qué parámetros existen** (con tipo, límites, obligatoriedad).

**Producto comercial** = lo que se *ofrece* al cliente. Pertenece a un operativo y tiene **versiones** con los valores concretos (tasas, plazos, montos, comisiones). Puede ser **variante** de otro comercial (un solo nivel): hereda sus valores y sobrescribe solo los suyos.

**Estrategia de versionado** (en el operativo):
- *Dinámico*: el crédito lee siempre la versión vigente (las condiciones cambian para todos).
- *Fijo al origen*: el crédito copia las condiciones de la versión vigente al formalizarse y nunca cambia.

**Ciclo de una versión**: Borrador → Revisión → Aprobado → Programado → Vigente → Expirado.
Publicar una versión es una **gestión** (`PRD_VERSION_PRODUCTO`, aprobación obligatoria de 1 nivel). Al aprobarse queda *Programado* con su fecha de vigencia; al llegar la fecha pasa a *Vigente* y la anterior a *Expirado* (cambio de fecha lo ejecuta el proceso EOD; mientras tanto se resuelve por fecha al consultar). Restricciones en BD: una sola Vigente y una sola en edición (Borrador/Revisión) por producto.

**Componente económico** (comisión, seguro, cargo, mora): un parámetro marcado como económico, calculado como *Monto fijo* o *Porcentaje sobre una base* (monto desembolsado, aprobado, saldo, saldo insoluto, cuota, pago, cuota vencida). Lleva un código de evento contable que se enlaza a TIPO_EVENTO en la ronda de contabilización.

**Tasas de referencia** (INDICADOR_FINANCIERO + _VALOR): catálogo (TBP, Prime, SOFR de ejemplo) con historial por fecha de vigencia; la carga de un valor pasa por gestión (`PRD_INDICADOR_VALOR`). Una tasa variable = indicador + spread, con revisión periódica y piso/techo (los parámetros se definen en la plantilla, ver D-2).

**Bases de cálculo**: 30/360, ACT/365, ACT/360, ACT/ACT (catálogo con comportamiento en código; solo se pueden activar/inactivar).

**Calendario**: días hábiles de la semana + feriados del país (`gen.FERIADO`) con **convención de ajuste** (sin ajuste, siguiente hábil, hábil anterior, siguiente hábil sin cambiar de mes). Cada cuota guardará la fecha ya resuelta.

**Categoría regulatoria**: catálogo por país/regulador, definido por el cliente (por ejemplo, categorías de riesgo/clasificación de cartera de SUGEF o SBP).

## 3. Modelo físico (resumen)

| Esquema | Tabla | Propósito |
|---|---|---|
| gen | BASE_CALCULO | 4 bases sembradas |
| gen | INDICADOR_FINANCIERO / _VALOR | Tasas de referencia + historial (UQ por fecha) |
| gen | CALENDARIO | Por compañía, día hábil por día de semana, país para feriados, uno por defecto |
| gen | CATEGORIA_REGULATORIA | Por país_regulador |
| prd (nuevo) | PRODUCTO_OPERATIVO / _MONEDA | Estructura y monedas (una principal) |
| prd | PARAMETRO_PRODUCTO_OPERATIVO | Catálogo tipado de parámetros por producto (Texto, Entero, Decimal, Booleano, Fecha, Lista) |
| prd | PRODUCTO_COMERCIAL | Oferta, con `id_producto_comercial_base` para variantes |
| prd | PRODUCTO_COMERCIAL_VERSION | Versión con vigencia y gestión |
| prd | PRODUCTO_COMERCIAL_VERSION_VALOR | Valor por parámetro (texto; sin fila = hereda o usa defecto) |

Listas de valores nuevas: CATEGORIA_PRODUCTO_CREDITO, CATEGORIA_PRODUCTO_CUENTA, TIPO_CALCULO_PLAN_PAGO, FRECUENCIA_PAGO, TIPO_TASA, BASE_COMPONENTE_ECONOMICO, TIPO_INDICADOR_FINANCIERO, CONVENCION_AJUSTE_FECHA.
Tipos de gestión: `PRD_VERSION_PRODUCTO`, `PRD_INDICADOR_VALOR`.
Permisos: `PRD_ADMIN`, `PRD_CONSULTA`, `PRD_PUBLICAR`, `IND_ADMIN`, `IND_CARGA` (asignados al Administrador de seguridad).

## 4. Pantallas previstas (tras tu aprobación)

Launchpad "Créditos" / "Productos" con fichas: Productos operativos (ficha con tabs Parámetros y Monedas), Productos comerciales (ficha con tab Versiones; editor de versión con validación contra límites), Tasas de referencia (historial y carga por gestión), Calendarios, Categorías regulatorias. Patrón lista + barra de comandos + ficha, modales de mantenimiento y wizard para crear producto.

## 5. Decisiones (supuestos aplicados en el código)

Sin respuesta explícita, se implementó con las recomendaciones de cada punto; se pueden cambiar. D-1 plantilla estándar: **aplicada** (casilla al crear el producto). D-2 tasa variable como parámetros: **aplicada**. D-3 no tocar TIPO_CAMBIO: **aplicada**. D-4 categorías regulatorias vacías con mantenimiento: **aplicada**. D-5 calendario sin versionado: **aplicada**. D-6 dominio Cuenta listo sin pantallas propias: **aplicada** (el mismo mantenimiento admite ambos dominios). D-7 Programado→Vigente se resuelve al consultar hasta que exista el EOD: **aplicada**.

- **D-1 Plantilla de parámetros estándar**: al crear un producto operativo de crédito, ¿sembramos una plantilla (monto mín/máx, plazo mín/máx, tipo de tasa, tasa/indicador/spread/piso/techo, frecuencia de pago, tipo de cálculo, base de cálculo, convención de ajuste, gracia de capital/intereses, tasa de mora, comisión de formalización, seguro) que luego se edita? *Recomendado: sí.*
- **D-2 Tasa variable**: ¿parámetros de la plantilla (indicador, spread, piso, techo, periodicidad de revisión) o tabla propia de tramos de tasa? *Propuesto: parámetros en esta ronda; tramos cuando haya gracia/tasa escalonada.*
- **D-3 Tipo de cambio**: dejar `gen.TIPO_CAMBIO` como está y unificarlo con indicadores más adelante (el valor TIPO_CAMBIO ya está reservado en la lista). *Recomendado: no tocar ahora.*
- **D-4 Categorías regulatorias**: ¿las siembro vacías (las carga el cliente) o con una semilla genérica por país? *Recomendado: vacías, con pantalla de mantenimiento.*
- **D-5 Calendario**: fase 1 sin versionado (se usa el feriado vigente y la fecha de cada cuota queda guardada). ¿Aceptas?
- **D-6 Cuentas**: el operativo ya admite dominio Cuenta; ¿lo dejamos listo pero sin pantallas hasta la ronda de Cuentas? *Recomendado: sí.*
- **D-7 Cambio de vigencia**: el paso Programado → Vigente lo hará el EOD; mientras no exista, la versión se resuelve por fecha. ¿Aceptas?

## 6. Fuera de alcance de esta ronda

Cálculo de planes de pago, simulador, solicitudes, créditos, cuotas, pagos, mora, provisiones, garantías, líneas de crédito, contabilización y procesos EOD.
