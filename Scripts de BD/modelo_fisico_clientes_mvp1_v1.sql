/* ============================================================================
   CORE BANCARIO - MODELO FISICO MVP v1 - DOMINIO CLIENTES (INTERLOCUTORES)
   ============================================================================
   Alcance: unicamente el dominio Clientes/Interlocutores (DAD Seccion 12.1),
   segun ARQ-017 (MVP v1 acotado a Clientes) y CLI-021 (menores de edad y
   representacion legal).

   Motor de base de datos: SQL Server (ARQ-011). Compatible con SQL Server
   Express para desarrollo.

   Convenciones de nomenclatura: DAD Seccion 19 (ARQ-018).
     - Tablas: SCREAMING_SNAKE_CASE singular.
     - Campos: snake_case; PK/FK id_<entidad>; fechas fecha_<evento>;
       booleanos es_/tiene_/permite_; estado siempre en columna `estado`.
     - Indices: PK_<tabla>, FK_<origen>_<destino>, UQ_<tabla>_<campos>,
       IX_<tabla>_<campos>.
     - Auditoria estandar (ARQ-018, 19.2): creado_por, creado_en,
       modificado_por, modificado_en, correlation_id. NOTA DE RECONCILIACION:
       el documento narrativo original (ARQ-001) proponia fecha_creacion/
       usuario_creacion/fecha_ultima_modificacion/usuario_ultima_modificacion;
       se usa aqui la convencion mas reciente y formal de ARQ-018, que es la
       que rige "hacia adelante" segun quedo definido en la Seccion 19.
     - Llave sustituta: bigint identity (19.2), salvo excepcion justificada.

   Alcance de "corte vertical delgado" (ARQ-017): este script SOLO construye
   las tablas propias del dominio Clientes mas los catalogos minimos de los
   que depende directamente (LISTA_VALORES_TIPO/_ITEM, PAIS_REGULADOR,
   COMPANIA). Las piezas transversales mas amplias (GESTION/TIPO_GESTION,
   RBAC/USUARIO, LOG_AUDITORIA, DOCUMENTO/Gestor Documental, EVENTO_OPERATIVO)
   son referenciadas de forma logica (columnas id_gestion, documento_soporte,
   creado_por, etc. como datos sueltos) pero AUN NO se construyen en este
   script - quedan como el siguiente incremento del MVP v1, ya que son
   compartidas por todos los dominios y no exclusivas de Clientes.
   PRE-REQUISITO: ejecutar primero 00_crear_base_datos_cliente_cero.sql (crea
   la base de datos por cliente y los schemas gen/cli/sec/apr/aud, ARQ-019/020).
   ============================================================================ */

USE CoreBancario_CERO;
GO

SET NOCOUNT ON;
GO

/* ============================================================================
   1. CATALOGOS MINIMOS DE LOS QUE DEPENDE EL DOMINIO CLIENTES
   ============================================================================ */

-- Motor generico de catalogos tipo LOV (CAT-001, Documento narrativo 1.2)
CREATE TABLE gen.LISTA_VALORES_TIPO (
    id_lista_valores_tipo   bigint IDENTITY(1,1) NOT NULL,
    codigo                  nvarchar(100)        NOT NULL,
    nombre                  nvarchar(200)        NOT NULL,
    id_tipo_lista_padre     bigint               NULL,       -- listas dependientes (ej. futura Provincia->Canton)
    descripcion             nvarchar(500)        NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_LISTA_VALORES_TIPO_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_LISTA_VALORES_TIPO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_LISTA_VALORES_TIPO PRIMARY KEY (id_lista_valores_tipo),
    CONSTRAINT UQ_LISTA_VALORES_TIPO_codigo UNIQUE (codigo),
    CONSTRAINT FK_LISTA_VALORES_TIPO_LISTA_VALORES_TIPO FOREIGN KEY (id_tipo_lista_padre) REFERENCES gen.LISTA_VALORES_TIPO (id_lista_valores_tipo)
);
GO

