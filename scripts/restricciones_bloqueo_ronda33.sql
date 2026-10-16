/* ============================================================================
   Ronda 33 - Restricciones por tipo de bloqueo. Re-ejecutable.
   Requiere: bloqueos_ronda31.sql y lov_descripcion_ronda33.sql.

   - Lista de valores ACCION_RESTRINGIBLE: acciones que un bloqueo puede impedir (Cuentas y Creditos las consultaran).
   - cli.TIPO_BLOQUEO_ACCION: matriz «tipo de bloqueo -> acciones que restringe». Se siembra una vez con valores sugeridos.
   - Objeto BLO_RESTRICCIONES (pantalla de Administracion) para Oficial de Cumplimiento y Administrador de seguridad.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'gen.LISTA_VALORES_ITEM', N'descripcion') IS NULL
    THROW 50035, 'Falta correr lov_descripcion_ronda33.sql antes de este script.', 1;
GO

INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT N'ACCION_RESTRINGIBLE', N'Acciones restringibles por bloqueo', N'Acciones del cliente que un tipo de bloqueo puede impedir.', 1, N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'ACCION_RESTRINGIBLE');

DECLARE @items TABLE (codigo nvarchar(100), nombre nvarchar(200), orden int);
INSERT INTO @items VALUES
 (N'ABRIR_CUENTA',      N'Abrir cuentas', 1),
 (N'SOLICITAR_CREDITO', N'Solicitar créditos', 2),
 (N'DESEMBOLSO',        N'Recibir desembolsos', 3),
 (N'PAGO_CREDITO',      N'Pagar créditos', 4),
 (N'DEPOSITO',          N'Recibir depósitos y abonos a cuentas', 5),
 (N'RETIRO',            N'Retiros y débitos de cuentas', 6),
 (N'TRANSFERENCIA',     N'Transferencias y pagos a terceros', 7);
INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 1, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = N'ACCION_RESTRINGIBLE'
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);
GO

UPDATE x SET descripcion = d.descripcion
FROM gen.LISTA_VALORES_ITEM x JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = x.id_lista_valores_tipo AND lt.codigo = N'ACCION_RESTRINGIBLE'
JOIN (VALUES
  (N'ABRIR_CUENTA',      N'Abrir cuentas nuevas al cliente.'),
  (N'SOLICITAR_CREDITO', N'Presentar solicitudes de crédito nuevas.'),
  (N'DESEMBOLSO',        N'Desembolsar un crédito aprobado a favor del cliente.'),
  (N'PAGO_CREDITO',      N'Aplicar pagos a los créditos del cliente.'),
  (N'DEPOSITO',          N'Acreditar depósitos y abonos en las cuentas del cliente.'),
  (N'RETIRO',            N'Retiros y débitos de las cuentas del cliente.'),
  (N'TRANSFERENCIA',     N'Transferencias y pagos a terceros desde las cuentas del cliente.')
) d(codigo, descripcion) ON d.codigo = x.codigo
WHERE x.descripcion IS NULL;
GO

UPDATE x SET descripcion = d.descripcion
FROM gen.LISTA_VALORES_ITEM x JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = x.id_lista_valores_tipo AND lt.codigo = N'TIPO_BLOQUEO'
JOIN (VALUES
  (N'KYC',          N'El expediente KYC está vencido o incompleto. Normalmente solo impide abrir productos nuevos.'),
  (N'CUMPLIMIENTO', N'Bloqueo por alerta de cumplimiento (por ejemplo, sospecha de lavado de activos).'),
  (N'FRAUDE',       N'Sospecha o confirmación de fraude.'),
  (N'ENTE_EXTERNO', N'Orden de una autoridad o ente externo (embargo, requerimiento judicial).'),
  (N'OTRO',         N'Otro motivo; el comentario del bloqueo explica la razón.')
) d(codigo, descripcion) ON d.codigo = x.codigo
WHERE x.descripcion IS NULL;
GO

IF OBJECT_ID(N'cli.TIPO_BLOQUEO_ACCION', N'U') IS NULL
BEGIN
    CREATE TABLE cli.TIPO_BLOQUEO_ACCION (
        id_tipo_bloqueo bigint        NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_BLOQUEO)
        id_accion       bigint        NOT NULL,   -- LISTA_VALORES_ITEM (ACCION_RESTRINGIBLE)
        creado_por      nvarchar(100) NOT NULL,
        creado_en       datetime2     NOT NULL CONSTRAINT DF_TBA_creado_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_TIPO_BLOQUEO_ACCION PRIMARY KEY (id_tipo_bloqueo, id_accion),
        CONSTRAINT FK_TBA_TIPO   FOREIGN KEY (id_tipo_bloqueo) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_TBA_ACCION FOREIGN KEY (id_accion)       REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
END
GO

/* Valores sugeridos: solo si la matriz esta vacia (no pisa lo que el cliente configure) */
IF NOT EXISTS (SELECT 1 FROM cli.TIPO_BLOQUEO_ACCION)
BEGIN
    DECLARE @sug TABLE (tipo nvarchar(100), accion nvarchar(100));
    INSERT INTO @sug VALUES
     (N'KYC', N'ABRIR_CUENTA'), (N'KYC', N'SOLICITAR_CREDITO'),
     (N'OTRO', N'ABRIR_CUENTA'), (N'OTRO', N'SOLICITAR_CREDITO'),
     (N'ENTE_EXTERNO', N'ABRIR_CUENTA'), (N'ENTE_EXTERNO', N'SOLICITAR_CREDITO'), (N'ENTE_EXTERNO', N'DESEMBOLSO'),
     (N'ENTE_EXTERNO', N'PAGO_CREDITO'), (N'ENTE_EXTERNO', N'RETIRO'), (N'ENTE_EXTERNO', N'TRANSFERENCIA');
    INSERT INTO @sug SELECT t.tipo, a.accion FROM (VALUES (N'CUMPLIMIENTO'), (N'FRAUDE')) t(tipo)
        CROSS JOIN (VALUES (N'ABRIR_CUENTA'), (N'SOLICITAR_CREDITO'), (N'DESEMBOLSO'), (N'PAGO_CREDITO'), (N'DEPOSITO'), (N'RETIRO'), (N'TRANSFERENCIA')) a(accion);

    INSERT INTO cli.TIPO_BLOQUEO_ACCION (id_tipo_bloqueo, id_accion, creado_por)
    SELECT ti.id_lista_valores_item, ai.id_lista_valores_item, N'sistema'
    FROM @sug s
    JOIN gen.LISTA_VALORES_TIPO tt ON tt.codigo = N'TIPO_BLOQUEO'
    JOIN gen.LISTA_VALORES_ITEM ti ON ti.id_lista_valores_tipo = tt.id_lista_valores_tipo AND ti.codigo = s.tipo
    JOIN gen.LISTA_VALORES_TIPO ta ON ta.codigo = N'ACCION_RESTRINGIBLE'
    JOIN gen.LISTA_VALORES_ITEM ai ON ai.id_lista_valores_tipo = ta.id_lista_valores_tipo AND ai.codigo = s.accion;
