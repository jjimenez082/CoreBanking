/* ============================================================================
   MOTOR DE NUMERACION (rangos tipo SAP) - re-ejecutable
   Base: CoreBancario_<codigo>  (ejecutar con USE <su base> antes)

   Decisiones (2026-10-05):
     - El numero de negocio se asigna al FINAL del proceso, justo antes de
       crear el registro definitivo (el borrador no lleva numero).
     - Llave interna (id_interlocutor) distinta del numero de negocio.
     - Longitud fija por objeto; rangos por compania; se aceptan huecos
       (bloques en cache, o altas que fallan despues de reservar).
     - Sin digito verificador.
     - Objetos: CLIENTE, PROSPECTO, SOLICITUD_CREDITO (sin alcance) y, cuando
       existan los productos operativos, CUENTA y CREDITO con un rango por
       producto (columna id_alcance = id del producto; sin repetirse entre productos).

   Que hace este script:
     1. Crea gen.RANGO_NUMERACION.
     2. Agrega cli.INTERLOCUTOR.numero y numero_prospecto_origen (referencia al
       numero de prospecto cuando un prospecto se convierte en cliente).
     3. Siembra los rangos CLIENTE y PROSPECTO (8 digitos) por compania y
       numera a los clientes existentes con su id (rellenado a 8 digitos).
     4. Indice unico (compania, numero) y permiso NUM_ADMINISTRACION.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   1. Tabla de rangos
   --------------------------------------------------------------------------- */
IF OBJECT_ID(N'gen.RANGO_NUMERACION', N'U') IS NULL
BEGIN
    CREATE TABLE gen.RANGO_NUMERACION (
        id_rango_numeracion  bigint IDENTITY(1,1) NOT NULL,
        id_compania          bigint        NOT NULL,
        objeto               nvarchar(50)  NOT NULL,           -- CLIENTE / PROSPECTO / SOLICITUD_CREDITO / CUENTA / CREDITO
        id_alcance           bigint        NOT NULL CONSTRAINT DF_RANGO_NUMERACION_alcance DEFAULT (0), -- 0 = toda la compania; >0 = id del producto operativo
        longitud             tinyint       NOT NULL,           -- digitos fijos (rellena con ceros a la izquierda)
        numero_desde         bigint        NOT NULL,
        numero_hasta         bigint        NOT NULL,
        ultimo_asignado      bigint        NOT NULL,           -- ultimo numero reservado (cache incluido)
        tamano_bloque        int           NOT NULL CONSTRAINT DF_RANGO_NUMERACION_bloque DEFAULT (1),
        estado               nvarchar(20)  NOT NULL CONSTRAINT DF_RANGO_NUMERACION_estado DEFAULT ('Activo'),
        creado_por           nvarchar(100) NOT NULL,
        creado_en            datetime2     NOT NULL CONSTRAINT DF_RANGO_NUMERACION_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por       nvarchar(100) NULL,
        modificado_en        datetime2     NULL,
        CONSTRAINT PK_RANGO_NUMERACION PRIMARY KEY (id_rango_numeracion),
        CONSTRAINT FK_RANGO_NUMERACION_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT CK_RANGO_NUMERACION_estado CHECK (estado IN ('Activo','Inactivo')),
        CONSTRAINT CK_RANGO_NUMERACION_longitud CHECK (longitud BETWEEN 1 AND 18),
        CONSTRAINT CK_RANGO_NUMERACION_limites CHECK (numero_desde >= 1 AND numero_hasta >= numero_desde
                                                      AND ultimo_asignado >= numero_desde - 1 AND ultimo_asignado <= numero_hasta),
        CONSTRAINT CK_RANGO_NUMERACION_bloque CHECK (tamano_bloque BETWEEN 1 AND 100000)
    );
END
GO

