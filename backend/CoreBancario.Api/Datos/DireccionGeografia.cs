using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Configuracion de direccion de un pais, con el codigo postal.</summary>
public record ConfigDireccion(
    long IdPais, string PaisNombre, IReadOnlyList<NivelConfigResponse> Niveles,
    string? EtiquetaCodigoPostal, string? PatronCodigoPostal, bool CodigoPostalObligatorio, bool EsPorDefecto)
{
    public const int MaxNiveles = 4;

    /// <summary>Pais sin configuracion propia: 4 niveles de texto libre, codigo postal opcional y sin formato.</summary>
    public static ConfigDireccion PorDefecto(long idPais, string paisNombre) => new(
        idPais, paisNombre,
        [
            new(1, "Provincia / Estado", "Texto", false),
            new(2, "Ciudad / Canton", "Texto", false),
            new(3, "Distrito / Zona", "Texto", false),
            new(4, "Barrio", "Texto", false),
        ],
        "Codigo postal", null, false, true);

    public DireccionConfigResponse ARespuesta() =>
        new(IdPais, PaisNombre, Niveles, EtiquetaCodigoPostal, PatronCodigoPostal, CodigoPostalObligatorio, EsPorDefecto);
}

/// <summary>
/// Direccion por pais con niveles configurables (gen.PAIS_DIRECCION_NIVEL, gen.DIVISION_TERRITORIAL):
/// carga de configuracion y validacion/normalizacion de las direcciones de un interlocutor.
/// Un nivel de LISTA guarda el id de la division (y el texto se resuelve al leer); uno de TEXTO guarda el texto.
/// </summary>
public static class DireccionGeografia
{
    public const string ListaPais = "PAIS";

    /// <summary>Paises (items activos de la lista PAIS) -> nombre. Solo incluye los pedidos.</summary>
    private static async Task<Dictionary<long, (string Nombre, string Estado)>> CargarPaisesAsync(
        SqlConnection c, SqlTransaction? tx, IEnumerable<long> ids)
    {
        var lista = ids.Distinct().ToList();
        if (lista.Count == 0) return [];
        var filas = await c.QueryAsync(
            """
            SELECT i.id_lista_valores_item AS id, i.nombre, i.estado
            FROM gen.LISTA_VALORES_ITEM i
            JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = @lista AND i.id_lista_valores_item IN @lista2
            """, new { lista = ListaPais, lista2 = lista }, tx);
        return filas.ToDictionary(f => (long)f.id, f => ((string)f.nombre, (string)f.estado));
    }

    /// <summary>Configuracion de cada pais pedido (o la de por defecto si no tiene).</summary>
    public static async Task<Dictionary<long, ConfigDireccion>> CargarConfigsAsync(
        SqlConnection c, SqlTransaction? tx, IEnumerable<long> idsPais)
    {
        var ids = idsPais.Distinct().ToList();
        var resultado = new Dictionary<long, ConfigDireccion>();
        if (ids.Count == 0) return resultado;

        var paises = await CargarPaisesAsync(c, tx, ids);

        var cabeceras = (await c.QueryAsync(
            "SELECT id_pais, etiqueta_codigo_postal, patron_codigo_postal, codigo_postal_obligatorio FROM gen.PAIS_DIRECCION_CONFIG WHERE id_pais IN @ids",
            new { ids }, tx)).ToDictionary(x => (long)x.id_pais);

        var niveles = (await c.QueryAsync(
            "SELECT id_pais, nivel, nombre, modo, es_obligatorio FROM gen.PAIS_DIRECCION_NIVEL WHERE id_pais IN @ids ORDER BY id_pais, nivel",
            new { ids }, tx)).GroupBy(x => (long)x.id_pais)
            .ToDictionary(g => g.Key, g => g.Select(x => new NivelConfigResponse((int)(byte)x.nivel, (string)x.nombre, (string)x.modo, (bool)x.es_obligatorio)).ToList());

        foreach (var id in ids)
        {
            var nombre = paises.TryGetValue(id, out var p) ? p.Nombre : $"Pais {id}";
            if (cabeceras.TryGetValue(id, out var cab) && niveles.TryGetValue(id, out var nv) && nv.Count > 0)
                resultado[id] = new ConfigDireccion(id, nombre, nv, (string?)cab.etiqueta_codigo_postal,
                    (string?)cab.patron_codigo_postal, (bool)cab.codigo_postal_obligatorio, false);
            else
                resultado[id] = ConfigDireccion.PorDefecto(id, nombre);
        }
        return resultado;
    }

