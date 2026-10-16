using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Control de calidad de datos: busca personas ya registradas (clientes, otras personas y prospectos) que se parezcan a la que
/// se va a registrar. Puntuacion: identificacion igual (en cualquier tipo) = 100; si no, 65 % similitud de nombre + 35 % misma
/// fecha de nacimiento (si falta la fecha en alguno, el nombre solo vale hasta 80 %); telefono o correo iguales suman 10.
/// Los umbrales viven en Parametros generales (DUP_UMBRAL_ALERTA, DUP_UMBRAL_FUERTE, DUP_FUERTE_BLOQUEA).
/// </summary>
public static class MotorCoincidencias
{
    public const string ParamAlerta = "DUP_UMBRAL_ALERTA";
    public const string ParamFuerte = "DUP_UMBRAL_FUERTE";
    public const string ParamBloquea = "DUP_FUERTE_BLOQUEA";
    public const string PermisoAutorizar = "CLI_DUPLICADO_AUTORIZAR";
    private const int MaxCandidatos = 300;
    private const int MaxResultados = 10;

    // ----- Normalizacion y similitud ------------------------------------------------

    public static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SoloAlfanum(string? s) => new(Normalizar(s).Where(char.IsLetterOrDigit).ToArray());
    private static string Ocho(string? tel)
    {
        var d = new string((tel ?? "").Where(char.IsDigit).ToArray());
        return d.Length >= 8 ? d[^8..] : "";
    }

