using System.Globalization;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

// Modelo compilado (en memoria) de una version de reglas. El evaluador no toca la base de datos.
public record CondicionEval(string Codigo, string Atributo, string TipoAtributo, bool Multivalor, string? Funcion, string? ParametroFuncion, string Operador, int Aridad,
    string Origen, string? Valor1, string? Valor2, string? Lista, string? AtributoValor, string? TipoAtributoValor);
public record MiembroEval(CondicionEval Condicion, bool Negar);
public record GrupoEval(string Operador, IReadOnlyList<MiembroEval> Miembros, IReadOnlyList<GrupoEval> Subgrupos);
public record AccionEval(string Codigo, string Tipo, string? Resultado, string? Clave, string? Valor, string? Mensaje);
public record ReglaEval(string Codigo, string Nombre, int Orden, bool Activa, GrupoEval? Raiz, IReadOnlyList<AccionEval> Acciones);

/// <summary>Evaluador del motor de reglas (GEN-008): arbol Y/O anidado, funciones, operadores y politicas de resolucion. Puro y sin estado.</summary>
public static class ReglasEvaluador
{
    public const int MaxReglas = 50, MaxProfundidad = 4, MaxCondicionesPorRegla = 20;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ limites
    public static string? ValidarLimites(IReadOnlyList<ReglaGuardar> reglas)
    {
        if (reglas.Count > MaxReglas) return $"Una version admite como maximo {MaxReglas} reglas.";
        foreach (var r in reglas)
        {
            if (r.Condiciones is null) continue;
            if (Profundidad(r.Condiciones) > MaxProfundidad) return $"La regla '{r.Codigo}' supera {MaxProfundidad} niveles de grupos.";
            if (ContarCondiciones(r.Condiciones) > MaxCondicionesPorRegla) return $"La regla '{r.Codigo}' supera {MaxCondicionesPorRegla} condiciones.";
        }
        return null;
    }
    private static int Profundidad(ReglaGrupoGuardar g) => 1 + (g.Subgrupos is { Count: > 0 } ? g.Subgrupos.Max(Profundidad) : 0);
    private static int ContarCondiciones(ReglaGrupoGuardar g) => (g.Miembros?.Count ?? 0) + (g.Subgrupos?.Sum(ContarCondiciones) ?? 0);

    // ------------------------------------------------------------------ conjunto
    public static EvaluacionResultado Evaluar(IReadOnlyList<ReglaEval> reglas, string politica, IReadOnlyDictionary<string, object?> datos, DateOnly hoy)
    {
        var traza = new List<TrazaRegla>();
        var aplicadas = new List<ReglaEval>();
        foreach (var regla in reglas.Where(r => r.Activa).OrderBy(r => r.Orden))
        {
            var condiciones = new List<TrazaCondicion>();
            var cumple = regla.Raiz is null || EvaluarGrupo(regla.Raiz, datos, hoy, condiciones);
            traza.Add(new TrazaRegla(regla.Codigo, regla.Nombre, cumple, condiciones));
            if (!cumple) continue;
            aplicadas.Add(regla);
            if (politica == "PrimeraCoincidencia") break;
        }

        string? decision = null;
        var asignaciones = new Dictionary<string, string>();
        var mensajes = new List<string>();
        foreach (var r in aplicadas)
            foreach (var a in r.Acciones)
            {
                switch (a.Tipo)
                {
                    case "Decision":
                        if (Peso(a.Resultado) > Peso(decision)) decision = a.Resultado;
                        if (!string.IsNullOrWhiteSpace(a.Mensaje)) mensajes.Add(a.Mensaje!);
                        break;
                    case "Asignar":
                        if (a.Clave is not null && !asignaciones.ContainsKey(a.Clave)) asignaciones[a.Clave] = a.Valor ?? "";   // la primera regla fija el valor
                        break;
                    case "Mensaje":
                        if (!string.IsNullOrWhiteSpace(a.Mensaje)) mensajes.Add(a.Mensaje!);
                        break;
                }
            }
        return new EvaluacionResultado(decision, asignaciones, mensajes, aplicadas.Select(r => r.Codigo).ToList(), aplicadas.Count > 0, traza);
    }

