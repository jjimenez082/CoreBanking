using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Motor de reglas (GEN-008, ronda 43): catalogos, pool de condiciones y acciones, conjuntos de reglas versionados, evaluacion y casos de prueba.
/// El mantenimiento es directo con auditoria; publicar una version pasa por la gestion RUL_VERSION_CONJUNTO.
/// </summary>
public partial class ReglasRepositorio
{
    public const string PermisoConsulta = "RUL_CONSULTA", PermisoAdmin = "RUL_ADMIN", PermisoPublicar = "RUL_PUBLICAR", PermisoEvaluar = "RUL_EVALUAR";
    public const string TipoGestionVersion = "RUL_VERSION_CONJUNTO";
    public const string EntidadConjunto = "CONJUNTO_REGLAS", EntidadVersion = "CONJUNTO_VERSION", EntidadCondicion = "REGLA_CONDICION", EntidadAccion = "REGLA_ACCION", EntidadCaso = "REGLA_CASO";

    private static readonly string[] Decisiones = ["Aprobar", "Rechazar", "Referir"];
    private static readonly string[] Politicas = ["PrimeraCoincidencia", "AcumularTodas"];
    private static readonly string[] OperadoresMultivalor = ["IGUAL", "DISTINTO", "TIENE", "CONTIENE", "EN", "NO_EN", "ES_VACIO", "NO_ES_VACIO"];

