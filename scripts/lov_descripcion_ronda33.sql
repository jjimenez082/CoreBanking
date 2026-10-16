/* ============================================================================
   Ronda 33 - Descripcion (texto explicativo) en los valores de las listas. Re-ejecutable.
   DEBE correrse ANTES de arrancar el backend de esta ronda: todos los combos leen la columna.
   ============================================================================ */
SET NOCOUNT ON;
GO
IF COL_LENGTH(N'gen.LISTA_VALORES_ITEM', N'descripcion') IS NULL
    ALTER TABLE gen.LISTA_VALORES_ITEM ADD descripcion nvarchar(500) NULL;
GO
SELECT COL_LENGTH(N'gen.LISTA_VALORES_ITEM', N'descripcion') AS descripcion_creada;
GO
