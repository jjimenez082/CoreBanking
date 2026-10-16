/* ============================================================================
   Ronda 42 - Cimientos de Creditos (PROPUESTA DE MODELO FISICO, para revision). Re-ejecutable.
   Requiere: lov_descripcion_ronda33.sql, administracion_transversal.sql, numeracion_rangos.sql.

   Alcance (ver docs_proyecto/Creditos_Cimientos_Producto_Tasas_Calendarios.md):
     1. gen.BASE_CALCULO              Bases de dias (30/360, ACT/365, ACT/360, ACT/ACT)            CRE-004
     2. gen.INDICADOR_FINANCIERO(+_VALOR)  Tasas de referencia con historial (TBP, Prime, SOFR)    MCS-002 / PRC-005
     3. gen.CALENDARIO + convencion de ajuste de fecha (dias habiles por pais)                      GEN-005 (fase 1)
     4. gen.CATEGORIA_REGULATORIA     Clasificacion regulatoria por pais/regulador                  GEN-018 / CRE-001
     5. prd.PRODUCTO_OPERATIVO(+_MONEDA) y prd.PARAMETRO_PRODUCTO_OPERATIVO                          PRD-001 / CRE-001/002/003
     6. prd.PRODUCTO_COMERCIAL(+_VERSION, +_VERSION_VALOR) con vigencia programada y variantes      PRC-002 / PRC-003
     7. Listas de valores, tipo de gestion PRD_VERSION_PRODUCTO y permisos.
   NO crea tablas de credito (solicitud, credito, cuotas...): eso es de las rondas siguientes.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'gen.LISTA_VALORES_ITEM', N'descripcion') IS NULL
    THROW 50042, 'Falta correr lov_descripcion_ronda33.sql antes de este script.', 1;
IF OBJECT_ID(N'gen.FERIADO', N'U') IS NULL
    THROW 50043, 'Falta correr administracion_transversal.sql antes de este script.', 1;
GO

IF SCHEMA_ID(N'prd') IS NULL EXEC(N'CREATE SCHEMA prd');
GO

/* ---------------------------------------------------------------------------
   1. Bases de calculo de interes (catalogo global con comportamiento propio)
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'gen.BASE_CALCULO', N'U') IS NULL
BEGIN
    CREATE TABLE gen.BASE_CALCULO (
        id_base_calculo   bigint IDENTITY(1,1) NOT NULL,
        codigo            nvarchar(20)   NOT NULL,
        nombre            nvarchar(100)  NOT NULL,
        descripcion       nvarchar(500)  NULL,
        tipo_conteo       nvarchar(10)   NOT NULL,   -- Comercial (meses de 30 dias) / Real (dias calendario)
        dias_anio_fijo    smallint       NULL,       -- 360 / 365; NULL cuando usa_anio_real = 1
        usa_anio_real     bit            NOT NULL CONSTRAINT DF_BASE_CALCULO_anio_real DEFAULT (0),  -- ACT/ACT: 365 o 366 segun el anio
        estado            nvarchar(20)   NOT NULL CONSTRAINT DF_BASE_CALCULO_estado DEFAULT ('Activo'),
        creado_por        nvarchar(100)  NOT NULL,
        creado_en         datetime2      NOT NULL CONSTRAINT DF_BASE_CALCULO_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por    nvarchar(100)  NULL,
        modificado_en     datetime2      NULL,
        CONSTRAINT PK_BASE_CALCULO PRIMARY KEY (id_base_calculo),
        CONSTRAINT UQ_BASE_CALCULO_codigo UNIQUE (codigo),
        CONSTRAINT CK_BASE_CALCULO_conteo CHECK (tipo_conteo IN ('Comercial', 'Real')),
        CONSTRAINT CK_BASE_CALCULO_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_BASE_CALCULO_anio CHECK ((usa_anio_real = 1 AND dias_anio_fijo IS NULL) OR (usa_anio_real = 0 AND dias_anio_fijo IN (360, 365)))
    );
END
GO

INSERT INTO gen.BASE_CALCULO (codigo, nombre, descripcion, tipo_conteo, dias_anio_fijo, usa_anio_real, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, x.tipo_conteo, x.dias, x.anio_real, N'sistema' FROM (VALUES
  (N'30_360',  N'30/360',  N'Meses de 30 dias y anio de 360. Los dias entre dos fechas se cuentan por meses comerciales.', N'Comercial', 360, 0),
  (N'ACT_365', N'ACT/365', N'Dias calendario reales sobre un anio de 365.', N'Real', 365, 0),
  (N'ACT_360', N'ACT/360', N'Dias calendario reales sobre un anio de 360.', N'Real', 360, 0),
  (N'ACT_ACT', N'ACT/ACT', N'Dias calendario reales sobre el anio real: 365, o 366 si el anio es bisiesto (se resuelve por periodo).', N'Real', NULL, 1)
) x(codigo, nombre, descripcion, tipo_conteo, dias, anio_real)
WHERE NOT EXISTS (SELECT 1 FROM gen.BASE_CALCULO b WHERE b.codigo = x.codigo);
GO

/* ---------------------------------------------------------------------------
   2. Indicadores financieros (tasas de referencia) con historial de valores.
      Estructura general, no por compania. El tipo de cambio sigue en gen.TIPO_CAMBIO (decision D-3 del documento).
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'gen.INDICADOR_FINANCIERO', N'U') IS NULL
BEGIN
    CREATE TABLE gen.INDICADOR_FINANCIERO (
        id_indicador_financiero bigint IDENTITY(1,1) NOT NULL,
        codigo                  nvarchar(30)   NOT NULL,
        nombre                  nvarchar(150)  NOT NULL,
        descripcion             nvarchar(500)  NULL,
        id_tipo_indicador       bigint         NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_INDICADOR_FINANCIERO)
        moneda_codigo           nvarchar(3)    NULL,       -- ISO 4217 de la moneda a la que aplica la tasa (informativo)
        frecuencia              nvarchar(15)   NOT NULL CONSTRAINT DF_INDICADOR_frecuencia DEFAULT ('Mensual'),   -- Diaria / Semanal / Mensual / Trimestral
        estado                  nvarchar(20)   NOT NULL CONSTRAINT DF_INDICADOR_estado DEFAULT ('Activo'),
        creado_por              nvarchar(100)  NOT NULL,
        creado_en               datetime2      NOT NULL CONSTRAINT DF_INDICADOR_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por          nvarchar(100)  NULL,
        modificado_en           datetime2      NULL,
        CONSTRAINT PK_INDICADOR_FINANCIERO PRIMARY KEY (id_indicador_financiero),
        CONSTRAINT UQ_INDICADOR_FINANCIERO_codigo UNIQUE (codigo),
        CONSTRAINT CK_INDICADOR_frecuencia CHECK (frecuencia IN ('Diaria', 'Semanal', 'Mensual', 'Trimestral')),
        CONSTRAINT CK_INDICADOR_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_INDICADOR_TIPO FOREIGN KEY (id_tipo_indicador) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

IF OBJECT_ID(N'gen.INDICADOR_FINANCIERO_VALOR', N'U') IS NULL
BEGIN
    CREATE TABLE gen.INDICADOR_FINANCIERO_VALOR (
        id_indicador_valor      bigint IDENTITY(1,1) NOT NULL,
        id_indicador_financiero bigint         NOT NULL,
        fecha_vigencia          date           NOT NULL,   -- rige desde esta fecha hasta la siguiente cargada
        valor                   decimal(18,8)  NOT NULL,   -- tasa nominal anual en porcentaje (5.25 = 5,25 %)
        origen                  nvarchar(30)   NOT NULL CONSTRAINT DF_INDICADOR_VALOR_origen DEFAULT ('Manual'),
        id_gestion              bigint         NULL,
        creado_por              nvarchar(100)  NOT NULL,
        creado_en               datetime2      NOT NULL CONSTRAINT DF_INDICADOR_VALOR_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_INDICADOR_FINANCIERO_VALOR PRIMARY KEY (id_indicador_valor),
        CONSTRAINT UQ_INDICADOR_VALOR_fecha UNIQUE (id_indicador_financiero, fecha_vigencia),
        CONSTRAINT CK_INDICADOR_VALOR_rango CHECK (valor >= 0 AND valor <= 1000),
        CONSTRAINT FK_INDICADOR_VALOR_INDICADOR FOREIGN KEY (id_indicador_financiero) REFERENCES gen.INDICADOR_FINANCIERO (id_indicador_financiero),
        CONSTRAINT FK_INDICADOR_VALOR_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion)
    );
END
GO

/* ---------------------------------------------------------------------------
   3. Calendarios (fase 1): dias habiles de la semana + feriados del pais (gen.FERIADO).
      La convencion de ajuste es una lista de valores. El versionado y la materializacion de fechas llegan despues;
      cada cuota guardara la fecha ya resuelta (evidencia reproducible).
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'gen.CALENDARIO', N'U') IS NULL
BEGIN
    CREATE TABLE gen.CALENDARIO (
        id_calendario   bigint IDENTITY(1,1) NOT NULL,
        id_compania     bigint         NOT NULL,
        codigo          nvarchar(30)   NOT NULL,
        nombre          nvarchar(150)  NOT NULL,
        id_pais         bigint         NOT NULL,   -- LISTA_VALORES_ITEM (PAIS): sus feriados de gen.FERIADO aplican a este calendario
        lunes           bit            NOT NULL CONSTRAINT DF_CALENDARIO_lun DEFAULT (1),
        martes          bit            NOT NULL CONSTRAINT DF_CALENDARIO_mar DEFAULT (1),
        miercoles       bit            NOT NULL CONSTRAINT DF_CALENDARIO_mie DEFAULT (1),
        jueves          bit            NOT NULL CONSTRAINT DF_CALENDARIO_jue DEFAULT (1),
        viernes         bit            NOT NULL CONSTRAINT DF_CALENDARIO_vie DEFAULT (1),
        sabado          bit            NOT NULL CONSTRAINT DF_CALENDARIO_sab DEFAULT (0),
        domingo         bit            NOT NULL CONSTRAINT DF_CALENDARIO_dom DEFAULT (0),
        es_defecto      bit            NOT NULL CONSTRAINT DF_CALENDARIO_defecto DEFAULT (0),
        estado          nvarchar(20)   NOT NULL CONSTRAINT DF_CALENDARIO_estado DEFAULT ('Activo'),
        creado_por      nvarchar(100)  NOT NULL,
        creado_en       datetime2      NOT NULL CONSTRAINT DF_CALENDARIO_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por  nvarchar(100)  NULL,
        modificado_en   datetime2      NULL,
        CONSTRAINT PK_CALENDARIO PRIMARY KEY (id_calendario),
        CONSTRAINT UQ_CALENDARIO_codigo UNIQUE (id_compania, codigo),
        CONSTRAINT CK_CALENDARIO_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_CALENDARIO_habil CHECK (CAST(lunes AS int) + CAST(martes AS int) + CAST(miercoles AS int) + CAST(jueves AS int) + CAST(viernes AS int) + CAST(sabado AS int) + CAST(domingo AS int) >= 1),
        CONSTRAINT FK_CALENDARIO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_CALENDARIO_PAIS FOREIGN KEY (id_pais) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_CALENDARIO_defecto' AND object_id = OBJECT_ID(N'gen.CALENDARIO'))
    CREATE UNIQUE INDEX UX_CALENDARIO_defecto ON gen.CALENDARIO (id_compania) WHERE es_defecto = 1 AND estado = 'Activo';
GO

/* ---------------------------------------------------------------------------
   4. Categorias regulatorias por pais / regulador (CIC-SUGEF en Costa Rica, SBP en Panama). Las define el cliente.
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'gen.CATEGORIA_REGULATORIA', N'U') IS NULL
BEGIN
    CREATE TABLE gen.CATEGORIA_REGULATORIA (
        id_categoria_regulatoria bigint IDENTITY(1,1) NOT NULL,
        id_pais_regulador        bigint         NOT NULL,
        codigo                   nvarchar(30)   NOT NULL,
        nombre                   nvarchar(150)  NOT NULL,
        descripcion              nvarchar(500)  NULL,
        estado                   nvarchar(20)   NOT NULL CONSTRAINT DF_CATREG_estado DEFAULT ('Activo'),
        creado_por               nvarchar(100)  NOT NULL,
        creado_en                datetime2      NOT NULL CONSTRAINT DF_CATREG_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por           nvarchar(100)  NULL,
        modificado_en            datetime2      NULL,
        CONSTRAINT PK_CATEGORIA_REGULATORIA PRIMARY KEY (id_categoria_regulatoria),
        CONSTRAINT UQ_CATREG_codigo UNIQUE (id_pais_regulador, codigo),
        CONSTRAINT CK_CATREG_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_CATREG_PAIS_REGULADOR FOREIGN KEY (id_pais_regulador) REFERENCES gen.PAIS_REGULADOR (id_pais_regulador)
    );
END
GO

/* ---------------------------------------------------------------------------
   5. Producto operativo: lo que el producto ES y puede hacer (estructura). Lo comercial (tasas, comisiones, plazos) vive
      en las versiones del producto comercial. Cubre creditos hoy y cuentas despues (columna dominio).
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'prd.PRODUCTO_OPERATIVO', N'U') IS NULL
BEGIN
    CREATE TABLE prd.PRODUCTO_OPERATIVO (
        id_producto_operativo    bigint IDENTITY(1,1) NOT NULL,
        id_compania              bigint         NOT NULL,
        codigo                   nvarchar(30)   NOT NULL,
        nombre                   nvarchar(150)  NOT NULL,
        descripcion              nvarchar(500)  NULL,
        dominio                  nvarchar(10)   NOT NULL,    -- Credito / Cuenta
        id_categoria_producto    bigint         NOT NULL,    -- LISTA_VALORES_ITEM (CATEGORIA_PRODUCTO_CREDITO o _CUENTA segun dominio)
        id_categoria_regulatoria bigint         NULL,
        estrategia_versionado    nvarchar(15)   NOT NULL CONSTRAINT DF_PRODOP_estrategia DEFAULT ('FijoAlOrigen'),   -- Dinamico: la instancia lee la version vigente; FijoAlOrigen: copia una vez
        tipo_persona_aplicable   nvarchar(10)   NOT NULL CONSTRAINT DF_PRODOP_persona DEFAULT ('Ambas'),              -- Fisica / Juridica / Ambas
        id_calendario            bigint         NULL,        -- calendario para fechas de pago; NULL = el calendario por defecto de la compania
        estado                   nvarchar(20)   NOT NULL CONSTRAINT DF_PRODOP_estado DEFAULT ('Borrador'),
        creado_por               nvarchar(100)  NOT NULL,
        creado_en                datetime2      NOT NULL CONSTRAINT DF_PRODOP_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por           nvarchar(100)  NULL,
        modificado_en            datetime2      NULL,
        CONSTRAINT PK_PRODUCTO_OPERATIVO PRIMARY KEY (id_producto_operativo),
        CONSTRAINT UQ_PRODUCTO_OPERATIVO_codigo UNIQUE (id_compania, codigo),
        CONSTRAINT CK_PRODOP_dominio CHECK (dominio IN ('Credito', 'Cuenta')),
        CONSTRAINT CK_PRODOP_estrategia CHECK (estrategia_versionado IN ('Dinamico', 'FijoAlOrigen')),
        CONSTRAINT CK_PRODOP_persona CHECK (tipo_persona_aplicable IN ('Fisica', 'Juridica', 'Ambas')),
        CONSTRAINT CK_PRODOP_estado CHECK (estado IN ('Borrador', 'Activo', 'Inactivo')),
        CONSTRAINT FK_PRODOP_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_PRODOP_CATEGORIA FOREIGN KEY (id_categoria_producto) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_PRODOP_CATREG FOREIGN KEY (id_categoria_regulatoria) REFERENCES gen.CATEGORIA_REGULATORIA (id_categoria_regulatoria),
        CONSTRAINT FK_PRODOP_CALENDARIO FOREIGN KEY (id_calendario) REFERENCES gen.CALENDARIO (id_calendario)
    );
END
GO

IF OBJECT_ID(N'prd.PRODUCTO_OPERATIVO_MONEDA', N'U') IS NULL
BEGIN
    CREATE TABLE prd.PRODUCTO_OPERATIVO_MONEDA (
        id_producto_operativo  bigint        NOT NULL,
        id_moneda              bigint        NOT NULL,
        es_principal           bit           NOT NULL CONSTRAINT DF_PRODOP_MONEDA_principal DEFAULT (0),
        creado_por             nvarchar(100) NOT NULL,
        creado_en              datetime2     NOT NULL CONSTRAINT DF_PRODOP_MONEDA_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_PRODUCTO_OPERATIVO_MONEDA PRIMARY KEY (id_producto_operativo, id_moneda),
        CONSTRAINT FK_PRODOP_MONEDA_PRODUCTO FOREIGN KEY (id_producto_operativo) REFERENCES prd.PRODUCTO_OPERATIVO (id_producto_operativo),
        CONSTRAINT FK_PRODOP_MONEDA_MONEDA FOREIGN KEY (id_moneda) REFERENCES gen.MONEDA (id_moneda)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PRODOP_MONEDA_principal' AND object_id = OBJECT_ID(N'prd.PRODUCTO_OPERATIVO_MONEDA'))
    CREATE UNIQUE INDEX UX_PRODOP_MONEDA_principal ON prd.PRODUCTO_OPERATIVO_MONEDA (id_producto_operativo) WHERE es_principal = 1;
GO

/* Parametros del producto: la "forma" de las condiciones (que se puede configurar, con que tipo y limites).
   Los valores concretos viven en prd.PRODUCTO_COMERCIAL_VERSION_VALOR. Un componente economico (comision, seguro, mora)
   lleva ademas como se calcula: monto fijo o porcentaje sobre una base. */
