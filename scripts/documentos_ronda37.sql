/* ============================================================================
   Ronda 37 (r44b) - Documentos del expediente como gestion. Re-ejecutable.
   Requiere: documentos_ronda35.sql y documentos_ronda36.sql.

   - Tipo de gestion CLI_DOCUMENTO (entidad INTERLOCUTOR_DOCUMENTO, llave = id del cliente): agregar, reemplazar y retirar documentos.
   - Regla de aprobacion por defecto: AUTOMATICA (los documentos se aplican de inmediato). Para exigir revision humana se agregan reglas
     con mayor prioridad (menor numero) en Administracion > Reglas de aprobacion, por canal, unidad, usuario, etc.
   - Columnas nuevas en doc.DOCUMENTO (documento destino y motivo del reemplazo) y doc.DOCUMENTO_VERSION (gestion que la origino).
   ============================================================================ */
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'doc.DOCUMENTO', N'id_documento_destino') IS NULL
    ALTER TABLE doc.DOCUMENTO ADD id_documento_destino bigint NULL;
IF COL_LENGTH(N'doc.DOCUMENTO', N'motivo_solicitud') IS NULL
    ALTER TABLE doc.DOCUMENTO ADD motivo_solicitud nvarchar(300) NULL;
IF COL_LENGTH(N'doc.DOCUMENTO_VERSION', N'id_gestion') IS NULL
    ALTER TABLE doc.DOCUMENTO_VERSION ADD id_gestion bigint NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DOCUMENTO_DESTINO')
    ALTER TABLE doc.DOCUMENTO ADD CONSTRAINT FK_DOCUMENTO_DESTINO FOREIGN KEY (id_documento_destino) REFERENCES doc.DOCUMENTO (id_documento);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DOCVER_GESTION')
    ALTER TABLE doc.DOCUMENTO_VERSION ADD CONSTRAINT FK_DOCVER_GESTION FOREIGN KEY (id_gestion) REFERENCES apr.GESTION (id_gestion);
GO

/* Tipo de gestion */
IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_DOCUMENTO')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_DOCUMENTO', N'Documentos del expediente del cliente', N'Campos', 0, 1, N'Cliente', N'sistema');
GO

/* Regla por defecto: aprobacion automatica (una por compania, solo si el tipo aun no tiene reglas) */
DECLARE @tipo bigint = (SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = N'CLI_DOCUMENTO');
INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por)
SELECT c.id_compania, @tipo, N'Por defecto: aprobación automática', 999, N'script ronda37'
FROM gen.COMPANIA c
WHERE NOT EXISTS (SELECT 1 FROM apr.REGLA_APROBACION r WHERE r.id_compania = c.id_compania AND r.id_tipo_gestion = @tipo);

INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador)
SELECT r.id_regla, 1, N'AUTOMATICA'
FROM apr.REGLA_APROBACION r
WHERE r.id_tipo_gestion = @tipo AND r.creado_por = N'script ronda37'
  AND NOT EXISTS (SELECT 1 FROM apr.REGLA_APROBACION_NIVEL n WHERE n.id_regla = r.id_regla);
GO

SELECT tg.codigo, r.id_compania, r.prioridad, r.nombre, n.nivel, n.tipo_aprobador
FROM apr.REGLA_APROBACION r
JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = r.id_tipo_gestion
LEFT JOIN apr.REGLA_APROBACION_NIVEL n ON n.id_regla = r.id_regla
WHERE tg.codigo = N'CLI_DOCUMENTO' ORDER BY r.id_compania, r.prioridad, n.nivel;
GO
