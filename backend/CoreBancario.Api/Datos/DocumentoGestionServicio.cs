using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Documentos del expediente como gestion (CLI_DOCUMENTO, entidad INTERLOCUTOR_DOCUMENTO, llave = id del cliente).
/// Una gestion agrupa las operaciones de una carga: agregar documentos (Alta), reemplazar uno vigente por una version nueva (Reemplazo)
/// y retirar uno vigente (Obsoleto). Los archivos nuevos viven como «pendientes de aprobacion» (EnGestion) hasta que se resuelve:
/// si la regla de aprobacion es automatica se aplican de inmediato; si no, quedan esperando al aprobador. Al rechazarse se descartan.
/// Detalle de la gestion: docalta[idDocumentoTemporal], docrep[idDocumentoDestino], docobs[idDocumentoDestino].
/// </summary>
public static class DocumentoGestionServicio
{
    public const string TipoGestion = "CLI_DOCUMENTO";
    public const string Entidad = "INTERLOCUTOR_DOCUMENTO";
    public const string PrefijoAlta = "docalta[", PrefijoReemplazo = "docrep[", PrefijoObsoleto = "docobs[";
    private static readonly Regex PatronId = new(@"^doc(?:alta|rep|obs)\[(\d+)\]$", RegexOptions.Compiled);

    public static bool EsCampo(string campo) => PatronId.IsMatch(campo);
    public static long? IdDeCampo(string campo) => PatronId.Match(campo) is { Success: true } m ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    public static string EtiquetaCampo(string campo)
        => campo.StartsWith(PrefijoAlta, StringComparison.Ordinal) ? "Agregar documento"
         : campo.StartsWith(PrefijoReemplazo, StringComparison.Ordinal) ? "Reemplazar documento (nueva versión)"
         : "Retirar documento (obsoleto)";

    /// <summary>Operacion ya validada, lista para escribirse en el detalle de la gestion.</summary>
    public sealed record OperacionValida(string Tipo, long IdDocumento, long? IdDestino, string Campo, string? Anterior, string Nuevo, string? Motivo, string TipoNombre, string Archivo);

    public sealed record Validacion(ResultadoOperacion? Error, IReadOnlyList<OperacionValida> Operaciones, string? NumeroCliente);

    private static ResultadoOperacion F(int estado, string codigo, string mensaje) => ResultadoOperacion.Fallo(estado, codigo, mensaje);

