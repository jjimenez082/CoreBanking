/* ============================================================================
   Ronda 21 - Requisitos por rol a nivel de CAMPO
   Requiere: roles_ronda20.sql. Re-ejecutable.

   1. cli.TIPO_ROL_REQUISITO admite mas campos (segundo nombre/apellido, fecha de nacimiento, datos de la identificacion,
      division territorial, codigo postal, lineas 2 y 3 de la direccion).
      Parametrizacion «por exclusion»: es_requerido = 1 -> obligatorio; 0 -> opcional.
      Siempre obligatorios (no se guardan): primer nombre, primer apellido, tipo y numero de identificacion, linea 1 de una direccion.
   2. Valores iniciales: TODOS los roles conservan lo que se exigia hasta hoy (fecha de nacimiento, institucion y fecha de emision de la
      identificacion, division territorial y codigo postal segun el pais). El administrador los relaja desde
      Administracion > Requisitos por rol. No pisa filas existentes.
   3. cli.INTERLOCUTOR_FISICA.fecha_nacimiento pasa a admitir NULL (un rol puede no exigirla).
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Codigos admitidos ------------------------------------------------------ */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TIPO_ROL_REQUISITO_codigo' AND parent_object_id = OBJECT_ID(N'cli.TIPO_ROL_REQUISITO'))
    ALTER TABLE cli.TIPO_ROL_REQUISITO DROP CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo;
GO
ALTER TABLE cli.TIPO_ROL_REQUISITO ADD CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo CHECK (requisito IN (
    N'PERSONA_NOMBRE2', N'PERSONA_APELLIDO2', N'PERSONA_FECHA_NACIMIENTO',
    N'IDENT_INSTITUCION', N'IDENT_FECHA_EMISION', N'IDENT_FECHA_VENCIMIENTO',
    N'DIRECCION', N'DIR_NIVELES', N'DIR_CODIGO_POSTAL', N'DIR_LINEA2', N'DIR_LINEA3',
    N'CORREO', N'TELEFONO',
    N'ORG_NOMBRE_COMERCIAL', N'ORG_FECHA_CONSTITUCION', N'ORG_REFERENCIA_REGISTRAL', N'ORG_ACTIVIDAD'));  -- ORG_*: los agrega organizacion_ronda22.sql
GO
ALTER TABLE cli.TIPO_ROL_REQUISITO ALTER COLUMN requisito nvarchar(40) NOT NULL;
GO

/* 2. Valores iniciales ------------------------------------------------------ */
DECLARE @campos TABLE (requisito nvarchar(40), base bit);
INSERT INTO @campos VALUES
 (N'PERSONA_NOMBRE2', 0), (N'PERSONA_APELLIDO2', 0), (N'PERSONA_FECHA_NACIMIENTO', 1),
 (N'IDENT_INSTITUCION', 1), (N'IDENT_FECHA_EMISION', 1), (N'IDENT_FECHA_VENCIMIENTO', 0),
 (N'DIR_NIVELES', 1), (N'DIR_CODIGO_POSTAL', 1), (N'DIR_LINEA2', 0), (N'DIR_LINEA3', 0);

INSERT INTO cli.TIPO_ROL_REQUISITO (id_compania, id_tipo_rol, requisito, es_requerido, creado_por)
SELECT c.id_compania, i.id_lista_valores_item, k.requisito, k.base, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN @campos k
JOIN gen.LISTA_VALORES_TIPO t ON t.codigo = N'TIPO_ROL_INTERLOCUTOR'
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = t.id_lista_valores_tipo
 AND i.codigo IN (N'CLIENTE', N'BENEFICIARIO', N'FIRMANTE', N'FIADOR', N'CODEUDOR', N'AUTORIZADO')
WHERE NOT EXISTS (SELECT 1 FROM cli.TIPO_ROL_REQUISITO x
                   WHERE x.id_compania = c.id_compania AND x.id_tipo_rol = i.id_lista_valores_item AND x.requisito = k.requisito);
GO

/* 3. Fecha de nacimiento opcional en base de datos --------------------------- */
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'cli.INTERLOCUTOR_FISICA') AND name = N'fecha_nacimiento' AND is_nullable = 0)
    ALTER TABLE cli.INTERLOCUTOR_FISICA ALTER COLUMN fecha_nacimiento datetime2 NULL;
GO

/* Verificacion ----------------------------------------------------------------- */
SELECT i.codigo AS rol, r.requisito, r.es_requerido
FROM cli.TIPO_ROL_REQUISITO r JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_item = r.id_tipo_rol
ORDER BY i.orden, r.requisito;
SELECT is_nullable AS fecha_nacimiento_admite_null FROM sys.columns WHERE object_id = OBJECT_ID(N'cli.INTERLOCUTOR_FISICA') AND name = N'fecha_nacimiento';
GO
