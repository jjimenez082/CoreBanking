/* ============================================================================
   Ronda 15 - Notificaciones: estado "leida" por usuario en la base de datos
   Ejecutar en CoreBancario_DEV. Idempotente.
   Las notificaciones se siguen derivando del estado de las gestiones (no se guardan); aqui solo se
   guarda cuales claves ya leyo cada usuario, para que valga en cualquier equipo y para que se marquen
   solas al abrir la gestion por cualquier camino.
   ============================================================================ */

IF OBJECT_ID(N'sec.NOTIFICACION_LEIDA') IS NULL
BEGIN
    CREATE TABLE sec.NOTIFICACION_LEIDA (
        id_usuario  bigint        NOT NULL,
        clave       nvarchar(120) NOT NULL,
        leida_en    datetime2     NOT NULL CONSTRAINT DF_NOTIFICACION_LEIDA_leida_en DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_NOTIFICACION_LEIDA PRIMARY KEY (id_usuario, clave),
        CONSTRAINT FK_NOTIFICACION_LEIDA_USUARIO FOREIGN KEY (id_usuario) REFERENCES sec.USUARIO (id_usuario)
    );
END
GO