    private readonly TenantContext _tenant;
    private readonly NumeracionServicio _numeracion;
    public ReglasRepositorio(TenantContext tenant, NumeracionServicio numeracion) { _tenant = tenant; _numeracion = numeracion; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);
    private static bool Duplicado(SqlException ex) => ex.Number is 2601 or 2627;
    private static string? Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? ValidarCodigoNombre(string? codigo, string? nombre, int maxCodigo = 50)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Trim().Length > maxCodigo || codigo.Trim().Any(char.IsWhiteSpace)) return $"El codigo es obligatorio, sin espacios (maximo {maxCodigo} caracteres).";
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        return null;
    }

    // =============================================================== Catalogos
    public async Task<ReglaCatalogosResponse> CatalogosAsync(string? contexto)
    {
        using var c = NuevaConexion(); c.Open();
        var ctx = (await c.QueryAsync("SELECT * FROM rul.CONTEXTO WHERE estado <> 'Inactivo' ORDER BY id_contexto"))
            .Select(x => new ReglaContextoResponse((long)x.id_contexto, (string)x.codigo, (string)x.nombre, (string)x.estado)).ToList();
        var atr = (await c.QueryAsync(
            """
            SELECT DISTINCT a.* FROM rul.ATRIBUTO a
            LEFT JOIN rul.CONTEXTO_ATRIBUTO ca ON ca.id_atributo = a.id_atributo LEFT JOIN rul.CONTEXTO x ON x.id_contexto = ca.id_contexto
            WHERE a.estado = 'Activo' AND (@contexto IS NULL OR x.codigo = @contexto) ORDER BY a.codigo
            """, new { contexto }))
            .Select(a => new ReglaAtributoResponse((long)a.id_atributo, (string)a.codigo, (string)a.nombre, (string?)a.descripcion, (string)a.tipo_dato, (string?)a.lista_codigo, (bool)a.es_multivalor)).ToList();
        var ope = (await c.QueryAsync("SELECT * FROM rul.OPERADOR ORDER BY orden"))
            .Select(o => new ReglaOperadorResponse((long)o.id_operador, (string)o.codigo, (string)o.nombre, (string?)o.simbolo, (int)(byte)o.aridad, ((string)o.tipos_dato).Split(',').ToList())).ToList();
        var fun = (await c.QueryAsync("SELECT * FROM rul.FUNCION WHERE estado = 'Activo' ORDER BY nombre"))
            .Select(f => new ReglaFuncionResponse((long)f.id_funcion, (string)f.codigo, (string)f.nombre, (string?)f.descripcion, (string)f.tipo_entrada, (string)f.tipo_retorno, (int)(byte)f.cantidad_parametros)).ToList();
        return new ReglaCatalogosResponse(ctx, atr, ope, fun);
    }

    // =============================================================== Condiciones (pool)
    private const string SqlCondicionDe =
        """
        FROM rul.CONDICION c
        JOIN rul.ATRIBUTO a ON a.id_atributo = c.id_atributo
        LEFT JOIN rul.FUNCION f ON f.id_funcion = c.id_funcion
        JOIN rul.OPERADOR o ON o.id_operador = c.id_operador
        LEFT JOIN rul.ATRIBUTO av ON av.id_atributo = c.id_atributo_valor
        """;
    private const string SqlCondicionColumnas =
        "c.*, a.codigo AS attr_codigo, a.nombre AS attr_nombre, a.tipo_dato, f.codigo AS fn_codigo, f.nombre AS fn_nombre, o.codigo AS op_codigo, o.nombre AS op_nombre, o.simbolo, av.codigo AS av_codigo, av.nombre AS av_nombre";
    private const string SqlUsosCondicion =
        """
        (SELECT COUNT(DISTINCT r.id_version) FROM rul.GRUPO_CONDICION_MIEMBRO m JOIN rul.GRUPO_CONDICION g ON g.id_grupo = m.id_grupo
           JOIN rul.REGLA r ON r.id_regla = g.id_regla WHERE m.id_condicion = c.id_condicion) AS usos,
        (SELECT COUNT(DISTINCT r.id_version) FROM rul.GRUPO_CONDICION_MIEMBRO m JOIN rul.GRUPO_CONDICION g ON g.id_grupo = m.id_grupo
           JOIN rul.REGLA r ON r.id_regla = g.id_regla JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = r.id_version WHERE m.id_condicion = c.id_condicion AND v.estado <> 'Borrador') AS congeladas
        """;

    /// <summary>Texto legible de una condicion: "Edad (anios) >= 18".</summary>
    internal static string ResumenCondicion(dynamic c)
    {
        var izq = (string)c.attr_nombre;
        if (c.fn_nombre is not null) izq = $"{(string)c.fn_nombre} de {izq}";
        var op = (string?)c.simbolo ?? (string)c.op_nombre;
        var cod = (string)c.op_codigo;
        if (cod is "ES_VACIO" or "NO_ES_VACIO") return $"{izq} {(string)c.op_nombre}";
        string der;
        if ((string)c.origen_valor == "Atributo") der = (string)c.av_nombre;
        else if (cod is "EN" or "NO_EN") der = "[" + ((string?)c.valores_lista ?? "").Replace("|", ", ") + "]";
        else if (cod == "ENTRE") der = $"{(string?)c.valor_1} y {(string?)c.valor_2}";
        else der = (string?)c.valor_1 ?? "";
        return $"{izq} {op} {der}".Trim();
    }

    private static ReglaCondicionResponse MapCondicion(dynamic c) => new((long)c.id_condicion, (string)c.codigo, (string)c.nombre, (string?)c.descripcion, (long)c.id_atributo, (string)c.attr_codigo, (string)c.attr_nombre,
        (long?)c.id_funcion, (string?)c.fn_codigo, (string?)c.parametro_funcion, (long)c.id_operador, (string)c.op_codigo, (string)c.origen_valor, (string?)c.valor_1, (string?)c.valor_2,
        (string?)c.valores_lista, (long?)c.id_atributo_valor, (string?)c.av_codigo, (string)c.estado, ResumenCondicion(c), (int)c.usos, (int)c.congeladas > 0);

    public async Task<IReadOnlyList<ReglaCondicionResponse>> ListarCondicionesAsync(ActorContext actor)
    {
        using var c = NuevaConexion(); c.Open();
        var f = await c.QueryAsync($"SELECT {SqlCondicionColumnas}, {SqlUsosCondicion} {SqlCondicionDe} WHERE c.id_compania = @co ORDER BY c.codigo", new { co = actor.IdCompania });
        return f.Select(MapCondicion).Cast<ReglaCondicionResponse>().ToList();
    }

    private async Task<(string? Error, string? V1, string? V2, string? Lista)> ValidarCondicionAsync(SqlConnection c, SqlTransaction tx, CondicionGuardarRequest r)
    {
        var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM rul.ATRIBUTO WHERE id_atributo = @i AND estado = 'Activo'", new { i = r.IdAtributo }, tx);
        if (a is null) return ("El atributo no existe o esta inactivo.", null, null, null);
        var o = await c.QuerySingleOrDefaultAsync("SELECT * FROM rul.OPERADOR WHERE id_operador = @i", new { i = r.IdOperador }, tx);
        if (o is null) return ("El operador no existe.", null, null, null);
        var tipo = (string)a.tipo_dato; var multi = (bool)a.es_multivalor; var aridad = (int)(byte)o.aridad; var op = (string)o.codigo;

        if (r.IdFuncion is not null)
        {
            var f = await c.QuerySingleOrDefaultAsync("SELECT * FROM rul.FUNCION WHERE id_funcion = @i AND estado = 'Activo'", new { i = r.IdFuncion }, tx);
            if (f is null) return ("La funcion no existe o esta inactiva.", null, null, null);
            var fc = (string)f.codigo;
            if (multi && fc != "CANTIDAD") return ($"A un atributo multivalor solo se le puede aplicar la funcion Cantidad de valores.", null, null, null);
            if (!multi && (string)f.tipo_entrada != (tipo == "Lista" ? "Texto" : tipo)) return ($"La funcion '{(string)f.nombre}' aplica a atributos de tipo {(string)f.tipo_entrada}.", null, null, null);
            var np = (int)(byte)f.cantidad_parametros;
            if (np > 0 && !decimal.TryParse(r.ParametroFuncion, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return ($"La funcion '{(string)f.nombre}' necesita un parametro numerico.", null, null, null);
            if (np == 0 && !string.IsNullOrWhiteSpace(r.ParametroFuncion)) return ($"La funcion '{(string)f.nombre}' no lleva parametro.", null, null, null);
            tipo = ReglasEvaluador.FuncionRetorno(fc); multi = false;
        }
        else if (!string.IsNullOrWhiteSpace(r.ParametroFuncion)) return ("Hay un parametro de funcion sin funcion.", null, null, null);

        if (multi && !OperadoresMultivalor.Contains(op)) return ($"El operador '{(string)o.nombre}' no aplica a un atributo multivalor.", null, null, null);
        var tiposOp = ((string)o.tipos_dato).Split(',');
        var tipoOp = tipo;
        if (!multi && !tiposOp.Contains(tipoOp)) return ($"El operador '{(string)o.nombre}' no aplica a un valor de tipo {tipo}.", null, null, null);

        if (r.OrigenValor is not ("Valor" or "Atributo")) return ("El origen del valor debe ser Valor o Atributo.", null, null, null);
        string? v1 = null, v2 = null, lista = null;
        try
        {
            if (aridad == 0)
            {
                if (r.OrigenValor != "Valor" || r.IdAtributoValor is not null) return ("Este operador no lleva valor.", null, null, null);
            }
            else if (aridad == 9)
            {
                if (r.OrigenValor != "Valor") return ("Una lista de valores no se puede comparar contra otro atributo.", null, null, null);
                var partes = (r.ValoresLista ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (partes.Count == 0) return ("Indique al menos un valor en la lista.", null, null, null);
                if (partes.Count > 50) return ("La lista admite como maximo 50 valores.", null, null, null);
                var norm = partes.Select(p => ReglasEvaluador.Texto(ReglasEvaluador.Coercionar(p, multi ? "Texto" : tipoOp)) ?? "").ToList();
                lista = string.Join("|", norm);
                if (lista.Length > 1000) return ("La lista es demasiado larga.", null, null, null);
            }
            else if (r.OrigenValor == "Atributo")
            {
                if (aridad == 2) return ("El operador 'esta entre' solo admite valores fijos.", null, null, null);
                if (r.IdAtributoValor is null) return ("Seleccione el atributo contra el que se compara.", null, null, null);
                var av = await c.QuerySingleOrDefaultAsync("SELECT * FROM rul.ATRIBUTO WHERE id_atributo = @i AND estado = 'Activo'", new { i = r.IdAtributoValor }, tx);
                if (av is null) return ("El atributo de comparacion no existe o esta inactivo.", null, null, null);
                if ((bool)av.es_multivalor) return ("No se puede comparar contra un atributo multivalor.", null, null, null);
                var tav = (string)av.tipo_dato == "Lista" ? "Texto" : (string)av.tipo_dato; var til = tipoOp == "Lista" ? "Texto" : tipoOp;
                if (multi ? tav != "Texto" : tav != til) return ("El atributo de comparacion debe ser del mismo tipo.", null, null, null);
            }
            else
            {
                if (r.IdAtributoValor is not null) return ("Indique el origen Atributo para comparar contra otro atributo.", null, null, null);
                var t = multi ? "Texto" : tipoOp;
                if (string.IsNullOrWhiteSpace(r.Valor1)) return ("Falta el valor.", null, null, null);
                v1 = ReglasEvaluador.Texto(ReglasEvaluador.Coercionar(r.Valor1, t));
                if (aridad == 2)
                {
                    if (string.IsNullOrWhiteSpace(r.Valor2)) return ("Falta el segundo valor del rango.", null, null, null);
                    v2 = ReglasEvaluador.Texto(ReglasEvaluador.Coercionar(r.Valor2, t));
                    if (ReglasEvaluador.Comparar2(v1!, v2!, t) > 0) return ("El primer valor del rango no puede ser mayor que el segundo.", null, null, null);
                }
                else if (!string.IsNullOrWhiteSpace(r.Valor2)) return ("Este operador solo lleva un valor.", null, null, null);
            }
        }
        catch (FormatException ex) { return (ex.Message, null, null, null); }
        return (null, v1, v2, lista);
    }

    public async Task<ResultadoOperacion> GuardarCondicionAsync(ActorContext actor, long? id, CondicionGuardarRequest r)
    {
        var e = ValidarCodigoNombre(r.Codigo, r.Nombre);
        if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", e);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            dynamic? previo = null;
            if (id is not null)
            {
                previo = await c.QuerySingleOrDefaultAsync(
                    $"SELECT c.*, (SELECT COUNT(*) FROM rul.GRUPO_CONDICION_MIEMBRO m JOIN rul.GRUPO_CONDICION g ON g.id_grupo = m.id_grupo JOIN rul.REGLA r ON r.id_regla = g.id_regla JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = r.id_version WHERE m.id_condicion = c.id_condicion AND v.estado <> 'Borrador') AS congeladas FROM rul.CONDICION c WITH (UPDLOCK) WHERE c.id_condicion = @id AND c.id_compania = @co",
                    new { id, co = actor.IdCompania }, tx);
                if (previo is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la condicion.");
                if ((int)previo.congeladas > 0) return ResultadoOperacion.Fallo(409, "pieza_publicada", "La condicion forma parte de una version ya enviada a aprobacion o publicada; duplicala y modifica la copia.");
            }
            var (err, v1, v2, lista) = await ValidarCondicionAsync(c, tx, r);
            if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
            var p = new { co = actor.IdCompania, codigo = r.Codigo.Trim(), nombre = r.Nombre.Trim(), desc = Nulo(r.Descripcion), attr = r.IdAtributo, fn = r.IdFuncion, pf = Nulo(r.ParametroFuncion), op = r.IdOperador,
                          origen = r.OrigenValor, v1, v2, lista, av = r.OrigenValor == "Atributo" ? r.IdAtributoValor : null, por = actor.NombreDescriptivo, id };
            if (id is null)
            {
                var nuevo = await c.QuerySingleAsync<long>(
                    """
                    INSERT INTO rul.CONDICION (id_compania, codigo, nombre, descripcion, id_atributo, id_funcion, parametro_funcion, id_operador, origen_valor, valor_1, valor_2, valores_lista, id_atributo_valor, creado_por)
                    OUTPUT INSERTED.id_condicion VALUES (@co, @codigo, @nombre, @desc, @attr, @fn, @pf, @op, @origen, @v1, @v2, @lista, @av, @por)
                    """, p, tx);
                await Auditoria.AltaAsync(c, tx, actor, EntidadCondicion, nuevo, r.Codigo.Trim());
                tx.Commit();
                return ResultadoOperacion.Ok(nuevo);
            }
            await c.ExecuteAsync(
                """
                UPDATE rul.CONDICION SET codigo = @codigo, nombre = @nombre, descripcion = @desc, id_atributo = @attr, id_funcion = @fn, parametro_funcion = @pf, id_operador = @op, origen_valor = @origen,
                       valor_1 = @v1, valor_2 = @v2, valores_lista = @lista, id_atributo_valor = @av, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_condicion = @id
                """, p, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadCondicion, id.Value, "condicion", (string)previo!.codigo, r.Codigo.Trim());
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe una condicion con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> DuplicarCondicionAsync(ActorContext actor, long id, CondicionDuplicarRequest r)
    {
        var e = ValidarCodigoNombre(r.Codigo, r.Nombre);
        if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", e);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var o = await c.QuerySingleOrDefaultAsync("SELECT * FROM rul.CONDICION WHERE id_condicion = @id AND id_compania = @co", new { id, co = actor.IdCompania }, tx);
            if (o is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la condicion.");
            var nuevo = await c.QuerySingleAsync<long>(
                """
                INSERT INTO rul.CONDICION (id_compania, codigo, nombre, descripcion, id_atributo, id_funcion, parametro_funcion, id_operador, origen_valor, valor_1, valor_2, valores_lista, id_atributo_valor, creado_por)
                OUTPUT INSERTED.id_condicion
                SELECT id_compania, @codigo, @nombre, descripcion, id_atributo, id_funcion, parametro_funcion, id_operador, origen_valor, valor_1, valor_2, valores_lista, id_atributo_valor, @por FROM rul.CONDICION WHERE id_condicion = @id
                """, new { codigo = r.Codigo.Trim(), nombre = r.Nombre.Trim(), por = actor.NombreDescriptivo, id }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadCondicion, nuevo, $"{r.Codigo.Trim()} (copia de {(string)o.codigo})");
            tx.Commit();
            return ResultadoOperacion.Ok(nuevo);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe una condicion con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public Task<ResultadoOperacion> CambiarEstadoCondicionAsync(ActorContext actor, long id, string estado) =>
        CambiarEstadoPiezaAsync(actor, id, estado, "CONDICION", "id_condicion", EntidadCondicion,
            "SELECT COUNT(DISTINCT r.id_version) FROM rul.GRUPO_CONDICION_MIEMBRO m JOIN rul.GRUPO_CONDICION g ON g.id_grupo = m.id_grupo JOIN rul.REGLA r ON r.id_regla = g.id_regla JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = r.id_version WHERE m.id_condicion = @id AND v.estado IN ('Borrador', 'Revision', 'Programado', 'Vigente')");

    private async Task<ResultadoOperacion> CambiarEstadoPiezaAsync(ActorContext actor, long id, string estado, string tabla, string columnaId, string entidad, string sqlUsos)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>($"SELECT estado FROM rul.{tabla} WITH (UPDLOCK) WHERE {columnaId} = @id AND id_compania = @co", new { id, co = actor.IdCompania }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el registro.");
        if (estado == "Inactivo")
        {
            var usos = await c.ExecuteScalarAsync<int>(sqlUsos, new { id }, tx);
            if (usos > 0) return ResultadoOperacion.Fallo(409, "en_uso", $"Se usa en {usos} version(es) de conjuntos de reglas que aun no expiran; quitala de ellas primero.");
        }
        if (ant != estado)
        {
            await c.ExecuteAsync($"UPDATE rul.{tabla} SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE {columnaId} = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, entidad, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    // =============================================================== Acciones (pool)
    internal static string ResumenAccion(string tipo, string? resultado, string? clave, string? valor, string? mensaje) => tipo switch
    {
        "Decision" => $"{resultado}{(string.IsNullOrWhiteSpace(mensaje) ? "" : $" ({mensaje})")}",
        "Asignar" => $"{clave} = {valor}",
        _ => $"Mensaje: {mensaje}",
    };

    public async Task<IReadOnlyList<AccionResponse>> ListarAccionesAsync(ActorContext actor)
    {
        using var c = NuevaConexion(); c.Open();
        var f = await c.QueryAsync(
            """
            SELECT a.*,
              (SELECT COUNT(DISTINCT r.id_version) FROM rul.REGLA_ACCION ra JOIN rul.REGLA r ON r.id_regla = ra.id_regla WHERE ra.id_accion = a.id_accion) AS usos,
              (SELECT COUNT(DISTINCT r.id_version) FROM rul.REGLA_ACCION ra JOIN rul.REGLA r ON r.id_regla = ra.id_regla JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = r.id_version
                WHERE ra.id_accion = a.id_accion AND v.estado <> 'Borrador') AS congeladas
            FROM rul.ACCION a WHERE a.id_compania = @co ORDER BY a.codigo
            """, new { co = actor.IdCompania });
        return f.Select(a => new AccionResponse((long)a.id_accion, (string)a.codigo, (string)a.nombre, (string?)a.descripcion, (string)a.tipo_accion, (string?)a.resultado, (string?)a.clave, (string?)a.valor,
            (string?)a.mensaje, (string)a.estado, ResumenAccion((string)a.tipo_accion, (string?)a.resultado, (string?)a.clave, (string?)a.valor, (string?)a.mensaje), (int)a.usos, (int)a.congeladas > 0)).ToList();
    }

    public async Task<ResultadoOperacion> GuardarAccionAsync(ActorContext actor, long? id, AccionGuardarRequest r)
    {
        var e = ValidarCodigoNombre(r.Codigo, r.Nombre);
        if (e is null)
            e = r.TipoAccion switch
            {
                "Decision" => Decisiones.Contains(r.Resultado) ? null : "Elija la decision: Aprobar, Rechazar o Referir.",
                "Asignar" => string.IsNullOrWhiteSpace(r.Clave) || r.Clave.Trim().Length > 100 || r.Clave.Trim().Any(char.IsWhiteSpace) ? "La clave es obligatoria, sin espacios (maximo 100)."
                           : string.IsNullOrWhiteSpace(r.Valor) || r.Valor.Trim().Length > 200 ? "El valor es obligatorio (maximo 200)." : null,
                "Mensaje" => string.IsNullOrWhiteSpace(r.Mensaje) ? "El mensaje es obligatorio." : null,
                _ => "El tipo de accion debe ser Decision, Asignar o Mensaje.",
            };
        if (e is null && r.Mensaje is { Length: > 500 }) e = "El mensaje admite maximo 500 caracteres.";
        if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", e);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            dynamic? previo = null;
            if (id is not null)
            {
                previo = await c.QuerySingleOrDefaultAsync(
                    "SELECT a.*, (SELECT COUNT(*) FROM rul.REGLA_ACCION ra JOIN rul.REGLA r ON r.id_regla = ra.id_regla JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = r.id_version WHERE ra.id_accion = a.id_accion AND v.estado <> 'Borrador') AS congeladas FROM rul.ACCION a WITH (UPDLOCK) WHERE a.id_accion = @id AND a.id_compania = @co",
                    new { id, co = actor.IdCompania }, tx);
                if (previo is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la accion.");
                if ((int)previo.congeladas > 0) return ResultadoOperacion.Fallo(409, "pieza_publicada", "La accion forma parte de una version ya enviada a aprobacion o publicada; duplicala y modifica la copia.");
            }
            var t = r.TipoAccion;
            var p = new { co = actor.IdCompania, codigo = r.Codigo.Trim(), nombre = r.Nombre.Trim(), desc = Nulo(r.Descripcion), tipo = t, res = t == "Decision" ? r.Resultado : null,
                          clave = t == "Asignar" ? r.Clave!.Trim() : null, valor = t == "Asignar" ? r.Valor!.Trim() : null, msg = t == "Asignar" ? null : Nulo(r.Mensaje), por = actor.NombreDescriptivo, id };
            if (id is null)
            {
                var nuevo = await c.QuerySingleAsync<long>(
                    "INSERT INTO rul.ACCION (id_compania, codigo, nombre, descripcion, tipo_accion, resultado, clave, valor, mensaje, creado_por) OUTPUT INSERTED.id_accion VALUES (@co, @codigo, @nombre, @desc, @tipo, @res, @clave, @valor, @msg, @por)", p, tx);
                await Auditoria.AltaAsync(c, tx, actor, EntidadAccion, nuevo, r.Codigo.Trim());
                tx.Commit();
                return ResultadoOperacion.Ok(nuevo);
            }
            await c.ExecuteAsync(
                "UPDATE rul.ACCION SET codigo = @codigo, nombre = @nombre, descripcion = @desc, tipo_accion = @tipo, resultado = @res, clave = @clave, valor = @valor, mensaje = @msg, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_accion = @id", p, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadAccion, id.Value, "accion", (string)previo!.codigo, r.Codigo.Trim());
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe una accion con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> DuplicarAccionAsync(ActorContext actor, long id, CondicionDuplicarRequest r)
    {
        var e = ValidarCodigoNombre(r.Codigo, r.Nombre);
        if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", e);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var o = await c.QuerySingleOrDefaultAsync("SELECT codigo FROM rul.ACCION WHERE id_accion = @id AND id_compania = @co", new { id, co = actor.IdCompania }, tx);
            if (o is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la accion.");
            var nuevo = await c.QuerySingleAsync<long>(
                "INSERT INTO rul.ACCION (id_compania, codigo, nombre, descripcion, tipo_accion, resultado, clave, valor, mensaje, creado_por) OUTPUT INSERTED.id_accion SELECT id_compania, @codigo, @nombre, descripcion, tipo_accion, resultado, clave, valor, mensaje, @por FROM rul.ACCION WHERE id_accion = @id",
                new { codigo = r.Codigo.Trim(), nombre = r.Nombre.Trim(), por = actor.NombreDescriptivo, id }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadAccion, nuevo, $"{r.Codigo.Trim()} (copia de {(string)o.codigo})");
            tx.Commit();
            return ResultadoOperacion.Ok(nuevo);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe una accion con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public Task<ResultadoOperacion> CambiarEstadoAccionAsync(ActorContext actor, long id, string estado) =>
        CambiarEstadoPiezaAsync(actor, id, estado, "ACCION", "id_accion", EntidadAccion,
            "SELECT COUNT(DISTINCT r.id_version) FROM rul.REGLA_ACCION ra JOIN rul.REGLA r ON r.id_regla = ra.id_regla JOIN rul.CONJUNTO_REGLAS_VERSION v ON v.id_version = r.id_version WHERE ra.id_accion = @id AND v.estado IN ('Borrador', 'Revision', 'Programado', 'Vigente')");
}
