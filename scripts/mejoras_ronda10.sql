/* ============================================================================
   Ronda 10 - Alta de cliente por gestion.
   1. Tipo de gestion CLI_ALTA (el alta del wizard del Backoffice nace como gestion).
   2. Reglas de aprobacion iniciales de CLI_ALTA:
        prio 20  - Canal Sucursal: aprobacion AUTOMATICA (regla de desarrollo: el comportamiento
                   visible no cambia; el cliente se crea al instante pero queda la gestion).
        prio 999 - Por defecto: un nivel, aprobador = superior del solicitante.
   Para probar la aprobacion real, cambie el nivel de la regla de prioridad 20 (ver al final) o
   desactive esa regla; luego sera una pantalla de Administracion (fase 3).
   Idempotente. Ejecutar en la base del cliente (CoreBancario_DEV).
   ============================================================================ */
USE CoreBancario_DEV;
GO

IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_ALTA')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_ALTA', N'Alta de cliente', N'Campos', 1, 1, N'Cliente', N'script ronda10');
GO

DECLARE @tipo bigint = (SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = N'CLI_ALTA');
IF @tipo IS NOT NULL AND NOT EXISTS (SELECT 1 FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo)
BEGIN
    INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por)
    SELECT c.id_compania, @tipo, N'Canal Sucursal: aprobacion automatica (desarrollo)', 20, N'script ronda10' FROM gen.COMPANIA c;
    INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por)
    SELECT c.id_compania, @tipo, N'Por defecto: un nivel (superior)', 999, N'script ronda10' FROM gen.COMPANIA c;

    INSERT INTO apr.REGLA_APROBACION_CONDICION (id_regla, variable, operador, valor)
    SELECT id_regla, N'CANAL', N'IGUAL', N'SUCURSAL' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo AND prioridad = 20;

    INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador)
    SELECT id_regla, 1, N'AUTOMATICA' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo AND prioridad = 20;
    INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador)
    SELECT id_regla, 1, N'SUPERIOR' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo AND prioridad = 999;
END
GO

SELECT tg.codigo, r.prioridad, r.nombre, n.nivel, n.tipo_aprobador
FROM apr.REGLA_APROBACION r
JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = r.id_tipo_gestion
LEFT JOIN apr.REGLA_APROBACION_NIVEL n ON n.id_regla = r.id_regla
WHERE tg.codigo = N'CLI_ALTA' ORDER BY r.prioridad, n.nivel;
GO

/* ---- Para PROBAR la aprobacion del alta (ejecutar a mano cuando quiera) -------------------------
   Pasa la regla de Sucursal a un nivel con el superior del solicitante:
     UPDATE n SET tipo_aprobador = N'SUPERIOR'
     FROM apr.REGLA_APROBACION_NIVEL n JOIN apr.REGLA_APROBACION r ON r.id_regla = n.id_regla
     JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = r.id_tipo_gestion
     WHERE tg.codigo = N'CLI_ALTA' AND r.prioridad = 20;
   Volver a automatica: lo mismo con tipo_aprobador = N'AUTOMATICA'.
   ----------------------------------------------------------------------------------------------- */
