using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Bloqueo de clientes: el estado del cliente (Activo, Inactivo, Fusionado) es una cosa y estar «bloqueado» otra. Un cliente puede tener varios
/// bloqueos vigentes a la vez (uno por tipo: KYC, Cumplimiento, Fraude, Ente externo...) y esta bloqueado mientras tenga al menos uno.
/// Bloquear y desbloquear se piden por gestion con aprobacion, en un carril propio (entidad INTERLOCUTOR_BLOQUEO, llave «idCliente|idTipo»):
/// no choca con las gestiones de datos ni de KYC. El historial queda en cli.INTERLOCUTOR_BLOQUEO (Vigente / Levantado).
/// Detalle de la gestion: bloq_accion (Bloquear | Desbloquear), bloq_tipo (id del tipo) y bloq_comentario.
/// </summary>
public static class BloqueoServicio
{
    public const string Entidad = "INTERLOCUTOR_BLOQUEO";
    public const string ListaTipo = "TIPO_BLOQUEO";
    public const string CampoAccion = "bloq_accion";
    public const string CampoTipo = "bloq_tipo";
    public const string CampoComentario = "bloq_comentario";
    public const string Bloquear = "Bloquear";
    public const string Desbloquear = "Desbloquear";
    public const string EstadoVigente = "Vigente";
    public const string EstadoLevantado = "Levantado";

    public static string Llave(long idInterlocutor, long idTipo)
        => idInterlocutor.ToString(CultureInfo.InvariantCulture) + "|" + idTipo.ToString(CultureInfo.InvariantCulture);

