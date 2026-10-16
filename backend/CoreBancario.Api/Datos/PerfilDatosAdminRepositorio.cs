using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Administracion de mascaras, perfiles de datos y su asignacion a usuarios (permiso SEG_PERFILES). Es configuracion de administracion:
/// se guarda directo y todo cambio queda en la auditoria (entidades MASCARA, PERFIL_DATOS y USUARIO_PERFIL_DATOS). Nada se elimina: se inactiva.
/// </summary>
public class PerfilDatosAdminRepositorio
{
    public const string Permiso = "SEG_PERFILES";
    public const string EntidadMascara = "MASCARA";
    public const string EntidadPerfil = "PERFIL_DATOS";
    public const string EntidadUsuario = "USUARIO_PERFIL_DATOS";

    private static readonly string[] TiposMascara = ["PARCIAL", "CORREO", "RANGO", "ANIO", "OCULTO"];
    private readonly TenantContext _tenant;
    public PerfilDatosAdminRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    // ------------------------------------------------------------------ Mascaras

    private static string Ejemplo(MascaraDef m) => m.Tipo switch
    {
        "CORREO" => $"juan.perez@correo.com → {m.Aplicar("juan.perez@correo.com")}",
        "RANGO" => $"25300 → {m.Aplicar(25300m)}",
        "ANIO" => $"14/03/1985 → {m.Aplicar(new DateOnly(1985, 3, 14))}",
        "OCULTO" => $"401255484 → {m.Aplicar("401255484")}",
        _ => $"4012-5588-9901 → {m.Aplicar("4012-5588-9901")}",
    };

    public async Task<IReadOnlyList<MascaraResponse>> ListarMascarasAsync(long idCompania, bool soloActivas = false)
    {
        using var c = NuevaConexion();
        return await ListarMascarasAsync(c, null, idCompania, soloActivas);
    }

    private static async Task<IReadOnlyList<MascaraResponse>> ListarMascarasAsync(SqlConnection c, SqlTransaction? tx, long idCompania, bool soloActivas)
    {
        var filas = await c.QueryAsync(
            """
            SELECT m.id_mascara, m.codigo, m.nombre, m.descripcion, m.tipo, m.visibles_inicio, m.visibles_fin, m.caracter, m.es_sistema, m.estado,
                   (SELECT COUNT(DISTINCT pc.id_perfil_datos) FROM sec.PERFIL_DATOS_CAMPO pc WHERE pc.id_mascara = m.id_mascara) AS perfiles
            FROM sec.MASCARA m WHERE m.id_compania = @idCompania AND (@soloActivas = 0 OR m.estado = 'Activo')
            ORDER BY m.es_sistema DESC, m.nombre
            """, new { idCompania, soloActivas = soloActivas ? 1 : 0 }, tx);
        return filas.Select(f =>
        {
            var def = new MascaraDef((long)f.id_mascara, (string)f.codigo, (string)f.nombre, (string)f.tipo, (int)f.visibles_inicio, (int)f.visibles_fin, ((string)f.caracter)[0]);
            return new MascaraResponse(def.Id, def.Codigo, def.Nombre, (string?)f.descripcion, def.Tipo, def.Inicio, def.Fin, def.Caracter.ToString(),
                (bool)f.es_sistema, (string)f.estado, Ejemplo(def), (int)f.perfiles);
        }).ToList();
    }