    /// <summary>Rechazar > Referir > Aprobar.</summary>
    private static int Peso(string? decision) => decision switch { "Rechazar" => 3, "Referir" => 2, "Aprobar" => 1, _ => 0 };

    // ------------------------------------------------------------------ grupos
    private static bool EvaluarGrupo(GrupoEval g, IReadOnlyDictionary<string, object?> datos, DateOnly hoy, List<TrazaCondicion> traza)
    {
        var resultados = new List<bool>();
        foreach (var m in g.Miembros)
        {
            var (ok, observado, esperado, error) = EvaluarCondicion(m.Condicion, datos, hoy);
            var final = m.Negar ? !ok : ok;
            traza.Add(new TrazaCondicion(m.Condicion.Codigo, m.Condicion.Atributo, observado, m.Condicion.Operador, esperado, m.Negar, final, error));
            resultados.Add(final);
        }
        foreach (var s in g.Subgrupos) resultados.Add(EvaluarGrupo(s, datos, hoy, traza));
        if (resultados.Count == 0) return true;          // grupo vacio: no restringe
        return g.Operador == "O" ? resultados.Any(x => x) : resultados.All(x => x);
    }

    // ------------------------------------------------------------------ condicion
    private static (bool Ok, string? Observado, string? Esperado, string? Error) EvaluarCondicion(CondicionEval c, IReadOnlyDictionary<string, object?> datos, DateOnly hoy)
    {
        object? bruto = c.Atributo == "sistema.fecha_actual" && !datos.ContainsKey(c.Atributo) ? hoy : datos.GetValueOrDefault(c.Atributo);
        var multi = c.Multivalor && c.Funcion is null;
        string? observado = null, esperado = null;
        try
        {
            // ----- lado izquierdo
            List<string>? lista = null; object? izq = null; var tipo = c.TipoAtributo;
            if (c.Multivalor)
            {
                lista = Lista(bruto);
                observado = string.Join(", ", lista);
                if (c.Funcion == "CANTIDAD") { izq = (decimal)lista.Count; tipo = "Numero"; observado = lista.Count.ToString(Inv); }
                else if (c.Funcion is not null) throw new FormatException($"La funcion {c.Funcion} no aplica a un atributo multivalor.");
            }
            else
            {
                izq = Coercionar(bruto, c.TipoAtributo);
                if (c.Funcion is not null) { (izq, tipo) = AplicarFuncion(c.Funcion, c.ParametroFuncion, izq, c.TipoAtributo, hoy); }
                observado = Texto(izq);
            }

            // ----- operadores sin valor
            if (c.Operador is "ES_VACIO" or "NO_ES_VACIO")
            {
                var vacio = multi ? lista!.Count == 0 : izq is null || (izq is string s && s.Length == 0);
                return (c.Operador == "ES_VACIO" ? vacio : !vacio, observado, null, null);
            }
            if (multi ? lista!.Count == 0 : izq is null) return (false, observado, Descripcion(c), null);   // sin dato: la condicion no se cumple

            // ----- lado derecho
            if (c.Operador is "EN" or "NO_EN")
            {
                var opciones = (c.Lista ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => Coercionar(x, tipo)).ToList();
                esperado = string.Join("|", opciones.Select(Texto));
                var hay = multi ? lista!.Any(v => opciones.Any(o => Igual(v, o, "Texto"))) : opciones.Any(o => Igual(izq, o, tipo));
                return (c.Operador == "EN" ? hay : !hay, observado, esperado, null);
            }
            object? der1 = DerValor(c, c.Valor1, datos, tipo, hoy), der2 = c.Operador == "ENTRE" ? DerValor(c, c.Valor2, datos, tipo, hoy) : null;
            esperado = c.Operador == "ENTRE" ? $"{Texto(der1)} y {Texto(der2)}" : Texto(der1);
            if (der1 is null || (c.Operador == "ENTRE" && der2 is null)) return (false, observado, esperado, null);

            if (multi)
            {
                var dTxt = Texto(der1) ?? "";
                var contiene = lista!.Any(v => string.Equals(v, dTxt, StringComparison.OrdinalIgnoreCase));
                return c.Operador switch
                {
                    "IGUAL" or "TIENE" or "CONTIENE" => (contiene, observado, esperado, null),
                    "DISTINTO" => (!contiene, observado, esperado, null),
                    _ => throw new FormatException($"El operador {c.Operador} no aplica a un atributo multivalor."),
                };
            }
            var ok = c.Operador switch
            {
                "IGUAL" => Igual(izq, der1, tipo),
                "DISTINTO" => !Igual(izq, der1, tipo),
                "MAYOR" => Comparar(izq, der1, tipo) > 0,
                "MAYOR_IGUAL" => Comparar(izq, der1, tipo) >= 0,
                "MENOR" => Comparar(izq, der1, tipo) < 0,
                "MENOR_IGUAL" => Comparar(izq, der1, tipo) <= 0,
                "ENTRE" => Comparar(izq, der1, tipo) >= 0 && Comparar(izq, der2, tipo) <= 0,
                "CONTIENE" => (Texto(izq) ?? "").Contains(Texto(der1) ?? "", StringComparison.OrdinalIgnoreCase),
                "COMIENZA" => (Texto(izq) ?? "").StartsWith(Texto(der1) ?? "", StringComparison.OrdinalIgnoreCase),
                "TIENE" => string.Equals(Texto(izq), Texto(der1), StringComparison.OrdinalIgnoreCase),
                _ => throw new FormatException($"Operador desconocido: {c.Operador}."),
            };
            return (ok, observado, esperado, null);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return (false, observado, esperado, ex.Message);
        }
    }

