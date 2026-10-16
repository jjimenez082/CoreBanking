/* =====================================================================================================
   CoreBancario_DEV - Mejoras ronda 3: trazabilidad de correcciones de gestiones  (idempotente)
   Requiere mejoras_ronda2.sql (estado 'Devuelta'/'Corregida').
   apr.GESTION gana:
     id_gestion_origen      gestion que esta gestion CORRIGE (la devuelta de la que nace).
     id_gestion_correccion  gestion que CORRIGIO a esta (se llena cuando el solicitante la reenvia).
     numero_correccion      cuantas veces se ha corregido la cadena: 0 = original, 1 = primera correccion, 2 = ...
   Quien ingreso / aprobo / rechazo / devolvio ya se guarda (id_usuario_solicitante y apr.GESTION_APROBACION);
   la bandeja y el detalle ahora lo muestran para todas las gestiones, tambien las anteriores a este script.
   ===================================================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('apr.GESTION', 'id_gestion_origen') IS NULL
    ALTER TABLE apr.GESTION ADD id_gestion_origen bigint NULL;
GO
IF COL_LENGTH('apr.GESTION', 'id_gestion_correccion') IS NULL
    ALTER TABLE apr.GESTION ADD id_gestion_correccion bigint NULL;
GO
IF COL_LENGTH('apr.GESTION', 'numero_correccion') IS NULL
    ALTER TABLE apr.GESTION ADD numero_correccion int NOT NULL CONSTRAINT DF_GESTION_numero_correccion DEFAULT (0);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_GESTION_origen')
    ALTER TABLE apr.GESTION ADD CONSTRAINT FK_GESTION_origen FOREIGN KEY (id_gestion_origen) REFERENCES apr.GESTION (id_gestion);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_GESTION_correccion')
    ALTER TABLE apr.GESTION ADD CONSTRAINT FK_GESTION_correccion FOREIGN KEY (id_gestion_correccion) REFERENCES apr.GESTION (id_gestion);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GESTION_origen' AND object_id = OBJECT_ID(N'apr.GESTION'))
    CREATE INDEX IX_GESTION_origen ON apr.GESTION (id_gestion_origen) WHERE id_gestion_origen IS NOT NULL;
GO

/* Datos anteriores: la correccion quedaba anotada en el comentario («... Corregida en la gestion #N») */
UPDATE g SET id_gestion_correccion = TRY_CAST(SUBSTRING(g.comentario, CHARINDEX(N'Corregida en la gestion #', g.comentario) + LEN(N'Corregida en la gestion #'), 18) AS bigint)
FROM apr.GESTION g
WHERE g.estado = 'Corregida' AND g.id_gestion_correccion IS NULL AND g.comentario LIKE N'%Corregida en la gestion #%'
  AND TRY_CAST(SUBSTRING(g.comentario, CHARINDEX(N'Corregida en la gestion #', g.comentario) + LEN(N'Corregida en la gestion #'), 18) AS bigint) IS NOT NULL;
GO
UPDATE n SET id_gestion_origen = o.id_gestion
FROM apr.GESTION n JOIN apr.GESTION o ON o.id_gestion_correccion = n.id_gestion
WHERE n.id_gestion_origen IS NULL;
GO
DECLARE @i int = 0, @n int = 1;
WHILE @n > 0 AND @i < 50
BEGIN
    UPDATE n SET numero_correccion = o.numero_correccion + 1
    FROM apr.GESTION n JOIN apr.GESTION o ON o.id_gestion = n.id_gestion_origen
    WHERE n.numero_correccion <> o.numero_correccion + 1;
    SET @n = @@ROWCOUNT; SET @i = @i + 1;
END;
GO

SELECT 'columnas nuevas' AS item, COUNT(*) AS total FROM sys.columns
WHERE object_id = OBJECT_ID('apr.GESTION') AND name IN ('id_gestion_origen','id_gestion_correccion','numero_correccion')
UNION ALL SELECT 'gestiones corregidas', COUNT(*) FROM apr.GESTION WHERE id_gestion_correccion IS NOT NULL;
GO
