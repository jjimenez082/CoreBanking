using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Vigencia del KYC. Son dos conceptos distintos: el estado del expediente (Pendiente / En revision / Completo / Rechazado, lo fija Cumplimiento) y la
/// vigencia (Vigente / Por vencer / Vencido), que se deduce de la fecha de proxima revision comparada con hoy.
///
/// - La proxima revision es AUTOMATICA: al crear el cliente = creacion + meses de primera revision; al registrar una revision = ultima revision +
///   frecuencia segun el riesgo (parametros KYC_FRECUENCIA_*). Nunca se captura a mano ni se adelanta una fecha ya vencida sin una revision nueva.
/// - La situacion se calcula siempre con la fecha de hoy en la zona horaria de la compania (la guardia de apertura de productos no depende del
///   ultimo proceso de fin de dia). Ademas se guarda (cli.INTERLOCUTOR_KYC.vigencia) con historial de cambios; el proceso de fin de dia la recalcula.
/// </summary>
public static class KycVigenciaServicio
{
    public const string Vigente = "Vigente";
    public const string PorVencer = "Por vencer";
    public const string Vencido = "Vencido";
    public const string SinKyc = "Sin KYC";

    public const string ParPrimeraRevision = "KYC_MESES_PRIMERA_REVISION";
    public const string ParPorVencer = "KYC_MESES_POR_VENCER";
    public const string ParGracia = "KYC_MESES_GRACIA_BLOQUEO";
    public const string ParFrecuenciaPrefijo = "KYC_FRECUENCIA_";

    public const string OrigenCargaInicial = "Carga inicial";
    public const string OrigenAlta = "Alta de cliente";
    public const string OrigenGestion = "Gestion de KYC";
    public const string OrigenFinDeDia = "Proceso de fin de dia";

    public record Parametros(int MesesPrimeraRevision, int MesesPorVencer, int MesesGracia, IReadOnlyDictionary<string, int> Frecuencias);

    public static DateTime D(DateOnly f) => f.ToDateTime(TimeOnly.MinValue);
    private static DateOnly? A(object? o) => o is DateTime d ? DateOnly.FromDateTime(d) : null;

