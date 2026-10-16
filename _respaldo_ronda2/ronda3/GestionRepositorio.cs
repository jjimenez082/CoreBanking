using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Motor minimo de Gestiones (apr.GESTION): bandeja, detalle, creacion de la
/// gestion de modificacion de clientes, y aprobacion/rechazo.
///
/// Principio del sistema: los cambios a datos de negocio NO se aplican
/// directo; se solicitan como Gestion y se aplican al aprobarse. MVP: un solo
/// nivel de aprobacion, aprobador = quien tenga GES_APROBAR y no sea el mismo
/// solicitante (salvo Gestiones:PermitirAutoaprobacion, solo para pruebas).
/// El aprobador por tipo (usuario, superior jerarquico, grupo) queda como
/// evolucion del motor.
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoModificacionCliente = "CLI_MODIFICACION_DATOS";
    public const string EntidadInterlocutor = "INTERLOCUTOR";
    public const string EstadoPendiente = "Pendiente de Aprobacion";

    private readonly TenantContext _tenantContext;

    public GestionRepositorio(TenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    private SqlConnection NuevaConexion() => new(_tenantContext.ConnectionString);

    private static string Norm(string? v) => (v ?? string.Empty).Trim();

    // ----- Bandeja / detalle -------------------------------------------------

    public async Task<IReadOnlyList<GestionResponse>> ListarPendientesAsync(ActorContext actor, bool permitirAuto = false, bool porSuperior = true)
        => await ListarAsync(actor, soloPendientes: true, permitirAuto, porSuperior);

    /// <summary>
    /// Gestiones de la compania (ultimas 300). soloPendientes = las que esperan aprobacion, mas las propias devueltas
    /// para correccion. PuedeResolver indica si el usuario puede aprobar/rechazar/devolver cada una.
    /// </summary>
    public async Task<IReadOnlyList<GestionResponse>> ListarAsync(ActorContext actor, bool soloPendientes, bool permitirAuto = false, bool porSuperior = true)
    {
        using var conexion = NuevaConexion();

        const string sql = """
            SELECT TOP (300)
                g.id_gestion       AS id_gestion,
                tg.nombre          AS tipo_gestion,
                g.estado           AS estado,
                g.entidad_negocio  AS entidad_negocio,
                CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     ELSE ISNULL(g.llave_registro, '') END AS llave_registro,
                u.nombre_completo  AS solicitado_por,
                g.id_usuario_solicitante AS id_solicitante,
                u.id_superior      AS id_superior,
                g.creado_en        AS creado_en,
                g.fecha_resolucion AS fecha_resolucion
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            WHERE g.id_compania = @idCompania
              AND (@soloPendientes = 0 OR g.estado = 'Pendiente de Aprobacion'
                   OR (g.estado = 'Devuelta' AND g.id_usuario_solicitante = @idUsuario))
            ORDER BY g.fecha_solicitud DESC
            """;

        var filas = await conexion.QueryAsync(sql, new { idCompania = actor.IdCompania, soloPendientes = soloPendientes ? 1 : 0, idUsuario = actor.IdUsuario });
        return filas.Select(f =>
        {
            var esMia = actor.IdUsuario is not null && (long)f.id_solicitante == actor.IdUsuario;
            return new GestionResponse(
                (long)f.id_gestion, (string)f.tipo_gestion, (string)f.estado, (string)f.entidad_negocio, (string)f.llave_registro,
                (string)f.solicitado_por, (DateTime)f.creado_en, esMia,
                CalcularPuedeResolver((string)f.estado, actor, esMia, (long?)f.id_superior, permitirAuto, porSuperior),
                (DateTime?)f.fecha_resolucion);
        }).Cast<GestionResponse>().ToList();
    }

    /// <summary>Misma regla que usan aprobar/rechazar/devolver, para que la pantalla solo ofrezca lo que si se puede hacer.</summary>
    private static bool CalcularPuedeResolver(string estado, ActorContext actor, bool esMia, long? idSuperior, bool permitirAuto, bool porSuperior)
    {
        if (estado != EstadoPendiente || actor.IdUsuario is null || !actor.TienePermiso("GES_APROBAR")) return false;
        if (esMia) return permitirAuto;
        if (!porSuperior || actor.TienePermiso("GES_APROBAR_TODAS")) return true;
        return idSuperior is null || idSuperior == actor.IdUsuario;
    }

    public async Task<GestionDetalleResponse?> ObtenerDetalleAsync(ActorContext actor, long idGestion, bool permitirAuto = false, bool porSuperior = true)
    {
        var idCompania = actor.IdCompania;
        using var conexion = NuevaConexion();

        const string sql = """
            SELECT g.id_gestion, tg.nombre AS tipo_gestion, g.estado, g.entidad_negocio,
                   CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     ELSE ISNULL(g.llave_registro, '') END AS llave_registro, u.nombre_completo AS solicitado_por,
                   g.comentario, g.motivo_rechazo, g.creado_en, g.fecha_resolucion,
                   g.llave_registro AS llave_cruda, g.id_usuario_solicitante AS id_solicitante, u.id_superior,
                   g.comentario_devolucion, g.devuelto_por, g.fecha_devolucion
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            WHERE g.id_gestion = @idGestion AND g.id_compania = @idCompania
            """;
        var g = await conexion.QuerySingleOrDefaultAsync(sql, new { idGestion, idCompania });
        if (g is null) return null;

        var detalle = (await conexion.QueryAsync(
            "SELECT nombre_campo, valor_anterior, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion ORDER BY id_gestion_detalle",
            new { idGestion })).ToList();

        // Los campos que son FK se guardan como id; aqui se muestran con su descripcion.
        var items = (await conexion.QueryAsync(
            "SELECT id_lista_valores_item AS id, nombre FROM gen.LISTA_VALORES_ITEM")).ToDictionary(x => (long)x.id, x => (string)x.nombre);
        var paises = (await conexion.QueryAsync(
            "SELECT id_pais_regulador AS id, nombre_pais AS nombre FROM gen.PAIS_REGULADOR")).ToDictionary(x => (long)x.id, x => (string)x.nombre);

        var divisiones = (await conexion.QueryAsync(
            "SELECT id_division AS id, nombre FROM gen.DIVISION_TERRITORIAL")).ToDictionary(x => (long)x.id, x => (string)x.nombre);

        var unidades = (await conexion.QueryAsync(
            "SELECT id_departamento AS id, nombre FROM sec.DEPARTAMENTO")).ToDictionary(x => (long)x.id, x => (string)x.nombre);

        var tipoCambio = (string)g.entidad_negocio == EntidadTipoCambio;
        var monedas = tipoCambio
            ? (await conexion.QueryAsync("SELECT id_moneda AS id, codigo + ' · ' + nombre AS nombre FROM gen.MONEDA")).ToDictionary(x => (long)x.id, x => (string)x.nombre)
            : new Dictionary<long, string>();

        var esMia = actor.IdUsuario is not null && (long)g.id_solicitante == actor.IdUsuario;
        var campos = detalle.Select(d =>
        {
            var campo = (string)d.nombre_campo;
            if (tipoCambio)
            {
                string? Mon(string? v) => v is not null && campo == "id_moneda" && long.TryParse(v, out var im) && monedas.TryGetValue(im, out var nm) ? nm : v;
                return new GestionCampoResponse(campo, EtiquetasTipoCambio.GetValueOrDefault(campo, campo), Mon((string?)d.valor_anterior), Mon((string?)d.valor_nuevo), (string?)d.valor_anterior, (string?)d.valor_nuevo);
            }
            return new GestionCampoResponse(
                campo, EtiquetaCampo(campo),
                TextoCampo(campo, (string?)d.valor_anterior, items, paises, divisiones, unidades),
                TextoCampo(campo, (string?)d.valor_nuevo, items, paises, divisiones, unidades),
                (string?)d.valor_anterior, (string?)d.valor_nuevo);
        }).Cast<GestionCampoResponse>().ToList();

        return new GestionDetalleResponse(
            (long)g.id_gestion, (string)g.tipo_gestion, (string)g.estado, (string)g.entidad_negocio,
            (string)g.llave_registro, (string)g.solicitado_por, (string?)g.comentario, (string?)g.motivo_rechazo,
            (DateTime)g.creado_en, (DateTime?)g.fecha_resolucion, campos,
            (string)g.llave_cruda, esMia, CalcularPuedeResolver((string)g.estado, actor, esMia, (long?)g.id_superior, permitirAuto, porSuperior),
            (string?)g.comentario_devolucion, (string?)g.devuelto_por, (DateTime?)g.fecha_devolucion);
    }

    // ----- Quien puede resolver una gestion -------------------------------------------------

    /// <summary>
    /// Si el solicitante tiene superior directo, solo ese superior (o quien tenga GES_APROBAR_TODAS) puede aprobar o rechazar.
    /// Sin superior, basta GES_APROBAR. Se puede desactivar con Gestiones:AprobacionPorSuperior=false.
    /// </summary>
    private static async Task<ResultadoOperacion?> VerificarAprobadorAsync(
        SqlConnection conexion, SqlTransaction tx, ActorContext actor, long idSolicitante, bool porSuperior, bool permitirAutoaprobacion)
    {
        if (!porSuperior || actor.TienePermiso("GES_APROBAR_TODAS")) return null;
        if (permitirAutoaprobacion && idSolicitante == actor.IdUsuario) return null;
        var sup = await conexion.QuerySingleOrDefaultAsync(
            "SELECT s.id_usuario, s.nombre_completo FROM sec.USUARIO u JOIN sec.USUARIO s ON s.id_usuario = u.id_superior WHERE u.id_usuario = @id",
            new { id = idSolicitante }, tx);
        if (sup is null || (long)sup.id_usuario == actor.IdUsuario) return null;
        return ResultadoOperacion.Fallo(403, "aprobador_no_autorizado",
            $"Esta gestion debe resolverla el superior directo del solicitante ({(string)sup.nombre_completo}) o un usuario con permiso para aprobar cualquier gestion.");
    }

    // ----- Resolver: aprobar (aplica el cambio) / rechazar ---------------------

    public async Task<ResultadoOperacion> AprobarAsync(ActorContext actor, long idGestion, bool permitirAutoaprobacion, bool aprobacionPorSuperior = true)
    {
        if (actor.IdUsuario is null)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede aprobar una gestion.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var g = await conexion.QuerySingleOrDefaultAsync(
                """
                SELECT id_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, creado_por
                FROM apr.GESTION WITH (UPDLOCK, ROWLOCK)
                WHERE id_gestion = @idGestion AND id_compania = @idCompania
                """, new { idGestion, actor.IdCompania }, tx);
            if (g is null) return ResultadoOperacion.Fallo(404, "gestion_no_encontrada", "No existe la gestion.");
            if ((string)g.estado != EstadoPendiente)
                return ResultadoOperacion.Fallo(409, "estado_invalido", $"La gestion esta '{(string)g.estado}', no pendiente de aprobacion.");
            if (!permitirAutoaprobacion && (long)g.id_usuario_solicitante == actor.IdUsuario)
                return ResultadoOperacion.Fallo(409, "autoaprobacion_no_permitida", "Quien solicita el cambio no puede aprobarlo; debe aprobarlo otro usuario.");

            var noAutorizado = await VerificarAprobadorAsync(conexion, tx, actor, (long)g.id_usuario_solicitante, aprobacionPorSuperior, permitirAutoaprobacion);
            if (noAutorizado is not null) return noAutorizado;

            ResultadoOperacion? error;
            switch ((string)g.entidad_negocio)
            {
                case EntidadInterlocutor:
                    error = await AplicarCambiosInterlocutorAsync(conexion, tx, actor, idGestion, long.Parse((string)g.llave_registro, CultureInfo.InvariantCulture), (string)g.creado_por);
                    break;
                case EntidadTipoCambio:
                    error = await AplicarTipoCambioAsync(conexion, tx, actor, idGestion);
                    break;
                default:
                    return ResultadoOperacion.Fallo(501, "entidad_no_soportada", $"La entidad '{(string)g.entidad_negocio}' aun no tiene aplicador de cambios.");
            }
            if (error is not null) { tx.Rollback(); return error; }

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION_APROBACION
                   SET estado = 'Aprobado', id_usuario_aprobador = @idUsuario, fecha_resolucion = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion AND nivel = 1
                """, new { idGestion, idUsuario = actor.IdUsuario }, tx);

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION
                   SET estado = 'Aplicada', fecha_resolucion = SYSUTCDATETIME(), fecha_efectiva = SYSUTCDATETIME(),
                       modificado_por = @por, modificado_en = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion
                """, new { idGestion, por = actor.NombreDescriptivo }, tx);

            tx.Commit();
            return ResultadoOperacion.Ok(idGestion);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "identificacion_duplicada", "Ya existe otro cliente con esa identificacion; no se puede aplicar el cambio.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<ResultadoOperacion> RechazarAsync(ActorContext actor, long idGestion, string motivo, bool aprobacionPorSuperior = true, bool permitirAutoaprobacion = false)
    {
        if (actor.IdUsuario is null)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede rechazar una gestion.");
        if (string.IsNullOrWhiteSpace(motivo))
            return ResultadoOperacion.Fallo(400, "motivo_requerido", "El motivo del rechazo es obligatorio.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var g = await conexion.QuerySingleOrDefaultAsync(
                "SELECT estado, id_usuario_solicitante FROM apr.GESTION WITH (UPDLOCK, ROWLOCK) WHERE id_gestion = @idGestion AND id_compania = @idCompania",
                new { idGestion, actor.IdCompania }, tx);
            if (g is null) return ResultadoOperacion.Fallo(404, "gestion_no_encontrada", "No existe la gestion.");
            if ((string)g.estado != EstadoPendiente)
                return ResultadoOperacion.Fallo(409, "estado_invalido", $"La gestion esta '{(string)g.estado}', no pendiente de aprobacion.");

            var noAutorizado = await VerificarAprobadorAsync(conexion, tx, actor, (long)g.id_usuario_solicitante, aprobacionPorSuperior, permitirAutoaprobacion);
            if (noAutorizado is not null) return noAutorizado;

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION_APROBACION
                   SET estado = 'Rechazado', id_usuario_aprobador = @idUsuario, comentario = @motivo, fecha_resolucion = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion AND nivel = 1
                """, new { idGestion, idUsuario = actor.IdUsuario, motivo = motivo.Trim() }, tx);

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION
                   SET estado = 'Rechazada', motivo_rechazo = @motivo, fecha_resolucion = SYSUTCDATETIME(),
                       modificado_por = @por, modificado_en = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion
                """, new { idGestion, motivo = motivo.Trim(), por = actor.NombreDescriptivo }, tx);

            tx.Commit();
            return ResultadoOperacion.Ok(idGestion);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<ResultadoOperacion> DevolverAsync(ActorContext actor, long idGestion, string comentario, bool aprobacionPorSuperior = true, bool permitirAutoaprobacion = false)
    {
        if (actor.IdUsuario is null)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede devolver una gestion.");
        if (string.IsNullOrWhiteSpace(comentario))
            return ResultadoOperacion.Fallo(400, "comentario_requerido", "Indique que debe corregir o aclarar el solicitante.");
        if (comentario.Trim().Length > 1000)
            return ResultadoOperacion.Fallo(400, "comentario_largo", "El comentario admite hasta 1000 caracteres.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var g = await conexion.QuerySingleOrDefaultAsync(
                "SELECT estado, id_usuario_solicitante FROM apr.GESTION WITH (UPDLOCK, ROWLOCK) WHERE id_gestion = @idGestion AND id_compania = @idCompania",
                new { idGestion, actor.IdCompania }, tx);
            if (g is null) return ResultadoOperacion.Fallo(404, "gestion_no_encontrada", "No existe la gestion.");
            if ((string)g.estado != EstadoPendiente)
                return ResultadoOperacion.Fallo(409, "estado_invalido", $"La gestion esta '{(string)g.estado}', no pendiente de aprobacion.");
            if ((long)g.id_usuario_solicitante == actor.IdUsuario && !permitirAutoaprobacion)
                return ResultadoOperacion.Fallo(409, "autoaprobacion_no_permitida", "No puede devolverse a si mismo una gestion propia.");

            var noAutorizado = await VerificarAprobadorAsync(conexion, tx, actor, (long)g.id_usuario_solicitante, aprobacionPorSuperior, permitirAutoaprobacion);
            if (noAutorizado is not null) return noAutorizado;

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION_APROBACION
                   SET estado = 'Devuelto', id_usuario_aprobador = @idUsuario, comentario = @comentario, fecha_resolucion = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion AND nivel = 1
                """, new { idGestion, idUsuario = actor.IdUsuario, comentario = comentario.Trim() }, tx);

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION
                   SET estado = 'Devuelta', comentario_devolucion = @comentario, devuelto_por = @por, fecha_devolucion = SYSUTCDATETIME(),
                       modificado_por = @por, modificado_en = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion
                """, new { idGestion, comentario = comentario.Trim(), por = actor.NombreDescriptivo }, tx);

            tx.Commit();
            return ResultadoOperacion.Ok(idGestion);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Al reenviar una correccion se cierra la gestion devuelta ('Corregida'): debe ser del mismo solicitante,
    /// seguir devuelta y referirse al mismo registro.
    /// </summary>
    internal static async Task<ResultadoOperacion?> CerrarDevueltaAsync(
        SqlConnection c, SqlTransaction tx, ActorContext actor, long? idDevuelta, long idNueva, string entidad, string llave)
    {
        if (idDevuelta is null) return null;
        var d = await c.QuerySingleOrDefaultAsync(
            "SELECT estado, id_usuario_solicitante, entidad_negocio, llave_registro FROM apr.GESTION WITH (UPDLOCK, ROWLOCK) WHERE id_gestion = @id AND id_compania = @c",
            new { id = idDevuelta, c = actor.IdCompania }, tx);
        if (d is null || (string)d.estado != "Devuelta" || (long)d.id_usuario_solicitante != actor.IdUsuario
            || (string)d.entidad_negocio != entidad || (string)d.llave_registro != llave)
            return ResultadoOperacion.Fallo(409, "devolucion_invalida", $"La gestion #{idDevuelta} ya no esta devuelta para su correccion.");
        await c.ExecuteAsync(
            "UPDATE apr.GESTION SET estado = 'Corregida', fecha_resolucion = SYSUTCDATETIME(), comentario = CONCAT(ISNULL(comentario + N' | ', N''), N'Corregida en la gestion #', @n), modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_gestion = @id",
            new { id = idDevuelta, n = idNueva, por = actor.NombreDescriptivo }, tx);
        return null;
    }
}
