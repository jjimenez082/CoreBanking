/* ============================================================================
   Ronda 19 - Auditoria del cliente (linea de tiempo anio > mes > dia en la ficha 360)
   Idempotente. Ejecutar contra CoreBancario_DEV.

   1. Objeto de autorizacion CLI_AUDITORIA (API): consultar el historial de auditoria de un cliente. Rol ANALISTA_CLIENTES.
   2. Indice en aud.LOG_AUDITORIA para consultar por compania + entidad + registro + fecha sin recorrer la bitacora completa.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_AUDITORIA')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'CLI_AUDITORIA', N'Consultar auditoria del cliente', N'API',
            N'Ver en la ficha del cliente su historial de auditoria (linea de tiempo por anio, mes y dia).', N'sistema');
GO

DECLARE @id_analista bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @id_obj      bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_AUDITORIA');
IF @id_analista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @id_analista AND id_objeto_autorizacion = @id_obj)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@id_analista, @id_obj);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LOG_AUDITORIA_cliente' AND object_id = OBJECT_ID(N'aud.LOG_AUDITORIA'))
    CREATE INDEX IX_LOG_AUDITORIA_cliente
        ON aud.LOG_AUDITORIA (id_compania, entidad_negocio, llave_registro, fecha_hora)
        INCLUDE (id_gestion, id_usuario, id_usuario_integracion);
GO

SELECT r.codigo AS rol, oa.codigo AS objeto
FROM sec.ROL_AUTORIZACION r
JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = r.id_rol_autorizacion
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
WHERE oa.codigo = N'CLI_AUDITORIA';
SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID(N'aud.LOG_AUDITORIA') AND name = N'IX_LOG_AUDITORIA_cliente';
GO
