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
    public const string ListaTipoIdentificacionJuridica = "TIPO_IDENTIFICACION_JURIDICA";
    public const string ListaFormaJuridica = "FORMA_JURIDICA";
    public const string ListaEstadoLegalOrg = "ESTADO_LEGAL_ORG";
    public const string ListaActividad = "ACTIVIDAD_ECONOMICA";
    public const string ListaPais = "PAIS";
    public const string ListaSexo = "SEXO";
    public const string ListaEstadoCivil = "ESTADO_CIVIL";

    public static readonly string[] Listas =
        [ListaTipoIdentificacion, ListaInstitucion, ListaTipoDireccion, ListaTipoContacto, ListaTipoRol,
         ListaTipoIdentificacionJuridica, ListaFormaJuridica, ListaEstadoLegalOrg, ListaActividad, ListaPais, ListaSexo, ListaEstadoCivil];

    public static string ListaIdentificacionDe(string tipoPersona)
        => tipoPersona == "Juridica" ? ListaTipoIdentificacionJuridica : ListaTipoIdentificacion;

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

    /// <summary>
    /// Valida el estado completo de la persona. <paramref name="reglas"/> dice que campos parametrizables son obligatorios
    /// (union de los roles de la persona); sin reglas se usan las de siempre.
    /// </summary>
    public static List<string> Validar(
        string nombre1, string? nombre2, string apellido1, string? apellido2, DateOnly? fechaNacimiento,
        IReadOnlyList<IdentificacionDto>? identificaciones,
        IReadOnlyList<DireccionDto>? direcciones,
        IReadOnlyList<ContactoDto>? contactos,
        CatalogosInterlocutor cat, ReglasCampos? reglas = null, IdentidadPersonaDto? identidad = null)
    {
        reglas ??= ReglasCampos.Legado;
        var errores = new List<string>();

        if (string.IsNullOrWhiteSpace(nombre1)) errores.Add("El primer nombre es obligatorio.");
        if (string.IsNullOrWhiteSpace(apellido1)) errores.Add("El primer apellido es obligatorio.");
        if (reglas.Pide(ReglasCampos.Nombre2) && string.IsNullOrWhiteSpace(nombre2)) errores.Add("El segundo nombre es obligatorio.");
        if (reglas.Pide(ReglasCampos.Apellido2) && string.IsNullOrWhiteSpace(apellido2)) errores.Add("El segundo apellido es obligatorio.");
        if (fechaNacimiento is null)
        {
            if (reglas.Pide(ReglasCampos.FechaNacimiento)) errores.Add("La fecha de nacimiento es obligatoria.");
        }
        else if (fechaNacimiento > DateOnly.FromDateTime(DateTime.UtcNow) || fechaNacimiento.Value.Year < 1900)
            errores.Add("La fecha de nacimiento no es valida.");

        errores.AddRange(ValidarIdentidad(identidad, cat, reglas));
        errores.AddRange(ValidarColecciones(CatalogosInterlocutor.ListaTipoIdentificacion, identificaciones, direcciones, contactos, cat, reglas));
        return errores;
    }

    /// <summary>
    /// Nombre conocido, pais de nacimiento, sexo, estado civil y nacionalidades de la persona. Son opcionales salvo que un rol los exija.
    /// <paramref name="identidad"/> null = el bloque no viene (modificacion que no lo toca): no se valida nada.
    /// </summary>
    public static List<string> ValidarIdentidad(IdentidadPersonaDto? identidad, CatalogosInterlocutor cat, ReglasCampos reglas, bool soloSiViene = true)
    {
        var errores = new List<string>();
        if (identidad is null && soloSiViene) return errores;
        var i = identidad ?? new IdentidadPersonaDto(null, null, null, null, null);

        if (string.IsNullOrWhiteSpace(i.NombreConocido))
        {
            if (reglas.Pide(ReglasCampos.NombreConocido)) errores.Add("El nombre conocido es obligatorio.");
        }
        else if (i.NombreConocido.Trim().Length > 100) errores.Add("El nombre conocido supera los 100 caracteres.");

        void Lista(long? id, string lista, string codigoRegla, string etiqueta)
        {
            if (id is null) { if (reglas.Pide(codigoRegla)) errores.Add($"{etiqueta} es obligatorio."); return; }
            var item = cat.Buscar(lista, id.Value);
            if (item is null) errores.Add($"{etiqueta}: el valor no existe.");
            else if (item.Estado != "Activo") errores.Add($"{etiqueta}: el valor esta inactivo.");
        }
        Lista(i.IdPaisNacimiento, CatalogosInterlocutor.ListaPais, ReglasCampos.PaisNacimiento, "El pais de nacimiento");
        Lista(i.IdSexo, CatalogosInterlocutor.ListaSexo, ReglasCampos.Sexo, "El sexo");
        Lista(i.IdEstadoCivil, CatalogosInterlocutor.ListaEstadoCivil, ReglasCampos.EstadoCivil, "El estado civil");

        var nacs = i.Nacionalidades ?? [];
        if (nacs.Count == 0)
        {
            if (reglas.Pide(ReglasCampos.Nacionalidad)) errores.Add("Debe registrar al menos una nacionalidad.");
        }
        else
        {
            if (nacs.Select(n => n.IdPais).Distinct().Count() != nacs.Count) errores.Add("Hay una nacionalidad repetida.");
            foreach (var n in nacs)
            {
                var pais = cat.Buscar(CatalogosInterlocutor.ListaPais, n.IdPais);
                if (pais is null) errores.Add("Una nacionalidad indicada no existe en la lista de paises.");
                else if (n.Id is null && pais.Estado != "Activo") errores.Add("Una nacionalidad indicada esta inactiva en la lista de paises.");
            }
        }
        return errores;
    }

    /// <summary>
    /// Valida el estado completo de una ORGANIZACION (persona juridica): datos propios, identificaciones, direcciones y contactos.
    /// Siempre obligatorios: razon social, forma juridica, pais de constitucion, estado legal, tipo y numero de identificacion y linea 1 de direccion.
    /// </summary>
    public static List<string> ValidarOrganizacion(
        OrganizacionDto? o,
        IReadOnlyList<IdentificacionDto>? identificaciones,
        IReadOnlyList<DireccionDto>? direcciones,
        IReadOnlyList<ContactoDto>? contactos,
        CatalogosInterlocutor cat, ReglasCampos? reglas = null)
    {
        reglas ??= ReglasCampos.LegadoDe("Juridica");
        var errores = new List<string>();
        if (o is null) { errores.Add("Faltan los datos de la organizacion."); return errores; }

        if (string.IsNullOrWhiteSpace(o.RazonSocial)) errores.Add("La razon social es obligatoria.");
        else if (o.RazonSocial.Trim().Length > 200) errores.Add("La razon social supera los 200 caracteres.");

        var forma = cat.Buscar(CatalogosInterlocutor.ListaFormaJuridica, o.IdFormaJuridica);
        if (forma is null) errores.Add("La forma juridica es obligatoria y debe existir.");
        var pais = cat.Buscar(CatalogosInterlocutor.ListaPais, o.IdPaisConstitucion);
        if (pais is null) errores.Add("El pais de constitucion es obligatorio y debe existir.");
        var estadoLegal = cat.Buscar(CatalogosInterlocutor.ListaEstadoLegalOrg, o.IdEstadoLegal);
        if (estadoLegal is null) errores.Add("El estado legal es obligatorio y debe existir.");

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        if (o.FechaConstitucion is null)
        {
            if (reglas.Pide(ReglasCampos.OrgFechaConstitucion)) errores.Add("La fecha de constitucion es obligatoria.");
        }
        else if (o.FechaConstitucion > hoy || o.FechaConstitucion.Value.Year < 1800) errores.Add("La fecha de constitucion no es valida.");

        if (reglas.Pide(ReglasCampos.OrgReferenciaRegistral) && string.IsNullOrWhiteSpace(o.ReferenciaRegistral)) errores.Add("La referencia registral es obligatoria.");
        if (o.ReferenciaRegistral is { } rr && rr.Trim().Length > 200) errores.Add("La referencia registral supera los 200 caracteres.");

        if (o.FechaDisolucion is not null)
        {
            if (o.FechaConstitucion is not null && o.FechaDisolucion < o.FechaConstitucion) errores.Add("La fecha de disolucion no puede ser anterior a la de constitucion.");
            if (estadoLegal is not null && estadoLegal.Codigo != "DISUELTA") errores.Add("La fecha de disolucion solo aplica si el estado legal es Disuelta.");
        }

        var noms = (o.NombresComerciales ?? []).Where(n => !string.IsNullOrWhiteSpace(n.Nombre)).ToList();
        if (reglas.Pide(ReglasCampos.OrgNombreComercial) && noms.Count == 0) errores.Add("Debe registrar al menos un nombre comercial.");
        if (noms.Any(n => n.Nombre.Trim().Length > 200)) errores.Add("Un nombre comercial supera los 200 caracteres.");
        if (noms.Select(n => n.Nombre.Trim().ToUpperInvariant()).Distinct().Count() != noms.Count) errores.Add("Hay nombres comerciales repetidos.");

        var acts = o.Actividades ?? [];
        if (reglas.Pide(ReglasCampos.OrgActividad) && acts.Count == 0) errores.Add("Debe registrar al menos una actividad economica.");
        if (acts.Count > 0 && acts.Count(a => a.EsPrincipal) != 1) errores.Add("Debe marcar exactamente una actividad economica como principal.");
        for (var n = 0; n < acts.Count; n++)
        {
            var a = acts[n];
            var act = cat.Buscar(CatalogosInterlocutor.ListaActividad, a.IdActividad);
            if (act is null) errores.Add($"Actividad {n + 1}: la actividad economica no existe.");
            else if (a.Id is null && act.Estado != "Activo") errores.Add($"Actividad {n + 1}: la actividad economica esta inactiva.");
            if (a.Descripcion is { } d && d.Trim().Length > 300) errores.Add($"Actividad {n + 1}: la descripcion supera los 300 caracteres.");
        }
        if (acts.Select(a => a.IdActividad).Distinct().Count() != acts.Count) errores.Add("Hay actividades economicas repetidas.");

        errores.AddRange(ValidarColecciones(CatalogosInterlocutor.ListaTipoIdentificacionJuridica, identificaciones, direcciones, contactos, cat, reglas));
        return errores;
    }

    /// <summary>Identificaciones, direcciones y contactos: validacion comun a personas y organizaciones (cambia solo la lista de tipos de identificacion).</summary>
    private static List<string> ValidarColecciones(
        string listaTipoIdentificacion,
        IReadOnlyList<IdentificacionDto>? identificaciones,
        IReadOnlyList<DireccionDto>? direcciones,
        IReadOnlyList<ContactoDto>? contactos,
        CatalogosInterlocutor cat, ReglasCampos reglas)
    {
        var errores = new List<string>();
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
            var tipo = cat.Buscar(listaTipoIdentificacion, i.IdTipoIdentificacion);
            if (tipo is null) errores.Add($"{et}: el tipo de identificacion no existe.");
            else if (i.Id is null && tipo.Estado != "Activo") errores.Add($"{et}: el tipo de identificacion esta inactivo.");

            if (string.IsNullOrWhiteSpace(i.Numero)) errores.Add($"{et}: el numero es obligatorio.");
            else if (i.Numero.Trim().Length > 50) errores.Add($"{et}: el numero supera los 50 caracteres.");
            else if (!vistos.Add($"{i.IdTipoIdentificacion}|{i.Numero.Trim().ToUpperInvariant()}"))
                errores.Add($"{et}: esta repetida (mismo tipo y numero).");

            if (i.IdInstitucionEmisora is null)
            {
                if (reglas.Pide(ReglasCampos.IdentInstitucion)) errores.Add($"{et}: la institucion emisora es obligatoria.");
            }
            else
            {
                var inst = cat.Buscar(CatalogosInterlocutor.ListaInstitucion, i.IdInstitucionEmisora.Value);
                if (inst is null) errores.Add($"{et}: la institucion emisora no existe.");
                else if (i.Id is null && inst.Estado != "Activo") errores.Add($"{et}: la institucion emisora esta inactiva.");
            }

            if (i.FechaEmision is null)
            {
                if (reglas.Pide(ReglasCampos.IdentFechaEmision)) errores.Add($"{et}: la fecha de emision es obligatoria.");
            }
            else if (i.FechaEmision > DateOnly.FromDateTime(DateTime.UtcNow)) errores.Add($"{et}: la fecha de emision no puede ser futura.");
            if (i.FechaVencimiento is null && reglas.Pide(ReglasCampos.IdentFechaVencimiento)) errores.Add($"{et}: la fecha de vencimiento es obligatoria.");
            if (i.FechaEmision is not null && i.FechaVencimiento is not null && i.FechaVencimiento <= i.FechaEmision)
                errores.Add($"{et}: la fecha de vencimiento debe ser posterior a la de emision.");
        }

        // ----- Direcciones -----
        var dirs = direcciones ?? [];
        if (reglas.Pide(ReglasCampos.Direccion) && dirs.Count == 0) errores.Add("Debe registrar al menos una direccion.");
        if (dirs.Count(d => d.EsPrincipal) > 1) errores.Add("Solo una direccion puede ser la principal.");
        for (var n = 0; n < dirs.Count; n++)
        {
            var d = dirs[n];
            var et = $"Direccion {n + 1}";
            var tipo = cat.Buscar(CatalogosInterlocutor.ListaTipoDireccion, d.IdTipoDireccion);
            if (tipo is null) errores.Add($"{et}: el tipo de direccion no existe.");
            else if (d.Id is null && tipo.Estado != "Activo") errores.Add($"{et}: el tipo de direccion esta inactivo.");
            if (string.IsNullOrWhiteSpace(d.Detalle)) errores.Add($"{et}: la direccion exacta es obligatoria.");
            else if (d.Detalle.Trim().Length > 500) errores.Add($"{et}: la direccion exacta (linea 1) supera los 500 caracteres.");
            if (reglas.Pide(ReglasCampos.DirLinea2) && string.IsNullOrWhiteSpace(d.Detalle2)) errores.Add($"{et}: la linea 2 de la direccion es obligatoria.");
            if (reglas.Pide(ReglasCampos.DirLinea3) && string.IsNullOrWhiteSpace(d.Detalle3)) errores.Add($"{et}: la linea 3 de la direccion es obligatoria.");
            if (d.Detalle2 is { } l2 && l2.Trim().Length > 200) errores.Add($"{et}: la linea 2 de la direccion supera los 200 caracteres.");
            if (d.Detalle3 is { } l3 && l3.Trim().Length > 200) errores.Add($"{et}: la linea 3 de la direccion supera los 200 caracteres.");
        }

        // ----- Contactos -----
        var conts = contactos ?? [];
        if (reglas.Pide(ReglasCampos.Correo) || reglas.Pide(ReglasCampos.Telefono))
        {
            var codigos = conts.Select(c => cat.Buscar(CatalogosInterlocutor.ListaTipoContacto, c.IdTipoContacto)?.Codigo ?? "").ToList();
            if (reglas.Pide(ReglasCampos.Correo) && !codigos.Contains("CORREO")) errores.Add("Debe registrar al menos un correo electronico.");
            if (reglas.Pide(ReglasCampos.Telefono) && !codigos.Any(x => x is "TELEFONO_MOVIL" or "TELEFONO_FIJO" or "WHATSAPP")) errores.Add("Debe registrar al menos un telefono.");
        }
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
