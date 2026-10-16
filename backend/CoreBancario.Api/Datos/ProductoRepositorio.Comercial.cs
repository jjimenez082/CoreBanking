using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Productos comerciales, variantes y versiones con vigencia (ronda 42).</summary>
public partial class ProductoRepositorio
{
    // =============================================================== Producto comercial
    public async Task<IReadOnlyList<ProductoComercialResponse>> ListarComercialesAsync(long idCompania)
    {
        using var c = NuevaConexion();
        c.Open();
        await PromoverProgramadasAsync(c, null, idCompania, null);
        var f = await c.QueryAsync(
            """
            SELECT k.*, o.nombre AS operativo_nombre, o.estrategia_versionado, b.nombre AS base_nombre,
                   (SELECT TOP 1 numero_version FROM prd.PRODUCTO_COMERCIAL_VERSION v WHERE v.id_producto_comercial = k.id_producto_comercial AND v.estado = 'Vigente') AS vigente,
                   (SELECT TOP 1 numero_version FROM prd.PRODUCTO_COMERCIAL_VERSION v WHERE v.id_producto_comercial = k.id_producto_comercial AND v.estado IN ('Borrador', 'Revision')) AS edicion,
                   (SELECT TOP 1 numero_version FROM prd.PRODUCTO_COMERCIAL_VERSION v WHERE v.id_producto_comercial = k.id_producto_comercial AND v.estado IN ('Programado', 'Aprobado')) AS programada
            FROM prd.PRODUCTO_COMERCIAL k
            JOIN prd.PRODUCTO_OPERATIVO o ON o.id_producto_operativo = k.id_producto_operativo
            LEFT JOIN prd.PRODUCTO_COMERCIAL b ON b.id_producto_comercial = k.id_producto_comercial_base
            WHERE k.id_compania = @idCompania ORDER BY k.codigo
            """, new { idCompania });
        return f.Select(k => new ProductoComercialResponse((long)k.id_producto_comercial, (long)k.id_producto_operativo, (string)k.operativo_nombre, (string)k.codigo, (string)k.nombre, (string?)k.descripcion,
            (long?)k.id_producto_comercial_base, (string?)k.base_nombre, (string)k.estado, (string)k.estrategia_versionado, (int?)k.vigente, (int?)k.edicion, (int?)k.programada)).Cast<ProductoComercialResponse>().ToList();
    }

