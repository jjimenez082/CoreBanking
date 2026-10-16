/* ============================================================================
   r44a - Gestor documental, base (esquema doc).
   - Carpetas y tipos de documento (con reglas: cardinalidad, vencimiento, confidencialidad, aprobacion).
   - Documento / version / palabras clave. Evidencia de gestion (origen 'Gestion') y expediente vivo ('Expediente').
   - Parametros generales del almacen y permisos.
   Re-ejecutable.
   ============================================================================ */

IF SCHEMA_ID('doc') IS NULL EXEC('CREATE SCHEMA doc AUTHORIZATION dbo');
GO

/* 1. Carpetas (primer nivel del arbol del cliente) ----------------------------- */
IF OBJECT_ID(N'doc.CARPETA', N'U') IS NULL
CREATE TABLE doc.CARPETA (
    id_carpeta       bigint IDENTITY(1,1) NOT NULL,
    id_compania      bigint         NOT NULL,
    codigo           nvarchar(40)   NOT NULL,
    nombre           nvarchar(100)  NOT NULL,
    descripcion      nvarchar(300)  NULL,
    orden            int            NOT NULL CONSTRAINT DF_DOC_CARPETA_orden DEFAULT (100),
    es_sistema       bit            NOT NULL CONSTRAINT DF_DOC_CARPETA_sistema DEFAULT (0),   -- 1 = la gestiona el sistema (Gestiones)
    aplica_fisica    bit            NOT NULL CONSTRAINT DF_DOC_CARPETA_fis DEFAULT (1),
    aplica_juridica  bit            NOT NULL CONSTRAINT DF_DOC_CARPETA_jur DEFAULT (1),
    aplica_grupo     bit            NOT NULL CONSTRAINT DF_DOC_CARPETA_grp DEFAULT (0),
    estado           nvarchar(10)   NOT NULL CONSTRAINT DF_DOC_CARPETA_estado DEFAULT ('Activo'),
    creado_por       nvarchar(100)  NOT NULL,
    creado_en        datetime2      NOT NULL CONSTRAINT DF_DOC_CARPETA_creado DEFAULT (SYSUTCDATETIME()),
    modificado_por   nvarchar(100)  NULL,
    modificado_en    datetime2      NULL,
    CONSTRAINT PK_DOC_CARPETA PRIMARY KEY (id_carpeta),
    CONSTRAINT UQ_DOC_CARPETA_codigo UNIQUE (id_compania, codigo),
    CONSTRAINT CK_DOC_CARPETA_estado CHECK (estado IN ('Activo','Inactivo')),
    CONSTRAINT FK_DOC_CARPETA_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
);
GO

