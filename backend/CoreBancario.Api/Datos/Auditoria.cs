using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;

namespace CoreBancario.Api.Datos;

/// <summary>Escritura en aud.LOG_AUDITORIA para cambios directos (sin gestion), p. ej. administracion de seguridad, monedas, parametros.</summary>
public static class Auditoria
{
    public static Task AltaAsync(SqlConnection c, SqlTransaction? tx, ActorContext actor, string entidad, long llave, string? detalle = null, long? idGestion = null)
        => InsertarAsync(c, tx, actor, entidad, llave, "Alta", null, null, detalle, idGestion);

    public static Task CambioAsync(SqlConnection c, SqlTransaction? tx, ActorContext actor, string entidad, long llave, string campo, string? anterior, string? nuevo, long? idGestion = null)
        => InsertarAsync(c, tx, actor, entidad, llave, "Cambio", campo, anterior, nuevo, idGestion);

    /// <summary>Consulta de un dato sensible (revelar un dato enmascarado). campo: dato consultado; detalle: contexto.</summary>
    public static Task ConsultaAsync(SqlConnection c, SqlTransaction? tx, ActorContext actor, string entidad, long llave, string campo, string? detalle = null)
        => InsertarAsync(c, tx, actor, entidad, llave, "Consulta", campo, null, detalle, null);

    public static Task BajaAsync(SqlConnection c, SqlTransaction? tx, ActorContext actor, string entidad, long llave, string? detalle = null, long? idGestion = null)
        => InsertarAsync(c, tx, actor, entidad, llave, "Baja", null, detalle, null, idGestion);

    private static async Task InsertarAsync(SqlConnection c, SqlTransaction? tx, ActorContext actor, string entidad, long llave,
        string accion, string? campo, string? anterior, string? nuevo, long? idGestion)
    {
        await c.ExecuteAsync(
            """
            INSERT INTO aud.LOG_AUDITORIA (id_compania, id_usuario, id_usuario_integracion, entidad_negocio, llave_registro, accion,
                                           nombre_campo, valor_anterior, valor_nuevo, id_gestion)
            VALUES (@idCompania, @idUsuario, @idIntegracion, @entidad, @llave, @accion, @campo, @anterior, @nuevo, @idGestion)
            """,
            new
            {
                actor.IdCompania, idUsuario = actor.IdUsuario, idIntegracion = actor.IdUsuarioIntegracion, entidad,
                llave = llave.ToString(CultureInfo.InvariantCulture),
                accion, campo, anterior = Recortar(anterior), nuevo = Recortar(nuevo), idGestion
            }, tx);
    }

    private static string? Recortar(string? s) => s is { Length: > 1000 } ? s[..1000] : s;
}
