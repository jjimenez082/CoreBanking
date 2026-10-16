/* ============================================================================
   Ronda 43 - Motor de reglas (GEN-008) - Modelo fisico
   Esquema nuevo: rul.  Re-ejecutable (idempotente).
   Requiere: Ronda 42 (prd.PRODUCTO_COMERCIAL), apr.GESTION, gen.COMPANIA, sec.*
   Alcance: contextos, atributos, operadores, funciones, condiciones, acciones,
            conjuntos de reglas versionados, evaluaciones, casos de prueba.
   Fuera de alcance: ejecucion de efectos (los hace el dominio que invoca),
            scripting, notificaciones, migracion de apr.REGLA_APROBACION.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'prd.PRODUCTO_COMERCIAL', N'U') IS NULL
    THROW 50430, N'Falta la Ronda 42 (prd.PRODUCTO_COMERCIAL). Ejecutela primero.', 1;
IF OBJECT_ID(N'apr.GESTION', N'U') IS NULL
    THROW 50431, N'Falta apr.GESTION.', 1;
GO

IF SCHEMA_ID(N'rul') IS NULL EXEC(N'CREATE SCHEMA rul');
GO

/* ---------------------------------------------------------------------------
   1. Catalogos del sistema: contexto, atributo, operador, funcion
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'rul.CONTEXTO', N'U') IS NULL
BEGIN
    CREATE TABLE rul.CONTEXTO (
        id_contexto   bigint IDENTITY(1,1) NOT NULL,
        codigo        nvarchar(30)  NOT NULL,
        nombre        nvarchar(100) NOT NULL,
        descripcion   nvarchar(500) NULL,
        estado        nvarchar(15)  NOT NULL CONSTRAINT DF_RULCTX_estado DEFAULT ('Reservado'),  -- Reservado = el dominio aun no existe
        creado_por    nvarchar(100) NOT NULL,
        creado_en     datetime2     NOT NULL CONSTRAINT DF_RULCTX_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por nvarchar(100) NULL,
        modificado_en datetime2     NULL,
        CONSTRAINT PK_RUL_CONTEXTO PRIMARY KEY (id_contexto),
        CONSTRAINT UQ_RUL_CONTEXTO_codigo UNIQUE (codigo),
        CONSTRAINT CK_RULCTX_estado CHECK (estado IN ('Activo', 'Reservado', 'Inactivo'))
    );
END
GO

IF OBJECT_ID(N'rul.ATRIBUTO', N'U') IS NULL
BEGIN
    CREATE TABLE rul.ATRIBUTO (
        id_atributo   bigint IDENTITY(1,1) NOT NULL,
        codigo        nvarchar(100) NOT NULL,           -- con puntos: cliente.edad
        nombre        nvarchar(150) NOT NULL,
        descripcion   nvarchar(500) NULL,
        tipo_dato     nvarchar(15)  NOT NULL,           -- Texto, Numero, Fecha, Booleano, Lista
        lista_codigo  nvarchar(100) NULL,               -- codigo de gen.LISTA_VALORES_TIPO cuando tipo_dato = Lista
        es_multivalor bit           NOT NULL CONSTRAINT DF_RULATR_multi DEFAULT (0),
        es_sistema    bit           NOT NULL CONSTRAINT DF_RULATR_sistema DEFAULT (1),
        estado        nvarchar(15)  NOT NULL CONSTRAINT DF_RULATR_estado DEFAULT ('Activo'),
        creado_por    nvarchar(100) NOT NULL,
        creado_en     datetime2     NOT NULL CONSTRAINT DF_RULATR_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por nvarchar(100) NULL,
        modificado_en datetime2     NULL,
        CONSTRAINT PK_RUL_ATRIBUTO PRIMARY KEY (id_atributo),
        CONSTRAINT UQ_RUL_ATRIBUTO_codigo UNIQUE (codigo),
        CONSTRAINT CK_RULATR_tipo CHECK (tipo_dato IN ('Texto', 'Numero', 'Fecha', 'Booleano', 'Lista')),
        CONSTRAINT CK_RULATR_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_RULATR_lista CHECK (tipo_dato = 'Lista' OR lista_codigo IS NULL)
    );
END
GO

IF OBJECT_ID(N'rul.CONTEXTO_ATRIBUTO', N'U') IS NULL
BEGIN
    CREATE TABLE rul.CONTEXTO_ATRIBUTO (
        id_contexto bigint NOT NULL,
        id_atributo bigint NOT NULL,
        CONSTRAINT PK_RUL_CONTEXTO_ATRIBUTO PRIMARY KEY (id_contexto, id_atributo),
        CONSTRAINT FK_RULCA_CONTEXTO FOREIGN KEY (id_contexto) REFERENCES rul.CONTEXTO (id_contexto),
        CONSTRAINT FK_RULCA_ATRIBUTO FOREIGN KEY (id_atributo) REFERENCES rul.ATRIBUTO (id_atributo)
    );
END
GO

IF OBJECT_ID(N'rul.OPERADOR', N'U') IS NULL
BEGIN
    CREATE TABLE rul.OPERADOR (
        id_operador  bigint IDENTITY(1,1) NOT NULL,
        codigo       nvarchar(30)  NOT NULL,
        nombre       nvarchar(100) NOT NULL,
        simbolo      nvarchar(10)  NULL,
        aridad       tinyint       NOT NULL,            -- 0 sin valor (ES_VACIO), 1 un valor, 2 dos valores (ENTRE), 9 lista de valores (EN)
        tipos_dato   nvarchar(100) NOT NULL,            -- tipos aplicables separados por coma
        orden        int           NOT NULL CONSTRAINT DF_RULOPE_orden DEFAULT (0),
        creado_por   nvarchar(100) NOT NULL,
        creado_en    datetime2     NOT NULL CONSTRAINT DF_RULOPE_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RUL_OPERADOR PRIMARY KEY (id_operador),
        CONSTRAINT UQ_RUL_OPERADOR_codigo UNIQUE (codigo),
        CONSTRAINT CK_RULOPE_aridad CHECK (aridad IN (0, 1, 2, 9))
    );
END
GO

IF OBJECT_ID(N'rul.FUNCION', N'U') IS NULL
BEGIN
    CREATE TABLE rul.FUNCION (
        id_funcion   bigint IDENTITY(1,1) NOT NULL,
        codigo       nvarchar(50)  NOT NULL,
        nombre       nvarchar(100) NOT NULL,
        descripcion  nvarchar(500) NULL,
        tipo_entrada nvarchar(15)  NOT NULL,            -- tipo del atributo al que se aplica
        tipo_retorno nvarchar(15)  NOT NULL,
        cantidad_parametros tinyint NOT NULL CONSTRAINT DF_RULFUN_param DEFAULT (0),
        estado       nvarchar(15)  NOT NULL CONSTRAINT DF_RULFUN_estado DEFAULT ('Activo'),
        creado_por   nvarchar(100) NOT NULL,
        creado_en    datetime2     NOT NULL CONSTRAINT DF_RULFUN_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RUL_FUNCION PRIMARY KEY (id_funcion),
        CONSTRAINT UQ_RUL_FUNCION_codigo UNIQUE (codigo),
        CONSTRAINT CK_RULFUN_estado CHECK (estado IN ('Activo', 'Inactivo'))
    );
END
GO

/* ---------------------------------------------------------------------------
   2. Pool de condiciones y de acciones (por compania, reutilizables)
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'rul.CONDICION', N'U') IS NULL
BEGIN
    CREATE TABLE rul.CONDICION (
        id_condicion        bigint IDENTITY(1,1) NOT NULL,
        id_compania         bigint        NOT NULL,
        codigo              nvarchar(50)  NOT NULL,
        nombre              nvarchar(150) NOT NULL,
        descripcion         nvarchar(500) NULL,
        id_atributo         bigint        NOT NULL,
        id_funcion          bigint        NULL,          -- opcional: se aplica al atributo antes de comparar
        parametro_funcion   nvarchar(50)  NULL,
        id_operador         bigint        NOT NULL,
        origen_valor        nvarchar(10)  NOT NULL CONSTRAINT DF_RULCON_origen DEFAULT ('Valor'),  -- Valor | Atributo
        valor_1             nvarchar(200) NULL,
        valor_2             nvarchar(200) NULL,          -- segundo extremo de ENTRE
        valores_lista       nvarchar(1000) NULL,         -- EN / NO_EN, separados por |
        id_atributo_valor   bigint        NULL,          -- cuando origen_valor = Atributo
        estado              nvarchar(15)  NOT NULL CONSTRAINT DF_RULCON_estado DEFAULT ('Activo'),
        creado_por          nvarchar(100) NOT NULL,
        creado_en           datetime2     NOT NULL CONSTRAINT DF_RULCON_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por      nvarchar(100) NULL,
        modificado_en       datetime2     NULL,
        CONSTRAINT PK_RUL_CONDICION PRIMARY KEY (id_condicion),
        CONSTRAINT UQ_RUL_CONDICION_codigo UNIQUE (id_compania, codigo),
        CONSTRAINT CK_RULCON_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_RULCON_origen CHECK (origen_valor IN ('Valor', 'Atributo')),
        CONSTRAINT CK_RULCON_origen_attr CHECK ((origen_valor = 'Atributo' AND id_atributo_valor IS NOT NULL) OR (origen_valor = 'Valor' AND id_atributo_valor IS NULL)),
        CONSTRAINT FK_RULCON_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_RULCON_ATRIBUTO FOREIGN KEY (id_atributo) REFERENCES rul.ATRIBUTO (id_atributo),
        CONSTRAINT FK_RULCON_FUNCION FOREIGN KEY (id_funcion) REFERENCES rul.FUNCION (id_funcion),
        CONSTRAINT FK_RULCON_OPERADOR FOREIGN KEY (id_operador) REFERENCES rul.OPERADOR (id_operador),
        CONSTRAINT FK_RULCON_ATRIBUTO_VALOR FOREIGN KEY (id_atributo_valor) REFERENCES rul.ATRIBUTO (id_atributo)
    );
END
GO

IF OBJECT_ID(N'rul.ACCION', N'U') IS NULL
BEGIN
    CREATE TABLE rul.ACCION (
        id_accion    bigint IDENTITY(1,1) NOT NULL,
        id_compania  bigint        NOT NULL,
        codigo       nvarchar(50)  NOT NULL,
        nombre       nvarchar(150) NOT NULL,
        descripcion  nvarchar(500) NULL,
        tipo_accion  nvarchar(15)  NOT NULL,             -- Decision | Asignar | Mensaje (fase 1)
        resultado    nvarchar(15)  NULL,                 -- Decision: Aprobar | Rechazar | Referir
        clave        nvarchar(100) NULL,                 -- Asignar: nombre del resultado (ej. tasa_recargo)
        valor        nvarchar(200) NULL,                 -- Asignar: valor
        mensaje      nvarchar(500) NULL,                 -- Mensaje / motivo legible
        estado       nvarchar(15)  NOT NULL CONSTRAINT DF_RULACC_estado DEFAULT ('Activo'),
        creado_por   nvarchar(100) NOT NULL,
        creado_en    datetime2     NOT NULL CONSTRAINT DF_RULACC_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por nvarchar(100) NULL,
        modificado_en datetime2    NULL,
        CONSTRAINT PK_RUL_ACCION PRIMARY KEY (id_accion),
        CONSTRAINT UQ_RUL_ACCION_codigo UNIQUE (id_compania, codigo),
        CONSTRAINT CK_RULACC_tipo CHECK (tipo_accion IN ('Decision', 'Asignar', 'Mensaje')),
        CONSTRAINT CK_RULACC_resultado CHECK (resultado IS NULL OR resultado IN ('Aprobar', 'Rechazar', 'Referir')),
        CONSTRAINT CK_RULACC_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_RULACC_decision CHECK (tipo_accion <> 'Decision' OR resultado IS NOT NULL),
        CONSTRAINT CK_RULACC_asignar CHECK (tipo_accion <> 'Asignar' OR (clave IS NOT NULL AND valor IS NOT NULL)),
        CONSTRAINT FK_RULACC_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
    );
END
GO

/* ---------------------------------------------------------------------------
   3. Conjunto de reglas (AGRUPACION_REGLA del DAD) y sus versiones
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'rul.CONJUNTO_REGLAS', N'U') IS NULL
BEGIN
    CREATE TABLE rul.CONJUNTO_REGLAS (
        id_conjunto        bigint IDENTITY(1,1) NOT NULL,
        id_compania        bigint        NOT NULL,
        id_contexto        bigint        NOT NULL,
        codigo             nvarchar(50)  NOT NULL,
        nombre             nvarchar(150) NOT NULL,
        descripcion        nvarchar(500) NULL,
        politica_resolucion nvarchar(20) NOT NULL CONSTRAINT DF_RULCJ_politica DEFAULT ('PrimeraCoincidencia'),
        estado             nvarchar(15)  NOT NULL CONSTRAINT DF_RULCJ_estado DEFAULT ('Activo'),
        creado_por         nvarchar(100) NOT NULL,
        creado_en          datetime2     NOT NULL CONSTRAINT DF_RULCJ_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por     nvarchar(100) NULL,
        modificado_en      datetime2     NULL,
        CONSTRAINT PK_RUL_CONJUNTO_REGLAS PRIMARY KEY (id_conjunto),
        CONSTRAINT UQ_RUL_CONJUNTO_codigo UNIQUE (id_compania, codigo),
        CONSTRAINT CK_RULCJ_politica CHECK (politica_resolucion IN ('PrimeraCoincidencia', 'AcumularTodas')),
        CONSTRAINT CK_RULCJ_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_RULCJ_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_RULCJ_CONTEXTO FOREIGN KEY (id_contexto) REFERENCES rul.CONTEXTO (id_contexto)
    );
END
GO

IF OBJECT_ID(N'rul.CONJUNTO_REGLAS_VERSION', N'U') IS NULL
BEGIN
    CREATE TABLE rul.CONJUNTO_REGLAS_VERSION (
        id_version           bigint IDENTITY(1,1) NOT NULL,
        id_conjunto          bigint        NOT NULL,
        numero_version       int           NOT NULL,
        estado               nvarchar(15)  NOT NULL CONSTRAINT DF_RULCV_estado DEFAULT ('Borrador'),
        fecha_vigencia_desde date          NULL,
        fecha_vigencia_hasta date          NULL,
        comentario           nvarchar(500) NULL,
        id_gestion           bigint        NULL,
        creado_por           nvarchar(100) NOT NULL,
        creado_en            datetime2     NOT NULL CONSTRAINT DF_RULCV_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100) NULL,
        modificado_en        datetime2     NULL,
        CONSTRAINT PK_RUL_CONJUNTO_VERSION PRIMARY KEY (id_version),
        CONSTRAINT UQ_RUL_CONJUNTO_VERSION_numero UNIQUE (id_conjunto, numero_version),
        CONSTRAINT CK_RULCV_estado CHECK (estado IN ('Borrador', 'Revision', 'Aprobado', 'Programado', 'Vigente', 'Expirado')),
        CONSTRAINT CK_RULCV_fechas CHECK (fecha_vigencia_hasta IS NULL OR fecha_vigencia_desde IS NULL OR fecha_vigencia_hasta >= fecha_vigencia_desde),
        CONSTRAINT FK_RULCV_CONJUNTO FOREIGN KEY (id_conjunto) REFERENCES rul.CONJUNTO_REGLAS (id_conjunto),
        CONSTRAINT FK_RULCV_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RULCV_vigente' AND object_id = OBJECT_ID(N'rul.CONJUNTO_REGLAS_VERSION'))
    CREATE UNIQUE INDEX UX_RULCV_vigente ON rul.CONJUNTO_REGLAS_VERSION (id_conjunto) WHERE estado = 'Vigente';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RULCV_edicion' AND object_id = OBJECT_ID(N'rul.CONJUNTO_REGLAS_VERSION'))
    CREATE UNIQUE INDEX UX_RULCV_edicion ON rul.CONJUNTO_REGLAS_VERSION (id_conjunto) WHERE estado IN ('Borrador', 'Revision');
GO

IF OBJECT_ID(N'rul.REGLA', N'U') IS NULL
BEGIN
    CREATE TABLE rul.REGLA (
        id_regla     bigint IDENTITY(1,1) NOT NULL,
        id_version   bigint        NOT NULL,
        orden        int           NOT NULL,              -- prioridad: menor se evalua primero
        codigo       nvarchar(50)  NOT NULL,
        nombre       nvarchar(150) NOT NULL,
        descripcion  nvarchar(500) NULL,
        activa       bit           NOT NULL CONSTRAINT DF_RULREG_activa DEFAULT (1),
        creado_por   nvarchar(100) NOT NULL,
        creado_en    datetime2     NOT NULL CONSTRAINT DF_RULREG_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por nvarchar(100) NULL,
        modificado_en datetime2    NULL,
        CONSTRAINT PK_RUL_REGLA PRIMARY KEY (id_regla),
        CONSTRAINT UQ_RUL_REGLA_codigo UNIQUE (id_version, codigo),
        CONSTRAINT UQ_RUL_REGLA_orden UNIQUE (id_version, orden),
        CONSTRAINT FK_RULREG_VERSION FOREIGN KEY (id_version) REFERENCES rul.CONJUNTO_REGLAS_VERSION (id_version)
    );
END
GO

IF OBJECT_ID(N'rul.GRUPO_CONDICION', N'U') IS NULL
BEGIN
    CREATE TABLE rul.GRUPO_CONDICION (
        id_grupo        bigint IDENTITY(1,1) NOT NULL,
        id_regla        bigint       NOT NULL,
        id_grupo_padre  bigint       NULL,                -- NULL = grupo raiz de la regla (uno por regla)
        operador_logico nvarchar(2)  NOT NULL CONSTRAINT DF_RULGRP_oper DEFAULT ('Y'),
        orden           int          NOT NULL CONSTRAINT DF_RULGRP_orden DEFAULT (1),
        CONSTRAINT PK_RUL_GRUPO_CONDICION PRIMARY KEY (id_grupo),
        CONSTRAINT CK_RULGRP_oper CHECK (operador_logico IN ('Y', 'O')),
        CONSTRAINT FK_RULGRP_REGLA FOREIGN KEY (id_regla) REFERENCES rul.REGLA (id_regla),
        CONSTRAINT FK_RULGRP_PADRE FOREIGN KEY (id_grupo_padre) REFERENCES rul.GRUPO_CONDICION (id_grupo)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RULGRP_raiz' AND object_id = OBJECT_ID(N'rul.GRUPO_CONDICION'))
    CREATE UNIQUE INDEX UX_RULGRP_raiz ON rul.GRUPO_CONDICION (id_regla) WHERE id_grupo_padre IS NULL;
GO

IF OBJECT_ID(N'rul.GRUPO_CONDICION_MIEMBRO', N'U') IS NULL
BEGIN
    CREATE TABLE rul.GRUPO_CONDICION_MIEMBRO (
        id_miembro   bigint IDENTITY(1,1) NOT NULL,
        id_grupo     bigint NOT NULL,
        id_condicion bigint NOT NULL,
        negar        bit    NOT NULL CONSTRAINT DF_RULMIE_negar DEFAULT (0),
        orden        int    NOT NULL CONSTRAINT DF_RULMIE_orden DEFAULT (1),
        CONSTRAINT PK_RUL_GRUPO_MIEMBRO PRIMARY KEY (id_miembro),
        CONSTRAINT UQ_RUL_GRUPO_MIEMBRO UNIQUE (id_grupo, id_condicion),
        CONSTRAINT FK_RULMIE_GRUPO FOREIGN KEY (id_grupo) REFERENCES rul.GRUPO_CONDICION (id_grupo),
        CONSTRAINT FK_RULMIE_CONDICION FOREIGN KEY (id_condicion) REFERENCES rul.CONDICION (id_condicion)
    );
END
GO

IF OBJECT_ID(N'rul.REGLA_ACCION', N'U') IS NULL
BEGIN
    CREATE TABLE rul.REGLA_ACCION (
        id_regla  bigint NOT NULL,
        id_accion bigint NOT NULL,
        orden     int    NOT NULL CONSTRAINT DF_RULRA_orden DEFAULT (1),
        CONSTRAINT PK_RUL_REGLA_ACCION PRIMARY KEY (id_regla, id_accion),
        CONSTRAINT FK_RULRA_REGLA FOREIGN KEY (id_regla) REFERENCES rul.REGLA (id_regla),
        CONSTRAINT FK_RULRA_ACCION FOREIGN KEY (id_accion) REFERENCES rul.ACCION (id_accion)
    );
END
GO

/* Quien consume un conjunto (bloquea inactivarlo). El consumidor se registra al configurarse. */
IF OBJECT_ID(N'rul.CONJUNTO_REFERENCIA', N'U') IS NULL
BEGIN
    CREATE TABLE rul.CONJUNTO_REFERENCIA (
        id_referencia    bigint IDENTITY(1,1) NOT NULL,
        id_conjunto      bigint        NOT NULL,
        tipo_consumidor  nvarchar(40)  NOT NULL,          -- PRODUCTO_COMERCIAL, y los que se agreguen
        id_consumidor    bigint        NOT NULL,
        creado_por       nvarchar(100) NOT NULL,
        creado_en        datetime2     NOT NULL CONSTRAINT DF_RULREF_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RUL_CONJUNTO_REFERENCIA PRIMARY KEY (id_referencia),
        CONSTRAINT UQ_RUL_CONJUNTO_REFERENCIA UNIQUE (id_conjunto, tipo_consumidor, id_consumidor),
        CONSTRAINT FK_RULREF_CONJUNTO FOREIGN KEY (id_conjunto) REFERENCES rul.CONJUNTO_REGLAS (id_conjunto)
    );
