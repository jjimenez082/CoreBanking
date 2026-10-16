using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Tipo de cambio (gen.TIPO_CAMBIO): compra, venta y referencia por moneda y fecha, contra la moneda base.
/// La carga NO es directa: se solicita como gestion TC_CARGA y se aplica al aprobarse (ver GestionRepositorio.TipoCambio).
/// Un tipo de cambio rige desde su fecha hasta la siguiente cargada.
/// </summary>
public class TipoCambioRepositorio
{
    public const string PermisoConsulta = "TC_CONSULTA";
    public const string PermisoCarga = "TC_CARGA";
    public const string TipoGestion = "TC_CARGA";
    public const string Entidad = "TIPO_CAMBIO";
    public const string ParametroVariacion = "TC_VARIACION_MAXIMA_PORC";

    private readonly TenantContext _tenant;
    public TipoCambioRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    /// <summary>Llave de la gestion: moneda y fecha (una sola carga pendiente por moneda y fecha).</summary>
    public static string Llave(long idMoneda, DateOnly fecha) => $"{idMoneda}|{fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
    public static string Num(decimal v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<TipoCambioResponse>> ListarAsync(long idCompania, long? idMoneda, DateOnly? desde, DateOnly? hasta)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT TOP (500) t.id_tipo_cambio, t.id_moneda, m.codigo, m.nombre, t.fecha_vigencia, t.compra, t.venta, t.referencia,
                   t.origen, t.id_gestion, t.creado_por, t.creado_en
            FROM gen.TIPO_CAMBIO t
            JOIN gen.MONEDA m ON m.id_moneda = t.id_moneda
            WHERE t.id_compania = @idCompania
              AND (@idMoneda IS NULL OR t.id_moneda = @idMoneda)
              AND (@desde IS NULL OR t.fecha_vigencia >= @desde)
              AND (@hasta IS NULL OR t.fecha_vigencia <= @hasta)
            ORDER BY t.fecha_vigencia DESC, m.codigo
            """,
            new { idCompania, idMoneda, desde = desde?.ToDateTime(TimeOnly.MinValue), hasta = hasta?.ToDateTime(TimeOnly.MinValue) });
        return filas.Select(f => new TipoCambioResponse(
            (long)f.id_tipo_cambio, (long)f.id_moneda, (string)f.codigo, (string)f.nombre, DateOnly.FromDateTime((DateTime)f.fecha_vigencia),
            (decimal)f.compra, (decimal)f.venta, (decimal)f.referencia, (string)f.origen, (long?)f.id_gestion, (string)f.creado_por, (DateTime)f.creado_en)).Cast<TipoCambioResponse>().ToList();
    }

    /// <summary>Para cada moneda activa distinta de la base, el tipo de cambio vigente a la fecha (el ultimo con fecha menor o igual).</summary>
    public async Task<IReadOnlyList<TipoCambioVigenteResponse>> VigentesAsync(long idCompania, DateOnly fecha)
    {
        using var c = NuevaConexion();
        var baseCodigo = await c.ExecuteScalarAsync<string?>("SELECT codigo FROM gen.MONEDA WHERE id_compania = @c AND es_base = 1", new { c = idCompania }) ?? "";
        var filas = await c.QueryAsync(
            """
            SELECT m.id_moneda, m.codigo, m.nombre, v.fecha_vigencia, v.compra, v.venta, v.referencia
            FROM gen.MONEDA m
            OUTER APPLY (SELECT TOP 1 t.fecha_vigencia, t.compra, t.venta, t.referencia FROM gen.TIPO_CAMBIO t
                          WHERE t.id_moneda = m.id_moneda AND t.fecha_vigencia <= @fecha ORDER BY t.fecha_vigencia DESC) v
            WHERE m.id_compania = @idCompania AND m.estado = 'Activo' AND m.es_base = 0
            ORDER BY m.codigo
            """, new { idCompania, fecha = fecha.ToDateTime(TimeOnly.MinValue) });
        return filas.Select(f => new TipoCambioVigenteResponse(
            (long)f.id_moneda, (string)f.codigo, (string)f.nombre, baseCodigo,
            f.fecha_vigencia is null ? null : DateOnly.FromDateTime((DateTime)f.fecha_vigencia),
            (decimal?)f.compra, (decimal?)f.venta, (decimal?)f.referencia)).Cast<TipoCambioVigenteResponse>().ToList();
    }

    /// <summary>Valida y registra la gestion de carga (queda pendiente de aprobacion).</summary>
    public async Task<ResultadoOperacion> SolicitarCargaAsync(ActorContext actor, TipoCambioSolicitudRequest req)
    {
        if (actor.IdUsuario is null)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");

        var errorValores = ValidarValores(req.Compra, req.Venta, req.Referencia);
        if (errorValores is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", errorValores);

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var m = await c.QuerySingleOrDefaultAsync(
                "SELECT codigo, estado, es_base FROM gen.MONEDA WHERE id_moneda = @id AND id_compania = @c", new { id = req.IdMoneda, c = actor.IdCompania }, tx);
            if (m is null || (string)m.estado != "Activo") return ResultadoOperacion.Fallo(400, "datos_invalidos", "La moneda no existe o esta inactiva.");
            if ((bool)m.es_base) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La moneda base no tiene tipo de cambio (siempre vale 1).");

            var llave = Llave(req.IdMoneda, req.FechaVigencia);
            var pendiente = await c.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id_gestion FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
                new { e = Entidad, llave, p = GestionRepositorio.EstadoPendiente }, tx);
            if (pendiente is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"Ya hay una carga (gestion #{pendiente}) pendiente de aprobacion para {(string)m.codigo} en esa fecha.");

            var fecha = req.FechaVigencia.ToDateTime(TimeOnly.MinValue);
            var existente = await c.QuerySingleOrDefaultAsync(
                "SELECT compra, venta, referencia FROM gen.TIPO_CAMBIO WHERE id_compania = @c AND id_moneda = @m AND fecha_vigencia = @f",
                new { c = actor.IdCompania, m = req.IdMoneda, f = fecha }, tx);

            // Control de variacion respecto al vigente a esa fecha (parametro TC_VARIACION_MAXIMA_PORC; 0 = sin limite).
            var maxTxt = await c.ExecuteScalarAsync<string?>(
                "SELECT valor FROM gen.PARAMETRO WHERE id_compania = @c AND codigo = @p", new { c = actor.IdCompania, p = ParametroVariacion }, tx);
            if (decimal.TryParse(maxTxt, NumberStyles.Number, CultureInfo.InvariantCulture, out var max) && max > 0)
            {
                var previo = await c.ExecuteScalarAsync<decimal?>(
                    "SELECT TOP 1 referencia FROM gen.TIPO_CAMBIO WHERE id_compania = @c AND id_moneda = @m AND fecha_vigencia < @f ORDER BY fecha_vigencia DESC",
                    new { c = actor.IdCompania, m = req.IdMoneda, f = fecha }, tx);
                if (previo is > 0)
                {
                    var variacion = Math.Abs(req.Referencia - previo.Value) / previo.Value * 100m;
                    if (variacion > max)
                        return ResultadoOperacion.Fallo(400, "variacion_excedida",
                            $"La referencia varia {variacion:0.##}% respecto a la vigente ({Num(previo.Value)}); el maximo permitido es {max:0.##}%.");
                }
            }

            var idTipo = await c.ExecuteScalarAsync<long?>("SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoGestion }, tx);
            if (idTipo is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", "Falta el tipo de gestion TC_CARGA; correr administracion_transversal.sql.");

            var idGestion = await c.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@c, @tipo, @estado, @u, @entidad, @llave, @comentario, @por)
                """,
                new
                {
                    c = actor.IdCompania, tipo = idTipo, estado = GestionRepositorio.EstadoPendiente, u = actor.IdUsuario, entidad = Entidad, llave,
                    comentario = string.IsNullOrWhiteSpace(req.Comentario) ? null : req.Comentario.Trim(), por = actor.NombreDescriptivo
                }, tx);

