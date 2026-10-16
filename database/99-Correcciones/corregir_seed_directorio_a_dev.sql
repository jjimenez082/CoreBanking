/* ============================================================================
   CORRECCION: la fila sembrada en dir.CLIENTE_SAAS apunta a 'CERO', pero la
   base de datos del cliente quedó creada como CoreBancario_DEV (no
   CoreBancario_CERO). Este script corrige esa fila para que el mecanismo de
   enrutamiento (DAD 6.4) apunte a la base de datos real.

   Correr esto conectado contra CoreBancarioDir.
   ============================================================================ */

USE CoreBancarioDir;
GO

UPDATE dir.CLIENTE_SAAS
SET
    codigo_cliente    = N'DEV',
    nombre_base_datos = N'CoreBancario_DEV',
    realm_iam         = N'dev',
    modificado_por    = N'sistema',
    modificado_en     = SYSUTCDATETIME()
WHERE codigo_cliente = N'CERO';
GO

-- Verificacion
SELECT id_cliente_saas, codigo_cliente, nombre_base_datos, realm_iam, estado
FROM dir.CLIENTE_SAAS;
GO
