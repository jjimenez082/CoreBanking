/* ============================================================================
   Ronda 40 - Perfiles de datos y enmascaramiento. Re-ejecutable.

   Concepto: el ROL decide que pantallas y acciones tiene un usuario; el PERFIL DE DATOS decide
   QUE REGISTROS ve (filas) y COMO ve cada campo sensible (mascara).

   - Rol EMPLEADO (solo personas) con sus datos: unidad (sucursal o departamento de la estructura organizativa) y numero de empleado
     (cli.INTERLOCUTOR_ROL_EMPLEADO). El atributo de filas «Es empleado» de los perfiles = tener ese rol activo.
   - sec.MASCARA: catalogo parametrizable de mascaras (primeros N, ultimos M, caracter; y tipos especiales).
   - sec.PERFIL_DATOS: perfil; sec.PERFIL_DATOS_FILTRO: reglas de filas (incluir / excluir valores de un atributo);
     sec.PERFIL_DATOS_CAMPO: mascara que el perfil aplica a un campo sensible (sin fila = el perfil lo ve completo).
   - sec.USUARIO_PERFIL_DATOS: perfiles de cada usuario (una integracion actua con los de su usuario tecnico).
     Sin perfiles = sin restriccion. Varios perfiles: filas = UNION; campos = gana la mascara menos restrictiva.
   - aud.LOG_CONSULTA_DATO_SENSIBLE: bitacora de revelados («Ver» un dato enmascarado).
   - Permiso SEG_PERFILES (Administracion > Perfiles de datos y Mascaras) para ADMINISTRADOR_SEGURIDAD.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Rol EMPLEADO y sus datos propios (unidad y numero de empleado). */
DECLARE @tipoRol bigint = (SELECT id_lista_valores_tipo FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_ROL_INTERLOCUTOR');
IF @tipoRol IS NULL RAISERROR(N'No existe la lista TIPO_ROL_INTERLOCUTOR.', 16, 1);
ELSE IF NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @tipoRol AND codigo = N'EMPLEADO')
    INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, descripcion, orden, es_sistema, creado_por)
    VALUES (@tipoRol, N'EMPLEADO', N'Empleado',
            N'Persona que trabaja para la institución. Se registra con su unidad (sucursal o departamento) y su número de empleado.',
            ISNULL((SELECT MAX(orden) FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @tipoRol), 0) + 1, 1, N'sistema');
GO

IF OBJECT_ID(N'cli.INTERLOCUTOR_ROL_EMPLEADO', N'U') IS NULL
CREATE TABLE cli.INTERLOCUTOR_ROL_EMPLEADO (
    id_interlocutor_rol  bigint        NOT NULL,
    id_compania          bigint        NOT NULL,
    id_unidad            bigint        NOT NULL,     -- sec.DEPARTAMENTO (sucursal, agencia, departamento...)
    numero_empleado      nvarchar(30)  NOT NULL,
    estado               nvarchar(10)  NOT NULL CONSTRAINT DF_ROLEMP_estado DEFAULT (N'Activo'),
    creado_por           nvarchar(100) NOT NULL,
    creado_en            datetime2     NOT NULL CONSTRAINT DF_ROLEMP_creado DEFAULT (SYSUTCDATETIME()),
    modificado_por       nvarchar(100) NULL,
    modificado_en        datetime2     NULL,
    CONSTRAINT PK_INTERLOCUTOR_ROL_EMPLEADO PRIMARY KEY (id_interlocutor_rol),
    CONSTRAINT CK_ROLEMP_estado CHECK (estado IN (N'Activo', N'Inactivo')),
    CONSTRAINT FK_ROLEMP_ROL FOREIGN KEY (id_interlocutor_rol) REFERENCES cli.INTERLOCUTOR_ROL (id_interlocutor_rol),
    CONSTRAINT FK_ROLEMP_UNIDAD FOREIGN KEY (id_unidad) REFERENCES sec.DEPARTAMENTO (id_departamento)
);
GO
/* Un numero de empleado activo no se repite dentro de la compania. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ROLEMP_numero' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_ROL_EMPLEADO'))
    CREATE UNIQUE INDEX UX_ROLEMP_numero ON cli.INTERLOCUTOR_ROL_EMPLEADO (id_compania, numero_empleado) WHERE estado = N'Activo';
GO

/* 2. Mascaras. tipo: PARCIAL (primeros/ultimos), CORREO, RANGO (montos), ANIO (fechas), OCULTO. */
IF OBJECT_ID(N'sec.MASCARA', N'U') IS NULL
CREATE TABLE sec.MASCARA (
    id_mascara       bigint IDENTITY(1,1) NOT NULL,
    id_compania      bigint        NOT NULL,
    codigo           nvarchar(50)  NOT NULL,
    nombre           nvarchar(100) NOT NULL,
    descripcion      nvarchar(300) NULL,
    tipo             nvarchar(10)  NOT NULL,
    visibles_inicio  int           NOT NULL CONSTRAINT DF_MASCARA_ini DEFAULT (0),
    visibles_fin     int           NOT NULL CONSTRAINT DF_MASCARA_fin DEFAULT (0),
    caracter         nchar(1)      NOT NULL CONSTRAINT DF_MASCARA_car DEFAULT (N'*'),
    es_sistema       bit           NOT NULL CONSTRAINT DF_MASCARA_sis DEFAULT (0),
    estado           nvarchar(10)  NOT NULL CONSTRAINT DF_MASCARA_estado DEFAULT (N'Activo'),
    creado_por       nvarchar(100) NOT NULL,
    creado_en        datetime2     NOT NULL CONSTRAINT DF_MASCARA_creado DEFAULT (SYSUTCDATETIME()),
    modificado_por   nvarchar(100) NULL,
    modificado_en    datetime2     NULL,
    CONSTRAINT PK_MASCARA PRIMARY KEY (id_mascara),
    CONSTRAINT UQ_MASCARA_codigo UNIQUE (id_compania, codigo),
    CONSTRAINT CK_MASCARA_tipo CHECK (tipo IN (N'PARCIAL', N'CORREO', N'RANGO', N'ANIO', N'OCULTO')),
    CONSTRAINT CK_MASCARA_estado CHECK (estado IN (N'Activo', N'Inactivo')),
    CONSTRAINT CK_MASCARA_visibles CHECK (visibles_inicio BETWEEN 0 AND 20 AND visibles_fin BETWEEN 0 AND 20),
    CONSTRAINT FK_MASCARA_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
);
GO

