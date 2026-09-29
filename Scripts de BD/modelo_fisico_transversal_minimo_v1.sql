/* ============================================================================
   CORE BANCARIO - MODELO FISICO MVP v1 - CAPA TRANSVERSAL MINIMA
   ============================================================================
   Alcance (ARQ-017, "corte vertical delgado"): version minima, no el diseno
   completo de la Seccion 9, de las capacidades transversales que el dominio
   Clientes necesita para operar:
     1. Organizacion y usuarios (ORG-001/002)          -> DEPARTAMENTO, USUARIO
     2. RBAC (SEG-002)                                  -> OBJETO_AUTORIZACION,
                                                            ROL_AUTORIZACION,
                                                            ROL_AUTORIZACION_OBJETO,
                                                            USUARIO_ROL
     3. Sesiones (SEG-008)                              -> SESION_USUARIO
     4. Motor de Gestion y Aprobaciones (APR, Seccion 2.4) -> TIPO_GESTION,
                                                            GESTION,
                                                            GESTION_DETALLE,
                                                            GESTION_APROBACION
     5. Auditoria de cambios (AUD-001/002)              -> LOG_AUDITORIA

   Explicitamente FUERA de este incremento (per ARQ-017): ABAC a nivel de
   campo (depende de ATRIBUTO_ENTIDAD, aun no construido), data masking
   (SEG-007, TIPO_MASCARA), auditoria de consultas (AUD-004), Event Mesh
   (9.11), Motor de Reglas (9.6), Gestor Documental (9.5) - todas quedan
   para incrementos posteriores, tal como se acoto explicitamente en ARQ-017.

   PRE-REQUISITO: ejecutar primero modelo_fisico_clientes_mvp1.sql (este
   script agrega al final una FK real desde INTERLOCUTOR_BLOQUEO.id_gestion
   hacia GESTION, que hasta ahora era solo una columna suelta).

   Motor de base de datos: SQL Server (ARQ-011).
   Convenciones de nomenclatura: DAD Seccion 19 (ARQ-018).

   PRE-REQUISITO: ejecutar primero 00_crear_base_datos_cliente_cero.sql
   (crea la base de datos y los schemas) y luego modelo_fisico_clientes_mvp1.sql.
   ============================================================================ */

USE CoreBancario_CERO;
GO

SET NOCOUNT ON;
GO

/* ============================================================================
   1. ORGANIZACION Y USUARIOS (ORG-001/002)
   ============================================================================ */

CREATE TABLE sec.DEPARTAMENTO (
    id_departamento         bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint               NOT NULL,
    id_departamento_padre   bigint               NULL,   -- auto-referencia, estructura jerarquica
    nombre                  nvarchar(200)        NOT NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_DEPARTAMENTO_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_DEPARTAMENTO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_DEPARTAMENTO PRIMARY KEY (id_departamento),
    CONSTRAINT FK_DEPARTAMENTO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_DEPARTAMENTO_DEPARTAMENTO FOREIGN KEY (id_departamento_padre) REFERENCES sec.DEPARTAMENTO (id_departamento)
);
GO

-- Usuarios nombrados (empleados/operadores). Se autentican via Keycloak
-- (SEG-010); subject_iam guarda el "sub" (identificador unico) que trae el
-- token OIDC, para enlazar la identidad de Keycloak con el usuario de negocio.
CREATE TABLE sec.USUARIO (
    id_usuario              bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint               NOT NULL,
    id_departamento         bigint               NULL,
    id_superior             bigint               NULL,   -- auto-referencia, jefe directo
    subject_iam             nvarchar(200)        NOT NULL,  -- claim "sub" del token OIDC (Keycloak)
    nombre_completo         nvarchar(200)        NOT NULL,
    correo                  nvarchar(200)        NOT NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_USUARIO_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_USUARIO_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_USUARIO PRIMARY KEY (id_usuario),
    CONSTRAINT UQ_USUARIO_subject_iam UNIQUE (subject_iam),
    CONSTRAINT CK_USUARIO_estado CHECK (estado IN ('Activo','Inactivo')),
    CONSTRAINT FK_USUARIO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_USUARIO_DEPARTAMENTO FOREIGN KEY (id_departamento) REFERENCES sec.DEPARTAMENTO (id_departamento),
    CONSTRAINT FK_USUARIO_USUARIO FOREIGN KEY (id_superior) REFERENCES sec.USUARIO (id_usuario)
);
GO

