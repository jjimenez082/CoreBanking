/* =====================================================================================================
   CoreBancario_DEV - Estructura organizativa de EJEMPLO  (idempotente; solo datos de prueba)
   Crea el arbol de unidades de una financiera pequena en sec.DEPARTAMENTO, usando los tipos de la lista
   TIPO_UNIDAD_ORG (SUCURSAL, AGENCIA, DEPARTAMENTO, EQUIPO). Si una unidad con el mismo codigo ya existe, no se toca.
   Requiere: seguridad_organizacion.sql. Se aplica a la primera compania (la de DEV).

     Casa Matriz                                   (CM)
     |- Sucursal San Jose Centro                   (SJC)
     |   |- Departamento Plataforma de servicios   (SJC-PLT)
     |   |   |- Equipo Cajas                       (SJC-CAJ)
     |   |   '- Equipo Atencion al cliente         (SJC-ATC)
     |   '- Departamento Creditos                  (SJC-CRE)
     |       |- Equipo Originacion                 (SJC-ORI)
     |       '- Equipo Cobro                       (SJC-COB)
     |- Sucursal Heredia                           (HER)
     |   '- Agencia Mall Heredia                   (HER-MALL)
     |- Departamento Riesgo y Cumplimiento         (RYC)
     |   '- Equipo KYC y Prevencion de lavado      (RYC-KYC)
     '- Banca Digital  (unidad virtual, tipo Sucursal)   (BDIG)

   Para quitarlo: inactivar/borrar estas unidades desde Administracion > Estructura organizativa
   (solo si no tienen usuarios activos ni clientes asignados).
   ===================================================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @idCompania bigint = (SELECT TOP 1 id_compania FROM gen.COMPANIA ORDER BY id_compania);
IF @idCompania IS NULL
BEGIN
    RAISERROR('No existe ninguna compania en gen.COMPANIA.', 16, 1);
    RETURN;
END;

/* Falla con mensaje claro si falta la lista de tipos (script seguridad_organizacion.sql) */
IF NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_UNIDAD_ORG')
BEGIN
    RAISERROR('Falta la lista TIPO_UNIDAD_ORG: correr primero seguridad_organizacion.sql.', 16, 1);
    RETURN;
END;

DECLARE @u TABLE (
    nivel int NOT NULL,
    codigo nvarchar(20) NOT NULL,
    padre nvarchar(20) NULL,
    nombre nvarchar(200) NOT NULL,
    tipo nvarchar(30) NOT NULL
);

INSERT INTO @u (nivel, codigo, padre, nombre, tipo) VALUES
    (1, N'CM',       NULL,      N'Casa Matriz',                          N'SUCURSAL'),
    (2, N'SJC',      N'CM',     N'Sucursal San José Centro',             N'SUCURSAL'),
    (2, N'HER',      N'CM',     N'Sucursal Heredia',                     N'SUCURSAL'),
    (2, N'RYC',      N'CM',     N'Departamento Riesgo y Cumplimiento',   N'DEPARTAMENTO'),
    (2, N'BDIG',     N'CM',     N'Banca Digital',                        N'SUCURSAL'),
    (3, N'SJC-PLT',  N'SJC',    N'Departamento Plataforma de servicios', N'DEPARTAMENTO'),
    (3, N'SJC-CRE',  N'SJC',    N'Departamento Créditos',                N'DEPARTAMENTO'),
    (3, N'HER-MALL', N'HER',    N'Agencia Mall Heredia',                 N'AGENCIA'),
    (3, N'RYC-KYC',  N'RYC',    N'Equipo KYC y Prevención de lavado',    N'EQUIPO'),
    (4, N'SJC-CAJ',  N'SJC-PLT', N'Equipo Cajas',                        N'EQUIPO'),
    (4, N'SJC-ATC',  N'SJC-PLT', N'Equipo Atención al cliente',          N'EQUIPO'),
    (4, N'SJC-ORI',  N'SJC-CRE', N'Equipo Originación',                  N'EQUIPO'),
    (4, N'SJC-COB',  N'SJC-CRE', N'Equipo Cobro',                        N'EQUIPO');

/* Todos los tipos usados deben existir en la lista */
IF EXISTS (
    SELECT 1 FROM @u x
    WHERE NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i
                      JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                      WHERE t.codigo = N'TIPO_UNIDAD_ORG' AND i.codigo = x.tipo))
BEGIN
    RAISERROR('La lista TIPO_UNIDAD_ORG no tiene alguno de los tipos SUCURSAL/AGENCIA/DEPARTAMENTO/EQUIPO.', 16, 1);
    RETURN;
END;

BEGIN TRAN;

DECLARE @n int = 1;
WHILE @n <= 4
BEGIN
    INSERT INTO sec.DEPARTAMENTO (id_compania, id_departamento_padre, codigo, nombre, id_tipo_unidad, creado_por)
    SELECT @idCompania, p.id_departamento, x.codigo, x.nombre, ti.id_lista_valores_item, N'sistema'
    FROM @u x
    JOIN gen.LISTA_VALORES_TIPO tt ON tt.codigo = N'TIPO_UNIDAD_ORG'
    JOIN gen.LISTA_VALORES_ITEM ti ON ti.id_lista_valores_tipo = tt.id_lista_valores_tipo AND ti.codigo = x.tipo
    LEFT JOIN sec.DEPARTAMENTO p ON p.id_compania = @idCompania AND p.codigo = x.padre
    WHERE x.nivel = @n
      AND (x.padre IS NULL OR p.id_departamento IS NOT NULL)
      AND NOT EXISTS (SELECT 1 FROM sec.DEPARTAMENTO d WHERE d.id_compania = @idCompania AND d.codigo = x.codigo);
    SET @n = @n + 1;
END;

COMMIT;

/* Resultado: el arbol con sangria por nivel */
;WITH arbol AS (
    SELECT d.id_departamento, d.codigo, d.nombre, d.id_tipo_unidad, d.estado, 0 AS nivel,
           CAST(RIGHT('0000000000' + CAST(d.id_departamento AS varchar(10)), 10) AS varchar(400)) AS ruta
    FROM sec.DEPARTAMENTO d WHERE d.id_compania = @idCompania AND d.id_departamento_padre IS NULL
    UNION ALL
    SELECT h.id_departamento, h.codigo, h.nombre, h.id_tipo_unidad, h.estado, a.nivel + 1,
           CAST(a.ruta + '/' + RIGHT('0000000000' + CAST(h.id_departamento AS varchar(10)), 10) AS varchar(400))
    FROM sec.DEPARTAMENTO h JOIN arbol a ON h.id_departamento_padre = a.id_departamento
)
SELECT REPLICATE(N'    ', a.nivel) + a.nombre AS unidad, a.codigo, i.nombre AS tipo, a.estado
FROM arbol a
JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_item = a.id_tipo_unidad
ORDER BY a.ruta;
GO

/* Si ya se corrio mejoras_ronda4.sql: las unidades de tipo SUCURSAL nuevas quedan habilitadas para registros */
IF COL_LENGTH('sec.DEPARTAMENTO', 'permite_registros') IS NOT NULL
    EXEC (N'UPDATE d SET permite_registros = 1 FROM sec.DEPARTAMENTO d JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = d.id_tipo_unidad WHERE t.codigo = N''SUCURSAL'' AND d.permite_registros = 0');
GO
