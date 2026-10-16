/* ============================================================================
   Ronda 22 - Fase 1: Interlocutor ORGANIZACION (persona juridica)
   Requiere: roles_campos_ronda21.sql. Re-ejecutable.

   1. Listas de valores: TIPO_IDENTIFICACION_JURIDICA, FORMA_JURIDICA, ESTADO_LEGAL_ORG, ACTIVIDAD_ECONOMICA
      (+ objetos LOV_<codigo> para ADMINISTRADOR_SEGURIDAD).
   2. cli.INTERLOCUTOR_JURIDICA: forma juridica, pais de constitucion, referencia registral, estado legal, fecha de disolucion.
      (nombre_org1 = razon social; nombre_org2 queda en desuso: los nombres comerciales viven en su propia tabla.)
   3. Tablas nuevas: cli.INTERLOCUTOR_NOMBRE_COMERCIAL (0..N) y cli.INTERLOCUTOR_ACTIVIDAD (una principal).
   4. cli.TIPO_ROL_REQUISITO por tipo de persona (Fisica / Juridica) + codigos ORG_*.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Listas de valores ------------------------------------------------------ */
DECLARE @tipos TABLE (codigo nvarchar(100), nombre nvarchar(200), descripcion nvarchar(500));
INSERT INTO @tipos VALUES
 (N'TIPO_IDENTIFICACION_JURIDICA', N'Tipo de identificacion (organizacion)', N'Documentos con que se identifica una organizacion (cedula juridica, NITE, ID fiscal extranjero...).'),
 (N'FORMA_JURIDICA',               N'Forma juridica',                          N'Sociedad anonima, responsabilidad limitada, cooperativa, asociacion, etc.'),
 (N'ESTADO_LEGAL_ORG',             N'Estado legal de la organizacion',         N'Situacion legal de la organizacion ante el registro: activa, suspendida, en liquidacion, disuelta.'),
 (N'ACTIVIDAD_ECONOMICA',          N'Actividad economica',                     N'Clasificacion de actividades economicas (CIIU / CAECR). Se puede ampliar con el detalle que requiera el cliente.');

INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT t.codigo, t.nombre, t.descripcion, 1, N'sistema'
FROM @tipos t
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO x WHERE x.codigo = t.codigo);

