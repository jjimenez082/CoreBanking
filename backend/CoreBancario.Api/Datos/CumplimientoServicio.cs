using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// KYC y cumplimiento de personas y organizaciones: expediente KYC (estado, nivel de debida diligencia, riesgo, revisiones y segmentos),
/// motivos de riesgo, referencias de expediente y evaluaciones PEP / sanciones (historial; la vigente es la mas reciente de cada tipo).
/// Todo cambio viaja por una gestion CLI_KYC con aprobacion. Las evaluaciones se registran a mano por ahora; queda el punto de enganche
/// (una evaluacion nueva con su fuente) para que un proveedor externo las genere mas adelante.
/// </summary>
public static class CumplimientoServicio
{
    public const string ListaEstadoKyc = "ESTADO_KYC";
    public const string ListaNivelDd = "NIVEL_DEBIDA_DILIGENCIA";
    public const string ListaRiesgo = "CLASIFICACION_RIESGO";
    public const string ListaMotivoRiesgo = "MOTIVO_RIESGO";
    public const string ListaResultadoPep = "RESULTADO_PEP";
    public const string ListaResultadoSanciones = "RESULTADO_SANCIONES";
    public const string ListaSegmentoCliente = "SEGMENTO_CLIENTE";
    public const string ListaSegmentoPricing = "SEGMENTO_PRICING";
    public const string ListaSegmentoRiesgo = "SEGMENTO_RIESGO";

    public const string TipoPep = "PEP";
    public const string TipoSanciones = "Sanciones";
    public const string EstadoCompleto = "COMPLETO";

    /// <summary>Estado actual del cliente respecto de lo que ya tiene guardado (para no exigir de nuevo valores de lista ya inactivos).</summary>
    public record Vigente(HashSet<long> IdsListaVigentes, string? CodigoPepVigente, string? CodigoSancionesVigente, DateOnly? FechaPepVigente, DateOnly? FechaSancionesVigente);

