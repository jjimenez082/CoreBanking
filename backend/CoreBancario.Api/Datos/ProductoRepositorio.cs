using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Productos operativos (estructura y parametros) y comerciales (oferta, variantes y versiones con vigencia). Ronda 42.
/// El mantenimiento es directo con auditoria; publicar una version pasa por gestion PRD_VERSION_PRODUCTO.
/// </summary>
public partial class ProductoRepositorio
{
    public const string PermisoAdmin = "PRD_ADMIN", PermisoConsulta = "PRD_CONSULTA", PermisoPublicar = "PRD_PUBLICAR";
    public const string TipoGestionVersion = "PRD_VERSION_PRODUCTO";
    public const string EntidadOperativo = "PRODUCTO_OPERATIVO", EntidadParametro = "PARAMETRO_PRODUCTO", EntidadComercial = "PRODUCTO_COMERCIAL", EntidadVersion = "PRODUCTO_VERSION";

    private static readonly string[] Dominios = ["Credito", "Cuenta"];
    private static readonly string[] Estrategias = ["Dinamico", "FijoAlOrigen"];
    private static readonly string[] Personas = ["Fisica", "Juridica", "Ambas"];
    private static readonly string[] Grupos = ["Monto", "Plazo", "Tasa", "PlanPago", "Comision", "Mora", "Seguro", "Garantia", "Gracia", "General"];
    private static readonly string[] Tipos = ["Texto", "Entero", "Decimal", "Booleano", "Fecha", "Lista"];

    private readonly TenantContext _tenant;
    private readonly NumeracionServicio _numeracion;
    public ProductoRepositorio(TenantContext tenant, NumeracionServicio numeracion) { _tenant = tenant; _numeracion = numeracion; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);
    private static bool Duplicado(SqlException ex) => ex.Number is 2601 or 2627;
    private static string Hoy() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // =============================================================== Producto operativo
    private const string SqlOperativos =
        """
        SELECT p.*, cat.nombre AS categoria_nombre, cr.nombre AS catreg_nombre, cal.nombre AS calendario_nombre,
               (SELECT COUNT(*) FROM prd.PARAMETRO_PRODUCTO_OPERATIVO x WHERE x.id_producto_operativo = p.id_producto_operativo) AS cant_param,
               (SELECT COUNT(*) FROM prd.PRODUCTO_COMERCIAL x WHERE x.id_producto_operativo = p.id_producto_operativo) AS cant_com
        FROM prd.PRODUCTO_OPERATIVO p
        JOIN gen.LISTA_VALORES_ITEM cat ON cat.id_lista_valores_item = p.id_categoria_producto
        LEFT JOIN gen.CATEGORIA_REGULATORIA cr ON cr.id_categoria_regulatoria = p.id_categoria_regulatoria
        LEFT JOIN gen.CALENDARIO cal ON cal.id_calendario = p.id_calendario
        WHERE p.id_compania = @idCompania AND (@id IS NULL OR p.id_producto_operativo = @id)
        ORDER BY p.codigo
        """;

    public async Task<IReadOnlyList<ProductoOperativoResponse>> ListarOperativosAsync(long idCompania, long? id = null)
    {
        using var c = NuevaConexion();
        var filas = (await c.QueryAsync(SqlOperativos, new { idCompania, id })).ToList();
        var monedas = (await c.QueryAsync(
            """
            SELECT pm.id_producto_operativo, m.id_moneda, m.codigo, m.nombre, pm.es_principal FROM prd.PRODUCTO_OPERATIVO_MONEDA pm
            JOIN gen.MONEDA m ON m.id_moneda = pm.id_moneda JOIN prd.PRODUCTO_OPERATIVO p ON p.id_producto_operativo = pm.id_producto_operativo
            WHERE p.id_compania = @idCompania ORDER BY m.codigo
            """, new { idCompania })).ToLookup(m => (long)m.id_producto_operativo);
        return filas.Select(p => new ProductoOperativoResponse(
            (long)p.id_producto_operativo, (string)p.codigo, (string)p.nombre, (string?)p.descripcion, (string)p.dominio, (long)p.id_categoria_producto, (string)p.categoria_nombre,
            (long?)p.id_categoria_regulatoria, (string?)p.catreg_nombre, (string)p.estrategia_versionado, (string)p.tipo_persona_aplicable, (long?)p.id_calendario, (string?)p.calendario_nombre,
            (string)p.estado,
            monedas[(long)p.id_producto_operativo].Select(m => new ProductoMonedaResponse((long)m.id_moneda, (string)m.codigo, (string)m.nombre, (bool)m.es_principal)).ToList(),
            (int)p.cant_param, (int)p.cant_com)).Cast<ProductoOperativoResponse>().ToList();
    }

