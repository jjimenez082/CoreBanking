using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Administracion del Gestor documental (permiso DOC_ADMINISTRACION): carpetas, tipos de documento y documentos exigidos por rol.
/// Es configuracion de la compania: se guarda directo y cada cambio queda en la auditoria (entidades DOC_CARPETA, DOC_TIPO y ROL_REQUISITO).
/// Nada se borra: carpetas y tipos se inactivan y los documentos ya cargados se conservan.
/// </summary>
public class DocumentoAdminRepositorio
{
    public const string Permiso = "DOC_ADMINISTRACION";
    public const string EntidadCarpeta = "DOC_CARPETA";
    public const string EntidadTipo = "DOC_TIPO";

    private static readonly Regex CodigoValido = new("^[A-Z][A-Z0-9_]{1,39}$", RegexOptions.Compiled);

    private readonly TenantContext _tenant;
    public DocumentoAdminRepositorio(TenantContext tenant) => _tenant = tenant;
    private SqlConnection Nueva() => new(_tenant.ConnectionString);

    private static string SiNo(bool v) => v ? "Sí" : "No";
    private static string Aplica(bool f, bool j, bool g)
    {
        var t = string.Join(", ", new[] { f ? "Persona" : null, j ? "Organización" : null, g ? "Grupo" : null }.Where(x => x is not null));
        return t.Length == 0 ? "ninguno" : t;
    }

    // ------------------------------------------------------------------ Carpetas

    public async Task<IReadOnlyList<CarpetaAdminResponse>> ListarCarpetasAsync(long idCompania)
    {
        using var c = Nueva();
        return (await c.QueryAsync(
            """
            SELECT ca.id_carpeta, ca.codigo, ca.nombre, ca.descripcion, ca.orden, ca.es_sistema, ca.aplica_fisica, ca.aplica_juridica, ca.aplica_grupo, ca.estado,
                   (SELECT COUNT(*) FROM doc.TIPO_DOCUMENTO t WHERE t.id_carpeta = ca.id_carpeta AND t.estado = 'Activo') AS tipos
            FROM doc.CARPETA ca WHERE ca.id_compania = @idCompania ORDER BY ca.orden, ca.nombre
            """, new { idCompania })).Select(f => new CarpetaAdminResponse(
                (long)f.id_carpeta, (string)f.codigo, (string)f.nombre, (string?)f.descripcion, (int)f.orden, (bool)f.es_sistema,
                (bool)f.aplica_fisica, (bool)f.aplica_juridica, (bool)f.aplica_grupo, (string)f.estado, (int)f.tipos)).ToList();
    }

