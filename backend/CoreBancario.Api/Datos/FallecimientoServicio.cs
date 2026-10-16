using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Inactivacion por fallecimiento (solo personas fisicas). Es una gestion CON aprobacion obligatoria (CLI_FALLECIMIENTO), en un carril propio:
/// entidad INTERLOCUTOR_FALLECIMIENTO, llave = id del cliente (una pendiente por cliente). Detalle: fall_fecha (yyyy-MM-dd) y fall_comentario.
/// El respaldo es obligatorio: al menos un adjunto del tipo «Acta de defunción» (X-Adjuntos), que al aprobarse queda como evidencia de la gestion.
/// Es IRREVERSIBLE: no existe reactivar. Al aprobarse el cliente pasa a Fallecido con su fecha, se cierran sus membresias de grupo y
/// las relaciones, bloqueos y consentimientos vigentes quedan como historial; ya no admite cambios, roles, relaciones, bloqueos, consentimientos
/// ni fusion. Sus documentos siguen disponibles (la sucesion los necesita).
/// </summary>
public static class FallecimientoServicio
{
    public const string Entidad = "INTERLOCUTOR_FALLECIMIENTO";
    public const string TipoGestion = "CLI_FALLECIMIENTO";
    public const string CodigoTipoActa = "ACTA_DEFUNCION";
    public const string CampoFecha = "fall_fecha";
    public const string CampoComentario = "fall_comentario";

    public static bool EsCampo(string campo) => campo is CampoFecha or CampoComentario;

    private sealed record Membresia(long IdRelacion, long IdGrupo, string NumeroGrupo, string NombreGrupo, int Miembros);

