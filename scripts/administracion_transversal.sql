/* ============================================================================
   ADMINISTRACION TRANSVERSAL - re-ejecutable (sin USE: correr con CoreBancario_DEV seleccionada)
   Requiere: modelo_fisico_transversal_minimo.sql, listas_valores_admin.sql, interlocutor_multiples.sql,
             edicion_clientes_gestion.sql, seguridad_organizacion.sql

   Que hace:
     1. CANAL (lista CANAL): por donde nacio el cliente (Sucursal, Web, Movil, CRM, Onboarding digital, API).
        Es ORTOGONAL a la unidad organizativa: el cliente nace en la sucursal 100 POR el canal CRM.
        - cli.INTERLOCUTOR.id_canal_origen (se fija en el alta; luego solo cambia por gestion).
        - sec.USUARIO_INTEGRACION.id_canal (canal fijo de cada integracion; el servidor lo asigna en las altas por API).
     2. gen.MONEDA: catalogo de monedas (ISO 4217) y moneda base de la compania.
     3. gen.TIPO_CAMBIO: compra / venta / referencia por moneda y fecha (contra la moneda base). La carga es una
        GESTION (TC_CARGA) que se aplica al aprobarse.
     4. gen.FERIADO: calendario de feriados por pais.
     5. gen.PARAMETRO: parametros generales de la compania (se edita el valor; todo cambio se audita).
     6. Permisos (sec.OBJETO_AUTORIZACION) para cada opcion, asignados a ADMINISTRADOR_SEGURIDAD.
   Hacer respaldo antes: modifica cli.INTERLOCUTOR y sec.USUARIO_INTEGRACION.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Lista CANAL ---------------------------------------------------------- */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT N'CANAL', N'Canal', N'Canal por el que se origina un cliente, cuenta o credito (independiente de la unidad organizativa).', 0, N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'CANAL');

DECLARE @canales TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @canales VALUES (N'SUCURSAL', N'Sucursal', 1), (N'WEB', N'Web', 2), (N'MOVIL', N'Movil', 3),
                            (N'CRM', N'CRM', 4), (N'ONBOARDING_DIGITAL', N'Onboarding digital', 5), (N'API', N'API / Integracion', 6);

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, c.codigo, c.nombre, c.orden, 0, N'sistema'
FROM @canales c
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'CANAL'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = c.codigo);
GO

IF COL_LENGTH('cli.INTERLOCUTOR', 'id_canal_origen') IS NULL
    ALTER TABLE cli.INTERLOCUTOR ADD id_canal_origen bigint NULL;      -- LISTA_VALORES_ITEM (CANAL)
GO
IF COL_LENGTH('sec.USUARIO_INTEGRACION', 'id_canal') IS NULL
    ALTER TABLE sec.USUARIO_INTEGRACION ADD id_canal bigint NULL;      -- LISTA_VALORES_ITEM (CANAL)
GO

-- Clientes existentes: se asumen originados en Sucursal.
UPDATE i SET id_canal_origen = (SELECT TOP 1 x.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM x
                                 JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = x.id_lista_valores_tipo
                                 WHERE t.codigo = N'CANAL' AND x.codigo = N'SUCURSAL')
FROM cli.INTERLOCUTOR i WHERE i.id_canal_origen IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_CANAL_ORIGEN')
    ALTER TABLE cli.INTERLOCUTOR ADD CONSTRAINT FK_INTERLOCUTOR_CANAL_ORIGEN FOREIGN KEY (id_canal_origen) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_USUARIO_INTEGRACION_CANAL')
    ALTER TABLE sec.USUARIO_INTEGRACION ADD CONSTRAINT FK_USUARIO_INTEGRACION_CANAL FOREIGN KEY (id_canal) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_canal_origen' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR'))
    CREATE INDEX IX_INTERLOCUTOR_canal_origen ON cli.INTERLOCUTOR (id_compania, id_canal_origen);
