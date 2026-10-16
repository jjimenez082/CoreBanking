/* ============================================================================
   Ronda 31 - Bloqueo de clientes (carril propio) + carril propio del KYC. Re-ejecutable.
   Requiere: kyc_cumplimiento_ronda30.sql.

   - cli.INTERLOCUTOR_BLOQUEO: historial de bloqueos (Vigente / Levantado). Un cliente puede tener varios vigentes (uno por tipo).
   - Lista de valores TIPO_BLOQUEO (KYC, Cumplimiento, Fraude, Ente externo, Otro).
   - Tipos de gestion CLI_BLOQUEO y CLI_DESBLOQUEO (aprobacion obligatoria, 1 nivel). Entidad de gestion INTERLOCUTOR_BLOQUEO,
     llave «idCliente|idTipo»: no choca con las gestiones de datos (INTERLOCUTOR) ni de KYC (INTERLOCUTOR_KYC).
   - Permisos CLI_BLOQUEO_VER y CLI_BLOQUEO; se asignan al rol Oficial de Cumplimiento (y VER a Analista y Aprobador).
   - Las gestiones CLI_KYC existentes pasan a la entidad INTERLOCUTOR_KYC (carril propio).
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Tabla ------------------------------------------------------------------------ */
IF OBJECT_ID(N'cli.INTERLOCUTOR_BLOQUEO', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_BLOQUEO (
        id_bloqueo               bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor          bigint         NOT NULL,
        id_tipo_bloqueo          bigint         NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_BLOQUEO)
        comentario               nvarchar(500)  NOT NULL,
        estado                   nvarchar(20)   NOT NULL CONSTRAINT DF_INT_BLOQ_estado DEFAULT ('Vigente'),
        fecha_bloqueo            datetime2      NOT NULL CONSTRAINT DF_INT_BLOQ_fecha DEFAULT (SYSUTCDATETIME()),
        bloqueado_por            nvarchar(100)  NOT NULL,
        id_gestion_bloqueo       bigint         NULL,
        fecha_levantamiento      datetime2      NULL,
        comentario_levantamiento nvarchar(500)  NULL,
        levantado_por            nvarchar(100)  NULL,
        id_gestion_levantamiento bigint         NULL,
        CONSTRAINT PK_INTERLOCUTOR_BLOQUEO PRIMARY KEY (id_bloqueo),
        CONSTRAINT CK_INT_BLOQ_estado CHECK (estado IN ('Vigente', 'Levantado')),
        CONSTRAINT CK_INT_BLOQ_levantado CHECK ((estado = 'Vigente' AND fecha_levantamiento IS NULL) OR (estado = 'Levantado' AND fecha_levantamiento IS NOT NULL)),
        CONSTRAINT FK_INT_BLOQ_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_BLOQ_TIPO FOREIGN KEY (id_tipo_bloqueo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE UNIQUE INDEX UX_INT_BLOQ_vigente ON cli.INTERLOCUTOR_BLOQUEO (id_interlocutor, id_tipo_bloqueo) WHERE estado = 'Vigente';
    CREATE INDEX IX_INT_BLOQ_interlocutor ON cli.INTERLOCUTOR_BLOQUEO (id_interlocutor, estado);
END
GO

/* 2. Lista de valores ----------------------------------------------------------------- */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT N'TIPO_BLOQUEO', N'Tipo de bloqueo de cliente', N'Motivo por el que se bloquea a un cliente (KYC, cumplimiento, fraude, ente externo...).', 1, N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_BLOQUEO');

DECLARE @items TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES
 (N'KYC',           N'KYC vencido o incompleto', 1),
 (N'CUMPLIMIENTO',  N'Cumplimiento', 2),
 (N'FRAUDE',        N'Fraude', 3),
 (N'ENTE_EXTERNO',  N'Orden de ente externo', 4),
 (N'OTRO',          N'Otro', 99);
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'TIPO_BLOQUEO'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

/* 3. Tipos de gestion ----------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_BLOQUEO')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_BLOQUEO', N'Bloqueo de cliente', N'Campos', 1, 1, N'Cliente', N'sistema');
IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_DESBLOQUEO')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_DESBLOQUEO', N'Desbloqueo de cliente', N'Campos', 1, 1, N'Cliente', N'sistema');
GO

/* Las gestiones de KYC ya creadas pasan a su carril propio */
UPDATE g SET entidad_negocio = N'INTERLOCUTOR_KYC'
FROM apr.GESTION g JOIN apr.TIPO_GESTION t ON t.id_tipo_gestion = g.id_tipo_gestion
WHERE t.codigo = N'CLI_KYC' AND g.entidad_negocio = N'INTERLOCUTOR';
GO

/* 4. Seguridad ------------------------------------------------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, x.tipo, x.descripcion, N'sistema' FROM (VALUES
  (N'CLI_BLOQUEO_VER', N'Ver bloqueos de clientes', N'API', N'Consultar los bloqueos vigentes e historial de un cliente y la lista de clientes bloqueados.'),
  (N'CLI_BLOQUEO',     N'Solicitar bloqueo y desbloqueo de clientes', N'API', N'Solicitar (vía Gestión) el bloqueo o el desbloqueo de un cliente por tipo de bloqueo.')
) x(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_TIPO_BLOQUEO', N'Mantener lista de valores: Tipo de bloqueo de cliente', N'Transaccion', N'Mantenimiento de la lista de valores TIPO_BLOQUEO.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_TIPO_BLOQUEO');
GO

DECLARE @of  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'OFICIAL_CUMPLIMIENTO' AND id_compania IS NULL);
DECLARE @an  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @ap  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'APROBADOR_GESTIONES' AND id_compania IS NULL);
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
DECLARE @map TABLE (rol bigint, objeto nvarchar(100));
INSERT INTO @map VALUES (@of, N'CLI_BLOQUEO'), (@of, N'CLI_BLOQUEO_VER'), (@an, N'CLI_BLOQUEO_VER'), (@ap, N'CLI_BLOQUEO_VER'), (@adm, N'LOV_TIPO_BLOQUEO');
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT m.rol, o.id_objeto_autorizacion FROM @map m JOIN sec.OBJETO_AUTORIZACION o ON o.codigo = m.objeto
WHERE m.rol IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = m.rol AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

SELECT t.codigo, COUNT(*) AS valores FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
WHERE t.codigo = N'TIPO_BLOQUEO' GROUP BY t.codigo;
GO