    private static async Task<string?> ValidarOperativoAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, ProductoOperativoGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Trim().Length > 30) return "El codigo es obligatorio (maximo 30 caracteres).";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        if (!Dominios.Contains(r.Dominio)) return "Dominio invalido.";
        if (!Estrategias.Contains(r.EstrategiaVersionado)) return "Estrategia de versionado invalida.";
        if (!Personas.Contains(r.TipoPersona)) return "Tipo de persona invalido.";
        if (r.Monedas is null || r.Monedas.Count == 0) return "Indique al menos una moneda.";
        if (r.Monedas.Select(m => m.IdMoneda).Distinct().Count() != r.Monedas.Count) return "Moneda repetida.";
        if (r.Monedas.Count(m => m.EsPrincipal) != 1) return "Marque exactamente una moneda principal.";
        var lista = r.Dominio == "Credito" ? "CATEGORIA_PRODUCTO_CREDITO" : "CATEGORIA_PRODUCTO_CUENTA";
        var okCat = await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
              WHERE t.codigo = @l AND i.id_lista_valores_item = @i AND i.estado = 'Activo') THEN 1 ELSE 0 END
            """, new { l = lista, i = r.IdCategoria }, tx);
        if (!okCat) return "La categoria no existe, esta inactiva o no corresponde al dominio.";
        var nMon = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM gen.MONEDA WHERE id_compania = @c AND estado = 'Activo' AND id_moneda IN @ids", new { c = actor.IdCompania, ids = r.Monedas.Select(m => m.IdMoneda).ToArray() }, tx);
        if (nMon != r.Monedas.Count) return "Alguna moneda no existe o esta inactiva.";
        if (r.IdCalendario is not null &&
            !await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.CALENDARIO WHERE id_calendario = @i AND id_compania = @c AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { i = r.IdCalendario, c = actor.IdCompania }, tx)) return "El calendario no existe o esta inactivo.";
        if (r.IdCategoriaRegulatoria is not null &&
            !await c.ExecuteScalarAsync<bool>(
                """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.CATEGORIA_REGULATORIA k JOIN gen.COMPANIA co ON co.id_pais_regulador = k.id_pais_regulador
                  WHERE k.id_categoria_regulatoria = @i AND co.id_compania = @c AND k.estado = 'Activo') THEN 1 ELSE 0 END
                """, new { i = r.IdCategoriaRegulatoria, c = actor.IdCompania }, tx)) return "La categoria regulatoria no existe, esta inactiva o es de otro pais.";
        return null;
    }

    public async Task<ResultadoOperacion> CrearOperativoAsync(ActorContext actor, ProductoOperativoGuardarRequest r)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var err = await ValidarOperativoAsync(c, tx, actor, r);
            if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
            var cod = r.Codigo.Trim().ToUpperInvariant();
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO prd.PRODUCTO_OPERATIVO (id_compania, codigo, nombre, descripcion, dominio, id_categoria_producto, id_categoria_regulatoria, estrategia_versionado, tipo_persona_aplicable, id_calendario, creado_por)
                OUTPUT INSERTED.id_producto_operativo VALUES (@c, @cod, @n, @d, @dom, @cat, @cr, @est, @per, @cal, @por)
                """, new { c = actor.IdCompania, cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), dom = r.Dominio, cat = r.IdCategoria, cr = r.IdCategoriaRegulatoria, est = r.EstrategiaVersionado, per = r.TipoPersona, cal = r.IdCalendario, por = actor.NombreDescriptivo }, tx);
            foreach (var m in r.Monedas)
                await c.ExecuteAsync("INSERT INTO prd.PRODUCTO_OPERATIVO_MONEDA (id_producto_operativo, id_moneda, es_principal, creado_por) VALUES (@id, @m, @p, @por)",
                    new { id, m = m.IdMoneda, p = m.EsPrincipal, por = actor.NombreDescriptivo }, tx);
            if (r.SembrarPlantilla) await SembrarPlantillaAsync(c, tx, actor, id, r.Dominio);
            await Auditoria.AltaAsync(c, tx, actor, EntidadOperativo, id, $"{cod} · {r.Nombre.Trim()} ({r.Dominio})");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un producto operativo con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EditarOperativoAsync(ActorContext actor, long id, ProductoOperativoGuardarRequest r)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var err = await ValidarOperativoAsync(c, tx, actor, r);
            if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
            var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM prd.PRODUCTO_OPERATIVO WITH (UPDLOCK) WHERE id_producto_operativo = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el producto operativo.");
            var conVersiones = await c.ExecuteScalarAsync<bool>(
                """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM prd.PRODUCTO_COMERCIAL_VERSION v JOIN prd.PRODUCTO_COMERCIAL k ON k.id_producto_comercial = v.id_producto_comercial
                  WHERE k.id_producto_operativo = @id AND v.estado IN ('Programado', 'Vigente', 'Expirado')) THEN 1 ELSE 0 END
                """, new { id }, tx);
            if (conVersiones && ((string)a.dominio != r.Dominio || (long)a.id_categoria_producto != r.IdCategoria || (string)a.estrategia_versionado != r.EstrategiaVersionado))
                return ResultadoOperacion.Fallo(409, "en_uso", "Ya tiene versiones publicadas: no se puede cambiar el dominio, la categoria ni la estrategia de versionado.");
            var cod = r.Codigo.Trim().ToUpperInvariant();
            await c.ExecuteAsync(
                """
                UPDATE prd.PRODUCTO_OPERATIVO SET codigo = @cod, nombre = @n, descripcion = @d, dominio = @dom, id_categoria_producto = @cat, id_categoria_regulatoria = @cr,
                       estrategia_versionado = @est, tipo_persona_aplicable = @per, id_calendario = @cal, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_producto_operativo = @id
                """, new { cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), dom = r.Dominio, cat = r.IdCategoria, cr = r.IdCategoriaRegulatoria, est = r.EstrategiaVersionado, per = r.TipoPersona, cal = r.IdCalendario, por = actor.NombreDescriptivo, id }, tx);
            async Task Cambio(string campo, string? ant, string? nue) { if (ant != nue) await Auditoria.CambioAsync(c, tx, actor, EntidadOperativo, id, campo, ant, nue); }
            await Cambio("codigo", (string)a.codigo, cod); await Cambio("nombre", (string)a.nombre, r.Nombre.Trim()); await Cambio("descripcion", (string?)a.descripcion, r.Descripcion?.Trim());
            await Cambio("id_categoria_producto", ((long)a.id_categoria_producto).ToString(), r.IdCategoria.ToString());
            await Cambio("id_categoria_regulatoria", ((long?)a.id_categoria_regulatoria)?.ToString(), r.IdCategoriaRegulatoria?.ToString());
            await Cambio("estrategia_versionado", (string)a.estrategia_versionado, r.EstrategiaVersionado); await Cambio("tipo_persona_aplicable", (string)a.tipo_persona_aplicable, r.TipoPersona);
            await Cambio("id_calendario", ((long?)a.id_calendario)?.ToString(), r.IdCalendario?.ToString());

            var previas = (await c.QueryAsync("SELECT id_moneda, es_principal FROM prd.PRODUCTO_OPERATIVO_MONEDA WHERE id_producto_operativo = @id", new { id }, tx)).ToDictionary(m => (long)m.id_moneda, m => (bool)m.es_principal);
            var nuevas = r.Monedas.ToDictionary(m => m.IdMoneda, m => m.EsPrincipal);
            if (!previas.OrderBy(k => k.Key).SequenceEqual(nuevas.OrderBy(k => k.Key)))
            {
                await c.ExecuteAsync("DELETE FROM prd.PRODUCTO_OPERATIVO_MONEDA WHERE id_producto_operativo = @id", new { id }, tx);
                foreach (var m in r.Monedas)
                    await c.ExecuteAsync("INSERT INTO prd.PRODUCTO_OPERATIVO_MONEDA (id_producto_operativo, id_moneda, es_principal, creado_por) VALUES (@id, @m, @p, @por)", new { id, m = m.IdMoneda, p = m.EsPrincipal, por = actor.NombreDescriptivo }, tx);
                await Auditoria.CambioAsync(c, tx, actor, EntidadOperativo, id, "monedas", string.Join(",", previas.Keys.OrderBy(x => x)), string.Join(",", nuevas.Keys.OrderBy(x => x)));
            }
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un producto operativo con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoOperativoAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Borrador" or "Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT estado FROM prd.PRODUCTO_OPERATIVO WITH (UPDLOCK) WHERE id_producto_operativo = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el producto operativo.");
        if (estado == "Activo")
        {
            var obligatorios = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM prd.PARAMETRO_PRODUCTO_OPERATIVO WHERE id_producto_operativo = @id AND estado = 'Activo'", new { id }, tx);
            if (obligatorios == 0) return ResultadoOperacion.Fallo(409, "sin_parametros", "Defina al menos un parametro antes de activar el producto.");
        }
        if (estado == "Inactivo")
        {
            var vig = await c.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM prd.PRODUCTO_COMERCIAL k WHERE k.id_producto_operativo = @id AND k.estado = 'Activo'
                """, new { id }, tx);
            if (vig > 0) return ResultadoOperacion.Fallo(409, "en_uso", $"Tiene {vig} producto(s) comercial(es) activo(s); inactivelos primero.");
        }
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE prd.PRODUCTO_OPERATIVO SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_producto_operativo = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadOperativo, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    /// <summary>Listas de valores activas que se pueden asignar a un parametro de tipo Lista.</summary>
    public async Task<IReadOnlyList<object>> ListasDisponiblesAsync()
    {
        using var c = NuevaConexion();
        return (await c.QueryAsync("SELECT id_lista_valores_tipo AS id, codigo, nombre FROM gen.LISTA_VALORES_TIPO WHERE estado = 'Activo' ORDER BY nombre")).Cast<object>().ToList();
    }

    // =============================================================== Parametros
    public async Task<IReadOnlyList<ParametroProductoResponse>> ListarParametrosAsync(long idProducto)
    {
        using var c = NuevaConexion();
        var f = await c.QueryAsync(
            """
            SELECT p.*, lt.nombre AS lista_nombre, lt.codigo AS lista_codigo, b.nombre AS base_nombre FROM prd.PARAMETRO_PRODUCTO_OPERATIVO p
            LEFT JOIN gen.LISTA_VALORES_TIPO lt ON lt.id_lista_valores_tipo = p.id_lista_valores_tipo
            LEFT JOIN gen.LISTA_VALORES_ITEM b ON b.id_lista_valores_item = p.id_base_componente
            WHERE p.id_producto_operativo = @idProducto ORDER BY p.orden, p.codigo
            """, new { idProducto });
        return f.Select(p => MapParametro(p)).Cast<ParametroProductoResponse>().ToList();
    }

    private static ParametroProductoResponse MapParametro(dynamic p) => new((long)p.id_parametro_producto, (string)p.codigo, (string)p.nombre, (string?)p.descripcion, (string)p.grupo, (string)p.tipo_dato,
        (long?)p.id_lista_valores_tipo, (string?)p.lista_nombre, (string?)p.lista_codigo, (decimal?)p.valor_minimo, (decimal?)p.valor_maximo, (bool)p.obligatorio, (string?)p.valor_defecto, (bool)p.es_componente_economico,
        (string?)p.tipo_calculo_economico, (long?)p.id_base_componente, (string?)p.base_nombre, (string?)p.codigo_tipo_evento, (int)p.orden, (string)p.estado);

    private static async Task<string?> ValidarParametroAsync(SqlConnection c, SqlTransaction tx, ParametroProductoGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Trim().Length > 60 || !System.Text.RegularExpressions.Regex.IsMatch(r.Codigo.Trim(), "^[A-Za-z][A-Za-z0-9_]*$"))
            return "El codigo es obligatorio: letras, numeros y guion bajo (maximo 60), empezando con letra.";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        if (!Grupos.Contains(r.Grupo)) return "Grupo invalido.";
        if (!Tipos.Contains(r.TipoDato)) return "Tipo de dato invalido.";
        if (r.TipoDato == "Lista" && r.IdListaTipo is null) return "Indique la lista de valores.";
        if (r.TipoDato != "Lista" && r.IdListaTipo is not null) return "Solo los parametros de tipo Lista llevan lista de valores.";
        if (r.TipoDato is not ("Entero" or "Decimal") && (r.Minimo is not null || r.Maximo is not null)) return "Los limites solo aplican a Entero y Decimal.";
        if (r.Minimo is not null && r.Maximo is not null && r.Minimo > r.Maximo) return "El minimo no puede ser mayor que el maximo.";
        if (r.EsComponenteEconomico)
        {
            if (r.TipoDato != "Decimal") return "Un componente economico es de tipo Decimal.";
            if (r.TipoCalculoEconomico is not ("MontoFijo" or "Porcentaje")) return "Indique si el componente es Monto fijo o Porcentaje.";
            if (r.TipoCalculoEconomico == "Porcentaje" && r.IdBaseComponente is null) return "Un porcentaje necesita la base sobre la que se aplica.";
            if (r.TipoCalculoEconomico == "MontoFijo" && r.IdBaseComponente is not null) return "Un monto fijo no lleva base.";
        }
        else if (r.TipoCalculoEconomico is not null || r.IdBaseComponente is not null) return "Solo un componente economico lleva tipo de calculo y base.";
        if (r.IdListaTipo is not null && !await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE id_lista_valores_tipo = @i) THEN 1 ELSE 0 END", new { i = r.IdListaTipo }, tx))
            return "La lista de valores no existe.";
        if (r.IdBaseComponente is not null && !await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE t.codigo = 'BASE_COMPONENTE_ECONOMICO' AND i.id_lista_valores_item = @i AND i.estado = 'Activo') THEN 1 ELSE 0 END",
            new { i = r.IdBaseComponente }, tx)) return "La base del componente no existe o esta inactiva.";
        return null;
    }

    public async Task<ResultadoOperacion> GuardarParametroAsync(ActorContext actor, long idProducto, long? id, ParametroProductoGuardarRequest r)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            if (!await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM prd.PRODUCTO_OPERATIVO WHERE id_producto_operativo = @i AND id_compania = @c) THEN 1 ELSE 0 END", new { i = idProducto, c = actor.IdCompania }, tx))
                return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el producto operativo.");
            var err = await ValidarParametroAsync(c, tx, r);
            if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
            var cod = r.Codigo.Trim();
            var p = new { idProducto, cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), g = r.Grupo, t = r.TipoDato, l = r.IdListaTipo, mn = r.Minimo, mx = r.Maximo, ob = r.Obligatorio,
                          df = string.IsNullOrWhiteSpace(r.ValorDefecto) ? null : r.ValorDefecto.Trim(), ec = r.EsComponenteEconomico, tc = r.TipoCalculoEconomico, bc = r.IdBaseComponente,
                          ev = string.IsNullOrWhiteSpace(r.CodigoTipoEvento) ? null : r.CodigoTipoEvento.Trim(), o = r.Orden, por = actor.NombreDescriptivo, id };
            if (id is null)
            {
                var nuevo = await c.QuerySingleAsync<long>(
                    """
                    INSERT INTO prd.PARAMETRO_PRODUCTO_OPERATIVO (id_producto_operativo, codigo, nombre, descripcion, grupo, tipo_dato, id_lista_valores_tipo, valor_minimo, valor_maximo, obligatorio, valor_defecto,
                           es_componente_economico, tipo_calculo_economico, id_base_componente, codigo_tipo_evento, orden, creado_por)
                    OUTPUT INSERTED.id_parametro_producto VALUES (@idProducto, @cod, @n, @d, @g, @t, @l, @mn, @mx, @ob, @df, @ec, @tc, @bc, @ev, @o, @por)
                    """, p, tx);
                await Auditoria.AltaAsync(c, tx, actor, EntidadParametro, nuevo, $"{cod} · {r.Nombre.Trim()}");
                tx.Commit();
                return ResultadoOperacion.Ok(nuevo);
            }
            var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM prd.PARAMETRO_PRODUCTO_OPERATIVO WITH (UPDLOCK) WHERE id_parametro_producto = @id AND id_producto_operativo = @idProducto", p, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el parametro.");
            var usado = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM prd.PRODUCTO_COMERCIAL_VERSION_VALOR WHERE id_parametro_producto = @id) THEN 1 ELSE 0 END", new { id }, tx);
            if (usado && ((string)a.tipo_dato != r.TipoDato || (string)a.codigo != cod || (long?)a.id_lista_valores_tipo != r.IdListaTipo || (bool)a.es_componente_economico != r.EsComponenteEconomico))
                return ResultadoOperacion.Fallo(409, "en_uso", "Ya tiene valores en versiones: no se puede cambiar el codigo, el tipo de dato, la lista ni su condicion de componente economico.");
            await c.ExecuteAsync(
                """
                UPDATE prd.PARAMETRO_PRODUCTO_OPERATIVO SET codigo = @cod, nombre = @n, descripcion = @d, grupo = @g, tipo_dato = @t, id_lista_valores_tipo = @l, valor_minimo = @mn, valor_maximo = @mx,
                       obligatorio = @ob, valor_defecto = @df, es_componente_economico = @ec, tipo_calculo_economico = @tc, id_base_componente = @bc, codigo_tipo_evento = @ev, orden = @o,
                       modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_parametro_producto = @id
                """, p, tx);
            async Task Cambio(string campo, string? ant, string? nue) { if (ant != nue) await Auditoria.CambioAsync(c, tx, actor, EntidadParametro, id.Value, campo, ant, nue); }
            await Cambio("codigo", (string)a.codigo, cod); await Cambio("nombre", (string)a.nombre, r.Nombre.Trim()); await Cambio("grupo", (string)a.grupo, r.Grupo);
            await Cambio("valor_minimo", ((decimal?)a.valor_minimo)?.ToString("0.######", CultureInfo.InvariantCulture), r.Minimo?.ToString("0.######", CultureInfo.InvariantCulture));
            await Cambio("valor_maximo", ((decimal?)a.valor_maximo)?.ToString("0.######", CultureInfo.InvariantCulture), r.Maximo?.ToString("0.######", CultureInfo.InvariantCulture));
            await Cambio("obligatorio", ((bool)a.obligatorio).ToString(), r.Obligatorio.ToString()); await Cambio("valor_defecto", (string?)a.valor_defecto, (string?)p.df);
            await Cambio("tipo_calculo_economico", (string?)a.tipo_calculo_economico, r.TipoCalculoEconomico); await Cambio("orden", ((int)a.orden).ToString(), r.Orden.ToString());
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un parametro con ese codigo en el producto."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoParametroAsync(ActorContext actor, long idProducto, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT p.estado FROM prd.PARAMETRO_PRODUCTO_OPERATIVO p JOIN prd.PRODUCTO_OPERATIVO o ON o.id_producto_operativo = p.id_producto_operativo WHERE p.id_parametro_producto = @id AND p.id_producto_operativo = @pr AND o.id_compania = @c", new { id, pr = idProducto, c = actor.IdCompania }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el parametro.");
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE prd.PARAMETRO_PRODUCTO_OPERATIVO SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_parametro_producto = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadParametro, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    // =============================================================== Plantilla estandar (D-1)
    private record Plantilla(string Codigo, string Nombre, string Grupo, string Tipo, string? Lista, decimal? Min, decimal? Max, bool Obligatorio, string? Defecto, string? Calculo = null, string? Base = null);

    private static readonly Plantilla[] PlantillaCredito =
    [
        new("monto_minimo", "Monto minimo", "Monto", "Decimal", null, 0, null, true, null),
        new("monto_maximo", "Monto maximo", "Monto", "Decimal", null, 0, null, true, null),
        new("plazo_minimo_meses", "Plazo minimo (meses)", "Plazo", "Entero", null, 1, 600, true, null),
        new("plazo_maximo_meses", "Plazo maximo (meses)", "Plazo", "Entero", null, 1, 600, true, null),
        new("tipo_tasa", "Tipo de tasa", "Tasa", "Lista", "TIPO_TASA", null, null, true, "FIJA"),
        new("tasa_nominal_anual", "Tasa nominal anual (%) - tasa fija", "Tasa", "Decimal", null, 0, 1000, false, null),
        new("indicador_referencia", "Indicador de referencia - tasa variable", "Tasa", "Texto", null, null, null, false, null),
        new("spread", "Spread sobre el indicador (%)", "Tasa", "Decimal", null, 0, 1000, false, null),
        new("tasa_piso", "Tasa piso (%)", "Tasa", "Decimal", null, 0, 1000, false, null),
        new("tasa_techo", "Tasa techo (%)", "Tasa", "Decimal", null, 0, 1000, false, null),
        new("revision_tasa_meses", "Revision de tasa cada (meses)", "Tasa", "Entero", null, 1, 120, false, null),
        new("tipo_calculo", "Tipo de calculo del plan de pagos", "PlanPago", "Lista", "TIPO_CALCULO_PLAN_PAGO", null, null, true, "NIVELADA"),
        new("frecuencia_pago", "Frecuencia de pago", "PlanPago", "Lista", "FRECUENCIA_PAGO", null, null, true, "MENSUAL"),
        new("base_calculo", "Base de calculo de intereses", "PlanPago", "Texto", null, null, null, true, "30_360"),
        new("convencion_ajuste", "Convencion de ajuste de fecha", "PlanPago", "Lista", "CONVENCION_AJUSTE_FECHA", null, null, true, "SIGUIENTE_HABIL"),
        new("gracia_capital_cuotas", "Gracia de capital (cuotas)", "Gracia", "Entero", null, 0, 120, false, "0"),
        new("gracia_intereses_cuotas", "Gracia de intereses (cuotas)", "Gracia", "Entero", null, 0, 120, false, "0"),
        new("tasa_mora_anual", "Tasa moratoria anual (%)", "Mora", "Decimal", null, 0, 1000, false, null),
        new("comision_formalizacion", "Comision de formalizacion", "Comision", "Decimal", null, 0, null, false, null, "Porcentaje", "MONTO_DESEMBOLSADO"),
        new("seguro_saldo", "Seguro sobre saldo", "Seguro", "Decimal", null, 0, null, false, null, "Porcentaje", "SALDO_INSOLUTO"),
        new("cargo_cobranza", "Cargo de cobranza", "Comision", "Decimal", null, 0, null, false, null, "MontoFijo", null),
    ];

    private static readonly Plantilla[] PlantillaCuenta =
    [
        new("tasa_interes_anual", "Tasa de interes anual (%)", "Tasa", "Decimal", null, 0, 1000, false, null),
        new("saldo_minimo", "Saldo minimo", "Monto", "Decimal", null, 0, null, false, null),
        new("base_calculo", "Base de calculo de intereses", "PlanPago", "Texto", null, null, null, true, "ACT_365"),
        new("comision_mantenimiento", "Comision de mantenimiento", "Comision", "Decimal", null, 0, null, false, null, "MontoFijo", null),
    ];

    private static async Task SembrarPlantillaAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idProducto, string dominio)
    {
        var orden = 0;
        foreach (var t in dominio == "Credito" ? PlantillaCredito : PlantillaCuenta)
        {
            orden += 10;
            long? lista = t.Lista is null ? null : await c.ExecuteScalarAsync<long?>("SELECT id_lista_valores_tipo FROM gen.LISTA_VALORES_TIPO WHERE codigo = @c", new { c = t.Lista }, tx);
            long? bc = t.Base is null ? null : await c.ExecuteScalarAsync<long?>(
                "SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO l ON l.id_lista_valores_tipo = i.id_lista_valores_tipo WHERE l.codigo = 'BASE_COMPONENTE_ECONOMICO' AND i.codigo = @c", new { c = t.Base }, tx);
            var idPar = await c.QuerySingleAsync<long>(
                """
                INSERT INTO prd.PARAMETRO_PRODUCTO_OPERATIVO (id_producto_operativo, codigo, nombre, grupo, tipo_dato, id_lista_valores_tipo, valor_minimo, valor_maximo, obligatorio, valor_defecto,
                       es_componente_economico, tipo_calculo_economico, id_base_componente, orden, creado_por)
                OUTPUT INSERTED.id_parametro_producto VALUES (@p, @cod, @n, @g, @t, @l, @mn, @mx, @ob, @df, @ec, @tc, @bc, @o, @por)
                """, new { p = idProducto, cod = t.Codigo, n = t.Nombre, g = t.Grupo, t = t.Tipo, l = lista, mn = t.Min, mx = t.Max, ob = t.Obligatorio, df = t.Defecto, ec = t.Calculo is not null, tc = t.Calculo, bc, o = orden, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadParametro, idPar, $"{t.Codigo} · {t.Nombre} (plantilla)");
        }
    }
}