END
GO

/* Seguridad */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, x.tipo, x.descripcion, N'sistema' FROM (VALUES
  (N'BLO_RESTRICCIONES', N'Restricciones por tipo de bloqueo', N'Pantalla', N'Definir qué acciones restringe cada tipo de bloqueo de cliente.'),
  (N'LOV_ACCION_RESTRINGIBLE', N'Mantener lista de valores: Acciones restringibles', N'Transaccion', N'Mantenimiento de la lista de valores ACCION_RESTRINGIBLE.')
) x(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);
GO

DECLARE @of  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'OFICIAL_CUMPLIMIENTO' AND id_compania IS NULL);
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
DECLARE @map TABLE (rol bigint, objeto nvarchar(100));
INSERT INTO @map VALUES (@of, N'BLO_RESTRICCIONES'), (@adm, N'BLO_RESTRICCIONES'), (@adm, N'LOV_ACCION_RESTRINGIBLE');
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT m.rol, o.id_objeto_autorizacion FROM @map m JOIN sec.OBJETO_AUTORIZACION o ON o.codigo = m.objeto
WHERE m.rol IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = m.rol AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

SELECT ti.codigo AS tipo_bloqueo, COUNT(*) AS acciones_restringidas
FROM cli.TIPO_BLOQUEO_ACCION a JOIN gen.LISTA_VALORES_ITEM ti ON ti.id_lista_valores_item = a.id_tipo_bloqueo GROUP BY ti.codigo;
GO
