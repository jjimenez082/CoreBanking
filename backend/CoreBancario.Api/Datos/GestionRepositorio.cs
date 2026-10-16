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
    private readonly NumeracionServicio _numeracion;

    public GestionRepositorio(TenantContext tenantContext, NumeracionServicio numeracion)
    {
        _tenantContext = tenantContext;
        _numeracion = numeracion;
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
                g.numero           AS numero,
                gor.numero          AS numero_origen,
                CASE WHEN g.entidad_negocio = 'INTERLOCUTOR_RELACION' THEN CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') THEN 'Inactivación de relación de interlocutor' WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') THEN 'Modificación de relación de interlocutor' WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') THEN 'Creación de relación de interlocutor' ELSE tg.nombre END ELSE tg.nombre END AS tipo_gestion,
                g.estado           AS estado,
                g.entidad_negocio  AS entidad_negocio,
                CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     WHEN g.entidad_negocio IN ('INTERLOCUTOR', 'INTERLOCUTOR_ALTA', 'INTERLOCUTOR_FUSION', 'INTERLOCUTOR_ROL', 'INTERLOCUTOR_RELACION', 'INTERLOCUTOR_KYC', 'INTERLOCUTOR_BLOQUEO', 'INTERLOCUTOR_CONSENTIMIENTO', 'INTERLOCUTOR_DOCUMENTO', 'INTERLOCUTOR_FALLECIMIENTO') AND g.llave_registro NOT LIKE 'ALTA-%'
                     THEN ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), g.llave_registro)
                     WHEN g.llave_registro LIKE 'ALTA-%'
                     THEN 'Nuevo: ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'razon_social'), LTRIM(ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'nombre1'), '') + ' ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'apellido1'), '')))
                     ELSE ISNULL(g.llave_registro, '') END AS llave_registro,
                u.nombre_completo  AS solicitado_por,
                g.id_usuario_solicitante AS id_solicitante,
                u.id_superior      AS id_superior,
                g.creado_en        AS creado_en,
                g.fecha_resolucion AS fecha_resolucion,
                g.id_gestion_origen, g.id_gestion_correccion, g.numero_correccion,
                ua.nombre_completo AS resuelta_por,
                CASE a.estado WHEN 'Aprobado' THEN 'Aprobó' WHEN 'Rechazado' THEN 'Rechazó' WHEN 'Devuelto' THEN 'Devolvió' END AS resolucion_tipo,
                g.nivel_actual, g.niveles_requeridos,
                ac.tipo_aprobador, ac.id_usuario_asignado, tg.permite_mismo_usuario_multiples_niveles AS permite_repetir,
                CASE ac.tipo_aprobador WHEN 'GRUPO' THEN 'Grupo ' + gr.nombre ELSE uas.nombre_completo END AS pendiente_de,
                CASE WHEN ac.id_grupo_asignado IS NOT NULL AND EXISTS (SELECT 1 FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = ac.id_grupo_asignado AND m.id_usuario = @idUsuario) THEN 1 ELSE 0 END AS miembro,
                CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION_APROBACION p WHERE p.id_gestion = g.id_gestion AND p.estado = 'Aprobado' AND p.id_usuario_aprobador = @idUsuario) THEN 1 ELSE 0 END AS ya_aprobo
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            LEFT JOIN apr.GESTION gor ON gor.id_gestion = g.id_gestion_origen
            OUTER APPLY (SELECT TOP 1 x.estado, x.id_usuario_aprobador FROM apr.GESTION_APROBACION x
                         WHERE x.id_gestion = g.id_gestion AND x.estado IN ('Aprobado','Rechazado','Devuelto') AND g.estado <> 'Pendiente de Aprobacion'
                         ORDER BY x.nivel DESC) a
            LEFT JOIN sec.USUARIO ua ON ua.id_usuario = a.id_usuario_aprobador
            LEFT JOIN apr.GESTION_APROBACION ac ON ac.id_gestion = g.id_gestion AND ac.nivel = g.nivel_actual AND g.estado = 'Pendiente de Aprobacion'
            LEFT JOIN sec.USUARIO uas ON uas.id_usuario = ac.id_usuario_asignado
            LEFT JOIN sec.GRUPO_USUARIOS gr ON gr.id_grupo = ac.id_grupo_asignado
            WHERE g.id_compania = @idCompania
              AND (@soloPendientes = 0 OR g.estado = 'Pendiente de Aprobacion'
                   OR (g.estado = 'Devuelta' AND g.id_usuario_solicitante = @idUsuario))
            ORDER BY g.fecha_solicitud DESC
            """;

        var filas = await conexion.QueryAsync(sql, new { idCompania = actor.IdCompania, soloPendientes = soloPendientes ? 1 : 0, idUsuario = actor.IdUsuario });
        return filas.Select(f =>
        {
            var esMia = !actor.EsIntegracion && (long)f.id_solicitante == actor.IdUsuario;
            return new GestionResponse(
                (long)f.id_gestion, (string)f.tipo_gestion, (string)f.estado, (string)f.entidad_negocio, (string)f.llave_registro,
                (string)f.solicitado_por, (DateTime)f.creado_en, esMia,
                (string)f.estado == EstadoPendiente && MotorAprobacion.Puede(actor, esMia, permitirAuto, porSuperior, (string?)f.tipo_aprobador,
                    (long?)f.id_usuario_asignado, (int)f.miembro == 1, (int)f.ya_aprobo == 1, (bool)f.permite_repetir),
                (DateTime?)f.fecha_resolucion,
                (long?)f.id_gestion_origen, (long?)f.id_gestion_correccion, (int)f.numero_correccion,
                (string?)f.resuelta_por, (string?)f.resolucion_tipo,
                (int)f.nivel_actual, (int)f.niveles_requeridos, (string?)f.pendiente_de, (string?)f.numero, (string?)f.numero_origen);
        }).Cast<GestionResponse>().ToList();
    }

    public async Task<GestionDetalleResponse?> ObtenerDetalleAsync(ActorContext actor, long idGestion, bool permitirAuto = false, bool porSuperior = true)
    {
        var idCompania = actor.IdCompania;
        using var conexion = NuevaConexion();

        const string sql = """
            SELECT g.id_gestion, g.numero, gor.numero AS numero_origen, gc.numero AS numero_correccion_ref,
                   CASE WHEN g.entidad_negocio = 'INTERLOCUTOR_RELACION' THEN CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') THEN 'Inactivación de relación de interlocutor' WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') THEN 'Modificación de relación de interlocutor' WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') THEN 'Creación de relación de interlocutor' ELSE tg.nombre END ELSE tg.nombre END AS tipo_gestion, g.estado, g.entidad_negocio,
                   CASE WHEN g.entidad_negocio = 'TIPO_CAMBIO'
                     THEN ISNULL((SELECT m.codigo FROM gen.MONEDA m WHERE CONVERT(nvarchar(30), m.id_moneda) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), '?') + ' ' + SUBSTRING(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') + 1, 20)
                     WHEN g.entidad_negocio IN ('INTERLOCUTOR', 'INTERLOCUTOR_ALTA', 'INTERLOCUTOR_FUSION', 'INTERLOCUTOR_ROL', 'INTERLOCUTOR_RELACION', 'INTERLOCUTOR_KYC', 'INTERLOCUTOR_BLOQUEO', 'INTERLOCUTOR_CONSENTIMIENTO', 'INTERLOCUTOR_DOCUMENTO', 'INTERLOCUTOR_FALLECIMIENTO') AND g.llave_registro NOT LIKE 'ALTA-%'
                     THEN ISNULL((SELECT i.numero FROM cli.INTERLOCUTOR i WHERE i.id_compania = g.id_compania AND CONVERT(nvarchar(100), i.id_interlocutor) = LEFT(g.llave_registro, CHARINDEX('|', g.llave_registro + '|') - 1)), g.llave_registro)
                     WHEN g.llave_registro LIKE 'ALTA-%'
                     THEN 'Nuevo: ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'razon_social'), LTRIM(ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'nombre1'), '') + ' ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'apellido1'), '')))
                     WHEN g.llave_registro LIKE 'ALTA-%'
                     THEN 'Nuevo: ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'razon_social'), LTRIM(ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'nombre1'), '') + ' ' + ISNULL((SELECT d.valor_nuevo FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion AND d.nombre_campo = 'apellido1'), '')))
                     ELSE ISNULL(g.llave_registro, '') END AS llave_registro, u.nombre_completo AS solicitado_por,
                   g.comentario, g.motivo_rechazo, g.creado_en, g.fecha_resolucion,
                   g.llave_registro AS llave_cruda, g.id_usuario_solicitante AS id_solicitante, u.id_superior,
                   g.comentario_devolucion, g.devuelto_por, g.fecha_devolucion,
                   g.id_gestion_origen, g.id_gestion_correccion, g.numero_correccion,
                   ua.nombre_completo AS resuelta_por, a.fecha_resolucion AS resuelta_en,
                   CASE a.estado WHEN 'Aprobado' THEN 'Aprobó' WHEN 'Rechazado' THEN 'Rechazó' WHEN 'Devuelto' THEN 'Devolvió' END AS resolucion_tipo,
                   g.nivel_actual, g.niveles_requeridos, rg.nombre AS regla,
                   ac.tipo_aprobador, ac.id_usuario_asignado, tg.permite_mismo_usuario_multiples_niveles AS permite_repetir,
                   CASE ac.tipo_aprobador WHEN 'GRUPO' THEN 'Grupo ' + gr.nombre ELSE uas.nombre_completo END AS pendiente_de,
                   CASE WHEN ac.id_grupo_asignado IS NOT NULL AND EXISTS (SELECT 1 FROM sec.GRUPO_USUARIOS_MIEMBRO m WHERE m.id_grupo = ac.id_grupo_asignado AND m.id_usuario = @idUsuario) THEN 1 ELSE 0 END AS miembro,
                   CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION_APROBACION p WHERE p.id_gestion = g.id_gestion AND p.estado = 'Aprobado' AND p.id_usuario_aprobador = @idUsuario) THEN 1 ELSE 0 END AS ya_aprobo
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            LEFT JOIN apr.REGLA_APROBACION rg ON rg.id_regla = g.id_regla
            LEFT JOIN apr.GESTION gor ON gor.id_gestion = g.id_gestion_origen
            LEFT JOIN apr.GESTION gc ON gc.id_gestion = g.id_gestion_correccion
            OUTER APPLY (SELECT TOP 1 x.estado, x.id_usuario_aprobador, x.fecha_resolucion FROM apr.GESTION_APROBACION x
                         WHERE x.id_gestion = g.id_gestion AND x.estado IN ('Aprobado','Rechazado','Devuelto') AND g.estado <> 'Pendiente de Aprobacion'
                         ORDER BY x.nivel DESC) a
            LEFT JOIN sec.USUARIO ua ON ua.id_usuario = a.id_usuario_aprobador
            LEFT JOIN apr.GESTION_APROBACION ac ON ac.id_gestion = g.id_gestion AND ac.nivel = g.nivel_actual AND g.estado = 'Pendiente de Aprobacion'
            LEFT JOIN sec.USUARIO uas ON uas.id_usuario = ac.id_usuario_asignado
            LEFT JOIN sec.GRUPO_USUARIOS gr ON gr.id_grupo = ac.id_grupo_asignado
            WHERE g.id_gestion = @idGestion AND g.id_compania = @idCompania
            """;
        var g = await conexion.QuerySingleOrDefaultAsync(sql, new { idGestion, idCompania, idUsuario = actor.IdUsuario });
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
        var monedas = (await conexion.QueryAsync("SELECT id_moneda AS id, codigo + ' · ' + nombre AS nombre FROM gen.MONEDA")).ToDictionary(x => (long)x.id, x => (string)x.nombre);

        var esMia = !actor.EsIntegracion && (long)g.id_solicitante == actor.IdUsuario;
        var niveles = (await conexion.QueryAsync(
            """
            SELECT a.nivel, a.tipo_aprobador, a.estado, a.comentario, a.fecha_resolucion,
                   CASE a.tipo_aprobador WHEN 'GRUPO' THEN 'Grupo ' + gr.nombre ELSE ua.nombre_completo END AS asignado,
                   ap.nombre_completo AS aprobador
            FROM apr.GESTION_APROBACION a
            LEFT JOIN sec.USUARIO ua ON ua.id_usuario = a.id_usuario_asignado
            LEFT JOIN sec.GRUPO_USUARIOS gr ON gr.id_grupo = a.id_grupo_asignado
            LEFT JOIN sec.USUARIO ap ON ap.id_usuario = a.id_usuario_aprobador
            WHERE a.id_gestion = @idGestion ORDER BY a.nivel
            """, new { idGestion })).Select(n => new GestionNivelResponse(
                (int)(short)n.nivel, (string?)n.tipo_aprobador ?? "SUPERIOR", (string?)n.asignado, (string)n.estado,
                (string?)n.aprobador, (DateTime?)n.fecha_resolucion, (string?)n.comentario)).ToList();
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
                TextoCampo(campo, (string?)d.valor_anterior, items, paises, divisiones, unidades, monedas),
                TextoCampo(campo, (string?)d.valor_nuevo, items, paises, divisiones, unidades, monedas),
                (string?)d.valor_anterior, (string?)d.valor_nuevo);
        }).Cast<GestionCampoResponse>().ToList();

        // Perfil de datos del actor: los valores sensibles de la gestion se muestran enmascarados.
        if (actor.IdUsuario is { } idUsuarioPerfil)
        {
            var perfilActor = await PerfilDatosServicio.CargarAsync(conexion, null, idCompania, idUsuarioPerfil);
            campos = EnmascaradoClientes.AplicarGestion(campos, perfilActor).ToList();
        }

        return new GestionDetalleResponse(
            (long)g.id_gestion, (string)g.tipo_gestion, (string)g.estado, (string)g.entidad_negocio,
            (string)g.llave_registro, (string)g.solicitado_por, (string?)g.comentario, (string?)g.motivo_rechazo,
            (DateTime)g.creado_en, (DateTime?)g.fecha_resolucion, campos,
            (string)g.llave_cruda, esMia,
            (string)g.estado == EstadoPendiente && MotorAprobacion.Puede(actor, esMia, permitirAuto, porSuperior, (string?)g.tipo_aprobador,
                (long?)g.id_usuario_asignado, (int)g.miembro == 1, (int)g.ya_aprobo == 1, (bool)g.permite_repetir),
            (string?)g.comentario_devolucion, (string?)g.devuelto_por, (DateTime?)g.fecha_devolucion,
            (long?)g.id_gestion_origen, (long?)g.id_gestion_correccion, (int)g.numero_correccion,
            (string?)g.resuelta_por, (string?)g.resolucion_tipo, (DateTime?)g.resuelta_en,
            (int)g.nivel_actual, (int)g.niveles_requeridos, (string?)g.pendiente_de, (string?)g.regla, niveles,
            (string?)g.numero, (string?)g.numero_origen, (string?)g.numero_correccion_ref);
    }

    // ----- Resolver: aprobar (aplica el cambio) / rechazar ---------------------

    public async Task<ResultadoOperacion> AprobarAsync(ActorContext actor, long idGestion, bool permitirAutoaprobacion, bool aprobacionPorSuperior = true, string? comentario = null)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede aprobar una gestion.");
        comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        if (comentario is { Length: > 500 })
            return ResultadoOperacion.Fallo(400, "comentario_largo", "El comentario no puede superar 500 caracteres.");

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

            var (noAutorizado, nivel) = await MotorAprobacion.VerificarAsync(conexion, tx, actor, idGestion, (long)g.id_usuario_solicitante, aprobacionPorSuperior, permitirAutoaprobacion);
            if (noAutorizado is not null) return noAutorizado;

            if ((string)g.entidad_negocio == EntidadAlta && await MotorCoincidencias.AprobacionBloqueadaAsync(conexion, tx, actor, idGestion))
                return ResultadoOperacion.Fallo(403, "duplicado_fuerte_bloqueado",
                    "Esta alta llego por API con una coincidencia fuerte; solo un usuario con permiso para autorizar duplicados puede aprobarla.");

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION_APROBACION
                   SET estado = 'Aprobado', id_usuario_aprobador = @idUsuario, fecha_resolucion = SYSUTCDATETIME(), comentario = @comentario
                 WHERE id_gestion = @idGestion AND nivel = @nivel
                """, new { idGestion, idUsuario = actor.IdUsuario, nivel, comentario }, tx);

            var nombreRegla = await conexion.ExecuteScalarAsync<string?>(
                "SELECT r.nombre FROM apr.GESTION g JOIN apr.REGLA_APROBACION r ON r.id_regla = g.id_regla WHERE g.id_gestion = @idGestion", new { idGestion }, tx);
            var terminado = await MotorAprobacion.AvanzarAsync(conexion, tx, idGestion, nivel, actor.IdUsuario, (long)g.id_usuario_solicitante, nombreRegla);
            if (!terminado)
            {
                // Pasa al siguiente nivel: el cambio sigue sin aplicarse.
                await conexion.ExecuteAsync(
                    "UPDATE apr.GESTION SET modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_gestion = @idGestion",
                    new { idGestion, por = actor.NombreDescriptivo }, tx);
                tx.Commit();
                return ResultadoOperacion.Ok(idGestion);
            }

            var error = await AplicarYCerrarAsync(conexion, tx, actor, idGestion, (string)g.entidad_negocio, (string)g.llave_registro, (string)g.creado_por, actor.NombreDescriptivo, SiguienteNumeroDe(actor));
            if (error is not null) { tx.Rollback(); return error; }

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
        if (actor.EsIntegracion)
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

            var (noAutorizado, nivel) = await MotorAprobacion.VerificarAsync(conexion, tx, actor, idGestion, (long)g.id_usuario_solicitante, aprobacionPorSuperior, permitirAutoaprobacion);
            if (noAutorizado is not null) return noAutorizado;

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION_APROBACION
                   SET estado = 'Rechazado', id_usuario_aprobador = @idUsuario, comentario = @motivo, fecha_resolucion = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion AND nivel = @nivel
                """, new { idGestion, nivel, idUsuario = actor.IdUsuario, motivo = motivo.Trim() }, tx);
            await OmitirNivelesPendientesAsync(conexion, tx, idGestion, nivel);

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION
                   SET estado = 'Rechazada', motivo_rechazo = @motivo, fecha_resolucion = SYSUTCDATETIME(),
                       modificado_por = @por, modificado_en = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion
                """, new { idGestion, motivo = motivo.Trim(), por = actor.NombreDescriptivo }, tx);

            await DocumentoRepositorio.DescartarDeGestionAsync(conexion, tx, actor, idGestion);

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
        if (actor.EsIntegracion)
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
                "SELECT estado, id_usuario_solicitante, entidad_negocio FROM apr.GESTION WITH (UPDLOCK, ROWLOCK) WHERE id_gestion = @idGestion AND id_compania = @idCompania",
                new { idGestion, actor.IdCompania }, tx);
            if (g is null) return ResultadoOperacion.Fallo(404, "gestion_no_encontrada", "No existe la gestion.");
            if ((string)g.estado != EstadoPendiente)
                return ResultadoOperacion.Fallo(409, "estado_invalido", $"La gestion esta '{(string)g.estado}', no pendiente de aprobacion.");
            if ((string)g.entidad_negocio is EntidadDocumento or EntidadFallecimiento)
                return ResultadoOperacion.Fallo(409, "devolucion_no_soportada", "Esta gestión solo se aprueba o se rechaza; si hay que corregir algo, rechácela y el solicitante vuelve a solicitarla."); 
            if ((long)g.id_usuario_solicitante == actor.IdUsuario && !permitirAutoaprobacion)
                return ResultadoOperacion.Fallo(409, "autoaprobacion_no_permitida", "No puede devolverse a si mismo una gestion propia.");

            var (noAutorizado, nivel) = await MotorAprobacion.VerificarAsync(conexion, tx, actor, idGestion, (long)g.id_usuario_solicitante, aprobacionPorSuperior, permitirAutoaprobacion);
            if (noAutorizado is not null) return noAutorizado;

            await conexion.ExecuteAsync(
                """
                UPDATE apr.GESTION_APROBACION
                   SET estado = 'Devuelto', id_usuario_aprobador = @idUsuario, comentario = @comentario, fecha_resolucion = SYSUTCDATETIME()
                 WHERE id_gestion = @idGestion AND nivel = @nivel
                """, new { idGestion, nivel, idUsuario = actor.IdUsuario, comentario = comentario.Trim() }, tx);
            await OmitirNivelesPendientesAsync(conexion, tx, idGestion, nivel);

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

    private static Task OmitirNivelesPendientesAsync(SqlConnection c, SqlTransaction tx, long idGestion, short nivel)
        => c.ExecuteAsync(
            "UPDATE apr.GESTION_APROBACION SET estado = 'Omitido' WHERE id_gestion = @idGestion AND nivel > @nivel AND estado = 'Pendiente'",
            new { idGestion, nivel }, tx);

    /// <summary>Aplica el cambio de la gestion segun su entidad y la deja 'Aplicada'.</summary>
    private static async Task<ResultadoOperacion?> AplicarYCerrarAsync(
        SqlConnection conexion, SqlTransaction tx, ActorContext actor, long idGestion, string entidad, string llave, string creadoPor, string resueltaPor,
        Func<string, Task<string>>? siguienteNumero = null)
    {
        ResultadoOperacion? error;
        switch (entidad)
        {
            case EntidadInterlocutor:
                error = await AplicarCambiosInterlocutorAsync(conexion, tx, actor, idGestion, long.Parse(llave, CultureInfo.InvariantCulture), creadoPor);
                break;
            case EntidadKyc:
            {
                var idCliente = long.Parse(llave, CultureInfo.InvariantCulture);
                var previo = await KycVigenciaServicio.LeerAsync(conexion, tx, idCliente);
                // La proxima revision se calcula AQUI, al aprobarse, con los datos ya aplicados (se libera la fecha antes para no chocar con el check ultima <= proxima).
                await conexion.ExecuteAsync("UPDATE cli.INTERLOCUTOR_KYC SET fecha_proxima_revision = NULL WHERE id_interlocutor = @idCliente", new { idCliente }, tx);
                error = await AplicarCambiosInterlocutorAsync(conexion, tx, actor, idGestion, idCliente, creadoPor);
                if (error is null)
                    await KycVigenciaServicio.CalcularYGuardarProximaAsync(conexion, tx, actor.IdCompania, idCliente, previo?.Ultima, previo?.Proxima);
                if (error is null)
                    await KycVigenciaServicio.ActualizarVigenciaAsync(conexion, tx, actor.IdCompania, idCliente, KycVigenciaServicio.OrigenGestion, idGestion, creadoPor, previo?.Vigencia, previo?.Proxima);
                break;
            }
            case EntidadBloqueo:
                error = await BloqueoServicio.AplicarAsync(conexion, tx, actor, idGestion, llave, creadoPor);
                break;
            case EntidadConsentimiento:
                error = await ConsentimientoServicio.AplicarAsync(conexion, tx, actor, idGestion, llave, creadoPor);
                break;
            case EntidadTipoCambio:
                error = await AplicarTipoCambioAsync(conexion, tx, actor, idGestion);
                break;
            case EntidadAlta:
                error = await AplicarAltaInterlocutorAsync(conexion, tx, actor, idGestion, creadoPor, siguienteNumero);
                break;
            case EntidadFusion:
                error = await FusionServicio.AplicarAsync(conexion, tx, actor, idGestion, resueltaPor);
                break;
            case EntidadRol:
                error = await RolServicio.AplicarAsync(conexion, tx, actor, idGestion, long.Parse(llave, CultureInfo.InvariantCulture), resueltaPor);
                break;
            case EntidadRelacion:
                error = await RelacionServicio.AplicarAsync(conexion, tx, actor, idGestion, long.Parse(llave, CultureInfo.InvariantCulture), resueltaPor);
                break;
            case EntidadFallecimiento:
                error = await FallecimientoServicio.AplicarAsync(conexion, tx, actor, idGestion, llave, resueltaPor);
                break;
            case EntidadDocumento:
                error = await DocumentoGestionServicio.AplicarAsync(conexion, tx, actor, idGestion, long.Parse(llave, CultureInfo.InvariantCulture), resueltaPor);
                break;
            default:
                return ResultadoOperacion.Fallo(501, "entidad_no_soportada", $"La entidad '{entidad}' aun no tiene aplicador de cambios.");
        }
        if (error is not null) return error;

        // Adjuntos (r44a): la evidencia de la gestion pasa a ser documento vigente del cliente.
        await DocumentoRepositorio.PromoverAsync(conexion, tx, actor, idGestion, entidad, resueltaPor);

        await conexion.ExecuteAsync(
            """
            UPDATE apr.GESTION
               SET estado = 'Aplicada', fecha_resolucion = SYSUTCDATETIME(), fecha_efectiva = SYSUTCDATETIME(),
                   modificado_por = @por, modificado_en = SYSUTCDATETIME()
             WHERE id_gestion = @idGestion
            """, new { idGestion, por = resueltaPor }, tx);
        return null;
    }

    /// <summary>
    /// Gestion cuyos niveles son todos automaticos: se aplica de inmediato dentro de la misma transaccion del alta de la solicitud.
    /// Queda en auditoria como aprobada por el sistema, con la regla aplicada.
    /// </summary>
    internal static async Task<ResultadoOperacion?> AplicarAutomaticaAsync(
        SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string? nombreRegla, Func<string, Task<string>>? siguienteNumero = null)
    {
        var g = await c.QuerySingleAsync(
            "SELECT entidad_negocio, llave_registro, creado_por FROM apr.GESTION WHERE id_gestion = @idGestion", new { idGestion }, tx);
        var por = $"Sistema (regla: {nombreRegla ?? "predeterminada"})";
        var error = await AplicarYCerrarAsync(c, tx, actor, idGestion, (string)g.entidad_negocio, (string)g.llave_registro, (string)g.creado_por, por, siguienteNumero);
        if (error is not null) return error;
        await Auditoria.CambioAsync(c, tx, actor, "GESTION", idGestion, "estado", EstadoPendiente,
            $"Aprobada y aplicada automaticamente por el sistema (regla: {nombreRegla ?? "predeterminada"})", idGestion);
        return null;
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
            "SELECT estado, id_usuario_solicitante, entidad_negocio, llave_registro, numero_correccion FROM apr.GESTION WITH (UPDLOCK, ROWLOCK) WHERE id_gestion = @id AND id_compania = @c",
            new { id = idDevuelta, c = actor.IdCompania }, tx);
        if (d is null || (string)d.estado != "Devuelta" || (long)d.id_usuario_solicitante != actor.IdUsuario
            || (string)d.entidad_negocio != entidad || (entidad != EntidadAlta && (string)d.llave_registro != llave))
            return ResultadoOperacion.Fallo(409, "devolucion_invalida", $"La gestion #{idDevuelta} ya no esta devuelta para su correccion.");
        // La vieja queda 'Corregida' y apunta a la que la corrige; la nueva apunta a la que corrige y suma una correccion.
        await c.ExecuteAsync(
            "UPDATE apr.GESTION SET estado = 'Corregida', id_gestion_correccion = @n, fecha_resolucion = SYSUTCDATETIME(), modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_gestion = @id",
            new { id = idDevuelta, n = idNueva, por = actor.NombreDescriptivo }, tx);
        await c.ExecuteAsync(
            "UPDATE apr.GESTION SET id_gestion_origen = @id, numero_correccion = @num WHERE id_gestion = @n",
            new { id = idDevuelta, n = idNueva, num = (int)d.numero_correccion + 1 }, tx);
        await DocumentoRepositorio.TrasladarCorreccionAsync(c, tx, idDevuelta.Value, idNueva);
        return null;
    }
}