    public static async Task<Vigente> LeerVigenteAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor)
    {
        var ids = new HashSet<long>();
        var k = await c.QuerySingleOrDefaultAsync(
            "SELECT id_estado_kyc, id_nivel_dd, id_riesgo, id_segmento_cliente, id_segmento_pricing, id_segmento_riesgo FROM cli.INTERLOCUTOR_KYC WHERE id_interlocutor = @idInterlocutor",
            new { idInterlocutor }, tx);
        if (k is not null)
            foreach (var v in new long?[] { k.id_estado_kyc, k.id_nivel_dd, k.id_riesgo, k.id_segmento_cliente, k.id_segmento_pricing, k.id_segmento_riesgo })
                if (v is not null) ids.Add(v.Value);
        foreach (var m in await c.QueryAsync<long>("SELECT id_motivo FROM cli.INTERLOCUTOR_MOTIVO_RIESGO WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo'", new { idInterlocutor }, tx))
            ids.Add(m);

        async Task<(string? Codigo, DateOnly? Fecha)> Ultima(string tipo)
        {
            var f = await c.QuerySingleOrDefaultAsync(
                """
                SELECT TOP 1 it.codigo, s.fecha_screening FROM cli.INTERLOCUTOR_SCREENING s JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = s.id_resultado
                WHERE s.id_interlocutor = @idInterlocutor AND s.tipo = @tipo AND s.estado = 'Activo' ORDER BY s.fecha_screening DESC, s.id_screening DESC
                """, new { idInterlocutor, tipo }, tx);
            return f is null ? (null, null) : ((string)f.codigo, DateOnly.FromDateTime((DateTime)f.fecha_screening));
        }
        var (pep, fp) = await Ultima(TipoPep);
        var (san, fs) = await Ultima(TipoSanciones);
        return new Vigente(ids, pep, san, fp, fs);
    }

    /// <summary>Valida el estado deseado del bloque de cumplimiento. No compara con lo vigente (eso lo hace la gestion).</summary>
    public static async Task<List<string>> ValidarAsync(SqlConnection c, SqlTransaction? tx, CumplimientoRequest req, Vigente vigente)
    {
        var e = new List<string>();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var k = req.Kyc ?? new KycDto(null, null, null, null, null, null, null, null);
        var motivos = req.IdsMotivosRiesgo ?? [];
        var expedientes = req.Expedientes ?? [];
        var evaluaciones = req.NuevasEvaluaciones ?? [];

        // --- Fechas ---
        if (k.FechaUltimaRevision is { } u && (u > hoy || u.Year < 1900)) e.Add("La fecha de la última revisión no es válida (no puede ser futura).");
        if (k.FechaProximaRevision is { } p && p.Year < 1900) e.Add("La fecha de la próxima revisión no es válida.");
        if (k.FechaUltimaRevision is { } u2 && k.FechaProximaRevision is { } p2 && p2 < u2) e.Add("La próxima revisión no puede ser anterior a la última revisión.");

        // --- Motivos de riesgo ---
        if (motivos.Distinct().Count() != motivos.Count) e.Add("Un motivo de riesgo no puede repetirse.");
        if (motivos.Count > 0 && k.IdClasificacionRiesgo is null) e.Add("Para registrar motivos de riesgo indique la clasificación de riesgo.");

        // --- Expedientes ---
        var n = 0;
        foreach (var x in expedientes)
        {
            n++;
            if (string.IsNullOrWhiteSpace(x.Referencia)) e.Add($"Expediente #{n}: la referencia es obligatoria.");
            else if (x.Referencia.Trim().Length > 100) e.Add($"Expediente #{n}: la referencia no puede superar 100 caracteres.");
            if (x.Sistema is { Length: > 100 }) e.Add($"Expediente #{n}: el sistema no puede superar 100 caracteres.");
            if (x.Descripcion is { Length: > 300 }) e.Add($"Expediente #{n}: la descripción no puede superar 300 caracteres.");
        }
        if (expedientes.Where(x => !string.IsNullOrWhiteSpace(x.Referencia)).GroupBy(x => ((x.Referencia).Trim().ToUpperInvariant(), (x.Sistema ?? "").Trim().ToUpperInvariant())).Any(g => g.Count() > 1))
            e.Add("Una misma referencia de expediente no puede repetirse en el mismo sistema.");

        // --- Evaluaciones nuevas ---
        n = 0;
        foreach (var ev in evaluaciones)
        {
            n++;
            if (ev.Tipo is not (TipoPep or TipoSanciones)) e.Add($"Evaluación nueva #{n}: el tipo debe ser PEP o Sanciones.");
            if (ev.Fecha > hoy || ev.Fecha.Year < 1900) e.Add($"Evaluación nueva #{n}: la fecha no es válida (no puede ser futura).");
            if (string.IsNullOrWhiteSpace(ev.Fuente)) e.Add($"Evaluación nueva #{n}: indique la fuente o lista consultada.");
            else if (ev.Fuente.Trim().Length > 200) e.Add($"Evaluación nueva #{n}: la fuente no puede superar 200 caracteres.");
            if (ev.Observacion is { Length: > 500 }) e.Add($"Evaluación nueva #{n}: la observación no puede superar 500 caracteres.");
        }

        // --- Estado COMPLETO ---
        string? codigoEstado = null;
        var requeridos = new List<(long Id, string Lista, string Desc)>();
        void L(long? id, string lista, string desc) { if (id is not null && !vigente.IdsListaVigentes.Contains(id.Value)) requeridos.Add((id.Value, lista, desc)); }
        L(k.IdEstadoKyc, ListaEstadoKyc, "el estado KYC");
        L(k.IdNivelDebidaDiligencia, ListaNivelDd, "el nivel de debida diligencia");
        L(k.IdClasificacionRiesgo, ListaRiesgo, "la clasificación de riesgo");
        L(k.IdSegmentoCliente, ListaSegmentoCliente, "el segmento de cliente");
        L(k.IdSegmentoPricing, ListaSegmentoPricing, "el segmento de pricing");
        L(k.IdSegmentoRiesgo, ListaSegmentoRiesgo, "el segmento de riesgo");
        foreach (var m in motivos) L(m, ListaMotivoRiesgo, "el motivo de riesgo");
        foreach (var ev in evaluaciones)
            L(ev.IdResultado, ev.Tipo == TipoPep ? ListaResultadoPep : ListaResultadoSanciones, ev.Tipo == TipoPep ? "el resultado PEP" : "el resultado de sanciones");

        var codigos = new Dictionary<long, (string Lista, string Codigo)>();
        var todos = new HashSet<long>(requeridos.Select(x => x.Id));
        if (k.IdEstadoKyc is not null) todos.Add(k.IdEstadoKyc.Value);
        foreach (var ev in evaluaciones) todos.Add(ev.IdResultado);
        if (todos.Count > 0)
            foreach (var f in await c.QueryAsync(
                """
                SELECT i.id_lista_valores_item AS id, t.codigo AS lista, i.codigo, i.estado
                FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE i.id_lista_valores_item IN @ids
                """, new { ids = todos.ToList() }, tx))
                codigos[(long)f.id] = ((string)f.lista, (string)f.codigo + ((string)f.estado == "Activo" ? "" : "\u0001inactivo"));

        foreach (var (id, lista, desc) in requeridos.Distinct())
            if (!codigos.TryGetValue(id, out var v) || v.Lista != lista || v.Codigo.EndsWith("\u0001inactivo", StringComparison.Ordinal)) e.Add($"Valor no válido para {desc}.");
        if (k.IdEstadoKyc is { } ek && codigos.TryGetValue(ek, out var ce) && ce.Lista == ListaEstadoKyc) codigoEstado = ce.Codigo.Replace("\u0001inactivo", "");

        if (codigoEstado == EstadoCompleto)
        {
            if (k.IdNivelDebidaDiligencia is null) e.Add("Para dejar el KYC completo indique el nivel de debida diligencia.");
            if (k.IdClasificacionRiesgo is null) e.Add("Para dejar el KYC completo indique la clasificación de riesgo.");
            if (k.FechaUltimaRevision is null) e.Add("Para dejar el KYC completo indique la fecha de la última revisión.");
            if (k.FechaProximaRevision is null) e.Add("Para dejar el KYC completo indique la fecha de la próxima revisión.");

            // La evaluacion vigente despues de esta solicitud: la nueva mas reciente, o la ya registrada si es igual o mas reciente.
            string? Resultado(string tipo, string? codigoActual, DateOnly? fechaActual)
            {
                var nueva = evaluaciones.Where(x => x.Tipo == tipo && codigos.ContainsKey(x.IdResultado)).OrderBy(x => x.Fecha).LastOrDefault();
                if (nueva is null) return codigoActual;
                return fechaActual is { } fa && fa > nueva.Fecha ? codigoActual : codigos[nueva.IdResultado].Codigo.Replace("\u0001inactivo", "");
            }
            var pep = Resultado(TipoPep, vigente.CodigoPepVigente, vigente.FechaPepVigente);
            var san = Resultado(TipoSanciones, vigente.CodigoSancionesVigente, vigente.FechaSancionesVigente);
            if (pep == "EN_REVISION") e.Add("No puede dejar el KYC completo mientras la evaluación PEP esté en revisión.");
            if (san == "EN_REVISION") e.Add("No puede dejar el KYC completo mientras la evaluación de sanciones esté en revisión.");
            if (san == "COINCIDENCIA_CONFIRMADA") e.Add("No puede dejar el KYC completo con una coincidencia confirmada en sanciones.");
        }
        return e;
    }

    /// <summary>Requisitos KYC_* de los roles (se verifican al asumir un rol sobre un cliente ya registrado).</summary>
    public static async Task<List<string>> FaltantesAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor, ReglasCampos r)
    {
        var faltan = new List<string>();
        if (r.Pide(ReglasCampos.KycEstadoCompleto))
        {
            var ok = await c.ExecuteScalarAsync<bool>(
                """
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM cli.INTERLOCUTOR_KYC k JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = k.id_estado_kyc
                    WHERE k.id_interlocutor = @idInterlocutor AND it.codigo = N'COMPLETO'
                      AND (k.fecha_proxima_revision IS NULL OR k.fecha_proxima_revision >= CAST(SYSUTCDATETIME() AS date))) THEN 1 ELSE 0 END
                """, new { idInterlocutor }, tx);
            if (!ok) faltan.Add("el KYC completo y vigente (Cumplimiento)");
        }
        if (r.Pide(ReglasCampos.KycPepEvaluado) || r.Pide(ReglasCampos.KycSancionesEvaluado))
        {
            var v = await LeerVigenteAsync(c, tx, idInterlocutor);
            if (r.Pide(ReglasCampos.KycPepEvaluado) && (v.CodigoPepVigente is null || v.CodigoPepVigente == "EN_REVISION")) faltan.Add("una evaluación PEP resuelta (Cumplimiento)");
            if (r.Pide(ReglasCampos.KycSancionesEvaluado) && (v.CodigoSancionesVigente is null || v.CodigoSancionesVigente is "EN_REVISION" or "COINCIDENCIA_CONFIRMADA"))
                faltan.Add("una evaluación de sanciones resuelta y sin coincidencia confirmada (Cumplimiento)");
        }
        return faltan;
    }

    public static async Task<CumplimientoResponse?> ObtenerAsync(SqlConnection c, long idCompania, long idInterlocutor)
    {
        var tipo = await c.ExecuteScalarAsync<string?>(
            "SELECT tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania", new { idInterlocutor, idCompania });
        if (tipo is null) return null;
        var p = new { idInterlocutor };
        static DateOnly? A(object? o) => o is DateTime d ? DateOnly.FromDateTime(d) : null;

        var k = await c.QuerySingleOrDefaultAsync(
            """
            SELECT x.id_estado_kyc, es.nombre AS estado, es.codigo AS cod_estado, x.id_nivel_dd, nd.nombre AS nivel, x.id_riesgo, ri.nombre AS riesgo, ri.codigo AS cod_riesgo,
                   x.fecha_ultima_revision, x.fecha_proxima_revision, x.id_segmento_cliente, sc.nombre AS seg_cliente,
                   x.id_segmento_pricing, sp.nombre AS seg_pricing, x.id_segmento_riesgo, sr.nombre AS seg_riesgo
            FROM cli.INTERLOCUTOR_KYC x
            LEFT JOIN gen.LISTA_VALORES_ITEM es ON es.id_lista_valores_item = x.id_estado_kyc
            LEFT JOIN gen.LISTA_VALORES_ITEM nd ON nd.id_lista_valores_item = x.id_nivel_dd
            LEFT JOIN gen.LISTA_VALORES_ITEM ri ON ri.id_lista_valores_item = x.id_riesgo
            LEFT JOIN gen.LISTA_VALORES_ITEM sc ON sc.id_lista_valores_item = x.id_segmento_cliente
            LEFT JOIN gen.LISTA_VALORES_ITEM sp ON sp.id_lista_valores_item = x.id_segmento_pricing
            LEFT JOIN gen.LISTA_VALORES_ITEM sr ON sr.id_lista_valores_item = x.id_segmento_riesgo
            WHERE x.id_interlocutor = @idInterlocutor
            """, p);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        KycResponse? kyc = null;
        if (k is not null)
        {
            var prox = A((object?)k.fecha_proxima_revision);
            kyc = new KycResponse(
                (long?)k.id_estado_kyc, (string?)k.estado, (string?)k.cod_estado, (long?)k.id_nivel_dd, (string?)k.nivel,
                (long?)k.id_riesgo, (string?)k.riesgo, (string?)k.cod_riesgo,
                A((object?)k.fecha_ultima_revision), prox, prox is { } pr && pr < hoy,
                (long?)k.id_segmento_cliente, (string?)k.seg_cliente, (long?)k.id_segmento_pricing, (string?)k.seg_pricing, (long?)k.id_segmento_riesgo, (string?)k.seg_riesgo);
        }

        var motivos = (await c.QueryAsync(
            """
            SELECT m.id_motivo_riesgo AS id, m.id_motivo, it.nombre FROM cli.INTERLOCUTOR_MOTIVO_RIESGO m
            JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = m.id_motivo
            WHERE m.id_interlocutor = @idInterlocutor AND m.estado = 'Activo' ORDER BY it.orden, it.nombre
            """, p)).Select(x => new MotivoRiesgoResponse((long)x.id, (long)x.id_motivo, (string)x.nombre)).ToList();

        var expedientes = (await c.QueryAsync(
            "SELECT id_expediente AS id, referencia, sistema, descripcion FROM cli.INTERLOCUTOR_EXPEDIENTE WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo' ORDER BY id_expediente", p))
            .Select(x => new ExpedienteResponse((long)x.id, (string)x.referencia, (string?)x.sistema, (string?)x.descripcion)).ToList();

        var filas = (await c.QueryAsync(
            """
            SELECT s.id_screening AS id, s.tipo, s.id_resultado, it.nombre AS resultado, it.codigo AS cod_resultado, s.fecha_screening, s.fuente, s.observacion, s.creado_por, s.creado_en
            FROM cli.INTERLOCUTOR_SCREENING s JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = s.id_resultado
            WHERE s.id_interlocutor = @idInterlocutor AND s.estado = 'Activo' ORDER BY s.fecha_screening DESC, s.id_screening DESC
            """, p)).ToList();
        var vigentes = filas.GroupBy(x => (string)x.tipo).Select(g => (long)g.First().id).ToHashSet();   // ya vienen ordenadas: la primera de cada tipo es la vigente
        var evaluaciones = filas.Select(x => new EvaluacionResponse(
            (long)x.id, (string)x.tipo, (long)x.id_resultado, (string)x.resultado, (string)x.cod_resultado, DateOnly.FromDateTime((DateTime)x.fecha_screening),
            (string)x.fuente, (string?)x.observacion, vigentes.Contains((long)x.id), (string)x.creado_por, (DateTime)x.creado_en)).ToList();

        var vigencia = await KycVigenciaServicio.ObtenerAsync(c, idCompania, idInterlocutor);
        return new CumplimientoResponse(idInterlocutor, tipo, kyc, motivos, expedientes, evaluaciones, vigencia);
    }
}
