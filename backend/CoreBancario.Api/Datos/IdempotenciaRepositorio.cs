using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Idempotencia de las operaciones por API (header Idempotency-Key). Se reserva la llave ANTES de operar (evita que dos envios
/// simultaneos creen dos clientes); al terminar se guarda la respuesta para repetirla ante un reintento.
/// </summary>
public class IdempotenciaRepositorio
{
    private readonly TenantContext _tenant;
    public IdempotenciaRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    public sealed record Registro(long Id, string Huella, string Estado, int? HttpEstado, string? Respuesta);

    /// <summary>(true, reserva) si la llave es nueva; (false, existente) si ya se uso.</summary>
    public async Task<(bool Nueva, Registro Registro)> ReservarAsync(ActorContext actor, string operacion, string llave, string huella, string correlacion)
    {
        using var c = NuevaConexion();
        try
        {
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO gen.API_IDEMPOTENCIA (id_compania, id_usuario_integracion, llave, operacion, huella, estado, id_correlacion)
                OUTPUT INSERTED.id_idempotencia
                VALUES (@c, @i, @llave, @operacion, @huella, 'EN_PROCESO', @correlacion)
                """, new { c = actor.IdCompania, i = actor.IdUsuarioIntegracion, llave, operacion, huella, correlacion });
            return (true, new Registro(id, huella, "EN_PROCESO", null, null));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            var r = (await c.QueryAsync<(long id_idempotencia, string huella, string estado, short? http_estado, string? respuesta)>(
                """
                SELECT id_idempotencia, huella, estado, http_estado, respuesta FROM gen.API_IDEMPOTENCIA
                WHERE id_compania = @c AND id_usuario_integracion = @i AND operacion = @operacion AND llave = @llave
                """, new { c = actor.IdCompania, i = actor.IdUsuarioIntegracion, operacion, llave })).First();
            return (false, new Registro(r.id_idempotencia, r.huella, r.estado, r.http_estado, r.respuesta));
        }
    }

    public async Task CompletarAsync(long id, int httpEstado, string respuesta)
    {
        using var c = NuevaConexion();
        await c.ExecuteAsync("UPDATE gen.API_IDEMPOTENCIA SET estado = 'COMPLETA', http_estado = @httpEstado, respuesta = @respuesta WHERE id_idempotencia = @id",
            new { id, httpEstado, respuesta });
    }

    /// <summary>Error inesperado (5xx): se libera la llave para que el integrador pueda reintentar.</summary>
    public async Task LiberarAsync(long id)
    {
        using var c = NuevaConexion();
        await c.ExecuteAsync("DELETE FROM gen.API_IDEMPOTENCIA WHERE id_idempotencia = @id AND estado = 'EN_PROCESO'", new { id });
    }
}