    private static string Descripcion(CondicionEval c) => c.Origen == "Atributo" ? $"({c.AtributoValor})" : c.Operador is "EN" or "NO_EN" ? c.Lista ?? "" : c.Valor1 ?? "";

    private static object? DerValor(CondicionEval c, string? literal, IReadOnlyDictionary<string, object?> datos, string tipoIzq, DateOnly hoy)
    {
        if (c.Origen == "Atributo")
        {
            var raw = c.AtributoValor == "sistema.fecha_actual" && !datos.ContainsKey(c.AtributoValor) ? hoy : datos.GetValueOrDefault(c.AtributoValor!);
            return Coercionar(raw, c.TipoAtributoValor ?? tipoIzq);
        }
        return Coercionar(literal, tipoIzq);
    }

    // ------------------------------------------------------------------ valores y tipos
    private static List<string> Lista(object? v) => v switch
    {
        null => [],
        string s => s.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        IEnumerable<string> e => e.ToList(),
        System.Collections.IEnumerable e => e.Cast<object?>().Select(x => Texto(x) ?? "").ToList(),
        _ => [Texto(v) ?? ""],
    };

    /// <summary>Convierte un valor crudo (texto, numero, fecha, booleano) al tipo del atributo; null o vacio = sin dato.</summary>
    internal static object? Coercionar(object? v, string tipo)
    {
        if (v is null) return null;
        if (v is string vacio && vacio.Trim().Length == 0) return null;
        switch (tipo)
        {
            case "Numero":
                return v switch
                {
                    decimal d => d, int i => (decimal)i, long l => (decimal)l, double db => (decimal)db,
                    string s when decimal.TryParse(s.Trim(), NumberStyles.Number, Inv, out var r) => r,
                    _ => throw new FormatException($"'{v}' no es un numero."),
                };
            case "Fecha":
                return v switch
                {
                    DateOnly d => d, DateTime dt => DateOnly.FromDateTime(dt),
                    string s when DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var r) => r,
                    _ => throw new FormatException($"'{v}' no es una fecha (aaaa-mm-dd)."),
                };
            case "Booleano":
                return v switch
                {
                    bool b => b,
                    string s when s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || s.Trim() is "1" or "si" or "Si" or "SI" => true,
                    string s when s.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) || s.Trim() is "0" or "no" or "No" or "NO" => false,
                    _ => throw new FormatException($"'{v}' no es un booleano."),
                };
            default:
                return v is string t ? t.Trim() : Texto(v);
        }
    }

    internal static string? Texto(object? v) => v switch
    {
        null => null,
        decimal d => d.ToString("0.########", Inv),
        DateOnly f => f.ToString("yyyy-MM-dd", Inv),
        bool b => b ? "true" : "false",
        _ => Convert.ToString(v, Inv),
    };

    private static bool Igual(object? a, object? b, string tipo) => tipo switch
    {
        "Numero" => (decimal)a! == (decimal)b!,
        "Fecha" => (DateOnly)a! == (DateOnly)b!,
        "Booleano" => (bool)a! == (bool)b!,
        _ => string.Equals(Texto(a), Texto(b), StringComparison.OrdinalIgnoreCase),
    };

    private static int Comparar(object? a, object? b, string tipo) => tipo switch
    {
        "Numero" => ((decimal)a!).CompareTo((decimal)b!),
        "Fecha" => ((DateOnly)a!).CompareTo((DateOnly)b!),
        _ => throw new FormatException("La comparacion mayor/menor solo aplica a numeros y fechas."),
    };

    /// <summary>Compara dos valores ya normalizados como texto segun el tipo (para validar rangos).</summary>
    internal static int Comparar2(string a, string b, string tipo) => Comparar(Coercionar(a, tipo), Coercionar(b, tipo), tipo);

    // ------------------------------------------------------------------ funciones
    private static (object?, string) AplicarFuncion(string f, string? parametro, object? v, string tipoEntrada, DateOnly hoy)
    {
        if (v is null) return (null, FuncionRetorno(f));
        decimal P() => decimal.TryParse(parametro, NumberStyles.Number, Inv, out var d) ? d : throw new FormatException($"La funcion {f} necesita un parametro numerico.");
        switch (f)
        {
            case "EDAD_ANIOS": { var d = (DateOnly)v; var e = hoy.Year - d.Year; if (d.AddYears(e) > hoy) e--; return ((decimal)e, "Numero"); }
            case "DIAS_DESDE": return ((decimal)(hoy.DayNumber - ((DateOnly)v).DayNumber), "Numero");
            case "DIAS_HASTA": return ((decimal)(((DateOnly)v).DayNumber - hoy.DayNumber), "Numero");
            case "MESES_DESDE": { var d = (DateOnly)v; var m = (hoy.Year - d.Year) * 12 + hoy.Month - d.Month; if (d.AddMonths(m) > hoy) m--; return ((decimal)m, "Numero"); }
            case "SUMAR_DIAS": return (((DateOnly)v).AddDays((int)P()), "Fecha");
            case "SUMAR_MESES": return (((DateOnly)v).AddMonths((int)P()), "Fecha");
            case "REDONDEAR": return (Math.Round((decimal)v, (int)P(), MidpointRounding.AwayFromZero), "Numero");
            case "ABSOLUTO": return (Math.Abs((decimal)v), "Numero");
            case "PORCENTAJE_DE": return ((decimal)v * P() / 100m, "Numero");
            case "LONGITUD": return ((decimal)(Texto(v)?.Length ?? 0), "Numero");
            case "MAYUSCULAS": return (Texto(v)?.ToUpperInvariant(), "Texto");
            default: throw new FormatException($"Funcion desconocida: {f}.");
        }
    }

    public static string FuncionRetorno(string f) => f switch
    {
        "SUMAR_DIAS" or "SUMAR_MESES" => "Fecha",
        "MAYUSCULAS" => "Texto",
        _ => "Numero",
    };
}
