/* Ronda 41: la auditoria general admite la accion 'Consulta' (revelado de datos enmascarados). Idempotente. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_LOG_AUDITORIA_accion' AND parent_object_id = OBJECT_ID(N'aud.LOG_AUDITORIA'))
    ALTER TABLE aud.LOG_AUDITORIA DROP CONSTRAINT CK_LOG_AUDITORIA_accion;
GO
ALTER TABLE aud.LOG_AUDITORIA ADD CONSTRAINT CK_LOG_AUDITORIA_accion CHECK (accion IN ('Alta','Cambio','Baja','Consulta'));
GO
