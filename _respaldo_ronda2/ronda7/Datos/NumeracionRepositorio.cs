using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Mantenimiento de gen.RANGO_NUMERACION (pantalla Administracion > Rangos de numeracion).
/// La asignacion de numeros NO esta aqui: la hace NumeracionServicio.
/// Reglas: una vez que el rango emitio numeros no se puede cambiar su longitud ni su inicio;
/// el limite superior solo puede crecer; los rangos de un mismo objeto no se solapan.
/// </summary>
public class NumeracionRepositorio
{
    public const string ObjetoAdministracion = "NUM_ADMINISTRACION";

    /// <summary>Objetos numerables hoy. CUENTA y CREDITO entran con su rango por producto cuando existan los productos.</summary>
    public static readonly IReadOnlyDictionary<string, string> Objetos = new Dictionary<string, string>
    {
        ["CLIENTE"] = "Cliente",
        ["PROSPECTO"] = "Prospecto",
        ["SOLICITUD_CREDITO"] = "Solicitud de crédito",
    };

    private readonly TenantContext _tenant;
    private readonly NumeracionServicio _servicio;

    public NumeracionRepositorio(TenantContext tenant, NumeracionServicio servicio)
    {
        _tenant = tenant;
        _servicio = servicio;
    }

    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private sealed record Fila(
        long id_rango_numeracion, string objeto, long id_alcance, byte longitud, long numero_desde, long numero_hasta,
        long ultimo_asignado, int tamano_bloque, string estado, string? modificado_por, DateTime? modificado_en);

    private static RangoNumeracionResponse A(Fila f)
    {
        var disponibles = f.estado == "Activo" ? f.numero_hasta - f.ultimo_asignado : 0;
        string? proximo = disponibles > 0 ? (f.ultimo_asignado + 1).ToString().PadLeft(f.longitud, '0') : null;
        return new RangoNumeracionResponse(
            f.id_rango_numeracion, f.objeto, Objetos.GetValueOrDefault(f.objeto, f.objeto), f.id_alcance, f.longitud,
            f.numero_desde, f.numero_hasta, f.ultimo_asignado, f.tamano_bloque, f.estado, disponibles, proximo,
            f.modificado_por, f.modificado_en);
    }

    private const string SqlColumnas = """
        id_rango_numeracion, objeto, id_alcance, longitud, numero_desde, numero_hasta,
        ultimo_asignado, tamano_bloque, estado, modificado_por, modificado_en
        """;

