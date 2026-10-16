using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Cimientos de Creditos (ronda 42): bases de calculo, indicadores financieros (tasas de referencia), calendarios y categorias regulatorias.
/// El mantenimiento es directo con auditoria; la carga de un valor de indicador pasa por gestion (PRD_INDICADOR_VALOR).
/// </summary>
public class CatalogoFinancieroRepositorio
{
    public const string Permiso = "IND_ADMIN";
    public const string PermisoCarga = "IND_CARGA";
    public const string PermisoConsulta = "PRD_CONSULTA";
    public const string TipoGestionValor = "PRD_INDICADOR_VALOR";
    public const string EntidadBase = "BASE_CALCULO", EntidadIndicador = "INDICADOR_FINANCIERO", EntidadValor = "INDICADOR_VALOR",
        EntidadCalendario = "CALENDARIO", EntidadCategoriaReg = "CATEGORIA_REGULATORIA";
    private static readonly string[] Frecuencias = ["Diaria", "Semanal", "Mensual", "Trimestral"];

    private readonly TenantContext _tenant;
    private readonly NumeracionServicio _numeracion;
    public CatalogoFinancieroRepositorio(TenantContext tenant, NumeracionServicio numeracion) { _tenant = tenant; _numeracion = numeracion; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private static bool Duplicado(SqlException ex) => ex.Number is 2601 or 2627;
    public static string Num(decimal v) => v.ToString("0.########", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- Bases de calculo
    public async Task<IReadOnlyList<BaseCalculoResponse>> ListarBasesAsync()
    {
        using var c = NuevaConexion();
        var f = await c.QueryAsync("SELECT * FROM gen.BASE_CALCULO ORDER BY id_base_calculo");
        return f.Select(b => new BaseCalculoResponse((long)b.id_base_calculo, (string)b.codigo, (string)b.nombre, (string?)b.descripcion, (string)b.tipo_conteo,
            b.dias_anio_fijo is null ? null : (int)(short)b.dias_anio_fijo, (bool)b.usa_anio_real, (string)b.estado)).Cast<BaseCalculoResponse>().ToList();
    }

    public async Task<ResultadoOperacion> CambiarEstadoBaseAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT estado FROM gen.BASE_CALCULO WITH (UPDLOCK) WHERE id_base_calculo = @id", new { id }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la base de calculo.");
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE gen.BASE_CALCULO SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_base_calculo = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadBase, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    // ---------------------------------------------------------------- Indicadores
    public async Task<IReadOnlyList<IndicadorResponse>> ListarIndicadoresAsync()
    {
        using var c = NuevaConexion();
        var f = await c.QueryAsync(
            """
            SELECT i.*, t.nombre AS tipo_nombre, v.valor AS valor_vigente, v.fecha_vigencia AS fecha_vigente
            FROM gen.INDICADOR_FINANCIERO i
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = i.id_tipo_indicador
            OUTER APPLY (SELECT TOP 1 x.valor, x.fecha_vigencia FROM gen.INDICADOR_FINANCIERO_VALOR x
                          WHERE x.id_indicador_financiero = i.id_indicador_financiero AND x.fecha_vigencia <= CAST(GETDATE() AS date)
                          ORDER BY x.fecha_vigencia DESC) v
            ORDER BY i.codigo
            """);
        return f.Select(i => new IndicadorResponse((long)i.id_indicador_financiero, (string)i.codigo, (string)i.nombre, (string?)i.descripcion, (long)i.id_tipo_indicador,
            (string)i.tipo_nombre, (string?)i.moneda_codigo, (string)i.frecuencia, (string)i.estado, (decimal?)i.valor_vigente,
            i.fecha_vigente is null ? null : DateOnly.FromDateTime((DateTime)i.fecha_vigente))).Cast<IndicadorResponse>().ToList();
    }

    private static string? ValidarIndicador(IndicadorGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Trim().Length > 30) return "El codigo es obligatorio (maximo 30 caracteres).";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        if (!Frecuencias.Contains(r.Frecuencia)) return "Frecuencia invalida.";
        if (r.MonedaCodigo is { Length: > 0 } m && m.Trim().Length != 3) return "La moneda debe ser un codigo ISO de 3 letras.";
        return null;
    }