    private static string? ValidarMascara(MascaraGuardarRequest r, bool validarCodigo)
    {
        if (validarCodigo && !Regex.IsMatch((r.Codigo ?? "").Trim().ToUpperInvariant(), "^[A-Z0-9_]{2,50}$"))
            return "El código debe tener entre 2 y 50 letras, números o guion bajo.";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 100) return "El nombre es obligatorio (máximo 100 caracteres).";
        if (r.Descripcion is { Length: > 300 }) return "La descripción admite hasta 300 caracteres.";
        if (!TiposMascara.Contains(r.Tipo)) return "El tipo de máscara no es válido.";
        if (r.VisiblesInicio is < 0 or > 20 || r.VisiblesFin is < 0 or > 20) return "Los caracteres visibles deben estar entre 0 y 20.";
        if (!string.IsNullOrEmpty(r.Caracter) && r.Caracter.Length != 1) return "El carácter de relleno debe ser uno solo.";
        return null;
    }

    public async Task<ResultadoOperacion> CrearMascaraAsync(ActorContext actor, MascaraGuardarRequest r)
    {
        var error = ValidarMascara(r, true);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        var codigo = r.Codigo.Trim().ToUpperInvariant();
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO sec.MASCARA (id_compania, codigo, nombre, descripcion, tipo, visibles_inicio, visibles_fin, caracter, creado_por)
                OUTPUT INSERTED.id_mascara
                VALUES (@c, @codigo, @nombre, @desc, @tipo, @ini, @fin, @car, @por)
                """, new
                {
                    c = actor.IdCompania, codigo, nombre = r.Nombre.Trim(), desc = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim(),
                    tipo = r.Tipo, ini = r.Tipo == "PARCIAL" ? r.VisiblesInicio : 0, fin = r.Tipo == "PARCIAL" ? r.VisiblesFin : 0,
                    car = string.IsNullOrEmpty(r.Caracter) ? "*" : r.Caracter, por = actor.NombreDescriptivo
                }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadMascara, id, $"{codigo} · {r.Nombre.Trim()}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "mascara_duplicada", $"Ya existe la máscara {codigo}.");
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EditarMascaraAsync(ActorContext actor, long id, MascaraGuardarRequest r)
    {
        var error = ValidarMascara(r, false);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var m = await c.QuerySingleOrDefaultAsync(
                "SELECT nombre, descripcion, tipo, visibles_inicio, visibles_fin, caracter, es_sistema FROM sec.MASCARA WITH (UPDLOCK) WHERE id_mascara = @id AND id_compania = @c",
                new { id, c = actor.IdCompania }, tx);
            if (m is null) return ResultadoOperacion.Fallo(404, "no_encontrada", "No existe la máscara.");
            if ((bool)m.es_sistema) return ResultadoOperacion.Fallo(409, "mascara_sistema", "Las máscaras del sistema no se editan; cree una nueva con los valores que necesite.");
            if ((string)m.tipo != r.Tipo)
            {
                var enUso = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.PERFIL_DATOS_CAMPO WHERE id_mascara = @id) THEN 1 ELSE 0 END", new { id }, tx);
                if (enUso) return ResultadoOperacion.Fallo(409, "mascara_en_uso", "La máscara ya la usa algún perfil; no se puede cambiar su tipo.");
            }
            var nombre = r.Nombre.Trim();
            var desc = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim();
            var ini = r.Tipo == "PARCIAL" ? r.VisiblesInicio : 0;
            var fin = r.Tipo == "PARCIAL" ? r.VisiblesFin : 0;
            var car = string.IsNullOrEmpty(r.Caracter) ? "*" : r.Caracter;
            await c.ExecuteAsync(
                "UPDATE sec.MASCARA SET nombre = @nombre, descripcion = @desc, tipo = @tipo, visibles_inicio = @ini, visibles_fin = @fin, caracter = @car, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_mascara = @id",
                new { nombre, desc, tipo = r.Tipo, ini, fin, car, por = actor.NombreDescriptivo, id }, tx);
            if ((string)m.nombre != nombre) await Auditoria.CambioAsync(c, tx, actor, EntidadMascara, id, "Nombre", (string)m.nombre, nombre);
            if ((string?)m.descripcion != desc) await Auditoria.CambioAsync(c, tx, actor, EntidadMascara, id, "Descripción", (string?)m.descripcion, desc);
            if ((string)m.tipo != r.Tipo) await Auditoria.CambioAsync(c, tx, actor, EntidadMascara, id, "Tipo", (string)m.tipo, r.Tipo);
            if ((int)m.visibles_inicio != ini) await Auditoria.CambioAsync(c, tx, actor, EntidadMascara, id, "Visibles al inicio", ((int)m.visibles_inicio).ToString(), ini.ToString());
            if ((int)m.visibles_fin != fin) await Auditoria.CambioAsync(c, tx, actor, EntidadMascara, id, "Visibles al final", ((int)m.visibles_fin).ToString(), fin.ToString());
            if ((string)m.caracter != car) await Auditoria.CambioAsync(c, tx, actor, EntidadMascara, id, "Carácter de relleno", (string)m.caracter, car);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoMascaraAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado inválido.");
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var m = await c.QuerySingleOrDefaultAsync("SELECT estado, es_sistema FROM sec.MASCARA WITH (UPDLOCK) WHERE id_mascara = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
            if (m is null) return ResultadoOperacion.Fallo(404, "no_encontrada", "No existe la máscara.");
            if ((string)m.estado == estado) return ResultadoOperacion.Ok(id);
            if (estado == "Inactivo")
            {
                if ((bool)m.es_sistema) return ResultadoOperacion.Fallo(409, "mascara_sistema", "Las máscaras del sistema no se inactivan.");
                var enUso = await c.ExecuteScalarAsync<bool>(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.PERFIL_DATOS_CAMPO pc JOIN sec.PERFIL_DATOS p ON p.id_perfil_datos = pc.id_perfil_datos WHERE pc.id_mascara = @id AND p.estado = 'Activo') THEN 1 ELSE 0 END", new { id }, tx);
                if (enUso) return ResultadoOperacion.Fallo(409, "mascara_en_uso", "La máscara la usa algún perfil activo; cámbiela en esos perfiles primero.");
            }
            await c.ExecuteAsync("UPDATE sec.MASCARA SET estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_mascara = @id", new { e = estado, por = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadMascara, id, "Estado", (string)m.estado, estado);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    // ------------------------------------------------------------------ Catalogo

    public async Task<PerfilCatalogoResponse> CatalogoAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var unidades = (await c.QueryAsync("SELECT id_departamento AS id, nombre FROM sec.DEPARTAMENTO WHERE id_compania = @idCompania AND estado = 'Activo' ORDER BY nombre", new { idCompania }))
            .Select(u => new OpcionValorResponse(((long)u.id).ToString(), (string)u.nombre)).ToList();
        static IReadOnlyList<OpcionValorResponse> Op(IEnumerable<(string Valor, string Nombre)> x) => x.Select(v => new OpcionValorResponse(v.Valor, v.Nombre)).ToList();
        var atributos = PerfilDatosCatalogo.Atributos.Select(a => new CatalogoAtributoResponse(a.Codigo, a.Etiqueta, a.Codigo switch
        {
            "TIPO_PERSONA" => Op(PerfilDatosCatalogo.TiposPersona),
            "ESTADO" => Op(PerfilDatosCatalogo.Estados),
            "UNIDAD" => unidades,
            _ => Op(PerfilDatosCatalogo.SiNo),
        })).ToList();
        var campos = PerfilDatosCatalogo.Campos.Select(f => new CatalogoCampoResponse(f.Codigo, f.Etiqueta, f.Descripcion, f.Tipo.ToString(), PerfilDatosCatalogo.TiposMascaraCompatibles(f.Tipo))).ToList();
        return new PerfilCatalogoResponse(atributos, campos, await ListarMascarasAsync(c, null, idCompania, true));
    }

    // ------------------------------------------------------------------ Perfiles

    private static string ResumenFilas(IEnumerable<PerfilFiltroDto> filtros, IReadOnlyDictionary<string, string> nombresUnidad)
    {
        var partes = new List<string>();
        foreach (var f in filtros)
        {
            var atr = PerfilDatosCatalogo.BuscarAtributo(f.Atributo);
            if (atr is null) continue;
            var nombres = f.Valores.Select(v => f.Atributo switch
            {
                "TIPO_PERSONA" => PerfilDatosCatalogo.TiposPersona.FirstOrDefault(x => x.Valor == v).Nombre ?? v,
                "ESTADO" => v,
                "UNIDAD" => nombresUnidad.GetValueOrDefault(v, v),
                _ => v == "1" ? "Sí" : "No",
            });
            partes.Add($"{atr.Etiqueta} {(f.Modo == "EXCLUIR" ? "excepto" : "solo")}: {string.Join(", ", nombres)}");
        }
        return partes.Count == 0 ? "Todos los clientes" : string.Join(" · ", partes);
    }

    private static async Task<Dictionary<string, string>> NombresUnidadAsync(SqlConnection c, SqlTransaction? tx, long idCompania)
        => (await c.QueryAsync("SELECT id_departamento AS id, nombre FROM sec.DEPARTAMENTO WHERE id_compania = @idCompania", new { idCompania }, tx))
            .ToDictionary(x => ((long)x.id).ToString(), x => (string)x.nombre);

    private static async Task<List<PerfilFiltroDto>> LeerFiltrosAsync(SqlConnection c, SqlTransaction? tx, long idPerfil)
        => (await c.QueryAsync("SELECT atributo, modo, valor FROM sec.PERFIL_DATOS_FILTRO WHERE id_perfil_datos = @idPerfil ORDER BY atributo, valor", new { idPerfil }, tx))
            .GroupBy(x => (string)x.atributo)
            .Select(g => new PerfilFiltroDto(g.Key, (string)g.First().modo, g.Select(x => (string)x.valor).ToList())).ToList();

    public async Task<IReadOnlyList<PerfilDatosResumenResponse>> ListarPerfilesAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var nombresUnidad = await NombresUnidadAsync(c, null, idCompania);
        var perfiles = (await c.QueryAsync(
            """
            SELECT p.id_perfil_datos, p.codigo, p.nombre, p.descripcion, p.estado,
                   (SELECT COUNT(*) FROM sec.PERFIL_DATOS_CAMPO x WHERE x.id_perfil_datos = p.id_perfil_datos) AS campos,
                   (SELECT COUNT(*) FROM sec.USUARIO_PERFIL_DATOS x WHERE x.id_perfil_datos = p.id_perfil_datos) AS usuarios
            FROM sec.PERFIL_DATOS p WHERE p.id_compania = @idCompania ORDER BY p.nombre
            """, new { idCompania })).ToList();
        var filtros = (await c.QueryAsync(
            "SELECT f.id_perfil_datos AS p, f.atributo, f.modo, f.valor FROM sec.PERFIL_DATOS_FILTRO f JOIN sec.PERFIL_DATOS d ON d.id_perfil_datos = f.id_perfil_datos WHERE d.id_compania = @idCompania",
            new { idCompania })).ToList();
        return perfiles.Select(p =>
        {
            var fs = filtros.Where(f => (long)f.p == (long)p.id_perfil_datos).GroupBy(f => (string)f.atributo)
                .Select(g => new PerfilFiltroDto(g.Key, (string)g.First().modo, g.Select(x => (string)x.valor).ToList()));
            return new PerfilDatosResumenResponse((long)p.id_perfil_datos, (string)p.codigo, (string)p.nombre, (string?)p.descripcion, (string)p.estado,
                ResumenFilas(fs, nombresUnidad), (int)p.campos, (int)p.usuarios);
        }).ToList();
    }

    public async Task<PerfilDatosResponse?> ObtenerPerfilAsync(long idCompania, long id)
    {
        using var c = NuevaConexion();
        var p = await c.QuerySingleOrDefaultAsync("SELECT id_perfil_datos, codigo, nombre, descripcion, estado FROM sec.PERFIL_DATOS WHERE id_perfil_datos = @id AND id_compania = @idCompania", new { id, idCompania });
        if (p is null) return null;
        var campos = (await c.QueryAsync("SELECT campo, id_mascara, permite_revelar FROM sec.PERFIL_DATOS_CAMPO WHERE id_perfil_datos = @id ORDER BY campo", new { id }))
            .Select(x => new PerfilCampoDto((string)x.campo, (long)x.id_mascara, (bool)x.permite_revelar)).ToList();
        var usuarios = (await c.QueryAsync(
            "SELECT u.id_usuario, u.username AS codigo, u.nombre_completo AS nombre FROM sec.USUARIO_PERFIL_DATOS up JOIN sec.USUARIO u ON u.id_usuario = up.id_usuario WHERE up.id_perfil_datos = @id ORDER BY u.nombre_completo", new { id }))
            .Select(x => new PerfilUsuarioResponse((long)x.id_usuario, (string)x.codigo, (string)x.nombre)).ToList();
        return new PerfilDatosResponse((long)p.id_perfil_datos, (string)p.codigo, (string)p.nombre, (string?)p.descripcion, (string)p.estado, await LeerFiltrosAsync(c, null, id), campos, usuarios);
    }

    private static async Task<string?> ValidarPerfilAsync(SqlConnection c, SqlTransaction tx, long idCompania, PerfilDatosGuardarRequest r, bool validarCodigo)
    {
        if (validarCodigo && !Regex.IsMatch((r.Codigo ?? "").Trim().ToUpperInvariant(), "^[A-Z0-9_]{2,50}$"))
            return "El código debe tener entre 2 y 50 letras, números o guion bajo.";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 100) return "El nombre es obligatorio (máximo 100 caracteres).";
        if (r.Descripcion is { Length: > 300 }) return "La descripción admite hasta 300 caracteres.";
        var filtros = r.Filtros ?? [];
        var campos = r.Campos ?? [];
        if (filtros.Count == 0 && campos.Count == 0) return "Defina al menos una regla de clientes visibles o un campo enmascarado.";

        if (filtros.Select(f => f.Atributo).Distinct().Count() != filtros.Count) return "Un atributo no puede repetirse en las reglas de clientes.";
        foreach (var f in filtros)
        {
            var atr = PerfilDatosCatalogo.BuscarAtributo(f.Atributo);
            if (atr is null) return $"El atributo «{f.Atributo}» no existe.";
            if (f.Modo is not ("INCLUIR" or "EXCLUIR")) return $"El modo de «{atr.Etiqueta}» no es válido.";
            var valores = (f.Valores ?? []).Distinct().ToList();
            if (valores.Count == 0) return $"Elija al menos un valor para «{atr.Etiqueta}».";
            if (f.Atributo == "UNIDAD")
            {
                var ids = valores.Select(v => long.TryParse(v, out var n) ? n : -1).ToList();
                var ok = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sec.DEPARTAMENTO WHERE id_compania = @idCompania AND id_departamento IN @ids", new { idCompania, ids }, tx);
                if (ok != ids.Count) return "Alguna de las unidades elegidas no existe.";
            }
            else
            {
                IEnumerable<string> validos = f.Atributo switch
                {
                    "TIPO_PERSONA" => PerfilDatosCatalogo.TiposPersona.Select(x => x.Valor),
                    "ESTADO" => PerfilDatosCatalogo.Estados.Select(x => x.Valor),
                    _ => PerfilDatosCatalogo.SiNo.Select(x => x.Valor),
                };
                if (valores.Any(v => !validos.Contains(v))) return $"Hay un valor no válido en «{atr.Etiqueta}».";
            }
        }

        if (campos.Select(f => f.Campo).Distinct().Count() != campos.Count) return "Un campo no puede repetirse.";
        foreach (var f in campos)
        {
            var campo = PerfilDatosCatalogo.BuscarCampo(f.Campo);
            if (campo is null) return $"El campo «{f.Campo}» no existe.";
            var m = await c.QuerySingleOrDefaultAsync("SELECT tipo, estado FROM sec.MASCARA WHERE id_mascara = @id AND id_compania = @idCompania", new { id = f.IdMascara, idCompania }, tx);
            if (m is null) return $"La máscara elegida para «{campo.Etiqueta}» no existe.";
            if ((string)m.estado != "Activo") return $"La máscara elegida para «{campo.Etiqueta}» está inactiva.";
            if (!PerfilDatosCatalogo.TiposMascaraCompatibles(campo.Tipo).Contains((string)m.tipo))
                return $"La máscara elegida no sirve para «{campo.Etiqueta}» (tipo {m.tipo}).";
        }
        return null;
    }

    private static async Task GuardarReglasAsync(SqlConnection c, SqlTransaction tx, long idPerfil, PerfilDatosGuardarRequest r)
    {
        await c.ExecuteAsync("DELETE FROM sec.PERFIL_DATOS_FILTRO WHERE id_perfil_datos = @idPerfil; DELETE FROM sec.PERFIL_DATOS_CAMPO WHERE id_perfil_datos = @idPerfil", new { idPerfil }, tx);
        foreach (var f in r.Filtros ?? [])
            foreach (var v in f.Valores.Distinct())
                await c.ExecuteAsync("INSERT INTO sec.PERFIL_DATOS_FILTRO (id_perfil_datos, atributo, modo, valor) VALUES (@idPerfil, @a, @m, @v)", new { idPerfil, a = f.Atributo, m = f.Modo, v }, tx);
        foreach (var f in r.Campos ?? [])
            await c.ExecuteAsync("INSERT INTO sec.PERFIL_DATOS_CAMPO (id_perfil_datos, campo, id_mascara, permite_revelar) VALUES (@idPerfil, @campo, @m, @rev)",
                new { idPerfil, campo = f.Campo, m = f.IdMascara, rev = f.PermiteRevelar }, tx);
    }

    private static async Task<string> ResumenCamposAsync(SqlConnection c, SqlTransaction tx, long idPerfil)
    {
        var filas = await c.QueryAsync(
            "SELECT pc.campo, m.nombre, pc.permite_revelar AS rev FROM sec.PERFIL_DATOS_CAMPO pc JOIN sec.MASCARA m ON m.id_mascara = pc.id_mascara WHERE pc.id_perfil_datos = @idPerfil ORDER BY pc.campo", new { idPerfil }, tx);
        var partes = filas.Select(f => $"{PerfilDatosCatalogo.BuscarCampo((string)f.campo)?.Etiqueta ?? (string)f.campo}: {f.nombre}{((bool)f.rev ? " (revelable)" : "")}").ToList();
        return partes.Count == 0 ? "Sin máscaras" : string.Join(" · ", partes);
    }

    public async Task<ResultadoOperacion> CrearPerfilAsync(ActorContext actor, PerfilDatosGuardarRequest r)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var error = await ValidarPerfilAsync(c, tx, actor.IdCompania, r, true);
            if (error is not null) { tx.Rollback(); return ResultadoOperacion.Fallo(400, "datos_invalidos", error); }
            var codigo = r.Codigo.Trim().ToUpperInvariant();
            var id = await c.QuerySingleAsync<long>(
                "INSERT INTO sec.PERFIL_DATOS (id_compania, codigo, nombre, descripcion, creado_por) OUTPUT INSERTED.id_perfil_datos VALUES (@c, @codigo, @nombre, @desc, @por)",
                new { c = actor.IdCompania, codigo, nombre = r.Nombre.Trim(), desc = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim(), por = actor.NombreDescriptivo }, tx);
            await GuardarReglasAsync(c, tx, id, r);
            await Auditoria.AltaAsync(c, tx, actor, EntidadPerfil, id, $"{codigo} · {r.Nombre.Trim()}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "perfil_duplicado", $"Ya existe el perfil {r.Codigo.Trim().ToUpperInvariant()}.");
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EditarPerfilAsync(ActorContext actor, long id, PerfilDatosGuardarRequest r)
    {
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var p = await c.QuerySingleOrDefaultAsync("SELECT nombre, descripcion FROM sec.PERFIL_DATOS WITH (UPDLOCK) WHERE id_perfil_datos = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
            if (p is null) { tx.Rollback(); return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el perfil."); }
            var error = await ValidarPerfilAsync(c, tx, actor.IdCompania, r, false);
            if (error is not null) { tx.Rollback(); return ResultadoOperacion.Fallo(400, "datos_invalidos", error); }

            var nombresUnidad = await NombresUnidadAsync(c, tx, actor.IdCompania);
            var filasAntes = ResumenFilas(await LeerFiltrosAsync(c, tx, id), nombresUnidad);
            var camposAntes = await ResumenCamposAsync(c, tx, id);

            var nombre = r.Nombre.Trim();
            var desc = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim();
            await c.ExecuteAsync("UPDATE sec.PERFIL_DATOS SET nombre = @nombre, descripcion = @desc, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_perfil_datos = @id",
                new { nombre, desc, por = actor.NombreDescriptivo, id }, tx);
            await GuardarReglasAsync(c, tx, id, r);

            if ((string)p.nombre != nombre) await Auditoria.CambioAsync(c, tx, actor, EntidadPerfil, id, "Nombre", (string)p.nombre, nombre);
            if ((string?)p.descripcion != desc) await Auditoria.CambioAsync(c, tx, actor, EntidadPerfil, id, "Descripción", (string?)p.descripcion, desc);
            var filasDespues = ResumenFilas(r.Filtros ?? [], nombresUnidad);
            if (filasAntes != filasDespues) await Auditoria.CambioAsync(c, tx, actor, EntidadPerfil, id, "Clientes visibles", filasAntes, filasDespues);
            var camposDespues = await ResumenCamposAsync(c, tx, id);
            if (camposAntes != camposDespues) await Auditoria.CambioAsync(c, tx, actor, EntidadPerfil, id, "Campos enmascarados", camposAntes, camposDespues);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> CambiarEstadoPerfilAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado inválido.");
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var p = await c.QuerySingleOrDefaultAsync("SELECT estado FROM sec.PERFIL_DATOS WITH (UPDLOCK) WHERE id_perfil_datos = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
            if (p is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el perfil.");
            if ((string)p.estado == estado) return ResultadoOperacion.Ok(id);
            await c.ExecuteAsync("UPDATE sec.PERFIL_DATOS SET estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_perfil_datos = @id", new { e = estado, por = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadPerfil, id, "Estado", (string)p.estado, estado);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    // ------------------------------------------------------------------ Perfiles por usuario

    public async Task<IReadOnlyList<UsuarioPerfilesResponse>> ListarUsuariosAsync(long idCompania)
    {
        using var c = NuevaConexion();
        var usuarios = (await c.QueryAsync("SELECT id_usuario, username, nombre_completo, tipo_usuario, estado FROM sec.USUARIO WHERE id_compania = @idCompania ORDER BY nombre_completo", new { idCompania })).ToList();
        var asign = (await c.QueryAsync(
            "SELECT up.id_usuario, up.id_perfil_datos FROM sec.USUARIO_PERFIL_DATOS up JOIN sec.PERFIL_DATOS p ON p.id_perfil_datos = up.id_perfil_datos WHERE p.id_compania = @idCompania", new { idCompania })).ToList();
        return usuarios.Select(u => new UsuarioPerfilesResponse((long)u.id_usuario, (string)u.username, (string)u.nombre_completo, (string)u.tipo_usuario, (string)u.estado,
            asign.Where(a => (long)a.id_usuario == (long)u.id_usuario).Select(a => (long)a.id_perfil_datos).ToList())).ToList();
    }

    public async Task<ResultadoOperacion> AsignarPerfilesAsync(ActorContext actor, long idUsuario, IReadOnlyList<long> perfiles)
    {
        perfiles = perfiles.Distinct().ToList();
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var u = await c.QuerySingleOrDefaultAsync("SELECT nombre_completo FROM sec.USUARIO WHERE id_usuario = @idUsuario AND id_compania = @c", new { idUsuario, c = actor.IdCompania }, tx);
            if (u is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El usuario no existe.");
            if (perfiles.Count > 0)
            {
                var ok = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sec.PERFIL_DATOS WHERE id_compania = @c AND estado = 'Activo' AND id_perfil_datos IN @perfiles", new { c = actor.IdCompania, perfiles }, tx);
                if (ok != perfiles.Count) return ResultadoOperacion.Fallo(400, "perfil_invalido", "Alguno de los perfiles no existe o está inactivo.");
            }
            var antes = (await c.QueryAsync<string>(
                "SELECT p.codigo FROM sec.USUARIO_PERFIL_DATOS up JOIN sec.PERFIL_DATOS p ON p.id_perfil_datos = up.id_perfil_datos WHERE up.id_usuario = @idUsuario ORDER BY p.codigo", new { idUsuario }, tx)).ToList();
            await c.ExecuteAsync("DELETE FROM sec.USUARIO_PERFIL_DATOS WHERE id_usuario = @idUsuario", new { idUsuario }, tx);
            foreach (var p in perfiles)
                await c.ExecuteAsync("INSERT INTO sec.USUARIO_PERFIL_DATOS (id_usuario, id_perfil_datos, creado_por) VALUES (@idUsuario, @p, @por)", new { idUsuario, p, por = actor.NombreDescriptivo }, tx);
            var despues = perfiles.Count == 0 ? new List<string>() : (await c.QueryAsync<string>("SELECT codigo FROM sec.PERFIL_DATOS WHERE id_perfil_datos IN @perfiles ORDER BY codigo", new { perfiles }, tx)).ToList();
            if (!antes.SequenceEqual(despues))
                await Auditoria.CambioAsync(c, tx, actor, EntidadUsuario, idUsuario, "Perfiles de datos", antes.Count == 0 ? "Sin perfiles" : string.Join(", ", antes), despues.Count == 0 ? "Sin perfiles" : string.Join(", ", despues));
            tx.Commit();
            return ResultadoOperacion.Ok(idUsuario);
        }
        catch { tx.Rollback(); throw; }
    }
}