    public async Task<IReadOnlyList<RangoNumeracionResponse>> ListarAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync<Fila>(
            $"SELECT {SqlColumnas} FROM gen.RANGO_NUMERACION WHERE id_compania = @idCompania ORDER BY objeto, id_alcance, id_rango_numeracion",
            new { idCompania });
        return filas.Select(A).ToList();
    }

    private static List<string> ValidarLimites(int longitud, long desde, long hasta, int bloque)
    {
        var e = new List<string>();
        if (longitud is < 1 or > 18) e.Add("La longitud debe estar entre 1 y 18 digitos.");
        if (desde < 1) e.Add("El numero inicial debe ser 1 o mayor.");
        if (hasta < desde) e.Add("El numero final no puede ser menor al inicial.");
        if (longitud is >= 1 and <= 18 && hasta >= Math.Pow(10, longitud))
            e.Add($"El numero final no cabe en {longitud} digitos (maximo {new string('9', longitud)}).");
        if (bloque is < 1 or > 100000) e.Add("El tamano de bloque debe estar entre 1 y 100000.");
        return e;
    }

    private static async Task<bool> SolapaAsync(SqlConnection c, long idCompania, string objeto, long idAlcance, long desde, long hasta, long? excluir)
        => await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM gen.RANGO_NUMERACION
                 WHERE id_compania = @idCompania AND objeto = @objeto AND estado = 'Activo'
                   AND (@excluir IS NULL OR id_rango_numeracion <> @excluir)
                   AND numero_desde <= @hasta AND numero_hasta >= @desde) THEN 1 ELSE 0 END
            """, new { idCompania, objeto, desde, hasta, excluir });

    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, RangoNumeracionCrearRequest r)
    {
        var objeto = (r.Objeto ?? string.Empty).Trim().ToUpperInvariant();
        if (!Objetos.ContainsKey(objeto))
            return ResultadoOperacion.Fallo(400, "datos_invalidos", $"Objeto no valido. Use: {string.Join(", ", Objetos.Keys)}.");

        var errores = ValidarLimites(r.Longitud, r.NumeroDesde, r.NumeroHasta, r.TamanoBloque);
        if (errores.Count > 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

        using var c = NuevaConexion();
        c.Open();

        var yaHay = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.RANGO_NUMERACION WHERE id_compania = @c AND objeto = @o AND id_alcance = 0 AND estado = 'Activo') THEN 1 ELSE 0 END",
            new { c = actor.IdCompania, o = objeto });
        if (yaHay)
            return ResultadoOperacion.Fallo(409, "rango_existente", $"Ya existe un rango activo de {Objetos[objeto]}. Edítelo o desactívelo primero.");

        if (await SolapaAsync(c, actor.IdCompania, objeto, 0, r.NumeroDesde, r.NumeroHasta, null))
            return ResultadoOperacion.Fallo(409, "rango_solapado", "El rango se solapa con otro rango activo del mismo objeto.");

        var id = await c.QuerySingleAsync<long>(
            """
            INSERT INTO gen.RANGO_NUMERACION (id_compania, objeto, id_alcance, longitud, numero_desde, numero_hasta, ultimo_asignado, tamano_bloque, creado_por)
            OUTPUT INSERTED.id_rango_numeracion
            VALUES (@c, @o, 0, @l, @d, @h, @d - 1, @b, @por)
            """,
            new { c = actor.IdCompania, o = objeto, l = r.Longitud, d = r.NumeroDesde, h = r.NumeroHasta, b = r.TamanoBloque, por = actor.NombreDescriptivo });

        return ResultadoOperacion.Ok(id);
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long idRango, RangoNumeracionEditarRequest r)
    {
        using var c = NuevaConexion();
        c.Open();

        var f = await c.QuerySingleOrDefaultAsync<Fila>(
            $"SELECT {SqlColumnas} FROM gen.RANGO_NUMERACION WHERE id_rango_numeracion = @idRango AND id_compania = @c",
            new { idRango, c = actor.IdCompania });
        if (f is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El rango no existe.");

        if (r.Estado is not ("Activo" or "Inactivo"))
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "El estado debe ser Activo o Inactivo.");

        var errores = ValidarLimites(r.Longitud, r.NumeroDesde, r.NumeroHasta, r.TamanoBloque);

        var yaEmitio = f.ultimo_asignado >= f.numero_desde;
        if (yaEmitio)
        {
            if (r.Longitud != f.longitud) errores.Add("No se puede cambiar la longitud: el rango ya emitio numeros.");
            if (r.NumeroDesde != f.numero_desde) errores.Add("No se puede cambiar el numero inicial: el rango ya emitio numeros.");
            if (r.NumeroHasta < f.ultimo_asignado) errores.Add($"El numero final no puede ser menor al ultimo asignado ({f.ultimo_asignado}).");
        }
        if (errores.Count > 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

        if (r.Estado == "Activo")
        {
            if (f.estado == "Inactivo" &&
                await c.ExecuteScalarAsync<bool>(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.RANGO_NUMERACION WHERE id_compania = @c AND objeto = @o AND id_alcance = @a AND estado = 'Activo' AND id_rango_numeracion <> @id) THEN 1 ELSE 0 END",
                    new { c = actor.IdCompania, o = f.objeto, a = f.id_alcance, id = idRango }))
                return ResultadoOperacion.Fallo(409, "rango_existente", "Ya hay otro rango activo para este objeto.");

            if (await SolapaAsync(c, actor.IdCompania, f.objeto, f.id_alcance, r.NumeroDesde, r.NumeroHasta, idRango))
                return ResultadoOperacion.Fallo(409, "rango_solapado", "El rango se solapa con otro rango activo del mismo objeto.");
        }

        // Si el rango aun no emitio, "ultimo_asignado" acompana al nuevo inicio.
        var ultimo = yaEmitio ? f.ultimo_asignado : r.NumeroDesde - 1;

        await c.ExecuteAsync(
            """
            UPDATE gen.RANGO_NUMERACION
               SET longitud = @l, numero_desde = @d, numero_hasta = @h, ultimo_asignado = @u, tamano_bloque = @b,
                   estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME()
             WHERE id_rango_numeracion = @id
            """,
            new { l = r.Longitud, d = r.NumeroDesde, h = r.NumeroHasta, u = ultimo, b = r.TamanoBloque, e = r.Estado, por = actor.NombreDescriptivo, id = idRango });

        // Lo que quedaba en cache de este rango se descarta (queda como hueco, aceptado).
        _servicio.Invalidar(_tenant.ConnectionString, actor.IdCompania, f.objeto, f.id_alcance);
        return ResultadoOperacion.Ok(idRango);
    }
}
