/* ============================================================================
   mejoras_ronda11.sql - Administracion de aprobaciones (Fase 3)
   Ejecutar DESPUES de mejoras_ronda6 ... ronda10. Idempotente.
   Crea los permisos de las nuevas pantallas y los asigna al rol ADMINISTRADOR_SEGURIDAD:
     GES_REGLAS  -> Reglas de aprobacion
     GES_GRUPOS  -> Grupos de distribucion
   El campo Puesto del usuario (sec.USUARIO.puesto) ya fue creado en mejoras_ronda6.
   ============================================================================ */
USE CoreBancario_DEV;
GO
SET NOCOUNT ON;
GO

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'GES_REGLAS', N'Administrar reglas de aprobacion', N'Pantalla',
       N'Crear, ordenar, activar y probar las reglas de aprobacion por tipo de gestion.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'GES_REGLAS');
GO
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'GES_GRUPOS', N'Administrar grupos de distribucion', N'Pantalla',
       N'Crear grupos de usuarios y administrar sus miembros (usados como aprobadores y para notificaciones).', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'GES_GRUPOS');
GO

DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo IN (N'GES_REGLAS', N'GES_GRUPOS')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao
                       WHERE rao.id_rol_autorizacion = @id_rol AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

SELECT codigo, nombre FROM sec.OBJETO_AUTORIZACION WHERE codigo IN (N'GES_REGLAS', N'GES_GRUPOS');
GO
