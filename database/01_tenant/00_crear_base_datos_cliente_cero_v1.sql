/* ============================================================================
   CORE BANCARIO - CREACION DE BASE DE DATOS - CLIENTE CERO (DESARROLLO)
   ============================================================================
   Aislamiento en dos niveles (ARQ-019/ARQ-020, ver DAD Seccion 6.4 y 19.8):
     - Nivel 1 (entre clientes SaaS): fisico, una base de datos DEDICADA por
       cliente, nombrada "CoreBancario_<codigo_cliente>".
     - Nivel 2 (multi-compania dentro de un mismo cliente): logico, con
       id_compania (patron SAP) dentro de esta misma base de datos.

   "Cliente cero" es el codigo reservado para tu ambiente interno de
   desarrollo/pruebas (BD Consultores), no un cliente SaaS real. Cuando des
   de alta el primer cliente real, repite este mismo script cambiando
   @codigo_cliente por su codigo definitivo (3-8 caracteres, mayusculas, sin
   espacios ni acentos - regla de ARQ-020).

   Crecimiento VERTICAL (mas dominios funcionales con el tiempo: Cuentas,
   Creditos, Pagos...) se organiza con UN SCHEMA DE SQL SERVER POR DOMINIO
   dentro de esta misma base de datos (nunca todo en dbo):

     gen  -> Catalogos generales compartidos por todos los dominios
             (LISTA_VALORES_TIPO/_ITEM, PAIS_REGULADOR, COMPANIA)
     cli  -> Dominio Clientes/Interlocutores (DAD 12.1)
     sec  -> Organizacion, usuarios y RBAC (ORG-001/002, SEG-002/008)
     apr  -> Motor de Gestion y Aprobaciones (Seccion 2.4)
     aud  -> Auditoria de cambios (AUD-001/002)

   Reservados para cuando se construyan esos dominios (no se crean aun,
   para no dejar schemas vacios sin uso):
     cta  -> Dominio Cuentas (DAD 12.2)
     cred -> Dominio Creditos (DAD 12.3)
     pag  -> Dominio Pagos

   Motor: SQL Server (ARQ-011), compatible con SQL Server Express.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* NOTA TECNICA: "USE" en T-SQL no acepta una variable directamente
   (USE @variable da error de sintaxis) - por eso tanto el CREATE DATABASE
   como el USE se ejecutan aqui como SQL dinamico (EXEC). Ademas, todo esto
   debe ir en el MISMO lote (sin "GO" de por medio) porque "GO" borra las
   variables declaradas. EXEC('USE ...') SI cambia el contexto de base de
   datos de la sesion para los lotes siguientes (los de mas abajo, despues
   del GO de la linea 56), aunque se haya ejecutado como dinamico. */

DECLARE @codigo_cliente nvarchar(8) = N'DEV';           -- cambiar por el codigo_cliente real al aprovisionar un cliente nuevo
DECLARE @nombre_bd      nvarchar(128) = N'CoreBancario_' + @codigo_cliente;
DECLARE @sql            nvarchar(max);

IF DB_ID(@nombre_bd) IS NULL
BEGIN
    SET @sql = N'CREATE DATABASE ' + QUOTENAME(@nombre_bd);
    EXEC (@sql);
END

-- Cambia el contexto de la sesion a la base recien creada/existente.
-- Correcto aunque @codigo_cliente cambie: nunca queda un nombre fijo aqui.
SET @sql = N'USE ' + QUOTENAME(@nombre_bd);
EXEC (@sql);
GO

IF SCHEMA_ID('gen') IS NULL EXEC('CREATE SCHEMA gen AUTHORIZATION dbo');
GO
IF SCHEMA_ID('cli') IS NULL EXEC('CREATE SCHEMA cli AUTHORIZATION dbo');
GO
IF SCHEMA_ID('sec') IS NULL EXEC('CREATE SCHEMA sec AUTHORIZATION dbo');
GO
IF SCHEMA_ID('apr') IS NULL EXEC('CREATE SCHEMA apr AUTHORIZATION dbo');
GO
IF SCHEMA_ID('aud') IS NULL EXEC('CREATE SCHEMA aud AUTHORIZATION dbo');
GO

/* ============================================================================
   FIN - correr a continuacion, en este orden:
     1. modelo_fisico_clientes_mvp1.sql
     2. modelo_fisico_transversal_minimo.sql

   IMPORTANTE: esos dos scripts corren normalmente en una sesion/pestana
   nueva de tu herramienta SQL, por lo que NO heredan la variable
   @codigo_cliente de este script. Cada uno de ellos declara su propia
   @codigo_cliente al inicio - si cambiaste el valor aqui (ej. de 'CERO' a
   'DEV'), cambialo tambien ahi para que apunten a la misma base de datos.
   ============================================================================ */