    private static async Task<List<Membresia>> MembresiasAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor)
        => (await c.QueryAsync(
            """
            SELECT r.id_interlocutor_relacion, r.id_interlocutor_destino AS id_grupo, g.numero, ISNULL(ig.nombre_grupo1, g.numero) AS nombre,
                   (SELECT COUNT(*) FROM cli.INTERLOCUTOR_RELACION x WHERE x.id_interlocutor_destino = r.id_interlocutor_destino
                       AND x.id_tipo_relacion = r.id_tipo_relacion AND x.fecha_fin IS NULL) AS miembros
            FROM cli.INTERLOCUTOR_RELACION r
            JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_relacion AND it.codigo = N'MIEMBRO_GRUPO'
            JOIN cli.INTERLOCUTOR g ON g.id_interlocutor = r.id_interlocutor_destino
            LEFT JOIN cli.INTERLOCUTOR_GRUPO ig ON ig.id_interlocutor = g.id_interlocutor
            WHERE r.id_interlocutor_origen = @idInterlocutor AND r.fecha_fin IS NULL AND g.estado = N'Activo'
            """, new { idInterlocutor }, tx))
            .Select(x => new Membresia((long)x.id_interlocutor_relacion, (long)x.id_grupo, (string)x.numero, (string)x.nombre, (int)x.miembros)).ToList();

    /// <summary>
    /// Valida la solicitud (y se vuelve a validar al aprobar). `exigirActaTemporal`: al solicitar el acta viaja como temporal en X-Adjuntos;
    /// al aprobar ya esta ligada a la gestion (`idGestion`). Devuelve el primer error o null.
    /// </summary>
    public static async Task<(string? Error, int Estado, string? Codigo)> ValidarAsync(
        SqlConnection c, SqlTransaction? tx, ActorContext actor, long idInterlocutor, DateOnly? fecha, string? comentario, long? idGestion)
    {
        var cli = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.numero, i.tipo_persona, i.estado, f.fecha_nacimiento
            FROM cli.INTERLOCUTOR i LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania
            """, new { idInterlocutor, actor.IdCompania }, tx);
        if (cli is null) return ("No existe el cliente indicado.", 404, "no_encontrado");
        if ((string)cli.tipo_persona != "Fisica") return ("El fallecimiento aplica solo a personas físicas.", 409, "tipo_no_aplica");
        if ((string)cli.estado == "Fallecido") return ($"El cliente N.º {(string)cli.numero} ya está registrado como fallecido.", 409, "cliente_fallecido");
        if ((string)cli.estado != "Activo") return ($"El cliente N.º {(string)cli.numero} está '{(string)cli.estado}' y no admite este cambio.", 409, "cliente_no_activo");

        if (fecha is null) return ("Indique la fecha de fallecimiento.", 400, "datos_invalidos");
        var hoy = await KycVigenciaServicio.HoyAsync(c, tx, actor.IdCompania);
        if (fecha.Value > hoy) return ("La fecha de fallecimiento no puede ser futura.", 400, "datos_invalidos");
        if (cli.fecha_nacimiento is not null && fecha.Value < DateOnly.FromDateTime((DateTime)cli.fecha_nacimiento))
            return ("La fecha de fallecimiento no puede ser anterior a la fecha de nacimiento.", 400, "datos_invalidos");
        if ((comentario ?? "").Trim().Length > 500) return ("La observación admite hasta 500 caracteres.", 400, "datos_invalidos");

        // Una membresia de grupo se cierra; si el grupo quedaria con menos de 2 miembros, primero hay que resolver el grupo.
        foreach (var m in await MembresiasAsync(c, tx, idInterlocutor))
            if (m.Miembros - 1 < GrupoServicio.MinimoMiembros)
                return ($"El cliente es miembro del grupo «{m.NombreGrupo}» (N.º {m.NumeroGrupo}), que quedaría con menos de {GrupoServicio.MinimoMiembros} miembros. Sustituya al miembro o disuelva el grupo antes.", 409, "grupo_minimo_miembros");

        // Acta de defuncion (respaldo obligatorio).
        var actas = idGestion is null
            ? await c.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM doc.DOCUMENTO d JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
                WHERE d.id_documento IN @ids AND d.estado = 'Temporal' AND d.id_compania = @idCompania AND d.id_usuario_creador = @idUsuario AND t.codigo = @codigo
                """, new { ids = actor.AdjuntosTemporales.DefaultIfEmpty(0L).ToList(), actor.IdCompania, actor.IdUsuario, codigo = CodigoTipoActa }, tx)
            : await c.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM doc.DOCUMENTO d JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
                WHERE d.id_gestion = @idGestion AND d.estado = 'EnGestion' AND t.codigo = @codigo
                """, new { idGestion, codigo = CodigoTipoActa }, tx);
        if (actas == 0) return ("Adjunte el acta de defunción (tipo «Acta de defunción»); es el respaldo obligatorio del fallecimiento.", 400, "acta_requerida");
        return (null, 200, null);
    }

    public static async Task<ResultadoOperacion?> AplicarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string llave, string solicitadoPor)
    {
        if (!long.TryParse(llave, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idInterlocutor))
            return ResultadoOperacion.Fallo(500, "llave_invalida", "La gestión de fallecimiento tiene una llave no válida.");
        var det = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx))
            .ToDictionary(x => (string)x.nombre_campo, x => (string?)x.valor_nuevo);
        if (!DateOnly.TryParseExact(det.GetValueOrDefault(CampoFecha), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            return ResultadoOperacion.Fallo(500, "fecha_invalida", "La gestión de fallecimiento no tiene una fecha válida.");
        var comentario = det.GetValueOrDefault(CampoComentario);

        // Se revalida: entre la solicitud y la aprobacion el cliente pudo cambiar (grupos, estado).
        var (error, estado, codigo) = await ValidarAsync(c, tx, actor, idInterlocutor, fecha, comentario, idGestion);
        if (error is not null) return ResultadoOperacion.Fallo(estado == 404 ? 404 : 409, codigo ?? "fallecimiento_no_valido", error);

        foreach (var m in await MembresiasAsync(c, tx, idInterlocutor))
        {
            await c.ExecuteAsync(
                "UPDATE cli.INTERLOCUTOR_RELACION SET fecha_fin = SYSUTCDATETIME(), cerrado_por = @por, id_gestion_baja = @idGestion WHERE id_interlocutor_relacion = @id",
                new { id = m.IdRelacion, por = solicitadoPor, idGestion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, $"Miembro del grupo «{m.NombreGrupo}» (N.º {m.NumeroGrupo})", "Vigente", "Cerrado por fallecimiento", idGestion);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", m.IdGrupo, "Miembros", $"{m.Miembros}", $"{m.Miembros - 1} (fallecimiento de un miembro)", idGestion);
        }

        await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR SET estado = 'Fallecido', fecha_fallecimiento = @fecha, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @id AND id_compania = @idCompania",
            new { fecha = fecha.ToDateTime(TimeOnly.MinValue), por = solicitadoPor, id = idInterlocutor, actor.IdCompania }, tx);
        await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, "Estado", "Activo", "Fallecido", idGestion);
        await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, "Fecha de fallecimiento", null, fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), idGestion);
        return null;
    }

    public static async Task<FallecimientoInfoResponse?> InfoAsync(SqlConnection c, long idCompania, long idInterlocutor)
    {
        var x = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.estado, i.fecha_fallecimiento,
                   (SELECT TOP 1 g.numero FROM apr.GESTION g WHERE g.entidad_negocio = @entidad AND g.llave_registro = CONVERT(nvarchar(100), i.id_interlocutor)
                        AND g.estado = 'Aplicada' ORDER BY g.id_gestion DESC) AS numero_gestion
            FROM cli.INTERLOCUTOR i WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania
            """, new { entidad = Entidad, idInterlocutor, idCompania });
        if (x is null || (string)x.estado != "Fallecido") return null;
        return new FallecimientoInfoResponse(x.fecha_fallecimiento is null ? null : DateOnly.FromDateTime((DateTime)x.fecha_fallecimiento), (string?)x.numero_gestion);
    }
}