/* 2. Tipos de documento (segundo nivel) con sus reglas -------------------------- */
IF OBJECT_ID(N'doc.TIPO_DOCUMENTO', N'U') IS NULL
CREATE TABLE doc.TIPO_DOCUMENTO (
    id_tipo_documento  bigint IDENTITY(1,1) NOT NULL,
    id_compania        bigint         NOT NULL,
    id_carpeta         bigint         NOT NULL,
    codigo             nvarchar(40)   NOT NULL,
    nombre             nvarchar(120)  NOT NULL,
    descripcion        nvarchar(300)  NULL,
    orden              int            NOT NULL CONSTRAINT DF_DOC_TIPO_orden DEFAULT (100),
    cardinalidad       nvarchar(10)   NOT NULL CONSTRAINT DF_DOC_TIPO_card DEFAULT ('Multiple'),   -- Unico (un solo vigente) / Multiple
    maximo             int            NULL,                                                         -- tope opcional para 'Multiple'
    vencimiento        nvarchar(12)   NOT NULL CONSTRAINT DF_DOC_TIPO_venc DEFAULT ('NoAplica'),    -- Obligatorio / Opcional / NoAplica
    dias_aviso         int            NOT NULL CONSTRAINT DF_DOC_TIPO_aviso DEFAULT (30),
    confidencialidad   nvarchar(12)   NOT NULL CONSTRAINT DF_DOC_TIPO_conf DEFAULT ('Normal'),      -- Normal / Restringida
    requiere_aprobacion bit           NOT NULL CONSTRAINT DF_DOC_TIPO_apr DEFAULT (0),
    aplica_fisica      bit            NOT NULL CONSTRAINT DF_DOC_TIPO_fis DEFAULT (1),
    aplica_juridica    bit            NOT NULL CONSTRAINT DF_DOC_TIPO_jur DEFAULT (1),
    aplica_grupo       bit            NOT NULL CONSTRAINT DF_DOC_TIPO_grp DEFAULT (0),
    estado             nvarchar(10)   NOT NULL CONSTRAINT DF_DOC_TIPO_estado DEFAULT ('Activo'),
    creado_por         nvarchar(100)  NOT NULL,
    creado_en          datetime2      NOT NULL CONSTRAINT DF_DOC_TIPO_creado DEFAULT (SYSUTCDATETIME()),
    modificado_por     nvarchar(100)  NULL,
    modificado_en      datetime2      NULL,
    CONSTRAINT PK_DOC_TIPO PRIMARY KEY (id_tipo_documento),
    CONSTRAINT UQ_DOC_TIPO_codigo UNIQUE (id_compania, codigo),
    CONSTRAINT CK_DOC_TIPO_card CHECK (cardinalidad IN ('Unico','Multiple')),
    CONSTRAINT CK_DOC_TIPO_venc CHECK (vencimiento IN ('Obligatorio','Opcional','NoAplica')),
    CONSTRAINT CK_DOC_TIPO_conf CHECK (confidencialidad IN ('Normal','Restringida')),
    CONSTRAINT CK_DOC_TIPO_estado CHECK (estado IN ('Activo','Inactivo')),
    CONSTRAINT CK_DOC_TIPO_maximo CHECK (maximo IS NULL OR (maximo >= 1 AND cardinalidad = 'Multiple')),
    CONSTRAINT CK_DOC_TIPO_aviso CHECK (dias_aviso >= 0),
    CONSTRAINT FK_DOC_TIPO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_DOC_TIPO_CARPETA FOREIGN KEY (id_carpeta) REFERENCES doc.CARPETA (id_carpeta)
);
GO

