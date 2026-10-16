/* ============================================================================
   RONDA 17 - Tipificacion de mensajes de la API (severidad + codigo ER_/WA_/IN_) - re-ejecutable
   Base: CoreBancario_<codigo>  (ejecutar con USE <su base> antes; requiere mensajes_api_ronda16.sql ya corrido)

   gen.MENSAJE_SISTEMA gana:
     nombre     clave legible e interna (la que antes era "codigo": DATOS_INVALIDOS...)
     severidad  ERROR | WARNING | INFORMATIVO
     modulo     CLIE | API | SIS
     numero     consecutivo dentro de (severidad, modulo)
     resultado  CLIENTE_CREADO | GESTION_PENDIENTE | RECHAZADO
     codigo     pasa a ser el oficial: {ER|WA|IN}_{modulo}_{nnn}, p. ej. ER_CLIE_001
   Solo el texto es editable por la compania; codigo, severidad, resultado y HTTP son el contrato con el integrador.
   NO volver a correr mensajes_api_ronda16.sql despues de esta ronda.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'gen.MENSAJE_SISTEMA', N'nombre') IS NULL
BEGIN
    ALTER TABLE gen.MENSAJE_SISTEMA ADD
        nombre     nvarchar(60) NULL,
        severidad  nvarchar(12) NULL,
        modulo     nvarchar(6)  NULL,
        numero     smallint     NULL,
        resultado  nvarchar(20) NULL;
END
GO

/* Migracion: el codigo anterior pasa a nombre y se asigna el codigo oficial */
DECLARE @map TABLE (nombre nvarchar(60), severidad nvarchar(12), modulo nvarchar(6), numero smallint, resultado nvarchar(20), http smallint, texto nvarchar(500), descr nvarchar(300));
INSERT INTO @map VALUES
 (N'CLIENTE_CREADO',                 N'INFORMATIVO', N'CLIE', 1, N'CLIENTE_CREADO',    201, N'El cliente fue creado con el número {numeroCliente}.', N'Alta aprobada automáticamente y sin coincidencias.'),
 (N'GESTION_PENDIENTE_APROBACION',   N'INFORMATIVO', N'CLIE', 2, N'GESTION_PENDIENTE', 202, N'La solicitud quedó registrada en la gestión {numeroGestion} y está pendiente de aprobación.', N'El proceso exige aprobación humana.'),
 (N'GESTION_PENDIENTE_COINCIDENCIAS',N'WARNING',     N'CLIE', 1, N'GESTION_PENDIENTE', 202, N'Se encontraron personas similares ya registradas; la solicitud quedó en la gestión {numeroGestion} para validación de una persona.', N'Alta con alta similitud: validación humana aunque la regla sea automática.'),
 (N'DATOS_INVALIDOS',                N'ERROR',       N'CLIE', 1, N'RECHAZADO',         400, N'La solicitud contiene datos inválidos. Revise el detalle.', N'Validaciones de formato, catálogos y obligatorios.'),
 (N'IDENTIFICACION_DUPLICADA',       N'ERROR',       N'CLIE', 2, N'RECHAZADO',         409, N'La identificación {identificacion} ya se encuentra registrada.', N'No revela datos ni número del cliente existente.'),
 (N'ALTA_PENDIENTE_DUPLICADA',       N'ERROR',       N'CLIE', 3, N'RECHAZADO',         409, N'La identificación {identificacion} ya tiene un alta en proceso.', N'No revela la gestión de otro solicitante.'),
 (N'GESTION_EN_PROCESO',             N'INFORMATIVO', N'CLIE', 3, N'GESTION_PENDIENTE', 200, N'La gestión {numeroGestion} sigue pendiente de aprobación.', N'Consulta de estado: aún sin resolver.'),
 (N'GESTION_APROBADA',               N'INFORMATIVO', N'CLIE', 4, N'CLIENTE_CREADO',    200, N'La gestión {numeroGestion} fue aprobada; el cliente tiene el número {numeroCliente}.', N'Consulta de estado: aprobada y aplicada.'),
 (N'GESTION_RECHAZADA',              N'WARNING',     N'CLIE', 2, N'RECHAZADO',         200, N'La gestión {numeroGestion} fue rechazada; el cliente no fue creado.', N'Consulta de estado: rechazada. No expone el motivo interno.'),
 (N'GESTION_DEVUELTA',               N'WARNING',     N'CLIE', 3, N'RECHAZADO',         200, N'La gestión {numeroGestion} fue devuelta sin completarse; envíe una nueva solicitud con los datos corregidos.', N'Consulta de estado: devuelta por el aprobador.'),
 (N'GESTION_NO_ENCONTRADA',          N'ERROR',       N'CLIE', 4, N'RECHAZADO',         404, N'No se encontró la gestión indicada.', N'Inexistente o de otro solicitante: no se distingue.'),
 (N'IDEMPOTENCY_KEY_REQUERIDA',      N'ERROR',       N'API',  1, N'RECHAZADO',         400, N'Falta el encabezado Idempotency-Key.', N'Toda alta por API debe enviar una llave de idempotencia.'),
 (N'IDEMPOTENCY_KEY_INVALIDA',       N'ERROR',       N'API',  2, N'RECHAZADO',         400, N'El encabezado Idempotency-Key debe tener entre 8 y 100 caracteres.', NULL),
 (N'IDEMPOTENCY_KEY_CONFLICTO',      N'ERROR',       N'API',  3, N'RECHAZADO',         422, N'La llave de idempotencia ya fue usada con un contenido distinto.', N'Misma llave, cuerpo diferente.'),
 (N'IDEMPOTENCY_EN_PROCESO',         N'ERROR',       N'API',  4, N'RECHAZADO',         409, N'Hay una solicitud con esta llave de idempotencia aún en proceso; reintente en unos segundos.', NULL),
 (N'PERMISO_DENEGADO',               N'ERROR',       N'API',  5, N'RECHAZADO',         403, N'La integración no tiene permiso para esta operación.', NULL),
 (N'ERROR_INTERNO',                  N'ERROR',       N'SIS',  1, N'RECHAZADO',         500, N'Ocurrió un error inesperado. Indique el identificador de correlación al soporte.', NULL),
 (N'NUMERACION_AGOTADA',             N'ERROR',       N'SIS',  2, N'RECHAZADO',         409, N'No hay números disponibles para completar la solicitud; contacte al administrador.', NULL),
 (N'CONFIGURACION_INCOMPLETA',       N'ERROR',       N'SIS',  3, N'RECHAZADO',         500, N'El servicio no está configurado para esta operación; contacte al administrador.', NULL);

