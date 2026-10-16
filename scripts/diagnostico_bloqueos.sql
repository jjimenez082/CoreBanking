-- Diagnostico: ejecutar en la MISMA base que usa la API (la de la compañia/tenant).
SELECT DB_NAME() AS base_actual;
SELECT c.name AS columna FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'cli.INTERLOCUTOR_BLOQUEO') ORDER BY c.column_id;
SELECT COUNT(*) AS filas FROM cli.INTERLOCUTOR_BLOQUEO;
