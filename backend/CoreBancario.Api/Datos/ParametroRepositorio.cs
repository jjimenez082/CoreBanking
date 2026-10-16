using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Parametros generales de la compania (gen.PARAMETRO). Se cambia el valor (validado por tipo) y todo cambio se audita.</summary>
public class ParametroRepositorio
{
    public const string ObjetoAdministracion = "PAR_ADMINISTRACION";
    public const string Entidad = "PARAMETRO";
    private static readonly string[] Dias = ["LUN", "MAR", "MIE", "JUE", "VIE", "SAB", "DOM"];

    private readonly TenantContext _tenant;
    public ParametroRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    public async Task<IReadOnlyList<ParametroResponse>> ListarAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            """
            SELECT id_parametro, codigo, nombre, descripcion, categoria, tipo_dato, valor, valor_defecto, modificado_por, modificado_en
            FROM gen.PARAMETRO WHERE id_compania = @idCompania ORDER BY categoria, nombre
            """, new { idCompania });
        return filas.Select(f => new ParametroResponse(
            (long)f.id_parametro, (string)f.codigo, (string)f.nombre, (string?)f.descripcion, (string)f.categoria, (string)f.tipo_dato,
            (string)f.valor, (string)f.valor_defecto, (string)f.valor == (string)f.valor_defecto, (string?)f.modificado_por, (DateTime?)f.modificado_en))
            .Cast<ParametroResponse>().ToList();
    }

    /// <summary>Valor ya normalizado (o el mensaje de error).</summary>
    public static (string? Valor, string? Error) Normalizar(string codigo, string tipo, string? valorCrudo)
    {
        var v = (valorCrudo ?? "").Trim();
        if (v.Length == 0) return (null, "El valor es obligatorio.");
        if (v.Length > 500) return (null, "El valor supera los 500 caracteres.");
        switch (tipo)
        {
            case "Entero":
                if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return (null, "Debe ser un numero entero.");
                v = n.ToString(CultureInfo.InvariantCulture); break;
            case "Decimal":
                if (!decimal.TryParse(v.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return (null, "Debe ser un numero (use punto decimal).");
                v = d.ToString("0.######", CultureInfo.InvariantCulture); break;
            case "Booleano":
                if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("si", StringComparison.OrdinalIgnoreCase)) v = "true";
                else if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" || v.Equals("no", StringComparison.OrdinalIgnoreCase)) v = "false";
                else return (null, "Debe ser Si o No.");
                break;
        }

        switch (codigo)
        {
            case "DIAS_NO_HABILES":
                var partes = v.ToUpperInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
                if (partes.Any(p => !Dias.Contains(p))) return (null, "Use dias separados por coma: LUN,MAR,MIE,JUE,VIE,SAB,DOM.");
                if (partes.Count >= 7) return (null, "Al menos un dia de la semana debe ser habil.");
                v = string.Join(",", Dias.Where(partes.Contains)); break;
            case "DUP_UMBRAL_ALERTA":
            case "DUP_UMBRAL_FUERTE":
                var u = long.Parse(v, CultureInfo.InvariantCulture);
                if (u is < 1 or > 100) return (null, "Debe estar entre 1 y 100."); break;
            case "TC_VARIACION_MAXIMA_PORC":
                var porc = decimal.Parse(v, CultureInfo.InvariantCulture);
                if (porc is < 0 or > 100) return (null, "Debe estar entre 0 y 100."); break;
            case "KYC_MESES_PRIMERA_REVISION":
            case "KYC_MESES_POR_VENCER":
            case "KYC_MESES_GRACIA_BLOQUEO":
            case "KYC_FRECUENCIA_ALTO":
            case "KYC_FRECUENCIA_MEDIO":
            case "KYC_FRECUENCIA_BAJO":
                var meses = long.Parse(v, CultureInfo.InvariantCulture);
                if (meses is < 1 or > 120) return (null, "Debe estar entre 1 y 120 meses."); break;
            case "ZONA_HORARIA":
                try { TimeZoneInfo.FindSystemTimeZoneById(v); } catch { return (null, "Zona horaria desconocida (use un identificador IANA, por ejemplo America/Costa_Rica)."); }
                break;
        }
        return (v, null);
    }

    public async Task<ResultadoOperacion> CambiarValorAsync(ActorContext actor, long idParametro, string? valor, bool restablecer)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var p = await c.QuerySingleOrDefaultAsync(
                "SELECT codigo, tipo_dato, valor, valor_defecto FROM gen.PARAMETRO WITH (UPDLOCK) WHERE id_parametro = @id AND id_compania = @c",
                new { id = idParametro, c = actor.IdCompania }, tx);
            if (p is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el parametro.");

            var crudo = restablecer ? (string)p.valor_defecto : valor;
            var (nuevo, error) = Normalizar((string)p.codigo, (string)p.tipo_dato, crudo);
            if (error is not null) return ResultadoOperacion.Fallo(400, "valor_invalido", error);
            if (nuevo == (string)p.valor) return ResultadoOperacion.Ok(idParametro);

            await c.ExecuteAsync("UPDATE gen.PARAMETRO SET valor = @v, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_parametro = @id",
                new { v = nuevo, por = actor.NombreDescriptivo, id = idParametro }, tx);
            await Auditoria.CambioAsync(c, tx, actor, Entidad, idParametro, (string)p.codigo, (string)p.valor, nuevo);
            tx.Commit();
            return ResultadoOperacion.Ok(idParametro);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Valor de un parametro (o null si no existe).</summary>
    public static Task<string?> ValorAsync(SqlConnection c, SqlTransaction? tx, long idCompania, string codigo)
        => c.ExecuteScalarAsync<string?>("SELECT valor FROM gen.PARAMETRO WHERE id_compania = @c AND codigo = @codigo", new { c = idCompania, codigo }, tx);
}
