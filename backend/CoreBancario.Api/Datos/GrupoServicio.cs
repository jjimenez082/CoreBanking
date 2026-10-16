using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Interlocutor tipo GRUPO (grupo economico, familiar, solidario, consorcio...). Se da de alta por la gestion CLI_ALTA con
/// tipo_persona = Grupo. No tiene identificaciones, direcciones, contactos ni roles. Sus miembros son relaciones vigentes
/// MIEMBRO_GRUPO (persona u organizacion -> grupo) y un grupo debe tener siempre al menos 2.
/// El nombre del grupo se refleja tambien en cli.INTERLOCUTOR_JURIDICA.nombre_org1 para que listas, busquedas y nombres
/// de relaciones lo resuelvan igual que a cualquier otro interlocutor.
/// </summary>
public static class GrupoServicio
{
    public const string Tipo = "Grupo";
    public const string ListaTipoGrupo = "TIPO_GRUPO";
    public const string CodigoRelacionMiembro = "MIEMBRO_GRUPO";
    public const int MinimoMiembros = 2;
    public const string EstadoDisuelto = "Disuelto";

    public const string CampoTipoGrupo = "id_tipo_grupo";
    public const string CampoDescripcion = "grupo_descripcion";
    public const string CampoFormacion = "grupo_fecha_formacion";

    public sealed record Miembro(long Id, string Numero, string Nombre, string Tipo);
    public sealed record Calculo(List<string> Errores, IReadOnlyList<Miembro> Miembros, long IdTipoRelacion);

