using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Mantenimiento de las reglas de aprobacion (apr.REGLA_APROBACION y sus condiciones / niveles) y del parametro
/// permite_mismo_usuario_multiples_niveles por tipo de gestion. Los cambios solo afectan a gestiones NUEVAS:
/// los niveles se copian a apr.GESTION_APROBACION al crear la gestion.
/// </summary>
public class ReglaAprobacionRepositorio
{
    public const string Objeto = "GES_REGLAS";
    private const int MaxNiveles = 10;
    private static readonly string[] Operadores = ["IGUAL", "DIFERENTE", "EN"];

    private readonly TenantContext _tenant;
    public ReglaAprobacionRepositorio(TenantContext tenant) => _tenant = tenant;
    private SqlConnection Nueva() => new(_tenant.ConnectionString);

    // ---------- Tipos de gestion ----------
    public async Task<IReadOnlyList<TipoGestionAdminResponse>> TiposAsync(long idCompania)
    {
        using var c = Nueva();
        var f = await c.QueryAsync(
            """
            SELECT t.id_tipo_gestion, t.codigo, t.nombre, t.categoria, t.permite_mismo_usuario_multiples_niveles AS repetir,
                   (SELECT COUNT(*) FROM apr.REGLA_APROBACION r WHERE r.id_tipo_gestion = t.id_tipo_gestion AND r.id_compania = @c AND r.estado = 'Activo') AS activas,
                   (SELECT COUNT(*) FROM apr.REGLA_APROBACION r WHERE r.id_tipo_gestion = t.id_tipo_gestion AND r.id_compania = @c) AS total
            FROM apr.TIPO_GESTION t WHERE t.estado = 'Activo' ORDER BY t.categoria, t.nombre
            """, new { c = idCompania });
        return f.Select(x => new TipoGestionAdminResponse((long)x.id_tipo_gestion, (string)x.codigo, (string)x.nombre, (string)x.categoria, (bool)x.repetir, (int)x.activas, (int)x.total)).ToList();
    }

