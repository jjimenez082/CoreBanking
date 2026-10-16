/* ============================================================================
   Ronda 33 - Consentimientos del cliente (gestion con aprobacion + historial). Re-ejecutable.
   Requiere: bloqueos_ronda31.sql.

   - cli.INTERLOCUTOR_CONSENTIMIENTO: historial (Vigente / Revocado); un consentimiento vigente por cliente y tipo.
     El modelo fisico original traia una tabla vacia con otra estructura: si esta vacia se reemplaza; si tuviera datos el script se detiene.
   - Listas de valores TIPO_CONSENTIMIENTO y CANAL_CONSENTIMIENTO.
   - Tipo de gestion CLI_CONSENTIMIENTO (aprobacion obligatoria, 1 nivel). Entidad INTERLOCUTOR_CONSENTIMIENTO, llave = id del cliente.
   - Permisos CLI_CONSENTIMIENTO_VER y CLI_CONSENTIMIENTO.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'cli.INTERLOCUTOR_CONSENTIMIENTO', N'U') IS NOT NULL AND COL_LENGTH(N'cli.INTERLOCUTOR_CONSENTIMIENTO', N'id_consentimiento') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_CONSENTIMIENTO)
        THROW 50033, 'cli.INTERLOCUTOR_CONSENTIMIENTO tiene la estructura anterior y contiene datos; migrarlos antes de continuar.', 1;
    DROP TABLE cli.INTERLOCUTOR_CONSENTIMIENTO;
END
GO
IF OBJECT_ID(N'cli.INTERLOCUTOR_CONSENTIMIENTO', N'U') IS NULL
BEGIN
    CREATE TABLE cli.INTERLOCUTOR_CONSENTIMIENTO (
        id_consentimiento        bigint IDENTITY(1,1) NOT NULL,
        id_interlocutor          bigint         NOT NULL,
        id_tipo_consentimiento   bigint         NOT NULL,   -- LISTA_VALORES_ITEM (TIPO_CONSENTIMIENTO)
        estado                   nvarchar(20)   NOT NULL CONSTRAINT DF_INT_CONS_estado DEFAULT ('Vigente'),
        fecha_otorgamiento       datetime2      NOT NULL CONSTRAINT DF_INT_CONS_fecha DEFAULT (SYSUTCDATETIME()),
        id_canal                 bigint         NOT NULL,   -- LISTA_VALORES_ITEM (CANAL_CONSENTIMIENTO)
        comentario               nvarchar(500)  NULL,
        otorgado_por             nvarchar(100)  NOT NULL,
        id_gestion_otorgamiento  bigint         NULL,
        fecha_revocacion         datetime2      NULL,
        id_canal_revocacion      bigint         NULL,
        comentario_revocacion    nvarchar(500)  NULL,
        revocado_por             nvarchar(100)  NULL,
        id_gestion_revocacion    bigint         NULL,
        CONSTRAINT PK_INTERLOCUTOR_CONSENTIMIENTO PRIMARY KEY (id_consentimiento),
        CONSTRAINT CK_INT_CONS_estado CHECK (estado IN ('Vigente', 'Revocado')),
        CONSTRAINT CK_INT_CONS_revocado CHECK ((estado = 'Vigente' AND fecha_revocacion IS NULL) OR (estado = 'Revocado' AND fecha_revocacion IS NOT NULL)),
        CONSTRAINT FK_INT_CONS_INTERLOCUTOR FOREIGN KEY (id_interlocutor) REFERENCES cli.INTERLOCUTOR (id_interlocutor),
        CONSTRAINT FK_INT_CONS_TIPO  FOREIGN KEY (id_tipo_consentimiento) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INT_CONS_CANAL FOREIGN KEY (id_canal) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item),
        CONSTRAINT FK_INT_CONS_CANAL_REV FOREIGN KEY (id_canal_revocacion) REFERENCES gen.LISTA_VALORES_ITEM (id_lista_valores_item)
    );
    CREATE UNIQUE INDEX UX_INT_CONS_vigente ON cli.INTERLOCUTOR_CONSENTIMIENTO (id_interlocutor, id_tipo_consentimiento) WHERE estado = 'Vigente';
    CREATE INDEX IX_INT_CONS_interlocutor ON cli.INTERLOCUTOR_CONSENTIMIENTO (id_interlocutor, estado);
END
GO

/* Listas de valores */
INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, es_sistema, creado_por)
SELECT x.codigo, x.nombre, x.descripcion, 1, N'sistema' FROM (VALUES
  (N'TIPO_CONSENTIMIENTO',  N'Tipo de consentimiento del cliente', N'Autorizaciones que el cliente otorga o revoca (datos personales, comunicaciones, terceros...).'),
  (N'CANAL_CONSENTIMIENTO', N'Canal del consentimiento', N'Medio por el que se recibió o revocó el consentimiento.')
) x(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO t WHERE t.codigo = x.codigo);