    /// <summary>
    /// Valida la solicitud completa antes de crear la gestion. Los archivos nuevos deben haberse subido como temporales (X-Adjuntos) y
    /// coincidir exactamente con las operaciones de Alta y Reemplazo.
    /// </summary>
    public static async Task<Validacion> ValidarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idCliente, IReadOnlyList<DocumentoOperacionRequest> ops)
    {
        Validacion Falla(ResultadoOperacion e) => new(e, [], null);
        if (ops is null || ops.Count == 0) return Falla(F(400, "operaciones_requeridas", "Indique al menos un documento."));
        if (ops.Count > 50) return Falla(F(400, "demasiadas_operaciones", "Una gestion admite hasta 50 documentos."));

        var cli = await c.QuerySingleOrDefaultAsync(
            "SELECT numero, tipo_persona, estado FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idCliente AND id_compania = @idCompania",
            new { idCliente, actor.IdCompania }, tx);
        if (cli is null) return Falla(F(404, "cliente_no_encontrado", "No existe el cliente."));
        if ((string)cli.estado == "Fusionado") return Falla(F(409, "cliente_fusionado", "El cliente fue fusionado; sus documentos están en el cliente que se conserva."));
        var tipoPersona = (string)cli.tipo_persona;

        // Permiso por tipo de operacion
        foreach (var op in ops)
        {
            var permiso = op.Tipo switch { "Alta" => DocumentoRepositorio.PermisoSubir, "Reemplazo" => DocumentoRepositorio.PermisoReemplazar, "Obsoleto" => DocumentoRepositorio.PermisoObsoletar, _ => null };
            if (permiso is null) return Falla(F(400, "operacion_invalida", $"Operación desconocida: {op.Tipo}."));
            if (!actor.TienePermiso(permiso)) return Falla(F(403, "permiso_insuficiente", "No tiene permiso para esta operación sobre documentos."));
        }

        // Los temporales enviados deben ser exactamente los de las operaciones con archivo.
        var idsConArchivo = ops.Where(o => o.Tipo != "Obsoleto").Select(o => o.IdDocumento ?? 0).ToList();
        if (idsConArchivo.Any(i => i <= 0) || idsConArchivo.Distinct().Count() != idsConArchivo.Count
            || !idsConArchivo.OrderBy(x => x).SequenceEqual(actor.AdjuntosTemporales.OrderBy(x => x)))
            return Falla(F(400, "adjuntos_inconsistentes", "Los archivos enviados no coinciden con los documentos de la solicitud."));

        var temporales = idsConArchivo.Count == 0 ? new Dictionary<long, dynamic>() : (await c.QueryAsync(
            """
            SELECT d.id_documento, d.id_tipo_documento, d.estado, d.id_usuario_creador, t.nombre AS tipo, t.cardinalidad, t.maximo, t.aplica_fisica, t.aplica_juridica, t.aplica_grupo,
                   ca.es_sistema, v.nombre_archivo
              FROM doc.DOCUMENTO d
              JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
              JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta
              JOIN doc.DOCUMENTO_VERSION v ON v.id_documento = d.id_documento AND v.es_vigente = 1
             WHERE d.id_documento IN @ids AND d.id_compania = @idCompania
            """, new { ids = idsConArchivo, actor.IdCompania }, tx)).ToDictionary(x => (long)x.id_documento, x => x);

        var validas = new List<OperacionValida>();
        var altasPorTipo = new Dictionary<long, int>();
        var destinosUsados = new HashSet<long>();

        foreach (var op in ops)
        {
            var motivo = op.Motivo?.Trim();
            if (op.Tipo is "Reemplazo" or "Obsoleto")
            {
                if (op.IdDocumentoDestino is null) return Falla(F(400, "documento_destino_requerido", "Indique el documento que se reemplaza o se retira."));
                if (string.IsNullOrEmpty(motivo)) return Falla(F(400, "motivo_requerido", op.Tipo == "Reemplazo" ? "Indique el motivo del reemplazo." : "Indique el motivo para dejar el documento obsoleto."));
                if (motivo.Length > 300) return Falla(F(400, "motivo_largo", "El motivo no puede superar 300 caracteres."));
                if (!destinosUsados.Add(op.IdDocumentoDestino.Value)) return Falla(F(400, "documento_repetido", "Un mismo documento no puede tener dos operaciones en la misma gestión."));
            }

            dynamic? tmp = null;
            if (op.Tipo != "Obsoleto")
            {
                if (!temporales.TryGetValue(op.IdDocumento!.Value, out tmp)) return Falla(F(404, "adjunto_no_encontrado", "No se encontró uno de los archivos subidos."));
                if ((string)tmp.estado != "Temporal" || (long?)tmp.id_usuario_creador != actor.IdUsuario)
                    return Falla(F(409, "adjunto_invalido", "Uno de los archivos ya no está disponible; vuelva a subirlo."));
            }

            if (op.Tipo == "Alta")
            {
                var tipoNombre = (string)tmp!.tipo;
                if ((bool)tmp.es_sistema) return Falla(F(400, "tipo_solo_gestion", $"El tipo «{tipoNombre}» solo se adjunta dentro de una gestión."));
                var aplica = tipoPersona switch { "Fisica" => (bool)tmp.aplica_fisica, "Juridica" => (bool)tmp.aplica_juridica, "Grupo" => (bool)tmp.aplica_grupo, _ => false };
                if (!aplica) return Falla(F(400, "tipo_no_aplica", $"El tipo «{tipoNombre}» no aplica a este tipo de cliente."));

                var idTipo = (long)tmp.id_tipo_documento;
                altasPorTipo[idTipo] = altasPorTipo.GetValueOrDefault(idTipo) + 1;
                var vigentes = await c.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM doc.DOCUMENTO WHERE id_interlocutor = @idCliente AND id_tipo_documento = @idTipo AND origen = 'Expediente' AND estado = 'Vigente'", new { idCliente, idTipo }, tx);
                var pendientes = await c.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM doc.DOCUMENTO d JOIN apr.GESTION g ON g.id_gestion = d.id_gestion
                     WHERE d.id_interlocutor = @idCliente AND d.id_tipo_documento = @idTipo AND d.estado = 'EnGestion' AND d.id_documento_destino IS NULL
                       AND g.entidad_negocio = @entidad AND g.estado = @pendiente
                    """, new { idCliente, idTipo, entidad = Entidad, pendiente = GestionRepositorio.EstadoPendiente }, tx);
                var cardinalidad = (string)tmp.cardinalidad;
                if (cardinalidad == "Unico" && vigentes + pendientes + altasPorTipo[idTipo] > 1)
                    return Falla(F(409, "documento_unico_existente", vigentes > 0
                        ? $"El cliente ya tiene un documento vigente de tipo «{tipoNombre}». Use «Reemplazar» para cargar uno nuevo."
                        : $"«{tipoNombre}» admite un solo documento vigente y ya hay otro en espera de aprobación o en esta misma carga."));
                if (tmp.maximo is not null && vigentes + pendientes + altasPorTipo[idTipo] > (int)tmp.maximo)
                    return Falla(F(409, "documentos_tope", $"El tipo «{tipoNombre}» admite como máximo {(int)tmp.maximo} documento(s) vigente(s) por cliente."));

                validas.Add(new OperacionValida("Alta", op.IdDocumento!.Value, null, PrefijoAlta + op.IdDocumento + "]", null, $"{tipoNombre} · {(string)tmp.nombre_archivo}", null, tipoNombre, (string)tmp.nombre_archivo));
                continue;
            }

            // Reemplazo u obsoleto: el destino debe ser un documento vigente del expediente de este cliente y sin otra gestion en curso.
            var destino = await c.QuerySingleOrDefaultAsync(
                """
                SELECT d.id_documento, d.id_tipo_documento, d.origen, d.estado, d.id_interlocutor, t.nombre AS tipo, v.nombre_archivo, v.numero
                  FROM doc.DOCUMENTO d
                  JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
                  JOIN doc.DOCUMENTO_VERSION v ON v.id_documento = d.id_documento AND v.es_vigente = 1
                 WHERE d.id_documento = @id AND d.id_compania = @idCompania
                """, new { id = op.IdDocumentoDestino, actor.IdCompania }, tx);
            if (destino is null) return Falla(F(404, "documento_no_encontrado", "No existe el documento."));
            if ((string)destino.origen != "Expediente") return Falla(F(409, "evidencia_inmutable", "Este documento es evidencia de una gestión y no se puede modificar."));
            if ((string)destino.estado != "Vigente") return Falla(F(409, "documento_no_vigente", "El documento ya está obsoleto; cargue uno nuevo en su lugar."));
            if ((long?)destino.id_interlocutor != idCliente) return Falla(F(409, "documento_otro_cliente", "El documento no pertenece a este cliente."));
            var enCurso = await c.ExecuteScalarAsync<string?>(
                """
                SELECT TOP 1 g.numero FROM apr.GESTION_DETALLE x JOIN apr.GESTION g ON g.id_gestion = x.id_gestion
                 WHERE g.entidad_negocio = @entidad AND g.estado = @pendiente AND g.id_compania = @idCompania
                   AND (x.nombre_campo = @rep OR x.nombre_campo = @obs)
                """, new { entidad = Entidad, pendiente = GestionRepositorio.EstadoPendiente, actor.IdCompania,
                           rep = PrefijoReemplazo + op.IdDocumentoDestino + "]", obs = PrefijoObsoleto + op.IdDocumentoDestino + "]" }, tx);
            if (enCurso is not null)
                return Falla(F(409, "gestion_pendiente", $"Ya existe la gestión {enCurso} pendiente de aprobación sobre este documento; debe resolverse antes."));

            var tipoDestino = (string)destino.tipo;
            var anterior = $"{tipoDestino} · {(string)destino.nombre_archivo} (v{(int)destino.numero})";
            if (op.Tipo == "Reemplazo")
            {
                if ((long)tmp!.id_tipo_documento != (long)destino.id_tipo_documento)
                    return Falla(F(400, "tipo_distinto", "El archivo nuevo debe ser del mismo tipo que el documento que reemplaza."));
                validas.Add(new OperacionValida("Reemplazo", op.IdDocumento!.Value, op.IdDocumentoDestino, PrefijoReemplazo + op.IdDocumentoDestino + "]", anterior,
                    $"{(string)tmp.nombre_archivo} — {motivo}", motivo, tipoDestino, (string)tmp.nombre_archivo));
            }
            else
                validas.Add(new OperacionValida("Obsoleto", 0, op.IdDocumentoDestino, PrefijoObsoleto + op.IdDocumentoDestino + "]", anterior, motivo!, motivo, tipoDestino, (string)destino.nombre_archivo));
        }
        return new Validacion(null, validas, (string)cli.numero);
    }

    /// <summary>Deja los temporales de la solicitud apuntando a su cliente, su destino y su motivo (aun siguen 'Temporal'; el motor los liga a la gestion).</summary>
    public static async Task PrepararTemporalesAsync(SqlConnection c, SqlTransaction tx, long idCliente, IReadOnlyList<OperacionValida> ops)
    {
        foreach (var op in ops.Where(o => o.Tipo != "Obsoleto"))
            await c.ExecuteAsync(
                "UPDATE doc.DOCUMENTO SET id_interlocutor = @idCliente, id_documento_destino = @destino, motivo_solicitud = @motivo WHERE id_documento = @id AND estado = 'Temporal'",
                new { idCliente, destino = op.IdDestino, motivo = op.Motivo, id = op.IdDocumento }, tx);
    }

    /// <summary>Aplica la gestion aprobada: promueve los altas, crea las versiones nuevas y retira los obsoletos. Todo o nada.</summary>
    public static async Task<ResultadoOperacion?> AplicarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, long idLlaveCliente, string porQuien)
    {
        var cli = await c.QuerySingleOrDefaultAsync(
            "SELECT id_interlocutor, estado, id_interlocutor_sobreviviente FROM cli.INTERLOCUTOR WHERE id_interlocutor = @id AND id_compania = @idCompania",
            new { id = idLlaveCliente, actor.IdCompania }, tx);
        if (cli is null) return F(404, "cliente_no_encontrado", "No existe el cliente de la gestión.");
        var idCliente = (string)cli.estado == "Fusionado" && cli.id_interlocutor_sobreviviente is not null ? (long)cli.id_interlocutor_sobreviviente : idLlaveCliente;

        var docs = (await c.QueryAsync(
            """
            SELECT d.id_documento, d.id_tipo_documento, d.id_documento_destino, d.motivo_solicitud, t.nombre AS tipo, t.cardinalidad, t.maximo, v.nombre_archivo
              FROM doc.DOCUMENTO d
              JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
              JOIN doc.DOCUMENTO_VERSION v ON v.id_documento = d.id_documento AND v.es_vigente = 1
             WHERE d.id_gestion = @idGestion AND d.estado = 'EnGestion'
             ORDER BY d.id_documento
            """, new { idGestion }, tx)).ToList();

        foreach (var d in docs)
        {
            var id = (long)d.id_documento;
            var tipo = (string)d.tipo;
            var archivo = (string)d.nombre_archivo;
            if (d.id_documento_destino is null)
            {
                var vigentes = await c.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*) FROM doc.DOCUMENTO WITH (UPDLOCK, HOLDLOCK)
                     WHERE id_interlocutor = @idCliente AND id_tipo_documento = @idTipo AND origen = 'Expediente' AND estado = 'Vigente'
                    """, new { idCliente, idTipo = (long)d.id_tipo_documento }, tx);
                if ((string)d.cardinalidad == "Unico" && vigentes > 0)
                    return F(409, "documento_unico_existente", $"El cliente ya tiene un documento vigente de tipo «{tipo}»; rechace la gestión y use «Reemplazar».");
                if (d.maximo is not null && vigentes >= (int)d.maximo)
                    return F(409, "documentos_tope", $"El tipo «{tipo}» ya alcanzó su máximo de {(int)d.maximo} documento(s) vigente(s).");
                await c.ExecuteAsync(
                    "UPDATE doc.DOCUMENTO SET origen = 'Expediente', estado = 'Vigente', id_interlocutor = @idCliente, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_documento = @id",
                    new { id, idCliente, por = porQuien }, tx);
                await c.ExecuteAsync("UPDATE doc.DOCUMENTO_VERSION SET id_gestion = @idGestion WHERE id_documento = @id", new { id, idGestion }, tx);
                await Auditoria.AltaAsync(c, tx, actor, DocumentoRepositorio.Entidad, id, $"Documento «{tipo}» ({archivo}) agregado al expediente", idGestion);
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idCliente, $"Documento «{tipo}»", "Sin documento", $"Agregado: {archivo}", idGestion);
                continue;
            }

            var dest = (long)d.id_documento_destino;
            var ed = await c.QuerySingleOrDefaultAsync(
                "SELECT origen, estado, id_interlocutor FROM doc.DOCUMENTO WITH (UPDLOCK, ROWLOCK) WHERE id_documento = @dest", new { dest }, tx);
            if (ed is null || (string)ed.origen != "Expediente" || (string)ed.estado != "Vigente" || (long?)ed.id_interlocutor != idCliente)
                return F(409, "documento_destino_no_vigente", $"El documento que se iba a reemplazar ya no está vigente; rechace la gestión y cargue de nuevo.");
            var numero = await c.ExecuteScalarAsync<int>("SELECT ISNULL(MAX(numero), 0) FROM doc.DOCUMENTO_VERSION WHERE id_documento = @dest", new { dest }, tx);
            await c.ExecuteAsync("UPDATE doc.DOCUMENTO_VERSION SET es_vigente = 0 WHERE id_documento = @dest AND es_vigente = 1", new { dest }, tx);
            // La version nueva apunta al mismo archivo cifrado; el documento pendiente queda como evidencia de esta gestion (carpeta Gestiones).
            await c.ExecuteAsync(
                """
                INSERT INTO doc.DOCUMENTO_VERSION (id_documento, numero, es_vigente, nombre_archivo, tipo_mime, tamano_bytes, sha256, ruta_almacen, id_llave,
                                                    fecha_emision, fecha_vencimiento, motivo_reemplazo, subido_por, subido_en, id_gestion)
                SELECT @dest, @numero, 1, nombre_archivo, tipo_mime, tamano_bytes, sha256, ruta_almacen, id_llave,
                       fecha_emision, fecha_vencimiento, @motivo, subido_por, subido_en, @idGestion
                  FROM doc.DOCUMENTO_VERSION WHERE id_documento = @id AND es_vigente = 1
                """, new { dest, numero = numero + 1, motivo = (string?)d.motivo_solicitud, idGestion, id }, tx);
            await c.ExecuteAsync("UPDATE doc.DOCUMENTO SET modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_documento = @dest", new { dest, por = porQuien }, tx);
            await c.ExecuteAsync(
                "UPDATE doc.DOCUMENTO SET estado = 'Vigente', id_interlocutor = @idCliente, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_documento = @id",
                new { id, idCliente, por = porQuien }, tx);
            await Auditoria.CambioAsync(c, tx, actor, DocumentoRepositorio.Entidad, dest, "version", $"v{numero}", $"v{numero + 1} ({archivo}): {(string?)d.motivo_solicitud}", idGestion);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idCliente, $"Documento «{tipo}»", $"v{numero}", $"v{numero + 1} ({archivo}): {(string?)d.motivo_solicitud}", idGestion);
        }

        // Obsoletos: sin archivo, la operacion vive solo en el detalle de la gestion.
        var obs = (await c.QueryAsync(
            "SELECT nombre_campo, valor_anterior, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion AND nombre_campo LIKE 'docobs[[]%'", new { idGestion }, tx)).ToList();
        foreach (var o in obs)
        {
            var dest = IdDeCampo((string)o.nombre_campo)!.Value;
            var motivo = (string)o.valor_nuevo;
            var ed = await c.QuerySingleOrDefaultAsync(
                "SELECT d.origen, d.estado, d.id_interlocutor, t.nombre AS tipo FROM doc.DOCUMENTO d WITH (UPDLOCK, ROWLOCK) JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento WHERE d.id_documento = @dest",
                new { dest }, tx);
            if (ed is null || (string)ed.origen != "Expediente" || (string)ed.estado != "Vigente" || (long?)ed.id_interlocutor != idCliente)
                return F(409, "documento_destino_no_vigente", "El documento que se iba a retirar ya no está vigente; rechace la gestión.");
            await c.ExecuteAsync(
                "UPDATE doc.DOCUMENTO SET estado = 'Obsoleto', motivo_obsoleto = @motivo, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_documento = @dest",
                new { dest, motivo, por = porQuien }, tx);
            await Auditoria.CambioAsync(c, tx, actor, DocumentoRepositorio.Entidad, dest, "estado", "Vigente", $"Obsoleto: {motivo}", idGestion);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idCliente, $"Documento «{(string)ed.tipo}»", "Vigente", $"Obsoleto: {motivo}", idGestion);
        }
        return null;
    }
}
