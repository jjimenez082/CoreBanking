/* ============================================================================
   Ronda 44 - Entidad (agrupacion) de los tipos de gestion (PROPUESTA, para revision). Re-ejecutable.
   Requiere: lov_descripcion_ronda33.sql, administracion_transversal.sql.

   Objetivo: las reglas de aprobacion (y la bandeja de gestiones) crecen con cada modulo; cada tipo de gestion se
   asocia a una ENTIDAD (Clientes, Productos, Reglas...) para poder agruparlas y filtrarlas.

   - Lista de valores del sistema ENTIDAD_GESTION (administrable: se agregan entidades desde Listas de valores).
   - apr.TIPO_GESTION.id_entidad (FK a gen.LISTA_VALORES_ITEM), NULL = sin entidad ("General").
   - Se asigna a los tipos existentes segun el prefijo de su codigo. Los tipos nuevos de rondas futuras la declaran al sembrarse.
   ============================================================================ */

/* 1. Lista de valores */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, 1, N'sistema' FROM (VALUES
  (N'ENTIDAD_GESTION', N'Entidad de la gestion', N'Agrupa los tipos de gestion (y sus reglas de aprobacion) por entidad de negocio.')
) x(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO t WHERE t.codigo = x.codigo);
GO

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, descripcion, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, i.descripcion, N'sistema'
FROM (VALUES
  (N'CLIENTES',       N'Clientes',                 1, N'Interlocutores: alta, edicion, KYC, bloqueos, consentimientos, roles, relaciones, documentos, fusion, fallecimiento.'),
  (N'CUENTAS',        N'Cuentas',                  2, N'Apertura y mantenimiento de cuentas.'),
  (N'CREDITOS',       N'Creditos',                 3, N'Solicitudes, creditos, desembolsos, restructuras.'),
  (N'PRODUCTOS',      N'Productos',                4, N'Pricing Studio: productos, versiones, indicadores.'),
  (N'REGLAS',         N'Motor de reglas',          5, N'Publicacion de conjuntos de reglas.'),
  (N'ADMIN_TRANSVERSAL', N'Administracion transversal', 6, N'Monedas, tipos de cambio, calendarios, parametros.'),
  (N'GENERAL',        N'General',                  9, N'Sin entidad especifica.')
) i(codigo, nombre, orden, descripcion)
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'ENTIDAD_GESTION'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

/* 2. Columna y FK */
IF COL_LENGTH(N'apr.TIPO_GESTION', N'id_entidad') IS NULL
    ALTER TABLE apr.TIPO_GESTION ADD id_entidad bigint NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TIPO_GESTION_ENTIDAD')
    ALTER TABLE apr.TIPO_GESTION ADD CONSTRAINT FK_TIPO_GESTION_ENTIDAD
        FOREIGN KEY (id_entidad) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item);
GO

/* 3. Asignacion de los tipos existentes (solo los que aun no tienen entidad) */
;WITH ent AS (
    SELECT i.codigo, i.id_lista_valores_item AS id
    FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo AND t.codigo = N'ENTIDAD_GESTION'
)
UPDATE g SET id_entidad = (SELECT id FROM ent WHERE ent.codigo =
        CASE WHEN g.codigo LIKE N'CLI[_]%' OR g.codigo LIKE N'INTERLOCUTOR%' THEN N'CLIENTES'
             WHEN g.codigo LIKE N'CTA[_]%'                                   THEN N'CUENTAS'
             WHEN g.codigo LIKE N'CRE[_]%' OR g.codigo LIKE N'SOL[_]%'       THEN N'CREDITOS'
             WHEN g.codigo LIKE N'PRD[_]%'                                   THEN N'PRODUCTOS'
             WHEN g.codigo LIKE N'RUL[_]%'                                   THEN N'REGLAS'
             WHEN g.codigo LIKE N'TC[_]%' OR g.codigo LIKE N'MON[_]%' OR g.codigo LIKE N'CAL[_]%' OR g.codigo LIKE N'PAR[_]%' OR g.codigo LIKE N'LOV[_]%' THEN N'ADMIN_TRANSVERSAL'
             ELSE N'GENERAL' END)
FROM apr.TIPO_GESTION g
WHERE g.id_entidad IS NULL;
GO

SELECT e.nombre AS entidad, g.codigo, g.nombre
FROM apr.TIPO_GESTION g LEFT JOIN gen.LISTA_VALORES_ITEM e ON e.id_lista_valores_item = g.id_entidad
ORDER BY e.orden, g.codigo;
GO
PRINT N'Ronda 44 (entidad de tipos de gestion) aplicada.';
GO
