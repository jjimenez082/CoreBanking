using System.Globalization;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Aplica el perfil de datos a la ficha del cliente: enmascara lo que corresponde, restaura los valores reales cuando una edicion devuelve
/// valores enmascarados (para no sobrescribir el dato con su mascara) y entrega los valores reales a un revelado auditado.
/// Claves de texto de fechas y montos: fechaNacimiento, eco.gastos|activos|pasivos|patrimonio, ing.{id}, tra.{id}.min|max.
/// </summary>
public static class EnmascaradoClientes
{
    private static bool EsCorreo(ContactoResponse c) => c.TipoCodigo == "CORREO" || (c.TipoCodigo != "TELEFONO" && c.Valor.Contains('@'));
    private static string CampoContacto(ContactoResponse c) => EsCorreo(c) ? PerfilDatosCatalogo.Correo : PerfilDatosCatalogo.Telefono;

    private static string Dec(decimal? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

    public static InterlocutorDetalleResponse Aplicar(InterlocutorDetalleResponse d, PerfilEfectivo p)
    {
        if (!p.HayMascaras) return d;
        var textos = new Dictionary<string, string>();
        var campos = new HashSet<string>();

        var ident = d.Identificaciones;
        if (p.Enmascara(PerfilDatosCatalogo.Identificacion) && ident.Count > 0)
        {
            campos.Add(PerfilDatosCatalogo.Identificacion);
            ident = ident.Select(i => i with { Numero = p.Texto(PerfilDatosCatalogo.Identificacion, i.Numero) ?? "" }).ToList();
        }

        var contactos = d.Contactos.Select(c =>
        {
            var campo = CampoContacto(c);
            if (!p.Enmascara(campo)) return c;
            campos.Add(campo);
            return c with { Valor = p.Texto(campo, c.Valor) ?? "" };
        }).ToList();

        var dirs = d.Direcciones;
        if (p.Enmascara(PerfilDatosCatalogo.Direccion) && dirs.Count > 0)
        {
            campos.Add(PerfilDatosCatalogo.Direccion);
            dirs = dirs.Select(x => x with
            {
                Detalle = p.Texto(PerfilDatosCatalogo.Direccion, x.Detalle) ?? "",
                Detalle2 = p.Texto(PerfilDatosCatalogo.Direccion, x.Detalle2),
                Detalle3 = p.Texto(PerfilDatosCatalogo.Direccion, x.Detalle3),
            }).ToList();
        }

        var fecha = d.FechaNacimiento;
        if (p.Enmascara(PerfilDatosCatalogo.FechaNacimiento) && fecha is not null)
        {
            campos.Add(PerfilDatosCatalogo.FechaNacimiento);
            textos["fechaNacimiento"] = p.Fecha(PerfilDatosCatalogo.FechaNacimiento, fecha) ?? "";
            fecha = null;
        }

        var pf = d.PerfilFinanciero;
        if (pf is not null)
        {
            if (p.Enmascara(PerfilDatosCatalogo.MontosPerfil))
            {
                const string m = PerfilDatosCatalogo.MontosPerfil;
                var eco = pf.Economico;
                if (eco is not null)
                {
                    void T(string clave, decimal? v) { if (v is not null) { textos[clave] = p.Monto(m, v) ?? ""; campos.Add(m); } }
                    T("eco.gastos", eco.GastosEstimados); T("eco.activos", eco.ActivosDeclarados);
                    T("eco.pasivos", eco.PasivosDeclarados); T("eco.patrimonio", eco.PatrimonioDeclarado);
                    eco = eco with { GastosEstimados = null, ActivosDeclarados = null, PasivosDeclarados = null, PatrimonioDeclarado = null };
                }
                var ingresos = pf.Ingresos.Select(i => { textos[$"ing.{i.Id}"] = p.Monto(m, i.Monto) ?? ""; campos.Add(m); return i with { Monto = 0 }; }).ToList();
                var trans = pf.PerfilTransaccional.Select(t =>
                {
                    if (t.MontoMinimo is not null) textos[$"tra.{t.Id}.min"] = p.Monto(m, t.MontoMinimo) ?? "";
                    if (t.MontoMaximo is not null) textos[$"tra.{t.Id}.max"] = p.Monto(m, t.MontoMaximo) ?? "";
                    if (t.MontoMinimo is not null || t.MontoMaximo is not null) campos.Add(m);
                    return t with { MontoMinimo = null, MontoMaximo = null };
                }).ToList();
                pf = pf with { Economico = eco, Ingresos = ingresos, PerfilTransaccional = trans };
            }
            if (p.Enmascara(PerfilDatosCatalogo.CuentaExterna) && pf.CuentasExternas is { Count: > 0 })
            {
                campos.Add(PerfilDatosCatalogo.CuentaExterna);
                pf = pf with { CuentasExternas = pf.CuentasExternas.Select(x => x with { NumeroCuenta = p.Texto(PerfilDatosCatalogo.CuentaExterna, x.NumeroCuenta) ?? "" }).ToList() };
            }
        }

        var info = new MascarasInfoResponse(
            campos.OrderBy(x => x).Select(x => new MascaraCampoInfo(x, PerfilDatosCatalogo.BuscarCampo(x)?.Etiqueta ?? x, p.PuedeRevelar(x))).ToList(), textos);
        return d with { Identificaciones = ident, Contactos = contactos, Direcciones = dirs, FechaNacimiento = fecha, PerfilFinanciero = pf, Mascaras = info };
    }

    public static InterlocutorResponse Aplicar(InterlocutorResponse r, PerfilEfectivo p)
        => p.Enmascara(PerfilDatosCatalogo.Identificacion) ? r with { NumeroIdentificacion = p.Texto(PerfilDatosCatalogo.Identificacion, r.NumeroIdentificacion) ?? "" } : r;

    /// <summary>Las listas de identificaciones se muestran como "Tipo numero · Tipo numero": se enmascara el numero de cada una.</summary>
    public static string MascararIdentificaciones(string texto, PerfilEfectivo p)
    {
        if (!p.Enmascara(PerfilDatosCatalogo.Identificacion) || string.IsNullOrEmpty(texto)) return texto;
        return string.Join(" · ", texto.Split(" · ").Select(x =>
        {
            var i = x.LastIndexOf(' ');
            return i < 0 ? p.Texto(PerfilDatosCatalogo.Identificacion, x) : x[..(i + 1)] + p.Texto(PerfilDatosCatalogo.Identificacion, x[(i + 1)..]);
        }));
    }

    public static InterlocutorBusquedaResponse Aplicar(InterlocutorBusquedaResponse r, PerfilEfectivo p)
        => p.Enmascara(PerfilDatosCatalogo.Identificacion) ? r with { Identificaciones = MascararIdentificaciones(r.Identificaciones, p) } : r;

    /// <summary>Una coincidencia con un cliente que el usuario no puede ver conserva la alerta pero no revela quien es.</summary>
    public static CoincidenciaResponse Aplicar(CoincidenciaResponse r, PerfilEfectivo p, bool visible)
        => !visible
            ? r with { Numero = null, Nombre = "Cliente con acceso restringido", FechaNacimiento = null, Identificaciones = "", Roles = "" }
            : r with
            {
                Identificaciones = MascararIdentificaciones(r.Identificaciones, p),
                FechaNacimiento = p.Enmascara(PerfilDatosCatalogo.FechaNacimiento) ? null : r.FechaNacimiento
            };

    /// <summary>
    /// Si la edicion devuelve valores enmascarados (el usuario no pudo ver el real), se restauran los reales antes de validar y comparar:
    /// asi el dato enmascarado nunca se guarda ni se reporta como cambio. Un valor que el usuario si escribio (distinto de la mascara) se respeta.
    /// </summary>
    public static InterlocutorModificacionRequest Restaurar(InterlocutorModificacionRequest q, InterlocutorDetalleResponse real, PerfilEfectivo p)
    {
        if (!p.HayMascaras) return q;

        var ident = q.Identificaciones;
        if (p.Enmascara(PerfilDatosCatalogo.Identificacion))
            ident = q.Identificaciones.Select(i =>
            {
                var r = real.Identificaciones.FirstOrDefault(x => x.Id == i.Id);
                return r is not null && i.Numero == p.Texto(PerfilDatosCatalogo.Identificacion, r.Numero) ? i with { Numero = r.Numero } : i;
            }).ToList();

        var contactos = q.Contactos?.Select(c =>
        {
            var r = real.Contactos.FirstOrDefault(x => x.Id == c.Id);
            if (r is null) return c;
            var campo = CampoContacto(r);
            return p.Enmascara(campo) && c.Valor == p.Texto(campo, r.Valor) ? c with { Valor = r.Valor } : c;
        }).ToList();

        var dirs = q.Direcciones;
        if (p.Enmascara(PerfilDatosCatalogo.Direccion))
            dirs = q.Direcciones?.Select(x =>
            {
                var r = real.Direcciones.FirstOrDefault(y => y.Id == x.Id);
                if (r is null) return x;
                string? Rest(string? enviado, string? original) => enviado == p.Texto(PerfilDatosCatalogo.Direccion, original) ? original : enviado;
                return x with { Detalle = Rest(x.Detalle, r.Detalle) ?? "", Detalle2 = Rest(x.Detalle2, r.Detalle2), Detalle3 = Rest(x.Detalle3, r.Detalle3) };
            }).ToList();

        var fecha = q.FechaNacimiento;
        if (p.Enmascara(PerfilDatosCatalogo.FechaNacimiento) && fecha is null) fecha = real.FechaNacimiento;

        var pf = q.PerfilFinanciero;
        var rpf = real.PerfilFinanciero;
        if (pf is not null && rpf is not null)
        {
            if (p.Enmascara(PerfilDatosCatalogo.MontosPerfil))
            {
                var eco = pf.Economico;
                var reco = rpf.Economico;
                if (eco is not null && reco is not null)
                    eco = eco with
                    {
                        GastosEstimados = eco.GastosEstimados ?? reco.GastosEstimados, ActivosDeclarados = eco.ActivosDeclarados ?? reco.ActivosDeclarados,
                        PasivosDeclarados = eco.PasivosDeclarados ?? reco.PasivosDeclarados, PatrimonioDeclarado = eco.PatrimonioDeclarado ?? reco.PatrimonioDeclarado
                    };
                var ingresos = pf.Ingresos?.Select(i => i.Monto == 0 && rpf.Ingresos.FirstOrDefault(x => x.Id == i.Id) is { } r ? i with { Monto = r.Monto } : i).ToList();
                var trans = pf.PerfilTransaccional?.Select(t =>
                {
                    var r = rpf.PerfilTransaccional.FirstOrDefault(x => x.Id == t.Id);
                    return r is null ? t : t with { MontoMinimo = t.MontoMinimo ?? r.MontoMinimo, MontoMaximo = t.MontoMaximo ?? r.MontoMaximo };
                }).ToList();
                pf = pf with { Economico = eco, Ingresos = ingresos, PerfilTransaccional = trans };
            }
            if (p.Enmascara(PerfilDatosCatalogo.CuentaExterna))
                pf = pf with
                {
                    CuentasExternas = pf.CuentasExternas?.Select(x =>
                    {
                        var r = rpf.CuentasExternas?.FirstOrDefault(y => y.Id == x.Id);
                        return r is not null && x.NumeroCuenta == p.Texto(PerfilDatosCatalogo.CuentaExterna, r.NumeroCuenta) ? x with { NumeroCuenta = r.NumeroCuenta } : x;
                    }).ToList()
                };
        }
        return q with { Identificaciones = ident, Contactos = contactos, Direcciones = dirs, FechaNacimiento = fecha, PerfilFinanciero = pf };
    }

    private static readonly System.Text.RegularExpressions.Regex PatronGestion = new(
        @"^(?:(?<h>ident|dir|cont|ing|pt|cx)\[\+?\d+\]\.)?(?<c>numero_identificacion|valor|detalle[23]?|monto|monto_minimo|monto_maximo|numero_cuenta_externa|fecha_nacimiento|pe_gastos_estimados|pe_activos_declarados|pe_pasivos_declarados|pe_patrimonio_declarado)$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Campo sensible del catalogo al que corresponde un campo de gestion (null si no es sensible). Para contactos distingue correo de telefono por el valor.</summary>
    private static string? CampoDeGestion(string nombre, string? a, string? n)
    {
        var m = PatronGestion.Match(nombre);
        if (!m.Success) return null;
        var h = m.Groups["h"].Value;
        return (h, m.Groups["c"].Value) switch
        {
            ("ident", "numero_identificacion") or ("", "numero_identificacion") => PerfilDatosCatalogo.Identificacion,
            ("cont", "valor") => (a ?? n ?? "").Contains('@') ? PerfilDatosCatalogo.Correo : PerfilDatosCatalogo.Telefono,
            ("dir", _) => PerfilDatosCatalogo.Direccion,
            ("", "fecha_nacimiento") => PerfilDatosCatalogo.FechaNacimiento,
            ("ing", "monto") or ("pt", "monto_minimo") or ("pt", "monto_maximo") => PerfilDatosCatalogo.MontosPerfil,
            ("", var c) when c.StartsWith("pe_") => PerfilDatosCatalogo.MontosPerfil,
            ("cx", "numero_cuenta_externa") => PerfilDatosCatalogo.CuentaExterna,
            _ => null,
        };
    }

    /// <summary>Enmascara los valores sensibles del detalle de una gestion (y de la auditoria que la refleja).</summary>
    public static IReadOnlyList<GestionCampoResponse> AplicarGestion(IReadOnlyList<GestionCampoResponse> campos, PerfilEfectivo p)
    {
        if (!p.HayMascaras) return campos;
        return campos.Select(c =>
        {
            var campo = CampoDeGestion(c.NombreCampo, c.AnteriorCrudo ?? c.ValorAnterior, c.NuevoCrudo ?? c.ValorNuevo);
            if (campo is null || !p.Enmascara(campo)) return c;
            string? M(string? v) => string.IsNullOrEmpty(v) ? v : p.Texto(campo, v);
            return c with { ValorAnterior = M(c.AnteriorCrudo ?? c.ValorAnterior), ValorNuevo = M(c.NuevoCrudo ?? c.ValorNuevo), AnteriorCrudo = M(c.AnteriorCrudo), NuevoCrudo = M(c.NuevoCrudo) };
        }).ToList();
    }

    /// <summary>
    /// Valor de auditoria enmascarado. La auditoria del cliente registra los cambios con el nombre de campo de la gestion (ident[12].numero_identificacion)
    /// y las altas de un registro como resumen de texto ("Identificación #12" = "Número: ... · Tipo: ..."); ambos se enmascaran.
    /// </summary>
    public static string? ValorAuditoria(PerfilEfectivo? p, string entidad, string? columna, string? a, string? n, string? v)
    {
        if (p is null || !p.HayMascaras || string.IsNullOrEmpty(v) || columna is null) return v;
        var campo = CampoDeGestion(columna, a, n);
        if (campo is not null) return p.Enmascara(campo) ? p.Texto(campo, v) : v;
        if (!v.Contains(": ") || !System.Text.RegularExpressions.Regex.IsMatch(columna, @" #\d+$")) return v;
        var partes = v.Split(" · ").Select(x =>
        {
            var k = x.IndexOf(": ", StringComparison.Ordinal);
            if (k < 0) return x;
            var etiqueta = x[..k];
            var valor = x[(k + 2)..];
            string? c = etiqueta switch
            {
                "Número" => PerfilDatosCatalogo.Identificacion,
                "Dato" => valor.Contains('@') ? PerfilDatosCatalogo.Correo : PerfilDatosCatalogo.Telefono,
                var e when e.StartsWith("Dirección exacta") => PerfilDatosCatalogo.Direccion,
                "Monto" or "Monto mínimo esperado (por periodo)" or "Monto máximo esperado (por periodo)" => PerfilDatosCatalogo.MontosPerfil,
                "Número de cuenta (IBAN)" => PerfilDatosCatalogo.CuentaExterna,
                _ => null,
            };
            return c is not null && p.Enmascara(c) ? $"{etiqueta}: {p.Texto(c, valor)}" : x;
        });
        return string.Join(" · ", partes);
    }

    /// <summary>Valores reales de un campo para el revelado. Null si el campo/item no existe en el cliente.</summary>
    public static IReadOnlyDictionary<string, string?>? Reales(InterlocutorDetalleResponse d, string campo, long? idItem)
    {
        switch (campo)
        {
            case PerfilDatosCatalogo.Identificacion:
                return d.Identificaciones.FirstOrDefault(x => x.Id == idItem) is { } idf ? new Dictionary<string, string?> { ["numero"] = idf.Numero } : null;
            case PerfilDatosCatalogo.Correo or PerfilDatosCatalogo.Telefono:
                return d.Contactos.FirstOrDefault(x => x.Id == idItem && CampoContacto(x) == campo) is { } c ? new Dictionary<string, string?> { ["valor"] = c.Valor } : null;
            case PerfilDatosCatalogo.Direccion:
                return d.Direcciones.FirstOrDefault(x => x.Id == idItem) is { } x2
                    ? new Dictionary<string, string?> { ["detalle"] = x2.Detalle, ["detalle2"] = x2.Detalle2, ["detalle3"] = x2.Detalle3 } : null;
            case PerfilDatosCatalogo.FechaNacimiento:
                return d.FechaNacimiento is { } f ? new Dictionary<string, string?> { ["fechaNacimiento"] = f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } : null;
            case PerfilDatosCatalogo.CuentaExterna:
                return d.PerfilFinanciero?.CuentasExternas?.FirstOrDefault(x => x.Id == idItem) is { } ce ? new Dictionary<string, string?> { ["numeroCuenta"] = ce.NumeroCuenta } : null;
            case PerfilDatosCatalogo.MontosPerfil:
            {
                var pf = d.PerfilFinanciero;
                if (pf is null) return null;
                var r = new Dictionary<string, string?>();
                if (pf.Economico is { } e)
                {
                    if (e.GastosEstimados is not null) r["eco.gastos"] = Dec(e.GastosEstimados);
                    if (e.ActivosDeclarados is not null) r["eco.activos"] = Dec(e.ActivosDeclarados);
                    if (e.PasivosDeclarados is not null) r["eco.pasivos"] = Dec(e.PasivosDeclarados);
                    if (e.PatrimonioDeclarado is not null) r["eco.patrimonio"] = Dec(e.PatrimonioDeclarado);
                }
                foreach (var i in pf.Ingresos) r[$"ing.{i.Id}"] = Dec(i.Monto);
                foreach (var t in pf.PerfilTransaccional)
                {
                    if (t.MontoMinimo is not null) r[$"tra.{t.Id}.min"] = Dec(t.MontoMinimo);
                    if (t.MontoMaximo is not null) r[$"tra.{t.Id}.max"] = Dec(t.MontoMaximo);
                }
                return r;
            }
            default: return null;
        }
    }
}
