/* ============================================================================
   MEJORAS RONDA 5 - Bitacora: altas de registros dependientes
   ============================================================================
   Las lineas de auditoria antiguas de registros nuevos de un cliente (direccion, identificacion o contacto
   agregados por gestion) quedaron como "Cambio" con campos del tipo dir[+1].detalle. Se pasan a "Alta".
   Las lineas nuevas ya se generan como una sola "Alta" con el resumen del registro.
   Los nombres de campo y los valores se traducen al mostrarlos (no cambian en la base).
   Correr contra CoreBancario_DEV. Es repetible.
   ============================================================================ */
USE CoreBancario_DEV;
GO
SET NOCOUNT ON;
GO
UPDATE aud.LOG_AUDITORIA
SET accion = 'Alta'
WHERE accion = 'Cambio' AND entidad_negocio = 'INTERLOCUTOR'
  AND (nombre_campo LIKE 'ident[[]+%' OR nombre_campo LIKE 'dir[[]+%' OR nombre_campo LIKE 'cont[[]+%');
GO
SELECT COUNT(*) AS filas_alta_corregidas FROM aud.LOG_AUDITORIA
WHERE accion = 'Alta' AND entidad_negocio = 'INTERLOCUTOR' AND (nombre_campo LIKE 'ident[[]+%' OR nombre_campo LIKE 'dir[[]+%' OR nombre_campo LIKE 'cont[[]+%');
GO
