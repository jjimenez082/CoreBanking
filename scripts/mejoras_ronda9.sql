-- Ronda 9: comentario del aprobador (hasta 1000 caracteres) en cada nivel de aprobación.
USE CoreBancario_DEV;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('apr.GESTION_APROBACION') AND name = 'comentario' AND max_length <> 2000)
    ALTER TABLE apr.GESTION_APROBACION ALTER COLUMN comentario nvarchar(1000) NULL;
GO
