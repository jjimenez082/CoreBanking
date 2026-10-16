/* ============================================================================
   Ronda 20 - Roles del interlocutor: Firmante, Beneficiario, Fiador, Codeudor y Autorizado
   Idempotente. Ejecutar contra CoreBancario_DEV.

   1. Roles activos en la lista TIPO_ROL_INTERLOCUTOR (los cinco, ademas de CLIENTE).
   2. Tabla cli.TIPO_ROL_REQUISITO: requisitos de datos por rol (direccion, correo, telefono). Es la version
      liviana del «formulario por rol»: la fecha de nacimiento, el nombre y la identificacion siempre son obligatorios.
   3. Objetos de autorizacion:
        CLI_ROL             (API)         solicitar agregar/quitar roles de un cliente (via Gestion). Rol ANALISTA_CLIENTES.
        ROL_ADMINISTRACION  (Transaccion) mantener los requisitos por rol. Rol ADMINISTRADOR_SEGURIDAD.
   4. Tipo de gestion CLI_ROL (Transaccional, aprobacion obligatoria, 1 nivel).
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Roles activos --------------------------------------------------------- */
UPDATE i SET estado = N'Activo', modificado_por = N'sistema', modificado_en = SYSUTCDATETIME()
FROM gen.LISTA_VALORES_ITEM i
JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
WHERE t.codigo = N'TIPO_ROL_INTERLOCUTOR'
  AND i.codigo IN (N'CLIENTE', N'BENEFICIARIO', N'FIRMANTE', N'FIADOR', N'CODEUDOR', N'AUTORIZADO')
  AND i.estado <> N'Activo';
GO

/* 2. Requisitos por rol ----------------------------------------------------- */
IF OBJECT_ID(N'cli.TIPO_ROL_REQUISITO', N'U') IS NULL
BEGIN
    CREATE TABLE cli.TIPO_ROL_REQUISITO (
        id_tipo_rol_requisito  bigint IDENTITY(1,1) NOT NULL,
        id_compania            bigint        NOT NULL,
        id_tipo_rol            bigint        NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_ROL_INTERLOCUTOR)
        requisito              nvarchar(30)  NOT NULL,   -- DIRECCION / CORREO / TELEFONO
        es_requerido           bit           NOT NULL CONSTRAINT DF_TIPO_ROL_REQUISITO_req DEFAULT (0),
        creado_por             nvarchar(100) NOT NULL,
        creado_en              datetime2     NOT NULL CONSTRAINT DF_TIPO_ROL_REQUISITO_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por         nvarchar(100) NULL,
        modificado_en          datetime2     NULL,
        CONSTRAINT PK_TIPO_ROL_REQUISITO PRIMARY KEY (id_tipo_rol_requisito),
        CONSTRAINT UQ_TIPO_ROL_REQUISITO UNIQUE (id_compania, id_tipo_rol, requisito),
        CONSTRAINT CK_TIPO_ROL_REQUISITO_codigo CHECK (requisito IN (N'DIRECCION', N'CORREO', N'TELEFONO')),
        CONSTRAINT FK_TIPO_ROL_REQUISITO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_TIPO_ROL_REQUISITO_ROL FOREIGN KEY (id_tipo_rol) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

/* Valores iniciales (solo donde no existe la fila; no pisa lo que el administrador ya cambio):
   Cliente y Beneficiario: sin requisitos extra (como hasta hoy).  Firmante y Autorizado: telefono.
   Fiador y Codeudor: direccion, correo y telefono (mayor debida diligencia). */
DECLARE @def TABLE (rol nvarchar(100), requisito nvarchar(30), requerido bit);
INSERT INTO @def VALUES
 (N'CLIENTE', N'DIRECCION', 0), (N'CLIENTE', N'CORREO', 0), (N'CLIENTE', N'TELEFONO', 0),
 (N'BENEFICIARIO', N'DIRECCION', 0), (N'BENEFICIARIO', N'CORREO', 0), (N'BENEFICIARIO', N'TELEFONO', 0),
 (N'FIRMANTE', N'DIRECCION', 0), (N'FIRMANTE', N'CORREO', 0), (N'FIRMANTE', N'TELEFONO', 1),
 (N'AUTORIZADO', N'DIRECCION', 0), (N'AUTORIZADO', N'CORREO', 0), (N'AUTORIZADO', N'TELEFONO', 1),
 (N'FIADOR', N'DIRECCION', 1), (N'FIADOR', N'CORREO', 1), (N'FIADOR', N'TELEFONO', 1),
 (N'CODEUDOR', N'DIRECCION', 1), (N'CODEUDOR', N'CORREO', 1), (N'CODEUDOR', N'TELEFONO', 1);

