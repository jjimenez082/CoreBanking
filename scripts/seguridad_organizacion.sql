/* ============================================================================
   SEGURIDAD Y ORGANIZACION - re-ejecutable (sin USE: correr con CoreBancario_DEV seleccionada)
   Requiere: modelo_fisico_transversal_minimo.sql, listas_valores_admin.sql, interlocutor_multiples.sql

   Que hace:
     1. Lista TIPO_UNIDAD_ORG (Sucursal, Agencia, Departamento, Equipo).
     2. sec.DEPARTAMENTO pasa a ser el arbol de UNIDADES ORGANIZATIVAS: codigo, tipo, responsable.
     3. sec.USUARIO: username (usuario de Keycloak).
     4. cli.INTERLOCUTOR.id_unidad: sucursal/unidad a la que pertenece el cliente (igual se hara en cuentas y creditos).
     5. Permisos: ORG_ADMINISTRACION (estructura), GES_APROBAR_TODAS (aprobar gestiones de cualquier solicitante),
        LOV_TIPO_UNIDAD_ORG; se asignan a ADMINISTRADOR_SEGURIDAD.
   Hacer respaldo antes: modifica sec.DEPARTAMENTO, sec.USUARIO y cli.INTERLOCUTOR.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Lista de tipos de unidad */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT N'TIPO_UNIDAD_ORG', N'Tipo de unidad organizativa', N'Clasificacion de las unidades de la estructura organizativa (sucursal, departamento, etc.).', 0, N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_UNIDAD_ORG');

DECLARE @items TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES (N'SUCURSAL', N'Sucursal', 1), (N'AGENCIA', N'Agencia', 2), (N'DEPARTAMENTO', N'Departamento', 3), (N'EQUIPO', N'Equipo', 4);

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i
JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'TIPO_UNIDAD_ORG'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

/* 2. sec.DEPARTAMENTO -> unidades organizativas */
IF COL_LENGTH('sec.DEPARTAMENTO', 'codigo') IS NULL
    ALTER TABLE sec.DEPARTAMENTO ADD codigo nvarchar(20) NULL;
IF COL_LENGTH('sec.DEPARTAMENTO', 'id_tipo_unidad') IS NULL
    ALTER TABLE sec.DEPARTAMENTO ADD id_tipo_unidad bigint NULL;       -- LISTA_VALORES_ITEM (TIPO_UNIDAD_ORG)
IF COL_LENGTH('sec.DEPARTAMENTO', 'id_responsable') IS NULL
    ALTER TABLE sec.DEPARTAMENTO ADD id_responsable bigint NULL;       -- sec.USUARIO
GO

-- Unidades que ya existian: codigo U<id> y tipo Departamento.
UPDATE d SET codigo = N'U' + CONVERT(nvarchar(18), d.id_departamento)
FROM sec.DEPARTAMENTO d WHERE d.codigo IS NULL;

UPDATE d SET id_tipo_unidad = (SELECT TOP 1 i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i
                                JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                                WHERE t.codigo = N'TIPO_UNIDAD_ORG' AND i.codigo = N'DEPARTAMENTO')
