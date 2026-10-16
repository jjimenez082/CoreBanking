/* ============================================================================
   Ronda 29 - Perfil financiero (Persona y Organizacion): residencia fiscal, perfil economico, ingresos, origen de fondos/patrimonio
   y perfil transaccional. Todo opcional de entrada y parametrizable por rol en Administracion > Requisitos por rol.
   Requiere: persona_identidad_ronda27.sql. Re-ejecutable.

   Tablas nuevas (todas con baja logica y auditoria de creacion/modificacion):
     cli.INTERLOCUTOR_PERFIL_ECONOMICO     1:1 (condicion laboral, ocupacion, empleador, gastos, activos, pasivos, patrimonio, corte, uso de efectivo)
     cli.INTERLOCUTOR_RESIDENCIA_FISCAL    1..N (pais, identificador tributario, principal, vigencia)
     cli.INTERLOCUTOR_INGRESO              0..N (tipo, monto, moneda, periodicidad)
     cli.INTERLOCUTOR_ORIGEN               0..N (clase Fondos/Patrimonio + valor de lista)
     cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL 0..N (tipo de transaccion, frecuencia, monto y moneda, paises origen/destino)
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Tablas -------------------------------------------------------------------- */
IF OBJECT_ID(N'cli.INTERLOCUTOR_PERFIL_ECONOMICO', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_PERFIL_ECONOMICO (
        id_interlocutor      bigint         NOT NULL,
        id_condicion_laboral bigint         NULL,   -- LISTA_VALORES_ITEM (CONDICION_LABORAL) - persona
        id_ocupacion         bigint         NULL,   -- LISTA_VALORES_ITEM (OCUPACION) - persona
        empleador            nvarchar(200)  NULL,   -- persona
        gastos_estimados     decimal(18,2)  NULL,   -- persona
        activos_declarados   decimal(18,2)  NULL,
        pasivos_declarados   decimal(18,2)  NULL,
        patrimonio_declarado decimal(18,2)  NULL,   -- organizacion
        id_moneda_balance    bigint         NULL,   -- gen.MONEDA: moneda de gastos/activos/pasivos/patrimonio
        fecha_corte          date           NULL,   -- fecha de corte de activos/pasivos/patrimonio
        id_uso_efectivo      bigint         NULL,   -- LISTA_VALORES_ITEM (USO_EFECTIVO)
        creado_por           nvarchar(100)  NOT NULL,
        creado_en            datetime2      NOT NULL CONSTRAINT DF_INT_PERFEC_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100)  NULL,
        modificado_en        datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_PERFIL_ECONOMICO PRIMARY KEY (id_interlocutor),
        CONSTRAINT FK_INT_PERFEC_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_PERFEC_MONEDA FOREIGN KEY (id_moneda_balance) REFERENCES gen.MONEDA (id_moneda)
    );
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_RESIDENCIA_FISCAL', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_RESIDENCIA_FISCAL (
        id_residencia_fiscal bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor      bigint         NOT NULL,
        id_pais              bigint         NOT NULL,   -- LISTA_VALORES_ITEM (PAIS)
        identificador_tributario nvarchar(50) NULL,
        es_principal         bit            NOT NULL CONSTRAINT DF_INT_RESFIS_principal DEFAULT (0),
        fecha_desde          date           NULL,
        fecha_hasta          date           NULL,
        estado               nvarchar(20)   NOT NULL CONSTRAINT DF_INT_RESFIS_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100)  NOT NULL,
        creado_en            datetime2      NOT NULL CONSTRAINT DF_INT_RESFIS_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100)  NULL,
        modificado_en        datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_RESIDENCIA_FISCAL PRIMARY KEY (id_residencia_fiscal),
        CONSTRAINT CK_INT_RESFIS_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT FK_INT_RESFIS_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_RESFIS_PAIS FOREIGN KEY (id_pais) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE INDEX IX_INT_RESFIS_interlocutor ON cli.INTERLOCUTOR_RESIDENCIA_FISCAL (id_interlocutor, estado);
    CREATE UNIQUE INDEX UX_INT_RESFIS_pais ON cli.INTERLOCUTOR_RESIDENCIA_FISCAL (id_interlocutor, id_pais) WHERE estado = 'Activo';
    CREATE UNIQUE INDEX UX_INT_RESFIS_principal ON cli.INTERLOCUTOR_RESIDENCIA_FISCAL (id_interlocutor) WHERE estado = 'Activo' AND es_principal = 1;
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_INGRESO', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_INGRESO (
        id_ingreso           bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor      bigint         NOT NULL,
        id_tipo_ingreso      bigint         NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_INGRESO)
        monto                decimal(18,2)  NOT NULL,
        id_moneda            bigint         NOT NULL,   -- gen.MONEDA
        id_periodicidad      bigint         NOT NULL,   -- LISTA_VALORES_ITEM (PERIODICIDAD_INGRESO)
        estado               nvarchar(20)   NOT NULL CONSTRAINT DF_INT_INGRESO_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100)  NOT NULL,
        creado_en            datetime2      NOT NULL CONSTRAINT DF_INT_INGRESO_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100)  NULL,
        modificado_en        datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_INGRESO PRIMARY KEY (id_ingreso),
        CONSTRAINT CK_INT_INGRESO_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_INT_INGRESO_monto CHECK (monto >= 0),
        CONSTRAINT FK_INT_INGRESO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_INGRESO_MONEDA FOREIGN KEY (id_moneda) REFERENCES gen.MONEDA (id_moneda)
    );
    CREATE INDEX IX_INT_INGRESO_interlocutor ON cli.INTERLOCUTOR_INGRESO (id_interlocutor, estado);
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_ORIGEN', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_ORIGEN (
        id_origen_registro   bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor      bigint         NOT NULL,
        clase                nvarchar(10)   NOT NULL,   -- Fondos | Patrimonio
        id_origen            bigint         NOT NULL,   -- LISTA_VALORES_ITEM (ORIGEN_FONDOS u ORIGEN_PATRIMONIO segun la clase)
        estado               nvarchar(20)   NOT NULL CONSTRAINT DF_INT_ORIGEN_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100)  NOT NULL,
        creado_en            datetime2      NOT NULL CONSTRAINT DF_INT_ORIGEN_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100)  NULL,
        modificado_en        datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_ORIGEN PRIMARY KEY (id_origen_registro),
        CONSTRAINT CK_INT_ORIGEN_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_INT_ORIGEN_clase CHECK (clase IN ('Fondos', 'Patrimonio')),
        CONSTRAINT FK_INT_ORIGEN_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_ORIGEN_ITEM FOREIGN KEY (id_origen) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE INDEX IX_INT_ORIGEN_interlocutor ON cli.INTERLOCUTOR_ORIGEN (id_interlocutor, estado);
    CREATE UNIQUE INDEX UX_INT_ORIGEN_valor ON cli.INTERLOCUTOR_ORIGEN (id_interlocutor, clase, id_origen) WHERE estado = 'Activo';
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL (
        id_perfil_transaccional bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor      bigint         NOT NULL,
        id_tipo_transaccion  bigint         NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_TRANSACCION)
        id_frecuencia        bigint         NOT NULL,   -- LISTA_VALORES_ITEM (FRECUENCIA_TRANSACCION)
        monto_esperado       decimal(18,2)  NULL,
        id_moneda            bigint         NULL,       -- gen.MONEDA (obligatoria si hay monto)
        id_pais_origen       bigint         NULL,       -- LISTA_VALORES_ITEM (PAIS)
        id_pais_destino      bigint         NULL,       -- LISTA_VALORES_ITEM (PAIS)
        estado               nvarchar(20)   NOT NULL CONSTRAINT DF_INT_PERFTR_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100)  NOT NULL,
        creado_en            datetime2      NOT NULL CONSTRAINT DF_INT_PERFTR_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100)  NULL,
        modificado_en        datetime2      NULL,
        CONSTRAINT PK_INTERLOCUTOR_PERFIL_TRANSACCIONAL PRIMARY KEY (id_perfil_transaccional),
        CONSTRAINT CK_INT_PERFTR_estado CHECK (estado IN ('Activo', 'Inactivo')),
        CONSTRAINT CK_INT_PERFTR_monto CHECK (monto_esperado IS NULL OR monto_esperado >= 0),
        CONSTRAINT FK_INT_PERFTR_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_PERFTR_MONEDA FOREIGN KEY (id_moneda) REFERENCES gen.MONEDA (id_moneda)
    );
    CREATE INDEX IX_INT_PERFTR_interlocutor ON cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL (id_interlocutor, estado);
