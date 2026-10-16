using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Motor de aprobacion parametrico. Dos partes:
///  1. REGLAS por tipo de gestion (apr.REGLA_APROBACION + condiciones sobre variables del contexto);
///     gana la primera regla activa (por prioridad) cuyas condiciones se cumplen todas.
///  2. ACCION por nivel (apr.REGLA_APROBACION_NIVEL): USUARIO nombrado, GRUPO de distribucion,
///     SUPERIOR (se deduce de la estructura organizativa) o AUTOMATICA.
/// Variables disponibles hoy: CANAL, UNIDAD, USUARIO, PUESTO, TIPO_ACTOR. Para agregar una variable
/// basta con completarla en <see cref="ConstruirContextoAsync"/> y en <see cref="Variables"/>.
/// </summary>
public static class MotorAprobacion
{
    public const string TipoUsuario = "USUARIO", TipoGrupo = "GRUPO", TipoSuperior = "SUPERIOR", TipoAutomatica = "AUTOMATICA";

    public static readonly string[] Variables = ["CANAL", "UNIDAD", "USUARIO", "PUESTO", "TIPO_ACTOR"];

    /// <summary>Valores de las variables para evaluar reglas. Unidades = la del solicitante y todas sus unidades superiores.</summary>
    public record Contexto(string Canal, string TipoActor, long? IdUsuario, string? Puesto, HashSet<string> Unidades);

    public record ResultadoInicio(bool AplicarYa, string? NombreRegla);

    public static async Task<Contexto> ConstruirContextoAsync(SqlConnection c, SqlTransaction tx, ActorContext actor)
    {
        if (actor.EsIntegracion)
        {
            var canal = await c.ExecuteScalarAsync<string?>(
                """
                SELECT ca.codigo FROM sec.USUARIO_INTEGRACION ui
                JOIN gen.LISTA_VALORES_ITEM ca ON ca.id_lista_valores_item = ui.id_canal
                WHERE ui.id_usuario_integracion = @id
                """, new { id = actor.IdUsuarioIntegracion }, tx);
            return new Contexto(canal ?? "", "INTEGRACION", actor.IdUsuario, null, []);
        }

        // Backoffice = canal Sucursal (decision vigente: el canal no depende de la estructura organizativa).
        return await ContextoDeUsuarioAsync(c, tx, actor.IdUsuario.Value, "SUCURSAL");
    }

    /// <summary>Contexto de un usuario nombrado (unidad y unidades superiores, puesto). Lo usa tambien la simulacion de reglas.</summary>
    public static async Task<Contexto> ContextoDeUsuarioAsync(SqlConnection c, SqlTransaction? tx, long idUsuario, string canal)
    {
        var u = await c.QuerySingleOrDefaultAsync(
            "SELECT id_departamento, puesto FROM sec.USUARIO WHERE id_usuario = @id", new { id = idUsuario }, tx);
        var unidades = new HashSet<string>();
        if (u?.id_departamento is not null)
        {
            var ids = await c.QueryAsync<long>(
                """
                WITH arriba AS (
                    SELECT id_departamento, id_departamento_padre FROM sec.DEPARTAMENTO WHERE id_departamento = @d
                    UNION ALL
                    SELECT p.id_departamento, p.id_departamento_padre FROM sec.DEPARTAMENTO p JOIN arriba a ON p.id_departamento = a.id_departamento_padre
                )
                SELECT id_departamento FROM arriba
                """, new { d = (long)u.id_departamento }, tx);
            foreach (var i in ids) unidades.Add(i.ToString());
        }
        return new Contexto(canal, "PERSONA", idUsuario, (string?)u?.puesto, unidades);
    }

    public static bool Cumple(Contexto x, string variable, string operador, string valor)
    {
        var lista = valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Igual(string actual) => lista.Any(v => string.Equals(v, actual, StringComparison.OrdinalIgnoreCase));
        bool Eval(Func<bool> contiene) => operador == "DIFERENTE" ? !contiene() : contiene();
        return variable switch
        {
            "CANAL" => Eval(() => Igual(x.Canal)),
            "TIPO_ACTOR" => Eval(() => Igual(x.TipoActor)),
            "USUARIO" => Eval(() => x.IdUsuario is not null && Igual(x.IdUsuario.Value.ToString())),
            "PUESTO" => Eval(() => x.Puesto is not null && Igual(x.Puesto)),
            "UNIDAD" => Eval(() => lista.Any(v => x.Unidades.Contains(v))),
            _ => false
        };
    }

