/* ============================================================================
   Ronda 18 - Fusion de clientes duplicados (CLI-020)
   Idempotente. Ejecutar contra CoreBancario_DEV.

   Principio: una fusion SIEMPRE es una gestion (CLI_FUSION) con permiso propio. El cliente
   que se absorbe NUNCA se borra: queda 'Fusionado' apuntando al sobreviviente (alias).

   Que hace:
   1. Objeto de autorizacion CLI_FUSION (solicitar fusion de clientes). Rol ANALISTA_CLIENTES.
   2. Tipo de gestion CLI_FUSION (Transaccional, aprobacion obligatoria, 1 nivel).
   3. Tabla cli.INTERLOCUTOR_FUSION: bitacora de cada fusion aplicada (que se traslado, que se descarto).
   4. Indice por sobreviviente en cli.INTERLOCUTOR.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_FUSION')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'CLI_FUSION', N'Solicitar fusion de clientes', N'API',
            N'Solicitar (via Gestion) la fusion de dos clientes duplicados: el absorbido queda como Fusionado y apunta al sobreviviente.', N'sistema');
GO

DECLARE @id_analista bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @id_fusion   bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_FUSION');
IF @id_analista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @id_analista AND id_objeto_autorizacion = @id_fusion)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@id_analista, @id_fusion);
GO

IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_FUSION')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_FUSION', N'Fusion de clientes duplicados', N'Transaccional', 1, 1, N'Cliente', N'sistema');
GO

/* Bitacora de fusiones aplicadas ------------------------------------------------ */
IF OBJECT_ID(N'cli.INTERLOCUTOR_FUSION', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_FUSION (
        id_fusion              bigint IDENTITY(1,1) NOT NULL,
        id_compania            bigint         NOT NULL,
        id_gestion             bigint         NOT NULL,
        id_sobreviviente       bigint         NOT NULL,
        id_fusionado           bigint         NOT NULL,
        motivo                 nvarchar(500)  NOT NULL,
        resumen                nvarchar(max)  NOT NULL,   -- JSON: cuantos registros se trasladaron / descartaron por coleccion
        aplicada_por           nvarchar(100)  NOT NULL,
        aplicada_en            datetime2      NOT NULL CONSTRAINT DF_INTERLOCUTOR_FUSION_aplicada_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_INTERLOCUTOR_FUSION PRIMARY KEY (id_fusion),
        CONSTRAINT CK_INTERLOCUTOR_FUSION_json CHECK (ISJSON(resumen) = 1),
        CONSTRAINT CK_INTERLOCUTOR_FUSION_distintos CHECK (id_sobreviviente <> id_fusionado),
        CONSTRAINT FK_INTERLOCUTOR_FUSION_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_INTERLOCUTOR_FUSION_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion),
        CONSTRAINT FK_INTERLOCUTOR_FUSION_sobreviviente FOREIGN KEY (id_sobreviviente) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INTERLOCUTOR_FUSION_fusionado FOREIGN KEY (id_fusionado) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
    );
END
GO

-- Un cliente solo puede ser absorbido una vez.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_FUSION_fusionado' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_FUSION'))
    CREATE UNIQUE INDEX UX_INTERLOCUTOR_FUSION_fusionado ON cli.INTERLOCUTOR_FUSION (id_fusionado);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_FUSION_sobreviviente' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_FUSION'))
    CREATE INDEX IX_INTERLOCUTOR_FUSION_sobreviviente ON cli.INTERLOCUTOR_FUSION (id_sobreviviente);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_sobreviviente' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR'))
    CREATE INDEX IX_INTERLOCUTOR_sobreviviente ON cli.INTERLOCUTOR (id_interlocutor_sobreviviente) WHERE id_interlocutor_sobreviviente IS NOT NULL;
GO

/* Verificacion ------------------------------------------------------------ */
SELECT codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion FROM apr.TIPO_GESTION WHERE codigo = N'CLI_FUSION';
SELECT r.codigo AS rol, oa.codigo AS objeto
FROM sec.ROL_AUTORIZACION r
JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = r.id_rol_autorizacion
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
WHERE oa.codigo = N'CLI_FUSION';
GO
