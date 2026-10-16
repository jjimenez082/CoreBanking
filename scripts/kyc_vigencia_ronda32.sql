/* ============================================================================
   Ronda 32 - Vigencia del KYC y proxima revision automatica. Re-ejecutable.
   Requiere: kyc_cumplimiento_ronda30.sql y bloqueos_ronda31.sql.

   - Parametros generales (categoria «Cumplimiento (KYC)»): primera revision, anticipacion de «Por vencer», gracia antes del bloqueo
     por KYC y frecuencia de revision segun la clasificacion de riesgo.
   - cli.INTERLOCUTOR_KYC: columnas vigencia y fecha_evaluacion_vigencia (las mantiene el sistema; el proceso de fin de dia las recalculara).
   - cli.INTERLOCUTOR_KYC_HIST: historial de cambios de vigencia y de proxima revision.
   - ESTADO_KYC: «Vencido» deja de ser un estado manual (la vigencia se calcula por fechas); los expedientes que lo tenian pasan a Pendiente.
   - Carga inicial: toda persona u organizacion sin expediente lo recibe (Pendiente) con proxima revision = hoy + meses de primera revision.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Parametros --------------------------------------------------------------------- */
INSERT INTO gen.PARAMETRO (id_compania, codigo, nombre, descripcion, categoria, tipo_dato, valor, valor_defecto, creado_por)
SELECT c.id_compania, v.codigo, v.nombre, v.descripcion, N'Cumplimiento (KYC)', N'Entero', v.valor, v.valor, N'sistema'
FROM gen.COMPANIA c
CROSS JOIN (VALUES
    (N'KYC_MESES_PRIMERA_REVISION', N'KYC: meses hasta la primera revision',
        N'Al crear un cliente, la proxima revision del KYC se fija a estos meses desde la creacion. Aplica mientras no tenga riesgo asignado.', N'12'),
    (N'KYC_MESES_POR_VENCER', N'KYC: meses de anticipacion de «Por vencer»',
        N'El KYC pasa a «Por vencer» cuando faltan estos meses (o menos) para su proxima revision.', N'1'),
    (N'KYC_MESES_GRACIA_BLOQUEO', N'KYC: meses de gracia antes del bloqueo',
        N'Meses, contados desde el vencimiento de la proxima revision, tras los cuales el proceso de fin de dia solicita el bloqueo del cliente por KYC.', N'6'),
    (N'KYC_FRECUENCIA_ALTO', N'KYC: frecuencia de revision, riesgo Alto (meses)',
        N'Meses entre revisiones para clientes con clasificacion de riesgo Alto.', N'12'),
    (N'KYC_FRECUENCIA_MEDIO', N'KYC: frecuencia de revision, riesgo Medio (meses)',
        N'Meses entre revisiones para clientes con clasificacion de riesgo Medio.', N'24'),
    (N'KYC_FRECUENCIA_BAJO', N'KYC: frecuencia de revision, riesgo Bajo (meses)',
        N'Meses entre revisiones para clientes con clasificacion de riesgo Bajo.', N'36')
) AS v(codigo, nombre, descripcion, valor)
WHERE NOT EXISTS (SELECT 1 FROM gen.PARAMETRO p WHERE p.id_compania = c.id_compania AND p.codigo = v.codigo);
GO

/* 2. Columnas de vigencia y tabla de historial ------------------------------------------ */
IF COL_LENGTH(N'cli.INTERLOCUTOR_KYC', N'vigencia') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_KYC ADD vigencia nvarchar(20) NULL;
IF COL_LENGTH(N'cli.INTERLOCUTOR_KYC', N'fecha_evaluacion_vigencia') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_KYC ADD fecha_evaluacion_vigencia date NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_INT_KYC_vigencia')
    ALTER TABLE cli.INTERLOCUTOR_KYC ADD CONSTRAINT CK_INT_KYC_vigencia CHECK (vigencia IS NULL OR vigencia IN (N'Vigente', N'Por vencer', N'Vencido'));
GO

IF OBJECT_ID(N'cli.INTERLOCUTOR_KYC_HIST', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_KYC_HIST (
        id_kyc_hist        bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor    bigint        NOT NULL,
        fecha_proceso      date          NOT NULL,
        origen             nvarchar(40)  NOT NULL,   -- Carga inicial / Alta de cliente / Gestion de KYC / Proceso de fin de dia
        vigencia_anterior  nvarchar(20)  NULL,
        vigencia_nueva     nvarchar(20)  NULL,
        proxima_anterior   date          NULL,
        proxima_nueva      date          NULL,
        id_gestion         bigint        NULL,
        creado_por         nvarchar(100) NOT NULL,
        creado_en          datetime2     NOT NULL CONSTRAINT DF_INT_KYC_HIST_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_INTERLOCUTOR_KYC_HIST PRIMARY KEY (id_kyc_hist),
        CONSTRAINT FK_INT_KYC_HIST_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor)
    );
    CREATE INDEX IX_INT_KYC_HIST_interlocutor ON cli.INTERLOCUTOR_KYC_HIST (id_interlocutor, id_kyc_hist DESC);