CREATE TABLE gen.LISTA_VALORES_ITEM (
    id_lista_valores_item   bigint IDENTITY(1,1) NOT NULL,
    id_lista_valores_tipo   bigint               NOT NULL,
    codigo                  nvarchar(100)        NOT NULL,
    nombre                  nvarchar(200)        NOT NULL,
    orden                   int                  NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_LISTA_VALORES_ITEM_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_LISTA_VALORES_ITEM_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_LISTA_VALORES_ITEM PRIMARY KEY (id_lista_valores_item),
    CONSTRAINT UQ_LISTA_VALORES_ITEM_tipo_codigo UNIQUE (id_lista_valores_tipo, codigo),
    CONSTRAINT FK_LISTA_VALORES_ITEM_LISTA_VALORES_TIPO FOREIGN KEY (id_lista_valores_tipo) REFERENCES gen.LISTA_VALORES_TIPO (id_lista_valores_tipo)
);
GO

-- Paises/reguladores objetivo (GEN-018: Costa Rica y Panama). Tabla propia
-- (no LOV generico) porque necesita campos propios (mayoria_edad_anios,
-- moneda) usados directamente en reglas de negocio (CLI-021).
CREATE TABLE gen.PAIS_REGULADOR (
    id_pais_regulador       bigint IDENTITY(1,1) NOT NULL,
    codigo_iso_pais         nvarchar(3)          NOT NULL,
    nombre_pais             nvarchar(100)        NOT NULL,
    nombre_regulador        nvarchar(200)        NOT NULL,
    mayoria_edad_anios      smallint             NOT NULL CONSTRAINT DF_PAIS_REGULADOR_mayoria_edad DEFAULT (18),
    moneda_local            nvarchar(3)          NOT NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_PAIS_REGULADOR_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_PAIS_REGULADOR_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_PAIS_REGULADOR PRIMARY KEY (id_pais_regulador),
    CONSTRAINT UQ_PAIS_REGULADOR_codigo_iso UNIQUE (codigo_iso_pais)
);
GO

-- Multi-compania (Nivel 2, ARQ-019 / MCS-001): aislamiento logico dentro de
-- un mismo cliente SaaS (ver tambien CLIENTE_SAAS en la BD Directorio,
-- ARQ-020, que vive fuera de esta base de datos por tenant).
CREATE TABLE gen.COMPANIA (
    id_compania             bigint IDENTITY(1,1) NOT NULL,
    codigo_compania         nvarchar(20)         NOT NULL,
    nombre_compania         nvarchar(200)        NOT NULL,
    id_pais_regulador       bigint               NOT NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_COMPANIA_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_COMPANIA_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_COMPANIA PRIMARY KEY (id_compania),
    CONSTRAINT UQ_COMPANIA_codigo UNIQUE (codigo_compania),
    CONSTRAINT FK_COMPANIA_PAIS_REGULADOR FOREIGN KEY (id_pais_regulador) REFERENCES gen.PAIS_REGULADOR (id_pais_regulador)
);
GO

/* ============================================================================
   2. INTERLOCUTOR (supertipo) y subtipos 1:1
   ============================================================================ */

