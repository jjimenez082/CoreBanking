/* Ronda 36 (r44b): permisos del Gestor documental (subir, reemplazar, obsoletar). Re-ejecutable. */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, x.tipo, x.descripcion, N'sistema' FROM (VALUES
  (N'DOC_SUBIR',      N'Agregar documentos al expediente',   N'API', N'Agregar documentos al expediente de un cliente desde el Gestor documental.'),
  (N'DOC_REEMPLAZAR', N'Reemplazar documentos del expediente', N'API', N'Subir una nueva versión de un documento vigente del expediente.'),
  (N'DOC_OBSOLETAR',  N'Obsoletar documentos del expediente',  N'API', N'Marcar como obsoleto un documento vigente del expediente.')
) AS x(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);
GO

/* Los roles que ya podian adjuntar documentos reciben los tres permisos nuevos. */
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT DISTINCT rao.id_rol_autorizacion, nuevo.id_objeto_autorizacion
FROM sec.ROL_AUTORIZACION_OBJETO rao
JOIN sec.OBJETO_AUTORIZACION actual ON actual.id_objeto_autorizacion = rao.id_objeto_autorizacion AND actual.codigo = N'DOC_ADJUNTAR'
JOIN sec.OBJETO_AUTORIZACION nuevo ON nuevo.codigo IN (N'DOC_SUBIR', N'DOC_REEMPLAZAR', N'DOC_OBSOLETAR')
WHERE NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x
                   WHERE x.id_rol_autorizacion = rao.id_rol_autorizacion AND x.id_objeto_autorizacion = nuevo.id_objeto_autorizacion);
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'DOC_SUBIR', N'DOC_REEMPLAZAR', N'DOC_OBSOLETAR')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO
SELECT codigo FROM sec.OBJETO_AUTORIZACION WHERE codigo LIKE N'DOC[_]%' ORDER BY codigo;
GO
