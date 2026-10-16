/* ============================================================================
   Ronda 14 - Control de calidad de datos: coincidencias de clientes
   Ejecutar en CoreBancario_DEV. Idempotente.
   1. Parametros generales (categoria "Calidad de datos"):
        DUP_UMBRAL_ALERTA  (85)  porcentaje desde el cual se avisa de una persona parecida
        DUP_UMBRAL_FUERTE  (95)  porcentaje de coincidencia fuerte
        DUP_FUERTE_BLOQUEA (No)  si es Si, una coincidencia fuerte solo la puede saltar quien tenga CLI_DUPLICADO_AUTORIZAR
   2. Permiso CLI_DUPLICADO_AUTORIZAR (rol APROBADOR_GESTIONES).
   ============================================================================ */

INSERT INTO gen.PARAMETRO (id_compania, codigo, nombre, descripcion, categoria, tipo_dato, valor, valor_defecto, creado_por)
SELECT c.id_compania, v.codigo, v.nombre, v.descripcion, N'Calidad de datos', v.tipo, v.valor, v.valor, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES
    (N'DUP_UMBRAL_ALERTA', N'Umbral de alerta de duplicados (%)',
        N'Al registrar o promover un cliente, se avisa si ya hay una persona registrada con una coincidencia igual o mayor a este porcentaje (nombre, fecha de nacimiento, identificacion, contacto).', N'Entero', N'85'),
    (N'DUP_UMBRAL_FUERTE', N'Umbral de coincidencia fuerte (%)',
        N'Coincidencia a partir de la cual se considera casi seguro que es la misma persona. Debe ser mayor o igual al umbral de alerta.', N'Entero', N'95'),
    (N'DUP_FUERTE_BLOQUEA', N'La coincidencia fuerte bloquea el registro',
        N'Si es Si, ante una coincidencia fuerte solo un usuario con el permiso CLI_DUPLICADO_AUTORIZAR puede continuar. Si es No, basta indicar el motivo.', N'Booleano', N'false')
) AS v(codigo, nombre, descripcion, tipo, valor)
WHERE NOT EXISTS (SELECT 1 FROM gen.PARAMETRO p WHERE p.id_compania = c.id_compania AND p.codigo = v.codigo);
GO

IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_DUPLICADO_AUTORIZAR')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'CLI_DUPLICADO_AUTORIZAR', N'Autorizar registro con coincidencia fuerte', N'Transaccion',
            N'Continuar el alta o promocion de un cliente que tiene una coincidencia fuerte con una persona ya registrada (cuando el parametro DUP_FUERTE_BLOQUEA esta activo).', N'sistema');
GO

INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT r.id_rol_autorizacion, oa.id_objeto_autorizacion
FROM sec.ROL_AUTORIZACION r
CROSS JOIN sec.OBJETO_AUTORIZACION oa
WHERE r.codigo = N'APROBADOR_GESTIONES' AND r.id_compania IS NULL AND oa.codigo = N'CLI_DUPLICADO_AUTORIZAR'
  AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x
                  WHERE x.id_rol_autorizacion = r.id_rol_autorizacion AND x.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

SELECT codigo, valor, categoria FROM gen.PARAMETRO WHERE codigo LIKE N'DUP[_]%' ORDER BY id_compania, codigo;
GO
