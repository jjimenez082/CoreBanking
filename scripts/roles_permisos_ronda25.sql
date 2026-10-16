/* ============================================================================
   Ronda 25 - Permiso de consulta de roles: CLI_ROL_CONSULTA
   Idempotente. Ejecutar contra CoreBancario_DEV.
   CLI_ROL_CONSULTA (API): ver la pantalla «Roles» de un cliente (roles vigentes, disponibles e historial).
   CLI_ROL          (API): solicitar agregar / quitar roles (gestion con aprobacion). Ya existia.
   Para no quitar acceso a nadie, se concede CLI_ROL_CONSULTA a todo rol de autorizacion que ya tenga CLI_ROL o CLI_CONSULTA.
   ============================================================================ */
SET NOCOUNT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_ROL_CONSULTA')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'CLI_ROL_CONSULTA', N'Consultar roles de un cliente', N'API',
            N'Ver los roles vigentes, disponibles e historial de un cliente. Para solicitar cambios se requiere ademas CLI_ROL.', N'sistema');
GO
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT DISTINCT rao.id_rol_autorizacion, nuevo.id_objeto_autorizacion
FROM sec.ROL_AUTORIZACION_OBJETO rao
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion AND oa.codigo IN (N'CLI_ROL', N'CLI_CONSULTA')
CROSS JOIN (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_ROL_CONSULTA') nuevo
WHERE NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x
                   WHERE x.id_rol_autorizacion = rao.id_rol_autorizacion AND x.id_objeto_autorizacion = nuevo.id_objeto_autorizacion);
GO
SELECT ra.codigo AS rol_autorizacion, oa.codigo AS objeto
FROM sec.ROL_AUTORIZACION_OBJETO rao
JOIN sec.ROL_AUTORIZACION ra ON ra.id_rol_autorizacion = rao.id_rol_autorizacion
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
WHERE oa.codigo IN (N'CLI_ROL', N'CLI_ROL_CONSULTA') ORDER BY ra.codigo, oa.codigo;
GO
