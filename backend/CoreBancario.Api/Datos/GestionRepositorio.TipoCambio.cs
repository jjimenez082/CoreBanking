using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Aplicador de gestiones TC_CARGA: crea o corrige el tipo de cambio de una moneda en una fecha.</summary>
public partial class GestionRepositorio
{
    public const string EntidadTipoCambio = TipoCambioRepositorio.Entidad;

    private static readonly Dictionary<string, string> EtiquetasCreditos = new()
    {
        ["id_indicador"] = "Indicador", ["fecha_vigencia"] = "Vigente desde", ["valor"] = "Valor (%)",
        ["id_version"] = "Version", ["producto"] = "Producto comercial", ["conjunto"] = "Conjunto de reglas", ["numero_version"] = "Numero de version", ["fecha_vigencia_desde"] = "Vigente desde",
    };

    private static readonly Dictionary<string, string> EtiquetasTipoCambio = new()
    {
        ["id_moneda"] = "Moneda", ["fecha_vigencia"] = "Vigente desde", ["compra"] = "Compra", ["venta"] = "Venta", ["contable"] = "Valor contable",
    };

    private static async Task<ResultadoOperacion?> AplicarTipoCambioAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion)
    {
        var det = (await c.QueryAsync(
            "SELECT nombre_campo, valor_anterior, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx))
            .ToDictionary(d => (string)d.nombre_campo, d => (Anterior: (string?)d.valor_anterior, Nuevo: (string?)d.valor_nuevo));
        foreach (var k in EtiquetasTipoCambio.Keys)
            if (!det.ContainsKey(k)) return ResultadoOperacion.Fallo(500, "gestion_incompleta", $"La gestion no tiene el campo '{k}'.");

        var idMoneda = long.Parse(det["id_moneda"].Nuevo!, CultureInfo.InvariantCulture);
        var fecha = DateTime.ParseExact(det["fecha_vigencia"].Nuevo!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        decimal D(string k) => decimal.Parse(det[k].Nuevo!, CultureInfo.InvariantCulture);
        var (compra, venta, contable) = (D("compra"), D("venta"), D("contable"));

        var error = TipoCambioRepositorio.ValidarValores(compra, venta, contable);
        if (error is not null) return ResultadoOperacion.Fallo(409, "datos_invalidos", error);

        var m = await c.QuerySingleOrDefaultAsync(
            "SELECT estado, es_base FROM gen.MONEDA WHERE id_moneda = @id AND id_compania = @c", new { id = idMoneda, c = actor.IdCompania }, tx);
        if (m is null || (string)m.estado != "Activo" || (bool)m.es_base)
            return ResultadoOperacion.Fallo(409, "moneda_invalida", "La moneda ya no esta activa o paso a ser la moneda base; rechace la gestion.");

        var actual = await c.QuerySingleOrDefaultAsync(
            "SELECT id_tipo_cambio, compra, venta, contable FROM gen.TIPO_CAMBIO WITH (UPDLOCK, HOLDLOCK) WHERE id_compania = @c AND id_moneda = @m AND fecha_vigencia = @f",
            new { c = actor.IdCompania, m = idMoneda, f = fecha }, tx);

        if (actual is null)
        {
            if (det["compra"].Anterior is not null)
                return ResultadoOperacion.Fallo(409, "registro_cambio", "El tipo de cambio que se iba a corregir ya no existe; rechace la gestion y solicite de nuevo.");
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO gen.TIPO_CAMBIO (id_compania, id_moneda, fecha_vigencia, compra, venta, contable, origen, id_gestion, creado_por)
                OUTPUT INSERTED.id_tipo_cambio
                VALUES (@c, @m, @f, @compra, @venta, @contable, 'Manual', @g, @por)
                """, new { c = actor.IdCompania, m = idMoneda, f = fecha, compra, venta, @contable = contable, g = idGestion, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadTipoCambio, id,
                $"{det["fecha_vigencia"].Nuevo} compra {TipoCambioRepositorio.Num(compra)} venta {TipoCambioRepositorio.Num(venta)} contable {TipoCambioRepositorio.Num(contable)}", idGestion);
            return null;
        }

        // Correccion de una fecha ya cargada: los valores vigentes deben ser los que vio quien solicito.
        foreach (var (campo, vigente) in new[] { ("compra", (decimal)actual.compra), ("venta", (decimal)actual.venta), ("contable", (decimal)actual.contable) })
            if (det[campo].Anterior is null || TipoCambioRepositorio.Num(vigente) != det[campo].Anterior)
                return ResultadoOperacion.Fallo(409, "registro_cambio", $"'{EtiquetasTipoCambio[campo]}' cambio desde que se solicito la gestion; rechacela y solicite de nuevo.");

        await c.ExecuteAsync(
            "UPDATE gen.TIPO_CAMBIO SET compra = @compra, venta = @venta, contable = @contable, id_gestion = @g, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_tipo_cambio = @id",
            new { compra, venta, @contable = contable, g = idGestion, por = actor.NombreDescriptivo, id = (long)actual.id_tipo_cambio }, tx);
        foreach (var campo in new[] { "compra", "venta", "contable" })
            if (det[campo].Anterior != det[campo].Nuevo)
                await Auditoria.CambioAsync(c, tx, actor, EntidadTipoCambio, (long)actual.id_tipo_cambio, campo, det[campo].Anterior, det[campo].Nuevo, idGestion);
        return null;
    }
}
