using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Relaciones entre interlocutores (representante legal, accionista, beneficiario final, persona de contacto, miembro de grupo, familiar).
/// Todo cambio es la gestion CLI_RELACION (llave = id del interlocutor desde cuya ficha se pide). Cada tipo declara en
/// cli.TIPO_RELACION_CONFIG que tipos de interlocutor admite en cada extremo, si usa porcentajes y si es simetrico.
/// Origen = quien «es [tipo] de» el destino. La baja cierra la relacion con fecha (historial), nunca la borra.
/// </summary>
public static class RelacionServicio
{
    public const string Entidad = "INTERLOCUTOR_RELACION";
    public const string CampoMotivo = "motivo_relacion";
    public const string PrefijoBaja = "relbaja[";
    public static readonly System.Text.RegularExpressions.Regex PatronMod =
        new(@"^relmod\[(\d+)\]\.([a-z0-9_]+)$", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static readonly System.Text.RegularExpressions.Regex PatronNueva =
        new(@"^rel\[\+(\d+)\]\.([a-z0-9_]+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public sealed record TipoCfg(long Id, string Codigo, string Nombre, string Inversa, bool UsaPct, bool Simetrica, string Estado, string[] Origen, string[] Destino);
    public sealed record Calculo(List<string> Errores, string Numero, string Nombre, string Tipo, IReadOnlyList<TipoCfg> Tipos);
    private sealed record Otro(long Id, string Numero, string Nombre, string Tipo, string Estado);

    private static string? NullSiVacio(string? t) => string.IsNullOrWhiteSpace(t) ? null : t;
    private static string Texto(decimal? d) => d?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

    public static async Task<List<TipoCfg>> TiposAsync(SqlConnection c, SqlTransaction? tx)
        => (await c.QueryAsync(
            """
            SELECT i.id_lista_valores_item AS id, i.codigo, i.nombre, i.estado, f.etiqueta_inversa, f.usa_porcentajes, f.es_simetrica, f.tipos_origen, f.tipos_destino
            FROM cli.TIPO_RELACION_CONFIG f JOIN gen.LISTA_VALORES_ITEM i ON i.id_lista_valores_item = f.id_tipo_relacion
            ORDER BY i.orden, i.nombre
            """, null, tx))
            .Select(x => new TipoCfg((long)x.id, (string)x.codigo, (string)x.nombre, (string)x.etiqueta_inversa, (bool)x.usa_porcentajes, (bool)x.es_simetrica, (string)x.estado,
                ((string)x.tipos_origen).Split(',', StringSplitOptions.RemoveEmptyEntries), ((string)x.tipos_destino).Split(',', StringSplitOptions.RemoveEmptyEntries))).ToList();

    private static async Task<Otro?> InterlocutorAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long id)
    {
        var x = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.id_interlocutor, i.numero, i.tipo_persona, i.estado,
                   COALESCE(j.nombre_org1, LTRIM(RTRIM(CONCAT(f.nombre1, N' ', f.nombre2, N' ', f.apellido1, N' ', f.apellido2)))) AS nombre
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE i.id_interlocutor = @id AND i.id_compania = @idCompania
            """, new { id, idCompania }, tx);
        if (x is null) return null;
        return new Otro((long)x.id_interlocutor, (string)x.numero, System.Text.RegularExpressions.Regex.Replace((string?)x.nombre ?? "", @"\s+", " ").Trim(), (string)x.tipo_persona, (string)x.estado);
    }

    public static string EtiquetaDe(TipoCfg t, string sentido) => sentido == "Origen" || t.Simetrica ? t.Nombre : t.Inversa;

    // ----- Validacion ---------------------------------------------------------------------------

    public static async Task<Calculo> ValidarAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor,
        IReadOnlyList<RelacionNuevaDto> agregar, IReadOnlyList<long> cerrar, IReadOnlyList<RelacionEdicionDto> modificar, long? idGestionActual)
    {
        var errores = new List<string>();
        var tipos = await TiposAsync(c, tx);
        var yo = await InterlocutorAsync(c, tx, idCompania, idInterlocutor);
        if (yo is null) { errores.Add("El interlocutor no existe."); return new(errores, "", "", "", tipos); }
        if (yo.Estado != "Activo") errores.Add($"El interlocutor N.º {yo.Numero} está '{yo.Estado}' y no admite cambios de relaciones.");
        var pend = await FusionServicio.GestionPendienteAsync(c, tx, idCompania, idInterlocutor, idGestionActual);
        if (pend is not null) errores.Add($"El interlocutor N.º {yo.Numero} tiene la gestión {pend} pendiente de aprobación; debe resolverse antes.");
        if (agregar.Count == 0 && cerrar.Count == 0 && modificar.Count == 0) errores.Add("Indique al menos una relación a agregar, modificar o inactivar.");

        var vistas = new HashSet<string>();
        var otros = new Dictionary<long, Otro?>();
        var nuevasEnSolicitud = new List<(long O, long D, TipoCfg T, decimal? P)>();
        var miembrosNuevosPorGrupo = new Dictionary<long, int>();
        var n = 0;
        foreach (var r in agregar)
        {
            n++;
            var pre = $"Relación nueva #{n}: ";
            var t = tipos.FirstOrDefault(x => x.Id == r.IdTipoRelacion);
            if (t is null) { errores.Add(pre + "el tipo de relación no existe."); continue; }
            if (t.Estado != "Activo") { errores.Add(pre + $"el tipo «{t.Nombre}» está inactivo."); continue; }
            if (r.Sentido is not ("Origen" or "Destino")) { errores.Add(pre + "sentido inválido."); continue; }
            if (r.IdOtro == idInterlocutor) { errores.Add(pre + "un interlocutor no puede relacionarse consigo mismo."); continue; }
            if (!otros.TryGetValue(r.IdOtro, out var otro)) otros[r.IdOtro] = otro = await InterlocutorAsync(c, tx, idCompania, r.IdOtro);
            if (otro is null) { errores.Add(pre + "el otro interlocutor no existe."); continue; }
            if (otro.Estado != "Activo") { errores.Add(pre + $"el interlocutor N.º {otro.Numero} está '{otro.Estado}'."); continue; }

            var (idO, tipoO, idD, tipoD) = r.Sentido == "Origen" ? (idInterlocutor, yo.Tipo, otro.Id, otro.Tipo) : (otro.Id, otro.Tipo, idInterlocutor, yo.Tipo);
            string Nom(string tp) => tp switch { "Fisica" => "persona", "Juridica" => "organización", _ => "grupo" };
            if (!t.Origen.Contains(tipoO)) { errores.Add(pre + $"«{t.Nombre}»: quien la ejerce no puede ser {Nom(tipoO)}."); continue; }
            if (!t.Destino.Contains(tipoD)) { errores.Add(pre + $"«{t.Nombre}»: el destino no puede ser {Nom(tipoD)}."); continue; }

            foreach (var (valor, etiqueta) in new[] { (r.PorcentajeParticipacion, "participación"), (r.PorcentajeVoto, "voto"), (r.PorcentajeControl, "control") })
            {
                if (valor is null) continue;
                if (!t.UsaPct) errores.Add(pre + $"«{t.Nombre}» no usa porcentajes.");
                else if (valor < 0 || valor > 100) errores.Add(pre + $"el porcentaje de {etiqueta} debe estar entre 0 y 100.");
            }
            if (r.Funcion is { Length: > 200 }) errores.Add(pre + "la función no puede superar 200 caracteres.");
            if (r.FechaInicio is { } fi && fi > DateOnly.FromDateTime(DateTime.UtcNow)) errores.Add(pre + "la fecha de inicio no puede ser futura.");

            var clave = t.Simetrica ? $"{t.Id}|{Math.Min(idO, idD)}|{Math.Max(idO, idD)}" : $"{t.Id}|{idO}|{idD}";
            if (!vistas.Add(clave)) { errores.Add(pre + "está repetida en la solicitud."); continue; }
            var existe = await c.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM cli.INTERLOCUTOR_RELACION
                WHERE id_tipo_relacion = @t AND fecha_fin IS NULL
                  AND ((id_interlocutor_origen = @o AND id_interlocutor_destino = @d) OR (@sim = 1 AND id_interlocutor_origen = @d AND id_interlocutor_destino = @o))
                """, new { t = t.Id, o = idO, d = idD, sim = t.Simetrica ? 1 : 0 }, tx);
            if (existe > 0) { errores.Add(pre + $"ya existe una relación «{t.Nombre}» vigente entre N.º {(r.Sentido == "Origen" ? yo.Numero : otro.Numero)} y N.º {(r.Sentido == "Origen" ? otro.Numero : yo.Numero)}."); continue; }
            nuevasEnSolicitud.Add((idO, idD, t, r.PorcentajeParticipacion));
            if (t.Codigo == GrupoServicio.CodigoRelacionMiembro) miembrosNuevosPorGrupo[idD] = miembrosNuevosPorGrupo.GetValueOrDefault(idD) + 1;
        }