    public static double Ratio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return 1.0 - (double)d[a.Length, b.Length] / Math.Max(a.Length, b.Length);
    }

    private static string Ordenado(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Similitud de nombre 0..1: mejor entre nombre completo, sin segundo nombre/apellido, y con palabras ordenadas.</summary>
    public static double SimilitudNombre(string? n1, string? n2, string? a1, string? a2, string? m1, string? m2, string? b1, string? b2)
    {
        string Une(params string?[] p) => Normalizar(string.Join(' ', p.Where(x => !string.IsNullOrWhiteSpace(x))));
        var completoA = Une(n1, n2, a1, a2); var completoB = Une(m1, m2, b1, b2);
        var cortoA = Une(n1, a1); var cortoB = Une(m1, b1);
        var mejor = 0.0;
        foreach (var (x, y) in new[] { (completoA, completoB), (cortoA, cortoB), (cortoA, completoB), (completoA, cortoB) })
            mejor = Math.Max(mejor, Math.Max(Ratio(x, y), Ratio(Ordenado(x), Ordenado(y))));
        return mejor;
    }

    // ----- Parametros -------------------------------------------------------------------

    public static async Task<(int Alerta, int Fuerte, bool Bloquea)> UmbralesAsync(SqlConnection c, SqlTransaction? tx, long idCompania)
    {
        static int Entero(string? v, int def) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 100 ? n : def;
        var alerta = Entero(await ParametroRepositorio.ValorAsync(c, tx, idCompania, ParamAlerta), 85);
        var fuerte = Math.Max(alerta, Entero(await ParametroRepositorio.ValorAsync(c, tx, idCompania, ParamFuerte), 95));
        var bloquea = string.Equals(await ParametroRepositorio.ValorAsync(c, tx, idCompania, ParamBloquea), "true", StringComparison.OrdinalIgnoreCase);
        return (alerta, fuerte, bloquea);
    }

    // ----- Busqueda ---------------------------------------------------------------------

    private sealed record Candidato(string Origen, long Id, string? Numero, string? N1, string? N2, string? A1, string? A2, DateTime? Fn,
        string Estado, HashSet<string> Idents, HashSet<string> Tels, HashSet<string> Correos, string TextoIdents, string Roles);

    public static async Task<CoincidenciasResultado> BuscarAsync(SqlConnection c, SqlTransaction? tx, long idCompania, CoincidenciaRequest q)
    {
        if (q.TipoPersona == "Juridica") return await BuscarOrganizacionAsync(c, tx, idCompania, q);
        var (alerta, fuerte, bloquea) = await UmbralesAsync(c, tx, idCompania);
        var vacio = new CoincidenciasResultado([], alerta, fuerte, bloquea);

        var numeros = (q.Identificaciones ?? []).Select(i => SoloAlfanum(i.Numero)).Where(x => x.Length > 0).Distinct().ToList();
        var tels = (q.Telefonos ?? []).Select(Ocho).Where(x => x.Length > 0).Distinct().ToList();
        var correos = (q.Correos ?? []).Select(x => (x ?? "").Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        var fn = q.FechaNacimiento?.Date;
        if (Normalizar(q.Nombre1).Length == 0 && Normalizar(q.Apellido1).Length == 0 && numeros.Count == 0) return vacio;

        var p = new
        {
            idCompania, fn, n1 = Normalizar(q.Nombre1), a1 = Normalizar(q.Apellido1),
            numeros = numeros.Count > 0 ? numeros : new List<string> { "#" },
            tels = tels.Count > 0 ? tels : new List<string> { "#" },
            correos = correos.Count > 0 ? correos : new List<string> { "#" },
            exProspecto = q.ExcluirProspecto, exInterlocutor = q.ExcluirInterlocutor, top = MaxCandidatos
        };

        const string digitos = "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ct.valor, ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')";
        var cand = new List<Candidato>();

        // Interlocutores (clientes y otras personas)
        var filas = (await c.QueryAsync(
            $"""
            SELECT TOP (@top) i.id_interlocutor AS Id, i.numero AS Numero, i.estado AS Estado,
                   f.nombre1 AS N1, f.nombre2 AS N2, f.apellido1 AS A1, f.apellido2 AS A2, f.fecha_nacimiento AS Fn
            FROM cli.INTERLOCUTOR i
            JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania AND i.estado <> 'Fusionado' AND (@exInterlocutor IS NULL OR i.id_interlocutor <> @exInterlocutor)
              AND ( (@fn IS NOT NULL AND f.fecha_nacimiento = @fn)
                 OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_IDENTIFICACION x WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo'
                            AND UPPER(REPLACE(REPLACE(REPLACE(x.numero_identificacion, ' ', ''), '-', ''), '.', '')) IN @numeros)
                 OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_CONTACTO ct WHERE ct.id_interlocutor = i.id_interlocutor AND ct.estado = 'Activo'
                            AND (LOWER(ct.valor) IN @correos OR RIGHT({digitos}, 8) IN @tels))
                 OR (@a1 <> '' AND DIFFERENCE(f.apellido1, @a1) = 4 AND DIFFERENCE(f.nombre1, @n1) >= 3) )
            """, p, tx)).ToList();

        if (filas.Count > 0)
        {
            var ids = filas.Select(f => (long)f.Id).ToList();
            var idents = (await c.QueryAsync(
                """
                SELECT x.id_interlocutor AS Id, t.nombre AS Tipo, x.numero_identificacion AS Numero
                FROM cli.INTERLOCUTOR_IDENTIFICACION x JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_identificacion
                WHERE x.estado = 'Activo' AND x.id_interlocutor IN @ids
                """, new { ids }, tx)).ToList();
            var contactos = (await c.QueryAsync(
                "SELECT id_interlocutor AS Id, valor AS Valor FROM cli.INTERLOCUTOR_CONTACTO WHERE estado = 'Activo' AND id_interlocutor IN @ids", new { ids }, tx)).ToList();
            var roles = (await c.QueryAsync(
                """
                SELECT r.id_interlocutor AS Id, it.nombre AS Nombre FROM cli.INTERLOCUTOR_ROL r
                JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol WHERE r.estado = 'Activo' AND r.id_interlocutor IN @ids
                """, new { ids }, tx)).ToList();

            foreach (var f in filas)
            {
                long id = f.Id;
                var rs = roles.Where(r => (long)r.Id == id).Select(r => (string)r.Nombre).ToList();
                var cons = contactos.Where(x => (long)x.Id == id).Select(x => (string)x.Valor).ToList();
                cand.Add(new Candidato(rs.Contains("Cliente") ? "Cliente" : "Persona", id, (string)f.Numero, (string?)f.N1, (string?)f.N2, (string?)f.A1, (string?)f.A2,
                    (DateTime?)f.Fn, (string)f.Estado,
                    idents.Where(x => (long)x.Id == id).Select(x => SoloAlfanum((string)x.Numero)).ToHashSet(),
                    cons.Select(Ocho).Where(x => x.Length > 0).ToHashSet(),
                    cons.Where(x => x.Contains('@')).Select(x => x.Trim().ToLowerInvariant()).ToHashSet(),
                    string.Join(" · ", idents.Where(x => (long)x.Id == id).Select(x => $"{x.Tipo} {x.Numero}")), string.Join(", ", rs)));
            }
        }

        // Prospectos vigentes
        var pros = await c.QueryAsync(
            """
            SELECT TOP (@top) p.id_interlocutor_prospecto AS Id, p.nombre1 AS N1, p.nombre2 AS N2, p.apellido1 AS A1, p.apellido2 AS A2,
                   p.nombre_contacto AS Nombre, p.fecha_nacimiento AS Fn, p.numero_identificacion AS Numero, t.nombre AS Tipo,
                   p.telefono_contacto AS Tel, p.correo_contacto AS Correo
            FROM cli.INTERLOCUTOR_PROSPECTO p LEFT JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = p.id_tipo_identificacion
            WHERE p.id_compania = @idCompania AND p.estado = 'Prospecto' AND (@exProspecto IS NULL OR p.id_interlocutor_prospecto <> @exProspecto)
              AND ( (@fn IS NOT NULL AND p.fecha_nacimiento = @fn)
                 OR UPPER(REPLACE(REPLACE(REPLACE(ISNULL(p.numero_identificacion, '#'), ' ', ''), '-', ''), '.', '')) IN @numeros
                 OR LOWER(ISNULL(p.correo_contacto, '#')) IN @correos
                 OR RIGHT(REPLACE(REPLACE(REPLACE(ISNULL(p.telefono_contacto, '#'), ' ', ''), '-', ''), '+', ''), 8) IN @tels
                 OR (@a1 <> '' AND DIFFERENCE(ISNULL(p.apellido1, ''), @a1) = 4 AND DIFFERENCE(ISNULL(p.nombre1, ''), @n1) >= 3) )
            """, p, tx);
        foreach (var f in pros)
        {
            var ni = SoloAlfanum((string?)f.Numero);
            var tel = Ocho((string?)f.Tel);
            var cor = ((string?)f.Correo ?? "").Trim().ToLowerInvariant();
            cand.Add(new Candidato("Prospecto", (long)f.Id, null, (string?)f.N1 ?? (string?)f.Nombre, (string?)f.N2, (string?)f.A1, (string?)f.A2, (DateTime?)f.Fn, "Prospecto",
                ni.Length > 0 ? [ni] : [], tel.Length > 0 ? [tel] : [], cor.Length > 0 ? [cor] : [],
                f.Numero is null ? "" : $"{f.Tipo} {f.Numero}", "Prospecto"));
        }

        // Puntuacion
        var res = new List<CoincidenciaResponse>();
        foreach (var k in cand)
        {
            var motivos = new List<string>();
            var sim = SimilitudNombre(q.Nombre1, q.Nombre2, q.Apellido1, q.Apellido2, k.N1, k.N2, k.A1, k.A2);
            var idIgual = numeros.Any(k.Idents.Contains);
            var fnIgual = fn is not null && k.Fn is not null && k.Fn.Value.Date == fn.Value;
            var contacto = tels.Any(k.Tels.Contains) || correos.Any(k.Correos.Contains);

            double score;
            if (idIgual) { score = 1.0; motivos.Add("Misma identificación (puede estar registrada con otro tipo)"); }
            else
            {
                score = (fn is null || k.Fn is null) ? sim * 0.8 : 0.65 * sim + (fnIgual ? 0.35 : 0);
                if (contacto && sim >= 0.6) score = Math.Min(1.0, score + 0.10);
            }
            if (sim >= 0.8) motivos.Add($"Nombre muy parecido ({Math.Round(sim * 100)} %)");
            if (fnIgual) motivos.Add("Misma fecha de nacimiento");
            if (tels.Any(k.Tels.Contains)) motivos.Add("Mismo teléfono");
            if (correos.Any(k.Correos.Contains)) motivos.Add("Mismo correo");

            var pct = (int)Math.Round(score * 100);
            if (pct < alerta) continue;
            var nombre = string.Join(" ", new[] { k.N1, k.N2, k.A1, k.A2 }.Where(x => !string.IsNullOrWhiteSpace(x)));
            res.Add(new CoincidenciaResponse(k.Origen, k.Id, k.Numero, nombre, k.Fn, k.TextoIdents, k.Roles, k.Estado, pct, motivos, pct >= fuerte));
        }
        return new CoincidenciasResultado(res.OrderByDescending(x => x.Porcentaje).Take(MaxResultados).ToList(), alerta, fuerte, bloquea);
    }

    // ----- Organizaciones -----------------------------------------------------------------------

    private static readonly System.Text.RegularExpressions.Regex SufijoSocietario = new(
        @"\b(SOCIEDAD ANONIMA|SOCIEDAD DE RESPONSABILIDAD LIMITADA|S R L|S A|SRL|SA|LTDA|LIMITADA|EIRL|INC|LLC|CORP|CIA)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Nombre de una organizacion sin mayusculas, tildes ni sufijos societarios (S.A., Ltda., SRL...), para compararla.</summary>
    public static string NormalizarOrganizacion(string? s)
        => string.Join(' ', SufijoSocietario.Replace(Normalizar(s), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record CandidatoOrg(long Id, string Numero, string Estado, string Razon, List<string> Comerciales, DateTime? Fc,
        HashSet<string> Idents, HashSet<string> Tels, HashSet<string> Correos, string TextoIdents, string Roles);

    /// <summary>
    /// Duplicados de una ORGANIZACION: identificacion igual = 100; si no, similitud de razon social / nombres comerciales
    /// (sin S.A., Ltda...) con hasta 90 %, mas 10 si coincide la fecha de constitucion y 10 si coincide telefono o correo. Solo compara contra organizaciones.
    /// </summary>
    private static async Task<CoincidenciasResultado> BuscarOrganizacionAsync(SqlConnection c, SqlTransaction? tx, long idCompania, CoincidenciaRequest q)
    {
        var (alerta, fuerte, bloquea) = await UmbralesAsync(c, tx, idCompania);
        var vacio = new CoincidenciasResultado([], alerta, fuerte, bloquea);

        var numeros = (q.Identificaciones ?? []).Select(i => SoloAlfanum(i.Numero)).Where(x => x.Length > 0).Distinct().ToList();
        var tels = (q.Telefonos ?? []).Select(Ocho).Where(x => x.Length > 0).Distinct().ToList();
        var correos = (q.Correos ?? []).Select(x => (x ?? "").Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        var fc = q.FechaConstitucion?.Date;
        var mios = new[] { q.RazonSocial }.Concat(q.NombresComerciales ?? []).Select(NormalizarOrganizacion).Where(x => x.Length > 0).Distinct().ToList();
        if (mios.Count == 0 && numeros.Count == 0) return vacio;

        // Palabra mas larga de cada nombre: filtro grueso en SQL (la comparacion fina se hace en memoria).
        var tokens = mios.Select(m => m.Split(' ').OrderByDescending(t => t.Length).First()).Where(t => t.Length >= 3)
            .Select(t => "%" + t.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%").Distinct().ToList();
        var p = new DynamicParameters();
        p.Add("idCompania", idCompania);
        p.Add("numeros", numeros.Count > 0 ? numeros : new List<string> { "#" });
        p.Add("tels", tels.Count > 0 ? tels : new List<string> { "#" });
        p.Add("correos", correos.Count > 0 ? correos : new List<string> { "#" });
        p.Add("exInterlocutor", q.ExcluirInterlocutor);
        p.Add("top", MaxCandidatos);
        for (var n = 0; n < tokens.Count; n++) p.Add($"t{n}", tokens[n]);
        var condNombre = tokens.Count == 0 ? "1 = 0" : string.Join(" OR ", tokens.Select((_, n) =>
            $"j.nombre_org1 COLLATE Latin1_General_CI_AI LIKE @t{n} OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_NOMBRE_COMERCIAL nc WHERE nc.id_interlocutor = i.id_interlocutor AND nc.estado = 'Activo' AND nc.nombre COLLATE Latin1_General_CI_AI LIKE @t{n})"));
        const string digitos = "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ct.valor, ' ', ''), '-', ''), '+', ''), '(', ''), ')', '')";

        var filas = (await c.QueryAsync(
            $"""
            SELECT TOP (@top) i.id_interlocutor AS Id, i.numero AS Numero, i.estado AS Estado, j.nombre_org1 AS Razon, j.fecha_constitucion AS Fc
            FROM cli.INTERLOCUTOR i
            JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania AND i.estado <> 'Fusionado' AND (@exInterlocutor IS NULL OR i.id_interlocutor <> @exInterlocutor)
              AND ( EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_IDENTIFICACION x WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo'
                            AND UPPER(REPLACE(REPLACE(REPLACE(x.numero_identificacion, ' ', ''), '-', ''), '.', '')) IN @numeros)
                 OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_CONTACTO ct WHERE ct.id_interlocutor = i.id_interlocutor AND ct.estado = 'Activo'
                            AND (LOWER(ct.valor) IN @correos OR RIGHT({digitos}, 8) IN @tels))
                 OR {condNombre} )
            """, p, tx)).ToList();

        var cand = new List<CandidatoOrg>();
        if (filas.Count > 0)
        {
            var ids = filas.Select(f => (long)f.Id).ToList();
            var idents = (await c.QueryAsync(
                """
                SELECT x.id_interlocutor AS Id, t.nombre AS Tipo, x.numero_identificacion AS Numero
                FROM cli.INTERLOCUTOR_IDENTIFICACION x JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_identificacion
                WHERE x.estado = 'Activo' AND x.id_interlocutor IN @ids
                """, new { ids }, tx)).ToList();
            var contactos = (await c.QueryAsync(
                "SELECT id_interlocutor AS Id, valor AS Valor FROM cli.INTERLOCUTOR_CONTACTO WHERE estado = 'Activo' AND id_interlocutor IN @ids", new { ids }, tx)).ToList();
            var roles = (await c.QueryAsync(
                """
                SELECT r.id_interlocutor AS Id, it.nombre AS Nombre FROM cli.INTERLOCUTOR_ROL r
                JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol WHERE r.estado = 'Activo' AND r.id_interlocutor IN @ids
                """, new { ids }, tx)).ToList();
            var noms = (await c.QueryAsync(
                "SELECT id_interlocutor AS Id, nombre AS Nombre FROM cli.INTERLOCUTOR_NOMBRE_COMERCIAL WHERE estado = 'Activo' AND id_interlocutor IN @ids", new { ids }, tx)).ToList();
            foreach (var f in filas)
            {
                long id = f.Id;
                var cons = contactos.Where(x => (long)x.Id == id).Select(x => (string)x.Valor).ToList();
                cand.Add(new CandidatoOrg(id, (string)f.Numero, (string)f.Estado, (string)f.Razon,
                    noms.Where(x => (long)x.Id == id).Select(x => (string)x.Nombre).ToList(), (DateTime?)f.Fc,
                    idents.Where(x => (long)x.Id == id).Select(x => SoloAlfanum((string)x.Numero)).ToHashSet(),
                    cons.Select(Ocho).Where(x => x.Length > 0).ToHashSet(),
                    cons.Where(x => x.Contains('@')).Select(x => x.Trim().ToLowerInvariant()).ToHashSet(),
                    string.Join(" · ", idents.Where(x => (long)x.Id == id).Select(x => $"{x.Tipo} {x.Numero}")),
                    string.Join(", ", roles.Where(r => (long)r.Id == id).Select(r => (string)r.Nombre))));
            }
        }

        var res = new List<CoincidenciaResponse>();
        foreach (var k in cand)
        {
            var motivos = new List<string>();
            var suyos = new[] { k.Razon }.Concat(k.Comerciales).Select(NormalizarOrganizacion).Where(x => x.Length > 0).ToList();
            var sim = 0.0;
            foreach (var a in mios) foreach (var b in suyos) sim = Math.Max(sim, Math.Max(Ratio(a, b), Ratio(Ordenado(a), Ordenado(b))));
            var idIgual = numeros.Any(k.Idents.Contains);
            var fcIgual = fc is not null && k.Fc is not null && k.Fc.Value.Date == fc.Value;
            var contacto = tels.Any(k.Tels.Contains) || correos.Any(k.Correos.Contains);

            double score;
            if (idIgual) { score = 1.0; motivos.Add("Misma identificación"); }
            else
            {
                score = sim * 0.9;
                if (sim >= 0.6 && fcIgual) score += 0.10;
                if (sim >= 0.6 && contacto) score += 0.10;
                score = Math.Min(1.0, score);
            }
            if (sim >= 0.8) motivos.Add($"Razón social o nombre comercial muy parecido ({Math.Round(sim * 100)} %)");
            if (fcIgual) motivos.Add("Misma fecha de constitución");
            if (tels.Any(k.Tels.Contains)) motivos.Add("Mismo teléfono");
            if (correos.Any(k.Correos.Contains)) motivos.Add("Mismo correo");

            var pct = (int)Math.Round(score * 100);
            if (pct < alerta) continue;
            res.Add(new CoincidenciaResponse(k.Roles.Contains("Cliente") ? "Cliente" : "Organización", k.Id, k.Numero, k.Razon, k.Fc, k.TextoIdents, k.Roles, k.Estado, pct, motivos, pct >= fuerte));
        }
        return new CoincidenciasResultado(res.OrderByDescending(x => x.Porcentaje).Take(MaxResultados).ToList(), alerta, fuerte, bloquea);
    }

    /// <summary>Texto que queda en la gestion con lo que se reviso y lo que decidio quien registra.</summary>
    /// <summary>
    /// Altas por API con coincidencia fuerte: si la compania bloquea duplicados fuertes, solo aprueba quien tiene
    /// CLI_DUPLICADO_AUTORIZAR (el usuario tecnico de la integracion no tiene ese permiso ni aprueba).
    /// </summary>
    public static async Task<bool> AprobacionBloqueadaAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion)
    {
        if (actor.TienePermiso(PermisoAutorizar)) return false;
        var resumen = await c.ExecuteScalarAsync<string?>(
            "SELECT valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion AND nombre_campo = N'dup_revision'", new { idGestion }, tx);
        if (resumen is null || !resumen.StartsWith("[API]", StringComparison.Ordinal) || !resumen.Contains("(fuerte)", StringComparison.Ordinal)) return false;
        return string.Equals(await ParametroRepositorio.ValorAsync(c, tx, actor.IdCompania, ParamBloquea), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resumen para altas por API (nadie confirmo el motivo): la gestion exige validacion humana.</summary>
    public static string ResumenAutomatico(CoincidenciasResultado r)
    {
        var lista = string.Join("; ", r.Items.Take(3).Select(i => $"{(i.Numero ?? i.Origen)} {i.Nombre} ({i.Porcentaje} %)"));
        return $"Alta por API con {r.Items.Count} coincidencia(s), la mayor de {r.Items.Max(i => i.Porcentaje)} %{(r.Items.Any(i => i.Fuerte) ? " (fuerte)" : "")}: {lista}. " +
               "Requiere validacion de una persona: confirme que no es la misma persona antes de aprobar.";
    }

    public static string Resumen(CoincidenciasResultado r, string motivo)
    {
        var lista = string.Join("; ", r.Items.Take(3).Select(i => $"{(i.Numero ?? i.Origen)} {i.Nombre} ({i.Porcentaje} %)"));
        return $"Se revisaron {r.Items.Count} coincidencia(s), la mayor de {r.Items.Max(i => i.Porcentaje)} %{(r.Items.Any(i => i.Fuerte) ? " (fuerte)" : "")}: {lista}. " +
               $"Quien registra indicó que NO es la misma persona. Motivo: {motivo.Trim()}";
    }
}

/// <summary>Acceso por la conexion del cliente (tenant) para la pantalla de revision.</summary>
public class CoincidenciasRepositorio
{
    private readonly TenantContext _tenant;
    public CoincidenciasRepositorio(TenantContext tenant) { _tenant = tenant; }

    public async Task<CoincidenciasResultado> BuscarAsync(long idCompania, CoincidenciaRequest q)
    {
        using var c = new SqlConnection(_tenant.ConnectionString);
        return await MotorCoincidencias.BuscarAsync(c, null, idCompania, q);
    }
}
