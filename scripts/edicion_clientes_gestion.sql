/* ============================================================================
   Edicion de clientes por Gestion (aprobacion) - preparacion de base de datos
   Idempotente. Ejecutar contra CoreBancario_DEV.

   Principio: todo cambio a datos de un cliente se SOLICITA como Gestion y se
   aplica al aprobarse (un nivel de aprobacion en este MVP).

   Que hace:
   1. Objetos de autorizacion: CLI_EDICION (API, solicitar cambio de cliente)
      y GES_APROBAR (Transaccion, aprobar/rechazar gestiones).
   2. Tipo de gestion CLI_MODIFICACION_DATOS (modo Campos, aprobacion obligatoria, 1 nivel).
   3. Indice unico filtrado: una sola gestion PENDIENTE por registro
      (bloqueo de gestiones concurrentes sobre el mismo cliente).
   4. Roles: ANALISTA_CLIENTES recibe CLI_EDICION; nuevo rol APROBADOR_GESTIONES
      con GES_APROBAR. qa.arquitecto recibe ambos (para pruebas).
   ============================================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_EDICION')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'CLI_EDICION', N'Solicitar modificacion de cliente', N'API',
            N'Solicitar (via Gestion) el cambio de datos de un cliente existente.', N'sistema');

IF NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'GES_APROBAR')
    INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
    VALUES (N'GES_APROBAR', N'Aprobar o rechazar gestiones', N'Transaccion',
            N'Aprobar (aplicando el cambio) o rechazar gestiones pendientes. No permite aprobar las propias.', N'sistema');
GO

IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_MODIFICACION_DATOS')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_MODIFICACION_DATOS', N'Modificacion de datos de cliente', N'Campos', 1, 1, N'Cliente', N'sistema');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_GESTION_pendiente_por_registro' AND object_id = OBJECT_ID(N'apr.GESTION'))
    CREATE UNIQUE INDEX UX_GESTION_pendiente_por_registro
        ON apr.GESTION (entidad_negocio, llave_registro)
        WHERE estado = N'Pendiente de Aprobacion' AND llave_registro IS NOT NULL;
GO

/* Roles ------------------------------------------------------------------ */
IF NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION WHERE codigo = N'APROBADOR_GESTIONES' AND id_compania IS NULL)
    INSERT INTO sec.ROL_AUTORIZACION (id_compania, codigo, nombre, creado_por)
    VALUES (NULL, N'APROBADOR_GESTIONES', N'Aprobador de Gestiones', N'sistema');
GO

DECLARE @id_analista  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @id_aprobador bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'APROBADOR_GESTIONES' AND id_compania IS NULL);
DECLARE @id_edicion   bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_EDICION');
DECLARE @id_aprobar   bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'GES_APROBAR');

IF @id_analista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @id_analista AND id_objeto_autorizacion = @id_edicion)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@id_analista, @id_edicion);

IF NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @id_aprobador AND id_objeto_autorizacion = @id_aprobar)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@id_aprobador, @id_aprobar);

-- qa.arquitecto: el rol de aprobador (ya tiene ANALISTA_CLIENTES, que ahora incluye CLI_EDICION)
DECLARE @id_qa bigint = (SELECT id_usuario FROM sec.USUARIO WHERE correo = N'qa.arquitecto@example.com');
IF @id_qa IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.USUARIO_ROL WHERE id_usuario = @id_qa AND id_rol_autorizacion = @id_aprobador)
    INSERT INTO sec.USUARIO_ROL (id_usuario, id_rol_autorizacion) VALUES (@id_qa, @id_aprobador);
GO

/* Verificacion ------------------------------------------------------------ */
SELECT codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion FROM apr.TIPO_GESTION ORDER BY codigo;

SELECT r.codigo AS rol, oa.codigo AS objeto
FROM sec.ROL_AUTORIZACION r
JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = r.id_rol_autorizacion
JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
WHERE oa.codigo IN (N'CLI_EDICION', N'GES_APROBAR');
GO