-- Cuentas de servicio (usuarios de integracion, ej. el cliente OIDC
-- "core-bancario-backend" con Client Credentials, SEG-009/SEG-010).
CREATE TABLE sec.USUARIO_INTEGRACION (
    id_usuario_integracion  bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint               NOT NULL,
    client_id_iam           nvarchar(200)        NOT NULL,  -- client_id del cliente OIDC en Keycloak
    nombre                  nvarchar(200)        NOT NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_USUARIO_INTEGRACION_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_USUARIO_INTEGRACION_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_USUARIO_INTEGRACION PRIMARY KEY (id_usuario_integracion),
    CONSTRAINT UQ_USUARIO_INTEGRACION_client_id UNIQUE (client_id_iam),
    CONSTRAINT FK_USUARIO_INTEGRACION_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
);
GO

/* ============================================================================
   2. RBAC (SEG-002)
   ============================================================================ */

CREATE TABLE sec.OBJETO_AUTORIZACION (
    id_objeto_autorizacion  bigint IDENTITY(1,1) NOT NULL,
    codigo                  nvarchar(100)        NOT NULL,
    nombre                  nvarchar(200)        NOT NULL,
    tipo_objeto             nvarchar(20)         NOT NULL,  -- Pantalla / Transaccion / API
    descripcion             nvarchar(500)        NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_OBJETO_AUTORIZACION_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_OBJETO_AUTORIZACION_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_OBJETO_AUTORIZACION PRIMARY KEY (id_objeto_autorizacion),
    CONSTRAINT UQ_OBJETO_AUTORIZACION_codigo UNIQUE (codigo),
    CONSTRAINT CK_OBJETO_AUTORIZACION_tipo CHECK (tipo_objeto IN ('Pantalla','Transaccion','API'))
);
GO

CREATE TABLE sec.ROL_AUTORIZACION (
    id_rol_autorizacion     bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint               NULL,   -- NULL = rol de plantilla reutilizable entre companias
    codigo                  nvarchar(100)        NOT NULL,
    nombre                  nvarchar(200)        NOT NULL,
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_ROL_AUTORIZACION_estado DEFAULT ('Activo'),
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_ROL_AUTORIZACION_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ROL_AUTORIZACION PRIMARY KEY (id_rol_autorizacion),
    CONSTRAINT UQ_ROL_AUTORIZACION_compania_codigo UNIQUE (id_compania, codigo),
    CONSTRAINT FK_ROL_AUTORIZACION_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania)
);
GO

CREATE TABLE sec.ROL_AUTORIZACION_OBJETO (
    id_rol_autorizacion     bigint NOT NULL,
    id_objeto_autorizacion  bigint NOT NULL,
    CONSTRAINT PK_ROL_AUTORIZACION_OBJETO PRIMARY KEY (id_rol_autorizacion, id_objeto_autorizacion),
    CONSTRAINT FK_ROL_AUTORIZACION_OBJETO_ROL_AUTORIZACION FOREIGN KEY (id_rol_autorizacion) REFERENCES sec.ROL_AUTORIZACION (id_rol_autorizacion),
    CONSTRAINT FK_ROL_AUTORIZACION_OBJETO_OBJETO_AUTORIZACION FOREIGN KEY (id_objeto_autorizacion) REFERENCES sec.OBJETO_AUTORIZACION (id_objeto_autorizacion)
);
GO

CREATE TABLE sec.USUARIO_ROL (
    id_usuario              bigint    NOT NULL,
    id_rol_autorizacion     bigint    NOT NULL,
    fecha_asignacion        datetime2 NOT NULL CONSTRAINT DF_USUARIO_ROL_fecha_asignacion DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_USUARIO_ROL PRIMARY KEY (id_usuario, id_rol_autorizacion),
    CONSTRAINT FK_USUARIO_ROL_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario),
    CONSTRAINT FK_USUARIO_ROL_ROL_AUTORIZACION FOREIGN KEY (id_rol_autorizacion) REFERENCES sec.ROL_AUTORIZACION (id_rol_autorizacion)
);
GO