    private sealed record Div(long Id, long IdPais, int Nivel, long? Padre, string Nombre, string Estado);

    /// <summary>
    /// Valida las direcciones contra la configuracion de su pais y devuelve las direcciones normalizadas
    /// (4 niveles: id solo en niveles de lista, texto solo en niveles de texto; sin textos sobrantes).
    /// </summary>
    public static async Task<(List<string> Errores, List<DireccionDto> Normalizadas)> ValidarAsync(
        SqlConnection c, SqlTransaction? tx, IReadOnlyList<DireccionDto>? direcciones, bool exigirNiveles = true, bool exigirCodigoPostal = true)
    {
        var errores = new List<string>();
        var normalizadas = new List<DireccionDto>();
        var lista = direcciones ?? [];
        if (lista.Count == 0) return (errores, normalizadas);

        var paises = await CargarPaisesAsync(c, tx, lista.Select(d => d.IdPais));
        var configs = await CargarConfigsAsync(c, tx, lista.Where(d => paises.ContainsKey(d.IdPais)).Select(d => d.IdPais));

        var idsDiv = lista.SelectMany(d => d.Niveles ?? []).Where(n => n.Id is not null).Select(n => n.Id!.Value).Distinct().ToList();
        var divisiones = idsDiv.Count == 0 ? new Dictionary<long, Div>()
            : (await c.QueryAsync(
                "SELECT id_division, id_pais, nivel, id_division_padre, nombre, estado FROM gen.DIVISION_TERRITORIAL WHERE id_division IN @ids",
                new { ids = idsDiv }, tx))
                .ToDictionary(x => (long)x.id_division,
                    x => new Div((long)x.id_division, (long)x.id_pais, (int)(byte)x.nivel, (long?)x.id_division_padre, (string)x.nombre, (string)x.estado));

        for (var n = 0; n < lista.Count; n++)
        {
            var d = lista[n];
            var et = $"Direccion {n + 1}";

            if (!paises.TryGetValue(d.IdPais, out var pais) || pais.Estado != "Activo")
            {
                errores.Add($"{et}: el pais no existe o esta inactivo.");
                continue;
            }
            var cfg = configs[d.IdPais];
            var entrada = d.Niveles ?? [];
            var salida = new List<NivelDireccionDto>();
            long? padre = null;      // id de la division elegida en el nivel anterior (si fue de lista)
            var cadenaOk = true;     // false si un nivel anterior de lista quedo sin elegir

            foreach (var nivel in cfg.Niveles.OrderBy(x => x.Nivel))
            {
                var e = nivel.Nivel - 1 < entrada.Count ? entrada[nivel.Nivel - 1] : new NivelDireccionDto(null, null);
                var texto = string.IsNullOrWhiteSpace(e.Texto) ? null : e.Texto.Trim();

                if (nivel.Modo == "Texto")
                {
                    if (texto is null && nivel.EsObligatorio && exigirNiveles) errores.Add($"{et}: {nivel.Nombre} es obligatorio.");
                    else if (texto is not null && texto.Length > 150) errores.Add($"{et}: {nivel.Nombre} supera los 150 caracteres.");
                    salida.Add(new NivelDireccionDto(null, texto));
                    padre = null;
                    continue;
                }

                // Nivel de lista
                if (e.Id is null)
                {
                    // Sin eleccion: es un error solo si es obligatorio Y existen opciones para elegir.
                    if (nivel.EsObligatorio && exigirNiveles && cadenaOk)
                    {
                        var hayOpciones = await c.ExecuteScalarAsync<bool>(
                            """
                            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.DIVISION_TERRITORIAL
                                WHERE id_pais = @pais AND nivel = @nivel AND estado = 'Activo'
                                  AND ((@padre IS NULL AND id_division_padre IS NULL) OR id_division_padre = @padre)) THEN 1 ELSE 0 END
                            """, new { pais = d.IdPais, nivel = nivel.Nivel, padre }, tx);
                        if (hayOpciones) errores.Add($"{et}: {nivel.Nombre} es obligatorio.");
                    }
                    cadenaOk = false;
                    salida.Add(new NivelDireccionDto(null, null));
                    continue;
                }

                if (!cadenaOk)
                {
                    errores.Add($"{et}: elija primero el nivel anterior a {nivel.Nombre}.");
                    salida.Add(new NivelDireccionDto(null, null));
                    continue;
                }
                if (!divisiones.TryGetValue(e.Id.Value, out var div) || div.IdPais != d.IdPais || div.Nivel != nivel.Nivel)
                {
                    errores.Add($"{et}: {nivel.Nombre} no es valido para el pais elegido.");
                    cadenaOk = false;
                    salida.Add(new NivelDireccionDto(null, null));
                    continue;
                }
                if (nivel.Nivel > 1 && div.Padre != padre)
                {
                    errores.Add($"{et}: {nivel.Nombre} no pertenece al nivel anterior elegido.");
                    cadenaOk = false;
                    salida.Add(new NivelDireccionDto(null, null));
                    continue;
                }
                // Una division inactiva no se ofrece en altas nuevas.
                if (div.Estado != "Activo") errores.Add($"{et}: {nivel.Nombre} '{div.Nombre}' esta inactivo.");
                salida.Add(new NivelDireccionDto(div.Id, null));
                padre = div.Id;
            }

            while (salida.Count < ConfigDireccion.MaxNiveles) salida.Add(new NivelDireccionDto(null, null));

            // Codigo postal
            var cp = string.IsNullOrWhiteSpace(d.CodigoPostal) ? null : d.CodigoPostal.Trim();
            if (cfg.EtiquetaCodigoPostal is null) cp = null;
            else if (cp is null)
            {
                if (cfg.CodigoPostalObligatorio && exigirCodigoPostal) errores.Add($"{et}: {cfg.EtiquetaCodigoPostal} es obligatorio.");
            }
            else if (cp.Length > 20) errores.Add($"{et}: {cfg.EtiquetaCodigoPostal} supera los 20 caracteres.");
            else if (!string.IsNullOrWhiteSpace(cfg.PatronCodigoPostal) && !Regex.IsMatch(cp, cfg.PatronCodigoPostal, RegexOptions.None, TimeSpan.FromSeconds(1)))
                errores.Add($"{et}: {cfg.EtiquetaCodigoPostal} no tiene un formato valido para {cfg.PaisNombre}.");

            normalizadas.Add(d with { Niveles = salida, CodigoPostal = cp });
        }

        return (errores, normalizadas);
    }

    /// <summary>
    /// Niveles legibles de una direccion guardada (para consulta y edicion). Los de lista muestran el nombre de
    /// la division; si un dato antiguo quedo como texto en un nivel de lista, se muestra ese texto.
    /// </summary>
    public static List<NivelDireccionResponse> ConstruirNiveles(
        ConfigDireccion cfg, IReadOnlyList<(long? Id, string? Texto, string? NombreDivision)> guardados)
    {
        var resultado = new List<NivelDireccionResponse>();
        foreach (var nivel in cfg.Niveles.OrderBy(n => n.Nivel))
        {
            var g = nivel.Nivel - 1 < guardados.Count ? guardados[nivel.Nivel - 1] : (null, null, null);
            var texto = nivel.Modo == "Lista" ? (g.NombreDivision ?? g.Texto) : g.Texto;
            if (nivel.Modo == "Texto" && string.IsNullOrWhiteSpace(texto) && g.NombreDivision is not null) texto = g.NombreDivision;
            resultado.Add(new NivelDireccionResponse(nivel.Nivel, nivel.Nombre, nivel.Modo, nivel.Modo == "Lista" ? g.Id : null, texto));
        }
        return resultado;
    }
}
