/* ============================================================================
   Ronda 27 - Campos de identidad de la PERSONA (todos opcionales, parametrizables por rol)
   Requiere: roles_campos_ronda21.sql, organizacion_ronda22.sql. Re-ejecutable.

   1. cli.INTERLOCUTOR_FISICA: nombre conocido, pais de nacimiento, sexo, estado civil.
   2. cli.INTERLOCUTOR_NACIONALIDAD: nacionalidades 1..N (baja logica, sin repetir pais activo).
   3. Listas de valores SEXO y ESTADO_CIVIL (+ objetos LOV_* para ADMINISTRADOR_SEGURIDAD). El pais usa la lista PAIS.
   4. cli.TIPO_ROL_REQUISITO admite los codigos nuevos (PERSONA_NOMBRE_CONOCIDO, PERSONA_PAIS_NACIMIENTO, PERSONA_NACIONALIDAD,
      PERSONA_SEXO, PERSONA_ESTADO_CIVIL). Sin fila = opcional: ningun rol los exige hasta que el administrador lo decida
      en Administracion > Requisitos por rol.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Columnas en la persona fisica ---------------------------------------- */
IF COL_LENGTH('cli.INTERLOCUTOR_FISICA', 'nombre_conocido') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_FISICA ADD nombre_conocido nvarchar(100) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_FISICA', 'id_pais_nacimiento') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_FISICA ADD id_pais_nacimiento bigint NULL;     -- LISTA_VALORES_ITEM (PAIS)
IF COL_LENGTH('cli.INTERLOCUTOR_FISICA', 'id_sexo') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_FISICA ADD id_sexo bigint NULL;                -- LISTA_VALORES_ITEM (SEXO)
IF COL_LENGTH('cli.INTERLOCUTOR_FISICA', 'id_estado_civil') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_FISICA ADD id_estado_civil bigint NULL;        -- LISTA_VALORES_ITEM (ESTADO_CIVIL)
GO

/* 2. Nacionalidades --------------------------------------------------------- */
IF OBJECT_ID(N'cli.INTERLOCUTOR_NACIONALIDAD', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_NACIONALIDAD (
        id_nacionalidad_interlocutor bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor      bigint        NOT NULL,
        id_pais              bigint        NOT NULL,   -- LISTA_VALORES_ITEM (PAIS)
        estado               nvarchar(20)  NOT NULL CONSTRAINT DF_INT_NACION_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100) NOT NULL,
        creado_en            datetime2     NOT NULL CONSTRAINT DF_INT_NACION_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100) NULL,
        modificado_en        datetime2     NULL,
        CONSTRAINT PK_INTERLOCUTOR_NACIONALIDAD PRIMARY KEY (id_nacionalidad_interlocutor),
        CONSTRAINT CK_INT_NACION_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_INT_NACION_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_NACION_PAIS FOREIGN KEY (id_pais) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE INDEX IX_INT_NACION_interlocutor ON cli.INTERLOCUTOR_NACIONALIDAD (id_interlocutor, estado);
    CREATE UNIQUE INDEX UX_INT_NACION_pais ON cli.INTERLOCUTOR_NACIONALIDAD (id_interlocutor, id_pais) WHERE estado = 'Activo';
END
GO

/* 3. Listas de valores -------------------------------------------------------- */
DECLARE @tipos TABLE (codigo nvarchar(100), nombre nvarchar(200), descripcion nvarchar(500));
INSERT INTO @tipos VALUES
 (N'SEXO',         N'Sexo',         N'Sexo de la persona segun su documento de identidad.'),
 (N'ESTADO_CIVIL', N'Estado civil', N'Estado civil de la persona.');
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT t.codigo, t.nombre, t.descripcion, 1, N'sistema' FROM @tipos t
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO x WHERE x.codigo = t.codigo);

DECLARE @items TABLE (tipo nvarchar(100), codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES
 (N'SEXO', N'MASCULINO', N'Masculino', 1),
 (N'SEXO', N'FEMENINO',  N'Femenino', 2),
 (N'ESTADO_CIVIL', N'SOLTERO',     N'Soltero(a)', 1),
 (N'ESTADO_CIVIL', N'CASADO',      N'Casado(a)', 2),
 (N'ESTADO_CIVIL', N'UNION_LIBRE', N'Unión libre', 3),
 (N'ESTADO_CIVIL', N'SEPARADO',    N'Separado(a)', 4),
 (N'ESTADO_CIVIL', N'DIVORCIADO',  N'Divorciado(a)', 5),
 (N'ESTADO_CIVIL', N'VIUDO',       N'Viudo(a)', 6);
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = i.tipo
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT v.codigo, v.nombre, N'Transaccion', v.descripcion, N'sistema'
FROM (VALUES (N'LOV_SEXO', N'Mantener lista de valores: Sexo', N'Mantenimiento de la lista de valores SEXO.'),
             (N'LOV_ESTADO_CIVIL', N'Mantener lista de valores: Estado civil', N'Mantenimiento de la lista de valores ESTADO_CIVIL.')) v(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION x WHERE x.codigo = v.codigo);
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'LOV_SEXO', N'LOV_ESTADO_CIVIL')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

/* 4. Codigos de requisito admitidos -------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TIPO_ROL_REQUISITO_codigo' AND parent_object_id = OBJECT_ID(N'cli.TIPO_ROL_REQUISITO'))
    ALTER TABLE cli.TIPO_ROL_REQUISITO DROP CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo;
GO
ALTER TABLE cli.TIPO_ROL_REQUISITO ADD CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo CHECK (requisito IN (
    N'PERSONA_NOMBRE2', N'PERSONA_APELLIDO2', N'PERSONA_FECHA_NACIMIENTO',
    N'PERSONA_NOMBRE_CONOCIDO', N'PERSONA_PAIS_NACIMIENTO', N'PERSONA_NACIONALIDAD', N'PERSONA_SEXO', N'PERSONA_ESTADO_CIVIL',
    N'IDENT_INSTITUCION', N'IDENT_FECHA_EMISION', N'IDENT_FECHA_VENCIMIENTO',
    N'DIRECCION', N'DIR_NIVELES', N'DIR_CODIGO_POSTAL', N'DIR_LINEA2', N'DIR_LINEA3',
    N'CORREO', N'TELEFONO',
    N'ORG_NOMBRE_COMERCIAL', N'ORG_FECHA_CONSTITUCION', N'ORG_REFERENCIA_REGISTRAL', N'ORG_ACTIVIDAD'));
GO

SELECT lt.codigo AS lista, i.codigo, i.nombre FROM gen.LISTA_VALORES_ITEM i
JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE lt.codigo IN (N'SEXO', N'ESTADO_CIVIL') ORDER BY lt.codigo, i.orden;
GO
