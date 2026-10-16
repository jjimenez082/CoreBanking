/* ============================================================================
   Ronda 24 - Roles de cliente: Cliente Basico, Cliente Deudor y Cliente Cuentahabiente (fase 3)
   Idempotente. Ejecutar contra CoreBancario_DEV.

   1. El rol CLIENTE pasa a llamarse «Cliente Basico» (el codigo se conserva: nadie se migra).
   2. Roles nuevos en la lista TIPO_ROL_INTERLOCUTOR: CLIENTE_DEUDOR y CLIENTE_CUENTAHABIENTE
      (prerrequisito: CLIENTE; se solicitan desde «Gestionar roles», no en el alta).
   3. Requisitos por rol y tipo (Persona / Organizacion): se copian los campos de CLIENTE y se marcan obligatorios
      direccion, correo y telefono (crecen cuando existan KYC y perfil economico).
   ============================================================================ */
SET NOCOUNT ON;
GO

DECLARE @tipo bigint = (SELECT id_lista_valores_tipo FROM gen.LISTA_VALORES_TIPO WHERE codigo = N'TIPO_ROL_INTERLOCUTOR');
IF @tipo IS NULL RAISERROR(N'No existe la lista TIPO_ROL_INTERLOCUTOR.', 16, 1);
ELSE
BEGIN
    UPDATE gen.LISTA_VALORES_ITEM SET nombre = N'Cliente Básico', modificado_por = N'sistema', modificado_en = SYSUTCDATETIME()
    WHERE id_lista_valores_tipo = @tipo AND codigo = N'CLIENTE' AND nombre <> N'Cliente Básico';

    DECLARE @orden int = ISNULL((SELECT MAX(orden) FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @tipo), 0);
    IF NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @tipo AND codigo = N'CLIENTE_DEUDOR')
        INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
        VALUES (@tipo, N'CLIENTE_DEUDOR', N'Cliente Deudor', @orden + 1, 1, N'sistema');
    IF NOT EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @tipo AND codigo = N'CLIENTE_CUENTAHABIENTE')
        INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, es_sistema, creado_por)
        VALUES (@tipo, N'CLIENTE_CUENTAHABIENTE', N'Cliente Cuentahabiente', @orden + 2, 1, N'sistema');
END
GO

/* Requisitos de los roles nuevos: mismos campos que CLIENTE; direccion, correo y telefono obligatorios. No pisa lo ya configurado. */
INSERT INTO cli.TIPO_ROL_REQUISITO (id_compania, id_tipo_rol, tipo_persona, requisito, es_requerido, creado_por)
SELECT b.id_compania, n.id_lista_valores_item, b.tipo_persona, b.requisito,
       CASE WHEN b.requisito IN (N'DIRECCION', N'CORREO', N'TELEFONO') THEN 1 ELSE b.es_requerido END, N'sistema'
FROM cli.TIPO_ROL_REQUISITO b
JOIN gen.LISTA_VALORES_ITEM c ON c.id_lista_valores_item = b.id_tipo_rol AND c.codigo = N'CLIENTE'
JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = c.id_lista_valores_tipo AND t.codigo = N'TIPO_ROL_INTERLOCUTOR'
JOIN gen.LISTA_VALORES_ITEM n ON n.id_lista_valores_tipo = t.id_lista_valores_tipo AND n.codigo IN (N'CLIENTE_DEUDOR', N'CLIENTE_CUENTAHABIENTE')
WHERE NOT EXISTS (SELECT 1 FROM cli.TIPO_ROL_REQUISITO x
                   WHERE x.id_compania = b.id_compania AND x.id_tipo_rol = n.id_lista_valores_item
                     AND x.tipo_persona = b.tipo_persona AND x.requisito = b.requisito);
GO

SELECT i.codigo, i.nombre, i.estado, i.orden FROM gen.LISTA_VALORES_ITEM i
JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo AND t.codigo = N'TIPO_ROL_INTERLOCUTOR' ORDER BY i.orden;
SELECT i.codigo, r.tipo_persona, SUM(CASE WHEN r.es_requerido = 1 THEN 1 ELSE 0 END) AS obligatorios, COUNT(*) AS campos
FROM cli.TIPO_ROL_REQUISITO r JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_item = r.id_tipo_rol
WHERE i.codigo IN (N'CLIENTE', N'CLIENTE_DEUDOR', N'CLIENTE_CUENTAHABIENTE') GROUP BY i.codigo, r.tipo_persona ORDER BY i.codigo, r.tipo_persona;
GO