DECLARE @items TABLE (tipo nvarchar(100), codigo nvarchar(100), nombre nvarchar(200), orden int, es_sistema bit);
INSERT INTO @items VALUES
 (N'TIPO_IDENTIFICACION_JURIDICA', N'CEDULA_JURIDICA',     N'Cedula juridica', 1, 1),
 (N'TIPO_IDENTIFICACION_JURIDICA', N'NITE',                N'NITE', 2, 0),
 (N'TIPO_IDENTIFICACION_JURIDICA', N'ID_FISCAL_EXTRANJERO',N'Identificacion fiscal extranjera', 3, 0),
 (N'TIPO_IDENTIFICACION_JURIDICA', N'CEDULA_INSTITUCION',  N'Cedula de institucion publica', 4, 0),

 (N'FORMA_JURIDICA', N'SA',                  N'Sociedad anonima', 1, 0),
 (N'FORMA_JURIDICA', N'SRL',                 N'Sociedad de responsabilidad limitada', 2, 0),
 (N'FORMA_JURIDICA', N'EIRL',                N'Empresa individual de responsabilidad limitada', 3, 0),
 (N'FORMA_JURIDICA', N'COOPERATIVA',         N'Cooperativa', 4, 0),
 (N'FORMA_JURIDICA', N'ASOCIACION',          N'Asociacion', 5, 0),
 (N'FORMA_JURIDICA', N'FUNDACION',           N'Fundacion', 6, 0),
 (N'FORMA_JURIDICA', N'FIDEICOMISO',         N'Fideicomiso', 7, 0),
 (N'FORMA_JURIDICA', N'SOCIEDAD_CIVIL',      N'Sociedad civil', 8, 0),
 (N'FORMA_JURIDICA', N'INSTITUCION_PUBLICA', N'Institucion publica', 9, 0),
 (N'FORMA_JURIDICA', N'SUCURSAL_EXTRANJERA', N'Sucursal de sociedad extranjera', 10, 0),
 (N'FORMA_JURIDICA', N'OTRA',                N'Otra', 99, 0),

 (N'ESTADO_LEGAL_ORG', N'ACTIVA',         N'Activa', 1, 1),
 (N'ESTADO_LEGAL_ORG', N'SUSPENDIDA',     N'Suspendida', 2, 1),
 (N'ESTADO_LEGAL_ORG', N'EN_LIQUIDACION', N'En liquidacion', 3, 1),
 (N'ESTADO_LEGAL_ORG', N'DISUELTA',       N'Disuelta', 4, 1),

 (N'ACTIVIDAD_ECONOMICA', N'A', N'A - Agricultura, ganaderia, silvicultura y pesca', 1, 0),
 (N'ACTIVIDAD_ECONOMICA', N'B', N'B - Explotacion de minas y canteras', 2, 0),
 (N'ACTIVIDAD_ECONOMICA', N'C', N'C - Industrias manufactureras', 3, 0),
 (N'ACTIVIDAD_ECONOMICA', N'D', N'D - Suministro de electricidad, gas, vapor y aire acondicionado', 4, 0),
 (N'ACTIVIDAD_ECONOMICA', N'E', N'E - Suministro de agua, alcantarillado y gestion de desechos', 5, 0),
 (N'ACTIVIDAD_ECONOMICA', N'F', N'F - Construccion', 6, 0),
 (N'ACTIVIDAD_ECONOMICA', N'G', N'G - Comercio al por mayor y al por menor', 7, 0),
 (N'ACTIVIDAD_ECONOMICA', N'H', N'H - Transporte y almacenamiento', 8, 0),
 (N'ACTIVIDAD_ECONOMICA', N'I', N'I - Alojamiento y servicios de comida', 9, 0),
 (N'ACTIVIDAD_ECONOMICA', N'J', N'J - Informacion y comunicaciones', 10, 0),
 (N'ACTIVIDAD_ECONOMICA', N'K', N'K - Actividades financieras y de seguros', 11, 0),
 (N'ACTIVIDAD_ECONOMICA', N'L', N'L - Actividades inmobiliarias', 12, 0),
 (N'ACTIVIDAD_ECONOMICA', N'M', N'M - Actividades profesionales, cientificas y tecnicas', 13, 0),
 (N'ACTIVIDAD_ECONOMICA', N'N', N'N - Actividades de servicios administrativos y de apoyo', 14, 0),
 (N'ACTIVIDAD_ECONOMICA', N'O', N'O - Administracion publica y defensa', 15, 0),
 (N'ACTIVIDAD_ECONOMICA', N'P', N'P - Ensenanza', 16, 0),
 (N'ACTIVIDAD_ECONOMICA', N'Q', N'Q - Salud humana y asistencia social', 17, 0),
 (N'ACTIVIDAD_ECONOMICA', N'R', N'R - Artes, entretenimiento y recreativas', 18, 0),
 (N'ACTIVIDAD_ECONOMICA', N'S', N'S - Otras actividades de servicios', 19, 0),
 (N'ACTIVIDAD_ECONOMICA', N'T', N'T - Actividades de los hogares como empleadores', 20, 0),
 (N'ACTIVIDAD_ECONOMICA', N'U', N'U - Organizaciones y organos extraterritoriales', 21, 0);

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, i.es_sistema, N'sistema'
FROM @items i
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = i.tipo
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x
                   WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

/* Permisos LOV_<codigo> para ADMINISTRADOR_SEGURIDAD */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_' + t.codigo, N'Mantener lista de valores: ' + t.nombre, N'Transaccion',
       N'Crear, editar, activar/desactivar y borrar los valores de la lista ' + t.codigo + N'.', N'sistema'
FROM gen.LISTA_VALORES_TIPO t
WHERE t.codigo IN (N'TIPO_IDENTIFICACION_JURIDICA', N'FORMA_JURIDICA', N'ESTADO_LEGAL_ORG', N'ACTIVIDAD_ECONOMICA')
  AND NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION x WHERE x.codigo = N'LOV_' + t.codigo);
GO
DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo IN (N'LOV_TIPO_IDENTIFICACION_JURIDICA', N'LOV_FORMA_JURIDICA', N'LOV_ESTADO_LEGAL_ORG', N'LOV_ACTIVIDAD_ECONOMICA')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO r
                       WHERE r.id_rol_autorizacion = @id_rol AND r.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

/* 2. Datos propios de la organizacion --------------------------------------- */
IF COL_LENGTH('cli.INTERLOCUTOR_JURIDICA', 'id_forma_juridica') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_JURIDICA ADD id_forma_juridica bigint NULL;       -- LISTA_VALORES_ITEM (FORMA_JURIDICA)
IF COL_LENGTH('cli.INTERLOCUTOR_JURIDICA', 'id_pais_constitucion') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_JURIDICA ADD id_pais_constitucion bigint NULL;    -- LISTA_VALORES_ITEM (PAIS)
IF COL_LENGTH('cli.INTERLOCUTOR_JURIDICA', 'referencia_registral') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_JURIDICA ADD referencia_registral nvarchar(200) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_JURIDICA', 'id_estado_legal') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_JURIDICA ADD id_estado_legal bigint NULL;         -- LISTA_VALORES_ITEM (ESTADO_LEGAL_ORG)
IF COL_LENGTH('cli.INTERLOCUTOR_JURIDICA', 'fecha_disolucion') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_JURIDICA ADD fecha_disolucion datetime2 NULL;
GO