            async Task Detalle(string campo, string? anterior, string? nuevo) => await c.ExecuteAsync(
                "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@g, @campo, @a, @n)",
                new { g = idGestion, campo, a = anterior, n = nuevo }, tx);

            await Detalle("id_moneda", null, req.IdMoneda.ToString(CultureInfo.InvariantCulture));
            await Detalle("fecha_vigencia", null, req.FechaVigencia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await Detalle("compra", existente is null ? null : Num((decimal)existente.compra), Num(req.Compra));
            await Detalle("venta", existente is null ? null : Num((decimal)existente.venta), Num(req.Venta));
            await Detalle("referencia", existente is null ? null : Num((decimal)existente.referencia), Num(req.Referencia));

            await c.ExecuteAsync("INSERT INTO apr.GESTION_APROBACION (id_gestion, nivel) VALUES (@g, 1)", new { g = idGestion }, tx);
            tx.Commit();
            return ResultadoOperacion.Ok(idGestion);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "Ya hay una carga pendiente de aprobacion para esa moneda y fecha.");
        }
        catch { tx.Rollback(); throw; }
    }

    public static string? ValidarValores(decimal compra, decimal venta, decimal referencia)
    {
        if (compra <= 0 || venta <= 0 || referencia <= 0) return "Compra, venta y referencia deben ser mayores que cero.";
        if (decimal.Round(compra, 6) != compra || decimal.Round(venta, 6) != venta || decimal.Round(referencia, 6) != referencia)
            return "Maximo 6 decimales.";
        if (compra > 999999999999m || venta > 999999999999m || referencia > 999999999999m) return "Valor demasiado grande.";
        if (compra > venta) return "La compra no puede ser mayor que la venta.";
        return null;
    }
}
