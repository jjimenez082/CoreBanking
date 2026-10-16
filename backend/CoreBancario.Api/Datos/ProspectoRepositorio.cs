using System.Text.Json;
using Dapper;
using System.Linq;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Prospectos (cli.INTERLOCUTOR_PROSPECTO) y validacion de identificacion contra los interlocutores de la compania.
/// El prospecto no tiene afectacion: no genera gestion, solo auditoria. Regla central: no se puede crear un prospecto
/// de una persona que ya existe como interlocutor (si ya es cliente se informa; si solo tiene otro rol tambien se bloquea,
/// porque ya esta registrada y se le puede agregar el rol de cliente por el alta).
/// </summary>
public class ProspectoRepositorio
{
    public const string Entidad = "PROSPECTO";
    private readonly TenantContext _tenant;
    public ProspectoRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    private static string? Vacio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ----- Validacion por identificacion --------------------------------------------

    public static async Task<IdentificacionValidacionResponse> ValidarAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, long idTipo, string numero)
    {
        numero = numero.Trim();
        var p = (await c.QueryAsync(
            """
            SELECT TOP 1 i.id_interlocutor, i.numero,
                   COALESCE(j.nombre_org1, CONCAT_WS(N' ', f.nombre1, f.nombre2, f.apellido1, f.apellido2)) AS nombre
            FROM cli.INTERLOCUTOR_IDENTIFICACION x
            JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = x.id_interlocutor AND i.id_compania = @idCompania
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE x.id_compania = @idCompania AND x.estado = 'Activo'
              AND x.id_tipo_identificacion = @idTipo AND x.numero_identificacion = @numero
            """, new { idCompania, idTipo, numero }, tx)).FirstOrDefault();

        var pro = (await c.QueryAsync(
            """
            SELECT TOP 1 id_interlocutor_prospecto AS id, estado, nombre_contacto AS nombre
            FROM cli.INTERLOCUTOR_PROSPECTO
            WHERE id_compania = @idCompania AND id_tipo_identificacion = @idTipo AND numero_identificacion = @numero
              AND estado IN ('Prospecto','Descartado')
            ORDER BY CASE estado WHEN 'Prospecto' THEN 0 ELSE 1 END, id_interlocutor_prospecto DESC
            """, new { idCompania, idTipo, numero }, tx)).FirstOrDefault();

        var roles = new List<string>();
        long? idInt = null;
        if (p is not null)
        {
            idInt = (long)p.id_interlocutor;
            roles = (await c.QueryAsync<string>(
                """
                SELECT it.codigo FROM cli.INTERLOCUTOR_ROL r
                JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
                WHERE r.id_interlocutor = @idInt AND r.estado = 'Activo' ORDER BY it.orden
                """, new { idInt }, tx)).ToList();
        }

        return new IdentificacionValidacionResponse(
            p is not null, idInt, p is null ? null : (string)p.numero, p is null ? null : (string?)p.nombre,
            roles.Contains("CLIENTE"), roles,
            pro is null ? null : (long)pro.id, pro is null ? null : (string)pro.estado, pro is null ? null : (string)pro.nombre);
    }

    public async Task<IdentificacionValidacionResponse> ValidarAsync(long idCompania, long idTipo, string numero)
    {
        using var c = NuevaConexion();
        return await ValidarAsync(c, null, idCompania, idTipo, numero);
    }

    // ----- Consulta -------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const string EstadoGestionPendiente = "Pendiente de Aprobacion";
    public const string CampoVinculo = "id_prospecto";

    private const string SqlBase = """
        SELECT p.id_interlocutor_prospecto AS IdProspecto, p.nombre_contacto AS Nombre, p.nombre1 AS Nombre1, p.nombre2 AS Nombre2,
               p.apellido1 AS Apellido1, p.apellido2 AS Apellido2, p.fecha_nacimiento AS FechaNacimiento,
               p.id_tipo_identificacion AS IdTipoIdentificacion, t.nombre AS TipoIdentificacion,
               p.numero_identificacion AS NumeroIdentificacion, p.telefono_contacto AS Telefono, p.correo_contacto AS Correo,
               p.canal_origen AS CanalOrigen, p.estado AS Estado, p.fecha_registro AS FechaRegistro,
               p.fecha_conversion AS FechaConversion, p.id_interlocutor AS IdInterlocutor, p.creado_por AS CreadoPor,
               p.id_unidad AS IdUnidad, un.nombre AS UnidadNombre, p.direccion_json AS DireccionJson,
               (SELECT TOP 1 g.id_gestion FROM apr.GESTION g
                  JOIN apr.GESTION_DETALLE d ON d.id_gestion = g.id_gestion AND d.nombre_campo = N'id_prospecto'
                 WHERE g.estado = N'Pendiente de Aprobacion' AND d.valor_nuevo = CONVERT(nvarchar(30), p.id_interlocutor_prospecto)) AS IdGestionPendiente,
               (SELECT TOP 1 g.numero FROM apr.GESTION g
                  JOIN apr.GESTION_DETALLE d ON d.id_gestion = g.id_gestion AND d.nombre_campo = N'id_prospecto'
                 WHERE g.estado = N'Pendiente de Aprobacion' AND d.valor_nuevo = CONVERT(nvarchar(30), p.id_interlocutor_prospecto)) AS NumeroGestionPendiente
        FROM cli.INTERLOCUTOR_PROSPECTO p
        LEFT JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = p.id_tipo_identificacion
        LEFT JOIN sec.DEPARTAMENTO un ON un.id_departamento = p.id_unidad
        """;

    private static ProspectoResponse Mapear(dynamic r)
    {
        DireccionDto? dir = null;
        var json = (string?)r.DireccionJson;
        if (!string.IsNullOrWhiteSpace(json)) dir = JsonSerializer.Deserialize<DireccionDto>(json, Json);
        return new ProspectoResponse(
            (long)r.IdProspecto, (string)r.Nombre, (string?)r.Nombre1, (string?)r.Nombre2, (string?)r.Apellido1, (string?)r.Apellido2,
            (DateTime?)r.FechaNacimiento, (long?)r.IdTipoIdentificacion, (string?)r.TipoIdentificacion, (string?)r.NumeroIdentificacion,
            (string?)r.Telefono, (string?)r.Correo, (string?)r.CanalOrigen, (string)r.Estado, (DateTime)r.FechaRegistro,
            (DateTime?)r.FechaConversion, (long?)r.IdInterlocutor, (string)r.CreadoPor, (long?)r.IdUnidad, (string?)r.UnidadNombre,
            dir, (long?)r.IdGestionPendiente, (string?)r.NumeroGestionPendiente);
    }

    public async Task<IReadOnlyList<ProspectoResponse>> ListarAsync(long idCompania, string? estado)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            SqlBase + " WHERE p.id_compania = @idCompania AND (@estado IS NULL OR p.estado = @estado) ORDER BY p.id_interlocutor_prospecto DESC",
            new { idCompania, estado = Vacio(estado) });
        return filas.Select(f => (ProspectoResponse)Mapear(f)).ToList();
    }

    public async Task<ProspectoResponse?> ObtenerAsync(long idCompania, long id)
    {
        using var c = NuevaConexion();
        var filas = await c.QueryAsync(
            SqlBase + " WHERE p.id_compania = @idCompania AND p.id_interlocutor_prospecto = @id", new { idCompania, id });
        var f = filas.FirstOrDefault();
        return f is null ? null : Mapear(f);
    }

    // ----- Validaciones comunes --------------------------------------------------------

    /// <summary>Valida contacto y direccion (obligatoria). Devuelve la direccion normalizada (principal) para guardarla.</summary>
    private static async Task<(string? Error, DireccionDto? Direccion)> ValidarContactoYDireccionAsync(
        SqlConnection c, SqlTransaction tx, string? telefono, string? correo, DireccionDto? direccion)
    {
        var errores = new List<string>();
        if (telefono is not null && InterlocutorValidador.ValidarContacto("TELEFONO_MOVIL", telefono) is { } et) errores.Add(et);
        if (correo is not null && InterlocutorValidador.ValidarContacto("CORREO", correo) is { } ec) errores.Add(ec);

        DireccionDto? norm = null;
        if (direccion is null) errores.Add("Indique la direccion del prospecto.");
        else
        {
            var cat = await InterlocutorRepositorio.CargarCatalogosAsync(c, tx);
            var tipo = cat.Buscar(CatalogosInterlocutor.ListaTipoDireccion, direccion.IdTipoDireccion);
            if (tipo is null || tipo.Estado != "Activo") errores.Add("El tipo de direccion no existe o esta inactivo.");
            if (string.IsNullOrWhiteSpace(direccion.Detalle)) errores.Add("La direccion exacta (linea 1) es obligatoria.");
            var (errGeo, lista) = await DireccionGeografia.ValidarAsync(c, tx, new[] { direccion with { EsPrincipal = true } });
            errores.AddRange(errGeo);
            norm = lista.FirstOrDefault() is { } d ? d with { Id = null, EsPrincipal = true } : null;
        }
        return (errores.Count > 0 ? string.Join("\n", errores) : null, norm);
    }

    /// <summary>Null si el prospecto puede promoverse; si no, el motivo (no existe, no vigente, o ya tiene una promocion pendiente).</summary>
    public static async Task<string?> ValidarPromocionAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idProspecto)
    {
        var estado = (await c.QueryAsync<string>(
            "SELECT estado FROM cli.INTERLOCUTOR_PROSPECTO WHERE id_interlocutor_prospecto = @idProspecto AND id_compania = @idCompania",
            new { idProspecto, idCompania }, tx)).FirstOrDefault();
        if (estado is null) return "El prospecto no existe.";
        if (estado != "Prospecto") return $"El prospecto esta en estado {estado}; solo un prospecto vigente puede promoverse.";
        var pend = (await c.QueryAsync<string>(
            """
            SELECT TOP 1 g.numero FROM apr.GESTION g
            JOIN apr.GESTION_DETALLE d ON d.id_gestion = g.id_gestion AND d.nombre_campo = N'id_prospecto'
            WHERE g.id_compania = @idCompania AND g.estado = N'Pendiente de Aprobacion' AND d.valor_nuevo = CONVERT(nvarchar(30), @idProspecto)
            """, new { idCompania, idProspecto }, tx)).FirstOrDefault();
        return pend is null ? null : $"El prospecto ya tiene una promocion pendiente de aprobacion (gestion {pend}).";
    }

    /// <summary>Al aprobarse la promocion: el prospecto queda Convertido y enlazado al cliente creado.</summary>
    public static async Task MarcarConvertidoAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idProspecto, long idInterlocutor, long idGestion)
    {
        await c.ExecuteAsync(
            """
            UPDATE cli.INTERLOCUTOR_PROSPECTO SET estado = 'Convertido', id_interlocutor = @idInterlocutor, fecha_conversion = SYSUTCDATETIME(),
                   modificado_por = @por, modificado_en = SYSUTCDATETIME()
            WHERE id_interlocutor_prospecto = @idProspecto
            """, new { idInterlocutor, idProspecto, por = actor.NombreDescriptivo }, tx);
        await Auditoria.CambioAsync(c, tx, actor, Entidad, idProspecto, "estado", "Prospecto", "Convertido", idGestion);
    }

    // ----- Alta / cambios ---------------------------------------------------------------

    private static string NombreCompleto(string? n1, string? n2, string? a1, string? a2) =>
        string.Join(" ", new[] { n1, n2, a1, a2 }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

    private static string? ErrorPersona(string? n1, string? a1, DateTime? nac)
    {
        if (Vacio(n1) is null) return "Indique el primer nombre.";
        if (Vacio(a1) is null) return "Indique el primer apellido.";
        if (nac is not null && nac.Value.Date > DateTime.Today) return "La fecha de nacimiento no puede ser futura.";
        return null;
    }

    public async Task<ResultadoOperacion> CrearAsync(ActorContext actor, ProspectoCrearRequest req)
    {
        var errPersona = ErrorPersona(req.Nombre1, req.Apellido1, req.FechaNacimiento);
        if (errPersona is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", errPersona);
        var nombre = NombreCompleto(req.Nombre1, req.Nombre2, req.Apellido1, req.Apellido2);
        var numero = Vacio(req.NumeroIdentificacion);
        if ((numero is null) != (req.IdTipoIdentificacion is null))
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "Indique el tipo y el numero de identificacion, o deje ambos vacios.");
        var telefono = Vacio(req.Telefono);
        var correo = Vacio(req.Correo)?.ToLowerInvariant();

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            if (numero is not null)
            {
                var tipoOk = await c.ExecuteScalarAsync<bool>(
                    """
                    SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i
                        JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                        WHERE t.codigo = @lista AND i.id_lista_valores_item = @id AND i.estado = 'Activo') THEN 1 ELSE 0 END
                    """, new { lista = CatalogosInterlocutor.ListaTipoIdentificacion, id = req.IdTipoIdentificacion }, tx);
                if (!tipoOk) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El tipo de identificacion no existe o esta inactivo.");

                var v = await ValidarAsync(c, tx, actor.IdCompania, req.IdTipoIdentificacion!.Value, numero);
                if (v.EsCliente)
                    return ResultadoOperacion.Fallo(409, "ya_es_cliente",
                        $"La persona ya es cliente ({v.NumeroInterlocutor} - {v.Nombre}). No se puede crear como prospecto.");
                if (v.ExistePersona)
                    return ResultadoOperacion.Fallo(409, "persona_ya_registrada",
                        $"La persona ya esta registrada ({v.NumeroInterlocutor} - {v.Nombre}, roles: {string.Join(", ", v.Roles)}). " +
                        "No se crea como prospecto; si desea hacerla cliente use el alta de cliente.");
                if (v.EstadoProspecto == "Prospecto")
                    return ResultadoOperacion.Fallo(409, "prospecto_existente", $"Ya existe un prospecto con esa identificacion ({v.NombreProspecto}).");
            }

            var (errorDatos, dir) = await ValidarContactoYDireccionAsync(c, tx, telefono, correo, req.Direccion);
            if (errorDatos is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", errorDatos);

            // Sucursal heredada del usuario que registra (la unidad mas cercana que permite registros).
            long? idUnidad = actor.IdUsuario is { } u ? await InterlocutorRepositorio.UnidadRegistroPorDefectoAsync(c, tx, u) : null;
            var errorUnidad = await InterlocutorRepositorio.ValidarUnidadAsync(c, tx, actor.IdCompania, idUnidad, null);
            if (errorUnidad is not null)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", "Su usuario no tiene una sucursal asignada que permita registros; contacte al administrador.");

            var id = await c.ExecuteScalarAsync<long>(
                """
                INSERT INTO cli.INTERLOCUTOR_PROSPECTO (id_compania, tipo_persona, nombre_contacto, nombre1, nombre2, apellido1, apellido2, fecha_nacimiento,
                                                        id_tipo_identificacion, numero_identificacion, telefono_contacto, correo_contacto, canal_origen,
                                                        id_unidad, direccion_json, creado_por)
                OUTPUT INSERTED.id_interlocutor_prospecto
                VALUES (@idCompania, 'Fisica', @nombre, @n1, @n2, @a1, @a2, @nac, @tipo, @numero, @tel, @correo, @canal, @unidad, @dir, @por)
                """,
                new
                {
                    idCompania = actor.IdCompania, nombre, n1 = Vacio(req.Nombre1), n2 = Vacio(req.Nombre2), a1 = Vacio(req.Apellido1), a2 = Vacio(req.Apellido2),
                    nac = req.FechaNacimiento?.Date, tipo = req.IdTipoIdentificacion, numero, tel = telefono, correo, canal = Vacio(req.CanalOrigen),
                    unidad = idUnidad, dir = JsonSerializer.Serialize(dir, Json), por = actor.NombreDescriptivo
                }, tx);

            await Auditoria.AltaAsync(c, tx, actor, Entidad, id, $"Prospecto {nombre}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return ResultadoOperacion.Fallo(409, "prospecto_existente", "Ya existe un prospecto con esa identificacion.");
        }
    }

    public async Task<ResultadoOperacion> EditarAsync(ActorContext actor, long id, ProspectoEditarRequest req)
    {
        var errPersona = ErrorPersona(req.Nombre1, req.Apellido1, req.FechaNacimiento);
        if (errPersona is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", errPersona);
        var nombre = NombreCompleto(req.Nombre1, req.Nombre2, req.Apellido1, req.Apellido2);
        var telefono = Vacio(req.Telefono);
        var correo = Vacio(req.Correo)?.ToLowerInvariant();

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var ant = (await c.QueryAsync(
            """
            SELECT nombre_contacto, nombre1, nombre2, apellido1, apellido2, fecha_nacimiento, telefono_contacto, correo_contacto, canal_origen, estado, direccion_json
            FROM cli.INTERLOCUTOR_PROSPECTO WHERE id_interlocutor_prospecto = @id AND id_compania = @c
            """, new { id, c = actor.IdCompania }, tx)).FirstOrDefault();
        if (ant is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El prospecto no existe.");
        if ((string)ant.estado == "Convertido") return ResultadoOperacion.Fallo(409, "prospecto_convertido", "El prospecto ya fue convertido en cliente.");
        var errPromo = await ValidarPromocionAsync(c, tx, actor.IdCompania, id);
        if (errPromo is not null && errPromo.StartsWith("El prospecto ya tiene una promocion", StringComparison.Ordinal))
            return ResultadoOperacion.Fallo(409, "promocion_pendiente", errPromo + " No se puede modificar mientras tanto.");

        var (errorDatos, dir) = await ValidarContactoYDireccionAsync(c, tx, telefono, correo, req.Direccion);
        if (errorDatos is not null) return ResultadoOperacion.Fallo(400, "datos_invalidos", errorDatos);
        var dirNueva = JsonSerializer.Serialize(dir, Json);
        var dirAnterior = (string?)ant.direccion_json;

        var nuevos = new (string Campo, string? Antes, string? Despues)[]
        {
            ("nombre_contacto", (string?)ant.nombre_contacto, nombre),
            ("nombre1", (string?)ant.nombre1, Vacio(req.Nombre1)),
            ("nombre2", (string?)ant.nombre2, Vacio(req.Nombre2)),
            ("apellido1", (string?)ant.apellido1, Vacio(req.Apellido1)),
            ("apellido2", (string?)ant.apellido2, Vacio(req.Apellido2)),
            ("fecha_nacimiento", ((DateTime?)ant.fecha_nacimiento)?.ToString("yyyy-MM-dd"), req.FechaNacimiento?.ToString("yyyy-MM-dd")),
            ("telefono_contacto", (string?)ant.telefono_contacto, telefono),
            ("correo_contacto", (string?)ant.correo_contacto, correo),
            ("canal_origen", (string?)ant.canal_origen, Vacio(req.CanalOrigen)),
            ("direccion", ResumenDireccion(dirAnterior), ResumenDireccion(dirNueva)),
        };
        await c.ExecuteAsync(
            """
            UPDATE cli.INTERLOCUTOR_PROSPECTO SET nombre_contacto = @n, nombre1 = @n1, nombre2 = @n2, apellido1 = @a1, apellido2 = @a2, fecha_nacimiento = @nac,
                   telefono_contacto = @t, correo_contacto = @e, canal_origen = @o, direccion_json = @dir,
                   modificado_por = @por, modificado_en = SYSUTCDATETIME()
            WHERE id_interlocutor_prospecto = @id
            """,
            new
            {
                n = nombre, n1 = Vacio(req.Nombre1), n2 = Vacio(req.Nombre2), a1 = Vacio(req.Apellido1), a2 = Vacio(req.Apellido2),
                nac = req.FechaNacimiento?.Date, t = telefono, e = correo, o = Vacio(req.CanalOrigen), dir = dirNueva, por = actor.NombreDescriptivo, id
            }, tx);
        foreach (var x in nuevos.Where(x => x.Antes != x.Despues))
            await Auditoria.CambioAsync(c, tx, actor, Entidad, id, x.Campo, x.Antes, x.Despues);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }

    private static string? ResumenDireccion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var d = JsonSerializer.Deserialize<DireccionDto>(json, Json);
        if (d is null) return null;
        var niveles = string.Join(", ", (d.Niveles ?? []).Select(n => n.Texto).Where(t => !string.IsNullOrWhiteSpace(t)));
        return string.Join(" | ", new[] { $"tipo {d.IdTipoDireccion}", $"pais {d.IdPais}", niveles, d.CodigoPostal, d.Detalle, d.Detalle2, d.Detalle3 }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    /// <summary>Descartar o reactivar. Al reactivar se revalida que la persona no se haya vuelto cliente entre tanto.</summary>
    public async Task<ResultadoOperacion> CambiarEstadoAsync(ActorContext actor, long id, string estado)
    {
        if (estado is not ("Descartado" or "Prospecto"))
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "Estado invalido: use Descartado o Prospecto.");

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        var p = (await c.QueryAsync(
            "SELECT estado, id_tipo_identificacion AS tipo, numero_identificacion AS numero FROM cli.INTERLOCUTOR_PROSPECTO WHERE id_interlocutor_prospecto = @id AND id_compania = @c",
            new { id, c = actor.IdCompania }, tx)).FirstOrDefault();
        if (p is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El prospecto no existe.");
        var actual = (string)p.estado;
        if (actual == "Convertido") return ResultadoOperacion.Fallo(409, "prospecto_convertido", "El prospecto ya fue convertido en cliente.");
        if (actual == estado) return ResultadoOperacion.Ok(id);

        if (estado == "Descartado")
        {
            var errPromo = await ValidarPromocionAsync(c, tx, actor.IdCompania, id);
            if (errPromo is not null && errPromo.StartsWith("El prospecto ya tiene una promocion", StringComparison.Ordinal))
                return ResultadoOperacion.Fallo(409, "promocion_pendiente", errPromo + " No se puede descartar mientras tanto.");
        }

        if (estado == "Prospecto" && p.numero is not null)
        {
            var v = await ValidarAsync(c, tx, actor.IdCompania, (long)p.tipo, (string)p.numero);
            if (v.ExistePersona)
                return ResultadoOperacion.Fallo(409, v.EsCliente ? "ya_es_cliente" : "persona_ya_registrada", "La persona ya esta registrada como interlocutor; no se puede reactivar el prospecto.");
            if (v.EstadoProspecto == "Prospecto")
                return ResultadoOperacion.Fallo(409, "prospecto_existente", "Ya existe otro prospecto vigente con esa identificacion.");
        }

        try
        {
            await c.ExecuteAsync(
                "UPDATE cli.INTERLOCUTOR_PROSPECTO SET estado = @e, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor_prospecto = @id",
                new { e = estado, por = actor.NombreDescriptivo, id }, tx);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return ResultadoOperacion.Fallo(409, "prospecto_existente", "Ya existe otro prospecto vigente con esa identificacion.");
        }
        await Auditoria.CambioAsync(c, tx, actor, Entidad, id, "estado", actual, estado);
        tx.Commit();
        return ResultadoOperacion.Ok(id);
    }
}