END
GO

/* 2. Listas de valores ---------------------------------------------------------- */
DECLARE @tipos TABLE (codigo nvarchar(100), nombre nvarchar(200), descripcion nvarchar(500));
INSERT INTO @tipos VALUES
 (N'CONDICION_LABORAL',      N'Condición laboral',            N'Situación laboral de la persona.'),
 (N'OCUPACION',              N'Ocupación o profesión',        N'Ocupación o profesión de la persona.'),
 (N'TIPO_INGRESO',           N'Tipo de ingreso',              N'Tipos de ingreso declarado (salario, honorarios, ventas...).'),
 (N'PERIODICIDAD_INGRESO',   N'Periodicidad del ingreso',     N'Periodo al que corresponde el monto del ingreso.'),
 (N'ORIGEN_FONDOS',          N'Origen de fondos',             N'De dónde provienen los fondos que se movilizarán.'),
 (N'ORIGEN_PATRIMONIO',      N'Origen del patrimonio',        N'De dónde proviene el patrimonio.'),
 (N'TIPO_TRANSACCION',       N'Tipo de transacción esperada', N'Tipos de transacción esperada en el perfil transaccional.'),
 (N'FRECUENCIA_TRANSACCION', N'Frecuencia de transacción',    N'Frecuencia esperada de una transacción.'),
 (N'USO_EFECTIVO',           N'Uso esperado de efectivo',     N'Nivel de uso de efectivo esperado.');
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT t.codigo, t.nombre, t.descripcion, 1, N'sistema' FROM @tipos t
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO x WHERE x.codigo = t.codigo);