GO

/* 2. Monedas -------------------------------------------------------------- */
IF OBJECT_ID(N'gen.MONEDA', N'U') IS NULL
CREATE TABLE gen.MONEDA (
    id_moneda       bigint IDENTITY(1,1) NOT NULL,
    id_compania     bigint        NOT NULL,
    codigo          nvarchar(3)   NOT NULL,                 -- ISO 4217 (CRC, USD, EUR...)
    nombre          nvarchar(100) NOT NULL,
    simbolo         nvarchar(5)   NOT NULL,
    decimales       tinyint       NOT NULL CONSTRAINT DF_MONEDA_decimales DEFAULT (2),
    es_base         bit           NOT NULL CONSTRAINT DF_MONEDA_es_base DEFAULT (0),
    estado          nvarchar(20)  NOT NULL CONSTRAINT DF_MONEDA_estado DEFAULT ('Activo'),
    creado_por      nvarchar(100) NOT NULL,
    creado_en       datetime2     NOT NULL CONSTRAINT DF_MONEDA_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por  nvarchar(100) NULL,
    modificado_en   datetime2     NULL,
    CONSTRAINT PK_MONEDA PRIMARY KEY (id_moneda),
    CONSTRAINT UQ_MONEDA_codigo UNIQUE (id_compania, codigo),
    CONSTRAINT CK_MONEDA_estado CHECK (estado IN ('Activo','Inactivo')),
    CONSTRAINT CK_MONEDA_decimales CHECK (decimales BETWEEN 0 AND 6),
    CONSTRAINT FK_MONEDA_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_MONEDA_base' AND object_id = OBJECT_ID(N'gen.MONEDA'))
    CREATE UNIQUE INDEX UX_MONEDA_base ON gen.MONEDA (id_compania) WHERE es_base = 1;   -- una sola moneda base por compania
GO

-- Semilla: colon costarricense (base) y dolar, solo si la compania aun no tiene monedas.
INSERT INTO gen.MONEDA (id_compania, codigo, nombre, simbolo, decimales, es_base, creado_por)
SELECT c.id_compania, v.codigo, v.nombre, v.simbolo, 2, v.es_base, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES (N'CRC', N'Colon costarricense', NCHAR(8353), 1), (N'USD', N'Dolar estadounidense', N'$', 0)) AS v(codigo, nombre, simbolo, es_base)
WHERE NOT EXISTS (SELECT 1 FROM gen.MONEDA m WHERE m.id_compania = c.id_compania);
GO

/* 3. Tipo de cambio ------------------------------------------------------- */
IF OBJECT_ID(N'gen.TIPO_CAMBIO', N'U') IS NULL
CREATE TABLE gen.TIPO_CAMBIO (
    id_tipo_cambio  bigint IDENTITY(1,1) NOT NULL,
    id_compania     bigint        NOT NULL,
    id_moneda       bigint        NOT NULL,                 -- moneda cotizada (contra la moneda base de la compania)
    fecha_vigencia  date          NOT NULL,                 -- rige desde esta fecha hasta la siguiente cargada
    compra          decimal(18,6) NOT NULL,
    venta           decimal(18,6) NOT NULL,
    referencia      decimal(18,6) NOT NULL,
    origen          nvarchar(30)  NOT NULL CONSTRAINT DF_TIPO_CAMBIO_origen DEFAULT ('Manual'),   -- Manual / (a futuro) BCCR...
    id_gestion      bigint        NULL,
    creado_por      nvarchar(100) NOT NULL,
    creado_en       datetime2     NOT NULL CONSTRAINT DF_TIPO_CAMBIO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por  nvarchar(100) NULL,
    modificado_en   datetime2     NULL,
    CONSTRAINT PK_TIPO_CAMBIO PRIMARY KEY (id_tipo_cambio),
    CONSTRAINT UQ_TIPO_CAMBIO_moneda_fecha UNIQUE (id_compania, id_moneda, fecha_vigencia),
    CONSTRAINT CK_TIPO_CAMBIO_valores CHECK (compra > 0 AND venta > 0 AND referencia > 0 AND compra <= venta),
    CONSTRAINT FK_TIPO_CAMBIO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_TIPO_CAMBIO_MONEDA FOREIGN KEY (id_moneda) REFERENCES gen.MONEDA (id_moneda),
    CONSTRAINT FK_TIPO_CAMBIO_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion)
);
GO

IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'TC_CARGA')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'TC_CARGA', N'Carga de tipo de cambio', N'Campos', 1, 1, N'Interna', N'sistema');
GO

/* 4. Feriados ------------------------------------------------------------- */
IF OBJECT_ID(N'gen.FERIADO', N'U') IS NULL
CREATE TABLE gen.FERIADO (
    id_feriado      bigint IDENTITY(1,1) NOT NULL,
    id_compania     bigint        NOT NULL,
    id_pais         bigint        NOT NULL,                 -- LISTA_VALORES_ITEM (PAIS)
    fecha           date          NOT NULL,
    nombre          nvarchar(150) NOT NULL,
    es_recurrente   bit           NOT NULL CONSTRAINT DF_FERIADO_recurrente DEFAULT (0),   -- se repite cada anio (mismo dia y mes) desde 'fecha'
    estado          nvarchar(20)  NOT NULL CONSTRAINT DF_FERIADO_estado DEFAULT ('Activo'),
    creado_por      nvarchar(100) NOT NULL,
    creado_en       datetime2     NOT NULL CONSTRAINT DF_FERIADO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por  nvarchar(100) NULL,
    modificado_en   datetime2     NULL,
    CONSTRAINT PK_FERIADO PRIMARY KEY (id_feriado),
    CONSTRAINT UQ_FERIADO_pais_fecha UNIQUE (id_compania, id_pais, fecha),
    CONSTRAINT CK_FERIADO_estado CHECK (estado IN ('Activo','Inactivo')),
    CONSTRAINT FK_FERIADO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_FERIADO_PAIS FOREIGN KEY (id_pais) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

/* 5. Parametros generales ------------------------------------------------- */
IF OBJECT_ID(N'gen.PARAMETRO', N'U') IS NULL
CREATE TABLE gen.PARAMETRO (
    id_parametro    bigint IDENTITY(1,1) NOT NULL,
    id_compania     bigint         NOT NULL,
    codigo          nvarchar(60)   NOT NULL,
    nombre          nvarchar(150)  NOT NULL,
    descripcion     nvarchar(500)  NULL,
    categoria       nvarchar(60)   NOT NULL,
    tipo_dato       nvarchar(20)   NOT NULL,                -- Texto / Entero / Decimal / Booleano
    valor           nvarchar(500)  NOT NULL,
    valor_defecto   nvarchar(500)  NOT NULL,
    creado_por      nvarchar(100)  NOT NULL,
    creado_en       datetime2      NOT NULL CONSTRAINT DF_PARAMETRO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por  nvarchar(100)  NULL,
    modificado_en   datetime2      NULL,
    CONSTRAINT PK_PARAMETRO PRIMARY KEY (id_parametro),
    CONSTRAINT UQ_PARAMETRO_codigo UNIQUE (id_compania, codigo),
    CONSTRAINT CK_PARAMETRO_tipo CHECK (tipo_dato IN ('Texto','Entero','Decimal','Booleano')),
    CONSTRAINT FK_PARAMETRO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
);
GO

INSERT INTO gen.PARAMETRO (id_compania, codigo, nombre, descripcion, categoria, tipo_dato, valor, valor_defecto, creado_por)
SELECT c.id_compania, v.codigo, v.nombre, v.descripcion, v.categoria, v.tipo, v.valor, v.valor, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES
    (N'DIAS_NO_HABILES', N'Dias no habiles de la semana',
        N'Dias de la semana que no son habiles, separados por coma: LUN,MAR,MIE,JUE,VIE,SAB,DOM. Se usa en el calendario.', N'Calendario', N'Texto', N'SAB,DOM'),
    (N'ZONA_HORARIA', N'Zona horaria de la compania',
        N'Zona horaria de referencia para fechas de negocio (identificador IANA).', N'General', N'Texto', N'America/Costa_Rica'),
    (N'TC_VARIACION_MAXIMA_PORC', N'Variacion maxima del tipo de cambio (%)',
        N'Una carga de tipo de cambio que varie mas que este porcentaje respecto a la vigente se rechaza al solicitarla. 0 = sin limite.', N'Tipo de cambio', N'Decimal', N'10')
) AS v(codigo, nombre, descripcion, categoria, tipo, valor)
WHERE NOT EXISTS (SELECT 1 FROM gen.PARAMETRO p WHERE p.id_compania = c.id_compania AND p.codigo = v.codigo);
GO