    public async Task<ResultadoOperacion> CrearIndicadorAsync(ActorContext actor, IndicadorGuardarRequest r)
    {
        var err = ValidarIndicador(r); if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var okTipo = await c.ExecuteScalarAsync<bool>(
                """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                  WHERE t.codigo = 'TIPO_INDICADOR_FINANCIERO' AND i.id_lista_valores_item = @t AND i.estado = 'Activo') THEN 1 ELSE 0 END
                """, new { t = r.IdTipo }, tx);
            if (!okTipo) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El tipo de indicador no existe o esta inactivo.");
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO gen.INDICADOR_FINANCIERO (codigo, nombre, descripcion, id_tipo_indicador, moneda_codigo, frecuencia, creado_por)
                OUTPUT INSERTED.id_indicador_financiero VALUES (@cod, @n, @d, @t, @m, @f, @por)
                """, new { cod = r.Codigo.Trim().ToUpperInvariant(), n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), t = r.IdTipo, m = r.MonedaCodigo?.Trim().ToUpperInvariant(), f = r.Frecuencia, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadIndicador, id, $"{r.Codigo.Trim().ToUpperInvariant()} · {r.Nombre.Trim()}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un indicador con ese codigo."); }
    }

    public async Task<ResultadoOperacion> EditarIndicadorAsync(ActorContext actor, long id, IndicadorGuardarRequest r)
    {
        var err = ValidarIndicador(r); if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM gen.INDICADOR_FINANCIERO WITH (UPDLOCK) WHERE id_indicador_financiero = @id", new { id }, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el indicador.");
            var cod = r.Codigo.Trim().ToUpperInvariant();
            await c.ExecuteAsync(
                """
                UPDATE gen.INDICADOR_FINANCIERO SET codigo = @cod, nombre = @n, descripcion = @d, id_tipo_indicador = @t, moneda_codigo = @m, frecuencia = @f,
                       modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_indicador_financiero = @id
                """, new { cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), t = r.IdTipo, m = r.MonedaCodigo?.Trim().ToUpperInvariant(), f = r.Frecuencia, por = actor.NombreDescriptivo, id }, tx);
            async Task Cambio(string campo, string? ant, string? nue) { if (ant != nue) await Auditoria.CambioAsync(c, tx, actor, EntidadIndicador, id, campo, ant, nue); }
            await Cambio("codigo", (string)a.codigo, cod); await Cambio("nombre", (string)a.nombre, r.Nombre.Trim());
            await Cambio("descripcion", (string?)a.descripcion, r.Descripcion?.Trim()); await Cambio("id_tipo_indicador", ((long)a.id_tipo_indicador).ToString(), r.IdTipo.ToString());
            await Cambio("moneda_codigo", (string?)a.moneda_codigo, r.MonedaCodigo?.Trim().ToUpperInvariant()); await Cambio("frecuencia", (string)a.frecuencia, r.Frecuencia);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un indicador con ese codigo."); }
    }

    public async Task<ResultadoOperacion> CambiarEstadoIndicadorAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT estado FROM gen.INDICADOR_FINANCIERO WITH (UPDLOCK) WHERE id_indicador_financiero = @id", new { id }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el indicador.");
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE gen.INDICADOR_FINANCIERO SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_indicador_financiero = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadIndicador, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<IReadOnlyList<IndicadorValorResponse>> ListarValoresAsync(long idIndicador)
    {
        using var c = NuevaConexion();
        var f = await c.QueryAsync(
            """
            SELECT TOP (500) v.*, (SELECT g.numero FROM apr.GESTION g WHERE g.id_gestion = v.id_gestion) AS numero_gestion
            FROM gen.INDICADOR_FINANCIERO_VALOR v WHERE v.id_indicador_financiero = @idIndicador ORDER BY v.fecha_vigencia DESC
            """, new { idIndicador });
        return f.Select(v => new IndicadorValorResponse((long)v.id_indicador_valor, DateOnly.FromDateTime((DateTime)v.fecha_vigencia), (decimal)v.valor, (string)v.origen,
            (long?)v.id_gestion, (string?)v.numero_gestion, (string)v.creado_por, (DateTime)v.creado_en)).Cast<IndicadorValorResponse>().ToList();
    }

    /// <summary>Solicita (gestion) la carga del valor de un indicador en una fecha.</summary>
    public async Task<ResultadoOperacion> SolicitarValorAsync(ActorContext actor, long idIndicador, IndicadorValorSolicitudRequest req)
    {
        if (actor.EsIntegracion) return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");
        if (req.Valor < 0 || req.Valor > 1000 || decimal.Round(req.Valor, 8) != req.Valor) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El valor debe estar entre 0 y 1000, con maximo 8 decimales.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var ind = await c.QuerySingleOrDefaultAsync("SELECT codigo, estado FROM gen.INDICADOR_FINANCIERO WHERE id_indicador_financiero = @id", new { id = idIndicador }, tx);
            if (ind is null || (string)ind.estado != "Activo") return ResultadoOperacion.Fallo(400, "datos_invalidos", "El indicador no existe o esta inactivo.");
            var fecha = req.FechaVigencia.ToDateTime(TimeOnly.MinValue);
            var existente = await c.ExecuteScalarAsync<decimal?>(
                "SELECT valor FROM gen.INDICADOR_FINANCIERO_VALOR WHERE id_indicador_financiero = @i AND fecha_vigencia = @f", new { i = idIndicador, f = fecha }, tx);
            var llave = $"{idIndicador}|{req.FechaVigencia:yyyy-MM-dd}";
            var r = await GestionSolicitudes.CrearAsync(c, tx, _tenant, _numeracion, actor, TipoGestionValor, EntidadValor, llave, req.Comentario,
                [("id_indicador", null, idIndicador.ToString(CultureInfo.InvariantCulture)),
                 ("fecha_vigencia", null, req.FechaVigencia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                 ("valor", existente is null ? null : Num(existente.Value), Num(req.Valor))]);
            if (r.Exito) tx.Commit(); else tx.Rollback();
            return r;
        }
        catch { tx.Rollback(); throw; }
    }

    public const int MaxFilasLote = 500;

    /// <summary>
    /// Carga por lote (desde Excel): cada fila valida genera su propia gestion PRD_INDICADOR_VALOR (se aprueban y se rechazan por separado).
    /// Las filas con error no detienen al resto; el resultado informa el estado de cada una.
    /// </summary>
    public async Task<IReadOnlyList<IndicadorLoteResultado>> SolicitarValoresLoteAsync(ActorContext actor, IReadOnlyList<IndicadorLoteFila> filas)
    {
        var resultados = new List<IndicadorLoteResultado>(filas.Count);
        Dictionary<string, long> indicadores;
        using (var c = NuevaConexion())
        {
            c.Open();
            indicadores = (await c.QueryAsync("SELECT id_indicador_financiero, codigo FROM gen.INDICADOR_FINANCIERO WHERE estado = 'Activo'"))
                .ToDictionary(x => ((string)x.codigo).ToUpperInvariant(), x => (long)x.id_indicador_financiero);
        }
        var vistos = new Dictionary<(string, DateOnly), int>();
        foreach (var f in filas)
        {
            var codigo = (f.Codigo ?? "").Trim().ToUpperInvariant();
            IndicadorLoteResultado Res(bool ok, string? msg, long? idGestion = null, string? numero = null) => new(f.Fila, codigo, f.FechaVigencia, ok, idGestion, numero, msg);
            if (!indicadores.TryGetValue(codigo, out var idInd)) { resultados.Add(Res(false, "El indicador no existe o esta inactivo.")); continue; }
            if (vistos.TryGetValue((codigo, f.FechaVigencia), out var previa)) { resultados.Add(Res(false, $"Repetida en el archivo (ya viene en la fila {previa}).")); continue; }
            vistos[(codigo, f.FechaVigencia)] = f.Fila;
            decimal? existente;
            using (var c = NuevaConexion())
            {
                c.Open();
                existente = await c.ExecuteScalarAsync<decimal?>("SELECT valor FROM gen.INDICADOR_FINANCIERO_VALOR WHERE id_indicador_financiero = @i AND fecha_vigencia = @f",
                    new { i = idInd, f = f.FechaVigencia.ToDateTime(TimeOnly.MinValue) });
            }
            if (existente is not null && existente.Value == f.Valor) { resultados.Add(Res(false, "Sin cambios: ya existe ese mismo valor para esa fecha.")); continue; }
            var r = await SolicitarValorAsync(actor, idInd, new IndicadorValorSolicitudRequest(f.FechaVigencia, f.Valor, string.IsNullOrWhiteSpace(f.Comentario) ? "Carga desde Excel" : f.Comentario.Trim()));
            resultados.Add(r.Exito ? Res(true, existente is null ? "Solicitud creada." : "Solicitud de correccion creada.", r.Id, r.Numero) : Res(false, r.Mensaje ?? r.Error));
        }
        return resultados;
    }

    /// <summary>Aplicador de gestiones PRD_INDICADOR_VALOR (llamado al aprobarse).</summary>
    public static async Task<ResultadoOperacion?> AplicarValorAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion)
    {
        var det = (await c.QueryAsync("SELECT nombre_campo, valor_anterior, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx))
            .ToDictionary(d => (string)d.nombre_campo, d => (Anterior: (string?)d.valor_anterior, Nuevo: (string?)d.valor_nuevo));
        foreach (var k in new[] { "id_indicador", "fecha_vigencia", "valor" })
            if (!det.ContainsKey(k)) return ResultadoOperacion.Fallo(500, "gestion_incompleta", $"La gestion no tiene el campo '{k}'.");
        var idInd = long.Parse(det["id_indicador"].Nuevo!, CultureInfo.InvariantCulture);
        var fecha = DateTime.ParseExact(det["fecha_vigencia"].Nuevo!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var valor = decimal.Parse(det["valor"].Nuevo!, CultureInfo.InvariantCulture);
        var estado = await c.ExecuteScalarAsync<string?>("SELECT estado FROM gen.INDICADOR_FINANCIERO WHERE id_indicador_financiero = @id", new { id = idInd }, tx);
        if (estado != "Activo") return ResultadoOperacion.Fallo(409, "indicador_invalido", "El indicador ya no esta activo; rechace la gestion.");
        var actual = await c.QuerySingleOrDefaultAsync(
            "SELECT id_indicador_valor, valor FROM gen.INDICADOR_FINANCIERO_VALOR WITH (UPDLOCK, HOLDLOCK) WHERE id_indicador_financiero = @i AND fecha_vigencia = @f", new { i = idInd, f = fecha }, tx);
        if (actual is null)
        {
            if (det["valor"].Anterior is not null) return ResultadoOperacion.Fallo(409, "registro_cambio", "El valor que se iba a corregir ya no existe; rechace la gestion y solicite de nuevo.");
            var id = await c.QuerySingleAsync<long>(
                "INSERT INTO gen.INDICADOR_FINANCIERO_VALOR (id_indicador_financiero, fecha_vigencia, valor, origen, id_gestion, creado_por) OUTPUT INSERTED.id_indicador_valor VALUES (@i, @f, @v, 'Manual', @g, @por)",
                new { i = idInd, f = fecha, v = valor, g = idGestion, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadValor, id, $"{det["fecha_vigencia"].Nuevo} · {Num(valor)}", idGestion);
            return null;
        }
        if (det["valor"].Anterior is null || Num((decimal)actual.valor) != det["valor"].Anterior)
            return ResultadoOperacion.Fallo(409, "registro_cambio", "El valor cambio desde que se solicito la gestion; rechacela y solicite de nuevo.");
        await c.ExecuteAsync("UPDATE gen.INDICADOR_FINANCIERO_VALOR SET valor = @v, id_gestion = @g WHERE id_indicador_valor = @id", new { v = valor, g = idGestion, id = (long)actual.id_indicador_valor }, tx);
        await Auditoria.CambioAsync(c, tx, actor, EntidadValor, (long)actual.id_indicador_valor, "valor", det["valor"].Anterior, det["valor"].Nuevo, idGestion);
        return null;
    }

    // ---------------------------------------------------------------- Calendarios
    private static CalendarioResponse MapCal(dynamic k) => new((long)k.id_calendario, (string)k.codigo, (string)k.nombre, (long)k.id_pais, (string)k.pais_nombre,
        (bool)k.lunes, (bool)k.martes, (bool)k.miercoles, (bool)k.jueves, (bool)k.viernes, (bool)k.sabado, (bool)k.domingo, (bool)k.es_defecto, (string)k.estado);

    public async Task<IReadOnlyList<CalendarioResponse>> ListarCalendariosAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var f = await c.QueryAsync(
            "SELECT k.*, p.nombre AS pais_nombre FROM gen.CALENDARIO k JOIN gen.LISTA_VALORES_ITEM p ON p.id_lista_valores_item = k.id_pais WHERE k.id_compania = @idCompania ORDER BY k.codigo", new { idCompania });
        return f.Select(k => (CalendarioResponse)MapCal(k)).ToList();
    }

    private static string? ValidarCalendario(CalendarioGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Trim().Length > 30) return "El codigo es obligatorio (maximo 30 caracteres).";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 150) return "El nombre es obligatorio (maximo 150 caracteres).";
        if (!(r.Lunes || r.Martes || r.Miercoles || r.Jueves || r.Viernes || r.Sabado || r.Domingo)) return "Debe haber al menos un dia habil en la semana.";
        return null;
    }

    public async Task<ResultadoOperacion> GuardarCalendarioAsync(ActorContext actor, long? id, CalendarioGuardarRequest r)
    {
        var err = ValidarCalendario(r); if (err is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", err);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var okPais = await c.ExecuteScalarAsync<bool>(
                """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                  WHERE t.codigo = 'PAIS' AND i.id_lista_valores_item = @p AND i.estado = 'Activo') THEN 1 ELSE 0 END
                """, new { p = r.IdPais }, tx);
            if (!okPais) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El pais no existe o esta inactivo.");
            var cod = r.Codigo.Trim().ToUpperInvariant();
            var p = new { c = actor.IdCompania, cod, n = r.Nombre.Trim(), pais = r.IdPais, r.Lunes, r.Martes, r.Miercoles, r.Jueves, r.Viernes, r.Sabado, r.Domingo, def = r.EsDefecto, por = actor.NombreDescriptivo, id };
            if (r.EsDefecto)
                await c.ExecuteAsync("UPDATE gen.CALENDARIO SET es_defecto = 0 WHERE id_compania = @c AND es_defecto = 1 AND (@id IS NULL OR id_calendario <> @id)", p, tx);
            if (id is null)
            {
                var nuevo = await c.QuerySingleAsync<long>(
                    """
                    INSERT INTO gen.CALENDARIO (id_compania, codigo, nombre, id_pais, lunes, martes, miercoles, jueves, viernes, sabado, domingo, es_defecto, creado_por)
                    OUTPUT INSERTED.id_calendario VALUES (@c, @cod, @n, @pais, @Lunes, @Martes, @Miercoles, @Jueves, @Viernes, @Sabado, @Domingo, @def, @por)
                    """, p, tx);
                await Auditoria.AltaAsync(c, tx, actor, EntidadCalendario, nuevo, $"{cod} · {r.Nombre.Trim()}");
                tx.Commit();
                return ResultadoOperacion.Ok(nuevo);
            }
            var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM gen.CALENDARIO WITH (UPDLOCK) WHERE id_calendario = @id AND id_compania = @c", p, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el calendario.");
            await c.ExecuteAsync(
                """
                UPDATE gen.CALENDARIO SET codigo = @cod, nombre = @n, id_pais = @pais, lunes = @Lunes, martes = @Martes, miercoles = @Miercoles, jueves = @Jueves, viernes = @Viernes,
                       sabado = @Sabado, domingo = @Domingo, es_defecto = @def, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_calendario = @id
                """, p, tx);
            async Task Cambio(string campo, string? ant, string? nue) { if (ant != nue) await Auditoria.CambioAsync(c, tx, actor, EntidadCalendario, id.Value, campo, ant, nue); }
            await Cambio("codigo", (string)a.codigo, cod); await Cambio("nombre", (string)a.nombre, r.Nombre.Trim()); await Cambio("id_pais", ((long)a.id_pais).ToString(), r.IdPais.ToString());
            await Cambio("lunes", ((bool)a.lunes).ToString(), r.Lunes.ToString()); await Cambio("martes", ((bool)a.martes).ToString(), r.Martes.ToString());
            await Cambio("miercoles", ((bool)a.miercoles).ToString(), r.Miercoles.ToString()); await Cambio("jueves", ((bool)a.jueves).ToString(), r.Jueves.ToString());
            await Cambio("viernes", ((bool)a.viernes).ToString(), r.Viernes.ToString()); await Cambio("sabado", ((bool)a.sabado).ToString(), r.Sabado.ToString());
            await Cambio("domingo", ((bool)a.domingo).ToString(), r.Domingo.ToString()); await Cambio("es_defecto", ((bool)a.es_defecto).ToString(), r.EsDefecto.ToString());
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un calendario con ese codigo."); }
    }

    public async Task<ResultadoOperacion> CambiarEstadoCalendarioAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var a = await c.QuerySingleOrDefaultAsync("SELECT estado, es_defecto FROM gen.CALENDARIO WITH (UPDLOCK) WHERE id_calendario = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
        if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el calendario.");
        if (estado == "Inactivo")
        {
            if ((bool)a.es_defecto) return ResultadoOperacion.Fallo(409, "calendario_defecto", "Es el calendario por defecto; marque otro como defecto antes de inactivarlo.");
            var uso = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM prd.PRODUCTO_OPERATIVO WHERE id_calendario = @id AND estado <> 'Inactivo'", new { id }, tx);
            if (uso > 0) return ResultadoOperacion.Fallo(409, "en_uso", $"Lo usan {uso} producto(s) operativo(s) activo(s).");
        }
        if ((string)a.estado != estado)
        {
            await c.ExecuteAsync("UPDATE gen.CALENDARIO SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_calendario = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadCalendario, id, "estado", (string)a.estado, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    // ---------------------------------------------------------------- Categorias regulatorias
    public async Task<IReadOnlyList<CategoriaRegulatoriaResponse>> ListarCategoriasAsync(long? idPaisRegulador)
    {
        using var c = NuevaConexion();
        var f = await c.QueryAsync(
            """
            SELECT k.*, p.nombre_pais FROM gen.CATEGORIA_REGULATORIA k JOIN gen.PAIS_REGULADOR p ON p.id_pais_regulador = k.id_pais_regulador
            WHERE (@p IS NULL OR k.id_pais_regulador = @p) ORDER BY p.nombre_pais, k.codigo
            """, new { p = idPaisRegulador });
        return f.Select(k => new CategoriaRegulatoriaResponse((long)k.id_categoria_regulatoria, (long)k.id_pais_regulador, (string)k.nombre_pais, (string)k.codigo, (string)k.nombre,
            (string?)k.descripcion, (string)k.estado)).Cast<CategoriaRegulatoriaResponse>().ToList();
    }

    public async Task<ResultadoOperacion> GuardarCategoriaAsync(ActorContext actor, long? id, CategoriaRegulatoriaGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo) || r.Codigo.Trim().Length > 30) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El codigo es obligatorio (maximo 30 caracteres).");
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 150) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El nombre es obligatorio (maximo 150 caracteres).");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var cod = r.Codigo.Trim().ToUpperInvariant();
            if (id is null)
            {
                var nuevo = await c.QuerySingleAsync<long>(
                    "INSERT INTO gen.CATEGORIA_REGULATORIA (id_pais_regulador, codigo, nombre, descripcion, creado_por) OUTPUT INSERTED.id_categoria_regulatoria VALUES (@p, @cod, @n, @d, @por)",
                    new { p = r.IdPaisRegulador, cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), por = actor.NombreDescriptivo }, tx);
                await Auditoria.AltaAsync(c, tx, actor, EntidadCategoriaReg, nuevo, $"{cod} · {r.Nombre.Trim()}");
                tx.Commit();
                return ResultadoOperacion.Ok(nuevo);
            }
            var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM gen.CATEGORIA_REGULATORIA WITH (UPDLOCK) WHERE id_categoria_regulatoria = @id", new { id }, tx);
            if (a is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la categoria.");
            if ((long)a.id_pais_regulador != r.IdPaisRegulador)
            {
                var uso = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM prd.PRODUCTO_OPERATIVO WHERE id_categoria_regulatoria = @id", new { id }, tx);
                if (uso > 0) return ResultadoOperacion.Fallo(409, "en_uso", "La categoria esta en uso; no se puede cambiar de pais.");
            }
            await c.ExecuteAsync(
                "UPDATE gen.CATEGORIA_REGULATORIA SET id_pais_regulador = @p, codigo = @cod, nombre = @n, descripcion = @d, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_categoria_regulatoria = @id",
                new { p = r.IdPaisRegulador, cod, n = r.Nombre.Trim(), d = r.Descripcion?.Trim(), por = actor.NombreDescriptivo, id }, tx);
            async Task Cambio(string campo, string? ant, string? nue) { if (ant != nue) await Auditoria.CambioAsync(c, tx, actor, EntidadCategoriaReg, id.Value, campo, ant, nue); }
            await Cambio("codigo", (string)a.codigo, cod); await Cambio("nombre", (string)a.nombre, r.Nombre.Trim()); await Cambio("descripcion", (string?)a.descripcion, r.Descripcion?.Trim());
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe una categoria con ese codigo para ese pais."); }
        catch (SqlException ex) when (ex.Number == 547) { tx.Rollback(); return ResultadoOperacion.Fallo(400, "datos_invalidos", "El pais/regulador no existe."); }
    }

    public async Task<ResultadoOperacion> CambiarEstadoCategoriaAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT estado FROM gen.CATEGORIA_REGULATORIA WITH (UPDLOCK) WHERE id_categoria_regulatoria = @id", new { id }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la categoria.");
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE gen.CATEGORIA_REGULATORIA SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_categoria_regulatoria = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadCategoriaReg, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<IReadOnlyList<object>> PaisesReguladoresAsync()
    {
        using var c = NuevaConexion();
        return (await c.QueryAsync("SELECT id_pais_regulador AS id, nombre_pais + ' · ' + nombre_regulador AS nombre FROM gen.PAIS_REGULADOR ORDER BY nombre_pais")).Cast<object>().ToList();
    }
}
