/* ============================================================================
   Interlocutor: identificaciones, direcciones y contactos 1:n, borradores y
   roles.  Idempotente: se puede correr mas de una vez.
   Ejecutar contra CoreBancario_DEV (o la base del cliente que corresponda),
   DESPUES de modelo_fisico_clientes_mvp1.sql, modelo_fisico_transversal_minimo.sql
   y listas_valores_admin.sql.

   Que hace:
   1. Crea las listas de valores nuevas (institucion emisora, tipo de direccion,
      tipo de contacto, tipo de rol) y agrega Pasaporte al tipo de identificacion.
   2. Crea cli.INTERLOCUTOR_IDENTIFICACION, _DIRECCION, _CONTACTO (1:n) con
      baja logica (estado Activo/Inactivo) y auditoria de creacion/modificacion.
   3. Crea cli.INTERLOCUTOR_BORRADOR (altas guardadas sin finalizar; solo las ve
      su propietario; vencen segun Borradores:DiasVigencia de appsettings).
   4. MIGRA la identificacion unica que hoy vive en cli.INTERLOCUTOR a la tabla
      nueva (queda como identificacion principal) y elimina esas columnas de
      cli.INTERLOCUTOR junto con su restriccion de unicidad.
   5. Asigna el rol CLIENTE a los interlocutores existentes que no tengan rol.
   6. Crea los objetos de autorizacion LOV_<codigo> de las listas nuevas y los
      asigna a ADMINISTRADOR_SEGURIDAD.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   1. Listas de valores
   --------------------------------------------------------------------------- */
DECLARE @tipos TABLE (codigo nvarchar(100), nombre nvarchar(200), descripcion nvarchar(500));
INSERT INTO @tipos VALUES
 (N'INSTITUCION_EMISORA_ID', N'Institucion emisora de identificacion', N'Entidad que emite un documento de identificacion (TSE, Migracion, etc.).'),
 (N'TIPO_DIRECCION',         N'Tipo de direccion',                      N'Clasificacion de las direcciones de un interlocutor (casa, trabajo, etc.).'),
 (N'TIPO_CONTACTO',          N'Tipo de medio de contacto',              N'Telefono movil, telefono fijo, correo electronico, WhatsApp. El codigo define la validacion de formato.'),
 (N'TIPO_ROL_INTERLOCUTOR',  N'Tipo de rol de interlocutor',            N'Rol que cumple un interlocutor: cliente, beneficiario, firmante, etc.');

INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT t.codigo, t.nombre, t.descripcion, 1, N'sistema'
FROM @tipos t
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO x WHERE x.codigo = t.codigo);

DECLARE @items TABLE (tipo nvarchar(100), codigo nvarchar(100), nombre nvarchar(200), orden int, es_sistema bit);
INSERT INTO @items VALUES
 (N'TIPO_IDENTIFICACION_FISICA', N'PASAPORTE', N'Pasaporte', 3, 0),
 (N'TIPO_IDENTIFICACION_FISICA', N'LICENCIA_CONDUCIR', N'Licencia de conducir', 4, 0),

 (N'INSTITUCION_EMISORA_ID', N'TSE',     N'Tribunal Supremo de Elecciones (TSE)', 1, 0),
 (N'INSTITUCION_EMISORA_ID', N'DGME',    N'Direccion General de Migracion y Extranjeria', 2, 0),
 (N'INSTITUCION_EMISORA_ID', N'MREC',    N'Ministerio de Relaciones Exteriores y Culto', 3, 0),
 (N'INSTITUCION_EMISORA_ID', N'COSEVI',  N'Consejo de Seguridad Vial (COSEVI)', 4, 0),
 (N'INSTITUCION_EMISORA_ID', N'RN',      N'Registro Nacional', 5, 0),

 (N'TIPO_DIRECCION', N'CASA',        N'Casa de habitacion', 1, 0),
 (N'TIPO_DIRECCION', N'TRABAJO',     N'Trabajo', 2, 0),
 (N'TIPO_DIRECCION', N'FACTURACION', N'Facturacion', 3, 0),
 (N'TIPO_DIRECCION', N'OTRA',        N'Otra', 4, 0),

 (N'TIPO_CONTACTO', N'TELEFONO_MOVIL', N'Telefono movil', 1, 1),
 (N'TIPO_CONTACTO', N'TELEFONO_FIJO',  N'Telefono fijo', 2, 1),
 (N'TIPO_CONTACTO', N'CORREO',         N'Correo electronico', 3, 1),
 (N'TIPO_CONTACTO', N'WHATSAPP',       N'WhatsApp', 4, 1),

 (N'TIPO_ROL_INTERLOCUTOR', N'CLIENTE',      N'Cliente', 1, 1),
 (N'TIPO_ROL_INTERLOCUTOR', N'BENEFICIARIO', N'Beneficiario', 2, 1),
 (N'TIPO_ROL_INTERLOCUTOR', N'FIRMANTE',     N'Firmante', 3, 1),
 (N'TIPO_ROL_INTERLOCUTOR', N'FIADOR',       N'Fiador', 4, 0),
 (N'TIPO_ROL_INTERLOCUTOR', N'CODEUDOR',     N'Codeudor', 5, 0),
 (N'TIPO_ROL_INTERLOCUTOR', N'AUTORIZADO',   N'Autorizado', 6, 0);

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, i.es_sistema, N'sistema'
FROM @items i
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = i.tipo
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x
                   WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