/* 6. Permisos ------------------------------------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT v.codigo, v.nombre, v.tipo, v.descripcion, N'sistema'
FROM (VALUES
    (N'MON_ADMINISTRACION', N'Administrar monedas', N'Pantalla', N'Crear y editar monedas y definir la moneda base.'),
    (N'TC_CONSULTA', N'Consultar tipos de cambio', N'Pantalla', N'Ver el historial de tipos de cambio.'),
    (N'TC_CARGA', N'Solicitar carga de tipo de cambio', N'Transaccion', N'Solicitar (gestion) la carga de compra/venta/referencia; se aplica al aprobarse.'),
    (N'CAL_ADMINISTRACION', N'Administrar calendario de feriados', N'Pantalla', N'Crear, editar e inactivar feriados por pais.'),
    (N'PAR_ADMINISTRACION', N'Administrar parametros generales', N'Pantalla', N'Cambiar el valor de los parametros generales de la compania.'),
    (N'AUD_CONSULTA', N'Consultar auditoria', N'Pantalla', N'Consultar la bitacora de cambios (quien, cuando, que cambio).'),
    (N'LOV_CANAL', N'Mantener lista de valores: Canal', N'Transaccion', N'Crear, editar, activar/desactivar y borrar los valores de la lista CANAL.'),
    (N'INT_ADMINISTRACION', N'Administrar usuarios de integracion', N'Pantalla', N'Ver los usuarios de integracion y asignarles su canal fijo.')
) AS v(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION oa WHERE oa.codigo = v.codigo);
GO

DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo IN (N'MON_ADMINISTRACION', N'TC_CONSULTA', N'TC_CARGA', N'CAL_ADMINISTRACION', N'PAR_ADMINISTRACION',
                        N'AUD_CONSULTA', N'LOV_CANAL', N'INT_ADMINISTRACION')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao
                       WHERE rao.id_rol_autorizacion = @id_rol AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

/* Verificacion */
SELECT t.codigo AS lista, i.codigo, i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE t.codigo = N'CANAL' ORDER BY i.orden;
SELECT id_compania, codigo, nombre, simbolo, es_base, estado FROM gen.MONEDA ORDER BY id_compania, es_base DESC, codigo;
SELECT id_compania, codigo, valor FROM gen.PARAMETRO ORDER BY id_compania, codigo;
SELECT codigo FROM sec.OBJETO_AUTORIZACION WHERE codigo IN (N'MON_ADMINISTRACION', N'TC_CONSULTA', N'TC_CARGA', N'CAL_ADMINISTRACION', N'PAR_ADMINISTRACION', N'AUD_CONSULTA', N'LOV_CANAL', N'INT_ADMINISTRACION');
SELECT COUNT(*) AS clientes_sin_canal FROM cli.INTERLOCUTOR WHERE id_canal_origen IS NULL;
GO
