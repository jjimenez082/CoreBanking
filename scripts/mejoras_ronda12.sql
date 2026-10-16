/* ============================================================================
   Mejoras ronda 12 - Usuario tecnico por integracion y tipo de usuario
   Que hace (se puede correr varias veces):
     1. sec.USUARIO.tipo_usuario: FISICO (persona, inicia sesion) | INTEGRACION (usuario tecnico, NO inicia sesion).
     2. sec.USUARIO_INTEGRACION.id_usuario: usuario tecnico cuyos roles/permisos usa la integracion.
     3. Rol INTEGRACION_ALTA_CLIENTES (solo CLI_ALTA).
     4. Para cada integracion existente sin usuario asociado: crea su usuario tecnico, lo enlaza y le asigna ese rol,
        para que el alta de clientes por API siga funcionando. Otros permisos (consulta, etc.) se dan despues en
        Seguridad > Usuarios (roles) sobre el usuario tecnico.
   Desde esta ronda una integracion YA NO tiene acceso total: sin permiso -> HTTP 403 permiso_denegado.
   Hacer respaldo antes: modifica sec.USUARIO y sec.USUARIO_INTEGRACION.
   ============================================================================ */
SET NOCOUNT ON;
GO

/* 1. Tipo de usuario */
IF COL_LENGTH('sec.USUARIO', 'tipo_usuario') IS NULL
    ALTER TABLE sec.USUARIO ADD tipo_usuario nvarchar(20) NOT NULL CONSTRAINT DF_USUARIO_tipo_usuario DEFAULT ('FISICO');
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_USUARIO_tipo_usuario')
    ALTER TABLE sec.USUARIO ADD CONSTRAINT CK_USUARIO_tipo_usuario CHECK (tipo_usuario IN ('FISICO','INTEGRACION'));
GO

/* 2. Usuario asociado a la integracion */
IF COL_LENGTH('sec.USUARIO_INTEGRACION', 'id_usuario') IS NULL
    ALTER TABLE sec.USUARIO_INTEGRACION ADD id_usuario bigint NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_USUARIO_INTEGRACION_USUARIO')
    ALTER TABLE sec.USUARIO_INTEGRACION ADD CONSTRAINT FK_USUARIO_INTEGRACION_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_USUARIO_INTEGRACION_usuario' AND object_id = OBJECT_ID(N'sec.USUARIO_INTEGRACION'))
    CREATE INDEX IX_USUARIO_INTEGRACION_usuario ON sec.USUARIO_INTEGRACION (id_usuario);
GO

/* 3. Rol de alta de clientes para integraciones */
IF NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION WHERE codigo = N'INTEGRACION_ALTA_CLIENTES' AND id_compania IS NULL)
    INSERT INTO sec.ROL_AUTORIZACION (id_compania, codigo, nombre, creado_por)
    VALUES (NULL, N'INTEGRACION_ALTA_CLIENTES', N'Integración: alta de clientes', N'sistema');
GO

DECLARE @rol bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'INTEGRACION_ALTA_CLIENTES' AND id_compania IS NULL);
DECLARE @obj bigint = (SELECT id_objeto_autorizacion FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_ALTA');
IF @rol IS NOT NULL AND @obj IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO WHERE id_rol_autorizacion = @rol AND id_objeto_autorizacion = @obj)
    INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion) VALUES (@rol, @obj);
GO

/* 4. Un usuario tecnico por cada integracion existente (sin login: sin cuenta en Keycloak) */
DECLARE @rol bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'INTEGRACION_ALTA_CLIENTES' AND id_compania IS NULL);
DECLARE @id_int bigint, @comp bigint, @client nvarchar(200), @nombre nvarchar(200), @estado nvarchar(20), @idu bigint, @user nvarchar(100);

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT id_usuario_integracion, id_compania, client_id_iam, nombre, estado FROM sec.USUARIO_INTEGRACION WHERE id_usuario IS NULL;
OPEN cur;
FETCH NEXT FROM cur INTO @id_int, @comp, @client, @nombre, @estado;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @user = LEFT(N'svc.' + @client, 100);
    SET @idu = (SELECT id_usuario FROM sec.USUARIO WHERE id_compania = @comp AND username = @user);

    IF @idu IS NULL
    BEGIN
        INSERT INTO sec.USUARIO (id_compania, subject_iam, username, nombres, apellidos, nombre_completo, correo, estado, tipo_usuario, creado_por)
        VALUES (@comp, N'svc:' + LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')), @user,
                LEFT(N'Técnico ' + @nombre, 100), N'(integracion)', LEFT(N'Técnico ' + @nombre, 200),
                LEFT(@user, 100) + N'@integracion.local', @estado, N'INTEGRACION', N'sistema');
        SET @idu = SCOPE_IDENTITY();
    END

    UPDATE sec.USUARIO_INTEGRACION SET id_usuario = @idu WHERE id_usuario_integracion = @id_int;

    IF @rol IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.USUARIO_ROL WHERE id_usuario = @idu AND id_rol_autorizacion = @rol)
        INSERT INTO sec.USUARIO_ROL (id_usuario, id_rol_autorizacion) VALUES (@idu, @rol);

    FETCH NEXT FROM cur INTO @id_int, @comp, @client, @nombre, @estado;
END
CLOSE cur; DEALLOCATE cur;
GO

SELECT ui.nombre AS integracion, ui.client_id_iam, u.username AS usuario_tecnico, u.tipo_usuario
FROM sec.USUARIO_INTEGRACION ui LEFT JOIN sec.USUARIO u ON u.id_usuario = ui.id_usuario;
GO