/* ---------------------------------------------------------------------------
   2. Tablas hijas 1:n
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'cli.INTERLOCUTOR_IDENTIFICACION', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_IDENTIFICACION (
        id_identificacion        bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor          bigint        NOT NULL,
        id_compania              bigint        NOT NULL,   -- copia de cli.INTERLOCUTOR para poder exigir unicidad por compania
        id_tipo_identificacion   bigint        NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_IDENTIFICACION_FISICA)
        numero_identificacion    nvarchar(50)  NOT NULL,
        id_institucion_emisora   bigint        NULL,       -- LISTA_VALORES_ITEM (INSTITUCION_EMISORA_ID); NULL solo en datos migrados
        fecha_emision            date          NULL,       -- NULL solo en datos migrados; la API la exige en altas nuevas
        fecha_vencimiento        date          NULL,
        es_principal             bit           NOT NULL CONSTRAINT DF_INTERLOCUTOR_IDENTIFICACION_principal DEFAULT (0),
        estado                   nvarchar(20)  NOT NULL CONSTRAINT DF_INTERLOCUTOR_IDENTIFICACION_estado DEFAULT ('Activo'),
        creado_por               nvarchar(100) NOT NULL,
        creado_en                datetime2     NOT NULL CONSTRAINT DF_INTERLOCUTOR_IDENTIFICACION_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por           nvarchar(100) NULL,
        modificado_en            datetime2     NULL,
        CONSTRAINT PK_INTERLOCUTOR_IDENTIFICACION PRIMARY KEY (id_identificacion),
        CONSTRAINT CK_INTERLOCUTOR_IDENTIFICACION_estado CHECK (estado IN ('Activo','Inactivo')),
        CONSTRAINT CK_INTERLOCUTOR_IDENTIFICACION_fechas CHECK (fecha_vencimiento IS NULL OR fecha_emision IS NULL OR fecha_vencimiento > fecha_emision),
        CONSTRAINT FK_INTERLOCUTOR_IDENTIFICACION_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INTERLOCUTOR_IDENTIFICACION_tipo FOREIGN KEY (id_tipo_identificacion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INTERLOCUTOR_IDENTIFICACION_institucion FOREIGN KEY (id_institucion_emisora) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

-- CLI-020: una identificacion activa (tipo + numero) pertenece a un solo interlocutor por compania.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_IDENTIFICACION_unica' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_IDENTIFICACION'))
    CREATE UNIQUE INDEX UX_INTERLOCUTOR_IDENTIFICACION_unica
        ON cli.INTERLOCUTOR_IDENTIFICACION (id_compania, id_tipo_identificacion, numero_identificacion)
        WHERE estado = 'Activo';
-- Una sola identificacion principal activa por interlocutor.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_IDENTIFICACION_principal' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_IDENTIFICACION'))
    CREATE UNIQUE INDEX UX_INTERLOCUTOR_IDENTIFICACION_principal
        ON cli.INTERLOCUTOR_IDENTIFICACION (id_interlocutor)
        WHERE es_principal = 1 AND estado = 'Activo';
-- Busqueda por numero (prefijo y coincidencia exacta).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_IDENTIFICACION_numero' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_IDENTIFICACION'))
    CREATE INDEX IX_INTERLOCUTOR_IDENTIFICACION_numero
        ON cli.INTERLOCUTOR_IDENTIFICACION (numero_identificacion) INCLUDE (id_interlocutor, id_compania, estado);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_IDENTIFICACION_interlocutor' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_IDENTIFICACION'))
    CREATE INDEX IX_INTERLOCUTOR_IDENTIFICACION_interlocutor ON cli.INTERLOCUTOR_IDENTIFICACION (id_interlocutor);
GO

IF OBJECT_ID(N'cli.INTERLOCUTOR_DIRECCION', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_DIRECCION (
        id_direccion        bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor     bigint         NOT NULL,
        id_tipo_direccion   bigint         NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_DIRECCION)
        provincia           nvarchar(100)  NULL,
        canton              nvarchar(100)  NULL,
        distrito            nvarchar(100)  NULL,
        detalle             nvarchar(500)  NOT NULL,   -- direccion exacta
        es_principal        bit            NOT NULL CONSTRAINT DF_INTERLOCUTOR_DIRECCION_principal DEFAULT (0),
        estado              nvarchar(20)   NOT NULL CONSTRAINT DF_INTERLOCUTOR_DIRECCION_estado DEFAULT ('Activo'),
        creado_por          nvarchar(100)  NOT NULL,
        creado_en           datetime2      NOT NULL CONSTRAINT DF_INTERLOCUTOR_DIRECCION_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por      nvarchar(100)  NULL,
        modificado_en       datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_DIRECCION PRIMARY KEY (id_direccion),
        CONSTRAINT CK_INTERLOCUTOR_DIRECCION_estado CHECK (estado IN ('Activo','Inactivo')),
        CONSTRAINT FK_INTERLOCUTOR_DIRECCION_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INTERLOCUTOR_DIRECCION_tipo FOREIGN KEY (id_tipo_direccion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_DIRECCION_principal' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_DIRECCION'))
    CREATE UNIQUE INDEX UX_INTERLOCUTOR_DIRECCION_principal
        ON cli.INTERLOCUTOR_DIRECCION (id_interlocutor) WHERE es_principal = 1 AND estado = 'Activo';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_DIRECCION_interlocutor' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_DIRECCION'))
    CREATE INDEX IX_INTERLOCUTOR_DIRECCION_interlocutor ON cli.INTERLOCUTOR_DIRECCION (id_interlocutor);
GO

IF OBJECT_ID(N'cli.INTERLOCUTOR_CONTACTO', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_CONTACTO (
        id_contacto         bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor     bigint         NOT NULL,
        id_tipo_contacto    bigint         NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_CONTACTO); el codigo define la validacion
        valor               nvarchar(200)  NOT NULL,
        es_principal        bit            NOT NULL CONSTRAINT DF_INTERLOCUTOR_CONTACTO_principal DEFAULT (0),   -- uno por tipo de contacto
        estado              nvarchar(20)   NOT NULL CONSTRAINT DF_INTERLOCUTOR_CONTACTO_estado DEFAULT ('Activo'),
        creado_por          nvarchar(100)  NOT NULL,
        creado_en           datetime2      NOT NULL CONSTRAINT DF_INTERLOCUTOR_CONTACTO_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por      nvarchar(100)  NULL,
        modificado_en       datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_CONTACTO PRIMARY KEY (id_contacto),
        CONSTRAINT CK_INTERLOCUTOR_CONTACTO_estado CHECK (estado IN ('Activo','Inactivo')),
        CONSTRAINT FK_INTERLOCUTOR_CONTACTO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INTERLOCUTOR_CONTACTO_tipo FOREIGN KEY (id_tipo_contacto) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_CONTACTO_principal' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_CONTACTO'))
    CREATE UNIQUE INDEX UX_INTERLOCUTOR_CONTACTO_principal
        ON cli.INTERLOCUTOR_CONTACTO (id_interlocutor, id_tipo_contacto) WHERE es_principal = 1 AND estado = 'Activo';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_CONTACTO_interlocutor' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_CONTACTO'))
    CREATE INDEX IX_INTERLOCUTOR_CONTACTO_interlocutor ON cli.INTERLOCUTOR_CONTACTO (id_interlocutor);
GO

/* ---------------------------------------------------------------------------
   3. Borradores de alta
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'cli.INTERLOCUTOR_BORRADOR', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_BORRADOR (
        id_borrador          bigint IDENTITY(1,1) NOT NULL,
        id_compania          bigint         NOT NULL,
        id_usuario           bigint         NOT NULL,   -- propietario: solo el ve y gestiona
        rol_destino          nvarchar(30)   NOT NULL CONSTRAINT DF_INTERLOCUTOR_BORRADOR_rol DEFAULT ('CLIENTE'),
        resumen_nombre       nvarchar(200)  NULL,       -- para listar sin abrir el contenido
        resumen_identificacion nvarchar(100) NULL,
        paso_actual          int            NOT NULL CONSTRAINT DF_INTERLOCUTOR_BORRADOR_paso DEFAULT (1),
        contenido            nvarchar(max)  NOT NULL,   -- JSON con todo lo capturado en el wizard
        creado_en            datetime2      NOT NULL CONSTRAINT DF_INTERLOCUTOR_BORRADOR_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_en        datetime2      NULL,
        vence_en             datetime2      NOT NULL,
        CONSTRAINT PK_INTERLOCUTOR_BORRADOR PRIMARY KEY (id_borrador),
        CONSTRAINT CK_INTERLOCUTOR_BORRADOR_json CHECK (ISJSON(contenido) = 1),
        CONSTRAINT FK_INTERLOCUTOR_BORRADOR_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_INTERLOCUTOR_BORRADOR_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_BORRADOR_usuario' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_BORRADOR'))
    CREATE INDEX IX_INTERLOCUTOR_BORRADOR_usuario ON cli.INTERLOCUTOR_BORRADOR (id_compania, id_usuario, vence_en);
GO

/* ---------------------------------------------------------------------------
   4. Migracion: la identificacion unica de cli.INTERLOCUTOR pasa a la tabla nueva.
      Dinamico (EXEC) para que el script siga corriendo despues de borrar las columnas.
   --------------------------------------------------------------------------- */