/* ============================================================================
   3. SESIONES (SEG-008)
   ============================================================================ */

CREATE TABLE sec.SESION_USUARIO (
    id_sesion_usuario       bigint IDENTITY(1,1) NOT NULL,
    id_usuario              bigint               NULL,   -- login interactivo
    id_usuario_integracion  bigint               NULL,   -- emision de token de una cuenta de servicio
    fecha_inicio            datetime2            NOT NULL CONSTRAINT DF_SESION_USUARIO_fecha_inicio DEFAULT (SYSUTCDATETIME()),
    fecha_ultima_actividad  datetime2            NOT NULL CONSTRAINT DF_SESION_USUARIO_fecha_ultima_actividad DEFAULT (SYSUTCDATETIME()),
    ip_origen               nvarchar(50)         NULL,
    canal                   nvarchar(50)         NULL,
    dispositivo             nvarchar(200)        NULL,
    estado                  nvarchar(40)         NOT NULL CONSTRAINT DF_SESION_USUARIO_estado DEFAULT ('Activa'),
    CONSTRAINT PK_SESION_USUARIO PRIMARY KEY (id_sesion_usuario),
    CONSTRAINT CK_SESION_USUARIO_estado CHECK (estado IN ('Activa','Cerrada por el usuario','Expirada por inactividad','Finalizada por administrador','Finalizada por mantenimiento')),
    -- exactamente uno de los dos (usuario nombrado XOR usuario de integracion)
    CONSTRAINT CK_SESION_USUARIO_titular CHECK (
        (CASE WHEN id_usuario IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN id_usuario_integracion IS NULL THEN 0 ELSE 1 END) = 1
    ),
    CONSTRAINT FK_SESION_USUARIO_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario),
    CONSTRAINT FK_SESION_USUARIO_USUARIO_INTEGRACION FOREIGN KEY (id_usuario_integracion) REFERENCES sec.USUARIO_INTEGRACION (id_usuario_integracion)
);
GO
CREATE INDEX IX_SESION_USUARIO_usuario_estado ON sec.SESION_USUARIO (id_usuario, estado);
GO

/* ============================================================================
   4. MOTOR DE GESTION Y APROBACIONES (Seccion 2.4 del documento narrativo)
   ============================================================================ */

CREATE TABLE apr.TIPO_GESTION (
    id_tipo_gestion                        bigint IDENTITY(1,1) NOT NULL,
    codigo                                  nvarchar(100)        NOT NULL,
    nombre                                  nvarchar(200)        NOT NULL,
    modo                                    nvarchar(20)         NOT NULL,  -- Campos / Transaccional
    requiere_aprobacion_obligatoria         bit                  NOT NULL CONSTRAINT DF_TIPO_GESTION_requiere_aprobacion DEFAULT (1),
    cantidad_niveles_aprobacion             smallint             NULL,
    permite_mismo_usuario_multiples_niveles bit                  NOT NULL CONSTRAINT DF_TIPO_GESTION_mismo_usuario DEFAULT (0),
    categoria                               nvarchar(20)         NOT NULL,  -- Interna / Cliente / Producto
    estado                                  nvarchar(20)         NOT NULL CONSTRAINT DF_TIPO_GESTION_estado DEFAULT ('Activo'),
    creado_por                              nvarchar(100)        NOT NULL,
    creado_en                               datetime2            NOT NULL CONSTRAINT DF_TIPO_GESTION_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_TIPO_GESTION PRIMARY KEY (id_tipo_gestion),
    CONSTRAINT UQ_TIPO_GESTION_codigo UNIQUE (codigo),
    CONSTRAINT CK_TIPO_GESTION_modo CHECK (modo IN ('Campos','Transaccional')),
    CONSTRAINT CK_TIPO_GESTION_categoria CHECK (categoria IN ('Interna','Cliente','Producto'))
);
GO

