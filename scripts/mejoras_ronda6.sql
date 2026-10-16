/* ============================================================================
   MEJORAS RONDA 6 - Motor de aprobacion parametrico (fase 1)
   ============================================================================
   1. Grupos de distribucion (sec.GRUPO_USUARIOS / GRUPO_USUARIOS_MIEMBRO): agrupacion de usuarios
      sin criterio organizativo. Sirven como aprobadores y, mas adelante, como destinatarios de notificaciones.
   2. Puesto del usuario (sec.USUARIO.puesto): variable disponible para las reglas.
   3. Reglas de aprobacion por tipo de gestion:
        apr.REGLA_APROBACION            regla (prioridad menor se evalua primero; gana la primera que cumple)
        apr.REGLA_APROBACION_CONDICION  condiciones (todas deben cumplirse): variable + operador + valor
        apr.REGLA_APROBACION_NIVEL      accion por nivel: USUARIO / GRUPO / SUPERIOR / AUTOMATICA
   4. apr.GESTION: id_regla, nivel_actual, niveles_requeridos.
      apr.GESTION_APROBACION: tipo_aprobador, id_usuario_asignado, id_grupo_asignado; estado 'Omitido'.
   5. Reglas iniciales de CLI_MODIFICACION_DATOS y TC_CARGA.
   Correr contra CoreBancario_DEV. Es repetible.
   ============================================================================ */
USE CoreBancario_DEV;
GO
SET NOCOUNT ON;
GO

/* ---- 1. Grupos de distribucion ---- */
IF OBJECT_ID(N'sec.GRUPO_USUARIOS') IS NULL
BEGIN
    CREATE TABLE sec.GRUPO_USUARIOS (
        id_grupo        bigint IDENTITY(1,1) NOT NULL,
        id_compania     bigint        NOT NULL,
        codigo          nvarchar(50)  NOT NULL,
        nombre          nvarchar(200) NOT NULL,
        descripcion     nvarchar(500) NULL,
        estado          nvarchar(20)  NOT NULL CONSTRAINT DF_GRUPO_USUARIOS_estado DEFAULT ('Activo'),
        creado_por      nvarchar(100) NOT NULL,
        creado_en       datetime2     NOT NULL CONSTRAINT DF_GRUPO_USUARIOS_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por  nvarchar(100) NULL,
        modificado_en   datetime2     NULL,
        CONSTRAINT PK_GRUPO_USUARIOS PRIMARY KEY (id_grupo),
        CONSTRAINT UQ_GRUPO_USUARIOS_codigo UNIQUE (id_compania, codigo),
        CONSTRAINT CK_GRUPO_USUARIOS_estado CHECK (estado IN ('Activo','Inactivo')),
        CONSTRAINT FK_GRUPO_USUARIOS_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
    );
END
GO
IF OBJECT_ID(N'sec.GRUPO_USUARIOS_MIEMBRO') IS NULL
BEGIN
    CREATE TABLE sec.GRUPO_USUARIOS_MIEMBRO (
        id_grupo    bigint        NOT NULL,
        id_usuario  bigint        NOT NULL,
        creado_por  nvarchar(100) NOT NULL,
        creado_en   datetime2     NOT NULL CONSTRAINT DF_GRUPO_MIEMBRO_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_GRUPO_USUARIOS_MIEMBRO PRIMARY KEY (id_grupo, id_usuario),
        CONSTRAINT FK_GRUPO_MIEMBRO_GRUPO FOREIGN KEY (id_grupo) REFERENCES sec.GRUPO_USUARIOS (id_grupo),
        CONSTRAINT FK_GRUPO_MIEMBRO_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario)
    );
END
GO

/* ---- 2. Puesto ---- */
IF COL_LENGTH('sec.USUARIO', 'puesto') IS NULL
    ALTER TABLE sec.USUARIO ADD puesto nvarchar(100) NULL;
GO