IF COL_LENGTH('cli.INTERLOCUTOR', 'numero_identificacion') IS NOT NULL
BEGIN
    EXEC(N'
        INSERT INTO cli.INTERLOCUTOR_IDENTIFICACION
            (id_interlocutor, id_compania, id_tipo_identificacion, numero_identificacion,
             es_principal, estado, creado_por, creado_en)
        SELECT i.id_interlocutor, i.id_compania, i.id_tipo_identificacion, i.numero_identificacion,
               1, ''Activo'', i.creado_por, i.creado_en
        FROM cli.INTERLOCUTOR i
        WHERE NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_IDENTIFICACION x WHERE x.id_interlocutor = i.id_interlocutor);
    ');

    IF EXISTS (SELECT 1 FROM sys.objects WHERE name = N'UQ_INTERLOCUTOR_identificacion' AND parent_object_id = OBJECT_ID(N'cli.INTERLOCUTOR'))
        ALTER TABLE cli.INTERLOCUTOR DROP CONSTRAINT UQ_INTERLOCUTOR_identificacion;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_LISTA_VALORES_ITEM' AND parent_object_id = OBJECT_ID(N'cli.INTERLOCUTOR'))
        ALTER TABLE cli.INTERLOCUTOR DROP CONSTRAINT FK_INTERLOCUTOR_LISTA_VALORES_ITEM;

    ALTER TABLE cli.INTERLOCUTOR DROP COLUMN id_tipo_identificacion;
    ALTER TABLE cli.INTERLOCUTOR DROP COLUMN numero_identificacion;
