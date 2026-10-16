using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Acceso a datos del Interlocutor (cli.INTERLOCUTOR y sus hijos 1:n:
/// identificaciones, direcciones, contactos, roles) y de los borradores del
/// wizard de alta, contra la base de datos del tenant ya resuelta en
/// TenantContext. Alcance actual: Persona Fisica.
///
/// Los cambios a un cliente YA creado NO pasan por aqui: se solicitan como
/// Gestion (ver GestionRepositorio.Interlocutor.cs). Aqui solo se crea.
/// </summary>
public class InterlocutorRepositorio
{
    private readonly TenantContext _tenantContext;
    private readonly IConfiguration _configuracion;
    private readonly NumeracionServicio _numeracion;

    public InterlocutorRepositorio(TenantContext tenantContext, IConfiguration configuracion, NumeracionServicio numeracion)
    {
        _tenantContext = tenantContext;
        _configuracion = configuracion;
        _numeracion = numeracion;
    }

    private SqlConnection NuevaConexion() => new(_tenantContext.ConnectionString);

    private static DateOnly? A(object? valor) => valor is DateTime d ? DateOnly.FromDateTime(d) : null;
    private static DateTime? D(DateOnly? valor) => valor?.ToDateTime(TimeOnly.MinValue);

    /// <summary>Escapa los comodines de LIKE para que el texto del usuario se busque literal.</summary>
    private static string? PatronLike(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        return $"%{t}%";
    }

    // ----- Catalogos para validar -----------------------------------------------

    public static async Task<CatalogosInterlocutor> CargarCatalogosAsync(SqlConnection conexion, SqlTransaction? tx = null)
    {
        var filas = await conexion.QueryAsync(
            """
            SELECT t.codigo AS lista, i.id_lista_valores_item AS id, i.codigo, i.estado
            FROM gen.LISTA_VALORES_ITEM i
            JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo IN @listas
            """, new { listas = CatalogosInterlocutor.Listas }, tx);

        var datos = CatalogosInterlocutor.Listas.ToDictionary(l => l, _ => new Dictionary<long, CatItem>());
        foreach (var f in filas)
            datos[(string)f.lista][(long)f.id] = new CatItem((string)f.codigo, (string)f.estado);
        return new CatalogosInterlocutor(datos);
    }

    /// <summary>
    /// Valida la unidad (sucursal) del cliente. `actual` = la que ya tiene (se acepta aunque se haya inactivado).
    /// Es obligatoria cuando la compania tiene al menos una unidad activa.
    /// </summary>
    public static async Task<string?> ValidarUnidadAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long? idUnidad, long? actual)
    {
        if (idUnidad is null)
        {
            var hay = await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE id_compania = @c AND estado = 'Activo') THEN 1 ELSE 0 END", new { c = idCompania }, tx);
            return hay && actual is null ? "Indique la unidad (sucursal) a la que pertenece el cliente." : null;
        }
        if (idUnidad == actual) return null;
        var ok = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE id_departamento = @u AND id_compania = @c AND estado = 'Activo') THEN 1 ELSE 0 END",
            new { u = idUnidad, c = idCompania }, tx);
        return ok ? null : "La unidad no existe o esta inactiva.";
    }

    public const string ListaCanal = "CANAL";

    /// <summary>Id del canal (item activo de la lista CANAL) por su codigo, o null.</summary>
    public static Task<long?> CanalPorCodigoAsync(SqlConnection c, SqlTransaction? tx, string codigo)
        => c.ExecuteScalarAsync<long?>(
            """
            SELECT i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i
            JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = @lista AND i.codigo = @codigo AND i.estado = 'Activo'
            """, new { lista = ListaCanal, codigo }, tx);

    /// <summary>Valida que el canal sea un valor activo de la lista CANAL (si no cambia respecto al actual, se acepta).</summary>
    public static async Task<string?> ValidarCanalAsync(SqlConnection c, SqlTransaction? tx, long? idCanal, long? actual)
    {
        if (idCanal is null) return actual is null ? "Indique el canal de origen del cliente." : null;
        if (idCanal == actual) return null;
        var ok = await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM i
                JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE t.codigo = @lista AND i.id_lista_valores_item = @id AND i.estado = 'Activo') THEN 1 ELSE 0 END
            """, new { lista = ListaCanal, id = idCanal }, tx);
        return ok ? null : "El canal no existe o esta inactivo.";
    }

    /// <summary>Numero del cliente que ya tiene esa identificacion activa (excluyendo a otro), o null.</summary>
    public static async Task<string?> BuscarDuplicadoAsync(
        SqlConnection conexion, SqlTransaction? tx, long idCompania, long idTipo, string numero, long? excluirInterlocutor)
    {
        return await conexion.ExecuteScalarAsync<string?>(
            """
            SELECT TOP 1 i.numero
            FROM cli.INTERLOCUTOR_IDENTIFICACION x
            JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = x.id_interlocutor
            WHERE x.id_compania = @idCompania AND x.estado = 'Activo'
              AND x.id_tipo_identificacion = @idTipo AND x.numero_identificacion = @numero
              AND (@excluir IS NULL OR x.id_interlocutor <> @excluir)
            """, new { idCompania, idTipo, numero = numero.Trim(), excluir = excluirInterlocutor }, tx);
    }

    // ----- Alta ----------------------------------------------------------------

    public async Task<ResultadoOperacion> AltaAsync(ActorContext actor, InterlocutorAltaRequest req)
    {
        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var cat = await CargarCatalogosAsync(conexion, tx);
            var errores = InterlocutorValidador.Validar(
                req.Nombre1, req.Apellido1, req.FechaNacimiento, req.Identificaciones, req.Direcciones, req.Contactos, cat);

            var paisOk = await conexion.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { p = req.IdPaisRegulador }, tx);
            if (!paisOk) errores.Add("El pais regulador no existe o esta inactivo.");

            var (erroresGeo, direcciones) = await DireccionGeografia.ValidarAsync(conexion, tx, req.Direcciones);
            errores.AddRange(erroresGeo);

            // Unidad (sucursal): por defecto la del usuario que da el alta; obligatoria si la compania ya tiene unidades activas.
            var idUnidad = req.IdUnidad;
            if (idUnidad is null && actor.IdUsuario is not null)
                idUnidad = await conexion.ExecuteScalarAsync<long?>(
                    "SELECT u.id_departamento FROM sec.USUARIO u JOIN sec.DEPARTAMENTO d ON d.id_departamento = u.id_departamento AND d.estado = 'Activo' WHERE u.id_usuario = @u",
                    new { u = actor.IdUsuario }, tx);
            var errorUnidad = await ValidarUnidadAsync(conexion, tx, actor.IdCompania, idUnidad, null);
            if (errorUnidad is not null) errores.Add(errorUnidad);

            // Canal de origen: una integracion (API) lo tiene fijo y lo asigna el servidor; en el Backoffice se elige (por defecto Sucursal).
            long? idCanal;
            if (actor.IdUsuarioIntegracion is not null)
            {
                idCanal = await conexion.ExecuteScalarAsync<long?>(
                    "SELECT id_canal FROM sec.USUARIO_INTEGRACION WHERE id_usuario_integracion = @i", new { i = actor.IdUsuarioIntegracion }, tx);
                if (idCanal is null) errores.Add("El usuario de integracion no tiene un canal asignado; asignelo en Administracion > Usuarios de integracion.");
            }
            else
            {
                idCanal = req.IdCanalOrigen ?? await CanalPorCodigoAsync(conexion, tx, "SUCURSAL");
                var errorCanal = await ValidarCanalAsync(conexion, tx, idCanal, null);
                if (errorCanal is not null) errores.Add(errorCanal);
            }

            if (errores.Count > 0)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

            foreach (var i in req.Identificaciones)
            {
                var dup = await BuscarDuplicadoAsync(conexion, tx, actor.IdCompania, i.IdTipoIdentificacion, i.Numero, null);
                if (dup is not null)
                    return ResultadoOperacion.Fallo(409, "identificacion_duplicada",
                        $"La identificacion {i.Numero.Trim()} ya pertenece al cliente N.º {dup}.");
            }

            var idRolCliente = cat.IdPorCodigo(CatalogosInterlocutor.ListaTipoRol, "CLIENTE");
            if (idRolCliente is null)
                return ResultadoOperacion.Fallo(500, "rol_no_configurado", "Falta el valor CLIENTE en la lista TIPO_ROL_INTERLOCUTOR; correr interlocutor_multiples.sql.");

            var por = actor.NombreDescriptivo;

            // El numero de cliente se asigna AL FINAL, justo antes de crear el registro definitivo
            // (en conexion propia: el rango nunca queda bloqueado por esta transaccion).
            string numero;
            try
            {
                numero = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "CLIENTE");
            }
            catch (NumeracionException ex)
            {
                tx.Rollback();
                return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message);
            }

            var idInterlocutor = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO cli.INTERLOCUTOR (id_compania, numero, tipo_persona, id_pais_regulador, id_unidad, id_canal_origen, creado_por)
                OUTPUT INSERTED.id_interlocutor
                VALUES (@idCompania, @numero, 'Fisica', @idPais, @idUnidad, @idCanal, @por)
                """, new { idCompania = actor.IdCompania, numero, idPais = req.IdPaisRegulador, idUnidad, idCanal, por }, tx);

            await conexion.ExecuteAsync(
                """
                INSERT INTO cli.INTERLOCUTOR_FISICA (id_interlocutor, nombre1, nombre2, apellido1, apellido2, fecha_nacimiento)
                VALUES (@idInterlocutor, @n1, @n2, @a1, @a2, @fn)
                """,
                new
                {
                    idInterlocutor, n1 = req.Nombre1.Trim(),
                    n2 = string.IsNullOrWhiteSpace(req.Nombre2) ? null : req.Nombre2.Trim(),
                    a1 = req.Apellido1.Trim(),
                    a2 = string.IsNullOrWhiteSpace(req.Apellido2) ? null : req.Apellido2.Trim(),
                    fn = D(req.FechaNacimiento)
                }, tx);

            foreach (var i in req.Identificaciones)
            {
                await conexion.ExecuteAsync(
                    """
                    INSERT INTO cli.INTERLOCUTOR_IDENTIFICACION
                        (id_interlocutor, id_compania, id_tipo_identificacion, numero_identificacion, id_institucion_emisora,
                         fecha_emision, fecha_vencimiento, es_principal, creado_por)
                    VALUES (@idInterlocutor, @idCompania, @tipo, @numero, @inst, @emision, @venc, @principal, @por)
                    """,
                    new
                    {
                        idInterlocutor, idCompania = actor.IdCompania, tipo = i.IdTipoIdentificacion, numero = i.Numero.Trim(),
                        inst = i.IdInstitucionEmisora, emision = D(i.FechaEmision), venc = D(i.FechaVencimiento),
                        principal = i.EsPrincipal, por
                    }, tx);
            }

            foreach (var d in direcciones)
            {
                await conexion.ExecuteAsync(
                    """
                    INSERT INTO cli.INTERLOCUTOR_DIRECCION
                        (id_interlocutor, id_tipo_direccion, id_pais,
                         nivel1_id, nivel2_id, nivel3_id, nivel4_id, nivel1_texto, nivel2_texto, nivel3_texto, nivel4_texto,
                         codigo_postal, detalle, es_principal, creado_por)
                    VALUES (@idInterlocutor, @tipo, @pais, @n1i, @n2i, @n3i, @n4i, @n1t, @n2t, @n3t, @n4t,
                            @cp, @detalle, @principal, @por)
                    """,
                    new
                    {
                        idInterlocutor, tipo = d.IdTipoDireccion, pais = d.IdPais,
                        n1i = d.Niveles![0].Id, n2i = d.Niveles[1].Id, n3i = d.Niveles[2].Id, n4i = d.Niveles[3].Id,
                        n1t = d.Niveles[0].Texto, n2t = d.Niveles[1].Texto, n3t = d.Niveles[2].Texto, n4t = d.Niveles[3].Texto,
                        cp = d.CodigoPostal, detalle = d.Detalle.Trim(), principal = d.EsPrincipal, por
                    }, tx);
            }

            foreach (var c in req.Contactos ?? [])
            {
                var codigo = cat.Buscar(CatalogosInterlocutor.ListaTipoContacto, c.IdTipoContacto)!.Codigo;
                await conexion.ExecuteAsync(
                    """
                    INSERT INTO cli.INTERLOCUTOR_CONTACTO (id_interlocutor, id_tipo_contacto, valor, es_principal, creado_por)
                    VALUES (@idInterlocutor, @tipo, @valor, @principal, @por)
                    """,
                    new
                    {
                        idInterlocutor, tipo = c.IdTipoContacto,
                        valor = InterlocutorValidador.NormalizarContacto(codigo, c.Valor), principal = c.EsPrincipal, por
                    }, tx);
            }

            await conexion.ExecuteAsync(
                "INSERT INTO cli.INTERLOCUTOR_ROL (id_interlocutor, id_tipo_rol, creado_por) VALUES (@idInterlocutor, @rol, @por)",
                new { idInterlocutor, rol = idRolCliente, por }, tx);

            if (req.IdBorrador is not null && actor.IdUsuario is not null)
            {
                await conexion.ExecuteAsync(
                    "DELETE FROM cli.INTERLOCUTOR_BORRADOR WHERE id_borrador = @id AND id_usuario = @u AND id_compania = @c",
                    new { id = req.IdBorrador, u = actor.IdUsuario, c = actor.IdCompania }, tx);
            }

            tx.Commit();
            return ResultadoOperacion.Ok(idInterlocutor);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "identificacion_duplicada", "Alguna identificacion ya pertenece a otro cliente.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static string? Vacio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ----- Listado y busqueda ------------------------------------------------------

    private const string CondicionBusqueda = """
        AND (@like IS NULL
             OR CONCAT_WS(' ', f.nombre1, f.nombre2, f.apellido1, f.apellido2) LIKE @like
             OR i.numero LIKE @like
             OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_IDENTIFICACION x
                         WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo'
                           AND x.numero_identificacion LIKE @like))
        """;

    private const string SqlRoles = """
        (SELECT STRING_AGG(it.nombre, N' · ')
           FROM cli.INTERLOCUTOR_ROL r
           JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
          WHERE r.id_interlocutor = i.id_interlocutor AND r.estado = 'Activo')
        """;

    /// <summary>Listado paginado; busca por nombre, N.º de cliente o cualquiera de sus identificaciones.</summary>
    public async Task<(IReadOnlyList<InterlocutorResponse> Items, int Total)> ListarAsync(
        long idCompania, string? texto, int pagina, int tamanoPagina)
    {
        using var conexion = NuevaConexion();
        var offset = (pagina - 1) * tamanoPagina;
        var like = PatronLike(texto);

        var total = await conexion.ExecuteScalarAsync<int>($"""
            SELECT COUNT(*)
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania
            {CondicionBusqueda}
            """, new { idCompania, like });

        var filas = await conexion.QueryAsync($"""
            SELECT
                i.id_interlocutor AS IdInterlocutor, i.numero AS Numero, i.tipo_persona AS TipoPersona, i.estado AS Estado, i.creado_en AS CreadoEn,
                f.nombre1 AS Nombre1, f.nombre2 AS Nombre2, f.apellido1 AS Apellido1, f.apellido2 AS Apellido2,
                p.id_tipo_identificacion AS IdTipoIdentificacion, p.numero_identificacion AS NumeroIdentificacion,
                {SqlRoles} AS Roles
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            OUTER APPLY (SELECT TOP 1 x.id_tipo_identificacion, x.numero_identificacion
                           FROM cli.INTERLOCUTOR_IDENTIFICACION x
                          WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo'
                          ORDER BY x.es_principal DESC, x.id_identificacion) p
            WHERE i.id_compania = @idCompania
            {CondicionBusqueda}
            ORDER BY i.id_interlocutor DESC
            OFFSET @offset ROWS FETCH NEXT @tamanoPagina ROWS ONLY;
            """, new { idCompania, like, offset, tamanoPagina });

        var items = filas.Select(f => new InterlocutorResponse(
            (long)f.IdInterlocutor, (string)f.Numero, (string)f.TipoPersona,
            (long?)f.IdTipoIdentificacion ?? 0L, (string?)f.NumeroIdentificacion ?? string.Empty,
            NombreCompleto((string?)f.Nombre1, (string?)f.Nombre2, (string?)f.Apellido1, (string?)f.Apellido2),
            (string?)f.Roles ?? string.Empty, (string)f.Estado, (DateTime)f.CreadoEn)).ToList();

        return (items, total);
    }

    /// <summary>Busqueda global (barra superior): hasta 8 coincidencias, sin importar el rol.</summary>
    public async Task<IReadOnlyList<InterlocutorBusquedaResponse>> BuscarAsync(long idCompania, string? texto)
    {
        var like = PatronLike(texto);
        if (like is null || texto!.Trim().Length < 2) return [];

        using var conexion = NuevaConexion();
        var filas = await conexion.QueryAsync($"""
            SELECT TOP (8)
                i.id_interlocutor AS IdInterlocutor, i.numero AS Numero, i.estado AS Estado,
                f.nombre1 AS Nombre1, f.nombre2 AS Nombre2, f.apellido1 AS Apellido1, f.apellido2 AS Apellido2,
                {SqlRoles} AS Roles,
                (SELECT STRING_AGG(CONCAT(t.nombre, N' ', x.numero_identificacion), N' · ')
                   FROM cli.INTERLOCUTOR_IDENTIFICACION x
                   JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_identificacion
                  WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo') AS Identificaciones
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania
            {CondicionBusqueda}
            ORDER BY i.id_interlocutor DESC
            """, new { idCompania, like });

        return filas.Select(f => new InterlocutorBusquedaResponse(
            (long)f.IdInterlocutor, (string)f.Numero,
            NombreCompleto((string?)f.Nombre1, (string?)f.Nombre2, (string?)f.Apellido1, (string?)f.Apellido2),
            (string?)f.Roles ?? string.Empty, (string?)f.Identificaciones ?? string.Empty, (string)f.Estado)).ToList();
    }

    private static string NombreCompleto(string? n1, string? n2, string? a1, string? a2) =>
        string.Join(" ", new[] { n1, n2, a1, a2 }.Where(p => !string.IsNullOrWhiteSpace(p)));

    // ----- Ficha completa -------------------------------------------------------------

    /// <summary>Ficha con roles y colecciones 1:n (solo registros activos), para consulta y para editar.</summary>
    public async Task<InterlocutorDetalleResponse?> ObtenerDetalleAsync(long idCompania, long idInterlocutor)
    {
        using var conexion = NuevaConexion();

        var r = await conexion.QuerySingleOrDefaultAsync(
            """
            SELECT
                i.id_interlocutor AS IdInterlocutor, i.numero AS Numero, i.tipo_persona AS TipoPersona, i.estado AS Estado,
                i.id_pais_regulador AS IdPaisRegulador, p.nombre_pais AS PaisReguladorNombre,
                i.id_unidad AS IdUnidad, un.nombre AS UnidadNombre,
                i.id_canal_origen AS IdCanalOrigen, cn.nombre AS CanalOrigenNombre,
                f.nombre1 AS Nombre1, f.nombre2 AS Nombre2, f.apellido1 AS Apellido1, f.apellido2 AS Apellido2,
                f.fecha_nacimiento AS FechaNacimiento,
                i.creado_en AS CreadoEn, i.creado_por AS CreadoPor,
                i.modificado_en AS ModificadoEn, i.modificado_por AS ModificadoPor,
                (SELECT TOP 1 g.id_gestion FROM apr.GESTION g
                  WHERE g.entidad_negocio = 'INTERLOCUTOR'
                    AND g.llave_registro = CONVERT(nvarchar(100), i.id_interlocutor)
                    AND g.estado = 'Pendiente de Aprobacion') AS IdGestionPendiente
            FROM cli.INTERLOCUTOR i
            JOIN gen.PAIS_REGULADOR p ON p.id_pais_regulador = i.id_pais_regulador
            LEFT JOIN sec.DEPARTAMENTO un ON un.id_departamento = i.id_unidad
            LEFT JOIN gen.LISTA_VALORES_ITEM cn ON cn.id_lista_valores_item = i.id_canal_origen
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania
            """, new { idInterlocutor, idCompania });
        if (r is null) return null;

        var roles = (await conexion.QueryAsync(
            """
            SELECT r.id_tipo_rol, it.codigo, it.nombre, r.estado
            FROM cli.INTERLOCUTOR_ROL r JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
            WHERE r.id_interlocutor = @idInterlocutor AND r.estado = 'Activo' ORDER BY it.orden, it.nombre
            """, new { idInterlocutor }))
            .Select(x => new RolInterlocutorResponse((long)x.id_tipo_rol, (string)x.codigo, (string)x.nombre, (string)x.estado)).ToList();

        var idents = (await conexion.QueryAsync(
            """
            SELECT x.id_identificacion, x.id_tipo_identificacion, t.nombre AS tipo_nombre, x.numero_identificacion,
                   x.id_institucion_emisora, ins.nombre AS institucion_nombre, x.fecha_emision, x.fecha_vencimiento,
                   x.es_principal, x.creado_por, x.creado_en, x.modificado_por, x.modificado_en
            FROM cli.INTERLOCUTOR_IDENTIFICACION x
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_identificacion
            LEFT JOIN gen.LISTA_VALORES_ITEM ins ON ins.id_lista_valores_item = x.id_institucion_emisora
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo'
            ORDER BY x.es_principal DESC, x.id_identificacion
            """, new { idInterlocutor }))
            .Select(x => new IdentificacionResponse(
                (long)x.id_identificacion, (long)x.id_tipo_identificacion, (string)x.tipo_nombre, (string)x.numero_identificacion,
                (long?)x.id_institucion_emisora, (string?)x.institucion_nombre, A(x.fecha_emision), A(x.fecha_vencimiento),
                (bool)x.es_principal, (string)x.creado_por, (DateTime)x.creado_en, (string?)x.modificado_por, (DateTime?)x.modificado_en)).ToList();

        var dirFilas = (await conexion.QueryAsync(
            """
            SELECT x.id_direccion, x.id_tipo_direccion, t.nombre AS tipo_nombre, x.id_pais,
                   x.nivel1_id, x.nivel2_id, x.nivel3_id, x.nivel4_id,
                   x.nivel1_texto, x.nivel2_texto, x.nivel3_texto, x.nivel4_texto,
                   d1.nombre AS nivel1_nombre, d2.nombre AS nivel2_nombre, d3.nombre AS nivel3_nombre, d4.nombre AS nivel4_nombre,
                   x.codigo_postal, x.detalle,
                   x.es_principal, x.creado_por, x.creado_en, x.modificado_por, x.modificado_en
            FROM cli.INTERLOCUTOR_DIRECCION x
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_direccion
            LEFT JOIN gen.DIVISION_TERRITORIAL d1 ON d1.id_division = x.nivel1_id
            LEFT JOIN gen.DIVISION_TERRITORIAL d2 ON d2.id_division = x.nivel2_id
            LEFT JOIN gen.DIVISION_TERRITORIAL d3 ON d3.id_division = x.nivel3_id
            LEFT JOIN gen.DIVISION_TERRITORIAL d4 ON d4.id_division = x.nivel4_id
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo'
            ORDER BY x.es_principal DESC, x.id_direccion
            """, new { idInterlocutor })).ToList();
        var cfgs = await DireccionGeografia.CargarConfigsAsync(conexion, null, dirFilas.Select(f => (long)f.id_pais));
        var dirs = dirFilas.Select(x =>
        {
            var cfg = cfgs[(long)x.id_pais];
            var niveles = DireccionGeografia.ConstruirNiveles(cfg, new (long? Id, string? Texto, string? NombreDivision)[]
            {
                ((long?)x.nivel1_id, (string?)x.nivel1_texto, (string?)x.nivel1_nombre),
                ((long?)x.nivel2_id, (string?)x.nivel2_texto, (string?)x.nivel2_nombre),
                ((long?)x.nivel3_id, (string?)x.nivel3_texto, (string?)x.nivel3_nombre),
                ((long?)x.nivel4_id, (string?)x.nivel4_texto, (string?)x.nivel4_nombre),
            });
            return new DireccionResponse(
                (long)x.id_direccion, (long)x.id_tipo_direccion, (string)x.tipo_nombre,
                (long)x.id_pais, cfg.PaisNombre, niveles, (string?)x.codigo_postal,
                (string)x.detalle, (bool)x.es_principal,
                (string)x.creado_por, (DateTime)x.creado_en, (string?)x.modificado_por, (DateTime?)x.modificado_en);
        }).ToList();

        var conts = (await conexion.QueryAsync(
            """
            SELECT x.id_contacto, x.id_tipo_contacto, t.codigo AS tipo_codigo, t.nombre AS tipo_nombre, x.valor,
                   x.es_principal, x.creado_por, x.creado_en, x.modificado_por, x.modificado_en
            FROM cli.INTERLOCUTOR_CONTACTO x
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_contacto
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo'
            ORDER BY t.orden, x.es_principal DESC, x.id_contacto
            """, new { idInterlocutor }))
            .Select(x => new ContactoResponse(
                (long)x.id_contacto, (long)x.id_tipo_contacto, (string)x.tipo_codigo, (string)x.tipo_nombre, (string)x.valor,
                (bool)x.es_principal, (string)x.creado_por, (DateTime)x.creado_en, (string?)x.modificado_por, (DateTime?)x.modificado_en)).ToList();

        return new InterlocutorDetalleResponse(
            (long)r.IdInterlocutor, (string)r.Numero, (string)r.TipoPersona, (string)r.Estado,
            (long)r.IdPaisRegulador, (string)r.PaisReguladorNombre,
            (long?)r.IdUnidad, (string?)r.UnidadNombre,
            (long?)r.IdCanalOrigen, (string?)r.CanalOrigenNombre,
            (string?)r.Nombre1, (string?)r.Nombre2, (string?)r.Apellido1, (string?)r.Apellido2,
            A(r.FechaNacimiento),
            (DateTime)r.CreadoEn, (string)r.CreadoPor, (DateTime?)r.ModificadoEn, (string?)r.ModificadoPor,
            (long?)r.IdGestionPendiente,
            roles, idents, dirs, conts);
    }

    // ----- Vista 360 ----------------------------------------------------------------------

    public async Task<Interlocutor360Response?> Obtener360Async(long idCompania, long idInterlocutor)
    {
        var ficha = await ObtenerDetalleAsync(idCompania, idInterlocutor);
        if (ficha is null) return null;

        using var conexion = NuevaConexion();
        var llave = idInterlocutor.ToString();

        var gestiones = (await conexion.QueryAsync(
            """
            SELECT g.id_gestion, tg.nombre AS tipo, g.estado, u.nombre_completo AS solicitado_por,
                   g.creado_en, g.fecha_resolucion, g.comentario, g.motivo_rechazo,
                   (SELECT COUNT(*) FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion) AS campos
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            WHERE g.id_compania = @idCompania AND g.entidad_negocio = 'INTERLOCUTOR' AND g.llave_registro = @llave
            ORDER BY g.fecha_solicitud DESC
            """, new { idCompania, llave }))
            .Select(g => new GestionResumenResponse(
                (long)g.id_gestion, (string)g.tipo, (string)g.estado, (string)g.solicitado_por,
                (DateTime)g.creado_en, (DateTime?)g.fecha_resolucion, (string?)g.comentario, (string?)g.motivo_rechazo, (int)g.campos))
            .ToList();

        var rel = (await conexion.QueryAsync(
            """
            SELECT CASE WHEN r.id_interlocutor_origen = @id THEN r.id_interlocutor_destino ELSE r.id_interlocutor_origen END AS id_rel,
                   CASE WHEN r.id_interlocutor_origen = @id THEN N'Origen' ELSE N'Destino' END AS sentido,
                   tr.nombre AS tipo, r.porcentaje_participacion
            FROM cli.INTERLOCUTOR_RELACION r
            JOIN gen.LISTA_VALORES_ITEM tr ON tr.id_lista_valores_item = r.id_tipo_relacion
            WHERE (r.id_interlocutor_origen = @id OR r.id_interlocutor_destino = @id) AND r.fecha_fin IS NULL
            """, new { id = idInterlocutor })).ToList();

        var relaciones = new List<RelacionResponse>();
        if (rel.Count > 0)
        {
            var ids = rel.Select(x => (long)x.id_rel).Distinct().ToList();
            var otros = (await conexion.QueryAsync($"""
                SELECT i.id_interlocutor AS Id, i.numero AS Numero,
                       f.nombre1 AS Nombre1, f.nombre2 AS Nombre2, f.apellido1 AS Apellido1, f.apellido2 AS Apellido2,
                       {SqlRoles} AS Roles
                FROM cli.INTERLOCUTOR i
                LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
                WHERE i.id_compania = @idCompania AND i.id_interlocutor IN @ids
                """, new { idCompania, ids })).ToDictionary(x => (long)x.Id);

            foreach (var x in rel)
            {
                var idRel = (long)x.id_rel;
                if (!otros.TryGetValue(idRel, out var o)) continue;
                var nombre = NombreCompleto((string?)o.Nombre1, (string?)o.Nombre2, (string?)o.Apellido1, (string?)o.Apellido2);
                relaciones.Add(new RelacionResponse(
                    idRel, (string)o.Numero, nombre.Length > 0 ? nombre : $"Interlocutor {(string)o.Numero}",
                    (string?)o.Roles ?? string.Empty, (string)x.tipo, (string)x.sentido, (decimal?)x.porcentaje_participacion));
            }
        }

        const string pendiente = "Pendiente de Aprobacion";
        return new Interlocutor360Response(
            ficha,
            gestiones.Where(g => g.Estado == pendiente).ToList(),
            gestiones.Where(g => g.Estado != pendiente).ToList(),
            relaciones,
            Productos: []);   // se llena cuando existan los modulos de Cuentas y Creditos
    }

    // ----- Borradores del wizard ------------------------------------------------------------------

    private int DiasVigenciaBorrador => Math.Max(1, _configuracion.GetValue("Borradores:DiasVigencia", 30));

    public async Task<IReadOnlyList<BorradorResumenResponse>> ListarBorradoresAsync(ActorContext actor)
    {
        using var conexion = NuevaConexion();

        // Limpieza oportunista: los borradores vencidos se eliminan al listar.
        await conexion.ExecuteAsync(
            "DELETE FROM cli.INTERLOCUTOR_BORRADOR WHERE id_compania = @c AND vence_en < SYSUTCDATETIME()",
            new { c = actor.IdCompania });

        var filas = await conexion.QueryAsync(
            """
            SELECT id_borrador, resumen_nombre, resumen_identificacion, paso_actual, ISNULL(modificado_en, creado_en) AS modificado_en, vence_en
            FROM cli.INTERLOCUTOR_BORRADOR
            WHERE id_compania = @c AND id_usuario = @u
            ORDER BY ISNULL(modificado_en, creado_en) DESC
            """, new { c = actor.IdCompania, u = actor.IdUsuario });

        return filas.Select(f => new BorradorResumenResponse(
            (long)f.id_borrador, (string?)f.resumen_nombre, (string?)f.resumen_identificacion,
            (int)f.paso_actual, (DateTime)f.modificado_en, (DateTime)f.vence_en)).ToList();
    }

    public async Task<BorradorDetalleResponse?> ObtenerBorradorAsync(ActorContext actor, long idBorrador)
    {
        using var conexion = NuevaConexion();
        var f = await conexion.QuerySingleOrDefaultAsync(
            """
            SELECT id_borrador, paso_actual, vence_en, contenido
            FROM cli.INTERLOCUTOR_BORRADOR
            WHERE id_borrador = @id AND id_compania = @c AND id_usuario = @u AND vence_en >= SYSUTCDATETIME()
            """, new { id = idBorrador, c = actor.IdCompania, u = actor.IdUsuario });
        if (f is null) return null;

        using var doc = JsonDocument.Parse((string)f.contenido);
        return new BorradorDetalleResponse((long)f.id_borrador, (int)f.paso_actual, (DateTime)f.vence_en, doc.RootElement.Clone());
    }

    /// <summary>Crea o actualiza un borrador propio. Minimo para guardar: nombre y una identificacion con tipo y numero.</summary>
    public async Task<ResultadoOperacion> GuardarBorradorAsync(ActorContext actor, BorradorGuardarRequest req)
    {
        if (actor.IdUsuario is null)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede guardar borradores.");
        if (req.Contenido.ValueKind != JsonValueKind.Object)
            return ResultadoOperacion.Fallo(400, "contenido_invalido", "El contenido del borrador debe ser un objeto.");

        var json = req.Contenido.GetRawText();
        if (json.Length > 200_000)
            return ResultadoOperacion.Fallo(400, "contenido_muy_grande", "El borrador es demasiado grande.");

        string? nombre1 = req.Contenido.TryGetProperty("nombre1", out var n1) && n1.ValueKind == JsonValueKind.String ? n1.GetString() : null;
        string? apellido1 = req.Contenido.TryGetProperty("apellido1", out var a1) && a1.ValueKind == JsonValueKind.String ? a1.GetString() : null;
        string? numero = null;
        long tipo = 0;
        if (req.Contenido.TryGetProperty("identificaciones", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in ids.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;
                var num = it.TryGetProperty("numero", out var nv) && nv.ValueKind == JsonValueKind.String ? nv.GetString() : null;
                var t = it.TryGetProperty("idTipoIdentificacion", out var tv) && tv.ValueKind == JsonValueKind.Number ? tv.GetInt64() : 0L;
                if (!string.IsNullOrWhiteSpace(num) && t > 0) { numero = num.Trim(); tipo = t; break; }
            }
        }
        if (string.IsNullOrWhiteSpace(nombre1) || numero is null || tipo == 0)
            return ResultadoOperacion.Fallo(400, "minimo_borrador",
                "Para guardar un borrador se requiere al menos el primer nombre y una identificacion con tipo y numero.");

        var resumenNombre = string.Join(" ", new[] { nombre1!.Trim(), apellido1?.Trim() }.Where(x => !string.IsNullOrEmpty(x)));
        var vence = DateTime.UtcNow.AddDays(DiasVigenciaBorrador);
        var paso = Math.Clamp(req.Paso, 1, 5);

        using var conexion = NuevaConexion();

        if (req.IdBorrador is null)
        {
            var id = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO cli.INTERLOCUTOR_BORRADOR (id_compania, id_usuario, resumen_nombre, resumen_identificacion, paso_actual, contenido, vence_en)
                OUTPUT INSERTED.id_borrador
                VALUES (@c, @u, @nombre, @numero, @paso, @json, @vence)
                """, new { c = actor.IdCompania, u = actor.IdUsuario, nombre = resumenNombre, numero, paso, json, vence });
            return ResultadoOperacion.Ok(id);
        }

        var filas = await conexion.ExecuteAsync(
            """
            UPDATE cli.INTERLOCUTOR_BORRADOR
               SET resumen_nombre = @nombre, resumen_identificacion = @numero, paso_actual = @paso, contenido = @json,
                   modificado_en = SYSUTCDATETIME(), vence_en = @vence
             WHERE id_borrador = @id AND id_compania = @c AND id_usuario = @u
            """, new { id = req.IdBorrador, c = actor.IdCompania, u = actor.IdUsuario, nombre = resumenNombre, numero, paso, json, vence });
        return filas == 0
            ? ResultadoOperacion.Fallo(404, "borrador_no_encontrado", "El borrador no existe o ya vencio.")
            : ResultadoOperacion.Ok(req.IdBorrador);
    }

    public async Task<ResultadoOperacion> EliminarBorradorAsync(ActorContext actor, long idBorrador)
    {
        using var conexion = NuevaConexion();
        var filas = await conexion.ExecuteAsync(
            "DELETE FROM cli.INTERLOCUTOR_BORRADOR WHERE id_borrador = @id AND id_compania = @c AND id_usuario = @u",
            new { id = idBorrador, c = actor.IdCompania, u = actor.IdUsuario });
        return filas == 0
            ? ResultadoOperacion.Fallo(404, "borrador_no_encontrado", "El borrador no existe.")
            : ResultadoOperacion.Ok(idBorrador);
    }
}