/* ---- 3. Reglas ---- */
IF OBJECT_ID(N'apr.REGLA_APROBACION') IS NULL
BEGIN
    CREATE TABLE apr.REGLA_APROBACION (
        id_regla         bigint IDENTITY(1,1) NOT NULL,
        id_compania      bigint        NOT NULL,
        id_tipo_gestion  bigint        NOT NULL,
        nombre           nvarchar(200) NOT NULL,
        prioridad        int           NOT NULL,
        estado           nvarchar(20)  NOT NULL CONSTRAINT DF_REGLA_APROB_estado DEFAULT ('Activo'),
        creado_por       nvarchar(100) NOT NULL,
        creado_en        datetime2     NOT NULL CONSTRAINT DF_REGLA_APROB_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por   nvarchar(100) NULL,
        modificado_en    datetime2     NULL,
        CONSTRAINT PK_REGLA_APROBACION PRIMARY KEY (id_regla),
        CONSTRAINT CK_REGLA_APROB_estado CHECK (estado IN ('Activo','Inactivo')),
        CONSTRAINT FK_REGLA_APROB_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_REGLA_APROB_TIPO FOREIGN KEY (id_tipo_gestion) REFERENCES apr.TIPO_GESTION (id_tipo_gestion)
    );
    CREATE INDEX IX_REGLA_APROB_tipo ON apr.REGLA_APROBACION (id_compania, id_tipo_gestion, prioridad);
END
GO
IF OBJECT_ID(N'apr.REGLA_APROBACION_CONDICION') IS NULL
BEGIN
    CREATE TABLE apr.REGLA_APROBACION_CONDICION (
        id_condicion  bigint IDENTITY(1,1) NOT NULL,
        id_regla      bigint        NOT NULL,
        variable      nvarchar(30)  NOT NULL,   -- CANAL, UNIDAD, USUARIO, PUESTO, TIPO_ACTOR (catalogo extensible en codigo)
        operador      nvarchar(10)  NOT NULL,   -- IGUAL, DIFERENTE, EN (lista separada por coma)
        valor         nvarchar(500) NOT NULL,
        CONSTRAINT PK_REGLA_APROB_CONDICION PRIMARY KEY (id_condicion),
        CONSTRAINT CK_REGLA_APROB_COND_operador CHECK (operador IN ('IGUAL','DIFERENTE','EN')),
        CONSTRAINT FK_REGLA_APROB_COND_REGLA FOREIGN KEY (id_regla) REFERENCES apr.REGLA_APROBACION (id_regla)
    );
END
GO
IF OBJECT_ID(N'apr.REGLA_APROBACION_NIVEL') IS NULL
BEGIN
    CREATE TABLE apr.REGLA_APROBACION_NIVEL (
        id_regla         bigint       NOT NULL,
        nivel            smallint     NOT NULL,
        tipo_aprobador   nvarchar(20) NOT NULL,   -- USUARIO / GRUPO / SUPERIOR / AUTOMATICA
        id_usuario       bigint       NULL,
        id_grupo         bigint       NULL,
        CONSTRAINT PK_REGLA_APROB_NIVEL PRIMARY KEY (id_regla, nivel),
        CONSTRAINT CK_REGLA_APROB_NIVEL_tipo CHECK (tipo_aprobador IN ('USUARIO','GRUPO','SUPERIOR','AUTOMATICA')),
        CONSTRAINT CK_REGLA_APROB_NIVEL_dest CHECK (
            (tipo_aprobador = 'USUARIO' AND id_usuario IS NOT NULL AND id_grupo IS NULL)
         OR (tipo_aprobador = 'GRUPO'   AND id_grupo IS NOT NULL AND id_usuario IS NULL)
         OR (tipo_aprobador IN ('SUPERIOR','AUTOMATICA') AND id_usuario IS NULL AND id_grupo IS NULL)),
        CONSTRAINT FK_REGLA_APROB_NIVEL_REGLA FOREIGN KEY (id_regla) REFERENCES apr.REGLA_APROBACION (id_regla),
        CONSTRAINT FK_REGLA_APROB_NIVEL_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario),
        CONSTRAINT FK_REGLA_APROB_NIVEL_GRUPO FOREIGN KEY (id_grupo) REFERENCES sec.GRUPO_USUARIOS (id_grupo)
    );
END
GO

/* ---- 4. Columnas del motor ---- */
IF COL_LENGTH('apr.GESTION', 'nivel_actual') IS NULL
    ALTER TABLE apr.GESTION ADD nivel_actual smallint NOT NULL CONSTRAINT DF_GESTION_nivel_actual DEFAULT (1);
GO
IF COL_LENGTH('apr.GESTION', 'niveles_requeridos') IS NULL
    ALTER TABLE apr.GESTION ADD niveles_requeridos smallint NOT NULL CONSTRAINT DF_GESTION_niveles_req DEFAULT (1);