    public async Task<ResultadoOperacion> CambiarPermiteRepetirAsync(ActorContext actor, long idTipo, bool valor)
    {
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync("SELECT permite_mismo_usuario_multiples_niveles AS v FROM apr.TIPO_GESTION WHERE id_tipo_gestion = @i", new { i = idTipo }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El tipo de gestión no existe.");
        if ((bool)a.v != valor)
        {
            await c.ExecuteAsync("UPDATE apr.TIPO_GESTION SET permite_mismo_usuario_multiples_niveles = @v WHERE id_tipo_gestion = @i", new { v = valor, i = idTipo }, tx);
            await Auditoria.CambioAsync(c, tx, actor, "TIPO_GESTION", idTipo, "permite_repetir", (bool)a.v ? "Sí" : "No", valor ? "Sí" : "No");
        }
        tx.Commit();
        return ResultadoOperacion.Ok(idTipo);
    }

    // ---------- Nombres para mostrar ----------
    private sealed class Nombres
    {
        public Dictionary<long, string> Usuarios = new(), Grupos = new(), Unidades = new();
        public Dictionary<string, string> Canales = new(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Nombres> CargarNombresAsync(SqlConnection c, long idCompania, SqlTransaction? tx = null)
    {
        var n = new Nombres();
        foreach (var x in await c.QueryAsync("SELECT id_usuario, nombre_completo FROM sec.USUARIO WHERE id_compania = @c", new { c = idCompania }, tx)) n.Usuarios[(long)x.id_usuario] = (string)x.nombre_completo;
        foreach (var x in await c.QueryAsync("SELECT id_grupo, nombre FROM sec.GRUPO_USUARIOS WHERE id_compania = @c", new { c = idCompania }, tx)) n.Grupos[(long)x.id_grupo] = (string)x.nombre;
        foreach (var x in await c.QueryAsync("SELECT id_departamento, nombre FROM sec.DEPARTAMENTO WHERE id_compania = @c", new { c = idCompania }, tx)) n.Unidades[(long)x.id_departamento] = (string)x.nombre;
        foreach (var x in await c.QueryAsync(
            "SELECT i.codigo, i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE t.codigo = 'CANAL'", null, tx))
            n.Canales[(string)x.codigo] = (string)x.nombre;
        return n;
    }

    private static string ValorTexto(Nombres n, string variable, string valor)
    {
        var partes = valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string T(string p) => variable switch
        {
            "UNIDAD" => long.TryParse(p, out var u) ? n.Unidades.GetValueOrDefault(u, p) : p,
            "USUARIO" => long.TryParse(p, out var us) ? n.Usuarios.GetValueOrDefault(us, p) : p,
            "CANAL" => n.Canales.GetValueOrDefault(p, p),
            "TIPO_ACTOR" => p == "PERSONA" ? "Persona (backoffice)" : p == "INTEGRACION" ? "Integración" : p,
            _ => p
        };
        return string.Join(", ", partes.Select(T));
    }

    private static string NombreVariable(string v) => v switch
    {
        "CANAL" => "Canal", "UNIDAD" => "Unidad", "USUARIO" => "Solicitante", "PUESTO" => "Puesto", "TIPO_ACTOR" => "Tipo de actor", _ => v
    };
    private static string NombreOperador(string o) => o switch { "IGUAL" => "es", "DIFERENTE" => "no es", "EN" => "es uno de", _ => o };
    private static string TextoCond(Nombres n, string v, string o, string valor) => $"{NombreVariable(v)} {NombreOperador(o)} {ValorTexto(n, v, valor)}";

    private static string Destino(Nombres n, string tipo, long? u, long? g) => tipo switch
    {
        "USUARIO" => u is not null ? n.Usuarios.GetValueOrDefault(u.Value, u.ToString()!) : "?",
        "GRUPO" => g is not null ? "Grupo " + n.Grupos.GetValueOrDefault(g.Value, g.ToString()!) : "?",
        "SUPERIOR" => "Superior del solicitante",
        "AUTOMATICA" => "Aprobación automática",
        _ => tipo
    };

    // ---------- Listado ----------
    public async Task<IReadOnlyList<ReglaAdminResponse>> ListarAsync(long idCompania, long idTipo)
    {
        using var c = Nueva();
        var n = await CargarNombresAsync(c, idCompania);
        var reglas = (await c.QueryAsync(
            """
            SELECT r.id_regla, r.id_tipo_gestion, r.nombre, r.prioridad, r.estado,
                   (SELECT COUNT(*) FROM apr.GESTION g WHERE g.id_regla = r.id_regla) AS usos
            FROM apr.REGLA_APROBACION r WHERE r.id_compania = @c AND r.id_tipo_gestion = @t ORDER BY r.prioridad, r.id_regla
            """, new { c = idCompania, t = idTipo })).ToList();
        var ids = reglas.Select(r => (long)r.id_regla).DefaultIfEmpty(0).ToList();
        var conds = (await c.QueryAsync("SELECT id_regla, variable, operador, valor FROM apr.REGLA_APROBACION_CONDICION WHERE id_regla IN @ids ORDER BY id_condicion", new { ids })).ToList();
        var nivs = (await c.QueryAsync("SELECT id_regla, nivel, tipo_aprobador, id_usuario, id_grupo FROM apr.REGLA_APROBACION_NIVEL WHERE id_regla IN @ids ORDER BY nivel", new { ids })).ToList();
        return reglas.Select(r =>
        {
            var id = (long)r.id_regla;
            return new ReglaAdminResponse(id, (long)r.id_tipo_gestion, (string)r.nombre, (int)r.prioridad, (string)r.estado,
                conds.Where(k => (long)k.id_regla == id).Select(k => new CondicionResponse((string)k.variable, (string)k.operador, (string)k.valor, ValorTexto(n, (string)k.variable, (string)k.valor))).ToList(),
                nivs.Where(k => (long)k.id_regla == id).Select(k => new NivelReglaResponse((int)(short)k.nivel, (string)k.tipo_aprobador, (long?)k.id_usuario, (long?)k.id_grupo, Destino(n, (string)k.tipo_aprobador, (long?)k.id_usuario, (long?)k.id_grupo))).ToList(),
                (int)r.usos);
        }).ToList();
    }

    // ---------- Validacion ----------
    private static async Task<(ResultadoOperacion? Error, List<CondicionDto> Conds, List<NivelReglaDto> Nivs)> ValidarAsync(
        SqlConnection c, SqlTransaction tx, long idCompania, ReglaGuardarRequest req, bool exigirActivos)
    {
        var conds = new List<CondicionDto>(); var nivs = new List<NivelReglaDto>();
        ResultadoOperacion E(string cod, string msg) => ResultadoOperacion.Fallo(400, cod, msg);

        var canales = (await c.QueryAsync<string>(
            "SELECT i.codigo FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE t.codigo = 'CANAL'", null, tx)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var k in req.Condiciones ?? [])
        {
            var v = (k.Variable ?? "").Trim().ToUpperInvariant(); var o = (k.Operador ?? "").Trim().ToUpperInvariant();
            var partes = (k.Valor ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
            if (!MotorAprobacion.Variables.Contains(v)) return (E("variable_invalida", $"La variable '{k.Variable}' no es válida."), conds, nivs);
            if (!Operadores.Contains(o)) return (E("operador_invalido", $"El operador '{k.Operador}' no es válido."), conds, nivs);
            if (partes.Count == 0) return (E("valor_requerido", $"Indique el valor de la condición {NombreVariable(v)}."), conds, nivs);
            if (o != "EN" && partes.Count > 1) return (E("valor_invalido", $"La condición {NombreVariable(v)} '{NombreOperador(o)}' admite un solo valor; use 'es uno de' para varios."), conds, nivs);
            if (v == "CANAL" && partes.Any(p => !canales.Contains(p))) return (E("canal_invalido", "Hay un canal que no existe en la lista de canales."), conds, nivs);
            if (v == "TIPO_ACTOR" && partes.Any(p => p is not ("PERSONA" or "INTEGRACION"))) return (E("tipo_actor_invalido", "Tipo de actor inválido."), conds, nivs);
            if (v == "PUESTO" && partes.Any(p => p.Length > 100)) return (E("puesto_invalido", "El puesto admite máximo 100 caracteres."), conds, nivs);
            if (v is "UNIDAD" or "USUARIO")
            {
                if (partes.Any(p => !long.TryParse(p, out _))) return (E("valor_invalido", $"Valor inválido en la condición {NombreVariable(v)}."), conds, nivs);
                var idsV = partes.Select(long.Parse).ToList();
                var tabla = v == "UNIDAD" ? "SELECT COUNT(*) FROM sec.DEPARTAMENTO WHERE id_compania = @c AND id_departamento IN @ids" : "SELECT COUNT(*) FROM sec.USUARIO WHERE id_compania = @c AND id_usuario IN @ids";
                if (await c.ExecuteScalarAsync<int>(tabla, new { c = idCompania, ids = idsV }, tx) != idsV.Count) return (E("valor_invalido", $"La condición {NombreVariable(v)} referencia registros inexistentes."), conds, nivs);
            }
            var texto = string.Join(",", partes);
            if (texto.Length > 500) return (E("valor_largo", "El valor de la condición es demasiado largo."), conds, nivs);
            if (conds.Any(x => x.Variable == v && x.Operador == o && x.Valor == texto)) continue;
            conds.Add(new CondicionDto(v, o, texto));
        }

        if (req.Niveles is null || req.Niveles.Count == 0) return (E("niveles_requeridos", "La regla debe tener al menos un nivel de aprobación."), conds, nivs);
        if (req.Niveles.Count > MaxNiveles) return (E("demasiados_niveles", $"Máximo {MaxNiveles} niveles de aprobación."), conds, nivs);
        foreach (var nv in req.Niveles)
        {
            var t = (nv.Tipo ?? "").Trim().ToUpperInvariant();
            switch (t)
            {
                case "USUARIO":
                    if (nv.IdUsuario is null) return (E("usuario_requerido", "Seleccione el usuario aprobador."), conds, nivs);
                    var okU = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM sec.USUARIO WHERE id_compania = @c AND id_usuario = @u AND tipo_usuario = 'FISICO'{(exigirActivos ? " AND estado = 'Activo'" : "")}", new { c = idCompania, u = nv.IdUsuario }, tx);
                    if (okU == 0) return (E("usuario_invalido", "El usuario aprobador no existe o está inactivo."), conds, nivs);
                    nivs.Add(new NivelReglaDto(t, nv.IdUsuario, null)); break;
                case "GRUPO":
                    if (nv.IdGrupo is null) return (E("grupo_requerido", "Seleccione el grupo aprobador."), conds, nivs);
                    var g = await c.QuerySingleOrDefaultAsync(
                        "SELECT g.estado, (SELECT COUNT(*) FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = g.id_grupo) AS miembros FROM sec.GRUPO_USUARIOS g WHERE g.id_compania = @c AND g.id_grupo = @g",
                        new { c = idCompania, g = nv.IdGrupo }, tx);
                    if (g is null || (exigirActivos && (string)g.estado != "Activo")) return (E("grupo_invalido", "El grupo aprobador no existe o está inactivo."), conds, nivs);
                    if (exigirActivos && (int)g.miembros == 0) return (E("grupo_vacio", "El grupo aprobador no tiene miembros."), conds, nivs);
                    nivs.Add(new NivelReglaDto(t, null, nv.IdGrupo)); break;
                case "SUPERIOR": case "AUTOMATICA":
                    nivs.Add(new NivelReglaDto(t, null, null)); break;
                default: return (E("tipo_nivel_invalido", $"Tipo de aprobador '{nv.Tipo}' inválido."), conds, nivs);
            }
        }
        return (null, conds, nivs);
    }

    private static async Task InsertarHijosAsync(SqlConnection c, SqlTransaction tx, long idRegla, List<CondicionDto> conds, List<NivelReglaDto> nivs)
    {
        foreach (var k in conds)
            await c.ExecuteAsync("INSERT INTO apr.REGLA_APROBACION_CONDICION (id_regla, variable, operador, valor) VALUES (@r, @v, @o, @x)", new { r = idRegla, v = k.Variable, o = k.Operador, x = k.Valor }, tx);
        short i = 1;
        foreach (var nv in nivs)
            await c.ExecuteAsync("INSERT INTO apr.REGLA_APROBACION_NIVEL (id_regla, nivel, tipo_aprobador, id_usuario, id_grupo) VALUES (@r, @n, @t, @u, @g)",
                new { r = idRegla, n = i++, t = nv.Tipo, u = nv.IdUsuario, g = nv.IdGrupo }, tx);
    }

    private static string ResumenCondiciones(Nombres n, IEnumerable<CondicionDto> conds)
    {
        var l = conds.Select(k => TextoCond(n, k.Variable, k.Operador, k.Valor)).ToList();
        return l.Count == 0 ? "Siempre (sin condiciones)" : string.Join(" Y ", l);
    }
    private static string ResumenNiveles(Nombres n, IEnumerable<NivelReglaDto> nivs) =>
        string.Join(" → ", nivs.Select((x, i) => $"{i + 1}. {Destino(n, x.Tipo, x.IdUsuario, x.IdGrupo)}"));

    private static async Task RenumerarAsync(SqlConnection c, SqlTransaction tx, long idCompania, long idTipo, ActorContext? actor = null, bool auditar = false)
    {
        var filas = (await c.QueryAsync("SELECT id_regla, prioridad FROM apr.REGLA_APROBACION WHERE id_compania = @c AND id_tipo_gestion = @t ORDER BY prioridad, id_regla", new { c = idCompania, t = idTipo }, tx)).ToList();
        for (var i = 0; i < filas.Count; i++)
        {
            var nueva = (i + 1) * 10;
            if ((int)filas[i].prioridad != nueva)
            {
                await c.ExecuteAsync("UPDATE apr.REGLA_APROBACION SET prioridad = @p WHERE id_regla = @r", new { p = nueva, r = (long)filas[i].id_regla }, tx);
                if (auditar && actor is not null) await Auditoria.CambioAsync(c, tx, actor, "REGLA_APROBACION", (long)filas[i].id_regla, "prioridad", ((int)filas[i].prioridad / 10).ToString(), (nueva / 10).ToString());
            }
        }
    }

    // ---------- Crear / editar ----------
    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, ReglaGuardarRequest req)
    {
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length is < 2 or > 200) return ResultadoOperacion.Fallo(400, "nombre_invalido", "El nombre de la regla es obligatorio (máximo 200 caracteres).");
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM apr.TIPO_GESTION WHERE id_tipo_gestion = @t", new { t = req.IdTipoGestion }, tx) == 0)
            return ResultadoOperacion.Fallo(404, "tipo_no_encontrado", "El tipo de gestión no existe.");
        var (err, conds, nivs) = await ValidarAsync(c, tx, actor.IdCompania, req, true);
        if (err is not null) return err;

        // Una regla nueva se coloca antes de la primera "comodin" (sin condiciones); si no hay, al final.
        var existentes = (await c.QueryAsync(
            """
            SELECT r.id_regla, r.prioridad, (SELECT COUNT(*) FROM apr.REGLA_APROBACION_CONDICION k WHERE k.id_regla = r.id_regla) AS conds
            FROM apr.REGLA_APROBACION r WHERE r.id_compania = @c AND r.id_tipo_gestion = @t ORDER BY r.prioridad, r.id_regla
            """, new { c = actor.IdCompania, t = req.IdTipoGestion }, tx)).ToList();
        var comodin = existentes.FirstOrDefault(x => (int)x.conds == 0);
        var prioridad = comodin is not null ? (int)comodin.prioridad - 1 : (existentes.Count == 0 ? 10 : (int)existentes[^1].prioridad + 10);

        var id = await c.ExecuteScalarAsync<long>(
            "INSERT INTO apr.REGLA_APROBACION (id_compania, id_tipo_gestion, nombre, prioridad, creado_por) OUTPUT INSERTED.id_regla VALUES (@c, @t, @n, @p, @u)",
            new { c = actor.IdCompania, t = req.IdTipoGestion, n = nombre, p = prioridad, u = actor.NombreDescriptivo }, tx);
        await InsertarHijosAsync(c, tx, id, conds, nivs);
        await RenumerarAsync(c, tx, actor.IdCompania, req.IdTipoGestion);
        var nm = await CargarNombresAsync(c, actor.IdCompania, tx);
        await Auditoria.AltaAsync(c, tx, actor, "REGLA_APROBACION", id, $"{nombre} | Condiciones: {ResumenCondiciones(nm, conds)} | Niveles: {ResumenNiveles(nm, nivs)}");
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long id, ReglaGuardarRequest req)
    {
        var nombre = (req.Nombre ?? "").Trim();
        if (nombre.Length is < 2 or > 200) return ResultadoOperacion.Fallo(400, "nombre_invalido", "El nombre de la regla es obligatorio (máximo 200 caracteres).");
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync("SELECT id_tipo_gestion, nombre, estado FROM apr.REGLA_APROBACION WHERE id_regla = @i AND id_compania = @c", new { i = id, c = actor.IdCompania }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "La regla no existe.");
        var (err, conds, nivs) = await ValidarAsync(c, tx, actor.IdCompania, req, (string)a.estado == "Activo");
        if (err is not null) return err;

        var nm = await CargarNombresAsync(c, actor.IdCompania, tx);
        var condAnt = (await c.QueryAsync("SELECT variable, operador, valor FROM apr.REGLA_APROBACION_CONDICION WHERE id_regla = @i ORDER BY id_condicion", new { i = id }, tx))
            .Select(k => new CondicionDto((string)k.variable, (string)k.operador, (string)k.valor)).ToList();
        var nivAnt = (await c.QueryAsync("SELECT tipo_aprobador, id_usuario, id_grupo FROM apr.REGLA_APROBACION_NIVEL WHERE id_regla = @i ORDER BY nivel", new { i = id }, tx))
            .Select(k => new NivelReglaDto((string)k.tipo_aprobador, (long?)k.id_usuario, (long?)k.id_grupo)).ToList();

        await c.ExecuteAsync("DELETE FROM apr.REGLA_APROBACION_CONDICION WHERE id_regla = @i; DELETE FROM apr.REGLA_APROBACION_NIVEL WHERE id_regla = @i;", new { i = id }, tx);
        await InsertarHijosAsync(c, tx, id, conds, nivs);
        await c.ExecuteAsync("UPDATE apr.REGLA_APROBACION SET nombre = @n, modificado_por = @u, modificado_en = SYSUTCDATETIME() WHERE id_regla = @i", new { n = nombre, u = actor.NombreDescriptivo, i = id }, tx);

        if ((string)a.nombre != nombre) await Auditoria.CambioAsync(c, tx, actor, "REGLA_APROBACION", id, "nombre", (string)a.nombre, nombre);
        var cA = ResumenCondiciones(nm, condAnt); var cN = ResumenCondiciones(nm, conds);
        if (cA != cN) await Auditoria.CambioAsync(c, tx, actor, "REGLA_APROBACION", id, "condiciones", cA, cN);
        var nA = ResumenNiveles(nm, nivAnt); var nN = ResumenNiveles(nm, nivs);
        if (nA != nN) await Auditoria.CambioAsync(c, tx, actor, "REGLA_APROBACION", id, "niveles", nA, nN);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    // ---------- Estado / orden / eliminar ----------
    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado inválido.");
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync("SELECT estado FROM apr.REGLA_APROBACION WHERE id_regla = @i AND id_compania = @c", new { i = id, c = actor.IdCompania }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "La regla no existe.");
        if ((string)a.estado == estado) return ResultadoOperacion.Ok(id);
        if (estado == "Activo")
        {
            var malos = await c.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM apr.REGLA_APROBACION_NIVEL n
                LEFT JOIN sec.USUARIO u ON u.id_usuario = n.id_usuario
                LEFT JOIN sec.GRUPO_USUARIOS g ON g.id_grupo = n.id_grupo
                WHERE n.id_regla = @i AND ((n.id_usuario IS NOT NULL AND u.estado <> 'Activo')
                   OR (n.id_grupo IS NOT NULL AND (g.estado <> 'Activo' OR NOT EXISTS (SELECT 1 FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = g.id_grupo))))
                """, new { i = id }, tx);
            if (malos > 0) return ResultadoOperacion.Fallo(409, "aprobadores_inactivos", "La regla usa usuarios inactivos o grupos inactivos / sin miembros. Corrija los niveles antes de activarla.");
        }
        await c.ExecuteAsync("UPDATE apr.REGLA_APROBACION SET estado = @e, modificado_por = @u, modificado_en = SYSUTCDATETIME() WHERE id_regla = @i", new { e = estado, u = actor.NombreDescriptivo, i = id }, tx);
        await Auditoria.CambioAsync(c, tx, actor, "REGLA_APROBACION", id, "estado", (string)a.estado, estado);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<ResultadoOperacion> ReordenarAsync(ActorContext actor, ReglaOrdenRequest req)
    {
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var actuales = (await c.QueryAsync<long>("SELECT id_regla FROM apr.REGLA_APROBACION WHERE id_compania = @c AND id_tipo_gestion = @t", new { c = actor.IdCompania, t = req.IdTipoGestion }, tx)).ToHashSet();
        var nuevos = (req.IdsReglas ?? []).ToList();
        if (nuevos.Count != actuales.Count || nuevos.Distinct().Count() != nuevos.Count || nuevos.Any(x => !actuales.Contains(x)))
            return ResultadoOperacion.Fallo(400, "orden_invalido", "El orden debe incluir exactamente las reglas del tipo de gestión. Recargue la pantalla.");
        var prev = (await c.QueryAsync("SELECT id_regla, prioridad FROM apr.REGLA_APROBACION WHERE id_compania = @c AND id_tipo_gestion = @t", new { c = actor.IdCompania, t = req.IdTipoGestion }, tx))
            .ToDictionary(x => (long)x.id_regla, x => (int)x.prioridad);
        for (var i = 0; i < nuevos.Count; i++)
        {
            var p = (i + 1) * 10;
            if (prev[nuevos[i]] != p)
            {
                await c.ExecuteAsync("UPDATE apr.REGLA_APROBACION SET prioridad = @p, modificado_por = @u, modificado_en = SYSUTCDATETIME() WHERE id_regla = @r", new { p, u = actor.NombreDescriptivo, r = nuevos[i] }, tx);
                await Auditoria.CambioAsync(c, tx, actor, "REGLA_APROBACION", nuevos[i], "prioridad", (prev[nuevos[i]] / 10).ToString(), (p / 10).ToString());
            }
        }
        tx.Commit();
        return ResultadoOperacion.Ok();
    }

    public async Task<ResultadoOperacion> EliminarAsync(ActorContext actor, long id)
    {
        using var c = Nueva(); c.Open(); using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync("SELECT id_tipo_gestion, nombre FROM apr.REGLA_APROBACION WHERE id_regla = @i AND id_compania = @c", new { i = id, c = actor.IdCompania }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "La regla no existe.");
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM apr.GESTION WHERE id_regla = @i", new { i = id }, tx) > 0)
            return ResultadoOperacion.Fallo(409, "regla_en_uso", "La regla ya fue usada por gestiones; no se puede eliminar. Inactívela para que deje de aplicarse.");
        await c.ExecuteAsync("DELETE FROM apr.REGLA_APROBACION_CONDICION WHERE id_regla = @i; DELETE FROM apr.REGLA_APROBACION_NIVEL WHERE id_regla = @i; DELETE FROM apr.REGLA_APROBACION WHERE id_regla = @i;", new { i = id }, tx);
        await Auditoria.BajaAsync(c, tx, actor, "REGLA_APROBACION", id, (string)a.nombre);
        await RenumerarAsync(c, tx, actor.IdCompania, (long)a.id_tipo_gestion);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    // ---------- Simulador ----------
    public async Task<ResultadoOperacion> SimularAsync(ActorContext actor, SimulacionRequest req, Action<SimulacionResponse> salida)
    {
        using var c = Nueva(); c.Open();
        var u = await c.QuerySingleOrDefaultAsync("SELECT nombre_completo FROM sec.USUARIO WHERE id_usuario = @u AND id_compania = @c", new { u = req.IdUsuario, c = actor.IdCompania });
        if (u is null) return ResultadoOperacion.Fallo(404, "usuario_no_encontrado", "El solicitante no existe.");
        var canal = (req.Canal ?? "").Trim().ToUpperInvariant();
        if (canal.Length == 0) return ResultadoOperacion.Fallo(400, "canal_requerido", "Seleccione el canal.");
        var n = await CargarNombresAsync(c, actor.IdCompania);
        var ctx = await MotorAprobacion.ContextoDeUsuarioAsync(c, null, req.IdUsuario, canal);

        var reglas = (await ListarAsync(actor.IdCompania, req.IdTipoGestion)).Where(r => r.Estado == "Activo").ToList();
        var traza = new List<SimulacionReglaResponse>(); ReglaAdminResponse? ganadora = null;
        foreach (var r in reglas)
        {
            var det = r.Condiciones.Select(k => new SimulacionCondicionResponse(TextoCond(n, k.Variable, k.Operador, k.Valor), MotorAprobacion.Cumple(ctx, k.Variable, k.Operador, k.Valor))).ToList();
            var aplica = det.All(d => d.Cumple);
            traza.Add(new SimulacionReglaResponse(r.IdRegla, r.Nombre, r.Prioridad, aplica, det));
            if (aplica && ganadora is null) ganadora = r;
        }

        var niveles = new List<SimulacionNivelResponse>();
        var lista = ganadora?.Niveles.Select(x => (x.Tipo, x.IdUsuario, x.IdGrupo)).ToList() ?? [("SUPERIOR", (long?)null, (long?)null)];
        var intervino = new HashSet<long> { req.IdUsuario };
        long? baseSup = req.IdUsuario; var baseDesconocida = false; var hayHumano = false;
        var repetir = await c.ExecuteScalarAsync<bool>("SELECT permite_mismo_usuario_multiples_niveles FROM apr.TIPO_GESTION WHERE id_tipo_gestion = @t", new { t = req.IdTipoGestion });
        var nivel = 0;
        foreach (var (tipo, idU, idG) in lista)
        {
            nivel++;
            string? nota = null; var destino = Destino(n, tipo, idU, idG);
            if (tipo == "AUTOMATICA") { niveles.Add(new SimulacionNivelResponse(nivel, tipo, destino, null)); continue; }
            if (tipo == "USUARIO")
            {
                if (idU is not null && intervino.Contains(idU.Value) && !repetir) nota = "Este usuario ya interviene en la gestión (solicitante o nivel previo); no se le asignaría.";
                if (idU is not null) { baseSup = idU; intervino.Add(idU.Value); baseDesconocida = false; }
                hayHumano = true;
            }
            else if (tipo == "GRUPO")
            {
                nota = "Cualquier miembro del grupo puede resolverlo."; baseDesconocida = true; hayHumano = true;
            }
            else // SUPERIOR
            {
                if (baseDesconocida) { nota = "Depende de qué miembro del grupo apruebe el nivel anterior."; destino = "Superior del aprobador anterior"; }
                else
                {
                    var sup = await c.QuerySingleOrDefaultAsync(
                        "SELECT s.id_usuario, s.nombre_completo FROM sec.USUARIO u JOIN sec.USUARIO s ON s.id_usuario = u.id_superior WHERE u.id_usuario = @id AND s.estado = 'Activo'", new { id = baseSup });
                    if (sup is null || (intervino.Contains((long)sup.id_usuario) && !repetir))
                    {
                        if (hayHumano)
                        {
                            niveles.Add(new SimulacionNivelResponse(nivel, tipo, "(se omite)", sup is null ? "Fin de la cadena: no hay más superiores; la gestión termina en el nivel anterior." : "El superior ya aprobó un nivel anterior; fin de la cadena."));
                            break;
                        }
                        niveles.Add(new SimulacionNivelResponse(nivel, tipo, "Cualquier usuario con permiso de aprobar", "El solicitante no tiene superior asignado."));
                        continue;
                    }
                    destino = (string)sup.nombre_completo; baseSup = (long)sup.id_usuario; intervino.Add(baseSup.Value); hayHumano = true;
                }
            }
            niveles.Add(new SimulacionNivelResponse(nivel, tipo, destino, nota));
        }

        var resumen = ganadora is null
            ? "Ninguna regla activa aplica: se usa el comportamiento predeterminado (un nivel, superior directo)."
            : $"Aplica la regla «{ganadora.Nombre}».";
        salida(new SimulacionResponse((string)u.nombre_completo, resumen, ganadora?.IdRegla, ganadora?.Nombre, traza, niveles));
        return ResultadoOperacion.Ok();
    }
}
