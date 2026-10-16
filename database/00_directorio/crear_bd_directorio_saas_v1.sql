/* ============================================================================
   CORE BANCARIO - BD DIRECTORIO (CONTROL-PLANE MULTI-TENANT)
   ============================================================================
   Base de datos PEQUEÑA Y SEPARADA de las bases de datos de cada cliente
   (CoreBancario_<codigo_cliente>). NO contiene datos de negocio (clientes,
   cuentas, creditos, etc.) - solo el mapa que le dice al backend, para cada
   codigo_cliente, a cual base de datos y a cual realm de Keycloak conectarse
   (DAD Seccion 6.4 y 19.8, ARQ-019/ARQ-020).

   Mecanismo de enrutamiento (ya documentado en el DAD 6.4):
     1. El usuario entra por un selector de acceso / subdominio propio de su
        banco (ej. fincorp.corebancario.com) -> el backend usa eso para saber
        a que REALM de Keycloak mandarlo a autenticar (un realm por banco).
     2. Autentica en Keycloak. El JWT que recibe trae como claim el
        codigo_cliente ("compania").
     3. En cada request, el backend toma ese claim, lo busca AQUI (tabla
        CLIENTE_SAAS de esta base de datos) y arma la cadena de conexion
        hacia CoreBancario_<codigo_cliente> para el resto de la sesion.

   El usuario NUNCA elige la base de datos manualmente - se resuelve siempre
   del lado del servidor a partir del subdominio/realm y del token firmado.

   Esta base de datos vive UNA sola vez, compartida por todos los clientes
   SaaS (a diferencia de CoreBancario_<codigo_cliente>, que es una por
   cliente). No necesita variable de codigo_cliente para su propio nombre.

   Motor: SQL Server (ARQ-011). Convenciones: DAD Seccion 19 (ARQ-018).
   ============================================================================ */

SET NOCOUNT ON;
GO

DECLARE @nombre_bd_directorio nvarchar(128) = N'CoreBancarioDir';
DECLARE @sql                  nvarchar(max);

IF DB_ID(@nombre_bd_directorio) IS NULL
BEGIN
    SET @sql = N'CREATE DATABASE ' + QUOTENAME(@nombre_bd_directorio);
    EXEC (@sql);
END

SET @sql = N'USE ' + QUOTENAME(@nombre_bd_directorio);
EXEC (@sql);
GO

IF SCHEMA_ID('dir') IS NULL EXEC('CREATE SCHEMA dir AUTHORIZATION dbo');
GO

/* ============================================================================
   TABLA CLIENTE_SAAS (control-plane)
   ============================================================================ */

CREATE TABLE dir.CLIENTE_SAAS (
    id_cliente_saas     bigint IDENTITY(1,1) NOT NULL,
    codigo_cliente      nvarchar(8)          NOT NULL,   -- 3-8 caracteres, mayusculas, sin espacios/acentos (ARQ-020)
    nombre_base_datos   nvarchar(128)        NOT NULL,   -- 'CoreBancario_' + codigo_cliente
    realm_iam           nvarchar(100)        NOT NULL,   -- mismo codigo, en minusculas, en Keycloak
    pais_principal      nvarchar(3)          NOT NULL,   -- codigo ISO del pais/regulador principal (referencia logica: PAIS_REGULADOR vive dentro de cada base de datos por cliente, no aqui - no es FK real por ser bases de datos distintas)
    estado              nvarchar(20)         NOT NULL CONSTRAINT DF_CLIENTE_SAAS_estado DEFAULT ('Activo'),  -- Activo / Suspendido / Dado de baja
    fecha_alta          datetime2            NOT NULL CONSTRAINT DF_CLIENTE_SAAS_fecha_alta DEFAULT (SYSUTCDATETIME()),
    creado_por          nvarchar(100)        NOT NULL,
    creado_en           datetime2            NOT NULL CONSTRAINT DF_CLIENTE_SAAS_creado_en DEFAULT (SYSUTCDATETIME()),
    modificado_por      nvarchar(100)        NULL,
    modificado_en       datetime2            NULL,
    CONSTRAINT PK_CLIENTE_SAAS PRIMARY KEY (id_cliente_saas),
    CONSTRAINT UQ_CLIENTE_SAAS_codigo_cliente UNIQUE (codigo_cliente),
    CONSTRAINT UQ_CLIENTE_SAAS_nombre_base_datos UNIQUE (nombre_base_datos),
    CONSTRAINT UQ_CLIENTE_SAAS_realm_iam UNIQUE (realm_iam),
    CONSTRAINT CK_CLIENTE_SAAS_estado CHECK (estado IN ('Activo','Suspendido','Dado de baja')),
    -- Validacion fisica minima del formato de codigo_cliente (ARQ-020): 3-8
    -- caracteres, solo mayusculas A-Z y digitos 0-9, sin espacios ni acentos.
    CONSTRAINT CK_CLIENTE_SAAS_codigo_formato CHECK (
        LEN(codigo_cliente) BETWEEN 3 AND 8
        AND codigo_cliente NOT LIKE '%[^A-Z0-9]%'
    )
);
GO
CREATE INDEX IX_CLIENTE_SAAS_estado ON dir.CLIENTE_SAAS (estado);
GO

/* ============================================================================
   SEED: registra el cliente cero (tu ambiente de desarrollo) para que el
   mecanismo de enrutamiento sea consultable de punta a punta desde ya.

   Debe coincidir con el @codigo_cliente usado en los otros 3 scripts
   (00_crear_base_datos_cliente_cero.sql, modelo_fisico_clientes_mvp1.sql,
   modelo_fisico_transversal_minimo.sql).
   ============================================================================ */

DECLARE @codigo_cliente_seed nvarchar(8) = N'CERO';

IF NOT EXISTS (SELECT 1 FROM dir.CLIENTE_SAAS WHERE codigo_cliente = @codigo_cliente_seed)
BEGIN
    INSERT INTO dir.CLIENTE_SAAS (codigo_cliente, nombre_base_datos, realm_iam, pais_principal, estado, creado_por)
    VALUES (
        @codigo_cliente_seed,
        N'CoreBancario_' + @codigo_cliente_seed,
        LOWER(@codigo_cliente_seed),
        N'CRI',   -- Costa Rica (GEN-018) - cambia si tu ambiente de pruebas usa otro pais principal
        N'Activo',
        N'sistema'
    );
END
GO

/* ============================================================================
   FIN DEL SCRIPT - BD Directorio

   Esta base de datos es independiente de la secuencia
   00_crear_base_datos_cliente_cero.sql -> modelo_fisico_clientes_mvp1.sql ->
   modelo_fisico_transversal_minimo.sql; puedes correr este script antes,
   despues o en cualquier momento respecto a esos tres.
   ============================================================================ */