/* 3. Documento (lo que no cambia al reemplazar) --------------------------------- */
IF OBJECT_ID(N'doc.DOCUMENTO', N'U') IS NULL
CREATE TABLE doc.DOCUMENTO (
    id_documento       bigint IDENTITY(1,1) NOT NULL,
    id_compania        bigint         NOT NULL,
    id_interlocutor    bigint         NULL,        -- NULL mientras es temporal o de un alta aun no aplicada
    id_tipo_documento  bigint         NOT NULL,
    descripcion        nvarchar(300)  NULL,
    origen             nvarchar(12)   NOT NULL,    -- Gestion (evidencia, inmutable) / Expediente (gestor documental)
    id_gestion         bigint         NULL,
    id_interlocutor_origen bigint     NULL,        -- cliente del que viene el documento cuando se traslado por una fusion
    estado             nvarchar(12)   NOT NULL,    -- Temporal / EnGestion / Descartado / Vigente / Obsoleto
    motivo_obsoleto    nvarchar(300)  NULL,
    creado_por         nvarchar(100)  NOT NULL,
    id_usuario_creador bigint         NULL,
    creado_en          datetime2      NOT NULL CONSTRAINT DF_DOCUMENTO_creado DEFAULT (SYSUTCDATETIME()),
    modificado_por     nvarchar(100)  NULL,
    modificado_en      datetime2      NULL,
    CONSTRAINT PK_DOCUMENTO PRIMARY KEY (id_documento),
    CONSTRAINT CK_DOCUMENTO_origen CHECK (origen IN ('Gestion','Expediente')),
    CONSTRAINT CK_DOCUMENTO_estado CHECK (estado IN ('Temporal','EnGestion','Descartado','Vigente','Obsoleto')),
    CONSTRAINT FK_DOCUMENTO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_DOCUMENTO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_DOCUMENTO_TIPO FOREIGN KEY (id_tipo_documento) REFERENCES doc.TIPO_DOCUMENTO (id_tipo_documento),
    CONSTRAINT FK_DOCUMENTO_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DOCUMENTO_interlocutor' AND object_id = OBJECT_ID(N'doc.DOCUMENTO'))
    CREATE INDEX IX_DOCUMENTO_interlocutor ON doc.DOCUMENTO (id_interlocutor, estado) INCLUDE (id_tipo_documento, id_gestion);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DOCUMENTO_gestion' AND object_id = OBJECT_ID(N'doc.DOCUMENTO'))
    CREATE INDEX IX_DOCUMENTO_gestion ON doc.DOCUMENTO (id_gestion) WHERE id_gestion IS NOT NULL;
GO

/* 4. Version (archivo, fechas y huella) ------------------------------------------ */
IF OBJECT_ID(N'doc.DOCUMENTO_VERSION', N'U') IS NULL
CREATE TABLE doc.DOCUMENTO_VERSION (
    id_version         bigint IDENTITY(1,1) NOT NULL,
    id_documento       bigint         NOT NULL,
    numero             int            NOT NULL,
    es_vigente         bit            NOT NULL CONSTRAINT DF_DOCVER_vigente DEFAULT (1),
    nombre_archivo     nvarchar(260)  NOT NULL,
    tipo_mime          nvarchar(100)  NOT NULL,
    tamano_bytes       bigint         NOT NULL,
    sha256             char(64)       NOT NULL,       -- huella del contenido original (sin cifrar)
    ruta_almacen       nvarchar(400)  NOT NULL,       -- llave opaca dentro del almacen (no es una ruta del usuario)
    id_llave           nvarchar(40)   NOT NULL,       -- identificador de la llave de cifrado (permite rotarla)
    fecha_emision      date           NULL,
    fecha_vencimiento  date           NULL,
    motivo_reemplazo   nvarchar(300)  NULL,
    subido_por         nvarchar(100)  NOT NULL,
    subido_en          datetime2      NOT NULL CONSTRAINT DF_DOCVER_subido DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_DOCUMENTO_VERSION PRIMARY KEY (id_version),
    CONSTRAINT UQ_DOCVER_numero UNIQUE (id_documento, numero),
    CONSTRAINT CK_DOCVER_fechas CHECK (fecha_emision IS NULL OR fecha_vencimiento IS NULL OR fecha_vencimiento >= fecha_emision),
    CONSTRAINT FK_DOCVER_DOCUMENTO FOREIGN KEY (id_documento) REFERENCES doc.DOCUMENTO (id_documento)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_DOCVER_vigente' AND object_id = OBJECT_ID(N'doc.DOCUMENTO_VERSION'))
    CREATE UNIQUE INDEX UX_DOCVER_vigente ON doc.DOCUMENTO_VERSION (id_documento) WHERE es_vigente = 1;
GO

/* 5. Palabras clave (normalizadas para buscar) ------------------------------------ */
IF OBJECT_ID(N'doc.DOCUMENTO_CLAVE', N'U') IS NULL
CREATE TABLE doc.DOCUMENTO_CLAVE (
    id_documento  bigint       NOT NULL,
    clave         nvarchar(60) NOT NULL,
    CONSTRAINT PK_DOCUMENTO_CLAVE PRIMARY KEY (id_documento, clave),
    CONSTRAINT FK_DOCCLAVE_DOCUMENTO FOREIGN KEY (id_documento) REFERENCES doc.DOCUMENTO (id_documento)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DOCUMENTO_CLAVE_clave' AND object_id = OBJECT_ID(N'doc.DOCUMENTO_CLAVE'))
    CREATE INDEX IX_DOCUMENTO_CLAVE_clave ON doc.DOCUMENTO_CLAVE (clave);
GO

/* 6. Carpetas por compania ---------------------------------------------------------- */
INSERT INTO doc.CARPETA (id_compania, codigo, nombre, descripcion, orden, es_sistema, aplica_fisica, aplica_juridica, aplica_grupo, creado_por)
SELECT c.id_compania, v.codigo, v.nombre, v.descripcion, v.orden, v.sistema, v.fis, v.jur, v.grp, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES
    (N'INFO_CLIENTE',  N'Información del cliente',  N'Ficha y datos generales del cliente.',                         10, 0, 1, 1, 0),
    (N'IDENTIFICACION',N'Identificación',           N'Documentos de identidad.',                                      20, 0, 1, 1, 0),
    (N'KYC_AML',       N'KYC / AML',                N'Debida diligencia, origen de fondos, PEP y screening.',         30, 0, 1, 1, 0),
    (N'GESTIONES',     N'Gestiones',                N'Evidencia de las gestiones aprobadas (la crea el sistema).',    40, 1, 1, 1, 1),
    (N'SOLICITUDES',   N'Solicitudes',              N'Solicitudes de productos y créditos.',                          50, 0, 1, 1, 0),
    (N'PRODUCTOS',     N'Productos',                N'Documentos por cuenta o crédito.',                              60, 0, 1, 1, 0),
    (N'CONTRATOS',     N'Contratos',                N'Contratos, pagarés, garantías y adendas.',                      70, 0, 1, 1, 0),
    (N'CORRESPONDENCIA',N'Correspondencia',         N'Cartas, notificaciones y oficios.',                             80, 0, 1, 1, 1),
    (N'INFO_FINANCIERA',N'Información financiera',  N'Estados financieros, constancias de ingresos y declaraciones.', 90, 0, 1, 1, 0),
    (N'INFO_LEGAL',    N'Información legal',        N'Documentos legales y de representación.',                      100, 0, 1, 1, 1),
    (N'OTROS',         N'Otros',                    N'Documentos sin categoría.',                                    110, 0, 1, 1, 1)
) AS v(codigo, nombre, descripcion, orden, sistema, fis, jur, grp)
WHERE NOT EXISTS (SELECT 1 FROM doc.CARPETA x WHERE x.id_compania = c.id_compania AND x.codigo = v.codigo);
GO

/* 7. Tipos de documento por compania -------------------------------------------------
   cardinalidad: U = Unico, M = Multiple.  vencimiento: O = Obligatorio, P = Opcional, N = No aplica.
   confidencialidad: R = Restringida, N = Normal.  aplica: fisica / juridica / grupo. */
INSERT INTO doc.TIPO_DOCUMENTO (id_compania, id_carpeta, codigo, nombre, descripcion, orden, cardinalidad, vencimiento, dias_aviso, confidencialidad, aplica_fisica, aplica_juridica, aplica_grupo, creado_por)
SELECT c.id_compania, ca.id_carpeta, v.codigo, v.nombre, v.descripcion, v.orden,
       CASE v.card WHEN 'U' THEN 'Unico' ELSE 'Multiple' END,
       CASE v.venc WHEN 'O' THEN 'Obligatorio' WHEN 'P' THEN 'Opcional' ELSE 'NoAplica' END,
       30, CASE v.conf WHEN 'R' THEN 'Restringida' ELSE 'Normal' END, v.fis, v.jur, v.grp, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES
    (N'INFO_CLIENTE',  N'FICHA_VINCULACION',  N'Ficha de vinculación',               NULL, 10, 'U','N','N',1,1,0),
    (N'INFO_CLIENTE',  N'FOTOGRAFIA',         N'Fotografía',                         NULL, 20, 'U','N','N',1,0,0),
    (N'IDENTIFICACION',N'CEDULA_IDENTIDAD',   N'Cédula de identidad',                NULL, 10, 'U','O','N',1,0,0),
    (N'IDENTIFICACION',N'PASAPORTE',          N'Pasaporte',                          NULL, 20, 'U','O','N',1,0,0),
    (N'IDENTIFICACION',N'DIMEX',              N'DIMEX',                              NULL, 30, 'U','O','N',1,0,0),
    (N'IDENTIFICACION',N'LICENCIA_CONDUCIR',  N'Licencia de conducir',               NULL, 40, 'U','P','N',1,0,0),
    (N'IDENTIFICACION',N'CEDULA_JURIDICA',    N'Cédula jurídica',                    NULL, 50, 'U','P','N',0,1,0),
    (N'IDENTIFICACION',N'ID_REPRESENTANTE',   N'Identificación del representante',   NULL, 60, 'M','O','N',0,1,0),
    (N'KYC_AML',       N'ORIGEN_FONDOS',      N'Declaración de origen de fondos',    NULL, 10, 'U','P','R',1,1,0),
    (N'KYC_AML',       N'DECLARACION_PEP',    N'Declaración de persona expuesta (PEP)', NULL, 20, 'U','P','R',1,1,0),
    (N'KYC_AML',       N'RESULTADO_SCREENING',N'Resultado de screening',             NULL, 30, 'M','N','R',1,1,0),
    (N'KYC_AML',       N'DEBIDA_DILIGENCIA',  N'Informe de debida diligencia',       NULL, 40, 'M','N','R',1,1,0),
    (N'GESTIONES',     N'EVIDENCIA_GESTION',  N'Evidencia de gestión',               N'Soporte general de una gestión.', 10, 'M','N','N',1,1,1),
    (N'SOLICITUDES',   N'SOLICITUD_CREDITO',  N'Solicitud de crédito',               NULL, 10, 'M','N','N',1,1,0),
    (N'SOLICITUDES',   N'SOLICITUD_APERTURA', N'Solicitud de apertura de cuenta',    NULL, 20, 'M','N','N',1,1,0),
    (N'PRODUCTOS',     N'DOC_PRODUCTO',       N'Documento de producto',              NULL, 10, 'M','N','N',1,1,0),
    (N'CONTRATOS',     N'CONTRATO',           N'Contrato firmado',                   NULL, 10, 'M','N','N',1,1,0),
    (N'CONTRATOS',     N'PAGARE',             N'Pagaré',                             NULL, 20, 'M','N','N',1,1,0),
    (N'CONTRATOS',     N'GARANTIA',           N'Garantía',                           NULL, 30, 'M','N','N',1,1,0),
    (N'CONTRATOS',     N'ADENDA',             N'Adenda',                             NULL, 40, 'M','N','N',1,1,0),
    (N'CORRESPONDENCIA',N'CARTA',             N'Carta',                              NULL, 10, 'M','N','N',1,1,1),
    (N'CORRESPONDENCIA',N'NOTIFICACION',      N'Notificación',                       NULL, 20, 'M','N','N',1,1,1),
    (N'CORRESPONDENCIA',N'OFICIO',            N'Oficio',                             NULL, 30, 'M','N','N',1,1,1),
    (N'INFO_FINANCIERA',N'ESTADOS_FINANCIEROS',N'Estados financieros',               NULL, 10, 'M','N','N',0,1,0),
    (N'INFO_FINANCIERA',N'CONSTANCIA_INGRESOS',N'Constancia de ingresos',            NULL, 20, 'U','P','N',1,0,0),
    (N'INFO_FINANCIERA',N'DECLARACION_RENTA', N'Declaración de renta',               NULL, 30, 'M','N','N',1,1,0),
    (N'INFO_LEGAL',    N'ESCRITURA',          N'Escritura de constitución',          NULL, 10, 'U','N','N',0,1,0),
    (N'INFO_LEGAL',    N'PERSONERIA',         N'Personería jurídica',                NULL, 20, 'U','P','N',0,1,0),
    (N'INFO_LEGAL',    N'PODER',              N'Poder',                              NULL, 30, 'M','P','N',1,1,0),
    (N'INFO_LEGAL',    N'ACTA_DEFUNCION',     N'Acta de defunción',                  NULL, 40, 'U','N','R',1,0,0),
    (N'INFO_LEGAL',    N'ACTA_GRUPO',         N'Acta o acuerdo del grupo',           NULL, 50, 'M','N','N',0,0,1),
    (N'OTROS',         N'OTRO',               N'Otro documento',                     NULL, 10, 'M','N','N',1,1,1)
) AS v(carpeta, codigo, nombre, descripcion, orden, card, venc, conf, fis, jur, grp)
JOIN doc.CARPETA ca ON ca.id_compania = c.id_compania AND ca.codigo = v.carpeta
WHERE NOT EXISTS (SELECT 1 FROM doc.TIPO_DOCUMENTO x WHERE x.id_compania = c.id_compania AND x.codigo = v.codigo);
GO

/* 8. Parametros generales del almacen ------------------------------------------------ */
INSERT INTO gen.PARAMETRO (id_compania, codigo, nombre, descripcion, categoria, tipo_dato, valor, valor_defecto, creado_por)
SELECT c.id_compania, v.codigo, v.nombre, v.descripcion, N'Documentos', v.tipo, v.valor, v.valor, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES
    (N'DOC_DIRECTORIO', N'Directorio del almacén de documentos',
        N'Carpeta del servidor donde se guardan los archivos cifrados. Puede ser relativa a la aplicación. Cambiarla no mueve los archivos ya guardados.', N'Texto', N'almacen_documentos'),
    (N'DOC_TAMANO_MAX_MB', N'Tamaño máximo por archivo (MB)', N'Un archivo más grande se rechaza al subirlo.', N'Entero', N'10'),
    (N'DOC_EXTENSIONES', N'Extensiones permitidas', N'Extensiones de archivo aceptadas, separadas por coma y sin punto.', N'Texto', N'pdf,jpg,jpeg,png'),
    (N'DOC_MAX_ARCHIVOS_GESTION', N'Máximo de archivos por gestión', N'Cantidad máxima de adjuntos que puede llevar una misma gestión.', N'Entero', N'10')
) AS v(codigo, nombre, descripcion, tipo, valor)
WHERE NOT EXISTS (SELECT 1 FROM gen.PARAMETRO p WHERE p.id_compania = c.id_compania AND p.codigo = v.codigo);
GO