/* Siembra por compania (solo si faltan). */
DECLARE @sem TABLE (codigo nvarchar(50), nombre nvarchar(100), descripcion nvarchar(300), tipo nvarchar(10), ini int, fin int);
INSERT INTO @sem VALUES
 (N'TODO_ASTERISCOS', N'Todo con asteriscos',         N'Reemplaza todos los caracteres: ********', N'PARCIAL', 0, 0),
 (N'ULTIMOS_4',       N'Solo los últimos 4',          N'Muestra los últimos 4 caracteres: ******5678', N'PARCIAL', 0, 4),
 (N'TARJETA',         N'Tarjeta (primero y últimos 3)', N'Muestra el primer carácter y los últimos 3: 4***-****-****-*123', N'PARCIAL', 1, 3),
 (N'PRIMEROS_2_ULTIMOS_2', N'Primeros 2 y últimos 2', N'Muestra 2 al inicio y 2 al final: 40*****84', N'PARCIAL', 2, 2),
 (N'CORREO_PARCIAL',  N'Correo parcial',              N'Primera letra del usuario y el dominio: j***@dominio.com', N'CORREO', 0, 0),
 (N'RANGO_MONTO',     N'Monto en rango',              N'Muestra un rango por orden de magnitud en vez del monto exacto: 10 000 – 99 999', N'RANGO', 0, 0),
 (N'SOLO_ANIO',       N'Solo el año',                 N'Muestra únicamente el año de una fecha: 1985', N'ANIO', 0, 0),
 (N'OCULTO',          N'Oculto',                      N'No muestra nada del dato: ••••', N'OCULTO', 0, 0);
INSERT INTO sec.MASCARA (id_compania, codigo, nombre, descripcion, tipo, visibles_inicio, visibles_fin, es_sistema, creado_por)
SELECT c.id_compania, s.codigo, s.nombre, s.descripcion, s.tipo, s.ini, s.fin, 1, N'sistema'
FROM gen.COMPANIA c CROSS JOIN @sem s
WHERE NOT EXISTS (SELECT 1 FROM sec.MASCARA m WHERE m.id_compania = c.id_compania AND m.codigo = s.codigo);
GO

/* 3. Perfiles de datos. */
IF OBJECT_ID(N'sec.PERFIL_DATOS', N'U') IS NULL
CREATE TABLE sec.PERFIL_DATOS (
    id_perfil_datos  bigint IDENTITY(1,1) NOT NULL,
    id_compania      bigint        NOT NULL,
    codigo           nvarchar(50)  NOT NULL,
    nombre           nvarchar(100) NOT NULL,
    descripcion      nvarchar(300) NULL,
    estado           nvarchar(10)  NOT NULL CONSTRAINT DF_PERFILDAT_estado DEFAULT (N'Activo'),
    creado_por       nvarchar(100) NOT NULL,
    creado_en        datetime2     NOT NULL CONSTRAINT DF_PERFILDAT_creado DEFAULT (SYSUTCDATETIME()),
    modificado_por   nvarchar(100) NULL,
    modificado_en    datetime2     NULL,
    CONSTRAINT PK_PERFIL_DATOS PRIMARY KEY (id_perfil_datos),
    CONSTRAINT UQ_PERFIL_DATOS_codigo UNIQUE (id_compania, codigo),
    CONSTRAINT CK_PERFILDAT_estado CHECK (estado IN (N'Activo', N'Inactivo')),
    CONSTRAINT FK_PERFILDAT_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
);
GO