END
GO

/* 3. «Vencido» ya no es un estado manual del expediente ---------------------------------- */
DECLARE @idPendiente bigint = (SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                               WHERE t.codigo = N'ESTADO_KYC' AND i.codigo = N'PENDIENTE');
DECLARE @idVencido bigint = (SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                             WHERE t.codigo = N'ESTADO_KYC' AND i.codigo = N'VENCIDO');
IF @idVencido IS NOT NULL AND @idPendiente IS NOT NULL
BEGIN
    UPDATE cli.INTERLOCUTOR_KYC SET id_estado_kyc = @idPendiente, modificado_por = N'sistema', modificado_en = SYSUTCDATETIME() WHERE id_estado_kyc = @idVencido;
    UPDATE gen.LISTA_VALORES_ITEM SET estado = N'Inactivo', modificado_por = N'sistema', modificado_en = SYSUTCDATETIME() WHERE id_lista_valores_item = @idVencido AND estado <> N'Inactivo';
END
GO

/* 4. Carga inicial: expediente y proxima revision para los clientes existentes ------------- */
DECLARE @hoy date = CAST(SYSUTCDATETIME() AS date);
DECLARE @idPendiente2 bigint = (SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                                WHERE t.codigo = N'ESTADO_KYC' AND i.codigo = N'PENDIENTE');

CREATE TABLE #previo (id_interlocutor bigint PRIMARY KEY, proxima date NULL, vigencia nvarchar(20) NULL, tenia_fila bit NOT NULL);
INSERT INTO #previo (id_interlocutor, proxima, vigencia, tenia_fila)
SELECT i.id_interlocutor, k.fecha_proxima_revision, k.vigencia, CASE WHEN k.id_interlocutor IS NULL THEN 0 ELSE 1 END
FROM cli.INTERLOCUTOR i LEFT JOIN cli.INTERLOCUTOR_KYC k ON k.id_interlocutor = i.id_interlocutor
WHERE i.tipo_persona IN (N'Fisica', N'Juridica');

INSERT INTO cli.INTERLOCUTOR_KYC (id_interlocutor, id_estado_kyc, creado_por)
SELECT p.id_interlocutor, @idPendiente2, N'sistema' FROM #previo p WHERE p.tenia_fila = 0;

UPDATE k SET fecha_proxima_revision = DATEADD(MONTH, ISNULL(TRY_CAST(pp.valor AS int), 12), @hoy), modificado_por = N'sistema', modificado_en = SYSUTCDATETIME()
FROM cli.INTERLOCUTOR_KYC k
JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = k.id_interlocutor
LEFT JOIN gen.PARAMETRO pp ON pp.id_compania = i.id_compania AND pp.codigo = N'KYC_MESES_PRIMERA_REVISION'
WHERE k.fecha_proxima_revision IS NULL;

UPDATE k SET
    vigencia = CASE WHEN k.fecha_proxima_revision < @hoy THEN N'Vencido'
                    WHEN k.fecha_proxima_revision <= DATEADD(MONTH, ISNULL(TRY_CAST(pv.valor AS int), 1), @hoy) THEN N'Por vencer'
                    ELSE N'Vigente' END,
    fecha_evaluacion_vigencia = @hoy
FROM cli.INTERLOCUTOR_KYC k
JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = k.id_interlocutor
LEFT JOIN gen.PARAMETRO pv ON pv.id_compania = i.id_compania AND pv.codigo = N'KYC_MESES_POR_VENCER'
WHERE k.vigencia IS NULL;

INSERT INTO cli.INTERLOCUTOR_KYC_HIST (id_interlocutor, fecha_proceso, origen, vigencia_anterior, vigencia_nueva, proxima_anterior, proxima_nueva, creado_por)
SELECT k.id_interlocutor, @hoy, N'Carga inicial', p.vigencia, k.vigencia, p.proxima, k.fecha_proxima_revision, N'sistema'
FROM #previo p JOIN cli.INTERLOCUTOR_KYC k ON k.id_interlocutor = p.id_interlocutor
WHERE p.vigencia IS NULL;   -- solo los que aun no tenian vigencia (la carga es re-ejecutable)
DROP TABLE #previo;
GO