CREATE TABLE cli.INTERLOCUTOR (
    id_interlocutor                bigint IDENTITY(1,1) NOT NULL,
    id_compania                    bigint               NOT NULL,
    tipo_persona                   nvarchar(10)         NOT NULL,   -- Fisica / Juridica / Grupo (discriminador estructural)
    id_tipo_identificacion         bigint               NOT NULL,   -- FK a LISTA_VALORES_ITEM (TIPO_IDENTIFICACION_FISICA o _JURIDICA segun tipo_persona)
    numero_identificacion          nvarchar(50)         NOT NULL,
    id_pais_regulador              bigint               NOT NULL,   -- pais de registro/identificacion
    estado                         nvarchar(20)         NOT NULL CONSTRAINT DF_INTERLOCUTOR_estado DEFAULT ('Activo'),  -- Activo / Fusionado / Fallecido
    id_interlocutor_sobreviviente  bigint               NULL,       -- CLI-020: fusion de duplicados, auto-referencia
    fecha_fallecimiento            datetime2            NULL,
    fecha_ultima_actualizacion_kyc datetime2            NULL,
    fecha_proxima_actualizacion_kyc datetime2           NULL,
    correlation_id                 uniqueidentifier     NULL,
    creado_por                     nvarchar(100)        NOT NULL,
    creado_en                      datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por                 nvarchar(100)        NULL,
    modificado_en                  datetime2            NULL,
    CONSTRAINT PK_INTERLOCUTOR PRIMARY KEY (id_interlocutor),
    CONSTRAINT CK_INTERLOCUTOR_tipo_persona CHECK (tipo_persona IN ('Fisica','Juridica','Grupo')),
    CONSTRAINT CK_INTERLOCUTOR_estado CHECK (estado IN ('Activo','Fusionado','Fallecido')),
    -- CLI-020: detección determinística de duplicados (tipo_identificacion + numero_identificacion + pais), por compañía
    CONSTRAINT UQ_INTERLOCUTOR_identificacion UNIQUE (id_compania, id_tipo_identificacion, numero_identificacion, id_pais_regulador),
    CONSTRAINT FK_INTERLOCUTOR_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_INTERLOCUTOR_LISTA_VALORES_ITEM FOREIGN KEY (id_tipo_identificacion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
    CONSTRAINT FK_INTERLOCUTOR_PAIS_REGULADOR FOREIGN KEY (id_pais_regulador) REFERENCES gen.PAIS_REGULADOR (id_pais_regulador),
    CONSTRAINT FK_INTERLOCUTOR_INTERLOCUTOR FOREIGN KEY (id_interlocutor_sobreviviente) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
);
GO
CREATE INDEX IX_INTERLOCUTOR_compania_estado ON cli.INTERLOCUTOR (id_compania, estado);
GO

-- Subtipo Persona Fisica (Documento narrativo 3.1: nombre1/nombre2/apellido1/apellido2)
CREATE TABLE cli.INTERLOCUTOR_FISICA (
    id_interlocutor         bigint          NOT NULL,
    nombre1                 nvarchar(100)   NOT NULL,
    nombre2                 nvarchar(100)   NULL,
    apellido1               nvarchar(100)   NOT NULL,
    apellido2               nvarchar(100)   NULL,
    fecha_nacimiento        datetime2       NOT NULL,   -- base del calculo de es_menor_edad (CLI-021)
    CONSTRAINT PK_INTERLOCUTOR_FISICA PRIMARY KEY (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_FISICA_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
);
GO

-- Subtipo Persona Juridica (Documento narrativo 3.1: nombre_org1/nombre_org2)
CREATE TABLE cli.INTERLOCUTOR_JURIDICA (
    id_interlocutor         bigint          NOT NULL,
    nombre_org1             nvarchar(200)   NOT NULL,   -- razon social
    nombre_org2             nvarchar(200)   NULL,       -- nombre comercial
    fecha_constitucion      datetime2       NULL,
    CONSTRAINT PK_INTERLOCUTOR_JURIDICA PRIMARY KEY (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_JURIDICA_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
);
GO

-- Subtipo Grupo economico (Documento narrativo 3.1: nombre_grupo1/nombre_grupo2)
CREATE TABLE cli.INTERLOCUTOR_GRUPO (
    id_interlocutor         bigint          NOT NULL,
    nombre_grupo1           nvarchar(200)   NOT NULL,
    nombre_grupo2           nvarchar(200)   NULL,
    CONSTRAINT PK_INTERLOCUTOR_GRUPO PRIMARY KEY (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_GRUPO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
);
GO

/* ============================================================================
   3. ONBOARDING: PROSPECTO Y ROLES
   ============================================================================ */

CREATE TABLE cli.INTERLOCUTOR_PROSPECTO (
    id_interlocutor_prospecto  bigint IDENTITY(1,1) NOT NULL,
    id_compania                bigint               NOT NULL,
    id_interlocutor            bigint               NULL,   -- se llena al convertirse (upgrade) en INTERLOCUTOR formal
    tipo_persona                nvarchar(10)        NOT NULL,
    nombre_contacto             nvarchar(200)       NOT NULL,
    id_tipo_identificacion      bigint              NULL,
    numero_identificacion       nvarchar(50)        NULL,
    telefono_contacto           nvarchar(30)        NULL,
    correo_contacto             nvarchar(200)       NULL,
    canal_origen                nvarchar(50)        NULL,
    estado                      nvarchar(20)        NOT NULL CONSTRAINT DF_INTERLOCUTOR_PROSPECTO_estado DEFAULT ('Prospecto'),  -- Prospecto / Convertido / Descartado
    fecha_registro               datetime2          NOT NULL CONSTRAINT DF_INTERLOCUTOR_PROSPECTO_fecha_registro DEFAULT (SYSUTCDATETIME()),
    fecha_conversion              datetime2         NULL,
    creado_por                   nvarchar(100)      NOT NULL,
    creado_en                    datetime2          NOT NULL CONSTRAINT DF_INTERLOCUTOR_PROSPECTO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por                nvarchar(100)     NULL,
    modificado_en                 datetime2         NULL,
    CONSTRAINT PK_INTERLOCUTOR_PROSPECTO PRIMARY KEY (id_interlocutor_prospecto),
    CONSTRAINT CK_INTERLOCUTOR_PROSPECTO_estado CHECK (estado IN ('Prospecto','Convertido','Descartado')),
    CONSTRAINT FK_INTERLOCUTOR_PROSPECTO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_INTERLOCUTOR_PROSPECTO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_PROSPECTO_LISTA_VALORES_ITEM FOREIGN KEY (id_tipo_identificacion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

CREATE TABLE cli.INTERLOCUTOR_ROL (
    id_interlocutor_rol     bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor         bigint               NOT NULL,
    id_tipo_rol             bigint               NOT NULL,   -- FK a LISTA_VALORES_ITEM, categoria TIPO_ROL_INTERLOCUTOR (Cliente Cuentas/Cliente Creditos/Fiador/Codeudor/Beneficiario/Firmante/Autorizado)
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_INTERLOCUTOR_ROL_estado DEFAULT ('Activo'),  -- Activo / Inactivo
    id_nivel_riesgo         bigint               NULL,       -- FK a LISTA_VALORES_ITEM, categoria NIVEL_RIESGO_AML
    fecha_asignacion        datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_ROL_fecha_asignacion DEFAULT (SYSUTCDATETIME()),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_ROL_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_INTERLOCUTOR_ROL PRIMARY KEY (id_interlocutor_rol),
    CONSTRAINT CK_INTERLOCUTOR_ROL_estado CHECK (estado IN ('Activo','Inactivo')),
    CONSTRAINT UQ_INTERLOCUTOR_ROL_interlocutor_tipo UNIQUE (id_interlocutor, id_tipo_rol),
    CONSTRAINT FK_INTERLOCUTOR_ROL_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_ROL_LISTA_VALORES_ITEM_tipo FOREIGN KEY (id_tipo_rol) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
    CONSTRAINT FK_INTERLOCUTOR_ROL_LISTA_VALORES_ITEM_riesgo FOREIGN KEY (id_nivel_riesgo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

/* ============================================================================
   4. IDENTIFICACION EXTERNA, CUMPLIMIENTO, RELACIONES, BLOQUEOS Y AFINES
   ============================================================================ */

CREATE TABLE cli.INTERLOCUTOR_ID_EXTERNO (
    id_interlocutor_id_externo bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor            bigint               NOT NULL,
    id_sistema_externo         bigint               NOT NULL,  -- FK a LISTA_VALORES_ITEM, categoria SISTEMA_EXTERNO
    numero_id_externo          nvarchar(50)         NOT NULL,
    vigencia_desde             datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_ID_EXTERNO_vigencia_desde DEFAULT (SYSUTCDATETIME()),
    vigencia_hasta             datetime2            NULL,      -- usado tambien para historico de identificacion (CLI-020)
    creado_por                 nvarchar(100)        NOT NULL,
    creado_en                  datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_ID_EXTERNO_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_INTERLOCUTOR_ID_EXTERNO PRIMARY KEY (id_interlocutor_id_externo),
    CONSTRAINT UQ_INTERLOCUTOR_ID_EXTERNO_sistema_numero UNIQUE (id_sistema_externo, numero_id_externo),
    CONSTRAINT FK_INTERLOCUTOR_ID_EXTERNO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_ID_EXTERNO_LISTA_VALORES_ITEM FOREIGN KEY (id_sistema_externo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

CREATE TABLE cli.INTERLOCUTOR_CUMPLIMIENTO (
    id_interlocutor_cumplimiento bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor              bigint               NOT NULL,
    es_pep                       bit                  NOT NULL CONSTRAINT DF_INTERLOCUTOR_CUMPLIMIENTO_es_pep DEFAULT (0),
    id_nivel_riesgo              bigint               NOT NULL,  -- FK a LISTA_VALORES_ITEM, categoria NIVEL_RIESGO_AML
    resultado_screening          nvarchar(20)         NOT NULL,  -- Aprobado / Pendiente / Rechazado (screening simulado en MVP v1, ARQ-017)
    fecha_screening              datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_CUMPLIMIENTO_fecha_screening DEFAULT (SYSUTCDATETIME()),
    fecha_proxima_revision       datetime2            NULL,
    creado_por                   nvarchar(100)        NOT NULL,
    creado_en                    datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_CUMPLIMIENTO_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_INTERLOCUTOR_CUMPLIMIENTO PRIMARY KEY (id_interlocutor_cumplimiento),
    CONSTRAINT CK_INTERLOCUTOR_CUMPLIMIENTO_resultado CHECK (resultado_screening IN ('Aprobado','Pendiente','Rechazado')),
    CONSTRAINT FK_INTERLOCUTOR_CUMPLIMIENTO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_CUMPLIMIENTO_LISTA_VALORES_ITEM FOREIGN KEY (id_nivel_riesgo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO
CREATE INDEX IX_INTERLOCUTOR_CUMPLIMIENTO_interlocutor ON cli.INTERLOCUTOR_CUMPLIMIENTO (id_interlocutor, fecha_screening DESC);
GO

CREATE TABLE cli.INTERLOCUTOR_RELACION (
    id_interlocutor_relacion    bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor_origen      bigint               NOT NULL,
    id_interlocutor_destino     bigint               NOT NULL,
    id_tipo_relacion            bigint               NOT NULL,  -- FK a LISTA_VALORES_ITEM, categoria TIPO_RELACION_INTERLOCUTOR
    porcentaje_participacion    decimal(5,2)         NULL,
    fecha_inicio                datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_RELACION_fecha_inicio DEFAULT (SYSUTCDATETIME()),
    fecha_fin                   datetime2            NULL,
    CONSTRAINT PK_INTERLOCUTOR_RELACION PRIMARY KEY (id_interlocutor_relacion),
    CONSTRAINT CK_INTERLOCUTOR_RELACION_no_autorelacion CHECK (id_interlocutor_origen <> id_interlocutor_destino),
    CONSTRAINT FK_INTERLOCUTOR_RELACION_INTERLOCUTOR_origen FOREIGN KEY (id_interlocutor_origen) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_RELACION_INTERLOCUTOR_destino FOREIGN KEY (id_interlocutor_destino) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_RELACION_LISTA_VALORES_ITEM FOREIGN KEY (id_tipo_relacion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

CREATE TABLE cli.INTERLOCUTOR_BLOQUEO (
    id_interlocutor_bloqueo  bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor          bigint               NOT NULL,
    id_tipo_bloqueo          bigint               NOT NULL,   -- FK a LISTA_VALORES_ITEM, categoria TIPO_BLOQUEO
    motivo                   nvarchar(500)        NOT NULL,
    fecha_inicio             datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_BLOQUEO_fecha_inicio DEFAULT (SYSUTCDATETIME()),
    fecha_fin                datetime2            NULL,
    estado                   nvarchar(20)         NOT NULL CONSTRAINT DF_INTERLOCUTOR_BLOQUEO_estado DEFAULT ('Activo'),  -- Activo / Vencido / Eliminado
    id_gestion               bigint               NULL,   -- referencia logica a GESTION (transversal, pendiente de construir)
    documento_soporte        nvarchar(500)        NULL,   -- referencia logica al Gestor Documental (transversal, pendiente de construir)
    creado_por               nvarchar(100)        NOT NULL,
    creado_en                datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_BLOQUEO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por           nvarchar(100)        NULL,
    modificado_en            datetime2            NULL,
    CONSTRAINT PK_INTERLOCUTOR_BLOQUEO PRIMARY KEY (id_interlocutor_bloqueo),
    CONSTRAINT CK_INTERLOCUTOR_BLOQUEO_estado CHECK (estado IN ('Activo','Vencido','Eliminado')),
    CONSTRAINT FK_INTERLOCUTOR_BLOQUEO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_BLOQUEO_LISTA_VALORES_ITEM FOREIGN KEY (id_tipo_bloqueo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO
CREATE INDEX IX_INTERLOCUTOR_BLOQUEO_interlocutor_estado ON cli.INTERLOCUTOR_BLOQUEO (id_interlocutor, estado);
GO

CREATE TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA (
    id_interlocutor_cuenta_externa bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor                bigint               NOT NULL,
    entidad_externa                nvarchar(200)        NOT NULL,
    numero_cuenta_externa          nvarchar(50)         NOT NULL,
    id_tipo_cuenta                 bigint               NOT NULL,  -- FK a LISTA_VALORES_ITEM, categoria TIPO_CUENTA_EXTERNA
    estado                         nvarchar(20)         NOT NULL CONSTRAINT DF_INTERLOCUTOR_CUENTA_EXTERNA_estado DEFAULT ('Activo'),
    creado_por                     nvarchar(100)        NOT NULL,
    creado_en                      datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_CUENTA_EXTERNA_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_INTERLOCUTOR_CUENTA_EXTERNA PRIMARY KEY (id_interlocutor_cuenta_externa),
    CONSTRAINT FK_INTERLOCUTOR_CUENTA_EXTERNA_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_CUENTA_EXTERNA_LISTA_VALORES_ITEM FOREIGN KEY (id_tipo_cuenta) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

CREATE TABLE cli.INTERLOCUTOR_SEGMENTO (
    id_interlocutor_segmento   bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor            bigint               NOT NULL,
    id_dimension_segmento      bigint               NOT NULL,  -- FK a LISTA_VALORES_TIPO (dimension: Valor / Ciclo de vida / Comportamiento)
    id_segmento                bigint               NOT NULL,  -- FK a LISTA_VALORES_ITEM (valor del segmento dentro de esa dimension)
    fecha_asignacion           datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_SEGMENTO_fecha_asignacion DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_INTERLOCUTOR_SEGMENTO PRIMARY KEY (id_interlocutor_segmento),
    CONSTRAINT UQ_INTERLOCUTOR_SEGMENTO_interlocutor_dimension UNIQUE (id_interlocutor, id_dimension_segmento),
    CONSTRAINT FK_INTERLOCUTOR_SEGMENTO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_SEGMENTO_LISTA_VALORES_TIPO FOREIGN KEY (id_dimension_segmento) REFERENCES gen.LISTA_VALORES_TIPO (id_lista_valores_tipo),
    CONSTRAINT FK_INTERLOCUTOR_SEGMENTO_LISTA_VALORES_ITEM FOREIGN KEY (id_segmento) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

CREATE TABLE cli.INTERLOCUTOR_CONSENTIMIENTO (
    id_interlocutor_consentimiento bigint IDENTITY(1,1) NOT NULL,
    id_interlocutor                bigint               NOT NULL,
    id_tipo_consentimiento         bigint               NOT NULL,  -- FK a LISTA_VALORES_ITEM, categoria TIPO_CONSENTIMIENTO
    otorgado                       bit                  NOT NULL,
    fecha_otorgamiento             datetime2            NULL,
    fecha_revocacion               datetime2            NULL,
    canal                          nvarchar(50)         NULL,
    CONSTRAINT PK_INTERLOCUTOR_CONSENTIMIENTO PRIMARY KEY (id_interlocutor_consentimiento),
    CONSTRAINT FK_INTERLOCUTOR_CONSENTIMIENTO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_INTERLOCUTOR_CONSENTIMIENTO_LISTA_VALORES_ITEM FOREIGN KEY (id_tipo_consentimiento) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
);
GO

/* ============================================================================
   5. REPRESENTACION LEGAL (CLI-021 - menores de edad)
   ============================================================================ */

CREATE TABLE cli.REPRESENTACION_LEGAL (
    id_representacion_legal  bigint IDENTITY(1,1) NOT NULL,
    id_representante         bigint               NOT NULL,   -- FK a INTERLOCUTOR, el apoderado/responsable legal
    id_representado          bigint               NOT NULL,   -- FK a INTERLOCUTOR, el menor de edad
    fecha_inicio             datetime2            NOT NULL CONSTRAINT DF_REPRESENTACION_LEGAL_fecha_inicio DEFAULT (SYSUTCDATETIME()),
    fecha_fin                datetime2            NULL,       -- nulo mientras esta vigente
    documento_soporte        nvarchar(500)        NULL,       -- referencia logica al Gestor Documental (partida de nacimiento, resolucion judicial)
    creado_por               nvarchar(100)        NOT NULL,
    creado_en                datetime2            NOT NULL CONSTRAINT DF_REPRESENTACION_LEGAL_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por           nvarchar(100)        NULL,
    modificado_en            datetime2            NULL,
    CONSTRAINT PK_REPRESENTACION_LEGAL PRIMARY KEY (id_representacion_legal),
    CONSTRAINT CK_REPRESENTACION_LEGAL_no_autorepresentacion CHECK (id_representante <> id_representado),
    CONSTRAINT FK_REPRESENTACION_LEGAL_INTERLOCUTOR_representante FOREIGN KEY (id_representante) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
    CONSTRAINT FK_REPRESENTACION_LEGAL_INTERLOCUTOR_representado FOREIGN KEY (id_representado) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
);
GO
CREATE INDEX IX_REPRESENTACION_LEGAL_representado_vigente ON cli.REPRESENTACION_LEGAL (id_representado, fecha_fin);
GO

/* ============================================================================
   6. VISTA DE APOYO: es_menor_edad
   ----------------------------------------------------------------------------
   NOTA DE DISENO: es_menor_edad NO se implementa como columna calculada
   persistida en INTERLOCUTOR porque SQL Server no permite que una columna
   calculada referencie otra tabla (fecha_nacimiento vive en
   INTERLOCUTOR_FISICA, mayoria_edad_anios vive en PAIS_REGULADOR). Se
   resuelve en tiempo de consulta con esta vista, evitando que el dato quede
   desactualizado con el paso del tiempo (tal como exige CLI-021). A la
   escala del MVP v1 esto es suficiente; si el volumen lo exige mas adelante,
   se puede materializar en un job de refresco periodico sin cambiar el
   contrato de la vista.
   ============================================================================ */

CREATE VIEW cli.V_INTERLOCUTOR_ES_MENOR_EDAD AS
SELECT
    f.id_interlocutor,
    f.fecha_nacimiento,
    pr.mayoria_edad_anios,
    CASE
        WHEN DATEDIFF(YEAR, f.fecha_nacimiento, SYSUTCDATETIME())
             - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, f.fecha_nacimiento, SYSUTCDATETIME()), f.fecha_nacimiento) > SYSUTCDATETIME()
                    THEN 1 ELSE 0 END
             < pr.mayoria_edad_anios
        THEN CAST(1 AS bit)
        ELSE CAST(0 AS bit)
    END AS es_menor_edad
FROM cli.INTERLOCUTOR_FISICA f
INNER JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = f.id_interlocutor
INNER JOIN gen.PAIS_REGULADOR pr ON pr.id_pais_regulador = i.id_pais_regulador;
GO

/* ============================================================================
   FIN DEL SCRIPT - MVP v1 / Dominio Clientes
   ============================================================================ */
