using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Conjuntos de reglas, versiones (Borrador → Revision → Programado/Vigente → Expirado), edicion de reglas y elegibilidad de productos.</summary>
public partial class ReglasRepositorio
{
    // =============================================================== Conjuntos
    public async Task<IReadOnlyList<ConjuntoReglasResponse>> ListarConjuntosAsync(ActorContext actor)
    {
        using var c = NuevaConexion(); c.Open();
        await PromoverProgramadasAsync(c, null, actor.IdCompania, null);
        var f = await c.QueryAsync(
            """
            SELECT j.*, x.codigo AS ctx_codigo, x.nombre AS ctx_nombre,
              (SELECT numero_version FROM rul.CONJUNTO_REGLAS_VERSION v WHERE v.id_conjunto = j.id_conjunto AND v.estado = 'Vigente') AS vigente,
              (SELECT numero_version FROM rul.CONJUNTO_REGLAS_VERSION v WHERE v.id_conjunto = j.id_conjunto AND v.estado IN ('Borrador', 'Revision')) AS edicion,
              (SELECT numero_version FROM rul.CONJUNTO_REGLAS_VERSION v WHERE v.id_conjunto = j.id_conjunto AND v.estado = 'Programado') AS programada,
              (SELECT COUNT(*) FROM rul.CONJUNTO_REFERENCIA r WHERE r.id_conjunto = j.id_conjunto) AS referencias
            FROM rul.CONJUNTO_REGLAS j JOIN rul.CONTEXTO x ON x.id_contexto = j.id_contexto
            WHERE j.id_compania = @co ORDER BY j.codigo
            """, new { co = actor.IdCompania });
        return f.Select(j => new ConjuntoReglasResponse((long)j.id_conjunto, (long)j.id_contexto, (string)j.ctx_codigo, (string)j.ctx_nombre, (string)j.codigo, (string)j.nombre, (string?)j.descripcion,
            (string)j.politica_resolucion, (string)j.estado, (int?)j.vigente, (int?)j.edicion, (int?)j.programada, (int)j.referencias)).ToList();
    }

