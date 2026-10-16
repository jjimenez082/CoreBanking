using System.Text.RegularExpressions;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Valor de una lista de valores tal como lo necesita el validador.</summary>
public record CatItem(string Codigo, string Estado);

/// <summary>Catalogos (lista -> id de item -> codigo/estado) usados para validar un alta o edicion.</summary>
public class CatalogosInterlocutor
{
    public const string ListaTipoIdentificacion = "TIPO_IDENTIFICACION_FISICA";
    public const string ListaInstitucion = "INSTITUCION_EMISORA_ID";
    public const string ListaTipoDireccion = "TIPO_DIRECCION";
    public const string ListaTipoContacto = "TIPO_CONTACTO";
    public const string ListaTipoRol = "TIPO_ROL_INTERLOCUTOR";

    public static readonly string[] Listas =
        [ListaTipoIdentificacion, ListaInstitucion, ListaTipoDireccion, ListaTipoContacto, ListaTipoRol];

    private readonly Dictionary<string, Dictionary<long, CatItem>> _datos;

    public CatalogosInterlocutor(Dictionary<string, Dictionary<long, CatItem>> datos) => _datos = datos;

    public CatItem? Buscar(string lista, long id) =>
        _datos.TryGetValue(lista, out var items) && items.TryGetValue(id, out var item) ? item : null;

    public long? IdPorCodigo(string lista, string codigo) =>
        _datos.TryGetValue(lista, out var items)
            ? items.Where(kv => kv.Value.Codigo == codigo).Select(kv => (long?)kv.Key).FirstOrDefault()
            : null;
}

/// <summary>
/// Reglas de validacion del alta/edicion de un cliente. Misma validacion para
/// ambos caminos, de modo que lo que se acepta en el alta tambien se acepta
/// al modificar. Devuelve la lista de errores (vacia = valido).
/// </summary>
public static class InterlocutorValidador
{
    private static readonly Regex CorreoRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$", RegexOptions.Compiled);

    /// <summary>Valida el formato de un medio de contacto segun el codigo de su tipo. null = valido.</summary>
    public static string? ValidarContacto(string codigoTipo, string valor)
    {
        var v = (valor ?? string.Empty).Trim();
        if (v.Length == 0) return "El dato de contacto es obligatorio.";
        if (v.Length > 200) return "El dato de contacto supera los 200 caracteres.";

        switch (codigoTipo)
        {
            case "CORREO":
                return CorreoRegex.IsMatch(v) ? null : $"El correo '{v}' no tiene un formato valido (ej. nombre@dominio.com).";
            case "TELEFONO_MOVIL":
            case "TELEFONO_FIJO":
            case "WHATSAPP":
                var digitos = new string(v.Where(char.IsDigit).ToArray());
                if (digitos.Length == 11 && digitos.StartsWith("506", StringComparison.Ordinal)) digitos = digitos[3..];
                return digitos.Length == 8 ? null : $"El numero '{v}' es invalido: debe tener 8 digitos (con o sin +506).";
            default:
                return null;
        }
    }

    public static string NormalizarContacto(string codigoTipo, string valor)
    {
        var v = (valor ?? string.Empty).Trim();
        return codigoTipo == "CORREO" ? v.ToLowerInvariant() : v;
    }