END
GO

/* ---------------------------------------------------------------------------
   5. Rol CLIENTE para los interlocutores existentes sin rol
   --------------------------------------------------------------------------- */
DECLARE @id_rol_cliente bigint = (
    SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i
    JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
    WHERE t.codigo = N'TIPO_ROL_INTERLOCUTOR' AND i.codigo = N'CLIENTE');

INSERT INTO cli.INTERLOCUTOR_ROL (id_interlocutor, id_tipo_rol, creado_por)
SELECT i.id_interlocutor, @id_rol_cliente, N'sistema'
FROM cli.INTERLOCUTOR i
WHERE @id_rol_cliente IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_ROL r WHERE r.id_interlocutor = i.id_interlocutor);
GO

/* ---------------------------------------------------------------------------
   6. Permisos de las listas nuevas (LOV_<codigo>) para ADMINISTRADOR_SEGURIDAD
   --------------------------------------------------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_' + t.codigo, N'Mantener lista de valores: ' + t.nombre, N'Transaccion',
       N'Crear, editar, activar/desactivar y borrar los valores de la lista ' + t.codigo + N'.', N'sistema'
FROM gen.LISTA_VALORES_TIPO t
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION oa WHERE oa.codigo = N'LOV_' + t.codigo);

DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo LIKE N'LOV[_]%'
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao
                       WHERE rao.id_rol_autorizacion = @id_rol AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT N'identificaciones' AS tabla, COUNT(*) AS filas FROM cli.INTERLOCUTOR_IDENTIFICACION
UNION ALL SELECT N'direcciones', COUNT(*) FROM cli.INTERLOCUTOR_DIRECCION
UNION ALL SELECT N'contactos', COUNT(*) FROM cli.INTERLOCUTOR_CONTACTO
UNION ALL SELECT N'borradores', COUNT(*) FROM cli.INTERLOCUTOR_BORRADOR
UNION ALL SELECT N'roles', COUNT(*) FROM cli.INTERLOCUTOR_ROL;

SELECT t.codigo AS lista, COUNT(i.id_lista_valores_item) AS valores
FROM gen.LISTA_VALORES_TIPO t
LEFT JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = t.id_lista_valores_tipo
WHERE t.codigo IN (N'INSTITUCION_EMISORA_ID', N'TIPO_DIRECCION', N'TIPO_CONTACTO', N'TIPO_ROL_INTERLOCUTOR', N'TIPO_IDENTIFICACION_FISICA')
GROUP BY t.codigo;
GO
