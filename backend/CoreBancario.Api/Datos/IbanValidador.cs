using System.Text;
using System.Text.RegularExpressions;

namespace CoreBancario.Api.Datos;

/// <summary>Validacion de numeros de cuenta en formato IBAN (ISO 13616): pais, digitos de control (mod 97) y longitud del pais cuando se conoce.</summary>
public static class IbanValidador
{
    private static readonly Regex Formato = new(@"^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$", RegexOptions.Compiled);

    // Longitudes oficiales de los paises mas habituales; los demas se validan por formato y digitos de control.
    private static readonly Dictionary<string, int> Longitudes = new()
    {
        ["CR"] = 22, ["ES"] = 24, ["DE"] = 22, ["FR"] = 27, ["GB"] = 22, ["IT"] = 27, ["PT"] = 25, ["NL"] = 18, ["BE"] = 16, ["CH"] = 21,
        ["DO"] = 28, ["GT"] = 28, ["SV"] = 28, ["PS"] = 29, ["BR"] = 29, ["LT"] = 20, ["IE"] = 22, ["AT"] = 20,
    };

    /// <summary>Sin espacios ni guiones y en mayusculas.</summary>
    public static string Normalizar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? "" : new string(valor.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray()).ToUpperInvariant();

    /// <summary>Devuelve null si el IBAN es valido, o el motivo del rechazo.</summary>
    public static string? Validar(string? valor)
    {
        var iban = Normalizar(valor);
        if (!Formato.IsMatch(iban)) return "el IBAN no tiene un formato válido (2 letras de país, 2 dígitos de control y el número de cuenta; ejemplo CR05015202001026284066).";
        if (Longitudes.TryGetValue(iban[..2], out var largo) && iban.Length != largo)
            return $"el IBAN de {iban[..2]} debe tener {largo} caracteres.";
        if (iban.Length > 34) return "el IBAN no puede superar 34 caracteres.";

        var movido = iban[4..] + iban[..4];
        var numerico = new StringBuilder();
        foreach (var ch in movido) numerico.Append(char.IsDigit(ch) ? ch.ToString() : (ch - 'A' + 10).ToString());
        var resto = 0;
        foreach (var ch in numerico.ToString()) resto = (resto * 10 + (ch - '0')) % 97;
        return resto == 1 ? null : "los dígitos de control del IBAN no son correctos; revise el número.";
    }
}
