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
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE id_compania = @c AND estado = 'Activo' AND permite_registros = 1) THEN 1 ELSE 0 END", new { c = idCompania }, tx);
            return hay && actual is null ? "Indique la unidad (sucursal) a la que pertenece el cliente." : null;
        }
        if (idUnidad == actual) return null;
        var ok = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE id_departamento = @u AND id_compania = @c AND estado = 'Activo' AND permite_registros = 1) THEN 1 ELSE 0 END",
            new { u = idUnidad, c = idCompania }, tx);
        return ok ? null : "La unidad no existe, esta inactiva o no permite registrar clientes.";
    }

    /// <summary>Unidad por defecto de un usuario para registros: la suya o, si no admite registros, la superior mas cercana que si.</summary>
    public static Task<long?> UnidadRegistroPorDefectoAsync(SqlConnection c, SqlTransaction? tx, long idUsuario)
        => c.ExecuteScalarAsync<long?>(
            """
            WITH arriba AS (
                SELECT d.id_departamento, d.id_departamento_padre, d.permite_registros, d.estado, 0 AS n
                FROM sec.USUARIO u JOIN sec.DEPARTAMENTO d ON d.id_departamento = u.id_departamento
                WHERE u.id_usuario = @u
                UNION ALL
                SELECT p.id_departamento, p.id_departamento_padre, p.permite_registros, p.estado, a.n + 1
                FROM arriba a JOIN sec.DEPARTAMENTO p ON p.id_departamento = a.id_departamento_padre
            )
            SELECT TOP (1) id_departamento FROM arriba WHERE permite_registros = 1 AND estado = 'Activo' ORDER BY n
            """, new { u = idUsuario }, tx);

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
            WHERE x.id_compania = @idCompania AND x.estado = 'Activo' AND i.estado <> 'Fusionado'
              AND x.id_tipo_identificacion = @idTipo AND x.numero_identificacion = @numero
              AND (@excluir IS NULL OR x.id_interlocutor <> @excluir)
            """, new { idCompania, idTipo, numero = numero.Trim(), excluir = excluirInterlocutor }, tx);
    }

    // ----- Alta ----------------------------------------------------------------

    public async Task<ResultadoOperacion> AltaAsync(ActorContext actor, InterlocutorAltaRequest req)
    {
        if (req.TipoPersona == "Juridica")
            return ResultadoOperacion.Fallo(400, "tipo_no_soportado", "El alta directa solo aplica a personas; las organizaciones se dan de alta por gestion.");
        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var cat = await CargarCatalogosAsync(conexion, tx);
            var (idRolAlta, reglas, erroresRol) = await RolServicio.ValidarRolAltaAsync(conexion, tx, actor.IdCompania, req.IdTipoRol);
            if (await EmpleadoServicio.EsRolEmpleadoAsync(conexion, tx, idRolAlta)) erroresRol.Add("El rol Empleado se da de alta por gestión, con su unidad y número de empleado.");
            var errores = InterlocutorValidador.Validar(
                req.Nombre1, req.Nombre2, req.Apellido1, req.Apellido2, req.FechaNacimiento, req.Identificaciones, req.Direcciones, req.Contactos, cat, reglas,
                req.Identidad ?? new IdentidadPersonaDto(null, null, null, null, null));
            errores.AddRange(erroresRol);

            var paisOk = await conexion.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { p = req.IdPaisRegulador }, tx);
            if (!paisOk) errores.Add("El pais regulador no existe o esta inactivo.");

            var (erroresGeo, direcciones) = await DireccionGeografia.ValidarAsync(conexion, tx, req.Direcciones, reglas.Pide(ReglasCampos.DirNiveles), reglas.Pide(ReglasCampos.DirCodigoPostal));
            errores.AddRange(erroresGeo);

            // Unidad (sucursal): por defecto la del usuario que da el alta; obligatoria si la compania ya tiene unidades activas.
            var idUnidad = req.IdUnidad;
            if (idUnidad is null && !actor.EsIntegracion)
                idUnidad = await UnidadRegistroPorDefectoAsync(conexion, tx, actor.IdUsuario.Value);
            var errorUnidad = await ValidarUnidadAsync(conexion, tx, actor.IdCompania, idUnidad, null);
            if (errorUnidad is not null) errores.Add(errorUnidad);

            // Canal de origen: nunca lo elige quien da el alta. Una integracion (API) tiene su canal fijo; el Backoffice siempre es Sucursal.
            long? idCanal;
            if (actor.EsIntegracion)
            {
                idCanal = await conexion.ExecuteScalarAsync<long?>(
                    "SELECT id_canal FROM sec.USUARIO_INTEGRACION WHERE id_usuario_integracion = @i", new { i = actor.IdUsuarioIntegracion }, tx);
                if (idCanal is null) errores.Add("El usuario de integracion no tiene un canal asignado; asignelo en Administracion > Usuarios de integracion.");
            }
            else
            {
                idCanal = await CanalPorCodigoAsync(conexion, tx, "SUCURSAL");
                if (idCanal is null) errores.Add("Falta el canal SUCURSAL en la lista CANAL; correr administracion_transversal.sql.");
            }

            if (errores.Count > 0)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

            foreach (var i in req.Identificaciones)
            {
                var dup = await BuscarDuplicadoAsync(conexion, tx, actor.IdCompania, i.IdTipoIdentificacion, i.Numero, null);
                if (dup is not null)
                    return ResultadoOperacion.Fallo(409, "identificacion_duplicada",
                        $"La identificacion {i.Numero.Trim()} ya pertenece al cliente N.º {dup}. Si necesita que asuma otro rol, use «Gestionar roles» en su ficha.");
            }

            var idRolCliente = idRolAlta;
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

            var idInterlocutor = await InsertarClienteAsync(
                conexion, tx, actor.IdCompania, numero, req, direcciones, cat, idRolCliente.Value, idUnidad, idCanal, por);

            if (req.IdBorrador is not null && !actor.EsIntegracion)
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

    /// <summary>
    /// Inserta el cliente (Persona Fisica u Organizacion) con sus identificaciones, direcciones, contactos y rol CLIENTE dentro de la
    /// transaccion dada. Lo usan el alta directa (integraciones) y la aplicacion de la gestion de alta al aprobarse.
    /// Las direcciones deben venir ya normalizadas (DireccionGeografia.ValidarAsync).
    /// </summary>
    internal static async Task<long> InsertarClienteAsync(
        SqlConnection conexion, SqlTransaction tx, long idCompania, string numero, InterlocutorAltaRequest req,
        IReadOnlyList<DireccionDto> direcciones, CatalogosInterlocutor cat, long idRolCliente, long? idUnidad, long? idCanal, string por)
    {
            var esOrg = req.TipoPersona == "Juridica";
            var idInterlocutor = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO cli.INTERLOCUTOR (id_compania, numero, tipo_persona, id_pais_regulador, id_unidad, id_canal_origen, creado_por)
                OUTPUT INSERTED.id_interlocutor
                VALUES (@idCompania, @numero, @tipoPersona, @idPais, @idUnidad, @idCanal, @por)
                """, new { idCompania, numero, tipoPersona = esOrg ? "Juridica" : "Fisica", idPais = req.IdPaisRegulador, idUnidad, idCanal, por }, tx);

            if (esOrg)
            {
                var o = req.Organizacion!;
                await conexion.ExecuteAsync(
                    """
                    INSERT INTO cli.INTERLOCUTOR_JURIDICA (id_interlocutor, nombre_org1, fecha_constitucion, id_forma_juridica, id_pais_constitucion,
                                                           referencia_registral, id_estado_legal, fecha_disolucion)
                    VALUES (@idInterlocutor, @razon, @fc, @forma, @pais, @ref, @estado, @fd)
                    """,
                    new
                    {
                        idInterlocutor, razon = o.RazonSocial.Trim(), fc = D(o.FechaConstitucion), forma = o.IdFormaJuridica,
                        pais = o.IdPaisConstitucion, @ref = Vacio(o.ReferenciaRegistral), estado = o.IdEstadoLegal, fd = D(o.FechaDisolucion)
                    }, tx);
                foreach (var n in (o.NombresComerciales ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Nombre)))
                    await conexion.ExecuteAsync(
                        "INSERT INTO cli.INTERLOCUTOR_NOMBRE_COMERCIAL (id_interlocutor, nombre, creado_por) VALUES (@idInterlocutor, @nombre, @por)",
                        new { idInterlocutor, nombre = n.Nombre.Trim(), por }, tx);
                foreach (var a in o.Actividades ?? [])
                    await conexion.ExecuteAsync(
                        """
                        INSERT INTO cli.INTERLOCUTOR_ACTIVIDAD (id_interlocutor, id_actividad, descripcion, es_principal, fecha_inicio, creado_por)
                        VALUES (@idInterlocutor, @act, @descripcion, @principal, @fi, @por)
                        """,
                        new { idInterlocutor, act = a.IdActividad, descripcion = Vacio(a.Descripcion), principal = a.EsPrincipal, fi = D(a.FechaInicio), por }, tx);
            }
            else
            {
                await conexion.ExecuteAsync(
                    """
                    INSERT INTO cli.INTERLOCUTOR_FISICA (id_interlocutor, nombre1, nombre2, apellido1, apellido2, fecha_nacimiento,
                                                         nombre_conocido, id_pais_nacimiento, id_sexo, id_estado_civil)
                    VALUES (@idInterlocutor, @n1, @n2, @a1, @a2, @fn, @conocido, @paisNac, @sexo, @civil)
                    """,
                    new
                    {
                        idInterlocutor, n1 = req.Nombre1.Trim(),
                        n2 = string.IsNullOrWhiteSpace(req.Nombre2) ? null : req.Nombre2.Trim(),
                        a1 = req.Apellido1.Trim(),
                        a2 = string.IsNullOrWhiteSpace(req.Apellido2) ? null : req.Apellido2.Trim(),
                        fn = D(req.FechaNacimiento),
                        conocido = Vacio(req.Identidad?.NombreConocido), paisNac = req.Identidad?.IdPaisNacimiento,
                        sexo = req.Identidad?.IdSexo, civil = req.Identidad?.IdEstadoCivil
                    }, tx);
                foreach (var nac in req.Identidad?.Nacionalidades ?? [])
                    await conexion.ExecuteAsync(
                        "INSERT INTO cli.INTERLOCUTOR_NACIONALIDAD (id_interlocutor, id_pais, creado_por) VALUES (@idInterlocutor, @pais, @por)",
                        new { idInterlocutor, pais = nac.IdPais, por }, tx);
            }

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
                        idInterlocutor, idCompania = idCompania, tipo = i.IdTipoIdentificacion, numero = i.Numero.Trim(),
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
                         codigo_postal, detalle, detalle2, detalle3, es_principal, creado_por)
                    VALUES (@idInterlocutor, @tipo, @pais, @n1i, @n2i, @n3i, @n4i, @n1t, @n2t, @n3t, @n4t,
                            @cp, @detalle, @detalle2, @detalle3, @principal, @por)
                    """,
                    new
                    {
                        idInterlocutor, tipo = d.IdTipoDireccion, pais = d.IdPais,
                        n1i = d.Niveles![0].Id, n2i = d.Niveles[1].Id, n3i = d.Niveles[2].Id, n4i = d.Niveles[3].Id,
                        n1t = d.Niveles[0].Texto, n2t = d.Niveles[1].Texto, n3t = d.Niveles[2].Texto, n4t = d.Niveles[3].Texto,
                        cp = d.CodigoPostal, detalle = d.Detalle.Trim(),
                        detalle2 = string.IsNullOrWhiteSpace(d.Detalle2) ? null : d.Detalle2.Trim(),
                        detalle3 = string.IsNullOrWhiteSpace(d.Detalle3) ? null : d.Detalle3.Trim(), principal = d.EsPrincipal, por
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

            // Todo cliente nace con su expediente KYC (Pendiente) y la proxima revision calculada por parametro.
            await KycVigenciaServicio.CrearExpedienteInicialAsync(conexion, tx, idCompania, idInterlocutor, por);

        return idInterlocutor;
    }

    private static string? Vacio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ----- Listado y busqueda ------------------------------------------------------

    private const string CondicionBusqueda = """
        AND (@like IS NULL
             OR CONCAT_WS(' ', f.nombre1, f.nombre2, f.apellido1, f.apellido2) LIKE @like
             OR j.nombre_org1 LIKE @like
             OR EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_NOMBRE_COMERCIAL nc
                         WHERE nc.id_interlocutor = i.id_interlocutor AND nc.estado = 'Activo' AND nc.nombre LIKE @like)
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
        long idCompania, string? texto, int pagina, int tamanoPagina, string? tipo = null, PerfilEfectivo? perfil = null)
    {
        using var conexion = NuevaConexion();
        var offset = (pagina - 1) * tamanoPagina;
        var like = PatronLike(texto);
        var pr = new DynamicParameters(new { idCompania, like, offset, tamanoPagina, tipo });
        var filtroPerfil = perfil?.PredicadoFilas("i", pr) ?? string.Empty;

        var total = await conexion.ExecuteScalarAsync<int>($"""
            SELECT COUNT(*)
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania
            AND (@tipo IS NULL OR i.tipo_persona = @tipo)
            {CondicionBusqueda}{filtroPerfil}
            """, pr);

        var filas = await conexion.QueryAsync($"""
            SELECT
                i.id_interlocutor AS IdInterlocutor, i.numero AS Numero, i.tipo_persona AS TipoPersona, i.estado AS Estado, i.creado_en AS CreadoEn,
                f.nombre1 AS Nombre1, f.nombre2 AS Nombre2, f.apellido1 AS Apellido1, f.apellido2 AS Apellido2, j.nombre_org1 AS RazonSocial,
                p.id_tipo_identificacion AS IdTipoIdentificacion, p.numero_identificacion AS NumeroIdentificacion,
                {SqlRoles} AS Roles
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            OUTER APPLY (SELECT TOP 1 x.id_tipo_identificacion, x.numero_identificacion
                           FROM cli.INTERLOCUTOR_IDENTIFICACION x
                          WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo'
                          ORDER BY x.es_principal DESC, x.id_identificacion) p
            WHERE i.id_compania = @idCompania
            AND (@tipo IS NULL OR i.tipo_persona = @tipo)
            {CondicionBusqueda}{filtroPerfil}
            ORDER BY i.id_interlocutor DESC
            OFFSET @offset ROWS FETCH NEXT @tamanoPagina ROWS ONLY;
            """, pr);

        var items = filas.Select(f => new InterlocutorResponse(
            (long)f.IdInterlocutor, (string)f.Numero, (string)f.TipoPersona,
            (long?)f.IdTipoIdentificacion ?? 0L, (string?)f.NumeroIdentificacion ?? string.Empty,
            (string?)f.RazonSocial ?? NombreCompleto((string?)f.Nombre1, (string?)f.Nombre2, (string?)f.Apellido1, (string?)f.Apellido2),
            (string?)f.Roles ?? string.Empty, (string)f.Estado, (DateTime)f.CreadoEn)).ToList();

        return (items, total);
    }

    /// <summary>Busqueda global (barra superior): hasta 8 coincidencias, sin importar el rol.</summary>
    public async Task<IReadOnlyList<InterlocutorBusquedaResponse>> BuscarAsync(long idCompania, string? texto, string? rol = null, PerfilEfectivo? perfil = null)
    {
        var like = PatronLike(texto);
        if (like is null || texto!.Trim().Length < 2) return [];
        var pr = new DynamicParameters(new { idCompania, like, rol = string.IsNullOrWhiteSpace(rol) ? null : rol.Trim().ToUpperInvariant() });
        var filtroPerfil = perfil?.PredicadoFilas("i", pr) ?? string.Empty;

        using var conexion = NuevaConexion();
        var filas = await conexion.QueryAsync($"""
            SELECT TOP (8)
                i.id_interlocutor AS IdInterlocutor, i.numero AS Numero, i.estado AS Estado,
                f.nombre1 AS Nombre1, f.nombre2 AS Nombre2, f.apellido1 AS Apellido1, f.apellido2 AS Apellido2, j.nombre_org1 AS RazonSocial,
                {SqlRoles} AS Roles,
                (SELECT STRING_AGG(CONCAT(t.nombre, N' ', x.numero_identificacion), N' · ')
                   FROM cli.INTERLOCUTOR_IDENTIFICACION x
                   JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_identificacion
                  WHERE x.id_interlocutor = i.id_interlocutor AND x.estado = 'Activo') AS Identificaciones
            FROM cli.INTERLOCUTOR i
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE i.id_compania = @idCompania
            {CondicionBusqueda}{filtroPerfil}
            AND (@rol IS NULL OR (i.estado = 'Activo' AND EXISTS (
                  SELECT 1 FROM cli.INTERLOCUTOR_ROL rr JOIN gen.LISTA_VALORES_ITEM ri ON ri.id_lista_valores_item = rr.id_tipo_rol
                   WHERE rr.id_interlocutor = i.id_interlocutor AND rr.estado = 'Activo' AND ri.codigo = @rol)))
            ORDER BY i.id_interlocutor DESC
            """, pr);

        return filas.Select(f => new InterlocutorBusquedaResponse(
            (long)f.IdInterlocutor, (string)f.Numero,
            (string?)f.RazonSocial ?? NombreCompleto((string?)f.Nombre1, (string?)f.Nombre2, (string?)f.Apellido1, (string?)f.Apellido2),
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
                f.nombre_conocido AS NombreConocido, f.id_pais_nacimiento AS IdPaisNacimiento, pn.nombre AS PaisNacimientoNombre,
                f.id_sexo AS IdSexo, sx.nombre AS SexoNombre, f.id_estado_civil AS IdEstadoCivil, ec.nombre AS EstadoCivilNombre,
                i.creado_en AS CreadoEn, i.creado_por AS CreadoPor,
                i.modificado_en AS ModificadoEn, i.modificado_por AS ModificadoPor,
                (SELECT TOP 1 g.id_gestion FROM apr.GESTION g
                  WHERE g.entidad_negocio IN ('INTERLOCUTOR', 'INTERLOCUTOR_FUSION', 'INTERLOCUTOR_ROL', 'INTERLOCUTOR_RELACION', 'INTERLOCUTOR_FALLECIMIENTO')
                    AND g.llave_registro = CONVERT(nvarchar(100), i.id_interlocutor)
                    AND g.estado = 'Pendiente de Aprobacion') AS IdGestionPendiente,
                (SELECT TOP 1 g.numero FROM apr.GESTION g
                  WHERE g.entidad_negocio IN ('INTERLOCUTOR', 'INTERLOCUTOR_FUSION', 'INTERLOCUTOR_ROL', 'INTERLOCUTOR_RELACION', 'INTERLOCUTOR_FALLECIMIENTO')
                    AND g.llave_registro = CONVERT(nvarchar(100), i.id_interlocutor)
                    AND g.estado = 'Pendiente de Aprobacion') AS NumeroGestionPendiente,
                i.id_interlocutor_sobreviviente AS IdSobreviviente, sv.numero AS NumeroSobreviviente
            FROM cli.INTERLOCUTOR i
            JOIN gen.PAIS_REGULADOR p ON p.id_pais_regulador = i.id_pais_regulador
            LEFT JOIN sec.DEPARTAMENTO un ON un.id_departamento = i.id_unidad
            LEFT JOIN gen.LISTA_VALORES_ITEM cn ON cn.id_lista_valores_item = i.id_canal_origen
            LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            LEFT JOIN gen.LISTA_VALORES_ITEM pn ON pn.id_lista_valores_item = f.id_pais_nacimiento
            LEFT JOIN gen.LISTA_VALORES_ITEM sx ON sx.id_lista_valores_item = f.id_sexo
            LEFT JOIN gen.LISTA_VALORES_ITEM ec ON ec.id_lista_valores_item = f.id_estado_civil
            LEFT JOIN cli.INTERLOCUTOR sv ON sv.id_interlocutor = i.id_interlocutor_sobreviviente
            WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania
            """, new { idInterlocutor, idCompania });
        if (r is null) return null;

        var roles = (await conexion.QueryAsync(
            """
            SELECT r.id_tipo_rol, it.codigo, it.nombre, r.estado, r.fecha_asignacion,
                   CASE WHEN em.id_interlocutor_rol IS NOT NULL THEN CONCAT(N'Unidad: ', un.nombre, N' · N.º de empleado: ', em.numero_empleado) END AS detalle
            FROM cli.INTERLOCUTOR_ROL r JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
            LEFT JOIN cli.INTERLOCUTOR_ROL_EMPLEADO em ON em.id_interlocutor_rol = r.id_interlocutor_rol AND em.estado = 'Activo'
            LEFT JOIN sec.DEPARTAMENTO un ON un.id_departamento = em.id_unidad
            WHERE r.id_interlocutor = @idInterlocutor AND r.estado = 'Activo' ORDER BY it.orden, it.nombre
            """, new { idInterlocutor }))
            .Select(x => new RolInterlocutorResponse((long)x.id_tipo_rol, (string)x.codigo, (string)x.nombre, (string)x.estado, (DateTime?)x.fecha_asignacion, (string?)x.detalle)).ToList();
        var historialRoles = await RolServicio.HistorialAsync(conexion, null, idInterlocutor);

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
                   x.codigo_postal, x.detalle, x.detalle2, x.detalle3,
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
                (string)x.creado_por, (DateTime)x.creado_en, (string?)x.modificado_por, (DateTime?)x.modificado_en,
                (string?)x.detalle2, (string?)x.detalle3);
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

        OrganizacionResponse? org = null;
        if ((string)r.TipoPersona == "Juridica")
        {
            var o = await conexion.QuerySingleOrDefaultAsync(
                """
                SELECT j.nombre_org1 AS razon, j.id_forma_juridica, fj.nombre AS forma, j.id_pais_constitucion, pc.nombre AS pais,
                       j.fecha_constitucion, j.referencia_registral, j.id_estado_legal, el.nombre AS estado_legal, j.fecha_disolucion
                FROM cli.INTERLOCUTOR_JURIDICA j
                LEFT JOIN gen.LISTA_VALORES_ITEM fj ON fj.id_lista_valores_item = j.id_forma_juridica
                LEFT JOIN gen.LISTA_VALORES_ITEM pc ON pc.id_lista_valores_item = j.id_pais_constitucion
                LEFT JOIN gen.LISTA_VALORES_ITEM el ON el.id_lista_valores_item = j.id_estado_legal
                WHERE j.id_interlocutor = @idInterlocutor
                """, new { idInterlocutor });
            var noms = (await conexion.QueryAsync(
                "SELECT id_nombre_comercial, nombre FROM cli.INTERLOCUTOR_NOMBRE_COMERCIAL WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo' ORDER BY id_nombre_comercial",
                new { idInterlocutor })).Select(x => new NombreComercialResponse((long)x.id_nombre_comercial, (string)x.nombre)).ToList();
            var acts = (await conexion.QueryAsync(
                """
                SELECT a.id_actividad_interlocutor, a.id_actividad, it.codigo, it.nombre, a.descripcion, a.es_principal, a.fecha_inicio
                FROM cli.INTERLOCUTOR_ACTIVIDAD a JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = a.id_actividad
                WHERE a.id_interlocutor = @idInterlocutor AND a.estado = 'Activo'
                ORDER BY a.es_principal DESC, a.id_actividad_interlocutor
                """, new { idInterlocutor }))
                .Select(x => new ActividadResponse((long)x.id_actividad_interlocutor, (long)x.id_actividad, (string)x.codigo, (string)x.nombre,
                    (string?)x.descripcion, (bool)x.es_principal, A(x.fecha_inicio))).ToList();
            if (o is not null)
                org = new OrganizacionResponse((string)o.razon, (long?)o.id_forma_juridica, (string?)o.forma, (long?)o.id_pais_constitucion, (string?)o.pais,
                    A(o.fecha_constitucion), (string?)o.referencia_registral, (long?)o.id_estado_legal, (string?)o.estado_legal, A(o.fecha_disolucion), noms, acts);
        }

        IdentidadPersonaResponse? identidad = null;
        if ((string)r.TipoPersona == "Fisica")
        {
            var nacs = (await conexion.QueryAsync(
                """
                SELECT n.id_nacionalidad_interlocutor, n.id_pais, p.nombre FROM cli.INTERLOCUTOR_NACIONALIDAD n
                JOIN gen.LISTA_VALORES_ITEM p ON p.id_lista_valores_item = n.id_pais
                WHERE n.id_interlocutor = @idInterlocutor AND n.estado = 'Activo' ORDER BY n.id_nacionalidad_interlocutor
                """, new { idInterlocutor }))
                .Select(x => new NacionalidadResponse((long)x.id_nacionalidad_interlocutor, (long)x.id_pais, (string)x.nombre)).ToList();
            identidad = new IdentidadPersonaResponse((string?)r.NombreConocido, (long?)r.IdPaisNacimiento, (string?)r.PaisNacimientoNombre,
                (long?)r.IdSexo, (string?)r.SexoNombre, (long?)r.IdEstadoCivil, (string?)r.EstadoCivilNombre, nacs);
        }

        var grupo = (string)r.TipoPersona == GrupoServicio.Tipo ? await GrupoServicio.ObtenerAsync(conexion, idInterlocutor) : null;
        var perfilFinanciero = (string)r.TipoPersona is "Fisica" or "Juridica" ? await PerfilFinancieroServicio.ObtenerAsync(conexion, idInterlocutor) : null;

        var bloqueos = await BloqueoServicio.VigentesAsync(conexion, idInterlocutor);
        var kycVigencia = (string)r.TipoPersona is "Fisica" or "Juridica" ? await KycVigenciaServicio.ObtenerAsync(conexion, idCompania, idInterlocutor) : null;

        return new InterlocutorDetalleResponse(
            (long)r.IdInterlocutor, (string)r.Numero, (string)r.TipoPersona, (string)r.Estado,
            (long)r.IdPaisRegulador, (string)r.PaisReguladorNombre,
            (long?)r.IdUnidad, (string?)r.UnidadNombre,
            (long?)r.IdCanalOrigen, (string?)r.CanalOrigenNombre,
            (string?)r.Nombre1, (string?)r.Nombre2, (string?)r.Apellido1, (string?)r.Apellido2,
            A(r.FechaNacimiento),
            (DateTime)r.CreadoEn, (string)r.CreadoPor, (DateTime?)r.ModificadoEn, (string?)r.ModificadoPor,
            (long?)r.IdGestionPendiente,
            roles, idents, dirs, conts, (string?)r.NumeroGestionPendiente, (long?)r.IdSobreviviente, (string?)r.NumeroSobreviviente, historialRoles, org, grupo, identidad, perfilFinanciero, bloqueos, bloqueos.Count > 0, kycVigencia);
    }

    // ----- Vista 360 ----------------------------------------------------------------------

    public async Task<ConsentimientosClienteResponse?> ObtenerConsentimientosClienteAsync(long idCompania, long idInterlocutor)
    {
        using var conexion = NuevaConexion();
        return await ConsentimientoServicio.ObtenerClienteAsync(conexion, idCompania, idInterlocutor);
    }

    public async Task<BloqueosClienteResponse?> ObtenerBloqueosClienteAsync(long idCompania, long idInterlocutor)
    {
        using var conexion = NuevaConexion();
        return await BloqueoServicio.ObtenerClienteAsync(conexion, idCompania, idInterlocutor);
    }

    public async Task<IReadOnlyList<KycSituacionItemResponse>> ListarKycPorSituacionAsync(long idCompania, string? situacion)
    {
        using var conexion = NuevaConexion();
        return await KycVigenciaServicio.ListarPorSituacionAsync(conexion, idCompania, situacion);
    }

    public async Task<KycVigenciaResponse?> ObtenerKycVigenciaAsync(long idCompania, long idInterlocutor)
    {
        using var conexion = NuevaConexion();
        return await KycVigenciaServicio.ObtenerAsync(conexion, idCompania, idInterlocutor);
    }

    public async Task<KycAperturaResponse> ConsultarAperturaKycAsync(long idCompania, long idInterlocutor)
    {
        using var conexion = NuevaConexion();
        return await KycVigenciaServicio.ConsultarAperturaAsync(conexion, idCompania, idInterlocutor);
    }

    public async Task<IReadOnlyList<BloqueoListadoItemResponse>> ListarBloqueosVigentesAsync(long idCompania)
    {
        using var conexion = NuevaConexion();
        return await BloqueoServicio.ListarVigentesAsync(conexion, idCompania);
    }

    public async Task<Interlocutor360Response?> Obtener360Async(long idCompania, long idInterlocutor)
    {
        var ficha = await ObtenerDetalleAsync(idCompania, idInterlocutor);
        if (ficha is null) return null;

        using var conexion = NuevaConexion();
        var llave = idInterlocutor.ToString();

        var gestiones = (await conexion.QueryAsync(
            """
            SELECT g.id_gestion, g.numero, CASE WHEN g.entidad_negocio = 'INTERLOCUTOR_RELACION' THEN CASE WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') THEN 'Inactivación de relación de interlocutor' WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') THEN 'Modificación de relación de interlocutor' WHEN EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'rel[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relbaja[[]%') AND NOT EXISTS (SELECT 1 FROM apr.GESTION_DETALLE x WHERE x.id_gestion = g.id_gestion AND x.nombre_campo LIKE 'relmod[[]%') THEN 'Creación de relación de interlocutor' ELSE tg.nombre END ELSE tg.nombre END AS tipo, g.estado, u.nombre_completo AS solicitado_por,
                   g.creado_en, g.fecha_resolucion, g.comentario, g.motivo_rechazo,
                   (SELECT COUNT(*) FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion) AS campos
            FROM apr.GESTION g
            JOIN apr.TIPO_GESTION tg ON tg.id_tipo_gestion = g.id_tipo_gestion
            JOIN sec.USUARIO u ON u.id_usuario = g.id_usuario_solicitante
            WHERE g.id_compania = @idCompania AND ((g.entidad_negocio IN ('INTERLOCUTOR', 'INTERLOCUTOR_ROL', 'INTERLOCUTOR_RELACION', 'INTERLOCUTOR_KYC', 'INTERLOCUTOR_CONSENTIMIENTO', 'INTERLOCUTOR_DOCUMENTO', 'INTERLOCUTOR_FALLECIMIENTO') AND g.llave_registro = @llave) OR (g.entidad_negocio = 'INTERLOCUTOR_BLOQUEO' AND g.llave_registro LIKE @llave + '|%'))
            ORDER BY g.fecha_solicitud DESC
            """, new { idCompania, llave }))
            .Select(g => new GestionResumenResponse(
                (long)g.id_gestion, (string)g.tipo, (string)g.estado, (string)g.solicitado_por,
                (DateTime)g.creado_en, (DateTime?)g.fecha_resolucion, (string?)g.comentario, (string?)g.motivo_rechazo, (int)g.campos, (string?)g.numero))
            .ToList();

        var rel = (await conexion.QueryAsync(
            """
            SELECT CASE WHEN r.id_interlocutor_origen = @id THEN r.id_interlocutor_destino ELSE r.id_interlocutor_origen END AS id_rel,
                   CASE WHEN r.id_interlocutor_origen = @id THEN N'Origen' ELSE N'Destino' END AS sentido,
                   CASE WHEN r.id_interlocutor_origen = @id OR cfg.es_simetrica = 1 OR cfg.etiqueta_inversa IS NULL THEN tr.nombre ELSE cfg.etiqueta_inversa END AS tipo,
                   r.porcentaje_participacion
            FROM cli.INTERLOCUTOR_RELACION r
            JOIN gen.LISTA_VALORES_ITEM tr ON tr.id_lista_valores_item = r.id_tipo_relacion
            LEFT JOIN cli.TIPO_RELACION_CONFIG cfg ON cfg.id_tipo_relacion = r.id_tipo_relacion
            WHERE (r.id_interlocutor_origen = @id OR r.id_interlocutor_destino = @id) AND r.fecha_fin IS NULL
            """, new { id = idInterlocutor })).ToList();

        var relaciones = new List<RelacionResponse>();
        if (rel.Count > 0)
        {
            var ids = rel.Select(x => (long)x.id_rel).Distinct().ToList();
            var otros = (await conexion.QueryAsync($"""
                SELECT i.id_interlocutor AS Id, i.numero AS Numero,
                       f.nombre1 AS Nombre1, f.nombre2 AS Nombre2, f.apellido1 AS Apellido1, f.apellido2 AS Apellido2, j.nombre_org1 AS RazonSocial,
                       {SqlRoles} AS Roles
                FROM cli.INTERLOCUTOR i
                LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
                LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
                WHERE i.id_compania = @idCompania AND i.id_interlocutor IN @ids
                """, new { idCompania, ids })).ToDictionary(x => (long)x.Id);

            foreach (var x in rel)
            {
                var idRel = (long)x.id_rel;
                if (!otros.TryGetValue(idRel, out var o)) continue;
                var nombre = (string?)o.RazonSocial ?? NombreCompleto((string?)o.Nombre1, (string?)o.Nombre2, (string?)o.Apellido1, (string?)o.Apellido2);
                relaciones.Add(new RelacionResponse(
                    idRel, (string)o.Numero, nombre.Length > 0 ? nombre : $"Interlocutor {(string)o.Numero}",
                    (string?)o.Roles ?? string.Empty, (string)x.tipo, (string)x.sentido, (decimal?)x.porcentaje_participacion));
            }
        }

        const string pendiente = "Pendiente de Aprobacion";
        return new Interlocutor360Response(
            ficha,
            gestiones.Where(g => g.Estado == pendiente || g.Estado == "Devuelta").ToList(),
            gestiones.Where(g => g.Estado != pendiente && g.Estado != "Devuelta").ToList(),
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
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede guardar borradores.");
        if (req.Contenido.ValueKind != JsonValueKind.Object)
            return ResultadoOperacion.Fallo(400, "contenido_invalido", "El contenido del borrador debe ser un objeto.");

        var json = req.Contenido.GetRawText();
        if (json.Length > 200_000)
            return ResultadoOperacion.Fallo(400, "contenido_muy_grande", "El borrador es demasiado grande.");

        string? nombre1 = req.Contenido.TryGetProperty("nombre1", out var n1) && n1.ValueKind == JsonValueKind.String ? n1.GetString() : null;
        string? apellido1 = req.Contenido.TryGetProperty("apellido1", out var a1) && a1.ValueKind == JsonValueKind.String ? a1.GetString() : null;
        var esOrg = req.Contenido.TryGetProperty("tipoPersona", out var tp) && tp.ValueKind == JsonValueKind.String && tp.GetString() == "Juridica";
        string? razon = esOrg && req.Contenido.TryGetProperty("razonSocial", out var rz) && rz.ValueKind == JsonValueKind.String ? rz.GetString() : null;
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
        if ((esOrg ? string.IsNullOrWhiteSpace(razon) : string.IsNullOrWhiteSpace(nombre1)) || numero is null || tipo == 0)
            return ResultadoOperacion.Fallo(400, "minimo_borrador",
                esOrg ? "Para guardar un borrador se requiere al menos la razon social y una identificacion con tipo y numero."
                      : "Para guardar un borrador se requiere al menos el primer nombre y una identificacion con tipo y numero.");

        var resumenNombre = esOrg ? razon!.Trim() : string.Join(" ", new[] { nombre1!.Trim(), apellido1?.Trim() }.Where(x => !string.IsNullOrEmpty(x)));
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