        // Modificaciones de relaciones vigentes (porcentajes, funcion, fecha de inicio, principal).
        foreach (var m in modificar)
        {
            var pre = $"Relación #{m.IdRelacion}: ";
            var rel = await c.QuerySingleOrDefaultAsync(
                "SELECT id_interlocutor_origen o, id_interlocutor_destino d, id_tipo_relacion t, fecha_fin, porcentaje_participacion pp, porcentaje_voto pv, porcentaje_control pc, es_principal prin, funcion fun, fecha_inicio fi FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_relacion = @id", new { id = m.IdRelacion }, tx);
            if (rel is null) { errores.Add(pre + "no existe."); continue; }
            if ((DateTime?)rel.fecha_fin is not null) { errores.Add(pre + "ya está inactiva y no se puede modificar."); continue; }
            if ((long)rel.o != idInterlocutor && (long)rel.d != idInterlocutor) { errores.Add(pre + "no pertenece a este interlocutor."); continue; }
            if (cerrar.Contains(m.IdRelacion)) { errores.Add(pre + "no puede modificarse e inactivarse a la vez."); continue; }
            var t = tipos.First(x => x.Id == (long)rel.t);
            foreach (var (valor, etiqueta) in new[] { (m.PorcentajeParticipacion, "participación"), (m.PorcentajeVoto, "voto"), (m.PorcentajeControl, "control") })
            {
                if (valor is null) continue;
                if (!t.UsaPct) errores.Add(pre + $"«{t.Nombre}» no usa porcentajes.");
                else if (valor < 0 || valor > 100) errores.Add(pre + $"el porcentaje de {etiqueta} debe estar entre 0 y 100.");
            }
            if (m.Funcion is { Length: > 200 }) errores.Add(pre + "la función no puede superar 200 caracteres.");
            if (m.FechaInicio is { } fi && fi > DateOnly.FromDateTime(DateTime.UtcNow)) errores.Add(pre + "la fecha de inicio no puede ser futura.");
            var igual = (decimal?)rel.pp == m.PorcentajeParticipacion && (decimal?)rel.pv == m.PorcentajeVoto && (decimal?)rel.pc == m.PorcentajeControl
                        && (bool)rel.prin == m.EsPrincipal && NullSiVacio((string?)rel.fun) == NullSiVacio(m.Funcion?.Trim())
                        && (m.FechaInicio is null || DateOnly.FromDateTime((DateTime)rel.fi) == m.FechaInicio);
            if (igual) { errores.Add(pre + "no tiene cambios."); continue; }
            nuevasEnSolicitud.Add(((long)rel.o, (long)rel.d, t, m.PorcentajeParticipacion));
        }

