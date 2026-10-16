/* ============================================================================
   Ronda 30 - KYC y cumplimiento (Grupo B, entrega 1): expediente KYC, riesgo, PEP, sanciones y segmentacion.
   Persona y Organizacion. Todo cambio se solicita por gestion CLI_KYC (aprobacion). Requiere: perfil_financiero_ronda29.sql. Re-ejecutable.

   Tablas nuevas:
     cli.INTERLOCUTOR_KYC            1:1  estado KYC, nivel de debida diligencia, riesgo, ultima/proxima revision, 3 segmentos
     cli.INTERLOCUTOR_MOTIVO_RIESGO  0..N motivos controlados de la clasificacion de riesgo
     cli.INTERLOCUTOR_EXPEDIENTE     0..N referencias al expediente en el gestor documental (no el documento)
     cli.INTERLOCUTOR_SCREENING      historial de evaluaciones PEP y sanciones (la vigente es la mas reciente de cada tipo)
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Tablas -------------------------------------------------------------------- */
IF OBJECT_ID(N'cli.INTERLOCUTOR_KYC', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_KYC (
        id_interlocutor        bigint        NOT NULL,
        id_estado_kyc          bigint        NULL,   -- LISTA_VALORES_ITEM (ESTADO_KYC)
        id_nivel_dd            bigint        NULL,   -- LISTA_VALORES_ITEM (NIVEL_DEBIDA_DILIGENCIA)
        id_riesgo              bigint        NULL,   -- LISTA_VALORES_ITEM (CLASIFICACION_RIESGO)
        fecha_ultima_revision  date          NULL,
        fecha_proxima_revision date          NULL,
        id_segmento_cliente    bigint        NULL,   -- LISTA_VALORES_ITEM (SEGMENTO_CLIENTE)
        id_segmento_pricing    bigint        NULL,   -- LISTA_VALORES_ITEM (SEGMENTO_PRICING)
        id_segmento_riesgo     bigint        NULL,   -- LISTA_VALORES_ITEM (SEGMENTO_RIESGO)
        creado_por             nvarchar(100) NOT NULL,
        creado_en              datetime2     NOT NULL CONSTRAINT DF_INT_KYC_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por         nvarchar(100) NULL,
        modificado_en          datetime2     NULL,
        CONSTRAINT PK_INTERLOCUTOR_KYC PRIMARY KEY (id_interlocutor),
        CONSTRAINT FK_INT_KYC_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_KYC_ESTADO FOREIGN KEY (id_estado_kyc) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INT_KYC_NIVEL FOREIGN KEY (id_nivel_dd) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INT_KYC_RIESGO FOREIGN KEY (id_riesgo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INT_KYC_SEG_CLI FOREIGN KEY (id_segmento_cliente) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INT_KYC_SEG_PRI FOREIGN KEY (id_segmento_pricing) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INT_KYC_SEG_RIE FOREIGN KEY (id_segmento_riesgo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT CK_INT_KYC_revision CHECK (fecha_proxima_revision IS NULL OR fecha_ultima_revision IS NULL OR fecha_proxima_revision >= fecha_ultima_revision)
    );
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_MOTIVO_RIESGO', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_MOTIVO_RIESGO (
        id_motivo_riesgo bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor  bigint        NOT NULL,
        id_motivo        bigint        NOT NULL,   -- LISTA_VALORES_ITEM (MOTIVO_RIESGO)
        estado           nvarchar(20)  NOT NULL CONSTRAINT DF_INT_MOTRIE_estado DEFAULT ('Activo'),
        creado_por       nvarchar(100) NOT NULL,
        creado_en        datetime2     NOT NULL CONSTRAINT DF_INT_MOTRIE_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por   nvarchar(100) NULL,
        modificado_en    datetime2     NULL,
        CONSTRAINT PK_INTERLOCUTOR_MOTIVO_RIESGO PRIMARY KEY (id_motivo_riesgo),
        CONSTRAINT CK_INT_MOTRIE_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_INT_MOTRIE_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_MOTRIE_ITEM FOREIGN KEY (id_motivo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE UNIQUE INDEX UX_INT_MOTRIE_valor ON cli.INTERLOCUTOR_MOTIVO_RIESGO (id_interlocutor, id_motivo) WHERE estado = 'Activo';
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_EXPEDIENTE', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_EXPEDIENTE (
        id_expediente    bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor  bigint         NOT NULL,
        referencia       nvarchar(100)  NOT NULL,   -- identificador en el gestor documental (no el documento)
        sistema          nvarchar(100)  NULL,       -- p. ej. CICAC, SharePoint
        descripcion      nvarchar(300)  NULL,
        estado           nvarchar(20)   NOT NULL CONSTRAINT DF_INT_EXPED_estado DEFAULT ('Activo'),
        creado_por       nvarchar(100)  NOT NULL,
        creado_en        datetime2      NOT NULL CONSTRAINT DF_INT_EXPED_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por   nvarchar(100)  NULL,
        modificado_en    datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_EXPEDIENTE PRIMARY KEY (id_expediente),
        CONSTRAINT CK_INT_EXPED_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_INT_EXPED_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
    );
    CREATE INDEX IX_INT_EXPED_interlocutor ON cli.INTERLOCUTOR_EXPEDIENTE (id_interlocutor, estado);
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_SCREENING', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_SCREENING (
        id_screening     bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor  bigint         NOT NULL,
        tipo             nvarchar(10)   NOT NULL,   -- PEP | Sanciones
        id_resultado     bigint         NOT NULL,   -- LISTA_VALORES_ITEM (RESULTADO_PEP o RESULTADO_SANCIONES segun el tipo)
        fecha_screening  date           NOT NULL,
        fuente           nvarchar(200)  NOT NULL,   -- servicio o lista utilizada
        observacion      nvarchar(500)  NULL,
        estado           nvarchar(20)   NOT NULL CONSTRAINT DF_INT_SCREEN_estado DEFAULT ('Activo'),
        creado_por       nvarchar(100)  NOT NULL,
        creado_en        datetime2      NOT NULL CONSTRAINT DF_INT_SCREEN_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por   nvarchar(100)  NULL,
        modificado_en    datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_SCREENING PRIMARY KEY (id_screening),
        CONSTRAINT CK_INT_SCREEN_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_INT_SCREEN_tipo CHECK (tipo IN ('PEP', 'Sanciones')),
        CONSTRAINT FK_INT_SCREEN_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_SCREEN_ITEM FOREIGN KEY (id_resultado) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE INDEX IX_INT_SCREEN_interlocutor ON cli.INTERLOCUTOR_SCREENING (id_interlocutor, tipo, fecha_screening DESC);
END
GO

/* 2. Listas de valores ---------------------------------------------------------- */
DECLARE @tipos TABLE (codigo nvarchar(100), nombre nvarchar(200), descripcion nvarchar(500));
INSERT INTO @tipos VALUES
 (N'ESTADO_KYC',                N'Estado KYC',                  N'Estado del expediente de conocimiento del cliente.'),
 (N'NIVEL_DEBIDA_DILIGENCIA',   N'Nivel de debida diligencia',  N'Nivel de debida diligencia aplicado al cliente.'),
 (N'CLASIFICACION_RIESGO',      N'Clasificación de riesgo',     N'Nivel de riesgo vigente según la metodología institucional.'),
 (N'MOTIVO_RIESGO',             N'Motivo de riesgo',            N'Motivos controlados que explican la clasificación de riesgo.'),
 (N'RESULTADO_PEP',             N'Resultado de análisis PEP',   N'Resultado de la evaluación de persona expuesta políticamente.'),
 (N'RESULTADO_SANCIONES',       N'Resultado de sanciones',      N'Resultado del screening contra listas de sanciones.'),
 (N'SEGMENTO_CLIENTE',          N'Segmento de cliente',         N'Segmento usado en elegibilidad, límites o servicio.'),
 (N'SEGMENTO_PRICING',          N'Segmento de pricing',         N'Segmento usado por el motor de precios.'),
 (N'SEGMENTO_RIESGO',           N'Segmento de riesgo',          N'Clasificación transversal usada en decisiones de riesgo.');
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT t.codigo, t.nombre, t.descripcion, 1, N'sistema' FROM @tipos t
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO x WHERE x.codigo = t.codigo);

DECLARE @items TABLE (tipo nvarchar(100), codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES
 (N'ESTADO_KYC', N'PENDIENTE',   N'Pendiente', 1),
 (N'ESTADO_KYC', N'EN_REVISION', N'En revisión', 2),
 (N'ESTADO_KYC', N'COMPLETO',    N'Completo', 3),
 (N'ESTADO_KYC', N'VENCIDO',     N'Vencido', 4),
 (N'ESTADO_KYC', N'RECHAZADO',   N'Rechazado', 5),
 (N'NIVEL_DEBIDA_DILIGENCIA', N'SIMPLIFICADA', N'Simplificada', 1),
 (N'NIVEL_DEBIDA_DILIGENCIA', N'ESTANDAR',     N'Estándar', 2),
 (N'NIVEL_DEBIDA_DILIGENCIA', N'REFORZADA',    N'Reforzada', 3),
 (N'CLASIFICACION_RIESGO', N'BAJO',  N'Bajo', 1),
 (N'CLASIFICACION_RIESGO', N'MEDIO', N'Medio', 2),
 (N'CLASIFICACION_RIESGO', N'ALTO',  N'Alto', 3),
 (N'MOTIVO_RIESGO', N'PEP',                 N'Persona expuesta políticamente', 1),
 (N'MOTIVO_RIESGO', N'PAIS_RIESGO',         N'País o jurisdicción de riesgo', 2),
 (N'MOTIVO_RIESGO', N'ACTIVIDAD_RIESGO',    N'Actividad económica de riesgo', 3),
 (N'MOTIVO_RIESGO', N'ORIGEN_FONDOS',       N'Origen de fondos no claro', 4),
 (N'MOTIVO_RIESGO', N'PRODUCTO_CANAL',      N'Producto o canal de riesgo', 5),
 (N'MOTIVO_RIESGO', N'ESTRUCTURA_COMPLEJA', N'Estructura de propiedad compleja', 6),
 (N'MOTIVO_RIESGO', N'COINCIDENCIA_LISTAS', N'Coincidencia en listas', 7),
 (N'MOTIVO_RIESGO', N'OTRO',                N'Otro', 99),
 (N'RESULTADO_PEP', N'NO_PEP',       N'No es PEP', 1),
 (N'RESULTADO_PEP', N'PEP',          N'PEP', 2),
 (N'RESULTADO_PEP', N'FAMILIAR_PEP', N'Familiar o asociado de PEP', 3),
 (N'RESULTADO_PEP', N'EN_REVISION',  N'En revisión', 4),
 (N'RESULTADO_SANCIONES', N'SIN_COINCIDENCIAS',      N'Sin coincidencias', 1),
 (N'RESULTADO_SANCIONES', N'COINCIDENCIA_POTENCIAL', N'Coincidencia potencial', 2),
 (N'RESULTADO_SANCIONES', N'COINCIDENCIA_CONFIRMADA',N'Coincidencia confirmada', 3),
 (N'RESULTADO_SANCIONES', N'EN_REVISION',            N'En revisión', 4),
 (N'SEGMENTO_CLIENTE', N'MASIVO',      N'Masivo', 1),
 (N'SEGMENTO_CLIENTE', N'PREFERENTE',  N'Preferente', 2),
 (N'SEGMENTO_CLIENTE', N'EMPRESARIAL', N'Empresarial', 3),
 (N'SEGMENTO_PRICING', N'ESTANDAR',    N'Estándar', 1),
 (N'SEGMENTO_PRICING', N'PREFERENCIAL',N'Preferencial', 2),
 (N'SEGMENTO_RIESGO',  N'STANDARD',    N'Estándar', 1),
 (N'SEGMENTO_RIESGO',  N'MONITOREO',   N'Monitoreo reforzado', 2);
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = i.tipo
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

/* 3. Seguridad: permisos, rol y tipo de gestion ----------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, x.tipo, x.descripcion, N'sistema' FROM (VALUES
  (N'CLI_KYC_VER', N'Ver KYC y cumplimiento del cliente', N'API', N'Consultar expediente KYC, riesgo, PEP, sanciones y segmentación de un cliente.'),
  (N'CLI_KYC',     N'Solicitar actualización de KYC y cumplimiento', N'API', N'Solicitar (vía Gestión) el cambio del expediente KYC, riesgo, segmentación y evaluaciones PEP/sanciones.')
) x(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_' + lt.codigo, N'Mantener lista de valores: ' + lt.nombre, N'Transaccion', N'Mantenimiento de la lista de valores ' + lt.codigo + N'.', N'sistema'
FROM gen.LISTA_VALORES_TIPO lt
WHERE lt.codigo IN (N'ESTADO_KYC', N'NIVEL_DEBIDA_DILIGENCIA', N'CLASIFICACION_RIESGO', N'MOTIVO_RIESGO', N'RESULTADO_PEP', N'RESULTADO_SANCIONES',
                    N'SEGMENTO_CLIENTE', N'SEGMENTO_PRICING', N'SEGMENTO_RIESGO')
  AND NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION x WHERE x.codigo = N'LOV_' + lt.codigo);

IF NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION WHERE codigo = N'OFICIAL_CUMPLIMIENTO' AND id_compania IS NULL)
    INSERT INTO sec.ROL_AUTORIZACION (id_compania, codigo, nombre, creado_por)
    VALUES (NULL, N'OFICIAL_CUMPLIMIENTO', N'Oficial de Cumplimiento', N'sistema');
GO

DECLARE @of  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'OFICIAL_CUMPLIMIENTO' AND id_compania IS NULL);
DECLARE @an  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @ap  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'APROBADOR_GESTIONES' AND id_compania IS NULL);
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
DECLARE @map TABLE (rol bigint, objeto nvarchar(100));
INSERT INTO @map VALUES (@of, N'CLI_KYC'), (@of, N'CLI_KYC_VER'), (@of, N'CLI_CONSULTA'), (@of, N'GES_APROBAR'),
                        (@an, N'CLI_KYC_VER'), (@ap, N'CLI_KYC_VER');
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT m.rol, o.id_objeto_autorizacion FROM @map m JOIN sec.OBJETO_AUTORIZACION o ON o.codigo = m.objeto
WHERE m.rol IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = m.rol AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'LOV_ESTADO_KYC', N'LOV_NIVEL_DEBIDA_DILIGENCIA', N'LOV_CLASIFICACION_RIESGO', N'LOV_MOTIVO_RIESGO', N'LOV_RESULTADO_PEP',
                       N'LOV_RESULTADO_SANCIONES', N'LOV_SEGMENTO_CLIENTE', N'LOV_SEGMENTO_PRICING', N'LOV_SEGMENTO_RIESGO')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);

-- qa.arquitecto: puede solicitar y consultar (ya aprueba gestiones)
DECLARE @qa bigint = (SELECT id_usuario FROM sec.USUARIO WHERE correo = N'qa.arquitecto@example.com');
IF @qa IS NOT NULL AND @of IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.USUARIO_ROL WHERE id_usuario = @qa AND id_rol_autorizacion = @of)
    INSERT INTO sec.USUARIO_ROL (id_usuario, id_rol_autorizacion) VALUES (@qa, @of);
GO

IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_KYC')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_KYC', N'Actualizacion de KYC y cumplimiento', N'Campos', 1, 1, N'Cliente', N'sistema');
GO

/* 4. Codigos de requisito admitidos (sin fila = opcional). KYC_* se verifican al asumir un rol sobre un cliente ya registrado ---------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_TIPO_ROL_REQUISITO_codigo' AND parent_object_id = OBJECT_ID(N'cli.TIPO_ROL_REQUISITO'))
    ALTER TABLE cli.TIPO_ROL_REQUISITO DROP CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo;
GO
ALTER TABLE cli.TIPO_ROL_REQUISITO ADD CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo CHECK (requisito IN (
    N'PERSONA_NOMBRE2', N'PERSONA_APELLIDO2', N'PERSONA_FECHA_NACIMIENTO',
    N'PERSONA_NOMBRE_CONOCIDO', N'PERSONA_PAIS_NACIMIENTO', N'PERSONA_NACIONALIDAD', N'PERSONA_SEXO', N'PERSONA_ESTADO_CIVIL',
    N'IDENT_INSTITUCION', N'IDENT_FECHA_EMISION', N'IDENT_FECHA_VENCIMIENTO',
    N'DIRECCION', N'DIR_NIVELES', N'DIR_CODIGO_POSTAL', N'DIR_LINEA2', N'DIR_LINEA3',
    N'CORREO', N'TELEFONO',
    N'ORG_NOMBRE_COMERCIAL', N'ORG_FECHA_CONSTITUCION', N'ORG_REFERENCIA_REGISTRAL', N'ORG_ACTIVIDAD',
    N'RF_RESIDENCIA', N'RF_IDENT_TRIBUTARIO',
    N'PF_CONDICION_LABORAL', N'PF_OCUPACION', N'PF_EMPLEADOR', N'PF_INGRESO', N'PF_GASTOS', N'PF_ACTIVOS', N'PF_PASIVOS', N'PF_PATRIMONIO',
    N'PF_ORIGEN_FONDOS', N'PF_ORIGEN_PATRIMONIO',
    N'PT_PERFIL', N'PT_USO_EFECTIVO',
    N'REL_REPRESENTANTE_LEGAL', N'REL_BENEFICIARIO_FINAL',
    N'KYC_ESTADO_COMPLETO', N'KYC_PEP_EVALUADO', N'KYC_SANCIONES_EVALUADO'));
GO

SELECT lt.codigo AS lista, COUNT(*) AS valores FROM gen.LISTA_VALORES_ITEM i
JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = i.id_lista_valores_tipo
WHERE lt.codigo IN (N'ESTADO_KYC', N'NIVEL_DEBIDA_DILIGENCIA', N'CLASIFICACION_RIESGO', N'MOTIVO_RIESGO', N'RESULTADO_PEP', N'RESULTADO_SANCIONES',
                    N'SEGMENTO_CLIENTE', N'SEGMENTO_PRICING', N'SEGMENTO_RIESGO') GROUP BY lt.codigo ORDER BY lt.codigo;
GO