CREATE TABLE apr.GESTION (
    id_gestion              bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint               NOT NULL,
    id_tipo_gestion         bigint               NOT NULL,
    estado                  nvarchar(30)         NOT NULL CONSTRAINT DF_GESTION_estado DEFAULT ('Borrador'),  -- Borrador/Pendiente de Aprobacion/Aprobada/Rechazada/Aplicada
    id_usuario_solicitante  bigint               NOT NULL,
    entidad_negocio         nvarchar(100)        NOT NULL,   -- ej. 'INTERLOCUTOR_BLOQUEO', 'INTERLOCUTOR_CUMPLIMIENTO'
    llave_registro          nvarchar(100)        NULL,       -- id del registro afectado (generico, sin FK dedicada por entidad)
    comentario              nvarchar(1000)       NULL,
    motivo_rechazo          nvarchar(500)        NULL,
    fecha_solicitud         datetime2            NOT NULL CONSTRAINT DF_GESTION_fecha_solicitud DEFAULT (SYSUTCDATETIME()),
    fecha_efectiva          datetime2            NULL,       -- aprobar hoy, aplicar en fecha futura
    fecha_resolucion        datetime2            NULL,
    correlation_id          uniqueidentifier     NULL,
    creado_por              nvarchar(100)        NOT NULL,
    creado_en               datetime2            NOT NULL CONSTRAINT DF_GESTION_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por          nvarchar(100)        NULL,
    modificado_en           datetime2            NULL,
    CONSTRAINT PK_GESTION PRIMARY KEY (id_gestion),
    CONSTRAINT CK_GESTION_estado CHECK (estado IN ('Borrador','Pendiente de Aprobacion','Aprobada','Rechazada','Aplicada')),
    CONSTRAINT FK_GESTION_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_GESTION_TIPO_GESTION FOREIGN KEY (id_tipo_gestion) REFERENCES apr.TIPO_GESTION (id_tipo_gestion),
    CONSTRAINT FK_GESTION_USUARIO FOREIGN KEY (id_usuario_solicitante) REFERENCES sec.USUARIO (id_usuario)
);
GO
CREATE INDEX IX_GESTION_entidad_llave ON apr.GESTION (entidad_negocio, llave_registro);
GO

-- Detalle campo a campo (solo para GESTION de modo 'Campos')
CREATE TABLE apr.GESTION_DETALLE (
    id_gestion_detalle      bigint IDENTITY(1,1) NOT NULL,
    id_gestion              bigint               NOT NULL,
    nombre_campo            nvarchar(100)        NOT NULL,
    valor_anterior          nvarchar(1000)       NULL,
    valor_nuevo             nvarchar(1000)       NULL,
    CONSTRAINT PK_GESTION_DETALLE PRIMARY KEY (id_gestion_detalle),
    CONSTRAINT FK_GESTION_DETALLE_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion)
);
GO

-- Workflow de N niveles (aprobador de cada nivel resuelto por configuracion,
-- no fijo en el modelo: aqui solo queda el resultado de cada nivel).
CREATE TABLE apr.GESTION_APROBACION (
    id_gestion_aprobacion   bigint IDENTITY(1,1) NOT NULL,
    id_gestion              bigint               NOT NULL,
    nivel                   smallint             NOT NULL,
    id_usuario_aprobador    bigint               NULL,   -- se llena al resolverse
    estado                  nvarchar(20)         NOT NULL CONSTRAINT DF_GESTION_APROBACION_estado DEFAULT ('Pendiente'),
    comentario              nvarchar(500)        NULL,
    fecha_resolucion        datetime2            NULL,
    CONSTRAINT PK_GESTION_APROBACION PRIMARY KEY (id_gestion_aprobacion),
    CONSTRAINT UQ_GESTION_APROBACION_gestion_nivel UNIQUE (id_gestion, nivel),
    CONSTRAINT CK_GESTION_APROBACION_estado CHECK (estado IN ('Pendiente','Aprobado','Rechazado')),
    CONSTRAINT FK_GESTION_APROBACION_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion),
    CONSTRAINT FK_GESTION_APROBACION_USUARIO FOREIGN KEY (id_usuario_aprobador) REFERENCES sec.USUARIO (id_usuario)
);
GO

