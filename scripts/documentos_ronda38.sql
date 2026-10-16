/* Ronda 38 (r44c): administracion de carpetas y tipos de documento, y documentos requeridos por rol. Re-ejecutable. */

/* 1. Documento vigente que exige cada rol (la presencia de la fila = el rol lo exige). */
IF OBJECT_ID(N'doc.ROL_REQUISITO_DOCUMENTO', N'U') IS NULL
CREATE TABLE doc.ROL_REQUISITO_DOCUMENTO (
    id_compania        bigint        NOT NULL,
    id_tipo_rol        bigint        NOT NULL,    -- gen.LISTA_VALORES_ITEM de TIPO_ROL_INTERLOCUTOR
    tipo_persona       nvarchar(10)  NOT NULL,    -- Fisica / Juridica
    id_tipo_documento  bigint        NOT NULL,
    creado_por         nvarchar(100) NOT NULL,
    creado_en          datetime2     NOT NULL CONSTRAINT DF_ROLREQDOC_creado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_ROL_REQUISITO_DOCUMENTO PRIMARY KEY (id_compania, id_tipo_rol, tipo_persona, id_tipo_documento),
    CONSTRAINT CK_ROLREQDOC_tipo CHECK (tipo_persona IN ('Fisica','Juridica')),
    CONSTRAINT FK_ROLREQDOC_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT FK_ROLREQDOC_TIPODOC FOREIGN KEY (id_tipo_documento) REFERENCES doc.TIPO_DOCUMENTO (id_tipo_documento)
);
GO

/* 2. Permiso de administracion. */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'DOC_ADMINISTRACION', N'Administrar carpetas y tipos de documento', N'API',
       N'Crear y editar carpetas y tipos de documento y definir los documentos que exige cada rol.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'DOC_ADMINISTRACION');
GO
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @adm IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @adm, o.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION o
    WHERE o.codigo = N'DOC_ADMINISTRACION'
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = @adm AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO
SELECT codigo FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'DOC_ADMINISTRACION';
GO