    /// <summary>Fecha de hoy en la zona horaria de la compania.</summary>
    public static async Task<DateOnly> HoyAsync(SqlConnection c, SqlTransaction? tx, long idCompania)
    {
        var id = await ParametroRepositorio.ValorAsync(c, tx, idCompania, "ZONA_HORARIA") ?? "America/Costa_Rica";
        TimeZoneInfo zona;
        try { zona = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { zona = TimeZoneInfo.Utc; }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zona));
    }

    public static async Task<Parametros> LeerParametrosAsync(SqlConnection c, SqlTransaction? tx, long idCompania)
    {
        var filas = (await c.QueryAsync(
            "SELECT codigo, valor FROM gen.PARAMETRO WHERE id_compania = @idCompania AND codigo LIKE N'KYC[_]%'", new { idCompania }, tx))
            .ToDictionary(x => (string)x.codigo, x => (string)x.valor);
        int E(string codigo, int defecto) => filas.TryGetValue(codigo, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : defecto;
        var frecuencias = filas.Keys.Where(k => k.StartsWith(ParFrecuenciaPrefijo, StringComparison.Ordinal))
            .ToDictionary(k => k[ParFrecuenciaPrefijo.Length..], k => E(k, 12), StringComparer.OrdinalIgnoreCase);
        return new Parametros(E(ParPrimeraRevision, 12), E(ParPorVencer, 1), Math.Max(0, filas.TryGetValue(ParGracia, out var g) && int.TryParse(g, out var gn) ? gn : 6), frecuencias);
    }

    /// <summary>Vigente, Por vencer, Vencido (o Sin KYC si no hay fecha de proxima revision).</summary>
    public static string Situacion(DateOnly? proxima, DateOnly hoy, Parametros p)
    {
        if (proxima is not { } f) return SinKyc;
        if (f < hoy) return Vencido;
        return f <= hoy.AddMonths(p.MesesPorVencer) ? PorVencer : Vigente;
    }

    public static DateOnly? FechaBloqueoPrevista(DateOnly? proxima, Parametros p) => proxima?.AddMonths(p.MesesGracia);

    /// <summary>
    /// Proxima revision automatica. Base = la ultima revision (nueva o registrada); sin ella se conserva la actual (creacion + primera revision).
    /// Frecuencia = la del riesgo; sin riesgo, la de la primera revision. Si la fecha actual ya esta vencida y no hay una revision nueva, no se adelanta.
    /// </summary>
    public static DateOnly CalcularProxima(DateOnly? ultimaNueva, DateOnly? ultimaActual, string? codigoRiesgo, DateOnly? proximaActual, Parametros p, DateOnly hoy)
    {
        var @base = ultimaNueva ?? ultimaActual;
        if (@base is null) return proximaActual ?? hoy.AddMonths(p.MesesPrimeraRevision);
        var meses = codigoRiesgo is not null && p.Frecuencias.TryGetValue(codigoRiesgo, out var m) ? m : p.MesesPrimeraRevision;
        var calculada = @base.Value.AddMonths(meses);
        var revisionNueva = ultimaNueva is not null && ultimaNueva != ultimaActual;
        if (!revisionNueva && proximaActual is { } pa && pa < hoy) return pa;
        return calculada;
    }

    public static async Task<string?> CodigoRiesgoAsync(SqlConnection c, SqlTransaction? tx, long? idRiesgo)
        => idRiesgo is null ? null : await c.ExecuteScalarAsync<string?>("SELECT codigo FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_item = @id", new { id = idRiesgo }, tx);

    /// <summary>Datos actuales del expediente (o null si el cliente no tiene).</summary>
    public static async Task<(DateOnly? Ultima, DateOnly? Proxima, string? Vigencia, long? IdRiesgo)?> LeerAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor)
    {
        var k = await c.QuerySingleOrDefaultAsync(
            "SELECT fecha_ultima_revision, fecha_proxima_revision, vigencia, id_riesgo FROM cli.INTERLOCUTOR_KYC WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor }, tx);
        if (k is null) return null;
        return (A((object?)k.fecha_ultima_revision), A((object?)k.fecha_proxima_revision), (string?)k.vigencia, (long?)k.id_riesgo);
    }

    /// <summary>Al crear un cliente: expediente Pendiente con proxima revision = hoy + meses de primera revision.</summary>
    public static async Task CrearExpedienteInicialAsync(SqlConnection c, SqlTransaction tx, long idCompania, long idInterlocutor, string por)
    {
        var hoy = await HoyAsync(c, tx, idCompania);
        var p = await LeerParametrosAsync(c, tx, idCompania);
        var proxima = hoy.AddMonths(p.MesesPrimeraRevision);
        var vigencia = Situacion(proxima, hoy, p);
        var idPendiente = await c.ExecuteScalarAsync<long?>(
            """
            SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = N'ESTADO_KYC' AND i.codigo = N'PENDIENTE'
            """, null, tx);
        await c.ExecuteAsync(
            """
            INSERT INTO cli.INTERLOCUTOR_KYC (id_interlocutor, id_estado_kyc, fecha_proxima_revision, vigencia, fecha_evaluacion_vigencia, creado_por)
            VALUES (@idInterlocutor, @idPendiente, @proxima, @vigencia, @hoy, @por)
            """, new { idInterlocutor, idPendiente, proxima = D(proxima), vigencia, hoy = D(hoy), por }, tx);
        await RegistrarHistorialAsync(c, tx, idInterlocutor, hoy, OrigenAlta, null, vigencia, null, proxima, null, por);
    }

    /// <summary>
    /// Calcula y guarda la proxima revision con los datos ya vigentes del expediente (ultima revision y riesgo actuales).
    /// previaUltima / previaProxima son los valores anteriores a la gestion: sirven para saber si hay una revision nueva y para no adelantar una fecha vencida.
    /// </summary>
    public static async Task CalcularYGuardarProximaAsync(
        SqlConnection c, SqlTransaction tx, long idCompania, long idInterlocutor, DateOnly? previaUltima, DateOnly? previaProxima)
    {
        var actual = await LeerAsync(c, tx, idInterlocutor);
        if (actual is null) return;
        var hoy = await HoyAsync(c, tx, idCompania);
        var p = await LeerParametrosAsync(c, tx, idCompania);
        var codigoRiesgo = await CodigoRiesgoAsync(c, tx, actual.Value.IdRiesgo);
        var proxima = CalcularProxima(actual.Value.Ultima, previaUltima, codigoRiesgo, previaProxima, p, hoy);
        await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_KYC SET fecha_proxima_revision = @proxima WHERE id_interlocutor = @idInterlocutor", new { proxima = D(proxima), idInterlocutor }, tx);
    }

    /// <summary>
    /// Recalcula y guarda la vigencia del cliente (la usan la aplicacion de una gestion de KYC y, mas adelante, el proceso de fin de dia).
    /// Deja historial cuando cambia la vigencia o la proxima revision respecto de los valores previos.
    /// </summary>
    public static async Task ActualizarVigenciaAsync(
        SqlConnection c, SqlTransaction tx, long idCompania, long idInterlocutor, string origen, long? idGestion, string por,
        string? vigenciaPrevia, DateOnly? proximaPrevia)
    {
        var actual = await LeerAsync(c, tx, idInterlocutor);
        if (actual is null) return;
        var hoy = await HoyAsync(c, tx, idCompania);
        var p = await LeerParametrosAsync(c, tx, idCompania);
        var vigencia = Situacion(actual.Value.Proxima, hoy, p);
        if (vigencia == SinKyc) return;
        await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR_KYC SET vigencia = @vigencia, fecha_evaluacion_vigencia = @hoy WHERE id_interlocutor = @idInterlocutor",
            new { vigencia, hoy = D(hoy), idInterlocutor }, tx);
        if (vigencia != vigenciaPrevia || actual.Value.Proxima != proximaPrevia)
            await RegistrarHistorialAsync(c, tx, idInterlocutor, hoy, origen, vigenciaPrevia, vigencia, proximaPrevia, actual.Value.Proxima, idGestion, por);
    }

    private static Task RegistrarHistorialAsync(
        SqlConnection c, SqlTransaction tx, long idInterlocutor, DateOnly fechaProceso, string origen, string? vigAnt, string? vigNueva,
        DateOnly? proxAnt, DateOnly? proxNueva, long? idGestion, string por)
        => c.ExecuteAsync(
            """
            INSERT INTO cli.INTERLOCUTOR_KYC_HIST (id_interlocutor, fecha_proceso, origen, vigencia_anterior, vigencia_nueva, proxima_anterior, proxima_nueva, id_gestion, creado_por)
            VALUES (@idInterlocutor, @fp, @origen, @vigAnt, @vigNueva, @pa, @pn, @idGestion, @por)
            """,
            new
            {
                idInterlocutor, fp = D(fechaProceso), origen, vigAnt, vigNueva,
                pa = proxAnt is { } a ? D(a) : (DateTime?)null, pn = proxNueva is { } n ? D(n) : (DateTime?)null, idGestion, por
            }, tx);

    /// <summary>Vigencia en tiempo real de un cliente (para detalle, 360 y consulta por API).</summary>
    public static async Task<KycVigenciaResponse?> ObtenerAsync(SqlConnection c, long idCompania, long idInterlocutor)
    {
        var k = await LeerAsync(c, null, idInterlocutor);
        if (k is null) return null;
        var hoy = await HoyAsync(c, null, idCompania);
        var p = await LeerParametrosAsync(c, null, idCompania);
        return Armar(k.Value.Proxima, hoy, p);
    }

    private static KycVigenciaResponse Armar(DateOnly? proxima, DateOnly hoy, Parametros p)
    {
        var situacion = Situacion(proxima, hoy, p);
        int? dias = proxima is { } f ? f.DayNumber - hoy.DayNumber : null;
        var permite = situacion != Vencido;
        var mensaje = situacion switch
        {
            Vencido => $"El KYC del cliente está vencido desde el {proxima:dd/MM/yyyy}; no se pueden abrir productos hasta actualizarlo.",
            PorVencer => $"El KYC vence el {proxima:dd/MM/yyyy} ({dias} días). Solicite la actualización de datos al cliente.",
            _ => null
        };
        return new KycVigenciaResponse(situacion, proxima, dias, FechaBloqueoPrevista(proxima, p), permite, mensaje);
    }

    /// <summary>Guardia de apertura de productos: compara fechas en este momento, sin depender del ultimo proceso de fin de dia.</summary>
    public static async Task<ResultadoOperacion?> ValidarAperturaProductoAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor)
    {
        var k = await LeerAsync(c, tx, idInterlocutor);
        if (k is null) return null;
        var hoy = await HoyAsync(c, tx, idCompania);
        var p = await LeerParametrosAsync(c, tx, idCompania);
        return Situacion(k.Value.Proxima, hoy, p) == Vencido
            ? ResultadoOperacion.Fallo(409, "kyc_vencido", $"El KYC del cliente está vencido desde el {k.Value.Proxima:dd/MM/yyyy}; actualícelo antes de abrir productos.")
            : null;
    }

    public static async Task<KycAperturaResponse> ConsultarAperturaAsync(SqlConnection c, long idCompania, long idInterlocutor)
    {
        var error = await ValidarAperturaProductoAsync(c, null, idCompania, idInterlocutor);
        return error is null ? new KycAperturaResponse(true, null, null) : new KycAperturaResponse(false, error.Error, error.Mensaje);
    }

    /// <summary>Clientes por situacion de KYC (insumo del reporte de KYC): en tiempo real, ordenados por proxima revision.</summary>
    public static async Task<IReadOnlyList<KycSituacionItemResponse>> ListarPorSituacionAsync(SqlConnection c, long idCompania, string? situacion)
    {
        var hoy = await HoyAsync(c, null, idCompania);
        var p = await LeerParametrosAsync(c, null, idCompania);
        var filas = await c.QueryAsync(
            """
            SELECT i.id_interlocutor, i.numero, i.tipo_persona, k.fecha_ultima_revision, k.fecha_proxima_revision, es.nombre AS estado_kyc, ri.nombre AS riesgo,
                   COALESCE(j.nombre_org1, LTRIM(ISNULL(f.nombre1, N'') + N' ' + ISNULL(f.apellido1, N''))) AS nombre,
                   CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_BLOQUEO b WHERE b.id_interlocutor = i.id_interlocutor AND b.estado = 'Vigente') THEN 1 ELSE 0 END AS bloqueado
            FROM cli.INTERLOCUTOR i
            JOIN cli.INTERLOCUTOR_KYC k ON k.id_interlocutor = i.id_interlocutor
            LEFT JOIN gen.LISTA_VALORES_ITEM es ON es.id_lista_valores_item = k.id_estado_kyc
            LEFT JOIN gen.LISTA_VALORES_ITEM ri ON ri.id_lista_valores_item = k.id_riesgo
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania AND i.estado = 'Activo'
            ORDER BY k.fecha_proxima_revision, i.numero
            """, new { idCompania });
        var lista = new List<KycSituacionItemResponse>();
        foreach (var x in filas)
        {
            var prox = A((object?)x.fecha_proxima_revision);
            var sit = Situacion(prox, hoy, p);
            if (!string.IsNullOrWhiteSpace(situacion) && !string.Equals(sit, situacion.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            lista.Add(new KycSituacionItemResponse(
                (long)x.id_interlocutor, (string)x.numero, ((string?)x.nombre ?? "").Trim(), (string)x.tipo_persona, (string?)x.estado_kyc, (string?)x.riesgo,
                A((object?)x.fecha_ultima_revision), prox, sit, prox is { } f ? f.DayNumber - hoy.DayNumber : null, (int)x.bloqueado == 1));
        }
        return lista;
    }
}
