using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Division territorial y estructura de direccion por pais.
/// Catalogos (cualquier usuario autenticado): configuracion del pais y divisiones activas por nivel/padre.
/// Administracion (DIV_ADMINISTRACION): editar la estructura del pais, mantener divisiones e importar por CSV.
/// </summary>
public class DivisionRepositorio
{
    public const string ObjetoAdministracion = "DIV_ADMINISTRACION";

    private readonly TenantContext _tenant;
    public DivisionRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    // ----- Catalogos -------------------------------------------------------------------

    public async Task<DireccionConfigResponse?> ObtenerConfigAsync(long idPais)
    {
        using var c = NuevaConexion();
        var existe = await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i
                JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE t.codigo = @lista AND i.id_lista_valores_item = @idPais) THEN 1 ELSE 0 END
            """, new { lista = DireccionGeografia.ListaPais, idPais });
        if (!existe) return null;
        var cfgs = await DireccionGeografia.CargarConfigsAsync(c, null, [idPais]);
        return cfgs[idPais].ARespuesta();
    }

    public async Task<IReadOnlyList<DivisionItemResponse>> ListarActivasAsync(long idPais, int nivel, long? idPadre)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync<DivisionItemResponse>(
            """
            SELECT id_division AS Id, codigo AS Codigo, nombre AS Nombre
            FROM gen.DIVISION_TERRITORIAL
            WHERE id_pais = @idPais AND nivel = @nivel AND estado = 'Activo'
              AND ((@idPadre IS NULL AND id_division_padre IS NULL) OR id_division_padre = @idPadre)
            ORDER BY nombre
            """, new { idPais, nivel, idPadre });
        return filas.ToList();
    }

    // ----- Administracion: estructura del pais -------------------------------------------

    public async Task<ResultadoOperacion> GuardarConfigAsync(ActorContext actor, long idPais, DireccionConfigGuardarRequest req)
    {
        var por = actor.NombreDescriptivo;
        var niveles = (req.Niveles ?? []).OrderBy(n => n.Nivel).ToList();
        var errores = new List<string>();
        if (niveles.Count is < 1 or > ConfigDireccion.MaxNiveles) errores.Add("La direccion debe tener entre 1 y 4 niveles.");
        for (var i = 0; i < niveles.Count; i++)
        {
            var n = niveles[i];
            if (n.Nivel != i + 1) { errores.Add("Los niveles deben ser consecutivos desde 1."); break; }
            if (string.IsNullOrWhiteSpace(n.Nombre) || n.Nombre.Trim().Length > 50) errores.Add($"Nivel {n.Nivel}: el nombre es obligatorio (maximo 50).");
            if (n.Modo is not ("Lista" or "Texto")) errores.Add($"Nivel {n.Nivel}: el modo debe ser Lista o Texto.");
            if (i > 0 && n.Modo == "Lista" && niveles[i - 1].Modo != "Lista")
                errores.Add($"Nivel {n.Nivel}: un nivel de lista debe seguir a otro nivel de lista.");
        }
        var cpEtiqueta = string.IsNullOrWhiteSpace(req.EtiquetaCodigoPostal) ? null : req.EtiquetaCodigoPostal.Trim();
        var cpPatron = string.IsNullOrWhiteSpace(req.PatronCodigoPostal) ? null : req.PatronCodigoPostal.Trim();
        if (cpEtiqueta is null && (cpPatron is not null || req.CodigoPostalObligatorio))
            errores.Add("Indique la etiqueta del codigo postal para usar formato u obligatoriedad.");
        if (cpPatron is not null)
        {
            try { _ = Regex.IsMatch("", cpPatron, RegexOptions.None, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException) { errores.Add("El formato (expresion regular) del codigo postal no es valido."); }
        }
        if (errores.Count > 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var paisOk = await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i
                JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE t.codigo = @lista AND i.id_lista_valores_item = @idPais) THEN 1 ELSE 0 END
            """, new { lista = DireccionGeografia.ListaPais, idPais }, tx);
        if (!paisOk) return ResultadoOperacion.Fallo(404, "pais_no_encontrado", "El pais no existe en la lista PAIS.");

        var hay = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_DIRECCION_CONFIG WHERE id_pais = @idPais) THEN 1 ELSE 0 END", new { idPais }, tx);
        if (hay)
            await c.ExecuteAsync(
                """
                UPDATE gen.PAIS_DIRECCION_CONFIG
                SET etiqueta_codigo_postal = @cpEtiqueta, patron_codigo_postal = @cpPatron, codigo_postal_obligatorio = @obl,
                    modificado_por = @por, modificado_en = SYSUTCDATETIME()
                WHERE id_pais = @idPais
                """, new { idPais, cpEtiqueta, cpPatron, obl = req.CodigoPostalObligatorio && cpEtiqueta is not null, por }, tx);
        else
            await c.ExecuteAsync(
                """
                INSERT INTO gen.PAIS_DIRECCION_CONFIG (id_pais, etiqueta_codigo_postal, patron_codigo_postal, codigo_postal_obligatorio, creado_por)
                VALUES (@idPais, @cpEtiqueta, @cpPatron, @obl, @por)
                """, new { idPais, cpEtiqueta, cpPatron, obl = req.CodigoPostalObligatorio && cpEtiqueta is not null, por }, tx);

        await c.ExecuteAsync("DELETE FROM gen.PAIS_DIRECCION_NIVEL WHERE id_pais = @idPais", new { idPais }, tx);
        foreach (var n in niveles)
            await c.ExecuteAsync(
                """
                INSERT INTO gen.PAIS_DIRECCION_NIVEL (id_pais, nivel, nombre, modo, es_obligatorio, creado_por)
                VALUES (@idPais, @nivel, @nombre, @modo, @obl, @por)
                """, new { idPais, nivel = n.Nivel, nombre = n.Nombre.Trim(), modo = n.Modo, obl = n.EsObligatorio, por }, tx);

        tx.Commit();
        return ResultadoOperacion.Ok(idPais);
    }

    // ----- Administracion: divisiones ----------------------------------------------------

    public async Task<IReadOnlyList<DivisionResponse>> ListarAsync(long idPais, int? nivel, long? idPadre, string? q, int max = 500)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync<DivisionResponse>(
            """
            SELECT TOP (@max) d.id_division AS IdDivision, d.id_pais AS IdPais, CAST(d.nivel AS int) AS Nivel, d.id_division_padre AS IdDivisionPadre,
                   p.nombre AS NombrePadre, d.codigo AS Codigo, d.nombre AS Nombre, d.estado AS Estado,
                   (SELECT COUNT(*) FROM gen.DIVISION_TERRITORIAL h WHERE h.id_division_padre = d.id_division) AS CantidadHijos
            FROM gen.DIVISION_TERRITORIAL d
            LEFT JOIN gen.DIVISION_TERRITORIAL p ON p.id_division = d.id_division_padre
            WHERE d.id_pais = @idPais
              AND (@nivel IS NULL OR d.nivel = @nivel)
              AND (@idPadre IS NULL OR d.id_division_padre = @idPadre)
              AND (@q IS NULL OR d.nombre LIKE '%' + @q + '%' OR d.codigo LIKE @q + '%')
            ORDER BY d.nivel, p.nombre, d.nombre
            """, new { idPais, nivel, idPadre, q = string.IsNullOrWhiteSpace(q) ? null : q.Trim(), max });
        return filas.ToList();
    }

    private static async Task<string?> ValidarNivelListaAsync(SqlConnection c, SqlTransaction tx, long idPais, int nivel)
    {
        var modo = await c.ExecuteScalarAsync<string?>(
            "SELECT modo FROM gen.PAIS_DIRECCION_NIVEL WHERE id_pais = @idPais AND nivel = @nivel", new { idPais, nivel }, tx);
        return modo == "Lista" ? null : "El nivel indicado no esta configurado como lista para ese pais.";
    }

    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, DivisionCrearRequest req)
    {
        var codigo = req.Codigo?.Trim() ?? "";
        var nombre = req.Nombre?.Trim() ?? "";
        if (codigo.Length is 0 or > 20) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El codigo es obligatorio (maximo 20).");
        if (nombre.Length is 0 or > 150) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El nombre es obligatorio (maximo 150).");

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var err = await ValidarNivelListaAsync(c, tx, req.IdPais, req.Nivel);
        if (err is not null) return ResultadoOperacion.Fallo(400, "nivel_invalido", err);

        if (req.Nivel == 1 && req.IdDivisionPadre is not null)
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "El nivel 1 no tiene division padre.");
        if (req.Nivel > 1)
        {
            var padreOk = req.IdDivisionPadre is not null && await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.DIVISION_TERRITORIAL WHERE id_division = @p AND id_pais = @pais AND nivel = @nivelPadre) THEN 1 ELSE 0 END",
                new { p = req.IdDivisionPadre, pais = req.IdPais, nivelPadre = req.Nivel - 1 }, tx);
            if (!padreOk) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La division padre no existe en el nivel anterior de ese pais.");
        }
        if (await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.DIVISION_TERRITORIAL WHERE id_pais = @p AND codigo = @codigo) THEN 1 ELSE 0 END",
                new { p = req.IdPais, codigo }, tx))
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", $"Ya existe una division con el codigo {codigo} en ese pais.");

        var id = await c.QuerySingleAsync<long>(
            """
            INSERT INTO gen.DIVISION_TERRITORIAL (id_pais, nivel, id_division_padre, codigo, nombre, creado_por)
            OUTPUT INSERTED.id_division
            VALUES (@idPais, @nivel, @padre, @codigo, @nombre, @por)
            """, new { idPais = req.IdPais, nivel = req.Nivel, padre = req.IdDivisionPadre, codigo, nombre, por = actor.NombreDescriptivo }, tx);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long idDivision, DivisionEditarRequest req)
    {
        var codigo = req.Codigo?.Trim() ?? "";
        var nombre = req.Nombre?.Trim() ?? "";
        if (codigo.Length is 0 or > 20) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El codigo es obligatorio (maximo 20).");
        if (nombre.Length is 0 or > 150) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El nombre es obligatorio (maximo 150).");
        if (req.Estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");

        using var c = NuevaConexion();
        var pais = await c.ExecuteScalarAsync<long?>("SELECT id_pais FROM gen.DIVISION_TERRITORIAL WHERE id_division = @idDivision", new { idDivision });
        if (pais is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "La division no existe.");
        if (await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.DIVISION_TERRITORIAL WHERE id_pais = @pais AND codigo = @codigo AND id_division <> @idDivision) THEN 1 ELSE 0 END",
                new { pais, codigo, idDivision }))
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", $"Ya existe otra division con el codigo {codigo} en ese pais.");

        await c.ExecuteAsync(
            """
            UPDATE gen.DIVISION_TERRITORIAL SET codigo = @codigo, nombre = @nombre, estado = @estado,
                   modificado_por = @por, modificado_en = SYSUTCDATETIME()
            WHERE id_division = @idDivision
            """, new { idDivision, codigo, nombre, estado = req.Estado, por = actor.NombreDescriptivo });
        return ResultadoOperacion.Ok(idDivision);
    }

    /// <summary>
    /// Importacion (CSV ya convertido en filas): por (pais, codigo) crea o actualiza el nombre. Los padres se
    /// resuelven por codigo; se procesa por nivel para que un padre nuevo exista antes que sus hijos.
    /// Las filas con error se informan y no detienen al resto.
    /// </summary>
    public async Task<(ResultadoOperacion Resultado, DivisionImportResponse? Detalle)> ImportarAsync(ActorContext actor, DivisionImportRequest req)
    {
        var filas = req.Filas ?? [];
        if (filas.Count == 0) return (ResultadoOperacion.Fallo(400, "datos_invalidos", "No hay filas para importar."), null);
        if (filas.Count > 20000) return (ResultadoOperacion.Fallo(400, "datos_invalidos", "Maximo 20000 filas por importacion."), null);

        var por = actor.NombreDescriptivo;
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();

        var modos = (await c.QueryAsync("SELECT nivel, modo FROM gen.PAIS_DIRECCION_NIVEL WHERE id_pais = @p", new { p = req.IdPais }, tx))
            .ToDictionary(x => (int)(byte)x.nivel, x => (string)x.modo);
        if (modos.Count == 0)
            return (ResultadoOperacion.Fallo(400, "pais_sin_config", "El pais no tiene estructura de direccion configurada."), null);

        var existentes = (await c.QueryAsync(
            "SELECT id_division, codigo, nivel, nombre FROM gen.DIVISION_TERRITORIAL WHERE id_pais = @p", new { p = req.IdPais }, tx))
            .ToDictionary(x => (string)x.codigo, x => (Id: (long)x.id_division, Nivel: (int)(byte)x.nivel, Nombre: (string)x.nombre), StringComparer.OrdinalIgnoreCase);

        int creadas = 0, actualizadas = 0;
        var errores = new List<string>();
        var n = 0;
        foreach (var f in filas.Select((fila, i) => (Fila: fila, Linea: i + 1)).OrderBy(x => x.Fila.Nivel).ThenBy(x => x.Linea))
        {
            n++;
            var r = f.Fila;
            var et = $"Fila {f.Linea}";
            var codigo = r.Codigo?.Trim() ?? "";
            var nombre = r.Nombre?.Trim() ?? "";
            if (codigo.Length is 0 or > 20 || nombre.Length is 0 or > 150) { errores.Add($"{et}: codigo y nombre son obligatorios (codigo max 20, nombre max 150)."); continue; }
            if (!modos.TryGetValue(r.Nivel, out var modo) || modo != "Lista") { errores.Add($"{et}: el nivel {r.Nivel} no es una lista configurada para el pais."); continue; }

            long? padre = null;
            if (r.Nivel > 1)
            {
                var cp = r.CodigoPadre?.Trim();
                if (string.IsNullOrEmpty(cp) || !existentes.TryGetValue(cp, out var p) || p.Nivel != r.Nivel - 1)
                { errores.Add($"{et}: el codigo padre '{cp}' no existe en el nivel {r.Nivel - 1}."); continue; }
                padre = p.Id;
            }

            if (existentes.TryGetValue(codigo, out var ex))
            {
                if (ex.Nivel != r.Nivel) { errores.Add($"{et}: el codigo {codigo} ya existe en otro nivel."); continue; }
                if (ex.Nombre != nombre)
                {
                    await c.ExecuteAsync(
                        "UPDATE gen.DIVISION_TERRITORIAL SET nombre = @nombre, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_division = @id",
                        new { nombre, por, id = ex.Id }, tx);
                    existentes[codigo] = ex with { Nombre = nombre };
                    actualizadas++;
                }
            }
            else
            {
                var id = await c.QuerySingleAsync<long>(
                    """
                    INSERT INTO gen.DIVISION_TERRITORIAL (id_pais, nivel, id_division_padre, codigo, nombre, creado_por)
                    OUTPUT INSERTED.id_division VALUES (@pais, @nivel, @padre, @codigo, @nombre, @por)
                    """, new { pais = req.IdPais, nivel = r.Nivel, padre, codigo, nombre, por }, tx);
                existentes[codigo] = (id, r.Nivel, nombre);
                creadas++;
            }
        }
        tx.Commit();
        return (ResultadoOperacion.Ok(), new DivisionImportResponse(creadas, actualizadas, errores));
    }
}