IF COL_LENGTH(N'gen.LISTA_VALORES_ITEM', N'descripcion') IS NULL
    THROW 50034, 'Falta correr lov_descripcion_ronda33.sql antes de este script.', 1;
GO

DECLARE @items TABLE (lista nvarchar(100), codigo nvarchar(100), nombre nvarchar(200), orden int, descripcion nvarchar(500));
INSERT INTO @items VALUES
 (N'TIPO_CONSENTIMIENTO', N'TRATAMIENTO_DATOS',  N'Tratamiento de datos personales', 1,
    N'Autoriza a la entidad a recopilar, almacenar y usar los datos personales del cliente para prestarle sus servicios y cumplir la ley. Sin este consentimiento no debería formalizarse ningún producto.'),
 (N'TIPO_CONSENTIMIENTO', N'CONSULTA_CENTRALES', N'Consulta a centrales de riesgo', 2,
    N'Autoriza a consultar el historial crediticio del cliente en centrales de riesgo y a reportar su comportamiento de pago. Se requiere para evaluar y otorgar créditos.'),
 (N'TIPO_CONSENTIMIENTO', N'MARKETING',          N'Campañas de marketing y promociones', 3,
    N'Autoriza a enviar al cliente ofertas, promociones y campañas comerciales por cualquier canal que tenga autorizado. Sin este consentimiento el cliente no debe ser incluido en campañas.'),
 (N'TIPO_CONSENTIMIENTO', N'CONTACTO_TELEFONO',  N'Contacto comercial por llamada telefónica', 4,
    N'Autoriza llamadas telefónicas con fines comerciales (ofrecer productos o servicios). Sin este consentimiento el cliente es «no llamar». No aplica a avisos del servicio, cobranza ni comunicaciones obligatorias por ley.'),
 (N'TIPO_CONSENTIMIENTO', N'CONTACTO_CORREO',    N'Contacto comercial por correo electrónico', 5,
    N'Autoriza correos electrónicos con fines comerciales. Sin este consentimiento el cliente es «no enviar correos comerciales». No aplica a estados de cuenta, avisos del servicio ni comunicaciones obligatorias.'),
 (N'TIPO_CONSENTIMIENTO', N'CONTACTO_MENSAJERIA', N'Contacto comercial por SMS y mensajería (WhatsApp)', 6,
    N'Autoriza mensajes de texto y de mensajería instantánea con fines comerciales. Sin este consentimiento el cliente es «no enviar mensajes comerciales». No aplica a códigos de verificación ni avisos del servicio.'),
 (N'TIPO_CONSENTIMIENTO', N'COMPARTIR_TERCEROS', N'Compartir datos con terceros', 7,
    N'Autoriza a compartir datos del cliente con aliados comerciales o terceros distintos de los exigidos por ley. Sin este consentimiento los datos no salen de la entidad salvo requerimiento legal.'),
 (N'CANAL_CONSENTIMIENTO', N'SUCURSAL',  N'Sucursal / presencial', 1, N'El cliente dio o retiró el consentimiento en persona.'),
 (N'CANAL_CONSENTIMIENTO', N'WEB',       N'Portal web', 2, N'El cliente dio o retiró el consentimiento en el portal web.'),
 (N'CANAL_CONSENTIMIENTO', N'APP',       N'App móvil', 3, N'El cliente dio o retiró el consentimiento en la app móvil.'),
 (N'CANAL_CONSENTIMIENTO', N'TELEFONO',  N'Teléfono / call center', 4, N'El cliente dio o retiró el consentimiento por llamada, con grabación o registro.'),
 (N'CANAL_CONSENTIMIENTO', N'CONTRATO',  N'Contrato o formulario firmado', 5, N'El consentimiento consta en un contrato o formulario firmado por el cliente.'),
 (N'CANAL_CONSENTIMIENTO', N'OTRO',      N'Otro', 99, NULL);

INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
SELECT lt.id_lista_valores_tipo, i.codigo, i.nombre, i.orden, 0, N'sistema'
FROM @items i JOIN gen.LISTA_VALORES_TIPO lt ON lt.codigo = i.lista
WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM x WHERE x.id_lista_valores_tipo = lt.id_lista_valores_tipo AND x.codigo = i.codigo);

-- Descripcion y orden de los valores sembrados; el nombre solo se renombra si sigue siendo el original (no pisa cambios del cliente).
UPDATE x SET descripcion = i.descripcion
FROM gen.LISTA_VALORES_ITEM x JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = x.id_lista_valores_tipo
JOIN @items i ON i.lista = lt.codigo AND i.codigo = x.codigo
WHERE x.descripcion IS NULL AND i.descripcion IS NOT NULL;
UPDATE x SET nombre = N'Campañas de marketing y promociones'
FROM gen.LISTA_VALORES_ITEM x JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = x.id_lista_valores_tipo
WHERE lt.codigo = N'TIPO_CONSENTIMIENTO' AND x.codigo = N'MARKETING' AND x.nombre = N'Comunicaciones comerciales';
UPDATE x SET orden = i.orden
FROM gen.LISTA_VALORES_ITEM x JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = x.id_lista_valores_tipo
JOIN @items i ON i.lista = lt.codigo AND i.codigo = x.codigo
WHERE lt.codigo = N'TIPO_CONSENTIMIENTO' AND x.creado_por = N'sistema' AND ISNULL(x.orden, -1) <> i.orden AND x.modificado_por IS NULL;
GO

/* Tipo de gestion */
IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_CONSENTIMIENTO')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_CONSENTIMIENTO', N'Consentimientos del cliente', N'Campos', 1, 1, N'Cliente', N'sistema');
GO

/* Seguridad */
INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT x.codigo, x.nombre, x.tipo, x.descripcion, N'sistema' FROM (VALUES
  (N'CLI_CONSENTIMIENTO_VER', N'Ver consentimientos de clientes', N'API', N'Consultar los consentimientos vigentes e historial de un cliente.'),
  (N'CLI_CONSENTIMIENTO',     N'Otorgar y revocar consentimientos de clientes', N'API', N'Solicitar (vía Gestión) el otorgamiento o la revocación de consentimientos de un cliente.'),
  (N'LOV_TIPO_CONSENTIMIENTO',  N'Mantener lista de valores: Tipo de consentimiento', N'Transaccion', N'Mantenimiento de la lista de valores TIPO_CONSENTIMIENTO.'),
  (N'LOV_CANAL_CONSENTIMIENTO', N'Mantener lista de valores: Canal del consentimiento', N'Transaccion', N'Mantenimiento de la lista de valores CANAL_CONSENTIMIENTO.')
) x(codigo, nombre, tipo, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION o WHERE o.codigo = x.codigo);
GO

DECLARE @of  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'OFICIAL_CUMPLIMIENTO' AND id_compania IS NULL);
DECLARE @an  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @ap  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'APROBADOR_GESTIONES' AND id_compania IS NULL);
DECLARE @adm bigint = (SELECT TOP 1 id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ADMINISTRADOR_SEGURIDAD');
DECLARE @map TABLE (rol bigint, objeto nvarchar(100));
INSERT INTO @map VALUES
 (@an, N'CLI_CONSENTIMIENTO'), (@an, N'CLI_CONSENTIMIENTO_VER'), (@of, N'CLI_CONSENTIMIENTO'), (@of, N'CLI_CONSENTIMIENTO_VER'), (@ap, N'CLI_CONSENTIMIENTO_VER'),
 (@adm, N'LOV_TIPO_CONSENTIMIENTO'), (@adm, N'LOV_CANAL_CONSENTIMIENTO');
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT m.rol, o.id_objeto_autorizacion FROM @map m JOIN sec.OBJETO_AUTORIZACION o ON o.codigo = m.objeto
WHERE m.rol IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = m.rol AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

SELECT t.codigo, COUNT(*) AS valores FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
WHERE t.codigo IN (N'TIPO_CONSENTIMIENTO', N'CANAL_CONSENTIMIENTO') GROUP BY t.codigo;
GO