        // La participacion total de un tipo sobre un mismo destino no puede pasar de 100 %.
        var excluidas = cerrar.Concat(modificar.Select(x => x.IdRelacion)).Append(0).Distinct().ToList();
        foreach (var g in nuevasEnSolicitud.Where(x => x.T.UsaPct && x.P is not null).GroupBy(x => (x.D, x.T.Id)))
        {
            var actual = await c.ExecuteScalarAsync<decimal>(
                "SELECT ISNULL(SUM(porcentaje_participacion), 0) FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_destino = @d AND id_tipo_relacion = @t AND fecha_fin IS NULL AND id_interlocutor_relacion NOT IN (SELECT value FROM STRING_SPLIT(@excluidas, ','))",
                new { d = g.Key.D, t = g.Key.Id, excluidas = string.Join(',', excluidas) }, tx);
            if (actual + g.Sum(x => x.P!.Value) > 100)
                errores.Add($"La participación total «{g.First().T.Nombre}» sobre el destino superaría 100 % (vigente {Texto(actual)} % + solicitado {Texto(g.Sum(x => x.P!.Value))} %).");
        }

        var miembrosCerradosPorGrupo = new Dictionary<long, int>();
        foreach (var id in cerrar.Distinct())
        {
            var rel = await c.QuerySingleOrDefaultAsync(
                "SELECT id_interlocutor_origen, id_interlocutor_destino, id_tipo_relacion, fecha_fin FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_relacion = @id", new { id }, tx);
            if (rel is null) errores.Add($"La relación #{id} no existe.");
            else if ((DateTime?)rel.fecha_fin is not null) errores.Add($"La relación #{id} ya está cerrada.");
            else if ((long)rel.id_interlocutor_origen != idInterlocutor && (long)rel.id_interlocutor_destino != idInterlocutor) errores.Add($"La relación #{id} no pertenece a este interlocutor.");
            else if (tipos.FirstOrDefault(x => x.Id == (long)rel.id_tipo_relacion)?.Codigo == GrupoServicio.CodigoRelacionMiembro)
                miembrosCerradosPorGrupo[(long)rel.id_interlocutor_destino] = miembrosCerradosPorGrupo.GetValueOrDefault((long)rel.id_interlocutor_destino) + 1;
        }