    /// <summary>Nombres de los tipos de bloqueo vigentes del cliente (vacio = no esta bloqueado).</summary>
    public static async Task<List<string>> TiposVigentesAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor)
        => (await c.QueryAsync<string>(
            """
            SELECT it.nombre FROM cli.INTERLOCUTOR_BLOQUEO b JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = b.id_tipo_bloqueo
            WHERE b.id_interlocutor = @idInterlocutor AND b.estado = 'Vigente' ORDER BY b.fecha_bloqueo
            """, new { idInterlocutor }, tx)).ToList();

    public static async Task<IReadOnlyList<BloqueoVigenteResponse>> VigentesAsync(SqlConnection c, long idInterlocutor)
        => (await c.QueryAsync(
            """
            SELECT b.id_tipo_bloqueo, it.codigo, it.nombre, b.fecha_bloqueo FROM cli.INTERLOCUTOR_BLOQUEO b
            JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = b.id_tipo_bloqueo
            WHERE b.id_interlocutor = @idInterlocutor AND b.estado = 'Vigente' ORDER BY b.fecha_bloqueo
            """, new { idInterlocutor })).Select(x => new BloqueoVigenteResponse((long)x.id_tipo_bloqueo, (string)x.codigo, (string)x.nombre, (DateTime)x.fecha_bloqueo)).ToList();

    /// <summary>Valida una solicitud de bloqueo o desbloqueo. Devuelve el nombre del tipo (para el resumen) o el primer error.</summary>
    public static async Task<(string? Error, int Estado, string? Codigo, string? NombreTipo)> ValidarAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor, long idTipo, string? comentario, bool bloquear)
    {
        var cliente = await c.QuerySingleOrDefaultAsync(
            "SELECT estado FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania", new { idInterlocutor, idCompania }, tx);
        if (cliente is null) return ("No existe el cliente indicado.", 404, "no_encontrado", null);
        if ((string)cliente.estado == "Fusionado") return ("El cliente fue fusionado en otro; los bloqueos se gestionan sobre el cliente que se conservó.", 409, "cliente_fusionado", null);
        if ((string)cliente.estado == "Fallecido") return ("El cliente está registrado como fallecido y no admite nuevos bloqueos ni desbloqueos.", 409, "cliente_fallecido", null);

        var texto = (comentario ?? "").Trim();
        if (texto.Length < 10) return ("Indique el comentario del " + (bloquear ? "bloqueo" : "desbloqueo") + " (mínimo 10 caracteres).", 400, "datos_invalidos", null);
        if (texto.Length > 500) return ("El comentario no puede superar 500 caracteres.", 400, "datos_invalidos", null);

        var tipo = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE i.id_lista_valores_item = @idTipo AND t.codigo = @lista AND i.estado = 'Activo'
            """, new { idTipo, lista = ListaTipo }, tx);
        if (tipo is null) return ("El tipo de bloqueo no existe o está inactivo.", 400, "datos_invalidos", null);

        var vigente = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_BLOQUEO WHERE id_interlocutor = @idInterlocutor AND id_tipo_bloqueo = @idTipo AND estado = 'Vigente') THEN 1 ELSE 0 END",
            new { idInterlocutor, idTipo }, tx);
        if (bloquear && vigente) return ($"El cliente ya tiene un bloqueo vigente de tipo «{(string)tipo.nombre}».", 409, "bloqueo_vigente", null);
        if (!bloquear && !vigente) return ($"El cliente no tiene un bloqueo vigente de tipo «{(string)tipo.nombre}».", 409, "bloqueo_inexistente", null);
        return (null, 200, null, (string)tipo.nombre);
    }

    public static async Task<BloqueosClienteResponse?> ObtenerClienteAsync(SqlConnection c, long idCompania, long idInterlocutor)
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

        var bloqueos = (await c.QueryAsync(
            """
            SELECT b.id_bloqueo, b.id_tipo_bloqueo, it.codigo, it.nombre, b.comentario, b.estado, b.fecha_bloqueo, b.bloqueado_por, b.id_gestion_bloqueo,
                   b.fecha_levantamiento, b.comentario_levantamiento, b.levantado_por, b.id_gestion_levantamiento
            FROM cli.INTERLOCUTOR_BLOQUEO b JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = b.id_tipo_bloqueo
            WHERE b.id_interlocutor = @idInterlocutor ORDER BY b.fecha_bloqueo DESC, b.id_bloqueo DESC
            """, new { idInterlocutor })).Select(x => new BloqueoResponse(
                (long)x.id_bloqueo, (long)x.id_tipo_bloqueo, (string)x.codigo, (string)x.nombre, (string)x.comentario, (string)x.estado,
                (DateTime)x.fecha_bloqueo, (string)x.bloqueado_por, (long?)x.id_gestion_bloqueo,
                (DateTime?)x.fecha_levantamiento, (string?)x.comentario_levantamiento, (string?)x.levantado_por, (long?)x.id_gestion_levantamiento)).ToList();

        var pendientes = (await c.QueryAsync(
            """
            SELECT g.id_gestion, g.numero, u.nombre_completo AS solicitante, g.llave_registro,
                   MAX(CASE WHEN d.nombre_campo = @cAccion THEN d.valor_nuevo END) AS accion
            FROM apr.GESTION g
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            JOIN apr.GESTION_DETALLE d ON d.id_gestion = g.id_gestion
            WHERE g.id_compania = @idCompania AND g.entidad_negocio = @entidad AND g.estado = N'Pendiente de Aprobacion'
              AND g.llave_registro LIKE @prefijo
            GROUP BY g.id_gestion, g.numero, u.nombre_completo, g.llave_registro
            """, new
            {
                idCompania, entidad = Entidad, cAccion = CampoAccion,
                prefijo = idInterlocutor.ToString(CultureInfo.InvariantCulture) + "|%"
            })).ToList();
        var nombresTipo = (await c.QueryAsync("SELECT id_lista_valores_item AS id, nombre FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_item IN @ids",
            new { ids = pendientes.Select(p => long.Parse(((string)p.llave_registro).Split('|')[1], CultureInfo.InvariantCulture)).DefaultIfEmpty(0).ToList() }))
            .ToDictionary(x => (long)x.id, x => (string)x.nombre);
        var listaPend = pendientes.Select(p =>
        {
            var idTipo = long.Parse(((string)p.llave_registro).Split('|')[1], CultureInfo.InvariantCulture);
            return new BloqueoPendienteResponse((long)p.id_gestion, (string)p.numero, (string?)p.accion ?? Bloquear, idTipo, nombresTipo.GetValueOrDefault(idTipo, "?"), (string)p.solicitante);
        }).ToList();

        return new BloqueosClienteResponse(idInterlocutor, (string)cli.numero, ((string?)cli.nombre ?? "").Trim(), (string)cli.tipo_persona, (string)cli.estado,
            bloqueos.Any(b => b.Estado == EstadoVigente), bloqueos, listaPend);
    }

    public static async Task<IReadOnlyList<BloqueoListadoItemResponse>> ListarVigentesAsync(SqlConnection c, long idCompania)
        => (await c.QueryAsync(
            """
            SELECT b.id_bloqueo, i.id_interlocutor, i.numero, i.tipo_persona, b.id_tipo_bloqueo, it.nombre AS tipo, b.comentario, b.fecha_bloqueo, b.bloqueado_por,
                   COALESCE(j.nombre_org1, g.nombre_grupo1, LTRIM(ISNULL(f.nombre1, N'') + N' ' + ISNULL(f.apellido1, N''))) AS nombre
            FROM cli.INTERLOCUTOR_BLOQUEO b
            JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = b.id_interlocutor
            JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = b.id_tipo_bloqueo
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_GRUPO g ON g.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania AND b.estado = 'Vigente' ORDER BY b.fecha_bloqueo DESC
            """, new { idCompania })).Select(x => new BloqueoListadoItemResponse(
                (long)x.id_bloqueo, (long)x.id_interlocutor, (string)x.numero, ((string?)x.nombre ?? "").Trim(), (string)x.tipo_persona, (long)x.id_tipo_bloqueo,
                (string)x.tipo, (string)x.comentario, (DateTime)x.fecha_bloqueo, (string)x.bloqueado_por)).ToList();

    // ----- Aplicar (al aprobar la gestion) -----------------------------------------------------------------------------

    public static async Task<ResultadoOperacion?> AplicarAsync(
        SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string llave, string solicitadoPor)
    {
        var partes = llave.Split('|');
        if (partes.Length != 2 || !long.TryParse(partes[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idInterlocutor)
            || !long.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idTipo))
            return ResultadoOperacion.Fallo(500, "llave_invalida", "La gestion de bloqueo tiene una llave no valida.");

        var det = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx))
            .ToDictionary(x => (string)x.nombre_campo, x => (string?)x.valor_nuevo);
        var accion = det.GetValueOrDefault(CampoAccion);
        var comentario = det.GetValueOrDefault(CampoComentario) ?? "";
        var bloquear = accion == Bloquear;
        if (!bloquear && accion != Desbloquear)
            return ResultadoOperacion.Fallo(500, "accion_invalida", "La gestion de bloqueo no indica si bloquea o desbloquea.");

        // Se revalida al aprobar: entre la solicitud y la aprobacion el bloqueo pudo cambiar o el cliente fusionarse.
        var (error, estado, codigo, nombreTipo) = await ValidarAsync(c, tx, actor.IdCompania, idInterlocutor, idTipo, comentario, bloquear);
        if (error is not null) return ResultadoOperacion.Fallo(estado == 404 ? 404 : 409, codigo ?? "bloqueo_no_valido", error);
        nombreTipo ??= "?";

        if (bloquear)
        {
            await c.ExecuteAsync(
                """
                INSERT INTO cli.INTERLOCUTOR_BLOQUEO (id_interlocutor, id_tipo_bloqueo, comentario, estado, bloqueado_por, id_gestion_bloqueo)
                VALUES (@idInterlocutor, @idTipo, @comentario, 'Vigente', @por, @idGestion)
                """, new { idInterlocutor, idTipo, comentario, por = solicitadoPor, idGestion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, "Bloqueo", null, $"Bloqueado · {nombreTipo}: {comentario}", idGestion);
        }
        else
        {
            await c.ExecuteAsync(
                """
                UPDATE cli.INTERLOCUTOR_BLOQUEO SET estado = 'Levantado', fecha_levantamiento = SYSUTCDATETIME(), comentario_levantamiento = @comentario,
                       levantado_por = @por, id_gestion_levantamiento = @idGestion
                WHERE id_interlocutor = @idInterlocutor AND id_tipo_bloqueo = @idTipo AND estado = 'Vigente'
                """, new { idInterlocutor, idTipo, comentario, por = solicitadoPor, idGestion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, "Bloqueo", $"Bloqueado · {nombreTipo}", $"Desbloqueado: {comentario}", idGestion);
        }
        return null;
    }
}
