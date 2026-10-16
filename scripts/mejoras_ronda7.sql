/* ============================================================================
   MEJORAS RONDA 7 - Numero de negocio de las gestiones (rangos de numeracion)
   ============================================================================
   - apr.GESTION.numero: numero visible de la gestion (ceros a la izquierda, longitud fija). El id interno no cambia.
   - Rango de numeracion GESTION por compania (8 digitos, 1 a 99999999). Se administra en
     Administracion > Rangos de numeracion (longitud, inicio, fin, bloque).
   - Las gestiones existentes se numeran con su id interno rellenado a la longitud del rango.
   Correr contra CoreBancario_DEV. Es repetible.
   ============================================================================ */
USE CoreBancario_DEV;
GO
SET NOCOUNT ON;
GO
IF COL_LENGTH('apr.GESTION', 'numero') IS NULL
    ALTER TABLE apr.GESTION ADD numero nvarchar(30) NULL;
GO
INSERT INTO gen.RANGO_NUMERACION (id_compania, objeto, longitud, numero_desde, numero_hasta, ultimo_asignado, tamano_bloque, creado_por)
SELECT c.id_compania, N'GESTION', 8, 1, 99999999, 0, 1, N'script ronda7'
FROM gen.COMPANIA c
WHERE NOT EXISTS (SELECT 1 FROM gen.RANGO_NUMERACION r
                   WHERE r.id_compania = c.id_compania AND r.objeto = N'GESTION' AND r.id_alcance = 0 AND r.estado = 'Activo');
GO
UPDATE apr.GESTION
   SET numero = RIGHT(REPLICATE('0', (SELECT TOP 1 r.longitud FROM gen.RANGO_NUMERACION r
                                       WHERE r.id_compania = apr.GESTION.id_compania AND r.objeto = N'GESTION' AND r.id_alcance = 0 AND r.estado = 'Activo'))
                      + CONVERT(varchar(20), id_gestion),
                      (SELECT TOP 1 r.longitud FROM gen.RANGO_NUMERACION r
                        WHERE r.id_compania = apr.GESTION.id_compania AND r.objeto = N'GESTION' AND r.id_alcance = 0 AND r.estado = 'Activo'))
 WHERE numero IS NULL;
GO
/* El rango sigue despues del mayor id ya numerado. */
UPDATE gen.RANGO_NUMERACION
   SET ultimo_asignado = (SELECT MAX(g.id_gestion) FROM apr.GESTION g WHERE g.id_compania = gen.RANGO_NUMERACION.id_compania)
 WHERE objeto = N'GESTION' AND id_alcance = 0
   AND ultimo_asignado < (SELECT MAX(g.id_gestion) FROM apr.GESTION g WHERE g.id_compania = gen.RANGO_NUMERACION.id_compania)
   AND (SELECT MAX(g.id_gestion) FROM apr.GESTION g WHERE g.id_compania = gen.RANGO_NUMERACION.id_compania) <= numero_hasta;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'apr.GESTION') AND name = N'numero' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM apr.GESTION WHERE numero IS NULL)
    ALTER TABLE apr.GESTION ALTER COLUMN numero nvarchar(30) NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_GESTION_numero' AND object_id = OBJECT_ID(N'apr.GESTION'))
    CREATE UNIQUE INDEX UX_GESTION_numero ON apr.GESTION (id_compania, numero);
GO
SELECT r.objeto, r.longitud, r.numero_desde, r.numero_hasta, r.ultimo_asignado,
       (SELECT COUNT(*) FROM apr.GESTION g WHERE g.id_compania = r.id_compania) AS gestiones_numeradas
FROM gen.RANGO_NUMERACION r WHERE r.objeto = N'GESTION';
GO