        // Un grupo conserva siempre al menos 2 miembros vigentes (una persona u organizacion sola no forma un grupo).
        foreach (var (idGrupo, cerrados) in miembrosCerradosPorGrupo)
        {
            var vigentes = await c.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM cli.INTERLOCUTOR_RELACION r JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_relacion AND it.codigo = N'MIEMBRO_GRUPO'
                WHERE r.id_interlocutor_destino = @g AND r.fecha_fin IS NULL
                """, new { g = idGrupo }, tx);
            if (vigentes - cerrados + miembrosNuevosPorGrupo.GetValueOrDefault(idGrupo) < GrupoServicio.MinimoMiembros)
                errores.Add($"Un grupo debe conservar al menos {GrupoServicio.MinimoMiembros} miembros vigentes; con este cambio quedaría con menos.");
        }
        return new(errores, yo.Numero, yo.Nombre, yo.Tipo, tipos);
    }

    // ----- Consulta -----------------------------------------------------------------------------

    public static async Task<List<RelacionDetalleResponse>> ListarAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor, bool vigentes)
    {
        var tipos = await TiposAsync(c, tx);
        var filas = (await c.QueryAsync(
            $"""
            SELECT r.id_interlocutor_relacion, r.id_interlocutor_origen, r.id_interlocutor_destino, r.id_tipo_relacion,
                   r.porcentaje_participacion, r.porcentaje_voto, r.porcentaje_control, r.es_principal, r.funcion,
                   r.fecha_inicio, r.fecha_fin, r.creado_por, r.cerrado_por
            FROM cli.INTERLOCUTOR_RELACION r
            WHERE (r.id_interlocutor_origen = @id OR r.id_interlocutor_destino = @id) AND r.fecha_fin {(vigentes ? "IS NULL" : "IS NOT NULL")}
            ORDER BY r.fecha_inicio DESC, r.id_interlocutor_relacion DESC
            """, new { id = idInterlocutor }, tx)).ToList();
        var res = new List<RelacionDetalleResponse>();
        foreach (var f in filas)
        {
            var esOrigen = (long)f.id_interlocutor_origen == idInterlocutor;
            var idOtro = esOrigen ? (long)f.id_interlocutor_destino : (long)f.id_interlocutor_origen;
            var otro = await InterlocutorAsync(c, tx, idCompania, idOtro);
            var t = tipos.FirstOrDefault(x => x.Id == (long)f.id_tipo_relacion);
            if (otro is null || t is null) continue;
            var sentido = esOrigen ? "Origen" : "Destino";
            res.Add(new RelacionDetalleResponse(
                (long)f.id_interlocutor_relacion, idOtro, otro.Numero, otro.Nombre, otro.Tipo, t.Id, EtiquetaDe(t, sentido), sentido,
                (decimal?)f.porcentaje_participacion, (decimal?)f.porcentaje_voto, (decimal?)f.porcentaje_control, (bool)f.es_principal, (string?)f.funcion,
                (DateTime)f.fecha_inicio, (DateTime?)f.fecha_fin, (string?)f.creado_por, (string?)f.cerrado_por));
        }
        return res;
    }

    // ----- Detalle de la gestion ----------------------------------------------------------------

    /// <summary>Convierte la solicitud en las filas de apr.GESTION_DETALLE. El otro interlocutor se guarda como «id|N.º · nombre».</summary>
    public static async Task<List<(string Campo, string? Anterior, string? Valor)>> FilasAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, IReadOnlyList<RelacionNuevaDto> agregar, IReadOnlyList<long> cerrar, IReadOnlyList<RelacionEdicionDto> modificar, string motivo, IReadOnlyList<TipoCfg> tipos)
    {
        var filas = new List<(string, string?, string?)> { (CampoMotivo, null, motivo) };
        var n = 0;
        foreach (var r in agregar)
        {
            n++;
            var otro = await InterlocutorAsync(c, tx, idCompania, r.IdOtro);
            filas.Add(($"rel[+{n}].id_otro", null, $"{r.IdOtro}|{otro?.Numero} · {otro?.Nombre}"));
            filas.Add(($"rel[+{n}].id_tipo_relacion", null, r.IdTipoRelacion.ToString(CultureInfo.InvariantCulture)));
            filas.Add(($"rel[+{n}].sentido", null, r.Sentido));
            if (r.PorcentajeParticipacion is not null) filas.Add(($"rel[+{n}].porcentaje_participacion", null, Texto(r.PorcentajeParticipacion)));
            if (r.PorcentajeVoto is not null) filas.Add(($"rel[+{n}].porcentaje_voto", null, Texto(r.PorcentajeVoto)));
            if (r.PorcentajeControl is not null) filas.Add(($"rel[+{n}].porcentaje_control", null, Texto(r.PorcentajeControl)));
            if (r.EsPrincipal) filas.Add(($"rel[+{n}].es_principal", null, "1"));
            if (!string.IsNullOrWhiteSpace(r.Funcion)) filas.Add(($"rel[+{n}].funcion", null, r.Funcion!.Trim()));
            if (r.FechaInicio is not null) filas.Add(($"rel[+{n}].fecha_inicio", null, r.FechaInicio.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        foreach (var id in cerrar.Distinct())
        {
            var rel = await c.QuerySingleOrDefaultAsync("SELECT id_interlocutor_origen o, id_interlocutor_destino d, id_tipo_relacion t FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_relacion = @id", new { id }, tx);
            string desc = $"#{id}";
            if (rel is not null)
            {
                var o = await InterlocutorAsync(c, tx, idCompania, (long)rel.o); var d = await InterlocutorAsync(c, tx, idCompania, (long)rel.d);
                var t = tipos.FirstOrDefault(x => x.Id == (long)rel.t);
                desc = $"{o?.Numero} · {o?.Nombre} — {t?.Nombre} — {d?.Numero} · {d?.Nombre}";
            }
            filas.Add(($"{PrefijoBaja}{id}]", null, $"{id}|{desc}"));
        }
        foreach (var m in modificar.GroupBy(x => x.IdRelacion).Select(g => g.First()))
        {
            var cur = await c.QuerySingleOrDefaultAsync(
                "SELECT id_interlocutor_origen o, id_interlocutor_destino d, id_tipo_relacion t, porcentaje_participacion pp, porcentaje_voto pv, porcentaje_control pc, es_principal prin, funcion fun, fecha_inicio fi FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_relacion = @id",
                new { id = m.IdRelacion }, tx);
            if (cur is null) continue;
            var o = await InterlocutorAsync(c, tx, idCompania, (long)cur.o); var d = await InterlocutorAsync(c, tx, idCompania, (long)cur.d);
            var t = tipos.FirstOrDefault(x => x.Id == (long)cur.t);
            var k = $"relmod[{m.IdRelacion}]";
            filas.Add(($"{k}.relacion", null, $"{m.IdRelacion}|{o?.Numero} · {o?.Nombre} — {t?.Nombre} — {d?.Numero} · {d?.Nombre}"));
            void Cambio(string campo, string? antes, string? despues) { if (antes != despues) filas.Add(($"{k}.{campo}", antes, despues)); }
            Cambio("porcentaje_participacion", NullSiVacio(Texto((decimal?)cur.pp)), NullSiVacio(Texto(m.PorcentajeParticipacion)));
            Cambio("porcentaje_voto", NullSiVacio(Texto((decimal?)cur.pv)), NullSiVacio(Texto(m.PorcentajeVoto)));
            Cambio("porcentaje_control", NullSiVacio(Texto((decimal?)cur.pc)), NullSiVacio(Texto(m.PorcentajeControl)));
            Cambio("es_principal", (bool)cur.prin ? "1" : "0", m.EsPrincipal ? "1" : "0");
            Cambio("funcion", NullSiVacio((string?)cur.fun), NullSiVacio(m.Funcion?.Trim()));
            Cambio("fecha_inicio", ((DateTime)cur.fi).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                (m.FechaInicio ?? DateOnly.FromDateTime((DateTime)cur.fi)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        return filas;
    }

    public static (List<RelacionNuevaDto> Agregar, List<long> Cerrar, string Motivo, Dictionary<long, Dictionary<string, string?>> Modificar) LeerDetalle(IEnumerable<(string Campo, string? Valor)> filas)
    {
        var modificar = new Dictionary<long, Dictionary<string, string?>>();
        var porIndice = new SortedDictionary<int, Dictionary<string, string?>>();
        var cerrar = new List<long>(); var motivo = "";
        foreach (var (campo, valor) in filas)
        {
            if (campo == CampoMotivo) { motivo = valor ?? ""; continue; }
            if (campo.StartsWith(PrefijoBaja, StringComparison.Ordinal))
            {
                var txt = (valor ?? "").Split('|')[0];
                if (long.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) cerrar.Add(id);
                continue;
            }
            var mm = PatronMod.Match(campo);
            if (mm.Success)
            {
                var idm = long.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
                if (!modificar.TryGetValue(idm, out var dm)) modificar[idm] = dm = new();
                dm[mm.Groups[2].Value] = valor;
                continue;
            }
            var m = PatronNueva.Match(campo);
            if (!m.Success) continue;
            var i = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!porIndice.TryGetValue(i, out var d)) porIndice[i] = d = new();
            d[m.Groups[2].Value] = valor;
        }
        decimal? Dec(Dictionary<string, string?> d, string k) => d.TryGetValue(k, out var v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : null;
        var agregar = porIndice.Values.Select(d => new RelacionNuevaDto(
            long.TryParse((d.GetValueOrDefault("id_otro") ?? "").Split('|')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) ? o : 0,
            long.TryParse(d.GetValueOrDefault("id_tipo_relacion"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0,
            d.GetValueOrDefault("sentido") ?? "",
            Dec(d, "porcentaje_participacion"), Dec(d, "porcentaje_voto"), Dec(d, "porcentaje_control"),
            d.GetValueOrDefault("es_principal") == "1", d.GetValueOrDefault("funcion"),
            DateOnly.TryParseExact(d.GetValueOrDefault("fecha_inicio"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fi) ? fi : null)).ToList();
        return (agregar, cerrar, motivo, modificar);
    }

    // ----- Aplicar (al aprobar) -----------------------------------------------------------------

    public static async Task<ResultadoOperacion?> AplicarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, long idInterlocutor, string porQuien)
    {
        var filas = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion ORDER BY id_gestion_detalle", new { idGestion }, tx))
            .Select(x => ((string)x.nombre_campo, (string?)x.valor_nuevo)).ToList();
        var (agregar, cerrar, _, modificar) = LeerDetalle(filas);

        // Se vuelve a validar: entre la solicitud y la aprobacion pudo cambiar cualquiera de los dos interlocutores.
        // Las modificaciones guardan solo los campos que cambian; el resto se toma de lo vigente para revalidar el resultado final.
        var ediciones = new List<RelacionEdicionDto>();
        foreach (var (idRel, campos) in modificar)
        {
            var cur = await c.QuerySingleOrDefaultAsync(
                "SELECT porcentaje_participacion pp, porcentaje_voto pv, porcentaje_control pc, es_principal prin, funcion fun, fecha_inicio fi FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_relacion = @id", new { id = idRel }, tx);
            if (cur is null) { ediciones.Add(new RelacionEdicionDto(idRel)); continue; }
            decimal? Dec(string k, decimal? actual) => campos.TryGetValue(k, out var v) ? (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : null) : actual;
            ediciones.Add(new RelacionEdicionDto(idRel,
                Dec("porcentaje_participacion", (decimal?)cur.pp), Dec("porcentaje_voto", (decimal?)cur.pv), Dec("porcentaje_control", (decimal?)cur.pc),
                campos.TryGetValue("es_principal", out var ep) ? ep == "1" : (bool)cur.prin,
                campos.TryGetValue("funcion", out var fu) ? fu : (string?)cur.fun,
                campos.TryGetValue("fecha_inicio", out var fe) && DateOnly.TryParseExact(fe, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fd) ? fd : DateOnly.FromDateTime((DateTime)cur.fi)));
        }
        var calc = await ValidarAsync(c, tx, actor.IdCompania, idInterlocutor, agregar, cerrar, ediciones, idGestion);
        if (calc.Errores.Count > 0)
            return ResultadoOperacion.Fallo(409, "relacion_no_aplicable", "El cambio de relaciones ya no es válido: " + string.Join(" ", calc.Errores) + " Rechace o devuelva la gestión.");

        foreach (var id in cerrar.Distinct())
        {
            var rel = await c.QuerySingleAsync("SELECT id_interlocutor_origen o, id_interlocutor_destino d, id_tipo_relacion t FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_relacion = @id", new { id }, tx);
            await c.ExecuteAsync(
                "UPDATE cli.INTERLOCUTOR_RELACION SET fecha_fin = SYSUTCDATETIME(), cerrado_por = @por, id_gestion_baja = @idGestion WHERE id_interlocutor_relacion = @id",
                new { id, por = porQuien, idGestion }, tx);
            var tn = calc.Tipos.First(x => x.Id == (long)rel.t).Nombre;
            var o = await InterlocutorAsync(c, tx, actor.IdCompania, (long)rel.o); var d = await InterlocutorAsync(c, tx, actor.IdCompania, (long)rel.d);
            var texto = $"{tn}: {o?.Numero} · {o?.Nombre} → {d?.Numero} · {d?.Nombre}";
            foreach (var quien in new[] { (long)rel.o, (long)rel.d })
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", quien, "relaciones", $"{texto} (vigente)", $"{texto} (cerrada)", idGestion);
        }
        foreach (var e in ediciones)
        {
            var campos = modificar[e.IdRelacion];
            var rel = await c.QuerySingleAsync("SELECT id_interlocutor_origen o, id_interlocutor_destino d, id_tipo_relacion t FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_relacion = @id", new { id = e.IdRelacion }, tx);
            if (e.EsPrincipal && campos.TryGetValue("es_principal", out var epr) && epr == "1")
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_RELACION SET es_principal = 0 WHERE id_interlocutor_destino = @d AND id_tipo_relacion = @t AND fecha_fin IS NULL AND es_principal = 1 AND id_interlocutor_relacion <> @id",
                    new { d = (long)rel.d, t = (long)rel.t, id = e.IdRelacion }, tx);
            await c.ExecuteAsync(
                "UPDATE cli.INTERLOCUTOR_RELACION SET porcentaje_participacion = @pp, porcentaje_voto = @pv, porcentaje_control = @pc, es_principal = @prin, funcion = @fun, fecha_inicio = @fi WHERE id_interlocutor_relacion = @id",
                new
                {
                    id = e.IdRelacion, pp = e.PorcentajeParticipacion, pv = e.PorcentajeVoto, pc = e.PorcentajeControl, prin = e.EsPrincipal,
                    fun = string.IsNullOrWhiteSpace(e.Funcion) ? null : e.Funcion!.Trim(), fi = e.FechaInicio?.ToDateTime(TimeOnly.MinValue)
                }, tx);
            var tn = calc.Tipos.First(x => x.Id == (long)rel.t).Nombre;
            var o = await InterlocutorAsync(c, tx, actor.IdCompania, (long)rel.o); var d = await InterlocutorAsync(c, tx, actor.IdCompania, (long)rel.d);
            var texto = $"{tn}: {o?.Numero} · {o?.Nombre} → {d?.Numero} · {d?.Nombre}";
            var que = string.Join(", ", campos.Where(x => x.Key != "relacion").Select(x => x.Key switch
            {
                "porcentaje_participacion" => "% participación", "porcentaje_voto" => "% voto", "porcentaje_control" => "% control",
                "es_principal" => "principal", "funcion" => "función", "fecha_inicio" => "fecha de inicio", var k => k
            }));
            foreach (var quien in new[] { (long)rel.o, (long)rel.d })
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", quien, "relaciones", $"{texto} (vigente)", $"{texto} (modificada: {que})", idGestion);
        }
        foreach (var r in agregar)
        {
            var t = calc.Tipos.First(x => x.Id == r.IdTipoRelacion);
            var (idO, idD) = r.Sentido == "Origen" ? (idInterlocutor, r.IdOtro) : (r.IdOtro, idInterlocutor);
            if (r.EsPrincipal)
                await c.ExecuteAsync(
                    "UPDATE cli.INTERLOCUTOR_RELACION SET es_principal = 0 WHERE id_interlocutor_destino = @d AND id_tipo_relacion = @t AND fecha_fin IS NULL AND es_principal = 1",
                    new { d = idD, t = t.Id }, tx);
            await c.ExecuteAsync(
                """
                INSERT INTO cli.INTERLOCUTOR_RELACION
                    (id_interlocutor_origen, id_interlocutor_destino, id_tipo_relacion, porcentaje_participacion, porcentaje_voto, porcentaje_control,
                     es_principal, funcion, fecha_inicio, creado_por, creado_en, id_gestion_alta)
                VALUES (@o, @d, @t, @pp, @pv, @pc, @prin, @fun, ISNULL(@fi, SYSUTCDATETIME()), @por, SYSUTCDATETIME(), @idGestion)
                """,
                new
                {
                    o = idO, d = idD, t = t.Id, pp = r.PorcentajeParticipacion, pv = r.PorcentajeVoto, pc = r.PorcentajeControl,
                    prin = r.EsPrincipal, fun = string.IsNullOrWhiteSpace(r.Funcion) ? null : r.Funcion!.Trim(),
                    fi = r.FechaInicio?.ToDateTime(TimeOnly.MinValue), por = porQuien, idGestion
                }, tx);
            var no = await InterlocutorAsync(c, tx, actor.IdCompania, idO); var nd = await InterlocutorAsync(c, tx, actor.IdCompania, idD);
            var texto = $"{t.Nombre}: {no?.Numero} · {no?.Nombre} → {nd?.Numero} · {nd?.Nombre}";
            foreach (var quien in new[] { idO, idD })
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", quien, "relaciones", "sin relación", $"{texto} (vigente)", idGestion);
        }
        return null;
    }
}
