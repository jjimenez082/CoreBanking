using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Compilacion de una version, evaluacion (prueba o real), bitacora y casos de prueba.</summary>
public partial class ReglasRepositorio
{
    private static readonly JsonSerializerOptions OpcionesJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
    private const int MaxEntradaJson = 8000;

    // ------------------------------------------------------------------ datos de entrada
    /// <summary>Convierte el JSON de entrada en valores simples (texto, numero, booleano, lista). Devuelve error si hay objetos anidados.</summary>
    internal static (Dictionary<string, object?>? Datos, string? Error) ConvertirDatos(IReadOnlyDictionary<string, JsonElement>? entrada)
    {
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in entrada ?? new Dictionary<string, JsonElement>())
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined: d[k] = null; break;
                case JsonValueKind.String: d[k] = v.GetString(); break;
                case JsonValueKind.Number: d[k] = v.GetDecimal(); break;
                case JsonValueKind.True: d[k] = true; break;
                case JsonValueKind.False: d[k] = false; break;
                case JsonValueKind.Array:
                    d[k] = v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString()).ToList();
                    break;
                default: return (null, $"El dato '{k}' debe ser un valor simple o una lista.");
            }
        }
        return (d, null);
    }

    // ------------------------------------------------------------------ compilacion
    private sealed record VersionCompilada(long IdVersion, int Numero, long IdConjunto, string ConjuntoCodigo, string Politica, string Estado, IReadOnlyList<ReglaEval> Reglas);

    private static async Task<VersionCompilada?> CompilarAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idVersion)
    {
        var v = await c.QuerySingleOrDefaultAsync(
            "SELECT v.id_version, v.numero_version, v.estado, j.id_conjunto, j.codigo, j.politica_resolucion FROM rul.CONJUNTO_REGLAS_VERSION v JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto WHERE v.id_version = @idVersion AND j.id_compania = @idCompania",
            new { idVersion, idCompania }, tx);
        if (v is null) return null;
        var reglas = (await c.QueryAsync("SELECT * FROM rul.REGLA WHERE id_version = @idVersion ORDER BY orden", new { idVersion }, tx)).ToList();
        var grupos = (await c.QueryAsync("SELECT g.* FROM rul.GRUPO_CONDICION g JOIN rul.REGLA r ON r.id_regla = g.id_regla WHERE r.id_version = @idVersion ORDER BY g.orden", new { idVersion }, tx)).ToList();
        var miembros = (await c.QueryAsync(
            """
            SELECT m.id_grupo, m.negar, m.orden AS orden_m, c.codigo, a.codigo AS attr, a.tipo_dato, a.es_multivalor, f.codigo AS fn, c.parametro_funcion, o.codigo AS op, o.aridad,
                   c.origen_valor, c.valor_1, c.valor_2, c.valores_lista, av.codigo AS av_codigo, av.tipo_dato AS av_tipo
            FROM rul.GRUPO_CONDICION_MIEMBRO m JOIN rul.GRUPO_CONDICION g ON g.id_grupo = m.id_grupo JOIN rul.REGLA r ON r.id_regla = g.id_regla
            JOIN rul.CONDICION c ON c.id_condicion = m.id_condicion JOIN rul.ATRIBUTO a ON a.id_atributo = c.id_atributo LEFT JOIN rul.FUNCION f ON f.id_funcion = c.id_funcion
            JOIN rul.OPERADOR o ON o.id_operador = c.id_operador LEFT JOIN rul.ATRIBUTO av ON av.id_atributo = c.id_atributo_valor
            WHERE r.id_version = @idVersion ORDER BY m.orden, m.id_miembro
            """, new { idVersion }, tx)).ToList();
        var acciones = (await c.QueryAsync(
            "SELECT ra.id_regla, a.* FROM rul.REGLA_ACCION ra JOIN rul.REGLA r ON r.id_regla = ra.id_regla JOIN rul.ACCION a ON a.id_accion = ra.id_accion WHERE r.id_version = @idVersion ORDER BY ra.orden",
            new { idVersion }, tx)).ToList();

        GrupoEval Construir(dynamic g)
        {
            long idg = g.id_grupo;
            var ms = miembros.Where(m => (long)m.id_grupo == idg).Select(m => new MiembroEval(
                new CondicionEval((string)m.codigo, (string)m.attr, (string)m.tipo_dato, (bool)m.es_multivalor, (string?)m.fn, (string?)m.parametro_funcion, (string)m.op, (int)(byte)m.aridad,
                    (string)m.origen_valor, (string?)m.valor_1, (string?)m.valor_2, (string?)m.valores_lista, (string?)m.av_codigo, (string?)m.av_tipo), (bool)m.negar)).ToList();
            var subs = grupos.Where(x => (long?)x.id_grupo_padre == idg).Select(x => (GrupoEval)Construir(x)).ToList();
            return new GrupoEval((string)g.operador_logico, ms, subs);
        }
        var lista = reglas.Select<dynamic, ReglaEval>(r =>
        {
            long idr = r.id_regla;
            var raiz = grupos.FirstOrDefault(g => (long)g.id_regla == idr && g.id_grupo_padre is null);
            GrupoEval? arbol = raiz is null ? null : (GrupoEval)Construir(raiz);
            var acc = acciones.Where(a => (long)a.id_regla == idr).Select(a => new AccionEval((string)a.codigo, (string)a.tipo_accion, (string?)a.resultado, (string?)a.clave, (string?)a.valor, (string?)a.mensaje)).ToList();
            return new ReglaEval((string)r.codigo, (string)r.nombre, (int)r.orden, (bool)r.activa, arbol, acc);
        }).ToList();
        return new VersionCompilada(idVersion, (int)v.numero_version, (long)v.id_conjunto, (string)v.codigo, (string)v.politica_resolucion, (string)v.estado, lista);
    }

    /// <summary>Version a usar: la indicada, o la Vigente del conjunto (tras promover las programadas).</summary>
    private async Task<(VersionCompilada? Version, ResultadoOperacion? Error)> ResolverVersionAsync(SqlConnection c, SqlTransaction? tx, ActorContext actor, long idConjunto, long? idVersion)
    {
        var existe = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM rul.CONJUNTO_REGLAS WHERE id_conjunto = @i AND id_compania = @co", new { i = idConjunto, co = actor.IdCompania }, tx);
        if (existe == 0) return (null, ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el conjunto de reglas."));
        long? id = idVersion;
        if (id is null)
        {
            await PromoverProgramadasAsync(c, tx, actor.IdCompania, idConjunto);
            id = await c.ExecuteScalarAsync<long?>("SELECT id_version FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @i AND estado = 'Vigente'", new { i = idConjunto }, tx);
            if (id is null) return (null, ResultadoOperacion.Fallo(409, "sin_version_vigente", "El conjunto no tiene una version vigente."));
        }
        var v = await CompilarAsync(c, tx, actor.IdCompania, id.Value);
        if (v is null || v.IdConjunto != idConjunto) return (null, ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version."));
        return (v, null);
    }

    // ------------------------------------------------------------------ evaluacion
    /// <summary>Simulador: evalua con los datos indicados y NO guarda la evaluacion.</summary>
    public async Task<(ReglaEvaluacionResponse? Respuesta, ResultadoOperacion? Error)> ProbarAsync(ActorContext actor, long idConjunto, EvaluarRequest req)
    {
        var (datos, errDatos) = ConvertirDatos(req.Datos);
        if (errDatos is not null) return (null, ResultadoOperacion.Fallo(400, "datos_invalidos", errDatos));
        using var c = NuevaConexion(); c.Open();
        var (v, err) = await ResolverVersionAsync(c, null, actor, idConjunto, req.IdVersion);
        if (err is not null) return (null, err);
        var res = ReglasEvaluador.Evaluar(v!.Reglas, v.Politica, datos!, req.FechaProceso ?? DateOnly.FromDateTime(DateTime.Now));
        return (new ReglaEvaluacionResponse(null, v.IdVersion, v.Numero, "Prueba", res), null);
    }

    /// <summary>Evaluacion real para uso de los demas dominios: usa la version vigente y guarda entrada, resultado y traza.</summary>
    public async Task<(ReglaEvaluacionResponse? Respuesta, ResultadoOperacion? Error)> EvaluarRealAsync(ActorContext actor, string codigoConjunto, IReadOnlyDictionary<string, object?> datos, string? referencia, DateOnly? fechaProceso = null)
    {
        using var c = NuevaConexion(); c.Open();
        var idConjunto = await c.ExecuteScalarAsync<long?>("SELECT id_conjunto FROM rul.CONJUNTO_REGLAS WHERE codigo = @cod AND id_compania = @co AND estado = 'Activo'", new { cod = codigoConjunto, co = actor.IdCompania });
        if (idConjunto is null) return (null, ResultadoOperacion.Fallo(404, "no_encontrado", $"No existe el conjunto de reglas activo '{codigoConjunto}'."));
        var (v, err) = await ResolverVersionAsync(c, null, actor, idConjunto.Value, null);
        if (err is not null) return (null, err);
        var res = ReglasEvaluador.Evaluar(v!.Reglas, v.Politica, datos, fechaProceso ?? DateOnly.FromDateTime(DateTime.Now));
        var idEval = await c.QuerySingleAsync<long>(
            """
            INSERT INTO rul.EVALUACION (id_version, id_regla_aplicada, modo, referencia, entrada, resultado, traza, evaluado_por)
            OUTPUT INSERTED.id_evaluacion
            VALUES (@v, (SELECT TOP 1 r.id_regla FROM rul.REGLA r WHERE r.id_version = @v AND r.codigo = @regla), 'Real', @ref, @entrada, @resultado, @traza, @por)
            """,
            new { v = v.IdVersion, regla = res.ReglasAplicadas.FirstOrDefault(), @ref = Nulo(referencia), entrada = JsonSerializer.Serialize(datos, OpcionesJson),
                  resultado = JsonSerializer.Serialize(new { res.Decision, res.Asignaciones, res.Mensajes, res.ReglasAplicadas }, OpcionesJson), traza = JsonSerializer.Serialize(res.Traza, OpcionesJson), por = actor.NombreDescriptivo });
        return (new ReglaEvaluacionResponse(idEval, v.IdVersion, v.Numero, "Real", res), null);
    }

    public async Task<IReadOnlyList<EvaluacionFilaResponse>> ListarEvaluacionesAsync(ActorContext actor, long? idConjunto, string? referencia)
    {
        using var c = NuevaConexion(); c.Open();
        var f = await c.QueryAsync(
            """
            SELECT TOP 200 e.id_evaluacion, e.id_version, e.modo, e.referencia, e.resultado, e.evaluado_por, e.evaluado_en, v.numero_version, j.codigo, r.codigo AS regla
            FROM rul.EVALUACION e JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = e.id_version JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto
            LEFT JOIN rul.REGLA r ON r.id_regla = e.id_regla_aplicada
            WHERE j.id_compania = @co AND (@idConjunto IS NULL OR j.id_conjunto = @idConjunto) AND (@referencia IS NULL OR e.referencia = @referencia)
            ORDER BY e.id_evaluacion DESC
            """, new { co = actor.IdCompania, idConjunto, referencia = Nulo(referencia) });
        return f.Select(MapEvaluacionFila).Cast<EvaluacionFilaResponse>().ToList();
    }

    private static EvaluacionFilaResponse MapEvaluacionFila(dynamic e) => new((long)e.id_evaluacion, (long)e.id_version, (string)e.codigo, (int)e.numero_version, (string)e.modo, (string?)e.referencia,
        DecisionDe((string)e.resultado), (string?)e.regla, (string)e.evaluado_por, (DateTime)e.evaluado_en);

    private static string? DecisionDe(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("decision", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null; }
        catch (JsonException) { return null; }
    }

    public async Task<EvaluacionDetalleResponse?> ObtenerEvaluacionAsync(ActorContext actor, long idEvaluacion)
    {
        using var c = NuevaConexion(); c.Open();
        var e = await c.QuerySingleOrDefaultAsync(
            """
            SELECT e.*, v.numero_version, j.codigo, r.codigo AS regla FROM rul.EVALUACION e JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = e.id_version
            JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto LEFT JOIN rul.REGLA r ON r.id_regla = e.id_regla_aplicada WHERE e.id_evaluacion = @idEvaluacion AND j.id_compania = @co
            """, new { idEvaluacion, co = actor.IdCompania });
        if (e is null) return null;
        return new EvaluacionDetalleResponse(MapEvaluacionFila(e), (string)e.entrada, (string)e.resultado, (string?)e.traza);
    }

    // =============================================================== Casos de prueba
    public async Task<IReadOnlyList<CasoPruebaResponse>> ListarCasosAsync(ActorContext actor, long idConjunto)
    {
        using var c = NuevaConexion(); c.Open();
        var f = await c.QueryAsync("SELECT k.* FROM rul.CASO_PRUEBA k JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = k.id_conjunto WHERE k.id_conjunto = @idConjunto AND j.id_compania = @co ORDER BY k.nombre", new { idConjunto, co = actor.IdCompania });
        return f.Select(k => new CasoPruebaResponse((long)k.id_caso, (long)k.id_conjunto, (string)k.nombre, (string)k.entrada, (string?)k.decision_esperada, (string?)k.regla_esperada, (string)k.estado)).ToList();
    }

    private static string? ValidarCaso(CasoPruebaGuardarRequest r, out string entradaJson)
    {
        entradaJson = "";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        if (r.DecisionEsperada is not null && !Decisiones.Contains(r.DecisionEsperada)) return "La decision esperada debe ser Aprobar, Rechazar, Referir o vacia.";
        var (_, err) = ConvertirDatos(r.Entrada);
        if (err is not null) return err;
        entradaJson = JsonSerializer.Serialize(r.Entrada ?? new Dictionary<string, JsonElement>(), OpcionesJson);
        return entradaJson.Length > MaxEntradaJson ? "Los datos del caso son demasiado largos." : null;
    }

    public async Task<ResultadoOperacion> GuardarCasoAsync(ActorContext actor, long idConjunto, long? idCaso, CasoPruebaGuardarRequest r)
    {
        var e = ValidarCaso(r, out var json);
        if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", e);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM rul.CONJUNTO_REGLAS WHERE id_conjunto = @i AND id_compania = @co", new { i = idConjunto, co = actor.IdCompania }, tx) == 0)
            return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el conjunto de reglas.");
        if (idCaso is null)
        {
            var nuevo = await c.QuerySingleAsync<long>(
                "INSERT INTO rul.CASO_PRUEBA (id_conjunto, nombre, entrada, decision_esperada, regla_esperada, creado_por) OUTPUT INSERTED.id_caso VALUES (@j, @n, @e, @d, @r, @por)",
                new { j = idConjunto, n = r.Nombre.Trim(), e = json, d = r.DecisionEsperada, r = Nulo(r.ReglaEsperada), por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadCaso, nuevo, r.Nombre.Trim());
            tx.Commit();
            return ResultadoOperacion.Ok(nuevo);
        }
        var n = await c.ExecuteAsync(
            "UPDATE rul.CASO_PRUEBA SET nombre = @n, entrada = @e, decision_esperada = @d, regla_esperada = @r, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_caso = @id AND id_conjunto = @j",
            new { n = r.Nombre.Trim(), e = json, d = r.DecisionEsperada, r = Nulo(r.ReglaEsperada), por = actor.NombreDescriptivo, id = idCaso, j = idConjunto }, tx);
        if (n == 0) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el caso de prueba.");
        await Auditoria.CambioAsync(c, tx, actor, EntidadCaso, idCaso.Value, "caso", null, r.Nombre.Trim());
        tx.Commit();
        return ResultadoOperacion.Ok(idCaso);
    }

    public async Task<ResultadoOperacion> CambiarEstadoCasoAsync(ActorContext actor, long idConjunto, long idCaso, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT k.estado FROM rul.CASO_PRUEBA k JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = k.id_conjunto WHERE k.id_caso = @idCaso AND k.id_conjunto = @idConjunto AND j.id_compania = @co", new { idCaso, idConjunto, co = actor.IdCompania }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el caso de prueba.");
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE rul.CASO_PRUEBA SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_caso = @idCaso", new { e = estado, p = actor.NombreDescriptivo, idCaso }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadCaso, idCaso, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(idCaso);
    }

    /// <summary>Corre los casos activos contra una version (por defecto la que esta en edicion, o la vigente). Informa; no bloquea la publicacion.</summary>
    public async Task<(CasosEjecutarResponse? Respuesta, ResultadoOperacion? Error)> EjecutarCasosAsync(ActorContext actor, long idConjunto, long? idVersion)
    {
        using var c = NuevaConexion(); c.Open();
        if (idVersion is null)
        {
            await PromoverProgramadasAsync(c, null, actor.IdCompania, idConjunto);
            idVersion = await c.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id_version FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @i AND estado IN ('Borrador', 'Revision', 'Vigente') ORDER BY CASE estado WHEN 'Borrador' THEN 0 WHEN 'Revision' THEN 1 ELSE 2 END", new { i = idConjunto });
            if (idVersion is null) return (null, ResultadoOperacion.Fallo(409, "sin_version", "El conjunto aun no tiene versiones."));
        }
        var (v, err) = await ResolverVersionAsync(c, null, actor, idConjunto, idVersion);
        if (err is not null) return (null, err);
        var casos = (await c.QueryAsync("SELECT * FROM rul.CASO_PRUEBA WHERE id_conjunto = @idConjunto AND estado = 'Activo' ORDER BY nombre", new { idConjunto })).ToList();
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        var salida = new List<CasoPruebaResultado>();
        foreach (var k in casos)
        {
            var entrada = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>((string)k.entrada) ?? [];
            var (datos, e) = ConvertirDatos(entrada);
            string? esperadaD = (string?)k.decision_esperada, esperadaR = (string?)k.regla_esperada;
            if (e is not null) { salida.Add(new CasoPruebaResultado((long)k.id_caso, (string)k.nombre, false, null, null, esperadaD, esperadaR, e)); continue; }
            var res = ReglasEvaluador.Evaluar(v!.Reglas, v.Politica, datos!, hoy);
            var regla = res.ReglasAplicadas.FirstOrDefault();
            var problemas = new List<string>();
            if (res.Decision != esperadaD) problemas.Add($"Se esperaba decision '{esperadaD ?? "ninguna"}' y se obtuvo '{res.Decision ?? "ninguna"}'.");
            if (esperadaR is not null && !string.Equals(regla, esperadaR, StringComparison.OrdinalIgnoreCase)) problemas.Add($"Se esperaba la regla '{esperadaR}' y aplico '{regla ?? "ninguna"}'.");
            salida.Add(new CasoPruebaResultado((long)k.id_caso, (string)k.nombre, problemas.Count == 0, res.Decision, regla, esperadaD, esperadaR, problemas.Count == 0 ? null : string.Join(" ", problemas)));
        }
        return (new CasosEjecutarResponse(v!.IdVersion, v.Numero, salida.Count, salida.Count(x => !x.Paso), salida), null);
    }
}
