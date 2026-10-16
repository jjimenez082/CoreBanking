/* ============================================================================
   Ronda 39 (r44c) - Inactivacion por fallecimiento. Re-ejecutable.
   Requiere: documentos_ronda35.sql (tipo de documento ACTA_DEFUNCION).

   - Tipo de gestion CLI_FALLECIMIENTO (entidad INTERLOCUTOR_FALLECIMIENTO, llave = id del cliente) con aprobacion OBLIGATORIA:
     antes de usarla hay que definir una regla en Administracion > Reglas de aprobacion (igual que consentimientos).
   - Permiso CLI_FALLECIMIENTO para solicitarla.
   ============================================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM apr.TIPO_GESTION WHERE codigo = N'CLI_FALLECIMIENTO')
    INSERT INTO apr.TIPO_GESTION (codigo, nombre, modo, requiere_aprobacion_obligatoria, cantidad_niveles_aprobacion, categoria, creado_por)
    VALUES (N'CLI_FALLECIMIENTO', N'Fallecimiento del cliente', N'Campos', 1, 1, N'Cliente', N'sistema');
GO

INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
SELECT N'CLI_FALLECIMIENTO', N'Registrar fallecimiento de clientes', N'API',
       N'Solicitar (vía Gestión, con aprobación) el fallecimiento de una persona, con el acta de defunción como respaldo.', N'sistema'
WHERE NOT EXISTS (SELECT 1 FROM sec.OBJETO_AUTORIZACION WHERE codigo = N'CLI_FALLECIMIENTO');
GO

DECLARE @of  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'OFICIAL_CUMPLIMIENTO' AND id_compania IS NULL);
DECLARE @an  bigint = (SELECT id_rol_autorizacion FROM sec.ROL_AUTORIZACION WHERE codigo = N'ANALISTA_CLIENTES' AND id_compania IS NULL);
DECLARE @map TABLE (rol bigint, objeto nvarchar(100));
INSERT INTO @map VALUES (@an, N'CLI_FALLECIMIENTO'), (@of, N'CLI_FALLECIMIENTO');
INSERT INTO sec.ROL_AUTORIZACION_OBJETO (id_rol_autorizacion, id_objeto_autorizacion)
SELECT m.rol, o.id_objeto_autorizacion FROM @map m JOIN sec.OBJETO_AUTORIZACION o ON o.codigo = m.objeto
WHERE m.rol IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sec.ROL_AUTORIZACION_OBJETO x WHERE x.id_rol_autorizacion = m.rol AND x.id_objeto_autorizacion = o.id_objeto_autorizacion);
GO

/* Verificacion: el tipo de documento del acta debe existir y estar activo en cada compania. */
SELECT c.id_compania, t.codigo, t.nombre, t.estado FROM gen.COMPANIA c
LEFT JOIN doc.TIPO_DOCUMENTO t ON t.id_compania = c.id_compania AND t.codigo = N'ACTA_DEFUNCION';
SELECT codigo, requiere_aprobacion_obligatoria FROM apr.TIPO_GESTION WHERE codigo = N'CLI_FALLECIMIENTO';
GO
