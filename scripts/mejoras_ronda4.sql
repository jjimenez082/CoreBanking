/* ============================================================================
   MEJORAS RONDA 4 - Unidades donde se pueden registrar clientes
   ============================================================================
   sec.DEPARTAMENTO.permite_registros: la unidad aparece en el combo "Unidad (sucursal)" del alta y la
   modificacion de clientes (y mas adelante cuentas y creditos). Se administra en Administracion > Estructura
   organizativa. Valor inicial: 1 para las unidades de tipo SUCURSAL, 0 para el resto.
   Correr contra CoreBancario_DEV (despues de mejoras_ronda3.sql). Es repetible.
   ============================================================================ */
USE CoreBancario_DEV;
GO
SET NOCOUNT ON;
GO
IF COL_LENGTH('sec.DEPARTAMENTO', 'permite_registros') IS NULL
BEGIN
    ALTER TABLE sec.DEPARTAMENTO ADD permite_registros bit NOT NULL CONSTRAINT DF_DEPARTAMENTO_permite_registros DEFAULT (0);
    -- La columna recien creada ya existe para el lote siguiente; el valor inicial se asigna en ese lote.
END
GO
/* Valor inicial (solo la primera vez, mientras ninguna unidad tenga el parametro activo): las de tipo SUCURSAL.
   Las agencias, departamentos y equipos se habilitan desde la pantalla si se desea. */
IF NOT EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE permite_registros = 1)
BEGIN
    UPDATE sec.DEPARTAMENTO SET permite_registros = 1
    WHERE id_tipo_unidad IN (SELECT id_lista_valores_item FROM gen.LISTA_VALORES_ITEM WHERE codigo = N'SUCURSAL');
END
GO
SELECT d.codigo, d.nombre, t.nombre AS tipo, d.permite_registros FROM sec.DEPARTAMENTO d
JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = d.id_tipo_unidad ORDER BY d.nombre;
GO
