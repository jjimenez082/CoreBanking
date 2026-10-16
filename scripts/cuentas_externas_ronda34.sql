/* ============================================================================
   Ronda 34 - Cuentas externas del cliente (cuentas en otras instituciones) como parte del perfil financiero.
   Entidad financiera = lista de valores ENTIDAD_FINANCIERA; numero de cuenta = IBAN (se valida en el backend).
   Viajan por la misma gestion de alta / modificacion de datos (con aprobacion). Re-ejecutable.
   Requiere: modelo_fisico_clientes_mvp1.sql, perfil_financiero_ronda29.sql y lov_descripcion_ronda33.sql.
   ============================================================================ */
SET NOCOUNT ON;
GO
IF COL_LENGTH(N'gen.LISTA_VALORES_ITEM', N'descripcion') IS NULL
    THROW 50034, 'Ejecute primero lov_descripcion_ronda33.sql (falta la columna descripcion de las listas de valores).', 1;
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_CUENTA_EXTERNA', N'U') IS NULL
    THROW 50036, 'Falta la tabla cli.INTERLOCUTOR_CUENTA_EXTERNA (modelo_fisico_clientes_mvp1.sql).', 1;
GO
IF COL_LENGTH(N'cli.INTERLOCUTOR_CUENTA_EXTERNA', N'id_moneda') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ADD id_moneda bigint NULL;          -- gen.MONEDA (opcional)
GO
IF COL_LENGTH(N'cli.INTERLOCUTOR_CUENTA_EXTERNA', N'id_entidad') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ADD id_entidad bigint NULL;         -- LISTA_VALORES_ITEM (ENTIDAD_FINANCIERA)
GO
-- La entidad ahora sale de la lista de valores: el texto libre anterior queda como dato historico opcional.
ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ALTER COLUMN entidad_externa nvarchar(200) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_CUENTA_EXTERNA_ENTIDAD')
    ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ADD CONSTRAINT FK_INTERLOCUTOR_CUENTA_EXTERNA_ENTIDAD FOREIGN KEY (id_entidad) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item);
GO
IF COL_LENGTH(N'cli.INTERLOCUTOR_CUENTA_EXTERNA', N'modificado_por') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ADD modificado_por nvarchar(100) NULL;
GO
IF COL_LENGTH(N'cli.INTERLOCUTOR_CUENTA_EXTERNA', N'modificado_en') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ADD modificado_en datetime2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_CUENTA_EXTERNA_MONEDA')
    ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ADD CONSTRAINT FK_INTERLOCUTOR_CUENTA_EXTERNA_MONEDA FOREIGN KEY (id_moneda) REFERENCES gen.MONEDA (id_moneda);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_INTERLOCUTOR_CUENTA_EXTERNA_estado')
    ALTER TABLE cli.INTERLOCUTOR_CUENTA_EXTERNA ADD CONSTRAINT CK_INTERLOCUTOR_CUENTA_EXTERNA_estado CHECK (estado IN ('Activo', 'Inactivo'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_CUENTA_EXTERNA_interlocutor')
    CREATE INDEX IX_INTERLOCUTOR_CUENTA_EXTERNA_interlocutor ON cli.INTERLOCUTOR_CUENTA_EXTERNA (id_interlocutor, estado);
GO
/* Un mismo IBAN no puede repetirse activo en el mismo cliente. Si hay duplicados previos, se omite el indice. */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_CUENTA_EXTERNA_cuenta')
    DROP INDEX UX_INTERLOCUTOR_CUENTA_EXTERNA_cuenta ON cli.INTERLOCUTOR_CUENTA_EXTERNA;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_INTERLOCUTOR_CUENTA_EXTERNA_iban')
   AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_CUENTA_EXTERNA WHERE estado = 'Activo' GROUP BY id_interlocutor, numero_cuenta_externa HAVING COUNT(*) > 1)
    CREATE UNIQUE INDEX UX_INTERLOCUTOR_CUENTA_EXTERNA_iban ON cli.INTERLOCUTOR_CUENTA_EXTERNA (id_interlocutor, numero_cuenta_externa) WHERE estado = 'Activo';
GO

/* Lista de valores TIPO_CUENTA_EXTERNA (con descripciones) */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT N'TIPO_CUENTA_EXTERNA', N'Tipo de cuenta externa', N'Tipo de cuenta que el cliente mantiene en otra institución financiera.', 1, N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_CUENTA_EXTERNA');
GO
DECLARE @items TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int, descripcion nvarchar(500));
INSERT INTO @items VALUES
 (N'AHORROS',   N'Cuenta de ahorros',   1, N'Cuenta de ahorro a la vista en otra institución.'),
 (N'CORRIENTE', N'Cuenta corriente',    2, N'Cuenta corriente o de cheques en otra institución.'),
 (N'PLAZO',     N'Depósito a plazo',    3, N'Inversión o certificado a plazo en otra institución.'),
 (N'BILLETERA', N'Billetera electrónica', 4, N'Billetera o monedero electrónico (fintech, dinero móvil).'),
 (N'TARJETA',   N'Cuenta de tarjeta',   5, N'Cuenta asociada a una tarjeta de crédito o débito.'),
 (N'OTRA',      N'Otra',               99, N'Cualquier otro tipo de cuenta.');
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por, descripcion)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema', i.descripcion
FROM @items i CROSS JOIN gen.LISTA_VALORES_TIPO lt
WHERE lt.codigo = N'TIPO_CUENTA_EXTERNA'
  AND NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO
/* Lista de valores ENTIDAD_FINANCIERA (ejemplos de Costa Rica; se administra en Administracion > Listas de valores) */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT N'ENTIDAD_FINANCIERA', N'Entidad financiera', N'Instituciones financieras donde el cliente mantiene cuentas externas. Agregue las que necesite su operación.', 1, N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'ENTIDAD_FINANCIERA');
GO
DECLARE @ent TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int, descripcion nvarchar(500));
INSERT INTO @ent VALUES
 (N'BNCR',      N'Banco Nacional de Costa Rica', 1, N'Banco estatal.'),
 (N'BCR',       N'Banco de Costa Rica', 2, N'Banco estatal.'),
 (N'BAC',       N'BAC Credomatic', 3, N'Banco privado.'),
 (N'BPOPULAR',  N'Banco Popular y de Desarrollo Comunal', 4, N'Banco estatal.'),
 (N'SCOTIA',    N'Scotiabank de Costa Rica', 5, N'Banco privado.'),
 (N'DAVIVIENDA',N'Banco Davivienda (Costa Rica)', 6, N'Banco privado.'),
 (N'PROMERICA', N'Banco Promerica', 7, N'Banco privado.'),
 (N'LAFISE',    N'Banco Lafise', 8, N'Banco privado.'),
 (N'OTRA',      N'Otra entidad', 99, N'Entidad no listada; agregue la entidad en Administración › Listas de valores.');
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por, descripcion)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema', i.descripcion
FROM @ent i CROSS JOIN gen.LISTA_VALORES_TIPO lt
WHERE lt.codigo = N'ENTIDAD_FINANCIERA'
  AND NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT v.codigo, v.nombre, N'Transaccion', v.descripcion, N'sistema'
FROM (VALUES (N'LOV_TIPO_CUENTA_EXTERNA', N'Mantener lista de valores: Tipo de cuenta externa', N'Mantenimiento de la lista de valores TIPO_CUENTA_EXTERNA.'),
             (N'LOV_ENTIDAD_FINANCIERA', N'Mantener lista de valores: Entidad financiera', N'Mantenimiento de la lista de valores ENTIDAD_FINANCIERA.')) v(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = v.codigo);
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo IN (N'LOV_TIPO_CUENTA_EXTERNA', N'LOV_ENTIDAD_FINANCIERA')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO
SELECT COUNT(*) AS tipos_cuenta_externa FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE t.codigo = N'TIPO_CUENTA_EXTERNA';
GO

/* ============================================================================
   Perfil transaccional: el monto esperado pasa a ser un RANGO por periodo de la frecuencia (minimo y maximo),
   y se retira el campo «Uso esperado de efectivo» (se expresa con una transaccion esperada de tipo efectivo).
   ============================================================================ */
IF COL_LENGTH(N'cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL', N'monto_minimo') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL ADD monto_minimo decimal(18,2) NULL;
GO
IF COL_LENGTH(N'cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL', N'monto_maximo') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL ADD monto_maximo decimal(18,2) NULL;
GO
-- Lo declarado antes como monto unico queda como rango de un solo valor (minimo = maximo); luego se puede ajustar por gestion.
UPDATE cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL SET monto_minimo = monto_esperado, monto_maximo = monto_esperado
WHERE monto_esperado IS NOT NULL AND monto_minimo IS NULL AND monto_maximo IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_INT_PERFTRX_rango')
    ALTER TABLE cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL WITH NOCHECK ADD CONSTRAINT CK_INT_PERFTRX_rango
        CHECK ((monto_minimo IS NULL AND monto_maximo IS NULL) OR (monto_minimo IS NOT NULL AND monto_maximo IS NOT NULL AND monto_minimo >= 0 AND monto_maximo >= monto_minimo));
GO
-- El requisito por rol PT_USO_EFECTIVO ya no existe y aparece CX_CUENTA_EXTERNA (al menos una cuenta externa, configurable por rol).
DELETE FROM cli.TIPO_ROL_REQUISITO WHERE requisito = N'PT_USO_EFECTIVO';
GO
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
    N'PT_PERFIL', N'CX_CUENTA_EXTERNA',
    N'REL_REPRESENTANTE_LEGAL', N'REL_BENEFICIARIO_FINAL',
    N'KYC_ESTADO_COMPLETO', N'KYC_PEP_EVALUADO', N'KYC_SANCIONES_EVALUADO'));
GO
SELECT COUNT(*) AS transacciones_con_rango FROM cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL WHERE monto_minimo IS NOT NULL;
GO