    public async Task<ResultadoOperacion> GuardarConjuntoAsync(ActorContext actor, long? id, ConjuntoReglasGuardarRequest r)
    {
        var e = ValidarCodigoNombre(r.Codigo, r.Nombre);
        if (e is null && !Politicas.Contains(r.PoliticaResolucion)) e = "La politica debe ser PrimeraCoincidencia o AcumularTodas.";
        if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", e);
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            if (id is null)
            {
                var ctx = await c.ExecuteScalarAsync<string?>("SELECT estado FROM rul.CONTEXTO WHERE id_contexto = @i", new { i = r.IdContexto }, tx);
                if (ctx is null) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El contexto no existe.");
                if (ctx != "Activo") return ResultadoOperacion.Fallo(409, "contexto_reservado", "Ese contexto todavia no esta habilitado.");
                var nuevo = await c.QuerySingleAsync<long>(
                    "INSERT INTO rul.CONJUNTO_REGLAS (id_compania, id_contexto, codigo, nombre, descripcion, politica_resolucion, creado_por) OUTPUT INSERTED.id_conjunto VALUES (@co, @ctx, @codigo, @nombre, @desc, @pol, @por)",
                    new { co = actor.IdCompania, ctx = r.IdContexto, codigo = r.Codigo.Trim(), nombre = r.Nombre.Trim(), desc = Nulo(r.Descripcion), pol = r.PoliticaResolucion, por = actor.NombreDescriptivo }, tx);
                await Auditoria.AltaAsync(c, tx, actor, EntidadConjunto, nuevo, r.Codigo.Trim());
                tx.Commit();
                return ResultadoOperacion.Ok(nuevo);
            }
            var previo = await c.QuerySingleOrDefaultAsync("SELECT * FROM rul.CONJUNTO_REGLAS WITH (UPDLOCK) WHERE id_conjunto = @id AND id_compania = @co", new { id, co = actor.IdCompania }, tx);
            if (previo is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el conjunto de reglas.");
            if ((long)previo.id_contexto != r.IdContexto) return ResultadoOperacion.Fallo(409, "contexto_fijo", "El contexto de un conjunto no se puede cambiar.");
            if ((string)previo.politica_resolucion != r.PoliticaResolucion)
            {
                var vig = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @id AND estado IN ('Revision', 'Programado', 'Vigente')", new { id }, tx);
                if (vig > 0) return ResultadoOperacion.Fallo(409, "politica_en_uso", "La politica no se puede cambiar mientras haya una version en revision, programada o vigente.");
            }
            await c.ExecuteAsync(
                "UPDATE rul.CONJUNTO_REGLAS SET codigo = @codigo, nombre = @nombre, descripcion = @desc, politica_resolucion = @pol, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_conjunto = @id",
                new { codigo = r.Codigo.Trim(), nombre = r.Nombre.Trim(), desc = Nulo(r.Descripcion), pol = r.PoliticaResolucion, por = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadConjunto, id.Value, "conjunto", $"{(string)previo.codigo} / {(string)previo.politica_resolucion}", $"{r.Codigo.Trim()} / {r.PoliticaResolucion}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "codigo_duplicado", "Ya existe un conjunto con ese codigo."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoConjuntoAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido.");
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var ant = await c.ExecuteScalarAsync<string?>("SELECT estado FROM rul.CONJUNTO_REGLAS WITH (UPDLOCK) WHERE id_conjunto = @id AND id_compania = @co", new { id, co = actor.IdCompania }, tx);
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el conjunto de reglas.");
        if (estado == "Inactivo")
        {
            var refs = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM rul.CONJUNTO_REFERENCIA WHERE id_conjunto = @id", new { id }, tx);
            if (refs > 0) return ResultadoOperacion.Fallo(409, "en_uso", $"Lo usan {refs} elemento(s) (por ejemplo productos comerciales); desvinculalo primero.");
            var edicion = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @id AND estado IN ('Revision', 'Programado')", new { id }, tx);
            if (edicion > 0) return ResultadoOperacion.Fallo(409, "version_en_curso", "Tiene una version en revision o programada.");
        }
        if (ant != estado)
        {
            await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS SET estado = @e, modificado_por = @p, modificado_en = SYSUTCDATETIME() WHERE id_conjunto = @id", new { e = estado, p = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadConjunto, id, "estado", ant, estado);
        }
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    public async Task<IReadOnlyList<ConjuntoReferenciaResponse>> ReferenciasAsync(ActorContext actor, long idConjunto)
    {
        using var c = NuevaConexion(); c.Open();
        var f = await c.QueryAsync(
            """
            SELECT r.tipo_consumidor, r.id_consumidor, k.codigo, k.nombre FROM rul.CONJUNTO_REFERENCIA r
            JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = r.id_conjunto
            LEFT JOIN prd.PRODUCTO_COMERCIAL k ON r.tipo_consumidor = 'PRODUCTO_COMERCIAL' AND k.id_producto_comercial = r.id_consumidor
            WHERE r.id_conjunto = @idConjunto AND j.id_compania = @co
            """, new { idConjunto, co = actor.IdCompania });
        return f.Select(x => new ConjuntoReferenciaResponse((string)x.tipo_consumidor, (long)x.id_consumidor,
            (string)x.tipo_consumidor == "PRODUCTO_COMERCIAL" ? $"Producto comercial {(string?)x.codigo} · {(string?)x.nombre} (elegibilidad)" : $"{(string)x.tipo_consumidor} {(long)x.id_consumidor}")).ToList();
    }

    /// <summary>Asigna (o quita) el conjunto de elegibilidad de un producto comercial. Contexto CLIENTE.</summary>
    public async Task<ResultadoOperacion> AsignarElegibilidadAsync(ActorContext actor, long idComercial, long? idConjunto)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var k = await c.QuerySingleOrDefaultAsync("SELECT id_conjunto_elegibilidad FROM prd.PRODUCTO_COMERCIAL WITH (UPDLOCK) WHERE id_producto_comercial = @i AND id_compania = @co", new { i = idComercial, co = actor.IdCompania }, tx);
        if (k is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el producto comercial.");
        if (idConjunto is not null)
        {
            var j = await c.QuerySingleOrDefaultAsync("SELECT j.estado, x.codigo AS ctx FROM rul.CONJUNTO_REGLAS j JOIN rul.CONTEXTO x ON x.id_contexto = j.id_contexto WHERE j.id_conjunto = @i AND j.id_compania = @co", new { i = idConjunto, co = actor.IdCompania }, tx);
            if (j is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el conjunto de reglas.");
            if ((string)j.estado != "Activo") return ResultadoOperacion.Fallo(409, "conjunto_inactivo", "El conjunto esta inactivo.");
            if ((string)j.ctx != "CLIENTE") return ResultadoOperacion.Fallo(409, "contexto_invalido", "La elegibilidad usa un conjunto del contexto CLIENTE.");
        }
        var ant = (long?)k.id_conjunto_elegibilidad;
        if (ant == idConjunto) { tx.Rollback(); return ResultadoOperacion.Ok(idComercial); }
        await c.ExecuteAsync("DELETE FROM rul.CONJUNTO_REFERENCIA WHERE tipo_consumidor = 'PRODUCTO_COMERCIAL' AND id_consumidor = @i", new { i = idComercial }, tx);
        if (idConjunto is not null)
            await c.ExecuteAsync("INSERT INTO rul.CONJUNTO_REFERENCIA (id_conjunto, tipo_consumidor, id_consumidor, creado_por) VALUES (@j, 'PRODUCTO_COMERCIAL', @i, @por)", new { j = idConjunto, i = idComercial, por = actor.NombreDescriptivo }, tx);
        await c.ExecuteAsync("UPDATE prd.PRODUCTO_COMERCIAL SET id_conjunto_elegibilidad = @j, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_producto_comercial = @i", new { j = idConjunto, i = idComercial, por = actor.NombreDescriptivo }, tx);
        await Auditoria.CambioAsync(c, tx, actor, ProductoRepositorio.EntidadComercial, idComercial, "id_conjunto_elegibilidad", ant?.ToString(CultureInfo.InvariantCulture), idConjunto?.ToString(CultureInfo.InvariantCulture));
        tx.Commit();
        return ResultadoOperacion.Ok(idComercial);
    }

    public async Task<long?> ElegibilidadDeProductoAsync(ActorContext actor, long idComercial)
    {
        using var c = NuevaConexion(); c.Open();
        return await c.ExecuteScalarAsync<long?>("SELECT id_conjunto_elegibilidad FROM prd.PRODUCTO_COMERCIAL WHERE id_producto_comercial = @i AND id_compania = @co", new { i = idComercial, co = actor.IdCompania });
    }

    // =============================================================== Versiones
    internal static async Task PromoverProgramadasAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long? idConjunto)
    {
        var hoy = DateTime.Now.Date;
        var due = (await c.QueryAsync(
            """
            SELECT v.id_version, v.id_conjunto, v.fecha_vigencia_desde FROM rul.CONJUNTO_REGLAS_VERSION v JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto
            WHERE j.id_compania = @idCompania AND v.estado = 'Programado' AND v.fecha_vigencia_desde <= @hoy AND (@idConjunto IS NULL OR v.id_conjunto = @idConjunto) ORDER BY v.fecha_vigencia_desde
            """, new { idCompania, hoy, idConjunto }, tx)).ToList();
        foreach (var d in due)
            await ActivarVersionAsync(c, tx, (long)d.id_conjunto, (long)d.id_version, DateOnly.FromDateTime((DateTime)d.fecha_vigencia_desde));
    }

    private static async Task ActivarVersionAsync(SqlConnection c, SqlTransaction? tx, long idConjunto, long idVersion, DateOnly desde)
    {
        var vigente = await c.QuerySingleOrDefaultAsync("SELECT id_version, fecha_vigencia_desde FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @i AND estado = 'Vigente'", new { i = idConjunto }, tx);
        if (vigente is not null)
        {
            var cierre = desde.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var ini = (DateTime?)vigente.fecha_vigencia_desde;
            if (ini is not null && cierre < ini) cierre = ini.Value;
            await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET estado = 'Expirado', fecha_vigencia_hasta = @h, modificado_en = SYSUTCDATETIME() WHERE id_version = @id", new { h = cierre, id = (long)vigente.id_version }, tx);
        }
        await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET estado = 'Vigente', fecha_vigencia_desde = @d, modificado_en = SYSUTCDATETIME() WHERE id_version = @id", new { d = desde.ToDateTime(TimeOnly.MinValue), id = idVersion }, tx);
    }

    private static VersionResponse MapVersion(dynamic v) => new((long)v.id_version, (int)v.numero_version, (string)v.estado,
        v.fecha_vigencia_desde is null ? null : DateOnly.FromDateTime((DateTime)v.fecha_vigencia_desde), v.fecha_vigencia_hasta is null ? null : DateOnly.FromDateTime((DateTime)v.fecha_vigencia_hasta),
        (string?)v.comentario, (long?)v.id_gestion, (string?)v.numero_gestion, (string?)v.estado_gestion, (string)v.creado_por, (DateTime)v.creado_en);

    public async Task<IReadOnlyList<VersionResponse>> ListarVersionesAsync(ActorContext actor, long idConjunto)
    {
        using var c = NuevaConexion(); c.Open();
        await PromoverProgramadasAsync(c, null, actor.IdCompania, idConjunto);
        var f = await c.QueryAsync(
            """
            SELECT v.*, g.numero AS numero_gestion, g.estado AS estado_gestion FROM rul.CONJUNTO_REGLAS_VERSION v JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto
            LEFT JOIN apr.GESTION g ON g.id_gestion = v.id_gestion WHERE v.id_conjunto = @idConjunto AND j.id_compania = @co ORDER BY v.numero_version DESC
            """, new { idConjunto, co = actor.IdCompania });
        return f.Select(MapVersion).Cast<VersionResponse>().ToList();
    }

    public async Task<ReglasVersionDetalleResponse?> ObtenerVersionAsync(ActorContext actor, long idVersion)
    {
        using var c = NuevaConexion(); c.Open();
        var v = await c.QuerySingleOrDefaultAsync(
            """
            SELECT v.*, j.id_conjunto AS idc, x.codigo AS ctx_codigo, g.numero AS numero_gestion, g.estado AS estado_gestion FROM rul.CONJUNTO_REGLAS_VERSION v
            JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto JOIN rul.CONTEXTO x ON x.id_contexto = j.id_contexto LEFT JOIN apr.GESTION g ON g.id_gestion = v.id_gestion
            WHERE v.id_version = @idVersion AND j.id_compania = @co
            """, new { idVersion, co = actor.IdCompania });
        if (v is null) return null;
        return new ReglasVersionDetalleResponse(MapVersion(v), (long)v.idc, (string)v.ctx_codigo, await LeerReglasAsync(c, null, idVersion));
    }

    // ----- lectura / escritura de reglas ------------------------------------------------------------
    private static async Task<List<ReglaDetalle>> LeerReglasAsync(SqlConnection c, SqlTransaction? tx, long idVersion)
    {
        var reglas = (await c.QueryAsync("SELECT * FROM rul.REGLA WHERE id_version = @v ORDER BY orden", new { v = idVersion }, tx)).ToList();
        var grupos = (await c.QueryAsync("SELECT g.* FROM rul.GRUPO_CONDICION g JOIN rul.REGLA r ON r.id_regla = g.id_regla WHERE r.id_version = @v ORDER BY g.orden, g.id_grupo", new { v = idVersion }, tx)).ToList();
        var miembros = (await c.QueryAsync(
            $"""
            SELECT m.id_grupo, m.negar, m.orden AS orden_m, {SqlCondicionColumnas} {SqlCondicionDe}
            JOIN rul.GRUPO_CONDICION_MIEMBRO m ON m.id_condicion = c.id_condicion JOIN rul.GRUPO_CONDICION g ON g.id_grupo = m.id_grupo JOIN rul.REGLA r ON r.id_regla = g.id_regla
            WHERE r.id_version = @v ORDER BY m.orden, m.id_miembro
            """, new { v = idVersion }, tx)).ToList();
        var acciones = (await c.QueryAsync(
            "SELECT ra.id_regla, ra.orden AS orden_a, a.* FROM rul.REGLA_ACCION ra JOIN rul.REGLA r ON r.id_regla = ra.id_regla JOIN rul.ACCION a ON a.id_accion = ra.id_accion WHERE r.id_version = @v ORDER BY ra.orden",
            new { v = idVersion }, tx)).ToList();

        ReglaGrupoDetalle Construir(dynamic g)
        {
            long idg = g.id_grupo;
            var ms = miembros.Where(m => (long)m.id_grupo == idg)
                .Select(m => new ReglaMiembroDetalle((long)m.id_condicion, (string)m.codigo, (string)m.nombre, (string)ResumenCondicion(m), (bool)m.negar, (string)m.estado)).ToList();
            var subs = grupos.Where(x => (long?)x.id_grupo_padre == idg).Select(x => (ReglaGrupoDetalle)Construir(x)).ToList();
            return new ReglaGrupoDetalle((string)g.operador_logico, ms, subs);
        }
        return reglas.Select<dynamic, ReglaDetalle>(r =>
        {
            long idr = r.id_regla;
            var raiz = grupos.FirstOrDefault(g => (long)g.id_regla == idr && g.id_grupo_padre is null);
            var acc = acciones.Where(a => (long)a.id_regla == idr)
                .Select(a => new ReglaAccionDetalle((long)a.id_accion, (string)a.codigo, (string)a.nombre, ResumenAccion((string)a.tipo_accion, (string?)a.resultado, (string?)a.clave, (string?)a.valor, (string?)a.mensaje), (string)a.estado)).ToList();
            ReglaGrupoDetalle? arbol = raiz is null ? null : (ReglaGrupoDetalle)Construir(raiz);
            return new ReglaDetalle(idr, (int)r.orden, (string)r.codigo, (string)r.nombre, (string?)r.descripcion, (bool)r.activa, arbol, acc);
        }).ToList();
    }

    private static ReglaGrupoGuardar? AGuardar(ReglaGrupoDetalle? g) => g is null ? null
        : new ReglaGrupoGuardar(g.Operador, g.Miembros.Select(m => new ReglaMiembroGuardar(m.IdCondicion, m.Negar)).ToList(), g.Subgrupos.Select(s => AGuardar(s)!).ToList());

    private static async Task InsertarGrupoAsync(SqlConnection c, SqlTransaction tx, long idRegla, long? padre, ReglaGrupoGuardar g, int orden)
    {
        var id = await c.QuerySingleAsync<long>("INSERT INTO rul.GRUPO_CONDICION (id_regla, id_grupo_padre, operador_logico, orden) OUTPUT INSERTED.id_grupo VALUES (@r, @p, @o, @ord)",
            new { r = idRegla, p = padre, o = g.Operador, ord = orden }, tx);
        var i = 1;
        foreach (var m in g.Miembros ?? [])
            await c.ExecuteAsync("INSERT INTO rul.GRUPO_CONDICION_MIEMBRO (id_grupo, id_condicion, negar, orden) VALUES (@g, @c, @n, @o)", new { g = id, c = m.IdCondicion, n = m.Negar, o = i++ }, tx);
        i = 1;
        foreach (var s in g.Subgrupos ?? []) await InsertarGrupoAsync(c, tx, idRegla, id, s, i++);
    }

    private static async Task InsertarReglasAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idVersion, IReadOnlyList<ReglaGuardar> reglas)
    {
        var orden = 1;
        foreach (var r in reglas)
        {
            var idRegla = await c.QuerySingleAsync<long>(
                "INSERT INTO rul.REGLA (id_version, orden, codigo, nombre, descripcion, activa, creado_por) OUTPUT INSERTED.id_regla VALUES (@v, @o, @codigo, @nombre, @desc, @act, @por)",
                new { v = idVersion, o = orden++, codigo = r.Codigo.Trim(), nombre = r.Nombre.Trim(), desc = Nulo(r.Descripcion), act = r.Activa, por = actor.NombreDescriptivo }, tx);
            if (r.Condiciones is not null) await InsertarGrupoAsync(c, tx, idRegla, null, r.Condiciones, 1);
            var j = 1;
            foreach (var a in r.Acciones ?? [])
                await c.ExecuteAsync("INSERT INTO rul.REGLA_ACCION (id_regla, id_accion, orden) VALUES (@r, @a, @o)", new { r = idRegla, a, o = j++ }, tx);
        }
    }

    private static async Task BorrarReglasAsync(SqlConnection c, SqlTransaction tx, long idVersion)
    {
        await c.ExecuteAsync(
            """
            DELETE m FROM rul.GRUPO_CONDICION_MIEMBRO m JOIN rul.GRUPO_CONDICION g ON g.id_grupo = m.id_grupo JOIN rul.REGLA r ON r.id_regla = g.id_regla WHERE r.id_version = @v;
            DELETE g FROM rul.GRUPO_CONDICION g JOIN rul.REGLA r ON r.id_regla = g.id_regla WHERE r.id_version = @v;
            DELETE ra FROM rul.REGLA_ACCION ra JOIN rul.REGLA r ON r.id_regla = ra.id_regla WHERE r.id_version = @v;
            DELETE FROM rul.REGLA WHERE id_version = @v;
            """, new { v = idVersion }, tx);
    }

    private static int ContarCond(ReglaGrupoGuardar? g) => g is null ? 0 : (g.Miembros?.Count ?? 0) + (g.Subgrupos?.Sum(ContarCond) ?? 0);

    /// <summary>Con primera coincidencia, una regla activa sin condiciones (la de por defecto) debe quedar al final.</summary>
    private static string? ValidarReglaPorDefecto(IReadOnlyList<(string Codigo, bool Activa, int Condiciones)> reglas, string politica)
    {
        if (politica != "PrimeraCoincidencia") return null;
        var activas = reglas.Where(r => r.Activa).ToList();
        for (var i = 0; i < activas.Count - 1; i++)
            if (activas[i].Condiciones == 0) return $"La regla '{activas[i].Codigo}' no tiene condiciones (es la regla por defecto) y debe quedar al final.";
        return null;
    }

    private static string? ValidarGrupo(ReglaGrupoGuardar g, string regla, HashSet<long> ids)
    {
        if (g.Operador is not ("Y" or "O")) return $"Regla '{regla}': el operador de un grupo debe ser Y u O.";
        var propios = new HashSet<long>();
        foreach (var m in g.Miembros ?? [])
        {
            if (!propios.Add(m.IdCondicion)) return $"Regla '{regla}': una condicion esta repetida dentro del mismo grupo.";
            ids.Add(m.IdCondicion);
        }
        foreach (var s in g.Subgrupos ?? [])
        {
            var e = ValidarGrupo(s, regla, ids);
            if (e is not null) return e;
        }
        return null;
    }

    public async Task<ResultadoOperacion> GuardarReglasAsync(ActorContext actor, long idVersion, ReglasGuardarRequest req)
    {
        var reglas = req.Reglas ?? [];
        var limite = ReglasEvaluador.ValidarLimites(reglas);
        if (limite is not null) return ResultadoOperacion.Fallo(400, "limite_complejidad", limite);
        var codigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var condIds = new HashSet<long>(); var accIds = new HashSet<long>();
        foreach (var r in reglas)
        {
            var e = ValidarCodigoNombre(r.Codigo, r.Nombre);
            if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", $"Regla '{r.Codigo}': {e}");
            if (!codigos.Add(r.Codigo.Trim())) return ResultadoOperacion.Fallo(400, "datos_invalidos", $"El codigo de regla '{r.Codigo.Trim()}' esta repetido.");
            if (r.Condiciones is not null) { e = ValidarGrupo(r.Condiciones, r.Codigo, condIds); if (e is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", e); }
            var propias = new HashSet<long>();
            foreach (var a in r.Acciones ?? [])
            {
                if (!propias.Add(a)) return ResultadoOperacion.Fallo(400, "datos_invalidos", $"Regla '{r.Codigo}': una accion esta repetida.");
                accIds.Add(a);
            }
        }
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var v = await c.QuerySingleOrDefaultAsync(
                """
                SELECT v.estado, v.numero_version, j.id_contexto, j.politica_resolucion FROM rul.CONJUNTO_REGLAS_VERSION v WITH (UPDLOCK)
                JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto WHERE v.id_version = @idVersion AND j.id_compania = @co
                """, new { idVersion, co = actor.IdCompania }, tx);
            if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
            if ((string)v.estado != "Borrador") return ResultadoOperacion.Fallo(409, "version_no_editable", "Solo una version en Borrador se puede editar.");

            if (condIds.Count > 0)
            {
                var validas = (await c.QueryAsync<long>(
                    """
                    SELECT c.id_condicion FROM rul.CONDICION c
                    WHERE c.id_condicion IN @ids AND c.id_compania = @co AND c.estado = 'Activo'
                      AND EXISTS (SELECT 1 FROM rul.CONTEXTO_ATRIBUTO ca WHERE ca.id_atributo = c.id_atributo AND ca.id_contexto = @ctx)
                      AND (c.id_atributo_valor IS NULL OR EXISTS (SELECT 1 FROM rul.CONTEXTO_ATRIBUTO ca WHERE ca.id_atributo = c.id_atributo_valor AND ca.id_contexto = @ctx))
                    """, new { ids = condIds.ToArray(), co = actor.IdCompania, ctx = (long)v.id_contexto }, tx)).ToHashSet();
                var mala = condIds.FirstOrDefault(i => !validas.Contains(i));
                if (mala != 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", $"La condicion {mala} no existe, esta inactiva o usa atributos que no pertenecen al contexto del conjunto.");
            }
            if (accIds.Count > 0)
            {
                var validas = (await c.QueryAsync<long>("SELECT id_accion FROM rul.ACCION WHERE id_accion IN @ids AND id_compania = @co AND estado = 'Activo'", new { ids = accIds.ToArray(), co = actor.IdCompania }, tx)).ToHashSet();
                var mala = accIds.FirstOrDefault(i => !validas.Contains(i));
                if (mala != 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", $"La accion {mala} no existe o esta inactiva.");
            }
            var defecto = ValidarReglaPorDefecto(reglas.Select(r => (r.Codigo.Trim(), r.Activa, ContarCond(r.Condiciones))).ToList(), (string)v.politica_resolucion);
            if (defecto is not null) return ResultadoOperacion.Fallo(400, "regla_por_defecto", defecto);

            var antes = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM rul.REGLA WHERE id_version = @idVersion", new { idVersion }, tx);
            await BorrarReglasAsync(c, tx, idVersion);
            await InsertarReglasAsync(c, tx, actor, idVersion, reglas);
            if (req.Comentario is not null)
                await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET comentario = @c, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion", new { c = Nulo(req.Comentario), por = actor.NombreDescriptivo, idVersion }, tx);
            else
                await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion", new { por = actor.NombreDescriptivo, idVersion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "reglas", $"{antes} regla(s)", $"{reglas.Count} regla(s)");
            tx.Commit();
            return ResultadoOperacion.Ok(idVersion);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Siguiente borrador: copia profunda de la ultima version (si existe).</summary>
    public async Task<ResultadoOperacion> CrearBorradorAsync(ActorContext actor, long idConjunto)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var k = await c.QuerySingleOrDefaultAsync("SELECT estado FROM rul.CONJUNTO_REGLAS WITH (UPDLOCK) WHERE id_conjunto = @i AND id_compania = @co", new { i = idConjunto, co = actor.IdCompania }, tx);
            if (k is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el conjunto de reglas.");
            if ((string)k.estado != "Activo") return ResultadoOperacion.Fallo(409, "conjunto_inactivo", "El conjunto de reglas esta inactivo.");
            var enEdicion = await c.ExecuteScalarAsync<int?>("SELECT TOP 1 numero_version FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @i AND estado IN ('Borrador', 'Revision')", new { i = idConjunto }, tx);
            if (enEdicion is not null) return ResultadoOperacion.Fallo(409, "version_en_edicion", $"Ya hay una version en edicion (v{enEdicion}).");
            var ultima = await c.QuerySingleOrDefaultAsync("SELECT TOP 1 id_version, numero_version FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @i ORDER BY numero_version DESC", new { i = idConjunto }, tx);
            var numero = ultima is null ? 1 : (int)ultima.numero_version + 1;
            var id = await c.QuerySingleAsync<long>("INSERT INTO rul.CONJUNTO_REGLAS_VERSION (id_conjunto, numero_version, creado_por) OUTPUT INSERTED.id_version VALUES (@i, @n, @por)",
                new { i = idConjunto, n = numero, por = actor.NombreDescriptivo }, tx);
            if (ultima is not null)
            {
                var previas = await LeerReglasAsync(c, tx, (long)ultima.id_version);
                var copia = previas.Select(r => new ReglaGuardar(r.Codigo, r.Nombre, r.Descripcion, r.Activa, AGuardar(r.Condiciones), r.Acciones.Select(a => a.IdAccion).ToList())).ToList();
                await InsertarReglasAsync(c, tx, actor, id, copia);
            }
            await Auditoria.AltaAsync(c, tx, actor, EntidadVersion, id, $"v{numero}{(ultima is null ? "" : $" (copia de v{(int)ultima.numero_version})")}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "version_en_edicion", "Ya hay una version en edicion."); }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EliminarBorradorAsync(ActorContext actor, long idVersion)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var v = await c.QuerySingleOrDefaultAsync(
            "SELECT v.estado, v.numero_version FROM rul.CONJUNTO_REGLAS_VERSION v WITH (UPDLOCK) JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto WHERE v.id_version = @idVersion AND j.id_compania = @co",
            new { idVersion, co = actor.IdCompania }, tx);
        if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
        if ((string)v.estado != "Borrador") return ResultadoOperacion.Fallo(409, "version_no_editable", "Solo se descarta una version en Borrador.");
        var evals = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM rul.EVALUACION WHERE id_version = @idVersion", new { idVersion }, tx);
        if (evals > 0) return ResultadoOperacion.Fallo(409, "en_uso", "La version tiene evaluaciones registradas.");
        await BorrarReglasAsync(c, tx, idVersion);
        await c.ExecuteAsync("DELETE FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_version = @idVersion", new { idVersion }, tx);
        await Auditoria.BajaAsync(c, tx, actor, EntidadVersion, idVersion, $"v{(int)v.numero_version} (borrador descartado)");
        tx.Commit();
        return ResultadoOperacion.Ok(idVersion);
    }

    public async Task<ResultadoOperacion> PublicarAsync(ActorContext actor, long idVersion, VersionPublicarRequest req)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        try
        {
            var v = await c.QuerySingleOrDefaultAsync(
                """
                SELECT v.*, j.codigo AS conjunto, j.estado AS estado_conjunto, j.politica_resolucion FROM rul.CONJUNTO_REGLAS_VERSION v WITH (UPDLOCK)
                JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto WHERE v.id_version = @idVersion AND j.id_compania = @co
                """, new { idVersion, co = actor.IdCompania }, tx);
            if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
            if ((string)v.estado != "Borrador") return ResultadoOperacion.Fallo(409, "version_no_editable", "Solo se publica una version en Borrador.");
            if ((string)v.estado_conjunto != "Activo") return ResultadoOperacion.Fallo(409, "conjunto_inactivo", "El conjunto de reglas esta inactivo.");
            var hoy = DateOnly.FromDateTime(DateTime.Now);
            if (req.FechaVigenciaDesde < hoy) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La vigencia no puede ser anterior a hoy.");
            var idConjunto = (long)v.id_conjunto;
            var vig = await c.ExecuteScalarAsync<DateTime?>("SELECT fecha_vigencia_desde FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @i AND estado = 'Vigente'", new { i = idConjunto }, tx);
            if (vig is not null && req.FechaVigenciaDesde.ToDateTime(TimeOnly.MinValue) <= vig.Value)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", $"La vigencia debe ser posterior a la de la version vigente ({vig:yyyy-MM-dd}).");
            if (await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM rul.CONJUNTO_REGLAS_VERSION WHERE id_conjunto = @i AND estado IN ('Programado', 'Revision')) THEN 1 ELSE 0 END", new { i = idConjunto }, tx))
                return ResultadoOperacion.Fallo(409, "version_programada", "Ya hay una version programada o en revision para este conjunto.");

            var reglas = await LeerReglasAsync(c, tx, idVersion);
            var err = ValidarCompletitud(reglas, (string)v.politica_resolucion);
            if (err is not null) return ResultadoOperacion.Fallo(400, "version_incompleta", err);

            await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET estado = 'Revision', fecha_vigencia_desde = @d, comentario = COALESCE(@com, comentario), modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion",
                new { d = req.FechaVigenciaDesde.ToDateTime(TimeOnly.MinValue), com = Nulo(req.Comentario), por = actor.NombreDescriptivo, idVersion }, tx);
            var r = await GestionSolicitudes.CrearAsync(c, tx, _tenant, _numeracion, actor, TipoGestionVersion, EntidadVersion, idVersion.ToString(CultureInfo.InvariantCulture), req.Comentario,
                [("id_version", null, idVersion.ToString(CultureInfo.InvariantCulture)), ("conjunto", null, (string)v.conjunto), ("numero_version", null, ((int)v.numero_version).ToString(CultureInfo.InvariantCulture)),
                 ("fecha_vigencia_desde", null, req.FechaVigenciaDesde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
            if (!r.Exito) { tx.Rollback(); return r; }
            await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET id_gestion = @g WHERE id_version = @idVersion", new { g = r.Id, idVersion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Borrador", "Revision", r.Id);
            tx.Commit();
            return r;
        }
        catch (SqlException ex) when (Duplicado(ex)) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "version_programada", "Ya hay una version en revision o programada."); }
        catch { tx.Rollback(); throw; }
    }

    private static string? ValidarCompletitud(IReadOnlyList<ReglaDetalle> reglas, string politica)
    {
        var activas = reglas.Where(r => r.Activa).ToList();
        if (activas.Count == 0) return "La version necesita al menos una regla activa.";
        var errores = new List<string>();
        foreach (var r in reglas)
        {
            if (r.Acciones.Count == 0) errores.Add($"La regla '{r.Codigo}' no tiene acciones.");
            foreach (var a in r.Acciones.Where(a => a.Estado != "Activo")) errores.Add($"La accion '{a.Codigo}' (regla '{r.Codigo}') esta inactiva.");
            foreach (var m in Miembros(r.Condiciones).Where(m => m.Estado != "Activo")) errores.Add($"La condicion '{m.Codigo}' (regla '{r.Codigo}') esta inactiva.");
        }
        var defecto = ValidarReglaPorDefecto(reglas.Select(r => (r.Codigo, r.Activa, Miembros(r.Condiciones).Count())).ToList(), politica);
        if (defecto is not null) errores.Add(defecto);
        return errores.Count == 0 ? null : string.Join(" ", errores);
    }

    private static IEnumerable<ReglaMiembroDetalle> Miembros(ReglaGrupoDetalle? g) =>
        g is null ? [] : g.Miembros.Concat(g.Subgrupos.SelectMany(s => Miembros(s)));

    /// <summary>Devuelve a Borrador una version cuya gestion fue rechazada o cancelada (no pendiente).</summary>
    public async Task<ResultadoOperacion> ReabrirAsync(ActorContext actor, long idVersion)
    {
        using var c = NuevaConexion(); c.Open(); using var tx = c.BeginTransaction();
        var v = await c.QuerySingleOrDefaultAsync(
            """
            SELECT v.estado, g.estado AS estado_gestion FROM rul.CONJUNTO_REGLAS_VERSION v WITH (UPDLOCK) JOIN rul.CONJUNTO_REGLAS j ON j.id_conjunto = v.id_conjunto
            LEFT JOIN apr.GESTION g ON g.id_gestion = v.id_gestion WHERE v.id_version = @idVersion AND j.id_compania = @co
            """, new { idVersion, co = actor.IdCompania }, tx);
        if (v is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe la version.");
        if ((string)v.estado != "Revision") return ResultadoOperacion.Fallo(409, "version_no_editable", "La version no esta en revision.");
        if ((string?)v.estado_gestion == GestionRepositorio.EstadoPendiente) return ResultadoOperacion.Fallo(409, "gestion_pendiente", "La gestion sigue pendiente de aprobacion.");
        await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET estado = 'Borrador', modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion", new { por = actor.NombreDescriptivo, idVersion }, tx);
        await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Revision", "Borrador");
        tx.Commit();
        return ResultadoOperacion.Ok(idVersion);
    }

    /// <summary>Aplicador de gestiones RUL_VERSION_CONJUNTO (al aprobarse).</summary>
    public static async Task<ResultadoOperacion?> AplicarPublicacionAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion)
    {
        var det = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx)).ToDictionary(d => (string)d.nombre_campo, d => (string?)d.valor_nuevo);
        if (!det.TryGetValue("id_version", out var sv) || !det.TryGetValue("fecha_vigencia_desde", out var sf)) return ResultadoOperacion.Fallo(500, "gestion_incompleta", "La gestion no tiene los campos de la version.");
        var idVersion = long.Parse(sv!, CultureInfo.InvariantCulture);
        var desde = DateOnly.ParseExact(sf!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var v = await c.QuerySingleOrDefaultAsync("SELECT estado, id_conjunto FROM rul.CONJUNTO_REGLAS_VERSION WITH (UPDLOCK) WHERE id_version = @idVersion", new { idVersion }, tx);
        if (v is null || (string)v.estado != "Revision") return ResultadoOperacion.Fallo(409, "version_no_editable", "La version ya no esta en revision; rechace la gestion.");
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        var efectiva = desde < hoy ? hoy : desde;
        var idConjunto = (long)v.id_conjunto;
        if (efectiva <= hoy)
        {
            await ActivarVersionAsync(c, tx, idConjunto, idVersion, efectiva);
            await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Revision", "Vigente", idGestion);
        }
        else
        {
            await c.ExecuteAsync("UPDATE rul.CONJUNTO_REGLAS_VERSION SET estado = 'Programado', fecha_vigencia_desde = @d, modificado_en = SYSUTCDATETIME() WHERE id_version = @idVersion", new { d = efectiva.ToDateTime(TimeOnly.MinValue), idVersion }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadVersion, idVersion, "estado", "Revision", "Programado", idGestion);
        }
        return null;
    }
}