END
GO

/* ---------------------------------------------------------------------------
   4. Evaluaciones (bitacora explicable) y casos de prueba
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'rul.EVALUACION', N'U') IS NULL
BEGIN
    CREATE TABLE rul.EVALUACION (
        id_evaluacion bigint IDENTITY(1,1) NOT NULL,
        id_version    bigint        NOT NULL,
        id_regla_aplicada bigint    NULL,
        modo          nvarchar(10)  NOT NULL CONSTRAINT DF_RULEVA_modo DEFAULT ('Real'),
        referencia    nvarchar(100) NULL,                 -- ej. SOLICITUD:1234
        entrada       nvarchar(max) NOT NULL,             -- JSON con los atributos usados
        resultado     nvarchar(max) NOT NULL,             -- JSON: decision, asignaciones, mensajes
        traza         nvarchar(max) NULL,                 -- JSON: cada regla/condicion y su resultado
        evaluado_por  nvarchar(100) NOT NULL,
        evaluado_en   datetime2     NOT NULL CONSTRAINT DF_RULEVA_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_RUL_EVALUACION PRIMARY KEY (id_evaluacion),
        CONSTRAINT CK_RULEVA_modo CHECK (modo IN ('Real', 'Prueba')),
        CONSTRAINT FK_RULEVA_VERSION FOREIGN KEY (id_version) REFERENCES rul.CONJUNTO_REGLAS_VERSION (id_version)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RULEVA_referencia' AND object_id = OBJECT_ID(N'rul.EVALUACION'))
    CREATE INDEX IX_RULEVA_referencia ON rul.EVALUACION (referencia, evaluado_en);
GO

IF OBJECT_ID(N'rul.CASO_PRUEBA', N'U') IS NULL
BEGIN
    CREATE TABLE rul.CASO_PRUEBA (
        id_caso       bigint IDENTITY(1,1) NOT NULL,
        id_conjunto   bigint        NOT NULL,
        nombre        nvarchar(150) NOT NULL,
        entrada       nvarchar(max) NOT NULL,             -- JSON de atributos
        decision_esperada nvarchar(15) NULL,              -- Aprobar | Rechazar | Referir | NULL = sin decision
        regla_esperada nvarchar(50) NULL,                 -- codigo de la regla que deberia aplicar
        estado        nvarchar(15)  NOT NULL CONSTRAINT DF_RULCP_estado DEFAULT ('Activo'),
        creado_por    nvarchar(100) NOT NULL,
        creado_en     datetime2     NOT NULL CONSTRAINT DF_RULCP_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por nvarchar(100) NULL,
        modificado_en datetime2     NULL,
        CONSTRAINT PK_RUL_CASO_PRUEBA PRIMARY KEY (id_caso),
        CONSTRAINT CK_RULCP_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_RULCP_decision CHECK (decision_esperada IS NULL OR decision_esperada IN ('Aprobar', 'Rechazar', 'Referir')),
        CONSTRAINT FK_RULCP_CONJUNTO FOREIGN KEY (id_conjunto) REFERENCES rul.CONJUNTO_REGLAS (id_conjunto)
    );
END
GO

/* ---------------------------------------------------------------------------
   5. Elegibilidad en producto comercial (variantes: "solo empleados")
   --------------------------------------------------------------------------- */