IF OBJECT_ID(N'prd.PARAMETRO_PRODUCTO_OPERATIVO', N'U') IS NULL
BEGIN
    CREATE TABLE prd.PARAMETRO_PRODUCTO_OPERATIVO (
        id_parametro_producto    bigint IDENTITY(1,1) NOT NULL,
        id_producto_operativo    bigint         NOT NULL,
        codigo                   nvarchar(60)   NOT NULL,
        nombre                   nvarchar(150)  NOT NULL,
        descripcion              nvarchar(500)  NULL,
        grupo                    nvarchar(20)   NOT NULL,    -- Monto / Plazo / Tasa / PlanPago / Comision / Mora / Seguro / Garantia / Gracia / General
        tipo_dato                nvarchar(15)   NOT NULL,    -- Texto / Entero / Decimal / Booleano / Fecha / Lista
        id_lista_valores_tipo    bigint         NULL,        -- cuando tipo_dato = Lista
        valor_minimo             decimal(18,6)  NULL,
        valor_maximo             decimal(18,6)  NULL,
        obligatorio              bit            NOT NULL CONSTRAINT DF_PARPROD_obligatorio DEFAULT (0),
        valor_defecto            nvarchar(500)  NULL,
        es_componente_economico  bit            NOT NULL CONSTRAINT DF_PARPROD_economico DEFAULT (0),
        tipo_calculo_economico   nvarchar(15)   NULL,        -- MontoFijo / Porcentaje
        id_base_componente       bigint         NULL,        -- LISTA_VALORES_ITEM (BASE_COMPONENTE_ECONOMICO) cuando es Porcentaje
        codigo_tipo_evento       nvarchar(100)  NULL,        -- evento contable del componente (se liga a TIPO_EVENTO en la ronda de contabilizacion)
        orden                    int            NOT NULL CONSTRAINT DF_PARPROD_orden DEFAULT (0),
        estado                   nvarchar(20)   NOT NULL CONSTRAINT DF_PARPROD_estado DEFAULT ('Activo'),
        creado_por               nvarchar(100)  NOT NULL,
        creado_en                datetime2      NOT NULL CONSTRAINT DF_PARPROD_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por           nvarchar(100)  NULL,
        modificado_en            datetime2      NULL,
        CONSTRAINT PK_PARAMETRO_PRODUCTO_OPERATIVO PRIMARY KEY (id_parametro_producto),
        CONSTRAINT UQ_PARPROD_codigo UNIQUE (id_producto_operativo, codigo),
        CONSTRAINT CK_PARPROD_grupo CHECK (grupo IN ('Monto', 'Plazo', 'Tasa', 'PlanPago', 'Comision', 'Mora', 'Seguro', 'Garantia', 'Gracia', 'General')),
        CONSTRAINT CK_PARPROD_tipo CHECK (tipo_dato IN ('Texto', 'Entero', 'Decimal', 'Booleano', 'Fecha', 'Lista')),
        CONSTRAINT CK_PARPROD_lista CHECK ((tipo_dato = 'Lista' AND id_lista_valores_tipo IS NOT NULL) OR (tipo_dato <> 'Lista' AND id_lista_valores_tipo IS NULL)),
        CONSTRAINT CK_PARPROD_limites CHECK (valor_minimo IS NULL OR valor_maximo IS NULL OR valor_minimo <= valor_maximo),
        CONSTRAINT CK_PARPROD_economico CHECK (
              (es_componente_economico = 0 AND tipo_calculo_economico IS NULL AND id_base_componente IS NULL)
           OR (es_componente_economico = 1 AND tipo_calculo_economico = 'MontoFijo' AND id_base_componente IS NULL)
           OR (es_componente_economico = 1 AND tipo_calculo_economico = 'Porcentaje' AND id_base_componente IS NOT NULL)),
        CONSTRAINT CK_PARPROD_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_PARPROD_PRODUCTO FOREIGN KEY (id_producto_operativo) REFERENCES prd.PRODUCTO_OPERATIVO (id_producto_operativo),
        CONSTRAINT FK_PARPROD_LISTA FOREIGN KEY (id_lista_valores_tipo) REFERENCES gen.LISTA_VALORES_TIPO (id_lista_valores_tipo),
        CONSTRAINT FK_PARPROD_BASE FOREIGN KEY (id_base_componente) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

/* ---------------------------------------------------------------------------
   6. Producto comercial: lo que se ofrece al cliente. Una variante hereda de un producto base (un solo nivel).
      Las condiciones se versionan: Borrador -> Revision -> Aprobado -> Programado -> Vigente -> Expirado (via Gestion).
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'prd.PRODUCTO_COMERCIAL', N'U') IS NULL
BEGIN
    CREATE TABLE prd.PRODUCTO_COMERCIAL (
        id_producto_comercial       bigint IDENTITY(1,1) NOT NULL,
        id_compania                 bigint         NOT NULL,
        id_producto_operativo       bigint         NOT NULL,
        codigo                      nvarchar(30)   NOT NULL,
        nombre                      nvarchar(150)  NOT NULL,
        descripcion                 nvarchar(500)  NULL,
        id_producto_comercial_base  bigint         NULL,   -- variante: hereda las condiciones del base (un nivel); sus propios valores las sustituyen
        estado                      nvarchar(20)   NOT NULL CONSTRAINT DF_PRODCOM_estado DEFAULT ('Activo'),
        creado_por                  nvarchar(100)  NOT NULL,
        creado_en                   datetime2      NOT NULL CONSTRAINT DF_PRODCOM_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por              nvarchar(100)  NULL,
        modificado_en               datetime2      NULL,
        CONSTRAINT PK_PRODUCTO_COMERCIAL PRIMARY KEY (id_producto_comercial),
        CONSTRAINT UQ_PRODUCTO_COMERCIAL_codigo UNIQUE (id_compania, codigo),
        CONSTRAINT CK_PRODCOM_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_PRODCOM_base CHECK (id_producto_comercial_base IS NULL OR id_producto_comercial_base <> id_producto_comercial),
        CONSTRAINT FK_PRODCOM_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_PRODCOM_OPERATIVO FOREIGN KEY (id_producto_operativo) REFERENCES prd.PRODUCTO_OPERATIVO (id_producto_operativo),
        CONSTRAINT FK_PRODCOM_BASE FOREIGN KEY (id_producto_comercial_base) REFERENCES prd.PRODUCTO_COMERCIAL (id_producto_comercial)
    );
END
GO

IF OBJECT_ID(N'prd.PRODUCTO_COMERCIAL_VERSION', N'U') IS NULL
BEGIN
    CREATE TABLE prd.PRODUCTO_COMERCIAL_VERSION (
        id_version              bigint IDENTITY(1,1) NOT NULL,
        id_producto_comercial   bigint         NOT NULL,
        numero_version          int            NOT NULL,
        estado                  nvarchar(15)   NOT NULL CONSTRAINT DF_PRODVER_estado DEFAULT ('Borrador'),
        fecha_vigencia_desde    date           NULL,       -- fecha en que la version pasa a Vigente (Programado hasta entonces)
        fecha_vigencia_hasta    date           NULL,       -- se completa cuando otra version la sustituye (Expirado)
        comentario              nvarchar(500)  NULL,
        id_gestion              bigint         NULL,       -- gestion de aprobacion que la publico
        creado_por              nvarchar(100)  NOT NULL,
        creado_en               datetime2      NOT NULL CONSTRAINT DF_PRODVER_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por          nvarchar(100)  NULL,
        modificado_en           datetime2      NULL,
        CONSTRAINT PK_PRODUCTO_COMERCIAL_VERSION PRIMARY KEY (id_version),
        CONSTRAINT UQ_PRODVER_numero UNIQUE (id_producto_comercial, numero_version),
        CONSTRAINT CK_PRODVER_estado CHECK (estado IN ('Borrador', 'Revision', 'Aprobado', 'Programado', 'Vigente', 'Expirado')),
        CONSTRAINT CK_PRODVER_fechas CHECK (fecha_vigencia_hasta IS NULL OR fecha_vigencia_desde IS NULL OR fecha_vigencia_hasta >= fecha_vigencia_desde),
        CONSTRAINT FK_PRODVER_PRODUCTO FOREIGN KEY (id_producto_comercial) REFERENCES prd.PRODUCTO_COMERCIAL (id_producto_comercial),
        CONSTRAINT FK_PRODVER_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion)
    );
END
GO
-- Una sola version Vigente por producto, y una sola en edicion (Borrador/Revision) a la vez.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PRODVER_vigente' AND object_id = OBJECT_ID(N'prd.PRODUCTO_COMERCIAL_VERSION'))
    CREATE UNIQUE INDEX UX_PRODVER_vigente ON prd.PRODUCTO_COMERCIAL_VERSION (id_producto_comercial) WHERE estado = 'Vigente';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PRODVER_edicion' AND object_id = OBJECT_ID(N'prd.PRODUCTO_COMERCIAL_VERSION'))
    CREATE UNIQUE INDEX UX_PRODVER_edicion ON prd.PRODUCTO_COMERCIAL_VERSION (id_producto_comercial) WHERE estado IN ('Borrador', 'Revision');
GO

IF OBJECT_ID(N'prd.PRODUCTO_COMERCIAL_VERSION_VALOR', N'U') IS NULL
BEGIN
    CREATE TABLE prd.PRODUCTO_COMERCIAL_VERSION_VALOR (
        id_version_valor      bigint IDENTITY(1,1) NOT NULL,
        id_version            bigint         NOT NULL,
        id_parametro_producto bigint         NOT NULL,
        valor                 nvarchar(500)  NOT NULL,     -- siempre texto; se interpreta segun tipo_dato del parametro. Sin fila = hereda del base o usa el valor por defecto
        creado_por            nvarchar(100)  NOT NULL,
        creado_en             datetime2      NOT NULL CONSTRAINT DF_PRODVERVAL_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_PRODUCTO_COMERCIAL_VERSION_VALOR PRIMARY KEY (id_version_valor),
        CONSTRAINT UQ_PRODVERVAL UNIQUE (id_version, id_parametro_producto),
        CONSTRAINT FK_PRODVERVAL_VERSION FOREIGN KEY (id_version) REFERENCES prd.PRODUCTO_COMERCIAL_VERSION (id_version),
        CONSTRAINT FK_PRODVERVAL_PARAMETRO FOREIGN KEY (id_parametro_producto) REFERENCES prd.PARAMETRO_PRODUCTO_OPERATIVO (id_parametro_producto)
    );
END
GO

/* ---------------------------------------------------------------------------
   7. Listas de valores
   --------------------------------------------------------------------------- */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, 1, N'sistema' FROM (VALUES
  (N'CATEGORIA_PRODUCTO_CREDITO',  N'Categoria de producto de credito', N'Tipo de credito que ofrece la entidad (personal, hipotecario, vehicular...).'),
  (N'CATEGORIA_PRODUCTO_CUENTA',   N'Categoria de producto de cuenta', N'Tipo de cuenta que ofrece la entidad (ahorro, corriente, plazo...).'),
  (N'TIPO_CALCULO_PLAN_PAGO',      N'Tipo de calculo del plan de pagos', N'Metodo de amortizacion del credito.'),
  (N'FRECUENCIA_PAGO',             N'Frecuencia de pago', N'Cada cuanto vence una cuota.'),
  (N'TIPO_TASA',                   N'Tipo de tasa', N'Fija durante todo el plazo o variable (indicador + spread, con revision periodica).'),
  (N'BASE_COMPONENTE_ECONOMICO',   N'Base de calculo de un componente economico', N'Sobre que monto se aplica el porcentaje de una comision, seguro o cargo.'),
  (N'TIPO_INDICADOR_FINANCIERO',   N'Tipo de indicador financiero', N'Clase de indicador: tasa de referencia o tipo de cambio.'),
  (N'CONVENCION_AJUSTE_FECHA',     N'Convencion de ajuste de fecha', N'Que hacer cuando una fecha de pago cae en dia no habil.')
) x(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO t WHERE t.codigo = x.codigo);
GO

DECLARE @items TABLE (lista nvarchar(100), codigo nvarchar(100), nombre nvarchar(200), orden int, descripcion nvarchar(500));
INSERT INTO @items VALUES
 (N'CATEGORIA_PRODUCTO_CREDITO', N'PERSONAL',      N'Personal / consumo', 1, N'Credito de consumo a personas fisicas.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'HIPOTECARIO',   N'Hipotecario / vivienda', 2, N'Credito para adquirir o construir vivienda, con garantia hipotecaria.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'VEHICULAR',     N'Vehicular', 3, N'Credito para adquirir un vehiculo, normalmente con prenda sobre el bien.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'COMERCIAL',     N'Comercial / empresarial', 4, N'Credito para empresas y capital de trabajo.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'TARJETA',       N'Tarjeta de credito', 5, N'Linea revolvente asociada a una tarjeta.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'MICROCREDITO',  N'Microcredito', 6, N'Credito de monto reducido para microempresarios.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'AGROPECUARIO',  N'Agropecuario', 7, N'Credito para actividades agricolas y ganaderas.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'EDUCATIVO',     N'Educativo', 8, N'Credito para financiar estudios.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'LEASING',       N'Leasing / arrendamiento', 9, N'Arrendamiento financiero de bienes.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'FACTORING',     N'Factoring / descuento de documentos', 10, N'Compra o descuento de facturas y documentos por cobrar.'),
 (N'CATEGORIA_PRODUCTO_CREDITO', N'PDV',           N'Credito en punto de venta', 11, N'Financiamiento de una compra en el punto de venta.'),
 (N'CATEGORIA_PRODUCTO_CUENTA',  N'AHORRO',        N'Cuenta de ahorro', 1, N'Cuenta de captacion a la vista que genera intereses.'),
 (N'CATEGORIA_PRODUCTO_CUENTA',  N'CORRIENTE',     N'Cuenta corriente', 2, N'Cuenta transaccional, con chequera y sobregiro autorizado opcional.'),
 (N'CATEGORIA_PRODUCTO_CUENTA',  N'PLAZO',         N'Deposito a plazo (CDP)', 3, N'Deposito con plazo y vencimiento definidos.'),
 (N'CATEGORIA_PRODUCTO_CUENTA',  N'PLAN_AHORRO',   N'Plan de ahorro programado', 4, N'Ahorro con cuota periodica.'),
 (N'TIPO_CALCULO_PLAN_PAGO', N'NIVELADA',      N'Cuota nivelada (Frances)', 1, N'Cuota constante: al inicio paga mas interes y al final mas capital.'),
 (N'TIPO_CALCULO_PLAN_PAGO', N'ALEMAN',        N'Amortizacion constante (Aleman)', 2, N'Capital constante en cada cuota; la cuota baja con el tiempo porque el interes disminuye.'),
 (N'TIPO_CALCULO_PLAN_PAGO', N'AMERICANA',     N'Pago al vencimiento (Americana / bullet)', 3, N'Solo intereses durante el plazo y el capital completo en la ultima cuota.'),
 (N'TIPO_CALCULO_PLAN_PAGO', N'REVOLVENTE',    N'Revolvente', 4, N'Linea que se consume y se repone; el pago minimo depende del saldo utilizado.'),
 (N'TIPO_CALCULO_PLAN_PAGO', N'PERSONALIZADA', N'Personalizada', 5, N'No genera plan automatico: las cuotas se capturan manualmente mediante una gestion de aprobacion.'),
 (N'FRECUENCIA_PAGO', N'SEMANAL',        N'Semanal', 1, NULL),
 (N'FRECUENCIA_PAGO', N'QUINCENAL',      N'Quincenal', 2, NULL),
 (N'FRECUENCIA_PAGO', N'MENSUAL',        N'Mensual', 3, NULL),
 (N'FRECUENCIA_PAGO', N'BIMESTRAL',      N'Bimestral', 4, NULL),
 (N'FRECUENCIA_PAGO', N'TRIMESTRAL',     N'Trimestral', 5, NULL),
 (N'FRECUENCIA_PAGO', N'SEMESTRAL',      N'Semestral', 6, NULL),
 (N'FRECUENCIA_PAGO', N'ANUAL',          N'Anual', 7, NULL),
 (N'FRECUENCIA_PAGO', N'AL_VENCIMIENTO', N'Unico, al vencimiento', 8, NULL),
 (N'TIPO_TASA', N'FIJA',     N'Fija', 1, N'La tasa no cambia durante el tramo.'),
 (N'TIPO_TASA', N'VARIABLE', N'Variable', 2, N'Indicador de referencia mas spread, con revision periodica y piso/techo opcionales.'),
 (N'BASE_COMPONENTE_ECONOMICO', N'MONTO_DESEMBOLSADO', N'Monto desembolsado', 1, N'Total entregado al cliente en el desembolso.'),
 (N'BASE_COMPONENTE_ECONOMICO', N'MONTO_APROBADO',     N'Monto aprobado', 2, N'Monto autorizado del credito.'),
 (N'BASE_COMPONENTE_ECONOMICO', N'SALDO_CAPITAL',      N'Saldo del credito', 3, N'Capital pendiente total del credito.'),
 (N'BASE_COMPONENTE_ECONOMICO', N'SALDO_INSOLUTO',     N'Saldo insoluto', 4, N'Capital aun no vencido.'),
 (N'BASE_COMPONENTE_ECONOMICO', N'MONTO_CUOTA',        N'Monto de la cuota', 5, N'Capital mas intereses de la cuota.'),
 (N'BASE_COMPONENTE_ECONOMICO', N'MONTO_PAGO',         N'Monto del pago', 6, N'Importe del pago recibido.'),
 (N'BASE_COMPONENTE_ECONOMICO', N'CUOTA_VENCIDA',      N'Cuota vencida', 7, N'Capital vencido y no pagado (base de la mora).'),
 (N'TIPO_INDICADOR_FINANCIERO', N'TASA_REFERENCIA', N'Tasa de referencia', 1, N'Tasa publicada por una fuente externa (TBP, Prime, SOFR...). Base de las tasas variables.'),
 (N'TIPO_INDICADOR_FINANCIERO', N'TIPO_CAMBIO',     N'Tipo de cambio', 2, N'Reservado para unificar el tipo de cambio en esta misma estructura.'),
 (N'CONVENCION_AJUSTE_FECHA', N'SIN_AJUSTE',               N'Sin ajuste', 1, N'La fecha se mantiene aunque caiga en dia no habil.'),
 (N'CONVENCION_AJUSTE_FECHA', N'SIGUIENTE_HABIL',          N'Siguiente dia habil', 2, N'Si cae en dia no habil, pasa al siguiente dia habil.'),
 (N'CONVENCION_AJUSTE_FECHA', N'ANTERIOR_HABIL',           N'Dia habil anterior', 3, N'Si cae en dia no habil, se adelanta al dia habil anterior.'),
 (N'CONVENCION_AJUSTE_FECHA', N'SIGUIENTE_HABIL_MISMO_MES', N'Siguiente habil, sin cambiar de mes', 4, N'Pasa al siguiente dia habil; si eso cambia de mes, se usa el dia habil anterior.');

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = i.lista
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);

UPDATE x SET descripcion = i.descripcion
FROM gen.LISTA_VALORES_ITEM x JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = x.id_lista_valores_tipo
JOIN @items i ON i.lista = lt.codigo AND i.codigo = x.codigo
WHERE x.descripcion IS NULL AND i.descripcion IS NOT NULL;
GO

/* Indicadores de ejemplo (sin valores): el cliente los ajusta o agrega los suyos. */
INSERT INTO gen.INDICADOR_FINANCIERO (codigo, nombre, descripcion, id_tipo_indicador, moneda_codigo, frecuencia, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, it.id_lista_valores_item, x.moneda, x.frecuencia, N'sistema'
FROM (VALUES
  (N'TBP',   N'Tasa Basica Pasiva',   N'Referencia de captacion en colones (Banco Central de Costa Rica).', N'CRC', N'Semanal'),
  (N'PRIME', N'Prime Rate',           N'Tasa preferencial de bancos de Estados Unidos.', N'USD', N'Mensual'),
  (N'SOFR',  N'SOFR',                 N'Secured Overnight Financing Rate, referencia en dolares.', N'USD', N'Diaria')
) x(codigo, nombre, descripcion, moneda, frecuencia)
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'TIPO_INDICADOR_FINANCIERO'
JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_tipo = lt.id_lista_valores_tipo AND it.codigo = N'TASA_REFERENCIA'
WHERE NOT EXISTS (SELECT 1 FROM gen.INDICADOR_FINANCIERO i WHERE i.codigo = x.codigo);
GO

/* ---------------------------------------------------------------------------
   8. Tipo de gestion y permisos
      - PRD_VERSION_PRODUCTO: publicar una version de producto comercial (aprobacion obligatoria, 1 nivel).
      - PRD_INDICADOR_VALOR: cargar el valor de una tasa de referencia (misma via que el tipo de cambio).
   --------------------------------------------------------------------------- */
INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
SELECT x.codigo, x.nombre, N'Campos', 1, 1, N'Producto', N'sistema' FROM (VALUES
  (N'PRD_VERSION_PRODUCTO', N'Publicacion de version de producto comercial'),
  (N'PRD_INDICADOR_VALOR',  N'Carga de valor de tasa de referencia')
) x(codigo, nombre)
WHERE NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION t WHERE t.codigo = x.codigo);
GO

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, N'Pantalla', x.descripcion, N'sistema' FROM (VALUES
  (N'PRD_ADMIN',        N'Administrar productos operativos y comerciales', N'Crear y editar productos operativos, sus parametros, productos comerciales y borradores de version.'),
  (N'PRD_CONSULTA',     N'Consultar productos', N'Ver productos operativos y comerciales y sus versiones.'),
  (N'PRD_PUBLICAR',     N'Solicitar publicacion de version de producto', N'Enviar a aprobacion una version de producto comercial.'),
  (N'IND_ADMIN',        N'Administrar indicadores financieros y calendarios', N'Mantener tasas de referencia, calendarios, bases de calculo y categorias regulatorias.'),
  (N'IND_CARGA',        N'Cargar valores de tasas de referencia', N'Solicitar la carga del valor de un indicador financiero.')
) x(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);
GO

DECLARE @adm bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'PRD_ADMIN', N'PRD_CONSULTA', N'PRD_PUBLICAR', N'IND_ADMIN', N'IND_CARGA')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO r WHERE r.id_rol_autorizacion = @adm AND r.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

PRINT 'Ronda 42 (cimientos de Creditos) aplicada.';
GO
