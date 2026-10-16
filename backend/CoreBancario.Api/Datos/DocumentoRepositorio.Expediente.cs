using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestor documental (r44b): consulta del expediente vivo del cliente. Agregar, reemplazar (version nueva) y obsoletar pasan por la
/// gestion CLI_DOCUMENTO (ver DocumentoGestionServicio): aqui solo se busca, se listan versiones y se leen limites. La evidencia es inmutable.
/// </summary>
public partial class DocumentoRepositorio
{
    public const string PermisoSubir = "DOC_SUBIR";
    public const string PermisoReemplazar = "DOC_REEMPLAZAR";
    public const string PermisoObsoletar = "DOC_OBSOLETAR";

    private const string SqlNombreCliente =
        "COALESCE(j.nombre_org1, g.nombre_grupo1, LTRIM(ISNULL(f.nombre1, N'') + N' ' + ISNULL(f.apellido1, N'')))";
    private const string SqlJoinsCliente =
        """
        LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
        LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
        LEFT JOIN cli.INTERLOCUTOR_GRUPO g ON g.id_interlocutor = i.id_interlocutor
        """;

    private static string Patron(string texto)
        => "%" + texto.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

    // ------------------------------------------------------------------ Selector de cliente

    /// <summary>Clientes (sin los fusionados, cuyos documentos viven en el que se conserva) por numero, nombre o numero de identificacion.</summary>
    public async Task<IReadOnlyList<ClienteDocumentoItem>> BuscarClientesAsync(long idCompania, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < 2) return [];
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            $"""
            SELECT TOP (15) i.id_interlocutor, i.numero, i.tipo_persona, i.estado, {SqlNombreCliente} AS nombre
              FROM cli.INTERLOCUTOR i {SqlJoinsCliente}
             WHERE i.id_compania = @idCompania AND i.estado <> 'Fusionado'
               AND (i.numero LIKE @t OR {SqlNombreCliente} LIKE @t
                    OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_IDENTIFICACION x WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo' AND x.numero_identificacion LIKE @t))
             ORDER BY i.numero
            """, new { idCompania, t = Patron(texto) });
        return filas.Select(f => new ClienteDocumentoItem((long)f.id_interlocutor, (string)f.numero, (string)f.nombre, (string)f.tipo_persona, (string)f.estado)).ToList();
    }

    // ------------------------------------------------------------------ Busqueda

    /// <summary>Documentos del expediente (no la evidencia de gestiones) segun los filtros; maximo 200, ocultando los restringidos sin permiso.</summary>
    public async Task<IReadOnlyList<DocumentoBusquedaItem>> BuscarAsync(ActorContext actor, DocumentoBusquedaFiltro f)
    {
        // Expediente (vigentes y obsoletos) y los archivos pendientes de aprobacion de gestiones de documentos.
        var filtro = $"d.id_compania = @idCompania AND d.id_interlocutor IS NOT NULL AND ((d.origen = 'Expediente' AND d.estado IN ('Vigente','Obsoleto')) OR (d.estado = 'EnGestion' AND g.entidad_negocio = '{DocumentoGestionServicio.Entidad}'))";
        var hoy = DateTime.UtcNow.Date;
        var p = new Dictionary<string, object?> { ["idCompania"] = actor.IdCompania, ["hoy"] = hoy };
        if (f.IdInterlocutor is not null) { filtro += " AND d.id_interlocutor = @idInterlocutor"; p["idInterlocutor"] = f.IdInterlocutor; }
        if (f.IdCarpeta is not null) { filtro += " AND t.id_carpeta = @idCarpeta"; p["idCarpeta"] = f.IdCarpeta; }
        if (f.IdTipoDocumento is not null) { filtro += " AND d.id_tipo_documento = @idTipo"; p["idTipo"] = f.IdTipoDocumento; }
        filtro += f.Estado switch
        {
            "Vigente" => " AND d.estado = 'Vigente' AND (v.fecha_vencimiento IS NULL OR v.fecha_vencimiento >= @hoy)",
            "PorVencer" => " AND d.estado = 'Vigente' AND v.fecha_vencimiento >= @hoy AND DATEDIFF(day, @hoy, v.fecha_vencimiento) <= t.dias_aviso",
            "Vencido" => " AND d.estado = 'Vigente' AND v.fecha_vencimiento < @hoy",
            "Obsoleto" => " AND d.estado = 'Obsoleto'",
            "Pendiente" => " AND d.estado = 'EnGestion'",
            _ => "",
        };
        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            filtro += $"""
                 AND (v.nombre_archivo LIKE @texto OR d.descripcion LIKE @texto OR t.nombre LIKE @texto
                      OR EXISTS (SELECT 1 FROM doc.DOCUMENTO_CLAVE k WHERE k.id_documento = d.id_documento AND k.clave LIKE @texto)
                      OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR i {SqlJoinsCliente}
                                  WHERE i.id_interlocutor = d.id_interlocutor AND (i.numero LIKE @texto OR {SqlNombreCliente} LIKE @texto)))
                """;
            p["texto"] = Patron(f.Texto);
        }

        using var c = NuevaConexion();
        var filas = (await LeerFilasAsync(c, null, filtro, new DynamicParameters(p), 200))
            .Where(x => PuedeVer(actor, x.Doc.Estado, x.Restringido, x.IdUsuarioCreador) && x.IdInterlocutor is not null).ToList();
        if (filas.Count == 0) return [];

        var ids = filas.Select(x => x.IdInterlocutor!.Value).Distinct().ToArray();
        var clientes = (await c.QueryAsync(
            $"SELECT i.id_interlocutor, i.numero, {SqlNombreCliente} AS nombre FROM cli.INTERLOCUTOR i {SqlJoinsCliente} WHERE i.id_interlocutor IN @ids", new { ids }))
            .ToDictionary(x => (long)x.id_interlocutor, x => (Numero: (string)x.numero, Nombre: (string)x.nombre));
        return filas.Select(x =>
        {
            var cli = clientes.GetValueOrDefault(x.IdInterlocutor!.Value, (Numero: "?", Nombre: "?"));
            return new DocumentoBusquedaItem(x.Doc, x.IdInterlocutor.Value, cli.Numero, cli.Nombre, x.NombreCarpeta);
        }).ToList();
    }

    /// <summary>Todas las versiones de un documento (si el usuario puede verlo).</summary>
    public async Task<IReadOnlyList<DocumentoVersionResponse>?> VersionesAsync(ActorContext actor, long idDocumento)
    {
        using var c = NuevaConexion();
        var d = await c.QuerySingleOrDefaultAsync(
            """
            SELECT d.estado, d.id_usuario_creador, t.confidencialidad
              FROM doc.DOCUMENTO d JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = d.id_tipo_documento
             WHERE d.id_documento = @idDocumento AND d.id_compania = @idCompania
            """, new { idDocumento, actor.IdCompania });
        if (d is null) return null;
        if (!PuedeVer(actor, (string)d.estado, (string)d.confidencialidad == "Restringida", (long?)d.id_usuario_creador)) return [];
        var filas = await c.QueryAsync(
            """
            SELECT v.id_version, v.numero, v.es_vigente, v.nombre_archivo, v.tipo_mime, v.tamano_bytes, v.fecha_emision, v.fecha_vencimiento, v.motivo_reemplazo, v.subido_por, v.subido_en, g.numero AS numero_gestion
              FROM doc.DOCUMENTO_VERSION v LEFT JOIN apr.GESTION g ON g.id_gestion = v.id_gestion
             WHERE v.id_documento = @idDocumento ORDER BY v.numero DESC
            """, new { idDocumento });
        return filas.Select(f =>
        {
            DateTime? emi = (DateTime?)f.fecha_emision, ven = (DateTime?)f.fecha_vencimiento;
            return new DocumentoVersionResponse((long)f.id_version, (int)f.numero, (bool)f.es_vigente, (string)f.nombre_archivo, (string)f.tipo_mime, (long)f.tamano_bytes,
                emi is null ? null : DateOnly.FromDateTime(emi.Value), ven is null ? null : DateOnly.FromDateTime(ven.Value), (string?)f.motivo_reemplazo, (string)f.subido_por, (DateTime)f.subido_en, (string?)f.numero_gestion);
        }).ToList();
    }

    // ------------------------------------------------------------------ Apoyo
}
