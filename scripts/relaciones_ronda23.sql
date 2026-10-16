/* ============================================================================
   Ronda 23 - Fase 2: RELACIONES entre interlocutores
   Requiere: organizacion_ronda22.sql. Re-ejecutable.

   1. Lista TIPO_RELACION_INTERLOCUTOR con los tipos iniciales (+ permiso LOV_ para ADMINISTRADOR_SEGURIDAD).
   2. cli.TIPO_RELACION_CONFIG: etiqueta inversa, si usa porcentajes, si es simetrica y que tipos de interlocutor admite cada extremo.
   3. cli.INTERLOCUTOR_RELACION: porcentajes de voto y control, principal, funcion, auditoria y gestion que la abrio / cerro.
   4. Permiso CLI_RELACION (rol ANALISTA_CLIENTES) y tipo de gestion CLI_RELACION (aprobacion obligatoria, 1 nivel).
   Origen = quien «es X de» el destino. Ej.: Maria (origen) es Representante legal de Casa del Core (destino).
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Lista de tipos de relacion ---------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_RELACION_INTERLOCUTOR')
    INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
    VALUES (N'TIPO_RELACION_INTERLOCUTOR', N'Tipo de relacion entre interlocutores',
            N'Representante legal, accionista, beneficiario final, persona de contacto, miembro de grupo, familiar.', 1, N'sistema');
GO

DECLARE @items TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES
 (N'REPRESENTANTE_LEGAL', N'Representante legal de', 1),
 (N'ACCIONISTA',          N'Accionista / propietario de', 2),
 (N'BENEFICIARIO_FINAL',  N'Beneficiario final de', 3),
 (N'PERSONA_CONTACTO',    N'Persona de contacto de', 4),
 (N'MIEMBRO_GRUPO',       N'Miembro del grupo', 5),
 (N'FAMILIAR',            N'Familiar de', 6);
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 1, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'TIPO_RELACION_INTERLOCUTOR'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_TIPO_RELACION_INTERLOCUTOR', N'Mantener lista de valores: Tipo de relacion entre interlocutores', N'Transaccion',
       N'Mantenimiento de la lista de valores TIPO_RELACION_INTERLOCUTOR.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_TIPO_RELACION_INTERLOCUTOR');
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
DECLARE @lov bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_TIPO_RELACION_INTERLOCUTOR');
IF @adm IS NOT NULL AND @lov IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @adm AND id_objeto_autorizacion = @lov)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@adm, @lov);
GO

/* 2. Configuracion por tipo de relacion -------------------------------------- */
IF OBJECT_ID(N'cli.TIPO_RELACION_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE cli.TIPO_RELACION_CONFIG (
        id_tipo_relacion   bigint        NOT NULL,   -- LISTA_VALORES_ITEM de TIPO_RELACION_INTERLOCUTOR
        etiqueta_inversa   nvarchar(100) NOT NULL,   -- como se lee desde el destino
        usa_porcentajes    bit           NOT NULL CONSTRAINT DF_TRC_usa_pct DEFAULT (0),
        es_simetrica       bit           NOT NULL CONSTRAINT DF_TRC_simetrica DEFAULT (0),
        tipos_origen       nvarchar(60)  NOT NULL,   -- lista separada por comas: Fisica,Juridica,Grupo
        tipos_destino      nvarchar(60)  NOT NULL,
        creado_por         nvarchar(100) NOT NULL,
        creado_en          datetime2     NOT NULL CONSTRAINT DF_TRC_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_TIPO_RELACION_CONFIG PRIMARY KEY (id_tipo_relacion),
        CONSTRAINT FK_TIPO_RELACION_CONFIG_item FOREIGN KEY (id_tipo_relacion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO
DECLARE @cfg TABLE (codigo nvarchar(100), inversa nvarchar(100), pct bit, simetrica bit, origen nvarchar(60), destino nvarchar(60));
INSERT INTO @cfg VALUES
 (N'REPRESENTANTE_LEGAL', N'Tiene como representante legal', 0, 0, N'Fisica',          N'Juridica'),
 (N'ACCIONISTA',          N'Tiene como accionista',          1, 0, N'Fisica,Juridica', N'Juridica'),
 (N'BENEFICIARIO_FINAL',  N'Tiene como beneficiario final',  1, 0, N'Fisica',          N'Juridica'),
 (N'PERSONA_CONTACTO',    N'Tiene como persona de contacto', 0, 0, N'Fisica',          N'Fisica,Juridica'),
 (N'MIEMBRO_GRUPO',       N'Tiene como miembro',             1, 0, N'Fisica,Juridica', N'Grupo'),
 (N'FAMILIAR',            N'Familiar de',                    0, 1, N'Fisica',          N'Fisica');
INSERT INTO cli.TIPO_RELACION_CONFIG (id_tipo_relacion, etiqueta_inversa, usa_porcentajes, es_simetrica, tipos_origen, tipos_destino, creado_por)
SELECT i.id_lista_valores_item, c.inversa, c.pct, c.simetrica, c.origen, c.destino, N'sistema'
FROM @cfg c
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'TIPO_RELACION_INTERLOCUTOR'
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_tipo = lt.id_lista_valores_tipo AND i.codigo = c.codigo
WHERE NOT EXISTS (SELECT 1 FROM cli.TIPO_RELACION_CONFIG x WHERE x.id_tipo_relacion = i.id_lista_valores_item);
GO

/* 3. Mas datos en la relacion ------------------------------------------------ */
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'porcentaje_voto') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD porcentaje_voto decimal(5,2) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'porcentaje_control') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD porcentaje_control decimal(5,2) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'es_principal') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD es_principal bit NOT NULL CONSTRAINT DF_INTERLOCUTOR_RELACION_principal DEFAULT (0);
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'funcion') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD funcion nvarchar(200) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'creado_por') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD creado_por nvarchar(100) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'creado_en') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD creado_en datetime2 NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'cerrado_por') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD cerrado_por nvarchar(100) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'id_gestion_alta') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD id_gestion_alta bigint NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_RELACION', 'id_gestion_baja') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD id_gestion_baja bigint NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_INTERLOCUTOR_RELACION_porcentajes' AND parent_object_id = OBJECT_ID(N'cli.INTERLOCUTOR_RELACION'))
    ALTER TABLE cli.INTERLOCUTOR_RELACION ADD CONSTRAINT CK_INTERLOCUTOR_RELACION_porcentajes CHECK (
        (porcentaje_participacion IS NULL OR porcentaje_participacion BETWEEN 0 AND 100)
    AND (porcentaje_voto          IS NULL OR porcentaje_voto          BETWEEN 0 AND 100)
    AND (porcentaje_control       IS NULL OR porcentaje_control       BETWEEN 0 AND 100));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_RELACION_origen' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_RELACION'))
    CREATE INDEX IX_INTERLOCUTOR_RELACION_origen ON cli.INTERLOCUTOR_RELACION (id_interlocutor_origen, id_tipo_relacion) WHERE fecha_fin IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_RELACION_destino' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_RELACION'))
    CREATE INDEX IX_INTERLOCUTOR_RELACION_destino ON cli.INTERLOCUTOR_RELACION (id_interlocutor_destino, id_tipo_relacion) WHERE fecha_fin IS NULL;
GO

/* 4. Permiso y tipo de gestion ----------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_RELACION')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'CLI_RELACION', N'Solicitar cambio de relaciones de un interlocutor', N'API',
            N'Solicitar (via Gestion) agregar o cerrar relaciones (representante legal, accionista, miembro de grupo...) de un interlocutor.', N'sistema');
GO
DECLARE @analista bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @obj bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_RELACION');
IF @analista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @analista AND id_objeto_autorizacion = @obj)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@analista, @obj);
GO
IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_RELACION')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_RELACION', N'Cambio de relaciones de un interlocutor', N'Transaccional', 1, 1, N'Cliente', N'sistema');
GO

/* Verificacion ----------------------------------------------------------------- */
SELECT i.codigo, i.nombre, c.etiqueta_inversa, c.usa_porcentajes, c.es_simetrica, c.tipos_origen, c.tipos_destino
FROM cli.TIPO_RELACION_CONFIG c JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_item = c.id_tipo_relacion ORDER BY i.orden;
SELECT codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion FROM apr.TIPO_GESTION WHERE codigo = N'CLI_RELACION';
GO
