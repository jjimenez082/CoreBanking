/* ============================================================================
   MEJORAS RONDA 8 - Cambio de asignacion de aprobador
   ============================================================================
   - Permiso GES_REASIGNAR (app "Cambio de asignacion de aprobador"): se concede a los roles que ya tienen
     GES_APROBAR_TODAS. Puede darse a otros roles desde Seguridad.
   - apr.GESTION_APROBACION.id_usuario_original: aprobador original del nivel cuando se reasigna (la cadena de
     superiores de los niveles siguientes parte de el).
   Correr contra CoreBancario_DEV. Es repetible.
   ============================================================================ */
USE CoreBancario_DEV;
GO
SET NOCOUNT ON;
GO
IF COL_LENGTH('apr.GESTION_APROBACION', 'id_usuario_original') IS NULL
    ALTER TABLE apr.GESTION_APROBACION ADD id_usuario_original bigint NULL;
GO
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'GES_REASIGNAR', N'Cambiar la asignacion de aprobador de gestiones', N'Transaccion',
       N'Pasar gestiones pendientes de un aprobador a otro (ausencias, vacaciones, salidas).', N'script ronda8'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'GES_REASIGNAR');
GO
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT DISTINCT rao.id_rol_autorizacion, nuevo.id_objeto_autorizacion
FROM sec.ROL_AUTORIZACION_OBJETO rao
JOIN sec.OBJETO_AUTORIZACION todas ON todas.id_objeto_autorizacion = rao.id_objeto_autorizacion AND todas.codigo = N'GES_APROBAR_TODAS'
CROSS JOIN (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'GES_REASIGNAR') nuevo
WHERE NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x
                   WHERE x.id_rol_autorizacion = rao.id_rol_autorizacion AND x.id_objeto_autorizacion = nuevo.id_objeto_autorizacion);
GO
SELECT ra.codigo AS rol, oa.codigo AS permiso
FROM sec.ROL_AUTORIZACION_OBJETO rao
JOIN sec.ROL_AUTORIZACION ra ON ra.id_rol_autorizacion = rao.id_rol_autorizacion
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
WHERE oa.codigo = N'GES_REASIGNAR';
GO