GO
IF COL_LENGTH('apr.GESTION', 'id_regla') IS NULL
    ALTER TABLE apr.GESTION ADD id_regla bigint NULL CONSTRAINT FK_GESTION_REGLA FOREIGN KEY REFERENCES apr.REGLA_APROBACION (id_regla);
GO
IF COL_LENGTH('apr.GESTION_APROBACION', 'tipo_aprobador') IS NULL
    ALTER TABLE apr.GESTION_APROBACION ADD tipo_aprobador nvarchar(20) NULL;
GO
IF COL_LENGTH('apr.GESTION_APROBACION', 'id_usuario_asignado') IS NULL
    ALTER TABLE apr.GESTION_APROBACION ADD id_usuario_asignado bigint NULL;
GO
IF COL_LENGTH('apr.GESTION_APROBACION', 'id_grupo_asignado') IS NULL
    ALTER TABLE apr.GESTION_APROBACION ADD id_grupo_asignado bigint NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_GESTION_APROBACION_estado' AND parent_object_id = OBJECT_ID(N'apr.GESTION_APROBACION'))
    ALTER TABLE apr.GESTION_APROBACION DROP CONSTRAINT CK_GESTION_APROBACION_estado;
GO
ALTER TABLE apr.GESTION_APROBACION ADD CONSTRAINT CK_GESTION_APROBACION_estado
    CHECK (estado IN ('Pendiente','Aprobado','Rechazado','Devuelto','Omitido'));
GO

/* Gestiones existentes: un nivel, aprobador = superior del solicitante (comportamiento anterior). */
UPDATE apr.GESTION_APROBACION
   SET tipo_aprobador = 'SUPERIOR',
       id_usuario_asignado = (SELECT u.id_superior FROM apr.GESTION g JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
                              WHERE g.id_gestion = apr.GESTION_APROBACION.id_gestion)
 WHERE tipo_aprobador IS NULL;
GO

/* ---- 5. Reglas iniciales (solo si el tipo aun no tiene reglas) ---- */
DECLARE @tipo_cli bigint = (SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = N'CLI_MODIFICACION_DATOS');
DECLARE @tipo_tc  bigint = (SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = N'TC_CARGA');

IF @tipo_cli IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_cli)
BEGIN
    INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por)
    SELECT c.id_compania, @tipo_cli, N'Integracion por API: aprobacion automatica', 10, N'script ronda6' FROM gen.COMPANIA c;
    INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por)
    SELECT c.id_compania, @tipo_cli, N'Canal Sucursal: dos niveles de superiores', 20, N'script ronda6' FROM gen.COMPANIA c;
    INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por)
    SELECT c.id_compania, @tipo_cli, N'Por defecto: un nivel (superior)', 999, N'script ronda6' FROM gen.COMPANIA c;

    INSERT INTO apr.REGLA_APROBACION_CONDICION (id_regla, variable, operador, valor)
    SELECT id_regla, N'TIPO_ACTOR', N'IGUAL', N'INTEGRACION' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_cli AND prioridad = 10;
    INSERT INTO apr.REGLA_APROBACION_CONDICION (id_regla, variable, operador, valor)
    SELECT id_regla, N'CANAL', N'IGUAL', N'SUCURSAL' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_cli AND prioridad = 20;

    INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador)
    SELECT id_regla, 1, N'AUTOMATICA' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_cli AND prioridad = 10;
    INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador)
    SELECT id_regla, 1, N'SUPERIOR' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_cli AND prioridad IN (20, 999);
    INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador)
    SELECT id_regla, 2, N'SUPERIOR' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_cli AND prioridad = 20;
END
GO
DECLARE @tipo_tc2 bigint = (SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = N'TC_CARGA');
IF @tipo_tc2 IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_tc2)
BEGIN
    INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por)
    SELECT c.id_compania, @tipo_tc2, N'Por defecto: un nivel (superior)', 999, N'script ronda6' FROM gen.COMPANIA c;
    INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador)
    SELECT id_regla, 1, N'SUPERIOR' FROM apr.REGLA_APROBACION WHERE id_tipo_gestion = @tipo_tc2;
END
GO

SELECT tg.codigo, r.prioridad, r.nombre, COUNT(n.nivel) AS niveles
FROM apr.REGLA_APROBACION r
JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = r.id_tipo_gestion
LEFT JOIN apr.REGLA_APROBACION_NIVEL n ON n.id_regla = r.id_regla
GROUP BY tg.codigo, r.prioridad, r.nombre ORDER BY tg.codigo, r.prioridad;
GO
