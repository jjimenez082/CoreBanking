/* Ronda 28 - Grupo: estado 'Disuelto' (disolucion de un grupo por gestion). Re-ejecutable. */
USE CoreBancario_DEV;
GO
DECLARE @ck sysname = (SELECT TOP 1 cc.name FROM sys.check_constraints cc
                       WHERE cc.parent_object_id = OBJECT_ID(N'cli.INTERLOCUTOR') AND cc.definition LIKE N'%Fusionado%');
IF @ck IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = @ck AND definition LIKE N'%Disuelto%')
BEGIN
    EXEC (N'ALTER TABLE cli.INTERLOCUTOR DROP CONSTRAINT ' + @ck);
    ALTER TABLE cli.INTERLOCUTOR ADD CONSTRAINT CK_INTERLOCUTOR_estado CHECK (estado IN ('Activo','Fusionado','Fallecido','Disuelto'));
    PRINT 'CK_INTERLOCUTOR_estado actualizado (agrega Disuelto).';
END
ELSE PRINT 'CK_INTERLOCUTOR_estado ya incluye Disuelto (o no se encontro).';
GO