FROM sec.DEPARTAMENTO d WHERE d.id_tipo_unidad IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DEPARTAMENTO_TIPO_UNIDAD')
    ALTER TABLE sec.DEPARTAMENTO ADD CONSTRAINT FK_DEPARTAMENTO_TIPO_UNIDAD FOREIGN KEY (id_tipo_unidad) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DEPARTAMENTO_RESPONSABLE')
    ALTER TABLE sec.DEPARTAMENTO ADD CONSTRAINT FK_DEPARTAMENTO_RESPONSABLE FOREIGN KEY (id_responsable) REFERENCES sec.USUARIO (id_usuario);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DEPARTAMENTO_estado')
    ALTER TABLE sec.DEPARTAMENTO ADD CONSTRAINT CK_DEPARTAMENTO_estado CHECK (estado IN ('Activo','Inactivo'));
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'sec.DEPARTAMENTO') AND name = N'codigo' AND is_nullable = 1)
    ALTER TABLE sec.DEPARTAMENTO ALTER COLUMN codigo nvarchar(20) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'sec.DEPARTAMENTO') AND name = N'id_tipo_unidad' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE id_tipo_unidad IS NULL)
    ALTER TABLE sec.DEPARTAMENTO ALTER COLUMN id_tipo_unidad bigint NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_DEPARTAMENTO_codigo' AND object_id = OBJECT_ID(N'sec.DEPARTAMENTO'))
    CREATE UNIQUE INDEX UX_DEPARTAMENTO_codigo ON sec.DEPARTAMENTO (id_compania, codigo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DEPARTAMENTO_padre' AND object_id = OBJECT_ID(N'sec.DEPARTAMENTO'))
    CREATE INDEX IX_DEPARTAMENTO_padre ON sec.DEPARTAMENTO (id_compania, id_departamento_padre);
GO

/* 3. Usuario: username de Keycloak, nombres y apellidos por separado */
IF COL_LENGTH('sec.USUARIO', 'username') IS NULL
    ALTER TABLE sec.USUARIO ADD username nvarchar(100) NULL;
IF COL_LENGTH('sec.USUARIO', 'nombres') IS NULL
    ALTER TABLE sec.USUARIO ADD nombres nvarchar(100) NULL;
IF COL_LENGTH('sec.USUARIO', 'apellidos') IS NULL
    ALTER TABLE sec.USUARIO ADD apellidos nvarchar(100) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_USUARIO_username' AND object_id = OBJECT_ID(N'sec.USUARIO'))
    CREATE UNIQUE INDEX UX_USUARIO_username ON sec.USUARIO (id_compania, username) WHERE username IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_USUARIO_superior' AND object_id = OBJECT_ID(N'sec.USUARIO'))
    CREATE INDEX IX_USUARIO_superior ON sec.USUARIO (id_superior);
GO

/* 4. Unidad del cliente */
IF COL_LENGTH('cli.INTERLOCUTOR', 'id_unidad') IS NULL
    ALTER TABLE cli.INTERLOCUTOR ADD id_unidad bigint NULL;           -- sec.DEPARTAMENTO: sucursal/unidad de origen
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_UNIDAD')
    ALTER TABLE cli.INTERLOCUTOR ADD CONSTRAINT FK_INTERLOCUTOR_UNIDAD FOREIGN KEY (id_unidad) REFERENCES sec.DEPARTAMENTO (id_departamento);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_INTERLOCUTOR_unidad' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR'))
    CREATE INDEX IX_INTERLOCUTOR_unidad ON cli.INTERLOCUTOR (id_compania, id_unidad);
GO

/* 5. Permisos */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT v.codigo, v.nombre, v.tipo, v.descripcion, N'sistema'
FROM (VALUES
    (N'ORG_ADMINISTRACION', N'Administrar estructura organizativa', N'Pantalla', N'Crear y editar unidades (sucursales, departamentos...), su jerarquia y responsable.'),
    (N'GES_APROBAR_TODAS',  N'Aprobar gestiones de cualquier solicitante', N'Transaccion', N'Aprobar o rechazar gestiones aunque el solicitante tenga otro superior directo.')
) AS v(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION oa WHERE oa.codigo = v.codigo);

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'LOV_TIPO_UNIDAD_ORG', N'Mantener lista de valores: Tipo de unidad organizativa', N'Transaccion',
       N'Crear, editar, activar/desactivar y borrar los valores de la lista TIPO_UNIDAD_ORG.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'LOV_TIPO_UNIDAD_ORG');
GO

DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion
    FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo IN (N'ORG_ADMINISTRACION', N'GES_APROBAR_TODAS', N'LOV_TIPO_UNIDAD_ORG')
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao
                       WHERE rao.id_rol_autorizacion = @id_rol AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

/* Verificacion */
SELECT d.id_departamento, d.codigo, d.nombre, d.id_departamento_padre, d.estado FROM sec.DEPARTAMENTO d ORDER BY d.id_departamento;
SELECT id_usuario, nombre_completo, username, nombres, apellidos, id_departamento, id_superior, estado FROM sec.USUARIO;
SELECT codigo FROM sec.OBJETO_AUTORIZACION WHERE codigo IN (N'ORG_ADMINISTRACION', N'GES_APROBAR_TODAS', N'LOV_TIPO_UNIDAD_ORG');