    private static string? ValidarCarpeta(CarpetaGuardarRequest r, bool validarCodigo)
    {
        if (validarCodigo && !CodigoValido.IsMatch((r.Codigo ?? "").Trim().ToUpperInvariant()))
            return "El código debe empezar con una letra y usar solo letras, números y guion bajo (2 a 40 caracteres).";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 100) return "El nombre es obligatorio (máximo 100 caracteres).";
        if ((r.Descripcion ?? "").Trim().Length > 300) return "La descripción admite máximo 300 caracteres.";
        if (r.Orden is < 0 or > 9999) return "El orden debe estar entre 0 y 9999.";
        if (!r.AplicaFisica && !r.AplicaJuridica && !r.AplicaGrupo) return "La carpeta debe aplicar al menos a un tipo de interlocutor.";
        return null;
    }

    public async Task<ResultadoOperacion> CrearCarpetaAsync(ActorContext actor, CarpetaGuardarRequest req)
    {
        var error = ValidarCarpeta(req, true);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        var codigo = req.Codigo.Trim().ToUpperInvariant();
        using var c = Nueva(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO doc.CARPETA (id_compania, codigo, nombre, descripcion, orden, es_sistema, aplica_fisica, aplica_juridica, aplica_grupo, creado_por)
                OUTPUT INSERTED.id_carpeta
                VALUES (@c, @codigo, @nombre, @descripcion, @orden, 0, @f, @j, @g, @por)
                """, new { c = actor.IdCompania, codigo, nombre = req.Nombre.Trim(), descripcion = Nulo(req.Descripcion), orden = req.Orden, f = req.AplicaFisica, j = req.AplicaJuridica, g = req.AplicaGrupo, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadCarpeta, id, $"Carpeta {codigo} · {req.Nombre.Trim()} (aplica a: {Aplica(req.AplicaFisica, req.AplicaJuridica, req.AplicaGrupo)})");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", $"Ya existe una carpeta con el código {codigo}.");
        }
        catch { tx.Rollback(); throw; }
    }

    private static string? Nulo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();

    public async Task<ResultadoOperacion> EditarCarpetaAsync(ActorContext actor, long id, CarpetaGuardarRequest req)
    {
        var error = ValidarCarpeta(req, false);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        using var c = Nueva(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var a = await c.QuerySingleOrDefaultAsync("SELECT * FROM doc.CARPETA WITH (UPDLOCK) WHERE id_carpeta = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
            if (a is null) { tx.Rollback(); return ResultadoOperacion.Fallo(404, "no_encontrada", "No existe la carpeta."); }
            if ((bool)a.es_sistema) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "carpeta_sistema", "Es una carpeta del sistema (la genera la aplicación) y no se puede modificar."); }

            // Dejar de aplicar a un tipo de interlocutor solo si ninguno de los tipos de la carpeta quedaria sin donde aplicar a ese tipo es demasiado fino:
            // la aplicabilidad de la carpeta es informativa para la clasificacion; la que valida la carga es la del tipo de documento.
            var nombre = req.Nombre.Trim(); var desc = Nulo(req.Descripcion);
            await c.ExecuteAsync(
                """
                UPDATE doc.CARPETA SET nombre = @nombre, descripcion = @desc, orden = @orden, aplica_fisica = @f, aplica_juridica = @j, aplica_grupo = @g,
                       modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_carpeta = @id
                """, new { nombre, desc, orden = req.Orden, f = req.AplicaFisica, j = req.AplicaJuridica, g = req.AplicaGrupo, por = actor.NombreDescriptivo, id }, tx);
            var etiqueta = $"Carpeta {(string)a.codigo}";
            if ((string)a.nombre != nombre) await Auditoria.CambioAsync(c, tx, actor, EntidadCarpeta, id, $"{etiqueta} · nombre", (string)a.nombre, nombre);
            if ((string?)a.descripcion != desc) await Auditoria.CambioAsync(c, tx, actor, EntidadCarpeta, id, $"{etiqueta} · descripción", (string?)a.descripcion, desc);
            if ((int)a.orden != req.Orden) await Auditoria.CambioAsync(c, tx, actor, EntidadCarpeta, id, $"{etiqueta} · orden", ((int)a.orden).ToString(), req.Orden.ToString());
            var antesAp = Aplica((bool)a.aplica_fisica, (bool)a.aplica_juridica, (bool)a.aplica_grupo); var ahoraAp = Aplica(req.AplicaFisica, req.AplicaJuridica, req.AplicaGrupo);
            if (antesAp != ahoraAp) await Auditoria.CambioAsync(c, tx, actor, EntidadCarpeta, id, $"{etiqueta} · aplica a", antesAp, ahoraAp);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EstadoCarpetaAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado inválido.");
        using var c = Nueva(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var a = await c.QuerySingleOrDefaultAsync("SELECT codigo, estado, es_sistema FROM doc.CARPETA WITH (UPDLOCK) WHERE id_carpeta = @id AND id_compania = @c", new { id, c = actor.IdCompania }, tx);
            if (a is null) { tx.Rollback(); return ResultadoOperacion.Fallo(404, "no_encontrada", "No existe la carpeta."); }
            if ((string)a.estado == estado) { tx.Rollback(); return ResultadoOperacion.Ok(id); }
            if ((bool)a.es_sistema) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "carpeta_sistema", "Es una carpeta del sistema y no se puede inactivar."); }
            if (estado == "Inactivo")
            {
                var tipos = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM doc.TIPO_DOCUMENTO WHERE id_carpeta = @id AND estado = 'Activo'", new { id }, tx);
                if (tipos > 0) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "carpeta_con_tipos", $"La carpeta tiene {tipos} tipo(s) de documento activos; inactívelos o muévalos a otra carpeta primero."); }
            }
            await c.ExecuteAsync("UPDATE doc.CARPETA SET estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_carpeta = @id", new { e = estado, por = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadCarpeta, id, $"Carpeta {(string)a.codigo} · estado", (string)a.estado, estado);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    // ------------------------------------------------------------------ Tipos de documento

    public async Task<IReadOnlyList<TipoAdminResponse>> ListarTiposAsync(long idCompania)
    {
        using var c = Nueva();
        return (await c.QueryAsync(
            """
            SELECT t.id_tipo_documento, t.codigo, t.nombre, t.descripcion, t.id_carpeta, ca.nombre AS carpeta, t.orden, t.cardinalidad, t.maximo, t.vencimiento,
                   t.dias_aviso, t.confidencialidad, t.aplica_fisica, t.aplica_juridica, t.aplica_grupo, t.estado,
                   (SELECT COUNT(*) FROM doc.DOCUMENTO d WHERE d.id_tipo_documento = t.id_tipo_documento AND d.estado = 'Vigente') AS vigentes,
                   (SELECT COUNT(*) FROM doc.ROL_REQUISITO_DOCUMENTO q WHERE q.id_tipo_documento = t.id_tipo_documento) AS roles
            FROM doc.TIPO_DOCUMENTO t JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta
            WHERE t.id_compania = @idCompania ORDER BY ca.orden, t.orden, t.nombre
            """, new { idCompania })).Select(f => new TipoAdminResponse(
                (long)f.id_tipo_documento, (string)f.codigo, (string)f.nombre, (string?)f.descripcion, (long)f.id_carpeta, (string)f.carpeta, (int)f.orden,
                (string)f.cardinalidad, (int?)f.maximo, (string)f.vencimiento, (int)f.dias_aviso, (string)f.confidencialidad,
                (bool)f.aplica_fisica, (bool)f.aplica_juridica, (bool)f.aplica_grupo, (string)f.estado, (int)f.vigentes, (int)f.roles)).ToList();
    }

    private static string? ValidarTipo(TipoGuardarRequest r, bool validarCodigo)
    {
        if (validarCodigo && !CodigoValido.IsMatch((r.Codigo ?? "").Trim().ToUpperInvariant()))
            return "El código debe empezar con una letra y usar solo letras, números y guion bajo (2 a 40 caracteres).";
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 120) return "El nombre es obligatorio (máximo 120 caracteres).";
        if ((r.Descripcion ?? "").Trim().Length > 300) return "La descripción admite máximo 300 caracteres.";
        if (r.IdCarpeta <= 0) return "Seleccione la carpeta.";
        if (r.Orden is < 0 or > 9999) return "El orden debe estar entre 0 y 9999.";
        if (r.Cardinalidad is not ("Unico" or "Multiple")) return "La cardinalidad debe ser Único o Múltiple.";
        if (r.Maximo is not null && (r.Cardinalidad != "Multiple" || r.Maximo < 1 || r.Maximo > 1000)) return "El máximo aplica solo a tipos Múltiples y debe estar entre 1 y 1000.";
        if (r.Vencimiento is not ("Obligatorio" or "Opcional" or "NoAplica")) return "El vencimiento debe ser Obligatorio, Opcional o No aplica.";
        if (r.DiasAviso is < 0 or > 365) return "Los días de aviso deben estar entre 0 y 365.";
        if (r.Confidencialidad is not ("Normal" or "Restringida")) return "La confidencialidad debe ser Normal o Restringida.";
        if (!r.AplicaFisica && !r.AplicaJuridica && !r.AplicaGrupo) return "El tipo debe aplicar al menos a un tipo de interlocutor.";
        return null;
    }

    private static async Task<string?> ValidarCarpetaDestinoAsync(SqlConnection c, SqlTransaction tx, long idCompania, long idCarpeta)
    {
        var ca = await c.QuerySingleOrDefaultAsync("SELECT es_sistema, estado FROM doc.CARPETA WHERE id_carpeta = @idCarpeta AND id_compania = @idCompania", new { idCarpeta, idCompania }, tx);
        if (ca is null) return "La carpeta no existe.";
        if ((bool)ca.es_sistema) return "La carpeta Gestiones es del sistema; no admite tipos de documento del expediente.";
        if ((string)ca.estado != "Activo") return "La carpeta está inactiva.";
        return null;
    }

    public async Task<ResultadoOperacion> CrearTipoAsync(ActorContext actor, TipoGuardarRequest req)
    {
        var error = ValidarTipo(req, true);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        var codigo = req.Codigo.Trim().ToUpperInvariant();
        using var c = Nueva(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var errCarpeta = await ValidarCarpetaDestinoAsync(c, tx, actor.IdCompania, req.IdCarpeta);
            if (errCarpeta is not null) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "carpeta_invalida", errCarpeta); }
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO doc.TIPO_DOCUMENTO (id_compania, id_carpeta, codigo, nombre, descripcion, orden, cardinalidad, maximo, vencimiento, dias_aviso, confidencialidad,
                                                requiere_aprobacion, aplica_fisica, aplica_juridica, aplica_grupo, creado_por)
                OUTPUT INSERTED.id_tipo_documento
                VALUES (@c, @carpeta, @codigo, @nombre, @desc, @orden, @card, @max, @venc, @aviso, @conf, 0, @f, @j, @g, @por)
                """, new { c = actor.IdCompania, carpeta = req.IdCarpeta, codigo, nombre = req.Nombre.Trim(), desc = Nulo(req.Descripcion), orden = req.Orden, card = req.Cardinalidad, max = req.Maximo,
                           venc = req.Vencimiento, aviso = req.DiasAviso, conf = req.Confidencialidad, f = req.AplicaFisica, j = req.AplicaJuridica, g = req.AplicaGrupo, por = actor.NombreDescriptivo }, tx);
            await Auditoria.AltaAsync(c, tx, actor, EntidadTipo, id,
                $"Tipo {codigo} · {req.Nombre.Trim()} ({(req.Cardinalidad == "Unico" ? "único vigente" : req.Maximo is null ? "múltiple" : $"múltiple, máximo {req.Maximo}")}; vencimiento {Texto(req.Vencimiento)}; {req.Confidencialidad.ToLowerInvariant()}; aplica a: {Aplica(req.AplicaFisica, req.AplicaJuridica, req.AplicaGrupo)})");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "codigo_duplicado", $"Ya existe un tipo de documento con el código {codigo}.");
        }
        catch { tx.Rollback(); throw; }
    }

    private static string Texto(string venc) => venc switch { "Obligatorio" => "obligatorio", "Opcional" => "opcional", _ => "no aplica" };

    public async Task<ResultadoOperacion> EditarTipoAsync(ActorContext actor, long id, TipoGuardarRequest req)
    {
        var error = ValidarTipo(req, false);
        if (error is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", error);
        using var c = Nueva(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var a = await c.QuerySingleOrDefaultAsync(
                "SELECT t.*, ca.nombre AS carpeta FROM doc.TIPO_DOCUMENTO t WITH (UPDLOCK) JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta WHERE t.id_tipo_documento = @id AND t.id_compania = @c",
                new { id, c = actor.IdCompania }, tx);
            if (a is null) { tx.Rollback(); return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el tipo de documento."); }

            if ((long)a.id_carpeta != req.IdCarpeta)
            {
                var errCarpeta = await ValidarCarpetaDestinoAsync(c, tx, actor.IdCompania, req.IdCarpeta);
                if (errCarpeta is not null) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "carpeta_invalida", errCarpeta); }
            }
            // Un tipo que pasa a «Unico» no puede tener clientes con mas de un documento vigente: la regla ya no se podria cumplir.
            if (req.Cardinalidad == "Unico" && (string)a.cardinalidad != "Unico")
            {
                var duplicados = await c.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM (SELECT id_interlocutor FROM doc.DOCUMENTO WHERE id_tipo_documento = @id AND estado = 'Vigente' AND origen = 'Expediente' GROUP BY id_interlocutor HAVING COUNT(*) > 1) x", new { id }, tx);
                if (duplicados > 0) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "hay_duplicados", $"{duplicados} cliente(s) ya tienen más de un documento vigente de este tipo; obsolete los sobrantes antes de volverlo Único."); }
            }
            // Dejar de aplicar a un tipo de interlocutor mientras un rol lo exige para ese tipo dejaria un requisito imposible.
            foreach (var (tipo, antes, ahora) in new[] { ("Fisica", (bool)a.aplica_fisica, req.AplicaFisica), ("Juridica", (bool)a.aplica_juridica, req.AplicaJuridica) })
            {
                if (!antes || ahora) continue;
                var exigido = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM doc.ROL_REQUISITO_DOCUMENTO WHERE id_tipo_documento = @id AND tipo_persona = @tipo", new { id, tipo }, tx);
                if (exigido > 0) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "exigido_por_rol", $"Hay roles que exigen este documento para {(tipo == "Fisica" ? "personas" : "organizaciones")}; quite el requisito antes de dejar de aplicarlo."); }
            }

            var nombre = req.Nombre.Trim(); var desc = Nulo(req.Descripcion);
            await c.ExecuteAsync(
                """
                UPDATE doc.TIPO_DOCUMENTO SET nombre = @nombre, descripcion = @desc, id_carpeta = @carpeta, orden = @orden, cardinalidad = @card, maximo = @max, vencimiento = @venc,
                       dias_aviso = @aviso, confidencialidad = @conf, aplica_fisica = @f, aplica_juridica = @j, aplica_grupo = @g,
                       modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_tipo_documento = @id
                """, new { nombre, desc, carpeta = req.IdCarpeta, orden = req.Orden, card = req.Cardinalidad, max = req.Maximo, venc = req.Vencimiento, aviso = req.DiasAviso, conf = req.Confidencialidad,
                           f = req.AplicaFisica, j = req.AplicaJuridica, g = req.AplicaGrupo, por = actor.NombreDescriptivo, id }, tx);

            var et = $"Tipo {(string)a.codigo}";
            async Task Cambio(string campo, string? antes, string? ahora)
            { if (antes != ahora) await Auditoria.CambioAsync(c, tx, actor, EntidadTipo, id, $"{et} · {campo}", antes, ahora); }
            await Cambio("nombre", (string)a.nombre, nombre);
            await Cambio("descripción", (string?)a.descripcion, desc);
            if ((long)a.id_carpeta != req.IdCarpeta)
            {
                var nueva = await c.ExecuteScalarAsync<string>("SELECT nombre FROM doc.CARPETA WHERE id_carpeta = @id", new { id = req.IdCarpeta }, tx);
                await Cambio("carpeta", (string)a.carpeta, nueva);
            }
            await Cambio("orden", ((int)a.orden).ToString(), req.Orden.ToString());
            await Cambio("cardinalidad", (string)a.cardinalidad == "Unico" ? "Único" : "Múltiple", req.Cardinalidad == "Unico" ? "Único" : "Múltiple");
            await Cambio("máximo", ((int?)a.maximo)?.ToString() ?? "sin máximo", req.Maximo?.ToString() ?? "sin máximo");
            await Cambio("vencimiento", Texto((string)a.vencimiento), Texto(req.Vencimiento));
            await Cambio("días de aviso", ((int)a.dias_aviso).ToString(), req.DiasAviso.ToString());
            await Cambio("confidencialidad", (string)a.confidencialidad, req.Confidencialidad);
            await Cambio("aplica a", Aplica((bool)a.aplica_fisica, (bool)a.aplica_juridica, (bool)a.aplica_grupo), Aplica(req.AplicaFisica, req.AplicaJuridica, req.AplicaGrupo));
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> EstadoTipoAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Activo" or "Inactivo")) return ResultadoOperacion.Fallo(400, "estado_invalido", "Estado inválido.");
        using var c = Nueva(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var a = await c.QuerySingleOrDefaultAsync(
                "SELECT t.codigo, t.estado, t.id_carpeta, ca.estado AS carpeta_estado FROM doc.TIPO_DOCUMENTO t WITH (UPDLOCK) JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta WHERE t.id_tipo_documento = @id AND t.id_compania = @c",
                new { id, c = actor.IdCompania }, tx);
            if (a is null) { tx.Rollback(); return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el tipo de documento."); }
            if ((string)a.estado == estado) { tx.Rollback(); return ResultadoOperacion.Ok(id); }
            if (estado == "Inactivo")
            {
                var roles = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM doc.ROL_REQUISITO_DOCUMENTO WHERE id_tipo_documento = @id", new { id }, tx);
                if (roles > 0) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "exigido_por_rol", $"{roles} rol(es) exigen este documento; quite el requisito antes de inactivarlo."); }
                var pend = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM doc.DOCUMENTO WHERE id_tipo_documento = @id AND estado IN ('EnGestion','Temporal')", new { id }, tx);
                if (pend > 0) { tx.Rollback(); return ResultadoOperacion.Fallo(409, "documentos_pendientes", "Hay documentos de este tipo en una gestión pendiente o sin enviar; deben resolverse antes."); }
            }
            else if ((string)a.carpeta_estado != "Activo") { tx.Rollback(); return ResultadoOperacion.Fallo(409, "carpeta_inactiva", "La carpeta del tipo está inactiva; actívela primero."); }
            await c.ExecuteAsync("UPDATE doc.TIPO_DOCUMENTO SET estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_tipo_documento = @id", new { e = estado, por = actor.NombreDescriptivo, id }, tx);
            await Auditoria.CambioAsync(c, tx, actor, EntidadTipo, id, $"Tipo {(string)a.codigo} · estado", (string)a.estado, estado);
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    // ------------------------------------------------------------------ Documentos exigidos por rol

    public async Task<RolDocumentosAdminResponse?> RolesAsync(long idCompania, string tipoPersona)
    {
        if (!ReglasCampos.TipoValido(tipoPersona)) return null;
        using var c = Nueva();
        var tipos = (await c.QueryAsync(
            """
            SELECT t.id_tipo_documento, t.nombre, ca.nombre AS carpeta
            FROM doc.TIPO_DOCUMENTO t JOIN doc.CARPETA ca ON ca.id_carpeta = t.id_carpeta
            WHERE t.id_compania = @idCompania AND t.estado = 'Activo' AND ca.estado = 'Activo' AND ca.es_sistema = 0
              AND ((@tipoPersona = 'Fisica' AND t.aplica_fisica = 1) OR (@tipoPersona = 'Juridica' AND t.aplica_juridica = 1))
            ORDER BY ca.orden, t.orden, t.nombre
            """, new { idCompania, tipoPersona })).Select(f => new TipoExigibleResponse((long)f.id_tipo_documento, (string)f.nombre, (string)f.carpeta)).ToList();
        var filas = (await c.QueryAsync("SELECT id_tipo_rol, id_tipo_documento FROM doc.ROL_REQUISITO_DOCUMENTO WHERE id_compania = @idCompania AND tipo_persona = @tipoPersona", new { idCompania, tipoPersona })).ToList();
        var catalogo = await RolServicio.CatalogoRolesAsync(c, null);
        var roles = catalogo.Where(r => r.Estado == "Activo" && RolServicio.AplicaATipo(r.Codigo, tipoPersona))
            .Select(r => new RolDocumentosResponse(r.IdTipoRol, r.Codigo, r.Nombre, filas.Where(f => (long)f.id_tipo_rol == r.IdTipoRol).Select(f => (long)f.id_tipo_documento).ToList())).ToList();
        return new RolDocumentosAdminResponse(tipoPersona, tipos, roles);
    }

    public async Task<ResultadoOperacion> GuardarRolesAsync(ActorContext actor, RolDocumentosGuardarRequest req)
    {
        if (req?.Roles is null || req.Roles.Count == 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", "No hay roles que guardar.");
        if (!ReglasCampos.TipoValido(req.TipoPersona)) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Tipo de interlocutor inválido (Fisica o Juridica).");
        var tipo = req.TipoPersona;
        var rotuloTipo = tipo == "Juridica" ? "Organización" : "Persona";
        using var c = Nueva(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var vigentes = await RolesAsync(actor.IdCompania, tipo);
            var permitidos = vigentes!.Tipos.ToDictionary(t => t.IdTipoDocumento, t => t.Nombre);
            var catalogo = await RolServicio.CatalogoRolesAsync(c, tx);
            foreach (var e in req.Roles)
            {
                var rol = catalogo.FirstOrDefault(x => x.IdTipoRol == e.IdTipoRol && x.Estado == "Activo" && RolServicio.AplicaATipo(x.Codigo, tipo));
                if (rol is null) { tx.Rollback(); return ResultadoOperacion.Fallo(404, "no_encontrado", "Uno de los roles no existe o no aplica a ese tipo de interlocutor."); }
                var nuevos = (e.IdsTipoDocumento ?? []).Distinct().ToHashSet();
                // Se aceptan los tipos activos y aplicables; un tipo que ya estaba exigido pero se inactivo se puede quitar, no agregar.
                var actuales = (await c.QueryAsync<long>("SELECT id_tipo_documento FROM doc.ROL_REQUISITO_DOCUMENTO WHERE id_compania = @c AND id_tipo_rol = @r AND tipo_persona = @tipo",
                    new { c = actor.IdCompania, r = e.IdTipoRol, tipo }, tx)).ToHashSet();
                if (nuevos.Any(n => !actuales.Contains(n) && !permitidos.ContainsKey(n)))
                { tx.Rollback(); return ResultadoOperacion.Fallo(400, "datos_invalidos", "Alguno de los tipos de documento no está activo o no aplica a ese tipo de interlocutor."); }

                foreach (var quitar in actuales.Except(nuevos))
                {
                    await c.ExecuteAsync("DELETE FROM doc.ROL_REQUISITO_DOCUMENTO WHERE id_compania = @c AND id_tipo_rol = @r AND tipo_persona = @tipo AND id_tipo_documento = @t",
                        new { c = actor.IdCompania, r = e.IdTipoRol, tipo, t = quitar }, tx);
                    var nombreT = await c.ExecuteScalarAsync<string>("SELECT nombre FROM doc.TIPO_DOCUMENTO WHERE id_tipo_documento = @t", new { t = quitar }, tx);
                    await Auditoria.CambioAsync(c, tx, actor, DocumentoRequisitoServicio.EntidadAuditoria, e.IdTipoRol, "requisito",
                        $"{rol.Nombre} ({rotuloTipo}) · Documento «{nombreT}» vigente: Obligatorio", $"{rol.Nombre} ({rotuloTipo}) · Documento «{nombreT}» vigente: Opcional");
                }
                foreach (var agregar in nuevos.Except(actuales))
                {
                    await c.ExecuteAsync("INSERT INTO doc.ROL_REQUISITO_DOCUMENTO (id_compania, id_tipo_rol, tipo_persona, id_tipo_documento, creado_por) VALUES (@c, @r, @tipo, @t, @por)",
                        new { c = actor.IdCompania, r = e.IdTipoRol, tipo, t = agregar, por = actor.NombreDescriptivo }, tx);
                    await Auditoria.CambioAsync(c, tx, actor, DocumentoRequisitoServicio.EntidadAuditoria, e.IdTipoRol, "requisito",
                        $"{rol.Nombre} ({rotuloTipo}) · Documento «{permitidos[agregar]}» vigente: Opcional", $"{rol.Nombre} ({rotuloTipo}) · Documento «{permitidos[agregar]}» vigente: Obligatorio");
                }
            }
            tx.Commit();
            return ResultadoOperacion.Ok(0);
        }
        catch { tx.Rollback(); throw; }
    }
}