    /// <summary>Valida solo los datos propios del grupo (nombre, tipo, descripcion, fecha). idGrupoExcluir = el grupo que se esta editando.</summary>
    public static async Task<List<string>> ValidarDatosAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, string? nombreIn, long idTipoGrupo, string? descripcion, DateOnly? fechaFormacion,
        long? idGestionActual, long? idGrupoExcluir)
    {
        var errores = new List<string>();
        var nombre = nombreIn?.Trim() ?? "";
        if (nombre.Length == 0) errores.Add("El nombre del grupo es obligatorio.");
        else if (nombre.Length > 200) errores.Add("El nombre del grupo supera los 200 caracteres.");
        if (descripcion is { Length: > 500 }) errores.Add("La descripción del grupo supera los 500 caracteres.");
        if (fechaFormacion is { } ff && (ff > DateOnly.FromDateTime(DateTime.UtcNow) || ff.Year < 1800)) errores.Add("La fecha de formación no es válida.");

        var tipoOk = await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                                      WHERE t.codigo = @lista AND i.id_lista_valores_item = @id AND i.estado = 'Activo') THEN 1 ELSE 0 END
            """, new { lista = ListaTipoGrupo, id = idTipoGrupo }, tx);
        if (!tipoOk) errores.Add("El tipo de grupo es obligatorio y debe existir.");

        if (nombre.Length > 0)
        {
            var existente = await c.ExecuteScalarAsync<string?>(
                """
                SELECT TOP 1 i.numero FROM cli.INTERLOCUTOR i JOIN cli.INTERLOCUTOR_GRUPO x ON x.id_interlocutor = i.id_interlocutor
                WHERE i.id_compania = @idCompania AND i.estado = 'Activo' AND x.nombre_grupo1 = @nombre
                  AND (@excluir IS NULL OR i.id_interlocutor <> @excluir)
                """, new { idCompania, nombre, excluir = idGrupoExcluir }, tx);
            if (existente is not null) errores.Add($"Ya existe el grupo N.º {existente} con el nombre «{nombre}».");
            var pendiente = await c.ExecuteScalarAsync<string?>(
                """
                SELECT TOP 1 g.numero FROM apr.GESTION g
                JOIN apr.GESTION_DETALLE dt ON dt.id_gestion = g.id_gestion AND dt.nombre_campo = N'tipo_persona' AND dt.valor_nuevo = N'Grupo'
                JOIN apr.GESTION_DETALLE dn ON dn.id_gestion = g.id_gestion AND dn.nombre_campo = N'razon_social' AND dn.valor_nuevo = @nombre
                WHERE g.id_compania = @idCompania AND g.entidad_negocio = N'INTERLOCUTOR_ALTA' AND g.estado = N'Pendiente de Aprobacion'
                  AND (@idGestion IS NULL OR g.id_gestion <> @idGestion)
                """, new { idCompania, nombre, idGestion = idGestionActual }, tx);
            if (pendiente is not null) errores.Add($"El grupo «{nombre}» ya está en el alta pendiente de aprobación de la gestión {pendiente}.");
        }
        return errores;
    }

    /// <summary>Valida los datos del grupo y de sus miembros (alta en solicitud o revalidacion al aprobar).</summary>
    public static async Task<Calculo> ValidarAsync(SqlConnection c, SqlTransaction? tx, long idCompania, GrupoDto? g, long? idGestionActual)
    {
        var miembros = new List<Miembro>();
        if (g is null) return new(["Faltan los datos del grupo."], miembros, 0);
        var errores = await ValidarDatosAsync(c, tx, idCompania, g.Nombre, g.IdTipoGrupo, g.Descripcion, g.FechaFormacion, idGestionActual, null);

        var idTipoRel = await c.ExecuteScalarAsync<long?>(
            """
            SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = N'TIPO_RELACION_INTERLOCUTOR' AND i.codigo = @c AND i.estado = 'Activo'
            """, new { c = CodigoRelacionMiembro }, tx);
        if (idTipoRel is null) errores.Add("Falta el tipo de relación MIEMBRO_GRUPO; correr relaciones_ronda23.sql.");

        var lista = g.Miembros ?? [];
        var distintos = lista.Select(m => m.IdMiembro).Distinct().Count();
        if (distintos != lista.Count) errores.Add("Un mismo interlocutor no puede ser miembro dos veces.");
        if (distintos < MinimoMiembros) errores.Add($"Un grupo necesita al menos {MinimoMiembros} miembros distintos.");

        var n = 0;
        foreach (var m in lista)
        {
            n++;
            var pre = $"Miembro #{n}: ";
            var x = await c.QuerySingleOrDefaultAsync(
                """
                SELECT i.id_interlocutor, i.numero, i.tipo_persona, i.estado,
                       COALESCE(j.nombre_org1, LTRIM(RTRIM(CONCAT(f.nombre1, N' ', f.nombre2, N' ', f.apellido1, N' ', f.apellido2)))) AS nombre
                FROM cli.INTERLOCUTOR i
                LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
                LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
                WHERE i.id_interlocutor = @id AND i.id_compania = @idCompania
                """, new { id = m.IdMiembro, idCompania }, tx);
            if (x is null) { errores.Add(pre + "el interlocutor no existe."); continue; }
            var nom = System.Text.RegularExpressions.Regex.Replace((string?)x.nombre ?? "", @"\s+", " ").Trim();
            if ((string)x.tipo_persona is not ("Fisica" or "Juridica")) errores.Add(pre + $"N.º {(string)x.numero} no puede ser miembro (solo personas y organizaciones).");
            else if ((string)x.estado != "Activo") errores.Add(pre + $"N.º {(string)x.numero} está '{(string)x.estado}'.");
            else miembros.Add(new Miembro((long)x.id_interlocutor, (string)x.numero, nom, (string)x.tipo_persona));
            if (m.Funcion is { Length: > 200 }) errores.Add(pre + "la función no puede superar 200 caracteres.");
        }

        return new(errores, miembros, idTipoRel ?? 0);
    }

    /// <summary>Crea el interlocutor Grupo (sin miembros: esos entran como relaciones). Devuelve su id.</summary>
    public static async Task<long> InsertarAsync(
        SqlConnection c, SqlTransaction tx, long idCompania, string numero, long idPaisRegulador, long? idUnidad, long? idCanal, GrupoDto g, string por)
    {
        var id = await c.QuerySingleAsync<long>(
            """
            INSERT INTO cli.INTERLOCUTOR (id_compania, numero, tipo_persona, id_pais_regulador, id_unidad, id_canal_origen, creado_por)
            OUTPUT INSERTED.id_interlocutor
            VALUES (@idCompania, @numero, N'Grupo', @pais, @idUnidad, @idCanal, @por)
            """, new { idCompania, numero, pais = idPaisRegulador, idUnidad, idCanal, por }, tx);
        var nombre = g.Nombre.Trim();
        var descripcion = string.IsNullOrWhiteSpace(g.Descripcion) ? null : g.Descripcion.Trim();
        var formacion = g.FechaFormacion?.ToDateTime(TimeOnly.MinValue);
        await c.ExecuteAsync(
            """
            INSERT INTO cli.INTERLOCUTOR_GRUPO (id_interlocutor, nombre_grupo1, id_tipo_grupo, descripcion, fecha_formacion)
            VALUES (@id, @nombre, @tipo, @descripcion, @formacion);
            INSERT INTO cli.INTERLOCUTOR_JURIDICA (id_interlocutor, nombre_org1, fecha_constitucion)
            VALUES (@id, @nombre, @formacion);
            """, new { id, nombre, tipo = g.IdTipoGrupo, descripcion, formacion }, tx);
        return id;
    }

    public static async Task<GrupoResponse?> ObtenerAsync(SqlConnection c, long idInterlocutor)
    {
        var x = await c.QuerySingleOrDefaultAsync(
            """
            SELECT g.nombre_grupo1, g.id_tipo_grupo, t.nombre AS tipo, g.descripcion, g.fecha_formacion,
                   (SELECT COUNT(*) FROM cli.INTERLOCUTOR_RELACION r
                     JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_relacion AND it.codigo = N'MIEMBRO_GRUPO'
                     WHERE r.id_interlocutor_destino = g.id_interlocutor AND r.fecha_fin IS NULL) AS miembros
            FROM cli.INTERLOCUTOR_GRUPO g LEFT JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = g.id_tipo_grupo
            WHERE g.id_interlocutor = @idInterlocutor
            """, new { idInterlocutor });
        if (x is null) return null;
        var ff = (DateTime?)x.fecha_formacion;
        return new GrupoResponse((string)x.nombre_grupo1, (long?)x.id_tipo_grupo, (string?)x.tipo, (string?)x.descripcion,
            ff is null ? null : DateOnly.FromDateTime(ff.Value), (int)x.miembros);
    }
}