    /// <summary>Elige la regla, crea los niveles de la gestion y deja asignado el primer nivel pendiente.</summary>
    public static async Task<ResultadoInicio> IniciarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, long idTipoGestion, bool exigirRevisionHumana = false)
    {
        // Adjuntos (r44a): los documentos temporales que acompanan la solicitud (X-Adjuntos) quedan ligados a esta gestion.
        await DocumentoRepositorio.VincularAGestionAsync(c, tx, actor, idGestion);
        var ctx = await ConstruirContextoAsync(c, tx, actor);
        var reglas = (await c.QueryAsync(
            "SELECT id_regla, nombre FROM apr.REGLA_APROBACION WHERE id_compania = @c AND id_tipo_gestion = @t AND estado = 'Activo' ORDER BY prioridad, id_regla",
            new { c = actor.IdCompania, t = idTipoGestion }, tx)).ToList();

        long? idRegla = null; string? nombre = null;
        foreach (var r in reglas)
        {
            var conds = await c.QueryAsync(
                "SELECT variable, operador, valor FROM apr.REGLA_APROBACION_CONDICION WHERE id_regla = @r", new { r = (long)r.id_regla }, tx);
            if (conds.All(k => Cumple(ctx, (string)k.variable, (string)k.operador, (string)k.valor)))
            {
                idRegla = (long)r.id_regla; nombre = (string)r.nombre; break;
            }
        }

        var niveles = new List<dynamic>();
        if (idRegla is not null)
            niveles = (await c.QueryAsync(
                "SELECT nivel, tipo_aprobador, id_usuario, id_grupo FROM apr.REGLA_APROBACION_NIVEL WHERE id_regla = @r ORDER BY nivel",
                new { r = idRegla }, tx)).ToList();
        if (niveles.Count == 0)
        {
            // Sin regla aplicable: comportamiento anterior (un nivel, superior directo).
            await c.ExecuteAsync(
                "INSERT INTO apr.GESTION_APROBACION (id_gestion, nivel, tipo_aprobador) VALUES (@g, 1, 'SUPERIOR')", new { g = idGestion }, tx);
            await c.ExecuteAsync("UPDATE apr.GESTION SET id_regla = NULL, niveles_requeridos = 1, nivel_actual = 1 WHERE id_gestion = @g", new { g = idGestion }, tx);
        }
        else
        {
            foreach (var n in niveles)
                await c.ExecuteAsync(
                    "INSERT INTO apr.GESTION_APROBACION (id_gestion, nivel, tipo_aprobador, id_usuario_asignado, id_grupo_asignado) VALUES (@g, @n, @t, @u, @gr)",
                    new { g = idGestion, n = (short)n.nivel, t = (string)n.tipo_aprobador, u = (long?)n.id_usuario, gr = (long?)n.id_grupo }, tx);
            await c.ExecuteAsync(
                "UPDATE apr.GESTION SET id_regla = @r, niveles_requeridos = @k, nivel_actual = 1 WHERE id_gestion = @g",
                new { r = idRegla, k = (short)niveles.Count, g = idGestion }, tx);

            // Coincidencias con personas ya registradas: aunque la regla sea automatica, una persona debe validar.
            if (exigirRevisionHumana && niveles.All(n => (string)n.tipo_aprobador == TipoAutomatica))
            {
                var extra = (short)(niveles.Count + 1);
                await c.ExecuteAsync(
                    "INSERT INTO apr.GESTION_APROBACION (id_gestion, nivel, tipo_aprobador) VALUES (@g, @n, 'SUPERIOR')", new { g = idGestion, n = extra }, tx);
                await c.ExecuteAsync("UPDATE apr.GESTION SET niveles_requeridos = @n WHERE id_gestion = @g", new { g = idGestion, n = extra }, tx);
            }
        }

        var av = await AvanzarAsync(c, tx, idGestion, 0, null, actor.IdUsuario ?? 0, nombre);
        return new ResultadoInicio(av, nombre);
    }

    /// <summary>
    /// Pasa al siguiente nivel despues de completar <paramref name="nivelCompletado"/>. Resuelve niveles automaticos y asigna el
    /// superior. Devuelve true si ya no quedan niveles (hay que aplicar el cambio).
    /// </summary>
    public static async Task<bool> AvanzarAsync(SqlConnection c, SqlTransaction tx, long idGestion, short nivelCompletado,
        long? ultimoAprobador, long idSolicitante, string? nombreRegla)
    {
        var filas = (await c.QueryAsync(
            "SELECT nivel, tipo_aprobador, estado FROM apr.GESTION_APROBACION WHERE id_gestion = @g AND nivel > @n ORDER BY nivel",
            new { g = idGestion, n = nivelCompletado }, tx)).ToList();
        var yaIntervino = (await c.QueryAsync<long>(
            "SELECT id_usuario_aprobador FROM apr.GESTION_APROBACION WHERE id_gestion = @g AND id_usuario_aprobador IS NOT NULL",
            new { g = idGestion }, tx)).ToHashSet();
        yaIntervino.Add(idSolicitante);
        var hayHumanoPrevio = ultimoAprobador is not null;
        // La cadena sigue al aprobador asignado al nivel completado (no a quien lo resolvio por permiso global).
        var asignadoPrevio = nivelCompletado > 0
            ? await c.ExecuteScalarAsync<long?>("SELECT COALESCE(id_usuario_original, id_usuario_asignado) FROM apr.GESTION_APROBACION WHERE id_gestion = @g AND nivel = @n", new { g = idGestion, n = nivelCompletado }, tx)
            : null;
        var baseSuperior = asignadoPrevio ?? ultimoAprobador ?? idSolicitante;

        foreach (var f in filas)
        {
            var nivel = (short)f.nivel;
            var tipo = (string)f.tipo_aprobador;

            if (tipo == TipoAutomatica)
            {
                await c.ExecuteAsync(
                    "UPDATE apr.GESTION_APROBACION SET estado = 'Aprobado', fecha_resolucion = SYSUTCDATETIME(), comentario = @m WHERE id_gestion = @g AND nivel = @n",
                    new { g = idGestion, n = nivel, m = $"Aprobacion automatica por regla '{nombreRegla ?? "predeterminada"}'" }, tx);
                continue;
            }

            if (tipo == TipoSuperior)
            {
                var sup = await c.ExecuteScalarAsync<long?>(
                    "SELECT s.id_usuario FROM sec.USUARIO u JOIN sec.USUARIO s ON s.id_usuario = u.id_superior WHERE u.id_usuario = @id AND s.estado = 'Activo'",
                    new { id = baseSuperior }, tx);
                if (sup is null || yaIntervino.Contains(sup.Value))
                {
                    if (hayHumanoPrevio)
                    {
                        var motivoOmision = sup is null
                            ? "Sin mas superiores en la estructura organizativa"
                            : "El superior de este nivel ya aprobo un nivel anterior de esta gestion";
                        // Fin de la cadena: termina en el ultimo nivel disponible.
                        await c.ExecuteAsync(
                            "UPDATE apr.GESTION_APROBACION SET estado = 'Omitido', comentario = @m WHERE id_gestion = @g AND nivel >= @n AND estado = 'Pendiente'",
                            new { g = idGestion, n = nivel, m = motivoOmision }, tx);
                        await c.ExecuteAsync("UPDATE apr.GESTION SET nivel_actual = @n WHERE id_gestion = @g", new { g = idGestion, n = (short)(nivel - 1) }, tx);
                        return true;
                    }
                    // Solicitante sin superior: lo resuelve cualquier usuario con GES_APROBAR (comportamiento anterior).
                    await c.ExecuteAsync("UPDATE apr.GESTION SET nivel_actual = @n WHERE id_gestion = @g", new { g = idGestion, n = nivel }, tx);
                    return false;
                }
                await c.ExecuteAsync("UPDATE apr.GESTION_APROBACION SET id_usuario_asignado = @s WHERE id_gestion = @g AND nivel = @n",
                    new { g = idGestion, n = nivel, s = sup }, tx);
            }

            await c.ExecuteAsync("UPDATE apr.GESTION SET nivel_actual = @n WHERE id_gestion = @g", new { g = idGestion, n = nivel }, tx);
            return false;
        }
        return true;
    }

    // ----- Quien puede resolver el nivel actual -------------------------------------------------

    /// <summary>Misma regla para la bandeja, el detalle y las acciones de aprobar / rechazar / devolver.</summary>
    public static bool Puede(ActorContext actor, bool esMia, bool permitirAuto, bool porSuperior, string? tipo,
        long? idUsuarioAsignado, bool esMiembro, bool yaAprobo, bool permiteRepetir)
    {
        if (actor.EsIntegracion || !actor.TienePermiso("GES_APROBAR")) return false;
        if (esMia) return permitirAuto;
        if (yaAprobo && !permiteRepetir) return false;
        return tipo switch
        {
            TipoUsuario => idUsuarioAsignado == actor.IdUsuario,
            TipoGrupo => esMiembro,
            TipoSuperior => !porSuperior || idUsuarioAsignado is null || idUsuarioAsignado == actor.IdUsuario,
            TipoAutomatica => false,
            _ => !porSuperior || idUsuarioAsignado is null || idUsuarioAsignado == actor.IdUsuario
        };
    }

    /// <summary>Valida que el actor pueda resolver el nivel actual de la gestion; devuelve el nivel o el error 403.</summary>
    public static async Task<(ResultadoOperacion? Error, short Nivel)> VerificarAsync(
        SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, long idSolicitante, bool porSuperior, bool permitirAuto)
    {
        var f = await c.QuerySingleOrDefaultAsync(
            """
            SELECT g.nivel_actual, a.tipo_aprobador, a.id_usuario_asignado, a.id_grupo_asignado,
                   tg.permite_mismo_usuario_multiples_niveles AS permite_repetir,
                   ua.nombre_completo AS asignado, gr.nombre AS grupo,
                   CASE WHEN a.id_grupo_asignado IS NOT NULL AND EXISTS (SELECT 1 FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = a.id_grupo_asignado AND m.id_usuario = @u) THEN 1 ELSE 0 END AS miembro,
                   CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION_APROBACION p WHERE p.id_gestion = g.id_gestion AND p.estado = 'Aprobado' AND p.id_usuario_aprobador = @u) THEN 1 ELSE 0 END AS ya_aprobo
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN apr.GESTION_APROBACION a ON a.id_gestion = g.id_gestion AND a.nivel = g.nivel_actual
            LEFT JOIN sec.USUARIO ua ON ua.id_usuario = a.id_usuario_asignado
            LEFT JOIN sec.GRUPO_USUARIOS gr ON gr.id_grupo = a.id_grupo_asignado
            WHERE g.id_gestion = @g
            """, new { g = idGestion, u = actor.IdUsuario }, tx);
        if (f is null) return (ResultadoOperacion.Fallo(500, "nivel_no_configurado", "La gestion no tiene un nivel de aprobacion vigente."), 0);

        var esMia = idSolicitante == actor.IdUsuario;
        var tipo = (string?)f.tipo_aprobador;
        if (Puede(actor, esMia, permitirAuto, porSuperior, tipo, (long?)f.id_usuario_asignado, (int)f.miembro == 1, (int)f.ya_aprobo == 1, (bool)f.permite_repetir))
            return (null, (short)f.nivel_actual);

        if ((int)f.ya_aprobo == 1 && !(bool)f.permite_repetir)
            return (ResultadoOperacion.Fallo(403, "aprobador_repetido", "Ya aprobo un nivel anterior de esta gestion; este nivel debe resolverlo otro usuario."), 0);
        var msg = tipo switch
        {
            TipoUsuario => $"Esta gestion (nivel {(short)f.nivel_actual}) solo la puede resolver {(string?)f.asignado}.",
            TipoGrupo => $"Esta gestion (nivel {(short)f.nivel_actual}) solo la pueden resolver integrantes del grupo {(string?)f.grupo}.",
            _ => $"Esta gestion (nivel {(short)f.nivel_actual}) debe resolverla {((string?)f.asignado is { } n ? n : "un aprobador")}."
        };
        return (ResultadoOperacion.Fallo(403, "aprobador_no_autorizado", msg), 0);
    }
}
