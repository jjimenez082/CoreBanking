/* ============================================================================
   Ronda 13 - Prospectos y validacion de identificacion
   Ejecutar en CoreBancario_DEV. Idempotente.
   1. Garantiza cli.INTERLOCUTOR_PROSPECTO (ya definida en el modelo fisico MVP1).
   2. Un solo prospecto vigente por identificacion (indice unico filtrado).
   3. Permisos PRO_CONSULTA / PRO_ALTA / PRO_EDICION asignados a ANALISTA_CLIENTES.
   El prospecto NO genera gestion (no tiene afectacion): alta directa con auditoria.
   La PROMOCION a cliente si es gestion: usa el alta de cliente (CLI_ALTA) con el vinculo id_prospecto;
   al aprobarse el prospecto queda Convertido. No requiere cambios de esquema adicionales.
   ============================================================================ */

IF OBJECT_ID(N'cli.INTERLOCUTOR_PROSPECTO') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_PROSPECTO (
        id_interlocutor_prospecto  bigint IDENTITY(1,1) NOT NULL,
        id_compania                bigint               NOT NULL,
        id_interlocutor            bigint               NULL,
        tipo_persona               nvarchar(10)         NOT NULL,
        nombre_contacto            nvarchar(200)        NOT NULL,
        id_tipo_identificacion     bigint               NULL,
        numero_identificacion      nvarchar(50)         NULL,
        telefono_contacto          nvarchar(30)         NULL,
        correo_contacto            nvarchar(200)        NULL,
        canal_origen               nvarchar(50)         NULL,
        estado                     nvarchar(20)         NOT NULL CONSTRAINT DF_INTERLOCUTOR_PROSPECTO_estado DEFAULT ('Prospecto'),
        fecha_registro             datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_PROSPECTO_fecha_registro DEFAULT (SYSUTCDATETIME()),
        fecha_conversion           datetime2            NULL,
        creado_por                 nvarchar(100)        NOT NULL,
        creado_en                  datetime2            NOT NULL CONSTRAINT DF_INTERLOCUTOR_PROSPECTO_creado_en DEFAULT (SYSUTCDATETIME()),
        modificado_por             nvarchar(100)        NULL,
        modificado_en              datetime2            NULL,
        CONSTRAINT PK_INTERLOCUTOR_PROSPECTO PRIMARY KEY (id_interlocutor_prospecto),
        CONSTRAINT CK_INTERLOCUTOR_PROSPECTO_estado CHECK (estado IN ('Prospecto','Convertido','Descartado')),
        CONSTRAINT FK_INTERLOCUTOR_PROSPECTO_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
        CONSTRAINT FK_INTERLOCUTOR_PROSPECTO_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INTERLOCUTOR_PROSPECTO_LISTA_VALORES_ITEM FOREIGN KEY (id_tipo_identificacion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PROSPECTO_identificacion_vigente' AND object_id = OBJECT_ID(N'cli.INTERLOCUTOR_PROSPECTO'))
    CREATE UNIQUE INDEX UX_PROSPECTO_identificacion_vigente
        ON cli.INTERLOCUTOR_PROSPECTO (id_compania, id_tipo_identificacion, numero_identificacion)
        WHERE estado = N'Prospecto' AND numero_identificacion IS NOT NULL;
GO

/* Datos de persona (mismo esquema de nombre del cliente) ---------------------- */
IF COL_LENGTH(N'cli.INTERLOCUTOR_PROSPECTO', N'nombre1') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_PROSPECTO ADD nombre1 nvarchar(100) NULL, nombre2 nvarchar(100) NULL,
        apellido1 nvarchar(100) NULL, apellido2 nvarchar(100) NULL, fecha_nacimiento date NULL;
GO

/* Sucursal heredada del usuario + direccion (JSON con la misma forma que una direccion del cliente) */
IF COL_LENGTH(N'cli.INTERLOCUTOR_PROSPECTO', N'id_unidad') IS NULL
    ALTER TABLE cli.INTERLOCUTOR_PROSPECTO ADD id_unidad bigint NULL, direccion_json nvarchar(max) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_INTERLOCUTOR_PROSPECTO_DEPARTAMENTO')
    ALTER TABLE cli.INTERLOCUTOR_PROSPECTO
        ADD CONSTRAINT FK_INTERLOCUTOR_PROSPECTO_DEPARTAMENTO FOREIGN KEY (id_unidad) REFERENCES sec.DEPARTAMENTO (id_departamento);
GO

/* Permisos --------------------------------------------------------------- */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT v.codigo, v.nombre, N'API', v.descripcion, N'sistema'
FROM (VALUES
  (N'PRO_CONSULTA', N'Consultar prospectos',  N'Ver la lista y la ficha de prospectos.'),
  (N'PRO_ALTA',     N'Crear prospectos',      N'Registrar un prospecto (no genera gestion; no puede ser ya cliente).'),
  (N'PRO_EDICION',  N'Modificar prospectos',  N'Editar datos de contacto, descartar o reactivar un prospecto.')
) v(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION oa WHERE oa.codigo = v.codigo);
GO

INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT r.id_rol_autorizacion, oa.id_objeto_autorizacion
FROM sec.ROL_AUTORIZACION r
CROSS JOIN sec.OBJETO_AUTORIZACION oa
WHERE r.codigo = N'ANALISTA_CLIENTES' AND r.id_compania IS NULL
  AND oa.codigo IN (N'PRO_CONSULTA', N'PRO_ALTA', N'PRO_EDICION')
  AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x
                  WHERE x.id_rol_autorizacion = r.id_rol_autorizacion AND x.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

SELECT r.codigo AS rol, oa.codigo AS objeto
FROM sec.ROL_AUTORIZACION r
JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = r.id_rol_autorizacion
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
WHERE oa.codigo LIKE N'PRO[_]%';
GO