-- Un solo rango activo por (compania, objeto, alcance).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RANGO_NUMERACION_activo' AND object_id = OBJECT_ID(N'gen.RANGO_NUMERACION'))
    CREATE UNIQUE INDEX UX_RANGO_NUMERACION_activo
        ON gen.RANGO_NUMERACION (id_compania, objeto, id_alcance) WHERE estado = 'Activo';
GO

/* ---------------------------------------------------------------------------
   2. Numero de negocio en el interlocutor
   --------------------------------------------------------------------------- */
IF COL_LENGTH('cli.INTERLOCUTOR', 'numero') IS NULL
    ALTER TABLE cli.INTERLOCUTOR ADD numero nvarchar(30) NULL;
GO
IF COL_LENGTH('cli.INTERLOCUTOR', 'numero_prospecto_origen') IS NULL
    ALTER TABLE cli.INTERLOCUTOR ADD numero_prospecto_origen nvarchar(30) NULL;
GO

/* ---------------------------------------------------------------------------
   3. Siembra de rangos y numeracion de los clientes existentes
   --------------------------------------------------------------------------- */
INSERT INTO gen.RANGO_NUMERACION (id_compania, objeto, longitud, numero_desde, numero_hasta, ultimo_asignado, tamano_bloque, creado_por)
SELECT c.id_compania, o.objeto, 8, 1, 99999999, 0, 1, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES (N'CLIENTE'), (N'PROSPECTO')) AS o(objeto)
WHERE NOT EXISTS (SELECT 1 FROM gen.RANGO_NUMERACION r
                   WHERE r.id_compania = c.id_compania AND r.objeto = o.objeto AND r.id_alcance = 0 AND r.estado = 'Activo');
GO

-- Clientes existentes: numero = su id interno rellenado a la longitud del rango CLIENTE.
UPDATE i
SET numero = RIGHT(REPLICATE('0', r.longitud) + CONVERT(varchar(20), i.id_interlocutor), r.longitud)
FROM cli.INTERLOCUTOR i
JOIN gen.RANGO_NUMERACION r ON r.id_compania = i.id_compania AND r.objeto = N'CLIENTE' AND r.id_alcance = 0 AND r.estado = 'Activo'
WHERE i.numero IS NULL;
GO

-- El rango sigue a partir del mayor numero ya usado.
UPDATE r
SET ultimo_asignado = x.maximo
FROM gen.RANGO_NUMERACION r
JOIN (SELECT id_compania, MAX(CONVERT(bigint, numero)) AS maximo FROM cli.INTERLOCUTOR GROUP BY id_compania) x
  ON x.id_compania = r.id_compania
WHERE r.objeto = N'CLIENTE' AND r.id_alcance = 0 AND r.ultimo_asignado < x.maximo AND x.maximo <= r.numero_hasta;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'cli.INTERLOCUTOR') AND name = N'numero' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR WHERE numero IS NULL)
    ALTER TABLE cli.INTERLOCUTOR ALTER COLUMN numero nvarchar(30) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_numero' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR'))
    CREATE UNIQUE INDEX UX_INTERLOCUTOR_numero ON cli.INTERLOCUTOR (id_compania, numero);
GO

/* ---------------------------------------------------------------------------
   4. Permiso de la pantalla "Rangos de numeracion" (Administracion)
   --------------------------------------------------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'NUM_ADMINISTRACION', N'Administrar rangos de numeracion', N'Pantalla',
       N'Crear y editar los rangos de numeracion (clientes, prospectos, solicitudes, cuentas, creditos).', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'NUM_ADMINISTRACION');
GO

DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo = N'NUM_ADMINISTRACION'
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao
                       WHERE rao.id_rol_autorizacion = @id_rol AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT objeto, id_alcance, longitud, numero_desde, numero_hasta, ultimo_asignado, tamano_bloque, estado FROM gen.RANGO_NUMERACION;
SELECT id_interlocutor, numero FROM cli.INTERLOCUTOR ORDER BY id_interlocutor;
