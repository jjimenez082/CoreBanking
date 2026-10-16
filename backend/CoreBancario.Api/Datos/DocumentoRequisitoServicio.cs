using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Documentos que exige cada rol (doc.ROL_REQUISITO_DOCUMENTO: la fila existe = el rol exige un documento VIGENTE de ese tipo).
/// Un documento cuenta como vigente si esta en estado Vigente (no obsoleto ni pendiente de aprobacion) y su version vigente no tiene
/// la fecha de vencimiento pasada. Igual que el KYC, se verifica al asumir un rol sobre un cliente ya registrado y se muestra en la ficha 360;
/// si el documento vence u obsoleta despues el cliente aparece con el requisito sin cumplir, sin romper lo ya aprobado.
/// </summary>
public static class DocumentoRequisitoServicio
{
    public const string EntidadAuditoria = "ROL_REQUISITO";

    /// <summary>Tipos de documento que exigen los roles indicados (para una persona fisica o juridica).</summary>
    public static async Task<HashSet<long>> TiposExigidosAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idTipoRol, string tipoPersona)
        => (await c.QueryAsync<long>(
            "SELECT id_tipo_documento FROM doc.ROL_REQUISITO_DOCUMENTO WHERE id_compania = @idCompania AND id_tipo_rol = @idTipoRol AND tipo_persona = @tipoPersona",
            new { idCompania, idTipoRol, tipoPersona }, tx)).ToHashSet();

    /// <summary>Estado de cada tipo exigido para el cliente: Cumple, PorVencer, Vencido (solo hay uno vencido) o Falta.</summary>
    public static async Task<List<DocumentoRequeridoItem>> SituacionAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor)
    {
        var tipoPersona = await c.ExecuteScalarAsync<string?>(
            "SELECT tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania", new { idInterlocutor, idCompania }, tx);
        if (tipoPersona is not ("Fisica" or "Juridica")) return [];
        var hoy = await KycVigenciaServicio.HoyAsync(c, tx, idCompania);

        var filas = (await c.QueryAsync(
            """
            SELECT t.id_tipo_documento, t.nombre AS tipo, ca.nombre AS carpeta, t.dias_aviso, ro.nombre AS rol
            FROM doc.ROL_REQUISITO_DOCUMENTO q
            JOIN cli.INTERLOCUTOR_ROL ir ON ir.id_tipo_rol = q.id_tipo_rol AND ir.id_interlocutor = @idInterlocutor AND ir.estado = 'Activo'
            JOIN gen.LISTA_VALORES_ITEM ro ON ro.id_lista_valores_item = q.id_tipo_rol
            JOIN doc.TIPO_DOCUMENTO t ON t.id_tipo_documento = q.id_tipo_documento
            JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta
            WHERE q.id_compania = @idCompania AND q.tipo_persona = @tipoPersona
            ORDER BY ca.orden, t.orden, t.nombre, ro.nombre
            """, new { idInterlocutor, idCompania, tipoPersona }, tx)).ToList();
        if (filas.Count == 0) return [];

        var docs = (await c.QueryAsync(
            """
            SELECT d.id_tipo_documento, v.fecha_vencimiento
            FROM doc.DOCUMENTO d
            JOIN doc.DOCUMENTO_VERSION v ON v.id_documento = d.id_documento AND v.es_vigente = 1
            WHERE d.id_interlocutor = @idInterlocutor AND d.estado = 'Vigente' AND d.origen = 'Expediente'
            """, new { idInterlocutor }, tx)).ToList();

        var res = new List<DocumentoRequeridoItem>();
        foreach (var g in filas.GroupBy(f => (long)f.id_tipo_documento))
        {
            var primero = g.First();
            var delTipo = docs.Where(d => (long)d.id_tipo_documento == g.Key).ToList();
            var vigentes = delTipo.Where(d => d.fecha_vencimiento is null || DateOnly.FromDateTime((DateTime)d.fecha_vencimiento) >= hoy).ToList();
            string estado; DateOnly? vence = null;
            if (vigentes.Count > 0)
            {
                var fechas = vigentes.Where(d => d.fecha_vencimiento is not null).Select(d => DateOnly.FromDateTime((DateTime)d.fecha_vencimiento)).ToList();
                vence = fechas.Count == vigentes.Count ? fechas.Max() : null;   // basta uno sin vencimiento para no vencer nunca
                estado = vence is not null && vence.Value.DayNumber - hoy.DayNumber <= (int)primero.dias_aviso ? "PorVencer" : "Cumple";
            }
            else if (delTipo.Count > 0)
            {
                estado = "Vencido";
                vence = delTipo.Where(d => d.fecha_vencimiento is not null).Select(d => DateOnly.FromDateTime((DateTime)d.fecha_vencimiento)).DefaultIfEmpty().Max();
            }
            else estado = "Falta";
            res.Add(new DocumentoRequeridoItem(g.Key, (string)primero.tipo, (string)primero.carpeta, g.Select(x => (string)x.rol).Distinct().ToList(), estado, vence));
        }
        return res;
    }

    /// <summary>Documentos que le faltan al cliente para cumplir los tipos exigidos (texto para el mensaje de roles).</summary>
    public static async Task<List<string>> FaltantesAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor, IReadOnlyCollection<long> tiposExigidos)
    {
        if (tiposExigidos.Count == 0) return [];
        var idCompania = await c.ExecuteScalarAsync<long?>("SELECT id_compania FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor }, tx);
        if (idCompania is null) return [];
        var hoy = await KycVigenciaServicio.HoyAsync(c, tx, idCompania.Value);
        var nombres = (await c.QueryAsync("SELECT id_tipo_documento, nombre FROM doc.TIPO_DOCUMENTO WHERE id_tipo_documento IN @ids", new { ids = tiposExigidos }, tx))
            .ToDictionary(x => (long)x.id_tipo_documento, x => (string)x.nombre);
        var vigentes = (await c.QueryAsync<long>(
            """
            SELECT DISTINCT d.id_tipo_documento
            FROM doc.DOCUMENTO d
            JOIN doc.DOCUMENTO_VERSION v ON v.id_documento = d.id_documento AND v.es_vigente = 1
            WHERE d.id_interlocutor = @idInterlocutor AND d.estado = 'Vigente' AND d.origen = 'Expediente'
              AND d.id_tipo_documento IN @ids AND (v.fecha_vencimiento IS NULL OR v.fecha_vencimiento >= @hoy)
            """, new { idInterlocutor, ids = tiposExigidos, hoy = hoy.ToDateTime(TimeOnly.MinValue) }, tx)).ToHashSet();
        return tiposExigidos.Where(id => !vigentes.Contains(id)).Select(id => $"el documento «{nombres.GetValueOrDefault(id, "#" + id)}» vigente").ToList();
    }
}
