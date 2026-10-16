using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CoreBancario.Api.Infraestructura;

/// <summary>Error de negocio del motor de numeracion (sin rango, rango agotado...).</summary>
public class NumeracionException(string codigo, string mensaje) : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
}

/// <summary>
/// Motor de numeracion tipo "rangos de numeros" de SAP.
///
/// - El numero se pide al FINAL del proceso, justo antes de crear el registro.
/// - Reserva un BLOQUE de numeros en gen.RANGO_NUMERACION con un UPDATE atomico
///   (corto, en su propia conexion: nunca mantiene bloqueos durante un alta) y
///   los entrega desde memoria, de modo que los procesos masivos no compiten
///   por la tabla de numeracion en cada registro.
/// - Se aceptan huecos: numeros reservados que no se usan (alta que falla despues
///   de pedir el numero, reinicio de la API con un bloque a medio usar) se pierden.
/// - Singleton: el cache vive en esta instancia de la API. Con tamano_bloque = 1
///   (valor por defecto) no hay cache y es seguro con varias instancias; un
///   bloque mayor se recomienda solo con una instancia o para cargas masivas.
/// </summary>
public class NumeracionServicio
{
    private sealed class Bloque
    {
        public long Siguiente;
        public long Fin;       // ultimo numero reservado en este bloque (inclusive)
        public int Longitud;
        public bool Vacio => Siguiente > Fin;
    }

    private readonly ConcurrentDictionary<string, Bloque> _bloques = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _candados = new();

    private static string Llave(string cadenaConexion, long idCompania, string objeto, long idAlcance) =>
        $"{cadenaConexion}|{idCompania}|{objeto}|{idAlcance}";

    /// <summary>Descarta los numeros en cache de un rango (tras editarlo); lo no usado queda como hueco.</summary>
    public void Invalidar(string cadenaConexion, long idCompania, string objeto, long idAlcance = 0) =>
        _bloques.TryRemove(Llave(cadenaConexion, idCompania, objeto, idAlcance), out _);

    /// <summary>Siguiente numero de negocio ya formateado (ceros a la izquierda, longitud fija).</summary>
    public async Task<string> SiguienteAsync(
        string cadenaConexion, long idCompania, string objeto, long idAlcance = 0, CancellationToken ct = default)
    {
        var llave = Llave(cadenaConexion, idCompania, objeto, idAlcance);
        var candado = _candados.GetOrAdd(llave, _ => new SemaphoreSlim(1, 1));

        await candado.WaitAsync(ct);
        try
        {
            if (!_bloques.TryGetValue(llave, out var bloque) || bloque.Vacio)
            {
                bloque = await ReservarBloqueAsync(cadenaConexion, idCompania, objeto, idAlcance, ct);
                _bloques[llave] = bloque;
            }

            var valor = bloque.Siguiente++;
            return valor.ToString().PadLeft(bloque.Longitud, '0');
        }
        finally
        {
            candado.Release();
        }
    }

    private static async Task<Bloque> ReservarBloqueAsync(
        string cadenaConexion, long idCompania, string objeto, long idAlcance, CancellationToken ct)
    {
        using var conexion = new SqlConnection(cadenaConexion);

        // Un unico UPDATE: reserva min(tamano_bloque, lo que queda) y devuelve los limites.
        var fila = await conexion.QuerySingleOrDefaultAsync(new CommandDefinition(
            """
            UPDATE gen.RANGO_NUMERACION WITH (UPDLOCK, ROWLOCK)
               SET ultimo_asignado = ultimo_asignado
                     + CASE WHEN ultimo_asignado + tamano_bloque <= numero_hasta
                            THEN tamano_bloque ELSE numero_hasta - ultimo_asignado END,
                   modificado_por = N'motor de numeracion', modificado_en = SYSUTCDATETIME()
            OUTPUT deleted.ultimo_asignado AS Antes, inserted.ultimo_asignado AS Despues, inserted.longitud AS Longitud
             WHERE id_compania = @idCompania AND objeto = @objeto AND id_alcance = @idAlcance
               AND estado = 'Activo' AND ultimo_asignado < numero_hasta
            """, new { idCompania, objeto, idAlcance }, cancellationToken: ct));

        if (fila is not null)
            return new Bloque { Siguiente = (long)fila.Antes + 1, Fin = (long)fila.Despues, Longitud = (int)(byte)fila.Longitud };

        // No se pudo reservar: distinguir "no configurado" de "agotado".
        var existe = await conexion.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.RANGO_NUMERACION
                                      WHERE id_compania = @idCompania AND objeto = @objeto AND id_alcance = @idAlcance AND estado = 'Activo')
                        THEN 1 ELSE 0 END
            """, new { idCompania, objeto, idAlcance }, cancellationToken: ct));

        throw existe
            ? new NumeracionException("rango_agotado",
                $"El rango de numeracion de {objeto} esta agotado. Amplie el rango en Administracion > Rangos de numeracion.")
            : new NumeracionException("rango_no_configurado",
                $"No hay un rango de numeracion activo para {objeto}. Configurelo en Administracion > Rangos de numeracion.");
    }
}