IF COL_LENGTH(N'prd.PRODUCTO_COMERCIAL', N'id_conjunto_elegibilidad') IS NULL
    ALTER TABLE prd.PRODUCTO_COMERCIAL ADD id_conjunto_elegibilidad bigint NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PRODCOM_ELEGIBILIDAD')
    EXEC(N'ALTER TABLE prd.PRODUCTO_COMERCIAL ADD CONSTRAINT FK_PRODCOM_ELEGIBILIDAD FOREIGN KEY (id_conjunto_elegibilidad) REFERENCES rul.CONJUNTO_REGLAS (id_conjunto)');
GO

/* ---------------------------------------------------------------------------
   6. Semillas: contextos, atributos, operadores, funciones
   --------------------------------------------------------------------------- */
INSERT INTO rul.CONTEXTO (codigo, nombre, descripcion, estado, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, x.estado, N'sistema' FROM (VALUES
  (N'CLIENTE',           N'Cliente',              N'Datos del cliente (interlocutor) evaluado.', N'Activo'),
  (N'SOLICITUD_CREDITO', N'Solicitud de credito', N'Reservado: se activa con la Ronda 5.', N'Reservado'),
  (N'CREDITO',           N'Credito',              N'Reservado: se activa con la Ronda 7.', N'Reservado'),
  (N'CUENTA',            N'Cuenta',               N'Reservado: se activa con el dominio de Cuentas.', N'Reservado'),
  (N'PAGO',              N'Pago',                 N'Reservado: se activa con la Ronda 8.', N'Reservado')
) x(codigo, nombre, descripcion, estado)
WHERE NOT EXISTS (SELECT 1 FROM rul.CONTEXTO c WHERE c.codigo = x.codigo);
GO

INSERT INTO rul.ATRIBUTO (codigo, nombre, descripcion, tipo_dato, lista_codigo, es_multivalor, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, x.tipo, x.lista, x.multi, N'sistema' FROM (VALUES
  (N'cliente.tipo_persona',     N'Tipo de persona',        N'Fisica o juridica.', N'Texto',    NULL, 0),
  (N'cliente.edad',             N'Edad (anios)',           N'Edad cumplida del cliente.', N'Numero', NULL, 0),
  (N'cliente.fecha_nacimiento', N'Fecha de nacimiento',    NULL, N'Fecha', NULL, 0),
  (N'cliente.estado',           N'Estado del cliente',     N'Activo, Inactivo, Fusionado, Fallecido...', N'Texto', NULL, 0),
  (N'cliente.antiguedad_meses', N'Antiguedad (meses)',     N'Meses desde el alta del cliente.', N'Numero', NULL, 0),
  (N'cliente.kyc_vigente',      N'KYC vigente',            N'El cliente tiene su conocimiento vigente.', N'Booleano', NULL, 0),
  (N'cliente.tiene_bloqueo',    N'Tiene bloqueo activo',   NULL, N'Booleano', NULL, 0),
  (N'cliente.es_empleado',      N'Es empleado de la entidad', NULL, N'Booleano', NULL, 0),
  (N'cliente.roles',            N'Roles del cliente',      N'Roles de interlocutor que posee (multivalor).', N'Texto', NULL, 1),
  (N'cliente.sucursal',         N'Sucursal',               NULL, N'Texto', NULL, 0),
  (N'cliente.canal_origen',     N'Canal de origen',        NULL, N'Texto', NULL, 0),
  (N'cliente.pais',             N'Pais de residencia',     NULL, N'Texto', NULL, 0),
  (N'sistema.fecha_actual',     N'Fecha actual',           N'Fecha de proceso.', N'Fecha', NULL, 0)
) x(codigo, nombre, descripcion, tipo, lista, multi)
WHERE NOT EXISTS (SELECT 1 FROM rul.ATRIBUTO a WHERE a.codigo = x.codigo);
GO

INSERT INTO rul.CONTEXTO_ATRIBUTO (id_contexto, id_atributo)
SELECT c.id_contexto, a.id_atributo
FROM rul.CONTEXTO c JOIN rul.ATRIBUTO a ON a.codigo LIKE N'cliente.%' OR a.codigo LIKE N'sistema.%'
WHERE c.codigo = N'CLIENTE'
  AND NOT EXISTS (SELECT 1 FROM rul.CONTEXTO_ATRIBUTO ca WHERE ca.id_contexto = c.id_contexto AND ca.id_atributo = a.id_atributo);
-- Los atributos cliente.* y sistema.* tambien estan disponibles en los contextos reservados.
INSERT INTO rul.CONTEXTO_ATRIBUTO (id_contexto, id_atributo)
SELECT c.id_contexto, a.id_atributo
FROM rul.CONTEXTO c JOIN rul.ATRIBUTO a ON a.codigo LIKE N'cliente.%' OR a.codigo LIKE N'sistema.%'
WHERE c.codigo <> N'CLIENTE'
  AND NOT EXISTS (SELECT 1 FROM rul.CONTEXTO_ATRIBUTO ca WHERE ca.id_contexto = c.id_contexto AND ca.id_atributo = a.id_atributo);
GO

INSERT INTO rul.OPERADOR (codigo, nombre, simbolo, aridad, tipos_dato, orden, creado_por)
SELECT x.codigo, x.nombre, x.simbolo, x.aridad, x.tipos, x.orden, N'sistema' FROM (VALUES
  (N'IGUAL',       N'es igual a',            N'=',  1, N'Texto,Numero,Fecha,Booleano,Lista', 1),
  (N'DISTINTO',    N'es distinto de',        N'<>', 1, N'Texto,Numero,Fecha,Booleano,Lista', 2),
  (N'MAYOR',       N'es mayor que',          N'>',  1, N'Numero,Fecha', 3),
  (N'MAYOR_IGUAL', N'es mayor o igual que',  N'>=', 1, N'Numero,Fecha', 4),
  (N'MENOR',       N'es menor que',          N'<',  1, N'Numero,Fecha', 5),
  (N'MENOR_IGUAL', N'es menor o igual que',  N'<=', 1, N'Numero,Fecha', 6),
  (N'ENTRE',       N'esta entre',            NULL,  2, N'Numero,Fecha', 7),
  (N'EN',          N'esta en la lista',      NULL,  9, N'Texto,Numero,Lista', 8),
  (N'NO_EN',       N'no esta en la lista',   NULL,  9, N'Texto,Numero,Lista', 9),
  (N'CONTIENE',    N'contiene',              NULL,  1, N'Texto', 10),
  (N'COMIENZA',    N'comienza con',          NULL,  1, N'Texto', 11),
  (N'TIENE',       N'incluye el valor',      NULL,  1, N'Texto,Lista', 12),   -- para multivalor
  (N'ES_VACIO',    N'no tiene valor',        NULL,  0, N'Texto,Numero,Fecha,Booleano,Lista', 13),
  (N'NO_ES_VACIO', N'tiene valor',           NULL,  0, N'Texto,Numero,Fecha,Booleano,Lista', 14)
) x(codigo, nombre, simbolo, aridad, tipos, orden)
WHERE NOT EXISTS (SELECT 1 FROM rul.OPERADOR o WHERE o.codigo = x.codigo);
GO

INSERT INTO rul.FUNCION (codigo, nombre, descripcion, tipo_entrada, tipo_retorno, cantidad_parametros, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, x.entrada, x.retorno, x.param, N'sistema' FROM (VALUES
  (N'EDAD_ANIOS',      N'Anios cumplidos hasta hoy',     N'Anios completos entre la fecha y la fecha actual.', N'Fecha',  N'Numero', 0),
  (N'DIAS_DESDE',      N'Dias transcurridos desde',      N'Dias entre la fecha y hoy.',                        N'Fecha',  N'Numero', 0),
  (N'MESES_DESDE',     N'Meses transcurridos desde',     N'Meses completos entre la fecha y hoy.',             N'Fecha',  N'Numero', 0),
  (N'DIAS_HASTA',      N'Dias que faltan hasta',         N'Dias entre hoy y la fecha.',                        N'Fecha',  N'Numero', 0),
  (N'SUMAR_DIAS',      N'Sumar dias',                    N'Fecha mas N dias.',                                 N'Fecha',  N'Fecha',  1),
  (N'SUMAR_MESES',     N'Sumar meses',                   N'Fecha mas N meses.',                                N'Fecha',  N'Fecha',  1),
  (N'REDONDEAR',       N'Redondear',                     N'Redondea a N decimales.',                           N'Numero', N'Numero', 1),
  (N'ABSOLUTO',        N'Valor absoluto',                NULL,                                                 N'Numero', N'Numero', 0),
  (N'PORCENTAJE_DE',   N'Porcentaje de',                 N'Numero multiplicado por N/100.',                    N'Numero', N'Numero', 1),
  (N'LONGITUD',        N'Longitud del texto',            NULL,                                                 N'Texto',  N'Numero', 0),
  (N'MAYUSCULAS',      N'Texto en mayusculas',           NULL,                                                 N'Texto',  N'Texto',  0),
  (N'CANTIDAD',        N'Cantidad de valores',           N'Numero de elementos de un atributo multivalor.',    N'Texto',  N'Numero', 0)
) x(codigo, nombre, descripcion, entrada, retorno, param)
WHERE NOT EXISTS (SELECT 1 FROM rul.FUNCION f WHERE f.codigo = x.codigo);
GO

/* ---------------------------------------------------------------------------
   7. Tipo de gestion, lista de valores y permisos
   --------------------------------------------------------------------------- */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, 1, N'sistema' FROM (VALUES
  (N'POLITICA_RESOLUCION_REGLAS', N'Politica de resolucion de reglas', N'Como se combinan las reglas que se cumplen dentro de un conjunto.')
) x(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO t WHERE t.codigo = x.codigo);
GO

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, descripcion, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, i.descripcion, N'sistema'
FROM (VALUES
  (N'PrimeraCoincidencia', N'Primera coincidencia', 1, N'Se aplica la primera regla (por orden) cuyas condiciones se cumplen; las demas se ignoran.'),
  (N'AcumularTodas',       N'Acumular todas',       2, N'Se aplican todas las reglas que se cumplen; si hay decisiones distintas prevalece Rechazar > Referir > Aprobar.')
) i(codigo, nombre, orden, descripcion)
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'POLITICA_RESOLUCION_REGLAS'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
SELECT x.codigo, x.nombre, N'Campos', 1, 1, N'Producto', N'sistema' FROM (VALUES
  (N'RUL_VERSION_CONJUNTO', N'Publicacion de version de conjunto de reglas')
) x(codigo, nombre)
WHERE NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION t WHERE t.codigo = x.codigo);
GO

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, N'Pantalla', x.descripcion, N'sistema' FROM (VALUES
  (N'RUL_CONSULTA', N'Consultar reglas',            N'Ver conjuntos de reglas, condiciones, acciones y evaluaciones.'),
  (N'RUL_ADMIN',    N'Administrar reglas',          N'Crear condiciones, acciones, conjuntos y borradores de version; casos de prueba.'),
  (N'RUL_PUBLICAR', N'Publicar version de reglas',  N'Enviar a aprobacion una version de conjunto de reglas.'),
  (N'RUL_EVALUAR',  N'Probar reglas',               N'Ejecutar el simulador contra casos de prueba o datos ingresados.')
) x(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);
GO

DECLARE @adm bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'RUL_CONSULTA', N'RUL_ADMIN', N'RUL_PUBLICAR', N'RUL_EVALUAR')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO r WHERE r.id_rol_autorizacion = @adm AND r.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

PRINT 'Ronda 43 (motor de reglas) aplicada.';
GO