/* ============================================================================
   5. AUDITORIA DE CAMBIOS (AUD-001/002)
   ============================================================================ */

CREATE TABLE aud.LOG_AUDITORIA (
    id_log_auditoria        bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint               NULL,
    fecha_hora              datetime2            NOT NULL CONSTRAINT DF_LOG_AUDITORIA_fecha_hora DEFAULT (SYSUTCDATETIME()),
    id_usuario              bigint               NULL,
    id_usuario_integracion  bigint               NULL,
    entidad_negocio         nvarchar(100)        NOT NULL,
    llave_registro          nvarchar(100)        NOT NULL,
    accion                  nvarchar(20)         NOT NULL,  -- Alta / Cambio / Baja
    nombre_campo            nvarchar(100)        NULL,
    valor_anterior          nvarchar(1000)       NULL,
    valor_nuevo             nvarchar(1000)       NULL,
    id_gestion              bigint               NULL,
    correlation_id          uniqueidentifier     NULL,
    CONSTRAINT PK_LOG_AUDITORIA PRIMARY KEY (id_log_auditoria),
    CONSTRAINT CK_LOG_AUDITORIA_accion CHECK (accion IN ('Alta','Cambio','Baja')),
    CONSTRAINT FK_LOG_AUDITORIA_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_LOG_AUDITORIA_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario),
    CONSTRAINT FK_LOG_AUDITORIA_USUARIO_INTEGRACION FOREIGN KEY (id_usuario_integracion) REFERENCES sec.USUARIO_INTEGRACION (id_usuario_integracion),
    CONSTRAINT FK_LOG_AUDITORIA_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion)
);
GO
CREATE INDEX IX_LOG_AUDITORIA_entidad_llave ON aud.LOG_AUDITORIA (entidad_negocio, llave_registro, fecha_hora DESC);
GO

/* ============================================================================
   6. ENLACE CON EL DOMINIO CLIENTES YA CONSTRUIDO
   ----------------------------------------------------------------------------
   Ahora que GESTION existe, se reemplaza la columna suelta
   INTERLOCUTOR_BLOQUEO.id_gestion por una FK real. Requiere haber corrido
   antes modelo_fisico_clientes_mvp1.sql.
   ============================================================================ */

ALTER TABLE cli.INTERLOCUTOR_BLOQUEO
    ADD CONSTRAINT FK_INTERLOCUTOR_BLOQUEO_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion);
GO

/* ============================================================================
   7. SEED DATA MINIMO — catalogos de ejemplo para probar el flujo de
      Clientes (Alta, KYC, Bloqueo) de punta a punta
   ============================================================================ */

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por) VALUES
    ('CLI_ALTA', 'Alta de interlocutor', 'API', 'Registrar un nuevo interlocutor/prospecto', 'sistema'),
    ('CLI_CONSULTA', 'Consulta de interlocutor', 'API', 'Ver el perfil 360 de un interlocutor', 'sistema'),
    ('CLI_KYC', 'Actualizacion de KYC', 'Transaccion', 'Actualizar datos de cumplimiento AML/KYC', 'sistema'),
    ('CLI_BLOQUEO', 'Alta/levantamiento de bloqueo de interlocutor', 'Transaccion', 'Bloquear o desbloquear a un interlocutor', 'sistema');
GO

INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por) VALUES
    ('CLI_ACTUALIZACION_KYC', 'Actualizacion de KYC', 'Campos', 0, 1, 'Cliente', 'sistema'),          -- CLI-012: autoaprobable si no cambia nivel de riesgo
    ('CLI_BLOQUEO', 'Alta/modificacion/levantamiento de bloqueo de cliente', 'Campos', 1, 1, 'Cliente', 'sistema'); -- CLI-008: siempre revision manual
GO

/* ============================================================================
   FIN DEL SCRIPT - MVP v1 / Capa transversal minima
   ============================================================================ */