    private static async Task<string?> ValidarComercialAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long? id, ProductoComercialGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Trim().Length > 30) return "El codigo es obligatorio (maximo 30 caracteres).";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        var op = await c.QuerySingleOrDefaultAsync("SELECT estado FROM prd.PRODUCTO_OPERATIVO WHERE id_producto_operativo = @i AND id_compania = @c", new { i = r.IdProductoOperativo, c = actor.IdCompania }, tx);
        if (op is null || (string)op.estado != "Activo") return "El producto operativo no existe o no esta activo.";
        if (r.IdProductoBase is not null)
        {
            if (r.IdProductoBase == id) return "Un producto no puede ser variante de si mismo.";
            var b = await c.QuerySingleOrDefaultAsync("SELECT id_producto_operativo, id_producto_comercial_base FROM prd.PRODUCTO_COMERCIAL WHERE id_producto_comercial = @i AND id_compania = @c", new { i = r.IdProductoBase, c = actor.IdCompania }, tx);
            if (b is null) return "El producto base no existe.";
            if ((long)b.id_producto_operativo != r.IdProductoOperativo) return "El producto base debe ser del mismo producto operativo.";
            if ((long?)b.id_producto_comercial_base is not null) return "El producto base ya es una variante: solo se permite un nivel de herencia.";
            if (id is not null && await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM prd.PRODUCTO_COMERCIAL WHERE id_producto_comercial_base = @i) THEN 1 ELSE 0 END", new { i = id }, tx))
                return "Este producto ya tiene variantes: no puede ser variante de otro.";
        }
        return null;
    }

    public async Task<ResultadoOperacion> GuardarComercialAsync(ActorContext actor, long? id, ProductoComercialGuardarRequest r)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var err = await ValidarComercialAsync(c, tx, actor, id, r);
            if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
            var cod = r.Codigo.Trim().ToUpperInvariant();
            if (id is null)
            {
                var nuevo = await c.QuerySingleAsync<long>(
                    "INSERT INTO prd.PRODUCTO_COMERCIAL (id_compania, id_producto_operativo, codigo, nombre, descripcion, id_producto_comercial_base, creado_por) OUTPUT INSERTED.id_producto_comercial VALUES (@c, @op, @cod, @n, @d, @b, @por)",
                    new { c = actor.IdCompania, op = r.IdProductoOperativo, cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), b = r.IdProductoBase, por = actor.NombreDescriptivo }, tx);
                await Auditoria.AltaAsync(c, tx, actor, EntidadComercial, nuevo, $"{cod} · {r.Nombre.Trim()}{(r.IdProductoBase is null ? "" : " (variante)")}");
                tx.Commit();
                return ResultadoOperacion.Ok(nuevo);
            }
            var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM prd.PRODUCTO_COMERCIAL WITH (UPDLOCK) WHERE id_producto_comercial = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el producto comercial.");
            if (((long)a.id_producto_operativo != r.IdProductoOperativo || (long?)a.id_producto_comercial_base != r.IdProductoBase) &&
                await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @id) THEN 1 ELSE 0 END", new { id }, tx))
                return ResultadoOperacion.Fallo(409, "en_uso", "Ya tiene versiones: no se puede cambiar el producto operativo ni el producto base.");
            await c.ExecuteAsync(
                "UPDATE prd.PRODUCTO_COMERCIAL SET id_producto_operativo = @op, codigo = @cod, nombre = @n, descripcion = @d, id_producto_comercial_base = @b, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_producto_comercial = @id",
                new { op = r.IdProductoOperativo, cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), b = r.IdProductoBase, por = actor.NombreDescriptivo, id }, tx);
            async Task Cambio(string campo, string? ant, string? nue) { if (ant != nue) await Auditoria.CambioAsync(c, tx, actor, EntidadComercial, id.Value, campo, ant, nue); }
            await Cambio("codigo", (string)a.codigo, cod); await Cambio("nombre", (string)a.nombre, r.Nombre.Trim()); await Cambio("descripcion", (string?)a.descripcion, r.Descripcion?.Trim());
            await Cambio("id_producto_comercial_base", ((long?)a.id_producto_comercial_base)?.ToString(), r.IdProductoBase?.ToString());
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un producto comercial con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoComercialAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT estado FROM prd.PRODUCTO_COMERCIAL WITH (UPDLOCK) WHERE id_producto_comercial = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el producto comercial.");
        if (estado == "Inactivo")
        {
            var variantes = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM prd.PRODUCTO_COMERCIAL WHERE id_producto_comercial_base = @id AND estado = 'Activo'", new { id }, tx);
            if (variantes > 0) return ResultadoOperacion.Fallo(409, "en_uso", $"Tiene {variantes} variante(s) activa(s); inactivelas primero.");
        }
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_producto_comercial = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadComercial, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    // =============================================================== Versiones
    /// <summary>Una version Programada cuya fecha llego pasa a Vigente y la anterior a Expirado (D-7: hasta que exista el EOD se resuelve al consultar).</summary>
    internal static async Task PromoverProgramadasAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long? idComercial)
    {
        var hoy = DateTime.Now.Date;
        var due = (await c.QueryAsync(
            """
            SELECT v.id_version, v.id_producto_comercial, v.fecha_vigencia_desde FROM prd.PRODUCTO_COMERCIAL_VERSION v
            JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial
            WHERE k.id_compania = @idCompania AND v.estado = 'Programado' AND v.fecha_vigencia_desde <= @hoy AND (@idComercial IS NULL OR v.id_producto_comercial = @idComercial)
            ORDER BY v.fecha_vigencia_desde
            """, new { idCompania, hoy, idComercial }, tx)).ToList();
        foreach (var d in due)
            await ActivarVersionAsync(c, tx, (long)d.id_producto_comercial, (long)d.id_version, DateOnly.FromDateTime((DateTime)d.fecha_vigencia_desde));
    }

    private static async Task ActivarVersionAsync(SqlConnection c, SqlTransaction? tx, long idComercial, long idVersion, DateOnly desde)
    {
        var vigente = await c.QuerySingleOrDefaultAsync("SELECT id_version, fecha_vigencia_desde FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @i AND estado = 'Vigente'", new { i = idComercial }, tx);
        if (vigente is not null)
        {
            var cierre = desde.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var ini = (DateTime?)vigente.fecha_vigencia_desde;
            if (ini is not null && cierre < ini) cierre = ini.Value;
            await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION SET estado = 'Expirado', fecha_vigencia_hasta = @h, modificado_en = SYSUTCDATETIME() WHERE id_version = @id", new { h = cierre, id = (long)vigente.id_version }, tx);
        }
        await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION SET estado = 'Vigente', fecha_vigencia_desde = @d, modificado_en = SYSUTCDATETIME() WHERE id_version = @id", new { d = desde.ToDateTime(TimeOnly.MinValue), id = idVersion }, tx);
    }

    public async Task<IReadOnlyList<VersionResponse>> ListarVersionesAsync(ActorContext actor, long idComercial)
    {
        using var c = NuevaConexion(); c.Open();
        await PromoverProgramadasAsync(c, null, actor.IdCompania, idComercial);
        var f = await c.QueryAsync(
            """
            SELECT v.*, g.numero AS numero_gestion, g.estado AS estado_gestion FROM prd.PRODUCTO_COMERCIAL_VERSION v
            JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial
            LEFT JOIN apr.GESTION g ON g.id_gestion = v.id_gestion
            WHERE v.id_producto_comercial = @idComercial AND k.id_compania = @c ORDER BY v.numero_version DESC
            """, new { idComercial, c = actor.IdCompania });
        return f.Select(v => MapVersion(v)).Cast<VersionResponse>().ToList();
    }

    private static VersionResponse MapVersion(dynamic v) => new((long)v.id_version, (int)v.numero_version, (string)v.estado,
        v.fecha_vigencia_desde is null ? null : DateOnly.FromDateTime((DateTime)v.fecha_vigencia_desde), v.fecha_vigencia_hasta is null ? null : DateOnly.FromDateTime((DateTime)v.fecha_vigencia_hasta),
        (string?)v.comentario, (long?)v.id_gestion, (string?)v.numero_gestion, (string?)v.estado_gestion, (string)v.creado_por, (DateTime)v.creado_en);

    public async Task<VersionDetalleResponse?> ObtenerVersionAsync(ActorContext actor, long idVersion)
    {
        using var c = NuevaConexion(); c.Open();
        var v = await c.QuerySingleOrDefaultAsync(
            """
            SELECT v.*, k.id_producto_operativo, k.id_producto_comercial_base, g.numero AS numero_gestion, g.estado AS estado_gestion FROM prd.PRODUCTO_COMERCIAL_VERSION v
            JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial LEFT JOIN apr.GESTION g ON g.id_gestion = v.id_gestion
            WHERE v.id_version = @idVersion AND k.id_compania = @c
            """, new { idVersion, c = actor.IdCompania });
        if (v is null) return null;
        var valores = (await c.QueryAsync("SELECT id_parametro_producto, valor FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @idVersion", new { idVersion })).ToDictionary(x => (long)x.id_parametro_producto, x => (string)x.valor);
        // Valores heredados: los de la version vigente (o la ultima publicada) del producto base.
        var heredados = new Dictionary<long, string>();
        if ((long?)v.id_producto_comercial_base is long baseId)
        {
            var vb = await c.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id_version FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @b AND estado IN ('Vigente', 'Programado', 'Expirado') ORDER BY CASE estado WHEN 'Vigente' THEN 0 WHEN 'Programado' THEN 1 ELSE 2 END, numero_version DESC", new { b = baseId });
            if (vb is not null)
                heredados = (await c.QueryAsync("SELECT id_parametro_producto, valor FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @vb", new { vb })).ToDictionary(x => (long)x.id_parametro_producto, x => (string)x.valor);
        }
        var pars = await c.QueryAsync(
            """
            SELECT p.*, b.nombre AS base_nombre, lt.codigo AS lista_codigo FROM prd.PARAMETRO_PRODUCTO_OPERATIVO p LEFT JOIN gen.LISTA_VALORES_ITEM b ON b.id_lista_valores_item = p.id_base_componente LEFT JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = p.id_lista_valores_tipo
            WHERE p.id_producto_operativo = @op AND (p.estado = 'Activo' OR p.id_parametro_producto IN (SELECT id_parametro_producto FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @idVersion))
            ORDER BY p.orden, p.codigo
            """, new { op = (long)v.id_producto_operativo, idVersion });
        var lista = pars.Select(p => new VersionValorResponse((long)p.id_parametro_producto, (string)p.codigo, (string)p.nombre, (string)p.grupo, (string)p.tipo_dato,
            valores.GetValueOrDefault((long)p.id_parametro_producto), heredados.GetValueOrDefault((long)p.id_parametro_producto), (string?)p.valor_defecto, (bool)p.obligatorio,
            (decimal?)p.valor_minimo, (decimal?)p.valor_maximo, (long?)p.id_lista_valores_tipo, (string?)p.lista_codigo, (bool)p.es_componente_economico, (string?)p.tipo_calculo_economico, (string?)p.base_nombre)).Cast<VersionValorResponse>().ToList();
        return new VersionDetalleResponse(MapVersion(v), lista);
    }

    /// <summary>Crea el siguiente borrador, copiando los valores de la ultima version (si hay).</summary>
    public async Task<ResultadoOperacion> CrearBorradorAsync(ActorContext actor, long idComercial)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var k = await c.QuerySingleOrDefaultAsync("SELECT estado FROM prd.PRODUCTO_COMERCIAL WITH (UPDLOCK) WHERE id_producto_comercial = @i AND id_compania = @c", new { i = idComercial, c = actor.IdCompania }, tx);
            if (k is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el producto comercial.");
            if ((string)k.estado != "Activo") return ResultadoOperacion.Fallo(409, "producto_inactivo", "El producto comercial esta inactivo.");
            var enEdicion = await c.ExecuteScalarAsync<int?>("SELECT TOP 1 numero_version FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @i AND estado IN ('Borrador', 'Revision')", new { i = idComercial }, tx);
            if (enEdicion is not null) return ResultadoOperacion.Fallo(409, "version_en_edicion", $"Ya hay una version en edicion (v{enEdicion}).");
            var ultima = await c.QuerySingleOrDefaultAsync("SELECT TOP 1 id_version, numero_version FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @i ORDER BY numero_version DESC", new { i = idComercial }, tx);
            var numero = ultima is null ? 1 : (int)ultima.numero_version + 1;
            var id = await c.QuerySingleAsync<long>(
                "INSERT INTO prd.PRODUCTO_COMERCIAL_VERSION (id_producto_comercial, numero_version, creado_por) OUTPUT INSERTED.id_version VALUES (@i, @n, @por)",
                new { i = idComercial, n = numero, por = actor.NombreDescriptivo }, tx);
            if (ultima is not null)
                await c.ExecuteAsync(
                    "INSERT INTO prd.PRODUCTO_COMERCIAL_VERSION_VALOR (id_version, id_parametro_producto, valor, creado_por) SELECT @n, id_parametro_producto, valor, @por FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @o",
                    new { n = id, o = (long)ultima.id_version, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadVersion, id, $"v{numero}{(ultima is null ? "" : $" (copia de v{(int)ultima.numero_version})")}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "version_en_edicion", "Ya hay una version en edicion."); }
        catch { tx.Rollback(); throw; }
    }

    private static string? ValidarValor(dynamic p, string valor, HashSet<string>? items, HashSet<string> indicadores, HashSet<string> bases)
    {
        var nombre = (string)p.nombre; var tipo = (string)p.tipo_dato; var codigo = (string)p.codigo;
        var inv = CultureInfo.InvariantCulture;
        switch (tipo)
        {
            case "Entero":
                if (!long.TryParse(valor, NumberStyles.Integer, inv, out var e)) return $"'{nombre}' debe ser un numero entero.";
                if (p.valor_minimo is not null && e < (decimal)p.valor_minimo) return $"'{nombre}' no puede ser menor que {((decimal)p.valor_minimo).ToString("0.######", inv)}.";
                if (p.valor_maximo is not null && e > (decimal)p.valor_maximo) return $"'{nombre}' no puede ser mayor que {((decimal)p.valor_maximo).ToString("0.######", inv)}.";
                break;
            case "Decimal":
                if (!decimal.TryParse(valor, NumberStyles.Number, inv, out var d)) return $"'{nombre}' debe ser un numero.";
                if (decimal.Round(d, 6) != d) return $"'{nombre}': maximo 6 decimales.";
                if (p.valor_minimo is not null && d < (decimal)p.valor_minimo) return $"'{nombre}' no puede ser menor que {((decimal)p.valor_minimo).ToString("0.######", inv)}.";
                if (p.valor_maximo is not null && d > (decimal)p.valor_maximo) return $"'{nombre}' no puede ser mayor que {((decimal)p.valor_maximo).ToString("0.######", inv)}.";
                if ((bool)p.es_componente_economico && d < 0) return $"'{nombre}' no puede ser negativo.";
                if ((string?)p.tipo_calculo_economico == "Porcentaje" && d > 100) return $"'{nombre}' es un porcentaje: maximo 100.";
                break;
            case "Booleano": if (valor is not ("true" or "false")) return $"'{nombre}' debe ser true o false."; break;
            case "Fecha": if (!DateOnly.TryParseExact(valor, "yyyy-MM-dd", inv, DateTimeStyles.None, out _)) return $"'{nombre}' debe ser una fecha (aaaa-mm-dd)."; break;
            case "Lista": if (items is null || !items.Contains(valor)) return $"'{nombre}': el valor '{valor}' no esta en la lista."; break;
            default: if (valor.Length > 500) return $"'{nombre}' es demasiado largo."; break;
        }
        if (codigo == "indicador_referencia" && !indicadores.Contains(valor)) return $"'{nombre}': el indicador '{valor}' no existe o esta inactivo.";
        if (codigo == "base_calculo" && !bases.Contains(valor)) return $"'{nombre}': la base '{valor}' no existe o esta inactiva.";
        return null;
    }

    public async Task<ResultadoOperacion> GuardarValoresAsync(ActorContext actor, long idVersion, VersionValoresGuardarRequest req)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var v = await c.QuerySingleOrDefaultAsync(
                """
                SELECT v.estado, v.numero_version, k.id_producto_operativo FROM prd.PRODUCTO_COMERCIAL_VERSION v WITH (UPDLOCK)
                JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial WHERE v.id_version = @idVersion AND k.id_compania = @c
                """, new { idVersion, c = actor.IdCompania }, tx);
            if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
            if ((string)v.estado != "Borrador") return ResultadoOperacion.Fallo(409, "version_no_editable", "Solo una version en Borrador se puede editar.");
            var pars = (await c.QueryAsync("SELECT * FROM prd.PARAMETRO_PRODUCTO_OPERATIVO WHERE id_producto_operativo = @op AND estado = 'Activo'", new { op = (long)v.id_producto_operativo }, tx)).ToDictionary(p => (long)p.id_parametro_producto);
            var indicadores = (await c.QueryAsync<string>("SELECT codigo FROM gen.INDICADOR_FINANCIERO WHERE estado = 'Activo'", transaction: tx)).ToHashSet();
            var bases = (await c.QueryAsync<string>("SELECT codigo FROM gen.BASE_CALCULO WHERE estado = 'Activo'", transaction: tx)).ToHashSet();
            var listas = new Dictionary<long, HashSet<string>>();
            var errores = new List<string>();
            foreach (var x in req.Valores)
            {
                if (!pars.TryGetValue(x.IdParametro, out var p)) { errores.Add($"El parametro {x.IdParametro} no existe o esta inactivo."); continue; }
                if (string.IsNullOrWhiteSpace(x.Valor)) continue;
                HashSet<string>? items = null;
                if ((string)p.tipo_dato == "Lista")
                {
                    var lt = (long)p.id_lista_valores_tipo;
                    if (!listas.TryGetValue(lt, out items))
                        listas[lt] = items = (await c.QueryAsync<string>("SELECT codigo FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @lt AND estado = 'Activo'", new { lt }, tx)).ToHashSet();
                }
                var err = ValidarValor(p, x.Valor.Trim(), items, indicadores, bases);
                if (err is not null) errores.Add(err);
            }
            if (errores.Count > 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join(" ", errores));

            var previos = (await c.QueryAsync("SELECT id_parametro_producto, valor FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @idVersion", new { idVersion }, tx)).ToDictionary(x => (long)x.id_parametro_producto, x => (string)x.valor);
            foreach (var x in req.Valores)
            {
                if (!pars.ContainsKey(x.IdParametro)) continue;
                var nuevo = string.IsNullOrWhiteSpace(x.Valor) ? null : x.Valor.Trim();
                var ant = previos.GetValueOrDefault(x.IdParametro);
                if (ant == nuevo) continue;
                if (nuevo is null) await c.ExecuteAsync("DELETE FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @idVersion AND id_parametro_producto = @p", new { idVersion, p = x.IdParametro }, tx);
                else if (ant is null) await c.ExecuteAsync("INSERT INTO prd.PRODUCTO_COMERCIAL_VERSION_VALOR (id_version, id_parametro_producto, valor, creado_por) VALUES (@idVersion, @p, @v, @por)", new { idVersion, p = x.IdParametro, v = nuevo, por = actor.NombreDescriptivo }, tx);
                else await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION_VALOR SET valor = @v WHERE id_version = @idVersion AND id_parametro_producto = @p", new { idVersion, p = x.IdParametro, v = nuevo }, tx);
                await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, (string)pars[x.IdParametro].codigo, ant, nuevo);
            }
            if (req.Comentario is not null)
                await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION SET comentario = @c, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion", new { c = string.IsNullOrWhiteSpace(req.Comentario) ? null : req.Comentario.Trim(), por = actor.NombreDescriptivo, idVersion }, tx);
            tx.Commit();
            return ResultadoOperacion.Ok(idVersion);
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EliminarBorradorAsync(ActorContext actor, long idVersion)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var v = await c.QuerySingleOrDefaultAsync(
            "SELECT v.estado, v.numero_version FROM prd.PRODUCTO_COMERCIAL_VERSION v WITH (UPDLOCK) JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial WHERE v.id_version = @idVersion AND k.id_compania = @c",
            new { idVersion, c = actor.IdCompania }, tx);
        if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
        if ((string)v.estado != "Borrador") return ResultadoOperacion.Fallo(409, "version_no_editable", "Solo se descarta una version en Borrador.");
        await c.ExecuteAsync("DELETE FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @idVersion; DELETE FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_version = @idVersion", new { idVersion }, tx);
        await Auditoria.BajaAsync(c, tx, actor, EntidadVersion, idVersion, $"v{(int)v.numero_version} (borrador descartado)");
        tx.Commit();
        return ResultadoOperacion.Ok(idVersion);
    }

    /// <summary>Reglas cruzadas y obligatorios antes de enviar a aprobacion.</summary>
    private static string? ValidarCompletitud(IEnumerable<dynamic> parametros, Dictionary<long, string> propios, Dictionary<long, string> heredados, bool esVariante)
    {
        var errores = new List<string>();
        string? V(string codigo)
        {
            var p = parametros.FirstOrDefault(x => (string)x.codigo == codigo); if (p is null) return null;
            var id = (long)p.id_parametro_producto;
            return propios.GetValueOrDefault(id) ?? heredados.GetValueOrDefault(id) ?? (string?)p.valor_defecto;
        }
        decimal? D(string codigo) => decimal.TryParse(V(codigo), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        foreach (var p in parametros)
        {
            if (!(bool)p.obligatorio) continue;
            if (V((string)p.codigo) is null) errores.Add($"Falta '{(string)p.nombre}'.");
        }
        void Par(string min, string max, string txt) { var a = D(min); var b = D(max); if (a is not null && b is not null && a > b) errores.Add(txt); }
        Par("monto_minimo", "monto_maximo", "El monto minimo no puede ser mayor que el maximo.");
        Par("plazo_minimo_meses", "plazo_maximo_meses", "El plazo minimo no puede ser mayor que el maximo.");
        Par("tasa_piso", "tasa_techo", "La tasa piso no puede ser mayor que la tasa techo.");
        var tipoTasa = V("tipo_tasa");
        if (tipoTasa == "FIJA" && D("tasa_nominal_anual") is null) errores.Add("Una tasa fija necesita la tasa nominal anual.");
        if (tipoTasa == "VARIABLE" && (V("indicador_referencia") is null || D("spread") is null)) errores.Add("Una tasa variable necesita el indicador de referencia y el spread.");
        return errores.Count == 0 ? null : string.Join(" ", errores);
    }

    public async Task<ResultadoOperacion> PublicarAsync(ActorContext actor, long idVersion, VersionPublicarRequest req)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var v = await c.QuerySingleOrDefaultAsync(
                """
                SELECT v.*, k.codigo AS producto, k.id_producto_operativo, k.id_producto_comercial_base, k.estado AS estado_producto FROM prd.PRODUCTO_COMERCIAL_VERSION v WITH (UPDLOCK)
                JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial WHERE v.id_version = @idVersion AND k.id_compania = @c
                """, new { idVersion, c = actor.IdCompania }, tx);
            if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
            if ((string)v.estado != "Borrador") return ResultadoOperacion.Fallo(409, "version_no_editable", "Solo se publica una version en Borrador.");
            if ((string)v.estado_producto != "Activo") return ResultadoOperacion.Fallo(409, "producto_inactivo", "El producto comercial esta inactivo.");
            var hoy = DateOnly.FromDateTime(DateTime.Now);
            if (req.FechaVigenciaDesde < hoy) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La vigencia no puede ser anterior a hoy.");
            var idComercial = (long)v.id_producto_comercial;
            var vig = await c.ExecuteScalarAsync<DateTime?>("SELECT fecha_vigencia_desde FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @i AND estado = 'Vigente'", new { i = idComercial }, tx);
            if (vig is not null && req.FechaVigenciaDesde.ToDateTime(TimeOnly.MinValue) <= vig.Value)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", $"La vigencia debe ser posterior a la de la version vigente ({vig:yyyy-MM-dd}).");
            if (await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @i AND estado IN ('Programado', 'Revision')) THEN 1 ELSE 0 END", new { i = idComercial }, tx))
                return ResultadoOperacion.Fallo(409, "version_programada", "Ya hay una version programada o en revision para este producto.");

            var pars = (await c.QueryAsync("SELECT * FROM prd.PARAMETRO_PRODUCTO_OPERATIVO WHERE id_producto_operativo = @op AND estado = 'Activo'", new { op = (long)v.id_producto_operativo }, tx)).ToList();
            var propios = (await c.QueryAsync("SELECT id_parametro_producto, valor FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @idVersion", new { idVersion }, tx)).ToDictionary(x => (long)x.id_parametro_producto, x => (string)x.valor);
            var heredados = new Dictionary<long, string>();
            if ((long?)v.id_producto_comercial_base is long baseId)
            {
                var vb = await c.ExecuteScalarAsync<long?>("SELECT TOP 1 id_version FROM prd.PRODUCTO_COMERCIAL_VERSION WHERE id_producto_comercial = @b AND estado IN ('Vigente', 'Programado', 'Expirado') ORDER BY CASE estado WHEN 'Vigente' THEN 0 WHEN 'Programado' THEN 1 ELSE 2 END, numero_version DESC", new { b = baseId }, tx);
                if (vb is null) return ResultadoOperacion.Fallo(409, "base_sin_version", "El producto base aun no tiene una version publicada.");
                heredados = (await c.QueryAsync("SELECT id_parametro_producto, valor FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_version = @vb", new { vb }, tx)).ToDictionary(x => (long)x.id_parametro_producto, x => (string)x.valor);
            }
            var err = ValidarCompletitud(pars, propios, heredados, (long?)v.id_producto_comercial_base is not null);
            if (err is not null) return ResultadoOperacion.Fallo(400, "version_incompleta", err);

            await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION SET estado = 'Revision', fecha_vigencia_desde = @d, comentario = COALESCE(@com, comentario), modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion",
                new { d = req.FechaVigenciaDesde.ToDateTime(TimeOnly.MinValue), com = string.IsNullOrWhiteSpace(req.Comentario) ? null : req.Comentario.Trim(), por = actor.NombreDescriptivo, idVersion }, tx);
            var r = await GestionSolicitudes.CrearAsync(c, tx, _tenant, _numeracion, actor, TipoGestionVersion, EntidadVersion, idVersion.ToString(CultureInfo.InvariantCulture), req.Comentario,
                [("id_version", null, idVersion.ToString(CultureInfo.InvariantCulture)), ("producto", null, (string)v.producto), ("numero_version", null, ((int)v.numero_version).ToString(CultureInfo.InvariantCulture)),
                 ("fecha_vigencia_desde", null, req.FechaVigenciaDesde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
            if (!r.Exito) { tx.Rollback(); return r; }
            await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION SET id_gestion = @g WHERE id_version = @idVersion", new { g = r.Id, idVersion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Borrador", "Revision", r.Id);
            tx.Commit();
            return r;
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "version_programada", "Ya hay una version en revision o programada."); }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Devuelve a Borrador una version cuya gestion fue rechazada o cancelada (no pendiente).</summary>
    public async Task<ResultadoOperacion> ReabrirAsync(ActorContext actor, long idVersion)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var v = await c.QuerySingleOrDefaultAsync(
            """
            SELECT v.estado, g.estado AS estado_gestion FROM prd.PRODUCTO_COMERCIAL_VERSION v WITH (UPDLOCK) JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial
            LEFT JOIN apr.GESTION g ON g.id_gestion = v.id_gestion WHERE v.id_version = @idVersion AND k.id_compania = @c
            """, new { idVersion, c = actor.IdCompania }, tx);
        if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
        if ((string)v.estado != "Revision") return ResultadoOperacion.Fallo(409, "version_no_editable", "La version no esta en revision.");
        if ((string?)v.estado_gestion == GestionRepositorio.EstadoPendiente) return ResultadoOperacion.Fallo(409, "gestion_pendiente", "La gestion sigue pendiente de aprobacion.");
        await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION SET estado = 'Borrador', modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion", new { por = actor.NombreDescriptivo, idVersion }, tx);
        await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Revision", "Borrador");
        tx.Commit();
        return ResultadoOperacion.Ok(idVersion);
    }

    /// <summary>Aplicador de gestiones PRD_VERSION_PRODUCTO (al aprobarse).</summary>
    public static async Task<ResultadoOperacion?> AplicarPublicacionAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion)
    {
        var det = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx)).ToDictionary(d => (string)d.nombre_campo, d => (string?)d.valor_nuevo);
        if (!det.TryGetValue("id_version", out var sv) || !det.TryGetValue("fecha_vigencia_desde", out var sf)) return ResultadoOperacion.Fallo(500, "gestion_incompleta", "La gestion no tiene los campos de la version.");
        var idVersion = long.Parse(sv!, CultureInfo.InvariantCulture);
        var desde = DateOnly.ParseExact(sf!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var v = await c.QuerySingleOrDefaultAsync("SELECT estado, id_producto_comercial FROM prd.PRODUCTO_COMERCIAL_VERSION WITH (UPDLOCK) WHERE id_version = @idVersion", new { idVersion }, tx);
        if (v is null || (string)v.estado != "Revision") return ResultadoOperacion.Fallo(409, "version_no_editable", "La version ya no esta en revision; rechace la gestion.");
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        var efectiva = desde < hoy ? hoy : desde;   // si se aprobo tarde, rige desde el dia de la aprobacion
        var idComercial = (long)v.id_producto_comercial;
        if (efectiva <= hoy)
        {
            await ActivarVersionAsync(c, tx, idComercial, idVersion, efectiva);
            await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Revision", "Vigente", idGestion);
        }
        else
        {
            await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL_VERSION SET estado = 'Programado', fecha_vigencia_desde = @d, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion", new { d = efectiva.ToDateTime(TimeOnly.MinValue), idVersion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Revision", "Programado", idGestion);
        }
        return null;
    }
}
