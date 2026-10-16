-- NOTA: no volver a correr este script despues de mensajes_codigos_ronda17.sql (reinsertaria los codigos antiguos).
/* ============================================================================
   RONDA 16 - Maestro de mensajes del sistema + idempotencia de la API - re-ejecutable
   Base: CoreBancario_<codigo>  (ejecutar con USE <su base> antes)

   1. gen.MENSAJE_SISTEMA: textos parametrizables de las respuestas de la API
      (codigo estable para el integrador; el texto lo edita la compania).
      Placeholders: {identificacion}, {numeroCliente}, {numeroGestion}.
   2. gen.API_IDEMPOTENCIA: llave de idempotencia (header Idempotency-Key) de las
      altas por API; permite reintentar sin crear duplicados y devolver la misma respuesta.
   3. Permiso MSG_ADMINISTRACION (rol ADMINISTRADOR_SEGURIDAD).
   ============================================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'gen.MENSAJE_SISTEMA', N'U') IS NULL
CREATE TABLE gen.MENSAJE_SISTEMA (
    id_mensaje      bigint IDENTITY(1,1) NOT NULL,
    id_compania     bigint         NOT NULL,
    codigo          nvarchar(60)   NOT NULL,            -- estable: lo usa el integrador
    tipo            nvarchar(10)   NOT NULL,            -- ERROR / INFO
    http_estado     smallint       NOT NULL,            -- estado HTTP con que responde la API
    texto           nvarchar(500)  NOT NULL,            -- texto vigente (editable)
    texto_defecto   nvarchar(500)  NOT NULL,            -- texto original (para restaurar)
    descripcion     nvarchar(300)  NULL,
    creado_por      nvarchar(100)  NOT NULL,
    creado_en       datetime2      NOT NULL CONSTRAINT DF_MENSAJE_SISTEMA_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por  nvarchar(100)  NULL,
    modificado_en   datetime2      NULL,
    CONSTRAINT PK_MENSAJE_SISTEMA PRIMARY KEY (id_mensaje),
    CONSTRAINT FK_MENSAJE_SISTEMA_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT CK_MENSAJE_SISTEMA_tipo CHECK (tipo IN ('ERROR','INFO'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_MENSAJE_SISTEMA_codigo' AND object_id = OBJECT_ID(N'gen.MENSAJE_SISTEMA'))
    CREATE UNIQUE INDEX UX_MENSAJE_SISTEMA_codigo ON gen.MENSAJE_SISTEMA (id_compania, codigo);
GO

IF OBJECT_ID(N'gen.API_IDEMPOTENCIA', N'U') IS NULL
CREATE TABLE gen.API_IDEMPOTENCIA (
    id_idempotencia         bigint IDENTITY(1,1) NOT NULL,
    id_compania             bigint         NOT NULL,
    id_usuario_integracion  bigint         NOT NULL,
    llave                   nvarchar(100)  NOT NULL,
    operacion               nvarchar(40)   NOT NULL,     -- p. ej. INTERLOCUTOR_ALTA
    huella                  char(64)       NOT NULL,     -- SHA-256 del cuerpo recibido
    estado                  nvarchar(12)   NOT NULL,     -- EN_PROCESO / COMPLETA
    http_estado             smallint       NULL,
    respuesta               nvarchar(max)  NULL,
    id_correlacion          nvarchar(64)   NULL,
    creado_en               datetime2      NOT NULL CONSTRAINT DF_API_IDEMPOTENCIA_creado_en DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_API_IDEMPOTENCIA PRIMARY KEY (id_idempotencia),
    CONSTRAINT FK_API_IDEMPOTENCIA_COMPANIA FOREIGN KEY (id_compania) REFERENCES gen.COMPANIA (id_compania),
    CONSTRAINT CK_API_IDEMPOTENCIA_estado CHECK (estado IN ('EN_PROCESO','COMPLETA'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_API_IDEMPOTENCIA_llave' AND object_id = OBJECT_ID(N'gen.API_IDEMPOTENCIA'))
    CREATE UNIQUE INDEX UX_API_IDEMPOTENCIA_llave ON gen.API_IDEMPOTENCIA (id_compania, id_usuario_integracion, operacion, llave);
GO

/* Semilla de mensajes por compania (no pisa los que ya existen ni sus ediciones) */
DECLARE @m TABLE (codigo nvarchar(60), tipo nvarchar(10), http smallint, texto nvarchar(500), descr nvarchar(300));
INSERT INTO @m VALUES
 (N'CLIENTE_CREADO',            N'INFO',  201, N'El cliente fue creado con el número {numeroCliente}.', N'Alta aprobada automáticamente y sin coincidencias fuertes.'),
 (N'GESTION_PENDIENTE_APROBACION', N'INFO', 202, N'La solicitud quedó registrada en la gestión {numeroGestion} y está pendiente de aprobación.', N'El proceso exige aprobación humana.'),
 (N'GESTION_PENDIENTE_COINCIDENCIAS', N'INFO', 202, N'Se encontraron personas similares ya registradas; la solicitud quedó en la gestión {numeroGestion} para validación de una persona.', N'Alta con alta similitud: se envía a validación aunque la regla sea automática.'),
 (N'DATOS_INVALIDOS',           N'ERROR', 400, N'La solicitud contiene datos inválidos. Revise el detalle.', N'Validaciones de formato, catálogos y obligatorios.'),
 (N'IDEMPOTENCY_KEY_REQUERIDA', N'ERROR', 400, N'Falta el encabezado Idempotency-Key.', N'Toda alta por API debe enviar una llave de idempotencia.'),
 (N'IDEMPOTENCY_KEY_INVALIDA',  N'ERROR', 400, N'El encabezado Idempotency-Key debe tener entre 8 y 100 caracteres.', NULL),
 (N'IDEMPOTENCY_KEY_CONFLICTO', N'ERROR', 422, N'La llave de idempotencia ya fue usada con un contenido distinto.', N'Misma llave, cuerpo diferente.'),
 (N'IDEMPOTENCY_EN_PROCESO',    N'ERROR', 409, N'Hay una solicitud con esta llave de idempotencia aún en proceso; reintente en unos segundos.', NULL),
 (N'IDENTIFICACION_DUPLICADA',  N'ERROR', 409, N'La identificación {identificacion} ya se encuentra registrada.', N'No revela datos ni número del cliente existente.'),
 (N'ALTA_PENDIENTE_DUPLICADA',  N'ERROR', 409, N'La identificación {identificacion} ya tiene un alta en proceso.', N'No revela la gestión de otro solicitante.'),
 (N'NUMERACION_AGOTADA',        N'ERROR', 409, N'No hay números disponibles para completar la solicitud; contacte al administrador.', NULL),
 (N'PERMISO_DENEGADO',          N'ERROR', 403, N'La integración no tiene permiso para esta operación.', NULL),
 (N'CONFIGURACION_INCOMPLETA',  N'ERROR', 500, N'El servicio no está configurado para esta operación; contacte al administrador.', NULL),
 (N'ERROR_INTERNO',             N'ERROR', 500, N'Ocurrió un error inesperado. Indique el identificador de correlación al soporte.', NULL);

INSERT INTO gen.MENSAJE_SISTEMA (id_compania, codigo, tipo, http_estado, texto, texto_defecto, descripcion, creado_por)
SELECT c.id_compania, m.codigo, m.tipo, m.http, m.texto, m.texto, m.descr, N'sistema'
FROM gen.COMPANIA c CROSS JOIN @m m
WHERE NOT EXISTS (SELECT 1 FROM gen.MENSAJE_SISTEMA x WHERE x.id_compania = c.id_compania AND x.codigo = m.codigo);
GO

/* Permiso */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'MSG_ADMINISTRACION', N'Administrar mensajes del sistema', N'Pantalla',
       N'Editar los textos de los mensajes que devuelve la API a las integraciones.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'MSG_ADMINISTRACION');
GO
DECLARE @id_rol bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
IF @id_rol IS NOT NULL
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
    SELECT @id_rol, oa.id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION oa
    WHERE oa.codigo = N'MSG_ADMINISTRACION'
      AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO rao WHERE rao.id_rol_autorizacion = @id_rol AND rao.id_objeto_autorizacion = oa.id_objeto_autorizacion);
GO

SELECT codigo, tipo, http_estado, texto FROM gen.MENSAJE_SISTEMA ORDER BY tipo, codigo;