/* Reglas de filas: por atributo, INCLUIR (solo estos valores) o EXCLUIR (todos menos estos). Todos los valores de un atributo comparten el modo. */
IF OBJECT_ID(N'sec.PERFIL_DATOS_FILTRO', N'U') IS NULL
CREATE TABLE sec.PERFIL_DATOS_FILTRO (
    id_perfil_datos  bigint        NOT NULL,
    atributo         nvarchar(30)  NOT NULL,     -- TIPO_PERSONA / ESTADO / UNIDAD / ES_EMPLEADO
    modo             nvarchar(10)  NOT NULL,
    valor            nvarchar(50)  NOT NULL,
    CONSTRAINT PK_PERFIL_DATOS_FILTRO PRIMARY KEY (id_perfil_datos, atributo, valor),
    CONSTRAINT CK_PERFILFIL_modo CHECK (modo IN (N'INCLUIR', N'EXCLUIR')),
    CONSTRAINT FK_PERFILFIL_PERFIL FOREIGN KEY (id_perfil_datos) REFERENCES sec.PERFIL_DATOS (id_perfil_datos)
);
GO

/* Mascara que el perfil aplica a un campo sensible. Sin fila = el perfil ve el campo completo. */
IF OBJECT_ID(N'sec.PERFIL_DATOS_CAMPO', N'U') IS NULL
CREATE TABLE sec.PERFIL_DATOS_CAMPO (
    id_perfil_datos  bigint        NOT NULL,
    campo            nvarchar(50)  NOT NULL,     -- codigo del catalogo de campos enmascarables
    id_mascara       bigint        NOT NULL,
    permite_revelar  bit           NOT NULL CONSTRAINT DF_PERFILCAM_rev DEFAULT (0),
    CONSTRAINT PK_PERFIL_DATOS_CAMPO PRIMARY KEY (id_perfil_datos, campo),
    CONSTRAINT FK_PERFILCAM_PERFIL FOREIGN KEY (id_perfil_datos) REFERENCES sec.PERFIL_DATOS (id_perfil_datos),
    CONSTRAINT FK_PERFILCAM_MASCARA FOREIGN KEY (id_mascara) REFERENCES sec.MASCARA (id_mascara)
);
GO

IF OBJECT_ID(N'sec.USUARIO_PERFIL_DATOS', N'U') IS NULL
CREATE TABLE sec.USUARIO_PERFIL_DATOS (
    id_usuario       bigint        NOT NULL,
    id_perfil_datos  bigint        NOT NULL,
    creado_por       nvarchar(100) NOT NULL,
    creado_en        datetime2     NOT NULL CONSTRAINT DF_USUPERFIL_creado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_USUARIO_PERFIL_DATOS PRIMARY KEY (id_usuario, id_perfil_datos),
    CONSTRAINT FK_USUPERFIL_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario),
    CONSTRAINT FK_USUPERFIL_PERFIL FOREIGN KEY (id_perfil_datos) REFERENCES sec.PERFIL_DATOS (id_perfil_datos)
);
GO

/* 4. Bitacora de revelados de datos enmascarados. */
IF OBJECT_ID(N'aud.LOG_CONSULTA_DATO_SENSIBLE', N'U') IS NULL
CREATE TABLE aud.LOG_CONSULTA_DATO_SENSIBLE (
    id_log_consulta         bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint        NOT NULL,
    fecha_hora              datetime2     NOT NULL CONSTRAINT DF_LOGCONS_fecha DEFAULT (SYSUTCDATETIME()),
    id_usuario              bigint        NULL,
    id_usuario_integracion  bigint        NULL,
    entidad_negocio         nvarchar(100) NOT NULL,
    llave_registro          nvarchar(100) NOT NULL,
    campo                   nvarchar(50)  NOT NULL,
    contexto                nvarchar(200) NULL,
    CONSTRAINT PK_LOG_CONSULTA_DATO_SENSIBLE PRIMARY KEY (id_log_consulta)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LOGCONS_registro' AND object_id = OBJECT_ID(N'aud.LOG_CONSULTA_DATO_SENSIBLE'))
    CREATE INDEX IX_LOGCONS_registro ON aud.LOG_CONSULTA_DATO_SENSIBLE (id_compania, entidad_negocio, llave_registro, fecha_hora);
GO

/* 5. Permiso de administracion. */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'SEG_PERFILES', N'Administrar perfiles de datos y máscaras', N'API',
       N'Crear perfiles de datos (qué clientes ve cada usuario y con qué máscaras), editar las máscaras y asignar perfiles a usuarios.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'SEG_PERFILES');
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo = N'SEG_PERFILES'
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

SELECT (SELECT COUNT(*) FROM sec.MASCARA) AS mascaras, (SELECT COUNT(*) FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'SEG_PERFILES') AS permiso,
       (SELECT COUNT(*) FROM gen.LISTA_VALORES_ITEM WHERE codigo = N'EMPLEADO') AS rol_empleado;
GO