/* 3. Nombres comerciales y actividades economicas ---------------------------- */
IF OBJECT_ID(N'cli.INTERLOCUTOR_NOMBRE_COMERCIAL', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_NOMBRE_COMERCIAL (
        id_nombre_comercial  bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor      bigint        NOT NULL,
        nombre               nvarchar(200) NOT NULL,
        estado               nvarchar(20)  NOT NULL CONSTRAINT DF_INT_NOMCOM_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100) NOT NULL,
        creado_en            datetime2     NOT NULL CONSTRAINT DF_INT_NOMCOM_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100) NULL,
        modificado_en        datetime2     NULL,
        CONSTRAINT PK_INTERLOCUTOR_NOMBRE_COMERCIAL PRIMARY KEY (id_nombre_comercial),
        CONSTRAINT CK_INT_NOMCOM_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_INT_NOMCOM_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
    );
    CREATE INDEX IX_INT_NOMCOM_interlocutor ON cli.INTERLOCUTOR_NOMBRE_COMERCIAL (id_interlocutor, estado);
END
GO

IF OBJECT_ID(N'cli.INTERLOCUTOR_ACTIVIDAD', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_ACTIVIDAD (
        id_actividad_interlocutor bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor      bigint        NOT NULL,
        id_actividad         bigint        NOT NULL,   -- LISTA_VALORES_ITEM (ACTIVIDAD_ECONOMICA)
        descripcion          nvarchar(300) NULL,
        es_principal         bit           NOT NULL CONSTRAINT DF_INT_ACTIV_principal DEFAULT (0),
        fecha_inicio         datetime2     NULL,
        estado               nvarchar(20)  NOT NULL CONSTRAINT DF_INT_ACTIV_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100) NOT NULL,
        creado_en            datetime2     NOT NULL CONSTRAINT DF_INT_ACTIV_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100) NULL,
        modificado_en        datetime2     NULL,
        CONSTRAINT PK_INTERLOCUTOR_ACTIVIDAD PRIMARY KEY (id_actividad_interlocutor),
        CONSTRAINT CK_INT_ACTIV_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_INT_ACTIV_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_ACTIV_ACTIVIDAD FOREIGN KEY (id_actividad) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE INDEX IX_INT_ACTIV_interlocutor ON cli.INTERLOCUTOR_ACTIVIDAD (id_interlocutor, estado);
    CREATE UNIQUE INDEX UX_INT_ACTIV_principal ON cli.INTERLOCUTOR_ACTIVIDAD (id_interlocutor)
        WHERE es_principal = 1 AND estado = 'Activo';
END
GO

/* 4. Requisitos por rol segun el tipo de persona ----------------------------- */
IF COL_LENGTH('cli.TIPO_ROL_REQUISITO', 'tipo_persona') IS NULL
    ALTER TABLE cli.TIPO_ROL_REQUISITO ADD tipo_persona nvarchar(10) NOT NULL
        CONSTRAINT DF_TIPO_ROL_REQUISITO_tipo DEFAULT ('Fisica');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TIPO_ROL_REQUISITO_tipo' AND parent_object_id = OBJECT_ID(N'cli.TIPO_ROL_REQUISITO'))
    ALTER TABLE cli.TIPO_ROL_REQUISITO ADD CONSTRAINT CK_TIPO_ROL_REQUISITO_tipo CHECK (tipo_persona IN ('Fisica', 'Juridica'));
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_TIPO_ROL_REQUISITO' AND parent_object_id = OBJECT_ID(N'cli.TIPO_ROL_REQUISITO'))
    ALTER TABLE cli.TIPO_ROL_REQUISITO DROP CONSTRAINT UQ_TIPO_ROL_REQUISITO;
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_TIPO_ROL_REQUISITO_tipo' AND parent_object_id = OBJECT_ID(N'cli.TIPO_ROL_REQUISITO'))
    ALTER TABLE cli.TIPO_ROL_REQUISITO ADD CONSTRAINT UQ_TIPO_ROL_REQUISITO_tipo UNIQUE (id_compania, id_tipo_rol, tipo_persona, requisito);
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TIPO_ROL_REQUISITO_codigo' AND parent_object_id = OBJECT_ID(N'cli.TIPO_ROL_REQUISITO'))
    ALTER TABLE cli.TIPO_ROL_REQUISITO DROP CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo;