    public static List<string> Validar(
        string nombre1, string apellido1, DateOnly fechaNacimiento,
        IReadOnlyList<IdentificacionDto>? identificaciones,
        IReadOnlyList<DireccionDto>? direcciones,
        IReadOnlyList<ContactoDto>? contactos,
        CatalogosInterlocutor cat)
    {
        var errores = new List<string>();

        if (string.IsNullOrWhiteSpace(nombre1)) errores.Add("El primer nombre es obligatorio.");
        if (string.IsNullOrWhiteSpace(apellido1)) errores.Add("El primer apellido es obligatorio.");
        if (fechaNacimiento > DateOnly.FromDateTime(DateTime.UtcNow) || fechaNacimiento.Year < 1900)
            errores.Add("La fecha de nacimiento no es valida.");

        // ----- Identificaciones -----
        var idents = identificaciones ?? [];
        if (idents.Count == 0)
            errores.Add("Debe registrar al menos una identificacion.");
        else if (idents.Count(i => i.EsPrincipal) != 1)
            errores.Add("Debe marcar exactamente una identificacion como principal.");

        var vistos = new HashSet<string>();
        for (var n = 0; n < idents.Count; n++)
        {
            var i = idents[n];
            var et = $"Identificacion {n + 1}";
            var tipo = cat.Buscar(CatalogosInterlocutor.ListaTipoIdentificacion, i.IdTipoIdentificacion);
            if (tipo is null) errores.Add($"{et}: el tipo de identificacion no existe.");
            else if (i.Id is null && tipo.Estado != "Activo") errores.Add($"{et}: el tipo de identificacion esta inactivo.");

            if (string.IsNullOrWhiteSpace(i.Numero)) errores.Add($"{et}: el numero es obligatorio.");
            else if (i.Numero.Trim().Length > 50) errores.Add($"{et}: el numero supera los 50 caracteres.");
            else if (!vistos.Add($"{i.IdTipoIdentificacion}|{i.Numero.Trim().ToUpperInvariant()}"))
                errores.Add($"{et}: esta repetida (mismo tipo y numero).");

            if (i.IdInstitucionEmisora is null) errores.Add($"{et}: la institucion emisora es obligatoria.");
            else
            {
                var inst = cat.Buscar(CatalogosInterlocutor.ListaInstitucion, i.IdInstitucionEmisora.Value);
                if (inst is null) errores.Add($"{et}: la institucion emisora no existe.");
                else if (i.Id is null && inst.Estado != "Activo") errores.Add($"{et}: la institucion emisora esta inactiva.");
            }

            if (i.FechaEmision is null) errores.Add($"{et}: la fecha de emision es obligatoria.");
            else if (i.FechaEmision > DateOnly.FromDateTime(DateTime.UtcNow)) errores.Add($"{et}: la fecha de emision no puede ser futura.");
            if (i.FechaEmision is not null && i.FechaVencimiento is not null && i.FechaVencimiento <= i.FechaEmision)
                errores.Add($"{et}: la fecha de vencimiento debe ser posterior a la de emision.");
        }

        // ----- Direcciones -----
        var dirs = direcciones ?? [];
        if (dirs.Count(d => d.EsPrincipal) > 1) errores.Add("Solo una direccion puede ser la principal.");
        for (var n = 0; n < dirs.Count; n++)
        {
            var d = dirs[n];
            var et = $"Direccion {n + 1}";
            var tipo = cat.Buscar(CatalogosInterlocutor.ListaTipoDireccion, d.IdTipoDireccion);
            if (tipo is null) errores.Add($"{et}: el tipo de direccion no existe.");
            else if (d.Id is null && tipo.Estado != "Activo") errores.Add($"{et}: el tipo de direccion esta inactivo.");
            if (string.IsNullOrWhiteSpace(d.Detalle)) errores.Add($"{et}: la direccion exacta es obligatoria.");
            else if (d.Detalle.Trim().Length > 500) errores.Add($"{et}: la direccion exacta supera los 500 caracteres.");
        }

        // ----- Contactos -----
        var conts = contactos ?? [];
        for (var n = 0; n < conts.Count; n++)
        {
            var c = conts[n];
            var et = $"Contacto {n + 1}";
            var tipo = cat.Buscar(CatalogosInterlocutor.ListaTipoContacto, c.IdTipoContacto);
            if (tipo is null) { errores.Add($"{et}: el tipo de contacto no existe."); continue; }
            if (c.Id is null && tipo.Estado != "Activo") errores.Add($"{et}: el tipo de contacto esta inactivo.");
            var error = ValidarContacto(tipo.Codigo, c.Valor);
            if (error is not null) errores.Add($"{et}: {error}");
        }
        foreach (var grupo in conts.GroupBy(c => c.IdTipoContacto))
        {
            if (grupo.Count(c => c.EsPrincipal) > 1)
                errores.Add($"Solo un contacto de tipo '{cat.Buscar(CatalogosInterlocutor.ListaTipoContacto, grupo.Key)?.Codigo}' puede ser el principal.");
        }

        return errores;
    }
}