-- 1) filas existentes (su codigo antiguo era el nombre)
UPDATE m SET
    nombre    = x.nombre,
    severidad = x.severidad,
    modulo    = x.modulo,
    numero    = x.numero,
    resultado = x.resultado,
    tipo      = CASE WHEN x.severidad = N'ERROR' THEN N'ERROR' ELSE N'INFO' END,
    codigo    = CASE x.severidad WHEN N'ERROR' THEN N'ER' WHEN N'WARNING' THEN N'WA' ELSE N'IN' END
                + N'_' + x.modulo + N'_' + RIGHT(N'000' + CAST(x.numero AS nvarchar(3)), 3)
FROM gen.MENSAJE_SISTEMA m
JOIN @map x ON x.nombre = m.codigo
WHERE m.nombre IS NULL;

-- 2) companias que no tenian alguno de los mensajes
INSERT INTO gen.MENSAJE_SISTEMA (id_compania, codigo, nombre, severidad, modulo, numero, resultado, tipo, http_estado, texto, texto_defecto, descripcion, creado_por)
SELECT c.id_compania,
       CASE x.severidad WHEN N'ERROR' THEN N'ER' WHEN N'WARNING' THEN N'WA' ELSE N'IN' END + N'_' + x.modulo + N'_' + RIGHT(N'000' + CAST(x.numero AS nvarchar(3)), 3),
       x.nombre, x.severidad, x.modulo, x.numero, x.resultado,
       CASE WHEN x.severidad = N'ERROR' THEN N'ERROR' ELSE N'INFO' END, x.http, x.texto, x.texto, x.descr, N'sistema'
FROM gen.COMPANIA c CROSS JOIN @map x
WHERE NOT EXISTS (SELECT 1 FROM gen.MENSAJE_SISTEMA m WHERE m.id_compania = c.id_compania AND m.nombre = x.nombre);

-- 3) la severidad, el modulo y el resultado quedan fijos del lado del contrato
UPDATE m SET severidad = x.severidad, modulo = x.modulo, resultado = x.resultado, http_estado = x.http
FROM gen.MENSAJE_SISTEMA m JOIN @map x ON x.nombre = m.nombre;
GO

/* Restricciones definitivas */
IF EXISTS (SELECT 1 FROM gen.MENSAJE_SISTEMA WHERE nombre IS NULL OR severidad IS NULL)
    RAISERROR(N'Hay mensajes sin migrar (codigo desconocido); revisar antes de continuar.', 16, 1);
ELSE
BEGIN
    ALTER TABLE gen.MENSAJE_SISTEMA ALTER COLUMN nombre    nvarchar(60) NOT NULL;
    ALTER TABLE gen.MENSAJE_SISTEMA ALTER COLUMN severidad nvarchar(12) NOT NULL;
    ALTER TABLE gen.MENSAJE_SISTEMA ALTER COLUMN modulo    nvarchar(6)  NOT NULL;
    ALTER TABLE gen.MENSAJE_SISTEMA ALTER COLUMN numero    smallint     NOT NULL;
    ALTER TABLE gen.MENSAJE_SISTEMA ALTER COLUMN resultado nvarchar(20) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_MENSAJE_SISTEMA_severidad')
    ALTER TABLE gen.MENSAJE_SISTEMA ADD CONSTRAINT CK_MENSAJE_SISTEMA_severidad CHECK (severidad IN ('ERROR','WARNING','INFORMATIVO'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_MENSAJE_SISTEMA_resultado')
    ALTER TABLE gen.MENSAJE_SISTEMA ADD CONSTRAINT CK_MENSAJE_SISTEMA_resultado CHECK (resultado IN ('CLIENTE_CREADO','GESTION_PENDIENTE','RECHAZADO'));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_MENSAJE_SISTEMA_nombre' AND object_id = OBJECT_ID(N'gen.MENSAJE_SISTEMA'))
    CREATE UNIQUE INDEX UX_MENSAJE_SISTEMA_nombre ON gen.MENSAJE_SISTEMA (id_compania, nombre);
GO

SELECT codigo, nombre, severidad, resultado, http_estado, texto FROM gen.MENSAJE_SISTEMA ORDER BY modulo, codigo;