GO
ALTER TABLE cli.TIPO_ROL_REQUISITO ADD CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo CHECK (requisito IN (
    N'PERSONA_NOMBRE2', N'PERSONA_APELLIDO2', N'PERSONA_FECHA_NACIMIENTO',
    N'IDENT_INSTITUCION', N'IDENT_FECHA_EMISION', N'IDENT_FECHA_VENCIMIENTO',
    N'DIRECCION', N'DIR_NIVELES', N'DIR_CODIGO_POSTAL', N'DIR_LINEA2', N'DIR_LINEA3',
    N'CORREO', N'TELEFONO',
    N'ORG_NOMBRE_COMERCIAL', N'ORG_FECHA_CONSTITUCION', N'ORG_REFERENCIA_REGISTRAL', N'ORG_ACTIVIDAD'));
GO

/* Valores iniciales para Juridica: copia de los codigos compartidos (identificacion, direccion, contactos) de la fila Fisica;
   la fecha de emision de la identificacion no aplica por defecto a organizaciones. No pisa filas existentes. */
INSERT INTO cli.TIPO_ROL_REQUISITO (id_compania, id_tipo_rol, tipo_persona, requisito, es_requerido, creado_por)
SELECT f.id_compania, f.id_tipo_rol, 'Juridica', f.requisito,
       CASE WHEN f.requisito = N'IDENT_FECHA_EMISION' THEN 0 ELSE f.es_requerido END, N'sistema'
FROM cli.TIPO_ROL_REQUISITO f
WHERE f.tipo_persona = 'Fisica'
  AND f.requisito IN (N'IDENT_INSTITUCION', N'IDENT_FECHA_EMISION', N'IDENT_FECHA_VENCIMIENTO',
                      N'DIRECCION', N'DIR_NIVELES', N'DIR_CODIGO_POSTAL', N'DIR_LINEA2', N'DIR_LINEA3', N'CORREO', N'TELEFONO')
  AND NOT EXISTS (SELECT 1 FROM cli.TIPO_ROL_REQUISITO x
                   WHERE x.id_compania = f.id_compania AND x.id_tipo_rol = f.id_tipo_rol
                     AND x.tipo_persona = 'Juridica' AND x.requisito = f.requisito);
GO

/* Campos propios de la organizacion: actividad economica obligatoria en todos los roles; el resto opcional. */
DECLARE @org TABLE (requisito nvarchar(40), base bit);
INSERT INTO @org VALUES (N'ORG_NOMBRE_COMERCIAL', 0), (N'ORG_FECHA_CONSTITUCION', 0), (N'ORG_REFERENCIA_REGISTRAL', 0), (N'ORG_ACTIVIDAD', 1);

INSERT INTO cli.TIPO_ROL_REQUISITO (id_compania, id_tipo_rol, tipo_persona, requisito, es_requerido, creado_por)
SELECT c.id_compania, i.id_lista_valores_item, 'Juridica', k.requisito, k.base, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN @org k
JOIN gen.LISTA_VALORES_TIPO t ON t.codigo = N'TIPO_ROL_INTERLOCUTOR'
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = t.id_lista_valores_tipo
 AND i.codigo IN (N'CLIENTE', N'BENEFICIARIO', N'FIRMANTE', N'FIADOR', N'CODEUDOR', N'AUTORIZADO')
WHERE NOT EXISTS (SELECT 1 FROM cli.TIPO_ROL_REQUISITO x
                   WHERE x.id_compania = c.id_compania AND x.id_tipo_rol = i.id_lista_valores_item
                     AND x.tipo_persona = 'Juridica' AND x.requisito = k.requisito);
GO

/* Verificacion ----------------------------------------------------------------- */
SELECT t.codigo AS lista, COUNT(i.id_lista_valores_item) AS valores
FROM gen.LISTA_VALORES_TIPO t LEFT JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = t.id_lista_valores_tipo
WHERE t.codigo IN (N'TIPO_IDENTIFICACION_JURIDICA', N'FORMA_JURIDICA', N'ESTADO_LEGAL_ORG', N'ACTIVIDAD_ECONOMICA')
GROUP BY t.codigo;
SELECT tipo_persona, COUNT(*) AS filas FROM cli.TIPO_ROL_REQUISITO GROUP BY tipo_persona;
SELECT OBJECT_ID(N'cli.INTERLOCUTOR_NOMBRE_COMERCIAL') AS tabla_nombres, OBJECT_ID(N'cli.INTERLOCUTOR_ACTIVIDAD') AS tabla_actividades;
GO
