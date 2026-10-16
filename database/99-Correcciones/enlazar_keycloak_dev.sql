/* ============================================================================
   ENLAZAR KEYCLOAK (realm 'dev') CON CoreBancario_DEV
   ============================================================================
   gen.COMPANIA (y gen.PAIS_REGULADOR, de la que depende) quedaron vacias al
   correr los scripts de creacion - no traian seed data de negocio, solo el
   RBAC (sec/apr) si la tenia. Este script crea una compania de prueba minima
   y despues enlaza el usuario y la cuenta de servicio de Keycloak.

   Correr contra CoreBancario_DEV (SSMS/sqlcmd).
   ============================================================================ */

USE CoreBancario_DEV;
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   1. Pais/regulador de prueba (GEN-018: Costa Rica)
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE codigo_iso_pais = N'CRI')
BEGIN
    INSERT INTO gen.PAIS_REGULADOR (codigo_iso_pais, nombre_pais, nombre_regulador, mayoria_edad_anios, moneda_local, creado_por)
    VALUES (N'CRI', N'Costa Rica', N'SUGEF/CONASSIF', 18, N'CRC', N'sistema');
END
GO

/* ---------------------------------------------------------------------------
   2. Compania de prueba (Nivel 2, ARQ-019) - tu ambiente de desarrollo
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM gen.COMPANIA WHERE codigo_compania = N'DEV01')
BEGIN
    DECLARE @id_pais_regulador bigint = (SELECT id_pais_regulador FROM gen.PAIS_REGULADOR WHERE codigo_iso_pais = N'CRI');

    INSERT INTO gen.COMPANIA (codigo_compania, nombre_compania, id_pais_regulador, creado_por)
    VALUES (N'DEV01', N'Compania de prueba - ambiente DEV', @id_pais_regulador, N'sistema');
END
GO

/* ---------------------------------------------------------------------------
   3. Usuario nombrado, enlazado al 'sub' real de Keycloak (qa.arquitecto)
   --------------------------------------------------------------------------- */
DECLARE @id_compania_dev bigint = (SELECT id_compania FROM gen.COMPANIA WHERE codigo_compania = N'DEV01');
DECLARE @subject_iam_qa  nvarchar(200) = N'2014a739-3680-466b-a3a2-da5010b61484';   -- 'sub' de qa.arquitecto en el realm 'dev'

IF NOT EXISTS (SELECT 1 FROM sec.USUARIO WHERE subject_iam = @subject_iam_qa)
BEGIN
    INSERT INTO sec.USUARIO (id_compania, subject_iam, nombre_completo, correo, creado_por)
    VALUES (@id_compania_dev, @subject_iam_qa, N'QA Arquitecto', N'qa.arquitecto@example.com', N'sistema');
END
GO

/* ---------------------------------------------------------------------------
   4. Cuenta de servicio del backend (Client Credentials, SEG-010)
   --------------------------------------------------------------------------- */
DECLARE @id_compania_dev2 bigint = (SELECT id_compania FROM gen.COMPANIA WHERE codigo_compania = N'DEV01');

IF NOT EXISTS (SELECT 1 FROM sec.USUARIO_INTEGRACION WHERE client_id_iam = N'core-bancario-backend')
BEGIN
    INSERT INTO sec.USUARIO_INTEGRACION (id_compania, client_id_iam, nombre, creado_por)
    VALUES (@id_compania_dev2, N'core-bancario-backend', N'Backend Core Bancario (Client Credentials)', N'sistema');
END
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT id_pais_regulador, codigo_iso_pais, nombre_pais FROM gen.PAIS_REGULADOR;
SELECT id_compania, codigo_compania, nombre_compania FROM gen.COMPANIA;
SELECT id_usuario, id_compania, subject_iam, nombre_completo, estado FROM sec.USUARIO;
SELECT id_usuario_integracion, id_compania, client_id_iam, nombre, estado FROM sec.USUARIO_INTEGRACION;
GO