DECLARE @items TABLE (tipo nvarchar(100), codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES
 (N'CONDICION_LABORAL', N'EMPLEADO',      N'Empleado(a) asalariado(a)', 1),
 (N'CONDICION_LABORAL', N'INDEPENDIENTE', N'Trabajador(a) independiente', 2),
 (N'CONDICION_LABORAL', N'EMPRESARIO',    N'Empresario(a) o propietario(a)', 3),
 (N'CONDICION_LABORAL', N'PENSIONADO',    N'Pensionado(a) o jubilado(a)', 4),
 (N'CONDICION_LABORAL', N'ESTUDIANTE',    N'Estudiante', 5),
 (N'CONDICION_LABORAL', N'HOGAR',         N'Labores del hogar', 6),
 (N'CONDICION_LABORAL', N'DESEMPLEADO',   N'Desempleado(a)', 7),
 (N'CONDICION_LABORAL', N'OTRA',          N'Otra', 99),
 (N'OCUPACION', N'PROFESIONAL', N'Profesional', 1),
 (N'OCUPACION', N'TECNICO',     N'Técnico(a)', 2),
 (N'OCUPACION', N'DOCENTE',     N'Docente', 3),
 (N'OCUPACION', N'SALUD',       N'Salud', 4),
 (N'OCUPACION', N'COMERCIANTE', N'Comerciante', 5),
 (N'OCUPACION', N'AGRICULTOR',  N'Agricultor(a) o ganadero(a)', 6),
 (N'OCUPACION', N'OPERARIO',    N'Operario(a) u obrero(a)', 7),
 (N'OCUPACION', N'SERVICIOS',   N'Servicios', 8),
 (N'OCUPACION', N'ADMINISTRATIVO', N'Administrativo(a)', 9),
 (N'OCUPACION', N'OTRA',        N'Otra', 99),
 (N'TIPO_INGRESO', N'SALARIO',    N'Salario', 1),
 (N'TIPO_INGRESO', N'HONORARIOS', N'Honorarios', 2),
 (N'TIPO_INGRESO', N'VENTAS',     N'Ventas del negocio', 3),
 (N'TIPO_INGRESO', N'RENTAS',     N'Rentas o alquileres', 4),
 (N'TIPO_INGRESO', N'PENSION',    N'Pensión', 5),
 (N'TIPO_INGRESO', N'REMESAS',    N'Remesas', 6),
 (N'TIPO_INGRESO', N'INTERESES',  N'Intereses o dividendos', 7),
 (N'TIPO_INGRESO', N'OTRO',       N'Otro', 99),
 (N'PERIODICIDAD_INGRESO', N'MENSUAL', N'Mensual', 1),
 (N'PERIODICIDAD_INGRESO', N'ANUAL',   N'Anual', 2),
 (N'ORIGEN_FONDOS', N'SALARIO',   N'Salario o empleo', 1),
 (N'ORIGEN_FONDOS', N'NEGOCIO',   N'Ventas o actividad comercial', 2),
 (N'ORIGEN_FONDOS', N'AHORROS',   N'Ahorros', 3),
 (N'ORIGEN_FONDOS', N'HERENCIA',  N'Herencia o donación', 4),
 (N'ORIGEN_FONDOS', N'VENTA_ACTIVOS', N'Venta de activos', 5),
 (N'ORIGEN_FONDOS', N'PRESTAMOS', N'Préstamos', 6),
 (N'ORIGEN_FONDOS', N'INVERSIONES', N'Inversiones', 7),
 (N'ORIGEN_FONDOS', N'REMESAS',   N'Remesas', 8),
 (N'ORIGEN_FONDOS', N'OTRO',      N'Otro', 99),
 (N'ORIGEN_PATRIMONIO', N'AHORRO',    N'Ahorro de ingresos', 1),
 (N'ORIGEN_PATRIMONIO', N'HERENCIA',  N'Herencia', 2),
 (N'ORIGEN_PATRIMONIO', N'VENTA_BIENES', N'Venta de bienes', 3),
 (N'ORIGEN_PATRIMONIO', N'UTILIDADES', N'Utilidades retenidas', 4),
 (N'ORIGEN_PATRIMONIO', N'APORTES',   N'Aportes de socios', 5),
 (N'ORIGEN_PATRIMONIO', N'DONACION',  N'Donación', 6),
 (N'ORIGEN_PATRIMONIO', N'OTRO',      N'Otro', 99),
 (N'TIPO_TRANSACCION', N'DEPOSITOS',   N'Depósitos', 1),
 (N'TIPO_TRANSACCION', N'RETIROS',     N'Retiros', 2),
 (N'TIPO_TRANSACCION', N'TRANSF_LOCAL', N'Transferencias locales', 3),
 (N'TIPO_TRANSACCION', N'TRANSF_INTERNACIONAL', N'Transferencias internacionales', 4),
 (N'TIPO_TRANSACCION', N'PAGO_SERVICIOS', N'Pago de servicios', 5),
 (N'TIPO_TRANSACCION', N'PAGO_CREDITO', N'Pago de créditos', 6),
 (N'TIPO_TRANSACCION', N'CHEQUES',     N'Cheques', 7),
 (N'TIPO_TRANSACCION', N'OTRA',        N'Otra', 99),
 (N'FRECUENCIA_TRANSACCION', N'DIARIA',      N'Diaria', 1),
 (N'FRECUENCIA_TRANSACCION', N'SEMANAL',     N'Semanal', 2),
 (N'FRECUENCIA_TRANSACCION', N'QUINCENAL',   N'Quincenal', 3),
 (N'FRECUENCIA_TRANSACCION', N'MENSUAL',     N'Mensual', 4),
 (N'FRECUENCIA_TRANSACCION', N'TRIMESTRAL',  N'Trimestral', 5),
 (N'FRECUENCIA_TRANSACCION', N'ANUAL',       N'Anual', 6),
 (N'FRECUENCIA_TRANSACCION', N'ESPORADICA',  N'Esporádica', 7),
 (N'USO_EFECTIVO', N'NINGUNO', N'Ninguno', 1),
 (N'USO_EFECTIVO', N'BAJO',    N'Bajo', 2),
 (N'USO_EFECTIVO', N'MEDIO',   N'Medio', 3),
 (N'USO_EFECTIVO', N'ALTO',    N'Alto', 4);
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = i.tipo
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

/* Permisos de mantenimiento de las listas nuevas (ADMINISTRADOR_SEGURIDAD) */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_' + lt.codigo, N'Mantener lista de valores: ' + lt.nombre, N'Transaccion', N'Mantenimiento de la lista de valores ' + lt.codigo + N'.', N'sistema'
FROM gen.LISTA_VALORES_TIPO lt
WHERE lt.codigo IN (N'CONDICION_LABORAL', N'OCUPACION', N'TIPO_INGRESO', N'PERIODICIDAD_INGRESO', N'ORIGEN_FONDOS', N'ORIGEN_PATRIMONIO',
                    N'TIPO_TRANSACCION', N'FRECUENCIA_TRANSACCION', N'USO_EFECTIVO')
  AND NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION x WHERE x.codigo = N'LOV_' + lt.codigo);
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'LOV_CONDICION_LABORAL', N'LOV_OCUPACION', N'LOV_TIPO_INGRESO', N'LOV_PERIODICIDAD_INGRESO', N'LOV_ORIGEN_FONDOS',
                       N'LOV_ORIGEN_PATRIMONIO', N'LOV_TIPO_TRANSACCION', N'LOV_FRECUENCIA_TRANSACCION', N'LOV_USO_EFECTIVO')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

/* 3. Codigos de requisito admitidos (sin fila = opcional; ningun rol los exige de entrada) ---------- */
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
    N'REL_REPRESENTANTE_LEGAL', N'REL_BENEFICIARIO_FINAL'));
GO

SELECT lt.codigo AS lista, COUNT(*) AS valores FROM gen.LISTA_VALORES_ITEM i
JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = i.id_lista_valores_tipo
WHERE lt.codigo IN (N'CONDICION_LABORAL', N'OCUPACION', N'TIPO_INGRESO', N'PERIODICIDAD_INGRESO', N'ORIGEN_FONDOS', N'ORIGEN_PATRIMONIO',
                    N'TIPO_TRANSACCION', N'FRECUENCIA_TRANSACCION', N'USO_EFECTIVO') GROUP BY lt.codigo ORDER BY lt.codigo;
GO
