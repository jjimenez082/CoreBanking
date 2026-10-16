/* ============================================================================
   Ronda 26 - Fase 4: Interlocutor GRUPO
   Requiere: relaciones_ronda23.sql, organizacion_ronda22.sql. Re-ejecutable.
   1. cli.INTERLOCUTOR_GRUPO: tipo de grupo, descripcion, fecha de formacion, motivo.
   2. Lista de valores TIPO_GRUPO (+ objeto LOV_TIPO_GRUPO para ADMINISTRADOR_SEGURIDAD).
   ============================================================================ */
SET NOCOUNT ON;
GO
IF COL_LENGTH('cli.INTERLOCUTOR_GRUPO', 'id_tipo_grupo') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_GRUPO ADD id_tipo_grupo bigint NULL;          -- LISTA_VALORES_ITEM (TIPO_GRUPO)
IF COL_LENGTH('cli.INTERLOCUTOR_GRUPO', 'descripcion') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_GRUPO ADD descripcion nvarchar(500) NULL;
IF COL_LENGTH('cli.INTERLOCUTOR_GRUPO', 'fecha_formacion') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_GRUPO ADD fecha_formacion datetime2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_GRUPO')
    INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
    VALUES (N'TIPO_GRUPO', N'Tipo de grupo', N'Clasificacion de los grupos de interlocutores: economico, familiar, solidario, consorcio...', 1, N'sistema');
GO
DECLARE @items TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES
 (N'HOLDING',    N'Holding (sociedad controladora y subsidiarias)', 1),
 (N'ECONOMICO',  N'Grupo económico', 2),
 (N'FAMILIAR',   N'Grupo familiar', 3),
 (N'SOLIDARIO',  N'Grupo solidario', 4),
 (N'CONSORCIO',  N'Consorcio / unión temporal', 5),
 (N'OTRO',       N'Otro', 99);
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'TIPO_GRUPO'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_TIPO_GRUPO', N'Mantener lista de valores: Tipo de grupo', N'Transaccion', N'Mantenimiento de la lista de valores TIPO_GRUPO.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_TIPO_GRUPO');
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
DECLARE @lov bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_TIPO_GRUPO');
IF @adm IS NOT NULL AND @lov IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @adm AND id_objeto_autorizacion = @lov)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@adm, @lov);
GO
/* 3. Los miembros de un grupo no llevan % de participacion (solo funcion). */
UPDATE c SET usa_porcentajes = 0
FROM cli.TIPO_RELACION_CONFIG c
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_item = c.id_tipo_relacion AND i.codigo = N'MIEMBRO_GRUPO'
WHERE c.usa_porcentajes = 1;
GO

SELECT lt.codigo, i.codigo AS item, i.nombre FROM gen.LISTA_VALORES_ITEM i
JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE lt.codigo = N'TIPO_GRUPO' ORDER BY i.orden;
