using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Consentimientos del cliente (tratamiento de datos, comunicaciones comerciales, compartir con terceros...). Se otorgan y se revocan por gestion
/// con aprobacion (CLI_CONSENTIMIENTO), en un carril propio: entidad INTERLOCUTOR_CONSENTIMIENTO, llave = id del cliente (una gestion pendiente por cliente).
/// Historial en cli.INTERLOCUTOR_CONSENTIMIENTO (Vigente / Revocado); un solo consentimiento vigente por cliente y tipo.
/// Detalle de la gestion: cons_t{idTipo} = Otorgar | Revocar, cons_canal (id del canal) y cons_comentario.
/// </summary>
public static class ConsentimientoServicio
{
    public const string Entidad = "INTERLOCUTOR_CONSENTIMIENTO";
    public const string ListaTipo = "TIPO_CONSENTIMIENTO";
    public const string ListaCanal = "CANAL_CONSENTIMIENTO";
    public const string PrefijoTipo = "cons_t";
    public const string CampoCanal = "cons_canal";
    public const string CampoComentario = "cons_comentario";
    public const string Otorgar = "Otorgar";
    public const string Revocar = "Revocar";

    public static bool EsCampoTipo(string campo) => campo.StartsWith(PrefijoTipo, StringComparison.Ordinal) && long.TryParse(campo[PrefijoTipo.Length..], out _);
    public static bool EsCampo(string campo) => EsCampoTipo(campo) || campo is CampoCanal or CampoComentario;