INSERT INTO cli.TIPO_ROL_REQUISITO (id_compania, id_tipo_rol, requisito, es_requerido, creado_por)
SELECT c.id_compania, i.id_lista_valores_item, d.requisito, d.requerido, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN @def d
JOIN gen.LISTA_VALORES_TIPO t ON t.codigo = N'TIPO_ROL_INTERLOCUTOR'
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = t.id_lista_valores_tipo AND i.codigo = d.rol
WHERE NOT EXISTS (SELECT 1 FROM cli.TIPO_ROL_REQUISITO x
                   WHERE x.id_compania = c.id_compania AND x.id_tipo_rol = i.id_lista_valores_item AND x.requisito = d.requisito);
GO

/* 3. Objetos de autorizacion ------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_ROL')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'CLI_ROL', N'Solicitar cambio de roles de un cliente', N'API',
            N'Solicitar (via Gestion) agregar o quitar roles (Firmante, Beneficiario, Fiador, Codeudor, Autorizado...) a un cliente existente.', N'sistema');
IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'ROL_ADMINISTRACION')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'ROL_ADMINISTRACION', N'Administracion de requisitos por rol', N'Transaccion',
            N'Definir que datos (direccion, correo, telefono) exige cada rol del interlocutor.', N'sistema');
GO

DECLARE @id_analista bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @id_admin    bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
DECLARE @id_cli_rol  bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_ROL');
DECLARE @id_rol_adm  bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'ROL_ADMINISTRACION');
IF @id_analista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @id_analista AND id_objeto_autorizacion = @id_cli_rol)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@id_analista, @id_cli_rol);
IF @id_admin IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @id_admin AND id_objeto_autorizacion = @id_rol_adm)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@id_admin, @id_rol_adm);
GO

/* 4. Tipo de gestion --------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_ROL')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_ROL', N'Cambio de roles de un cliente', N'Transaccional', 1, 1, N'Cliente', N'sistema');
GO

/* 5. Historial de periodos por rol -------------------------------------------
   fecha_baja = cuando termino el ultimo periodo del rol (NULL si sigue activo).
   cli.INTERLOCUTOR_ROL_PERIODO guarda los periodos ANTERIORES cuando un rol se vuelve a activar
   («fue fiador del 12/03/2026 al 05/09/2026» aunque hoy lo vuelva a ser). */
IF COL_LENGTH('cli.INTERLOCUTOR_ROL', 'fecha_baja') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_ROL ADD fecha_baja datetime2 NULL;
GO
UPDATE cli.INTERLOCUTOR_ROL SET fecha_baja = ISNULL(modificado_en, fecha_asignacion) WHERE estado = 'Inactivo' AND fecha_baja IS NULL;
GO
IF OBJECT_ID('cli.INTERLOCUTOR_ROL_PERIODO') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_ROL_PERIODO (
        id_interlocutor_rol_periodo bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor_rol         bigint        NOT NULL,
        fecha_desde                 datetime2     NOT NULL,
        fecha_hasta                 datetime2     NOT NULL,
        id_gestion                  bigint        NULL,   -- gestion que lo volvio a activar
        creado_por                  nvarchar(100) NOT NULL,
        creado_en                   datetime2     NOT NULL CONSTRAINT DF_INTERLOCUTOR_ROL_PERIODO_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_INTERLOCUTOR_ROL_PERIODO PRIMARY KEY (id_interlocutor_rol_periodo),
        CONSTRAINT FK_INTERLOCUTOR_ROL_PERIODO_ROL FOREIGN KEY (id_interlocutor_rol) REFERENCES cli.INTERLOCUTOR_ROL (id_interlocutor_rol)
    );
    CREATE INDEX IX_INTERLOCUTOR_ROL_PERIODO_rol ON cli.INTERLOCUTOR_ROL_PERIODO (id_interlocutor_rol);
END
GO

/* Verificacion ----------------------------------------------------------------- */
SELECT i.codigo, i.nombre, i.estado FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
WHERE t.codigo = N'TIPO_ROL_INTERLOCUTOR' ORDER BY i.orden;
SELECT i.codigo AS rol, r.requisito, r.es_requerido FROM cli.TIPO_ROL_REQUISITO r JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_item = r.id_tipo_rol ORDER BY i.orden, r.requisito;
SELECT codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion FROM apr.TIPO_GESTION WHERE codigo = N'CLI_ROL';
SELECT COL_LENGTH('cli.INTERLOCUTOR_ROL', 'fecha_baja') AS col_fecha_baja, OBJECT_ID('cli.INTERLOCUTOR_ROL_PERIODO') AS tabla_periodo;
SELECT r.codigo AS rol, oa.codigo AS objeto FROM sec.ROL_AUTORIZACION r
JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = r.id_rol_autorizacion
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion WHERE oa.codigo IN (N'CLI_ROL', N'ROL_ADMINISTRACION');
GO
