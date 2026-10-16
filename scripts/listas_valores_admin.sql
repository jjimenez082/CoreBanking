/* ============================================================================
   Listas de Valores (Administracion) - preparacion de base de datos
   Idempotente: se puede correr mas de una vez.
   Ejecutar contra CoreBancario_DEV (o la base del cliente que corresponda).

   Que hace:
   1. Agrega la columna es_sistema (bit) a gen.LISTA_VALORES_TIPO y _ITEM.
      es_sistema = 1 -> el codigo usa el valor/lista por nombre; no se puede
      desactivar ni borrar desde la pantalla.
   2. Marca como "de sistema" las listas que ya existen (las sembradas por
      scripts del proyecto). Sus valores quedan editables (es_sistema = 0);
      marque a mano los que su codigo use por nombre.
   3. Crea los objetos de autorizacion:
        LOV_ACCESO          (Pantalla)    entrar a la pantalla
        LOV_ADMINISTRACION  (Transaccion) todas las listas + crear/borrar listas
        LOV_<codigo_lista>  (Transaccion) mantener los valores de esa lista
   4. Los asigna al rol ADMINISTRADOR_SEGURIDAD (si existe) para que
      qa.arquitecto pueda probar la pantalla.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   1. Columna es_sistema
   --------------------------------------------------------------------------- */
IF COL_LENGTH('gen.LISTA_VALORES_TIPO', 'es_sistema') IS NULL
    ALTER TABLE gen.LISTA_VALORES_TIPO
        ADD es_sistema bit NOT NULL CONSTRAINT DF_LISTA_VALORES_TIPO_es_sistema DEFAULT (0);
GO

IF COL_LENGTH('gen.LISTA_VALORES_ITEM', 'es_sistema') IS NULL
    ALTER TABLE gen.LISTA_VALORES_ITEM
        ADD es_sistema bit NOT NULL CONSTRAINT DF_LISTA_VALORES_ITEM_es_sistema DEFAULT (0);
GO

/* ---------------------------------------------------------------------------
   2. Las listas que ya existen hoy se consideran de sistema (el modelo y el
      codigo las referencian por nombre).
   --------------------------------------------------------------------------- */
UPDATE gen.LISTA_VALORES_TIPO SET es_sistema = 1 WHERE es_sistema = 0;
GO

/* ---------------------------------------------------------------------------
   3. Objetos de autorizacion
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_ACCESO')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'LOV_ACCESO', N'Acceso a Listas de Valores', N'Pantalla',
            N'Entrar a la pantalla Listas de Valores. Solo se ven las listas para las que ademas se tenga permiso.', N'sistema');

IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_ADMINISTRACION')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'LOV_ADMINISTRACION', N'Administracion de Listas de Valores', N'Transaccion',
            N'Mantener TODAS las listas de valores y ademas crear, editar, desactivar y borrar listas.', N'sistema');

-- Un objeto LOV_<codigo> por cada lista existente
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_' + t.codigo,
       N'Mantener lista de valores: ' + t.nombre,
       N'Transaccion',
       N'Crear, editar, activar/desactivar y borrar los valores de la lista ' + t.codigo + N'.',
       N'sistema'
FROM gen.LISTA_VALORES_TIPO t
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION oa WHERE oa.codigo = N'LOV_' + t.codigo);
GO

/* ---------------------------------------------------------------------------
   4. Asignar al rol ADMINISTRADOR_SEGURIDAD (si existe)
   --------------------------------------------------------------------------- */
DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');

IF @id_rol IS NOT NULL
BEGIN
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE (oa.codigo IN (N'LOV_ACCESO', N'LOV_ADMINISTRACION') OR oa.codigo LIKE N'LOV[_]%')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao
                       WHERE rao.id_rol_autorizacion = @id_rol
                         AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
END
ELSE
    PRINT 'AVISO: no existe el rol ADMINISTRADOR_SEGURIDAD; asigne los objetos LOV_* desde la pantalla de Seguridad.';
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT codigo, nombre, es_sistema, estado FROM gen.LISTA_VALORES_TIPO ORDER BY codigo;

SELECT oa.codigo, oa.tipo_objeto
FROM sec.OBJETO_AUTORIZACION oa
WHERE oa.codigo LIKE N'LOV[_]%'
ORDER BY oa.codigo;
GO
