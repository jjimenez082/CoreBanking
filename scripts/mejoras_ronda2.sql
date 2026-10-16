/* =====================================================================================================
   CoreBancario_DEV - Mejoras ronda 2  (idempotente; correr sobre CoreBancario_DEV)
   1. cli.INTERLOCUTOR_DIRECCION: detalle2 y detalle3 (lineas 2 y 3 de la direccion exacta).
   2. apr.GESTION: estado 'Devuelta' + comentario_devolucion/fecha_devolucion/devuelto_por.
      apr.GESTION_APROBACION: estado 'Devuelto'.
   3. gen.TIPO_CAMBIO: la columna 'referencia' pasa a llamarse 'contable' (valor para calculos contables);
      se actualizan los detalles de gestion y la auditoria ya registrados.
   ===================================================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ---- 1. Direcciones: 3 lineas ---- */
IF COL_LENGTH('cli.INTERLOCUTOR_DIRECCION', 'detalle2') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_DIRECCION ADD detalle2 nvarchar(200) NULL;
GO
IF COL_LENGTH('cli.INTERLOCUTOR_DIRECCION', 'detalle3') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_DIRECCION ADD detalle3 nvarchar(200) NULL;
GO

/* ---- 2. Gestiones: Devolver ---- */
IF COL_LENGTH('apr.GESTION', 'comentario_devolucion') IS NULL
    ALTER TABLE apr.GESTION ADD comentario_devolucion nvarchar(1000) NULL;
GO
IF COL_LENGTH('apr.GESTION', 'fecha_devolucion') IS NULL
    ALTER TABLE apr.GESTION ADD fecha_devolucion datetime2 NULL;
GO
IF COL_LENGTH('apr.GESTION', 'devuelto_por') IS NULL
    ALTER TABLE apr.GESTION ADD devuelto_por nvarchar(200) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_GESTION_estado' AND parent_object_id = OBJECT_ID(N'apr.GESTION'))
    ALTER TABLE apr.GESTION DROP CONSTRAINT CK_GESTION_estado;
GO
ALTER TABLE apr.GESTION ADD CONSTRAINT CK_GESTION_estado
    CHECK (estado IN ('Borrador','Pendiente de Aprobacion','Aprobada','Rechazada','Aplicada','Devuelta','Corregida'));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_GESTION_APROBACION_estado' AND parent_object_id = OBJECT_ID(N'apr.GESTION_APROBACION'))
    ALTER TABLE apr.GESTION_APROBACION DROP CONSTRAINT CK_GESTION_APROBACION_estado;
GO
ALTER TABLE apr.GESTION_APROBACION ADD CONSTRAINT CK_GESTION_APROBACION_estado
    CHECK (estado IN ('Pendiente','Aprobado','Rechazado','Devuelto'));
GO

/* ---- 3. Tipo de cambio: referencia -> contable ---- */
IF COL_LENGTH('gen.TIPO_CAMBIO', 'referencia') IS NOT NULL AND COL_LENGTH('gen.TIPO_CAMBIO', 'contable') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TIPO_CAMBIO_valores' AND parent_object_id = OBJECT_ID(N'gen.TIPO_CAMBIO'))
        ALTER TABLE gen.TIPO_CAMBIO DROP CONSTRAINT CK_TIPO_CAMBIO_valores;
    EXEC sp_rename N'gen.TIPO_CAMBIO.referencia', N'contable', N'COLUMN';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TIPO_CAMBIO_valores' AND parent_object_id = OBJECT_ID(N'gen.TIPO_CAMBIO'))
    ALTER TABLE gen.TIPO_CAMBIO ADD CONSTRAINT CK_TIPO_CAMBIO_valores CHECK (compra > 0 AND venta > 0 AND contable > 0 AND compra <= venta);
GO

/* Gestiones e historial ya registrados con el nombre anterior del campo */
UPDATE d SET nombre_campo = 'contable'
FROM apr.GESTION_DETALLE d JOIN apr.GESTION g ON g.id_gestion = d.id_gestion
WHERE g.entidad_negocio = 'TIPO_CAMBIO' AND d.nombre_campo = 'referencia';
GO
UPDATE aud.LOG_AUDITORIA SET nombre_campo = 'contable' WHERE entidad_negocio = 'TIPO_CAMBIO' AND nombre_campo = 'referencia';
GO

UPDATE gen.PARAMETRO SET descripcion = REPLACE(descripcion, 'referencia', 'valor contable') WHERE codigo = 'TC_VARIACION_MAXIMA_PORC' AND descripcion LIKE '%referencia%';
GO
UPDATE sec.OBJETO_AUTORIZACION SET nombre = N'Ingreso de tipo de cambio' WHERE codigo = 'TC_CARGA';
GO
UPDATE sec.OBJETO_AUTORIZACION SET descripcion = REPLACE(descripcion, 'compra/venta/referencia', 'compra/venta/valor contable') WHERE codigo = 'TC_CARGA';
GO

/* Verificacion */
SELECT 'detalle2/3' AS item, COUNT(*) AS columnas FROM sys.columns WHERE object_id = OBJECT_ID('cli.INTERLOCUTOR_DIRECCION') AND name IN ('detalle2','detalle3')
UNION ALL SELECT 'gestion devolucion', COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('apr.GESTION') AND name IN ('comentario_devolucion','fecha_devolucion','devuelto_por')
UNION ALL SELECT 'tc contable', COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('gen.TIPO_CAMBIO') AND name = 'contable';
GO
