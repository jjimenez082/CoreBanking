using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Maestro de mensajes del sistema (gen.MENSAJE_SISTEMA): el codigo es estable para el integrador y el texto lo parametriza la
/// compania. Placeholders con llaves: {identificacion}, {numeroCliente}, {numeroGestion}.
/// </summary>
public class MensajesRepositorio
{
    public const string ObjetoAdministracion = "MSG_ADMINISTRACION";
    private readonly TenantContext _tenant;
    public MensajesRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    /// <summary>Mensaje resuelto: Codigo es el oficial (ER_CLIE_001); Nombre es la clave interna/legible (DATOS_INVALIDOS).</summary>
    public sealed record Resuelto(string Codigo, string Nombre, string Severidad, string Resultado, int Http, string Texto);

    /// <summary>Texto y estado HTTP del mensaje (por su nombre interno) con los valores reemplazados; null si no existe.</summary>
    public async Task<Resuelto?> ResolverAsync(long idCompania, string nombre, IReadOnlyDictionary<string, string>? valores = null)
    {
        using var c = NuevaConexion();
        var m = (await c.QueryAsync<(string codigo, string nombre, string severidad, string resultado, short http_estado, string texto)>(
            "SELECT codigo, nombre, severidad, resultado, http_estado, texto FROM gen.MENSAJE_SISTEMA WHERE id_compania = @idCompania AND nombre = @nombre",
            new { idCompania, nombre })).FirstOrDefault();
        if (m.codigo is null) return null;
        var texto = m.texto;
        if (valores is not null)
            foreach (var (k, v) in valores) texto = texto.Replace("{" + k + "}", v, StringComparison.OrdinalIgnoreCase);
        return new Resuelto(m.codigo, m.nombre, m.severidad, m.resultado, m.http_estado, texto);
    }

    public async Task<IReadOnlyList<MensajeSistemaResponse>> ListarAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync<(long id_mensaje, string codigo, string nombre, string severidad, string modulo, string resultado, short http_estado, string texto, string texto_defecto, string? descripcion, string? modificado_por, DateTime? modificado_en)>(
            """
            SELECT id_mensaje, codigo, nombre, severidad, modulo, resultado, http_estado, texto, texto_defecto, descripcion, modificado_por, modificado_en
            FROM gen.MENSAJE_SISTEMA WHERE id_compania = @idCompania ORDER BY modulo, codigo
            """, new { idCompania });
        return filas.Select(f => new MensajeSistemaResponse(f.id_mensaje, f.codigo, f.nombre, f.severidad, f.modulo, f.resultado, f.http_estado, f.texto, f.texto_defecto,
            f.descripcion, f.texto != f.texto_defecto, f.modificado_por, f.modificado_en)).ToList();
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long idMensaje, string? texto, bool restaurar)
    {
        using var c = NuevaConexion();
        c.Open();
        var m = (await c.QueryAsync<(string codigo, string texto, string texto_defecto)>(
            "SELECT codigo, texto, texto_defecto FROM gen.MENSAJE_SISTEMA WHERE id_mensaje = @idMensaje AND id_compania = @c",
            new { idMensaje, c = actor.IdCompania })).FirstOrDefault();
        if (m.codigo is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El mensaje no existe.");

        var nuevo = restaurar ? m.texto_defecto : (texto ?? string.Empty).Trim();
        if (nuevo.Length is < 5 or > 500)
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "El texto debe tener entre 5 y 500 caracteres.");
        if (nuevo == m.texto) return ResultadoOperacion.Ok(idMensaje);

        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(
            "UPDATE gen.MENSAJE_SISTEMA SET texto = @nuevo, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_mensaje = @idMensaje",
            new { nuevo, por = actor.NombreDescriptivo, idMensaje }, tx);
        await Auditoria.CambioAsync(c, tx, actor, "MENSAJE_SISTEMA", idMensaje, m.codigo, m.texto, nuevo);
        tx.Commit();
        return ResultadoOperacion.Ok(idMensaje);
    }
}