/* 9. Permisos -------------------------------------------------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, x.tipo, x.descripcion, N'sistema' FROM (VALUES
  (N'DOC_VER',             N'Ver documentos del cliente',            N'API', N'Ver el árbol de documentos de un cliente y descargar los de confidencialidad normal.'),
  (N'DOC_VER_RESTRINGIDO', N'Ver documentos restringidos',           N'API', N'Ver y descargar los documentos de tipos con confidencialidad restringida.'),
  (N'DOC_ADJUNTAR',        N'Adjuntar documentos a una gestión',     N'API', N'Subir los archivos que respaldan una gestión (evidencia).')
) AS x(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);
GO

/* DOC_VER: roles que ya consultan clientes. DOC_VER_RESTRINGIDO: roles que ya ven KYC. DOC_ADJUNTAR: roles que ya solicitan cambios. */
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT DISTINCT rao.id_rol_autorizacion, nuevo.id_objeto_autorizacion
FROM sec.ROL_AUTORIZACION_OBJETO rao
JOIN sec.OBJETO_AUTORIZACION actual ON actual.id_objeto_autorizacion = rao.id_objeto_autorizacion
JOIN sec.OBJETO_AUTORIZACION nuevo ON nuevo.codigo =
     CASE WHEN actual.codigo = N'CLI_CONSULTA' THEN N'DOC_VER'
          WHEN actual.codigo = N'CLI_KYC_VER' THEN N'DOC_VER_RESTRINGIDO'
          ELSE N'DOC_ADJUNTAR' END
WHERE actual.codigo IN (N'CLI_CONSULTA', N'CLI_KYC_VER', N'CLI_ALTA', N'CLI_EDICION', N'CLI_MODIFICACION_DATOS', N'CLI_KYC', N'CLI_FUSION',
                        N'CLI_BLOQUEO', N'CLI_DESBLOQUEO', N'CLI_CONSENTIMIENTO', N'CLI_ROL', N'CLI_RELACION')
  AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x
                   WHERE x.id_rol_autorizacion = rao.id_rol_autorizacion AND x.id_objeto_autorizacion = nuevo.id_objeto_autorizacion);
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'DOC_VER', N'DOC_VER_RESTRINGIDO', N'DOC_ADJUNTAR')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

/* Verificacion */
SELECT (SELECT COUNT(*) FROM doc.CARPETA) AS carpetas, (SELECT COUNT(*) FROM doc.TIPO_DOCUMENTO) AS tipos;
SELECT codigo FROM sec.OBJETO_AUTORIZACION WHERE codigo LIKE N'DOC[_]%';
SELECT codigo, valor FROM gen.PARAMETRO WHERE codigo LIKE N'DOC[_]%' ORDER BY id_compania, codigo;
GO
