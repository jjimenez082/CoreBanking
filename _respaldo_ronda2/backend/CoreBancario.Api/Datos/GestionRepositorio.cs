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

    public async Task<IReadOnlyList<GestionResponse>> ListarPendientesAsync(long idCompania)
        => await ListarAsync(idCompania, soloPendientes: true);

    /// <summary>Gestiones de la compania (ultimas 200). soloPendientes = solo las que esperan aprobacion.</summary>
    public async Task<IReadOnlyList<GestionResponse>> ListarAsync(long idCompania, bool soloPendientes)
    {
        using var conexion = NuevaConexion();

        const string sql = """
            SELECT TOP (200)
                g.id_gestion       AS IdGestion,
                tg.nombre          AS TipoGestion,
                g.estado           AS Estado,
                g.entidad_negocio  AS EntidadNegocio,
                CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     ELSE ISNULL(g.llave_registro, '') END AS LlaveRegistro,
                u.nombre_completo  AS SolicitadoPor,
                g.creado_en        AS CreadoEn
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            WHERE g.id_compania = @idCompania
              AND (@soloPendientes = 0 OR g.estado = 'Pendiente de Aprobacion')
            ORDER BY g.fecha_solicitud DESC
            """;

        var filas = await conexion.QueryAsync<GestionResponse>(sql, new { idCompania, soloPendientes = soloPendientes ? 1 : 0 });
        return filas.ToList();
    }

    public async Task<GestionDetalleResponse?> ObtenerDetalleAsync(long idCompania, long idGestion)
    {
        using var conexion = NuevaConexion();

        const string sql = """
            SELECT g.id_gestion, tg.nombre AS tipo_gestion, g.estado, g.entidad_negocio,
                   CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     ELSE ISNULL(g.llave_registro, '') END AS llave_registro, u.nombre_completo AS solicitado_por,
                   g.comentario, g.motivo_rechazo, g.creado_en, g.fecha_resolucion
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

        var campos = detalle.Select(d =>
        {
            var campo = (string)d.nombre_campo;
            if (tipoCambio)
            {
                string? Mon(string? v) => v is not null && campo == "id_moneda" && long.TryParse(v, out var im) && monedas.TryGetValue(im, out var nm) ? nm : v;
                return new GestionCampoResponse(campo, EtiquetasTipoCambio.GetValueOrDefault(campo, campo), Mon((string?)d.valor_anterior), Mon((string?)d.valor_nuevo));
            }
            return new GestionCampoResponse(
                campo, EtiquetaCampo(campo),
                TextoCampo(campo, (string?)d.valor_anterior, items, paises, divisiones, unidades),
                TextoCampo(campo, (string?)d.valor_nuevo, items, paises, divisiones, unidades));
        }).ToList();

        return new GestionDetalleResponse(
            (long)g.id_gestion, (string)g.tipo_gestion, (string)g.estado, (string)g.entidad_negocio,
            (string)g.llave_registro, (string)g.solicitado_por, (string?)g.comentario, (string?)g.motivo_rechazo,
            (DateTime)g.creado_en, (DateTime?)g.fecha_resolucion, campos);
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
}