    /// <summary>Valida una solicitud. Devuelve el primer error o null.</summary>
    public static async Task<(string? Error, int Estado, string? Codigo)> ValidarAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor, IReadOnlyList<ConsentimientoCambioDto>? cambios, long idCanal, string? comentario)
    {
        var cliente = await c.QuerySingleOrDefaultAsync(
            "SELECT estado, tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania", new { idInterlocutor, idCompania }, tx);
        if (cliente is null) return ("No existe el cliente indicado.", 404, "no_encontrado");
        if ((string)cliente.estado == "Fusionado") return ("El cliente fue fusionado en otro; los consentimientos se gestionan sobre el cliente que se conservó.", 409, "cliente_fusionado");
        if ((string)cliente.estado == "Fallecido") return ("El cliente está registrado como fallecido y no admite cambios de consentimientos.", 409, "cliente_fallecido");
        if ((string)cliente.tipo_persona is not ("Fisica" or "Juridica")) return ("Los consentimientos aplican a personas y organizaciones, no a grupos.", 400, "datos_invalidos");

        if (cambios is null || cambios.Count == 0) return ("Indique al menos un consentimiento a otorgar o revocar.", 400, "datos_invalidos");
        if (cambios.Select(x => x.IdTipoConsentimiento).Distinct().Count() != cambios.Count) return ("Un consentimiento no puede repetirse en la misma solicitud.", 400, "datos_invalidos");
        if ((comentario ?? "").Trim().Length > 500) return ("El comentario no puede superar 500 caracteres.", 400, "datos_invalidos");

        var canal = await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE i.id_lista_valores_item = @idCanal AND t.codigo = @lista AND i.estado = 'Activo'
            """, new { idCanal, lista = ListaCanal }, tx);
        if (canal == 0) return ("Indique el canal por el que se recibió el consentimiento.", 400, "datos_invalidos");

        foreach (var cambio in cambios)
        {
            var tipo = await c.QuerySingleOrDefaultAsync(
                """
                SELECT i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE i.id_lista_valores_item = @id AND t.codigo = @lista AND i.estado = 'Activo'
                """, new { id = cambio.IdTipoConsentimiento, lista = ListaTipo }, tx);
            if (tipo is null) return ("Un tipo de consentimiento no existe o está inactivo.", 400, "datos_invalidos");
            var vigente = await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_CONSENTIMIENTO WHERE id_interlocutor = @idInterlocutor AND id_tipo_consentimiento = @t AND estado = 'Vigente') THEN 1 ELSE 0 END",
                new { idInterlocutor, t = cambio.IdTipoConsentimiento }, tx);
            if (cambio.Otorgar && vigente) return ($"El cliente ya tiene otorgado el consentimiento «{(string)tipo.nombre}».", 409, "consentimiento_vigente");
            if (!cambio.Otorgar && !vigente) return ($"El cliente no tiene vigente el consentimiento «{(string)tipo.nombre}»; no se puede revocar.", 409, "consentimiento_inexistente");
        }
        return (null, 200, null);
    }

    public static async Task<ConsentimientosClienteResponse?> ObtenerClienteAsync(SqlConnection c, long idCompania, long idInterlocutor)
    {
        var cli = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.numero, i.tipo_persona, i.estado,
                   COALESCE(j.nombre_org1, g.nombre_grupo1, LTRIM(ISNULL(f.nombre1, N'') + N' ' + ISNULL(f.apellido1, N''))) AS nombre
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_GRUPO g ON g.id_interlocutor = i.id_interlocutor
            WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania
            """, new { idInterlocutor, idCompania });
        if (cli is null) return null;

        var lista = (await c.QueryAsync(
            """
            SELECT k.id_consentimiento, k.id_tipo_consentimiento, t.nombre AS tipo, k.estado, k.fecha_otorgamiento, ca.nombre AS canal, k.comentario, k.otorgado_por,
                   k.id_gestion_otorgamiento, k.fecha_revocacion, cr.nombre AS canal_revocacion, k.comentario_revocacion, k.revocado_por, k.id_gestion_revocacion
            FROM cli.INTERLOCUTOR_CONSENTIMIENTO k
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = k.id_tipo_consentimiento
            JOIN gen.LISTA_VALORES_ITEM ca ON ca.id_lista_valores_item = k.id_canal
            LEFT JOIN gen.LISTA_VALORES_ITEM cr ON cr.id_lista_valores_item = k.id_canal_revocacion
            WHERE k.id_interlocutor = @idInterlocutor ORDER BY k.fecha_otorgamiento DESC, k.id_consentimiento DESC
            """, new { idInterlocutor })).Select(x => new ConsentimientoResponse(
                (long)x.id_consentimiento, (long)x.id_tipo_consentimiento, (string)x.tipo, (string)x.estado, (DateTime)x.fecha_otorgamiento, (string)x.canal,
                (string?)x.comentario, (string)x.otorgado_por, (long?)x.id_gestion_otorgamiento, (DateTime?)x.fecha_revocacion, (string?)x.canal_revocacion,
                (string?)x.comentario_revocacion, (string?)x.revocado_por, (long?)x.id_gestion_revocacion)).ToList();

        var pend = await c.QuerySingleOrDefaultAsync(
            """
            SELECT TOP 1 g.id_gestion, g.numero, u.nombre_completo AS solicitante FROM apr.GESTION g
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            WHERE g.id_compania = @idCompania AND g.entidad_negocio = @entidad AND g.llave_registro = @llave AND g.estado = N'Pendiente de Aprobacion'
            """, new { idCompania, entidad = Entidad, llave = idInterlocutor.ToString(CultureInfo.InvariantCulture) });

        return new ConsentimientosClienteResponse(idInterlocutor, (string)cli.numero, ((string?)cli.nombre ?? "").Trim(), (string)cli.tipo_persona, (string)cli.estado, lista,
            pend is null ? null : new ConsentimientoPendienteResponse((long)pend.id_gestion, (string)pend.numero, (string)pend.solicitante));
    }

    /// <summary>Consentimientos vigentes del cliente (para la ficha y la API).</summary>
    public static async Task<List<string>> VigentesAsync(SqlConnection c, long idInterlocutor)
        => (await c.QueryAsync<string>(
            """
            SELECT t.nombre FROM cli.INTERLOCUTOR_CONSENTIMIENTO k JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = k.id_tipo_consentimiento
            WHERE k.id_interlocutor = @idInterlocutor AND k.estado = 'Vigente' ORDER BY t.orden, t.nombre
            """, new { idInterlocutor })).ToList();

    // ----- Aplicar (al aprobar la gestion) -----------------------------------------------------------------------------

    public static async Task<ResultadoOperacion?> AplicarAsync(
        SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string llave, string solicitadoPor)
    {
        if (!long.TryParse(llave, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idInterlocutor))
            return ResultadoOperacion.Fallo(500, "llave_invalida", "La gestion de consentimientos tiene una llave no valida.");

        var det = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx))
            .ToDictionary(x => (string)x.nombre_campo, x => (string?)x.valor_nuevo);
        var cambios = det.Where(d => d.Key.StartsWith(PrefijoTipo, StringComparison.Ordinal))
            .Select(d => new ConsentimientoCambioDto(long.Parse(d.Key[PrefijoTipo.Length..], CultureInfo.InvariantCulture), d.Value == Otorgar)).ToList();
        if (!long.TryParse(det.GetValueOrDefault(CampoCanal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idCanal))
            return ResultadoOperacion.Fallo(500, "canal_invalido", "La gestion de consentimientos no indica el canal.");
        var comentario = det.GetValueOrDefault(CampoComentario);

        // Se revalida al aprobar: entre la solicitud y la aprobacion el estado de los consentimientos pudo cambiar.
        var (error, estado, codigo) = await ValidarAsync(c, tx, actor.IdCompania, idInterlocutor, cambios, idCanal, comentario);
        if (error is not null) return ResultadoOperacion.Fallo(estado == 404 ? 404 : 409, codigo ?? "consentimiento_no_valido", error);

        var nombres = (await c.QueryAsync("SELECT id_lista_valores_item AS id, nombre FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_item IN @ids",
            new { ids = cambios.Select(x => x.IdTipoConsentimiento).Append(idCanal).ToList() }, tx)).ToDictionary(x => (long)x.id, x => (string)x.nombre);
        var nombreCanal = nombres.GetValueOrDefault(idCanal, "?");

        foreach (var cambio in cambios)
        {
            var nombre = nombres.GetValueOrDefault(cambio.IdTipoConsentimiento, "?");
            if (cambio.Otorgar)
            {
                await c.ExecuteAsync(
                    """
                    INSERT INTO cli.INTERLOCUTOR_CONSENTIMIENTO (id_interlocutor, id_tipo_consentimiento, estado, id_canal, comentario, otorgado_por, id_gestion_otorgamiento)
                    VALUES (@idInterlocutor, @t, 'Vigente', @idCanal, @comentario, @por, @idGestion)
                    """, new { idInterlocutor, t = cambio.IdTipoConsentimiento, idCanal, comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(), por = solicitadoPor, idGestion }, tx);
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, $"Consentimiento «{nombre}»", "Sin consentimiento", $"Otorgado por {nombreCanal}", idGestion);
            }
            else
            {
                await c.ExecuteAsync(
                    """
                    UPDATE cli.INTERLOCUTOR_CONSENTIMIENTO SET estado = 'Revocado', fecha_revocacion = SYSUTCDATETIME(), id_canal_revocacion = @idCanal,
                           comentario_revocacion = @comentario, revocado_por = @por, id_gestion_revocacion = @idGestion
                    WHERE id_interlocutor = @idInterlocutor AND id_tipo_consentimiento = @t AND estado = 'Vigente'
                    """, new { idInterlocutor, t = cambio.IdTipoConsentimiento, idCanal, comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(), por = solicitadoPor, idGestion }, tx);
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, $"Consentimiento «{nombre}»", "Otorgado", $"Revocado por {nombreCanal}", idGestion);
            }
        }
        return null;
    }
}
