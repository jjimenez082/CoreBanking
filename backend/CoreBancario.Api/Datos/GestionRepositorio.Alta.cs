using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Alta de un cliente (Persona u Organizacion) por gestion. Los datos del wizard se guardan en apr.GESTION_DETALLE con el mismo
/// esquema de nombres que la modificacion (campos del cliente, ident[+n].campo, dir[+n].campo, cont[+n].campo; todos con
/// valor anterior vacio). El cliente y su numero NO existen hasta que se aprueba el ultimo nivel: ahi se crea con los
/// datos de la gestion (a nombre del solicitante) y la gestion queda apuntando al cliente creado (llave_registro = id).
/// Mientras esta pendiente su llave es «ALTA-{numero de gestion}».
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoAltaCliente = "CLI_ALTA";
    public const string EntidadAlta = "INTERLOCUTOR_ALTA";
    private const string PrefijoLlaveAlta = "ALTA-";

    // ----- Armado de los pedidos por coleccion (compartido con la modificacion) ---------------

    private static Dictionary<string, List<(long? Id, Dictionary<string, string?> Campos)>> ConstruirPedidos(
        IReadOnlyList<IdentificacionDto> identificaciones, IReadOnlyList<DireccionDto> direccionesNorm,
        IReadOnlyList<ContactoDto>? contactos, CatalogosInterlocutor cat, OrganizacionDto? org = null, IdentidadPersonaDto? identidad = null, PerfilFinancieroDto? perfil = null)
    {
        var porIdent = identificaciones.Select(i => (i.Id, Campos: new Dictionary<string, string?>
        {
            ["id_tipo_identificacion"] = i.IdTipoIdentificacion.ToString(CultureInfo.InvariantCulture),
            ["numero_identificacion"] = V(i.Numero),
            ["id_institucion_emisora"] = i.IdInstitucionEmisora?.ToString(CultureInfo.InvariantCulture),
            ["fecha_emision"] = Fecha(i.FechaEmision),
            ["fecha_vencimiento"] = Fecha(i.FechaVencimiento),
            ["es_principal"] = Bit(i.EsPrincipal),
        })).ToList();

        var porDir = direccionesNorm.Select(d => (d.Id, Campos: new Dictionary<string, string?>
        {
            ["id_tipo_direccion"] = d.IdTipoDireccion.ToString(CultureInfo.InvariantCulture),
            ["id_pais"] = d.IdPais.ToString(CultureInfo.InvariantCulture),
            ["nivel1_id"] = d.Niveles![0].Id?.ToString(CultureInfo.InvariantCulture),
            ["nivel2_id"] = d.Niveles[1].Id?.ToString(CultureInfo.InvariantCulture),
            ["nivel3_id"] = d.Niveles[2].Id?.ToString(CultureInfo.InvariantCulture),
            ["nivel4_id"] = d.Niveles[3].Id?.ToString(CultureInfo.InvariantCulture),
            ["nivel1_texto"] = V(d.Niveles[0].Texto),
            ["nivel2_texto"] = V(d.Niveles[1].Texto),
            ["nivel3_texto"] = V(d.Niveles[2].Texto),
            ["nivel4_texto"] = V(d.Niveles[3].Texto),
            ["codigo_postal"] = V(d.CodigoPostal),
            ["detalle"] = V(d.Detalle),
            ["detalle2"] = V(d.Detalle2),
            ["detalle3"] = V(d.Detalle3),
            ["es_principal"] = Bit(d.EsPrincipal),
        })).ToList();

        var porCont = (contactos ?? []).Select(c => (c.Id, Campos: new Dictionary<string, string?>
        {
            ["id_tipo_contacto"] = c.IdTipoContacto.ToString(CultureInfo.InvariantCulture),
            ["valor"] = InterlocutorValidador.NormalizarContacto(cat.Buscar(CatalogosInterlocutor.ListaTipoContacto, c.IdTipoContacto)!.Codigo, c.Valor),
            ["es_principal"] = Bit(c.EsPrincipal),
        })).ToList();

        var porNom = (org?.NombresComerciales ?? []).Where(n => !string.IsNullOrWhiteSpace(n.Nombre)).Select(n => (n.Id, Campos: new Dictionary<string, string?>
        {
            ["nombre"] = V(n.Nombre),
        })).ToList();

        var porAct = (org?.Actividades ?? []).Select(a => (a.Id, Campos: new Dictionary<string, string?>
        {
            ["id_actividad"] = a.IdActividad.ToString(CultureInfo.InvariantCulture),
            ["descripcion"] = V(a.Descripcion),
            ["fecha_inicio"] = Fecha(a.FechaInicio),
            ["es_principal"] = Bit(a.EsPrincipal),
        })).ToList();

        var porNac = (identidad?.Nacionalidades ?? []).Select(n => (n.Id, Campos: new Dictionary<string, string?>
        {
            ["id_pais"] = n.IdPais.ToString(CultureInfo.InvariantCulture),
        })).ToList();

        static string? D(decimal? d) => PerfilFinancieroServicio.Texto(d);
        static string? Id(long? v) => v?.ToString(CultureInfo.InvariantCulture);
        var porRf = (perfil?.ResidenciasFiscales ?? []).Select(r => (r.Id, Campos: new Dictionary<string, string?>
        {
            ["id_pais"] = Id(r.IdPais),
            ["identificador_tributario"] = V(r.IdentificadorTributario),
            ["fecha_desde"] = Fecha(r.FechaDesde),
            ["fecha_hasta"] = Fecha(r.FechaHasta),
            ["es_principal"] = Bit(r.EsPrincipal),
        })).ToList();
        var porIng = (perfil?.Ingresos ?? []).Select(i => (i.Id, Campos: new Dictionary<string, string?>
        {
            ["id_tipo_ingreso"] = Id(i.IdTipoIngreso),
            ["monto"] = D(i.Monto),
            ["id_moneda"] = Id(i.IdMoneda),
            ["id_periodicidad"] = Id(i.IdPeriodicidad),
        })).ToList();
        var porOrig = (perfil?.Origenes ?? []).Select(o => (o.Id, Campos: new Dictionary<string, string?>
        {
            ["clase"] = o.Clase,
            ["id_origen"] = Id(o.IdOrigen),
        })).ToList();
        var porPt = (perfil?.PerfilTransaccional ?? []).Select(p => (p.Id, Campos: new Dictionary<string, string?>
        {
            ["id_tipo_transaccion"] = Id(p.IdTipoTransaccion),
            ["id_frecuencia"] = Id(p.IdFrecuencia),
            ["monto_minimo"] = D(p.MontoMinimo),
            ["monto_maximo"] = D(p.MontoMaximo),
            ["id_moneda"] = Id(p.IdMoneda),
            ["id_pais_origen"] = Id(p.IdPaisOrigen),
            ["id_pais_destino"] = Id(p.IdPaisDestino),
        })).ToList();

        var porCx = (perfil?.CuentasExternas ?? []).Select(x => (x.Id, Campos: new Dictionary<string, string?>
        {
            ["id_entidad"] = Id(x.IdEntidad),
            ["numero_cuenta_externa"] = IbanValidador.Normalizar(x.NumeroCuenta),
            ["id_tipo_cuenta"] = Id(x.IdTipoCuenta),
            ["id_moneda"] = Id(x.IdMoneda),
        })).ToList();

        return new()
        {
            ["cx"] = porCx, ["ident"] = porIdent, ["dir"] = porDir, ["cont"] = porCont, ["nom"] = porNom, ["act"] = porAct, ["nac"] = porNac,
            ["rf"] = porRf, ["ing"] = porIng, ["orig"] = porOrig, ["pt"] = porPt,
        };
    }

    // ----- Solicitar el alta ---------------------------------------------------------------------

    public async Task<ResultadoOperacion> SolicitarAltaInterlocutorAsync(ActorContext actor, InterlocutorAltaRequest req)
    {
        // Integracion (API): el usuario tecnico es el solicitante; el canal es el de la integracion y NO hay revision manual
        // de duplicados: si hay coincidencias la gestion se crea igual pero obliga a validacion humana.
        if (req.TipoPersona == GrupoServicio.Tipo) return await SolicitarAltaGrupoAsync(actor, req);

        var porApi = actor.EsIntegracion;
        var requiereRevisionHumana = false;
        List<CoincidenciaApi>? coincidenciasApi = null;

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            if (req.TipoPersona is not (null or "Fisica" or "Juridica"))
                return ResultadoOperacion.Fallo(400, "datos_invalidos", "El tipo de interlocutor debe ser Fisica o Juridica.");
            var esOrg = req.TipoPersona == "Juridica";
            if (esOrg && req.IdProspecto is not null)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", "Los prospectos solo aplican a personas; una organizacion se da de alta directamente.");

            var cat = await InterlocutorRepositorio.CargarCatalogosAsync(conexion, tx);
            var (idRolAlta, reglas, erroresRol) = await RolServicio.ValidarRolAltaAsync(conexion, tx, actor.IdCompania, req.IdTipoRol, esOrg ? "Juridica" : "Fisica");
            var errores = esOrg
                ? InterlocutorValidador.ValidarOrganizacion(req.Organizacion, req.Identificaciones, req.Direcciones, req.Contactos, cat, reglas)
                : InterlocutorValidador.Validar(
                    req.Nombre1, req.Nombre2, req.Apellido1, req.Apellido2, req.FechaNacimiento, req.Identificaciones, req.Direcciones, req.Contactos, cat, reglas,
                    req.Identidad ?? new IdentidadPersonaDto(null, null, null, null, null));
            errores.AddRange(erroresRol);
            var esAltaEmpleado = await EmpleadoServicio.EsRolEmpleadoAsync(conexion, tx, idRolAlta);
            if (esAltaEmpleado) errores.AddRange(await EmpleadoServicio.ValidarAsync(conexion, tx, actor.IdCompania, req.Empleado, null));
            errores.AddRange(await PerfilFinancieroServicio.ValidarAsync(conexion, tx, actor.IdCompania, esOrg ? "Juridica" : "Fisica", req.PerfilFinanciero, reglas));

            var paisOk = await conexion.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { p = req.IdPaisRegulador }, tx);
            if (!paisOk) errores.Add("El pais regulador no existe o esta inactivo.");

            var (erroresGeo, direcciones) = await DireccionGeografia.ValidarAsync(conexion, tx, req.Direcciones, reglas.Pide(ReglasCampos.DirNiveles), reglas.Pide(ReglasCampos.DirCodigoPostal));
            errores.AddRange(erroresGeo);

            var idUnidad = req.IdUnidad ?? (porApi ? null : await InterlocutorRepositorio.UnidadRegistroPorDefectoAsync(conexion, tx, actor.IdUsuario.Value));
            var errorUnidad = await InterlocutorRepositorio.ValidarUnidadAsync(conexion, tx, actor.IdCompania, idUnidad, null);
            if (errorUnidad is not null) errores.Add(errorUnidad);

            // El Backoffice siempre es canal Sucursal; una integracion tiene su canal fijo.
            long? idCanal;
            if (porApi)
            {
                idCanal = await conexion.ExecuteScalarAsync<long?>(
                    "SELECT id_canal FROM sec.USUARIO_INTEGRACION WHERE id_usuario_integracion = @i", new { i = actor.IdUsuarioIntegracion }, tx);
                if (idCanal is null) errores.Add("El usuario de integracion no tiene un canal asignado; asignelo en Administracion > Usuarios de integracion.");
            }
            else
            {
                idCanal = await InterlocutorRepositorio.CanalPorCodigoAsync(conexion, tx, "SUCURSAL");
                if (idCanal is null) errores.Add("Falta el canal SUCURSAL en la lista CANAL; correr administracion_transversal.sql.");
            }

            if (errores.Count > 0)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

            // Identificacion ya usada por un cliente, o por otra alta pendiente de aprobacion.
            foreach (var i in req.Identificaciones)
            {
                var dup = await InterlocutorRepositorio.BuscarDuplicadoAsync(conexion, tx, actor.IdCompania, i.IdTipoIdentificacion, i.Numero, null);
                if (dup is not null)
                    return ResultadoOperacion.Fallo(409, "identificacion_duplicada", $"La identificacion {i.Numero.Trim()} ya pertenece al cliente N.º {dup}. Si necesita que asuma otro rol, use «Gestionar roles» en su ficha.",
                        new Dictionary<string, string> { ["identificacion"] = i.Numero.Trim() });

                var pendiente = await conexion.ExecuteScalarAsync<string?>(
                    """
                    SELECT TOP 1 g.numero
                    FROM apr.GESTION g
                    JOIN apr.GESTION_DETALLE dn ON dn.id_gestion = g.id_gestion AND dn.nombre_campo LIKE N'ident[[]+%].numero_identificacion'
                    JOIN apr.GESTION_DETALLE dt ON dt.id_gestion = g.id_gestion
                                               AND dt.nombre_campo = REPLACE(dn.nombre_campo, N'numero_identificacion', N'id_tipo_identificacion')
                    WHERE g.id_compania = @idCompania AND g.entidad_negocio = @entidad AND g.estado = @pend
                      AND dn.valor_nuevo = @numero AND dt.valor_nuevo = @tipo
                    """,
                    new
                    {
                        actor.IdCompania, entidad = EntidadAlta, pend = EstadoPendiente, numero = i.Numero.Trim(),
                        tipo = i.IdTipoIdentificacion.ToString(CultureInfo.InvariantCulture)
                    }, tx);
                if (pendiente is not null)
                    return ResultadoOperacion.Fallo(409, "alta_pendiente_duplicada",
                        $"La identificacion {i.Numero.Trim()} ya esta en el alta pendiente de aprobacion de la gestion {pendiente}.",
                        new Dictionary<string, string> { ["identificacion"] = i.Numero.Trim() });
            }

            // Control de calidad de datos: personas ya registradas (clientes, otras personas, prospectos) parecidas a esta.
            string? resumenDuplicados = null;
            {
                string? CodigoContacto(long idTipo) => cat.Buscar(CatalogosInterlocutor.ListaTipoContacto, idTipo)?.Codigo;
                var contactos = req.Contactos ?? [];
                var coinc = await MotorCoincidencias.BuscarAsync(conexion, tx, actor.IdCompania, new CoincidenciaRequest(
                    req.Nombre1, req.Nombre2, req.Apellido1, req.Apellido2, req.FechaNacimiento?.ToDateTime(TimeOnly.MinValue),
                    req.Identificaciones.Select(i => new IdentificacionCoincidenciaDto(i.IdTipoIdentificacion, i.Numero)).ToList(),
                    contactos.Where(x => CodigoContacto(x.IdTipoContacto)?.StartsWith("TELEFONO", StringComparison.Ordinal) == true || CodigoContacto(x.IdTipoContacto) == "WHATSAPP").Select(x => x.Valor).ToList(),
                    contactos.Where(x => CodigoContacto(x.IdTipoContacto) == "CORREO").Select(x => x.Valor).ToList(),
                    req.IdProspecto, null,
                    esOrg ? "Juridica" : null, req.Organizacion?.RazonSocial,
                    req.Organizacion?.NombresComerciales?.Select(n => n.Nombre).ToList(), req.Organizacion?.FechaConstitucion?.ToDateTime(TimeOnly.MinValue)));
                if (coinc.Items.Count > 0)
                {
                    var rev = req.RevisionDuplicados;
                    if (porApi)
                    {
                        // Sin dialogo con una persona: queda en la gestion y la valida un humano. Marca [API] para la guardia de aprobacion.
                        requiereRevisionHumana = true;
                        coincidenciasApi = coinc.Items.Select(i => new CoincidenciaApi(i.Origen, i.Numero, i.Porcentaje, i.Fuerte, i.Motivos)).ToList();
                        resumenDuplicados = "[API] " + MotorCoincidencias.ResumenAutomatico(coinc);
                    }
                    else if (rev is not { Confirmado: true })
                        return ResultadoOperacion.Fallo(409, "posibles_duplicados", "Se encontraron personas similares ya registradas; revise antes de continuar.", coinc);
                    if (!porApi)
                    {
                        if (string.IsNullOrWhiteSpace(rev!.Motivo) || rev.Motivo.Trim().Length < 10)
                            return ResultadoOperacion.Fallo(400, "motivo_requerido", "Indique el motivo (minimo 10 caracteres) por el que no es la misma persona.", coinc);
                        if (coinc.Items.Any(i => i.Fuerte) && coinc.FuerteBloquea && !actor.TienePermiso(MotorCoincidencias.PermisoAutorizar))
                            return ResultadoOperacion.Fallo(403, "duplicado_fuerte_bloqueado",
                                "Hay una coincidencia fuerte con una persona ya registrada; solo un usuario con permiso para autorizar duplicados puede continuar.", coinc);
                        resumenDuplicados = MotorCoincidencias.Resumen(coinc, rev.Motivo);
                    }
                }
            }

            // Promocion de un prospecto: debe estar vigente y sin otra promocion pendiente.
            if (req.IdProspecto is not null)
            {
                var errorPromo = await ProspectoRepositorio.ValidarPromocionAsync(conexion, tx, actor.IdCompania, req.IdProspecto.Value);
                if (errorPromo is not null) return ResultadoOperacion.Fallo(409, "prospecto_no_promovible", errorPromo);
            }

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoAltaCliente }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoAltaCliente}; correr mejoras_ronda10.sql.");

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio,
                                         llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, NULL, @creadoPor)
                """,
                new
                {
                    actor.IdCompania, numeroGestion, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario,
                    entidad = EntidadAlta, llave = PrefijoLlaveAlta + numeroGestion, creadoPor = actor.NombreDescriptivo
                }, tx);

            var cambios = new List<Cambio>
            {
                new("id_pais_regulador", null, req.IdPaisRegulador.ToString(CultureInfo.InvariantCulture)),
                new("id_unidad", null, idUnidad?.ToString(CultureInfo.InvariantCulture)),
                new("id_canal_origen", null, idCanal?.ToString(CultureInfo.InvariantCulture)),
            };
            if (esOrg)
            {
                var o = req.Organizacion!;
                cambios.Add(new("tipo_persona", null, "Juridica"));
                cambios.Add(new("razon_social", null, V(o.RazonSocial)));
                cambios.Add(new("id_forma_juridica", null, o.IdFormaJuridica.ToString(CultureInfo.InvariantCulture)));
                cambios.Add(new("id_pais_constitucion", null, o.IdPaisConstitucion.ToString(CultureInfo.InvariantCulture)));
                cambios.Add(new("fecha_constitucion", null, Fecha(o.FechaConstitucion)));
                cambios.Add(new("referencia_registral", null, V(o.ReferenciaRegistral)));
                cambios.Add(new("id_estado_legal", null, o.IdEstadoLegal.ToString(CultureInfo.InvariantCulture)));
                cambios.Add(new("fecha_disolucion", null, Fecha(o.FechaDisolucion)));
            }
            else
            {
                cambios.Add(new("nombre1", null, V(req.Nombre1)));
                cambios.Add(new("nombre2", null, V(req.Nombre2)));
                cambios.Add(new("apellido1", null, V(req.Apellido1)));
                cambios.Add(new("apellido2", null, V(req.Apellido2)));
                cambios.Add(new("fecha_nacimiento", null, req.FechaNacimiento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                if (req.Identidad is { } idt)
                {
                    cambios.Add(new("nombre_conocido", null, V(idt.NombreConocido)));
                    cambios.Add(new("id_pais_nacimiento", null, idt.IdPaisNacimiento?.ToString(CultureInfo.InvariantCulture)));
                    cambios.Add(new("id_sexo", null, idt.IdSexo?.ToString(CultureInfo.InvariantCulture)));
                    cambios.Add(new("id_estado_civil", null, idt.IdEstadoCivil?.ToString(CultureInfo.InvariantCulture)));
                }
            }
            if (req.PerfilFinanciero?.Economico is { } ecoAlta)
                foreach (var (k, v) in PerfilEconomicoDeseado(ecoAlta, esOrg))
                    if (v is not null) cambios.Add(new(k, null, v));
            if (resumenDuplicados is not null) cambios.Add(new("dup_revision", null, resumenDuplicados));
            if (req.IdTipoRol is not null && idRolAlta is not null)
                cambios.Add(new(RolServicio.CampoRolAlta, null, idRolAlta.Value.ToString(CultureInfo.InvariantCulture)));
            if (esAltaEmpleado && req.Empleado is not null)
                cambios.AddRange(EmpleadoServicio.Campos(req.Empleado).Select(x => new Cambio(x.Campo, null, x.Valor)));
            if (req.IdProspecto is not null)
                cambios.Add(new(ProspectoRepositorio.CampoVinculo, null, req.IdProspecto.Value.ToString(CultureInfo.InvariantCulture)));
            var pedidos = ConstruirPedidos(req.Identificaciones, direcciones, req.Contactos, cat, esOrg ? req.Organizacion : null, esOrg ? null : req.Identidad, req.PerfilFinanciero);
            foreach (var col in Colecciones)
            {
                var n = 0;
                foreach (var item in pedidos[col.Prefijo])
                {
                    n++;
                    foreach (var (nombre, valor) in item.Campos)
                    {
                        if (valor is null) continue;
                        if (nombre == "es_principal" && valor == "0") continue;
                        cambios.Add(new Cambio($"{col.Prefijo}[+{n}].{nombre}", null, valor));
                    }
                }
            }
            foreach (var c in cambios.Where(c => c.Nuevo is not null))
            {
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, NULL, @nuevo)",
                    new { idGestion, campo = c.Campo, nuevo = c.Nuevo }, tx);
            }

            // El borrador de origen ya se envio: se elimina (si la gestion vuelve devuelta, se corrige desde la gestion).
            if (req.IdBorrador is not null)
                await conexion.ExecuteAsync(
                    "DELETE FROM cli.INTERLOCUTOR_BORRADOR WHERE id_borrador = @id AND id_usuario = @u AND id_compania = @c",
                    new { id = req.IdBorrador, u = actor.IdUsuario, c = actor.IdCompania }, tx);

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value, requiereRevisionHumana);

            var errorDevuelta = await CerrarDevueltaAsync(conexion, tx, actor, req.IdGestionDevuelta, idGestion, EntidadAlta, PrefijoLlaveAlta + numeroGestion);
            if (errorDevuelta is not null) return errorDevuelta;

            if (inicio.AplicarYa)
            {
                var errorAuto = await AplicarAutomaticaAsync(conexion, tx, actor, idGestion, inicio.NombreRegla, SiguienteNumeroDe(actor));
                if (errorAuto is not null) { tx.Rollback(); return errorAuto; }
            }

            tx.Commit();
            return ResultadoOperacion.Ok(idGestion) with { Numero = numeroGestion, Datos = requiereRevisionHumana ? coincidenciasApi : null };
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "identificacion_duplicada", "Ya existe un cliente o un alta pendiente con alguna de esas identificaciones.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Asignador de numeros (CLIENTE, GESTION...) para aplicadores que crean registros.</summary>
    private Func<string, Task<string>> SiguienteNumeroDe(ActorContext actor)
        => objeto => _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, objeto);

    public sealed record EstadoAltaApi(string Numero, string Estado, long? IdCliente);

    /// <summary>
    /// Estado de una alta (gestion CLI_ALTA) para quien la solicito (integracion). Solo ve sus propias gestiones: una gestion ajena
    /// o inexistente es indistinguible (null). El numero puede venir con o sin ceros a la izquierda.
    /// </summary>
    public async Task<EstadoAltaApi?> EstadoAltaDelSolicitanteAsync(ActorContext actor, string numero)
    {
        using var conexion = NuevaConexion();
        var g = (await conexion.QueryAsync<(string numero, string estado, string? llave_registro)>(
            """
            SELECT TOP 1 numero, estado, llave_registro FROM apr.GESTION
            WHERE id_compania = @idCompania AND id_usuario_solicitante = @idUsuario AND entidad_negocio = @e
              AND (numero = @n OR (TRY_CAST(numero AS bigint) IS NOT NULL AND TRY_CAST(numero AS bigint) = TRY_CAST(@n AS bigint)))
            """, new { actor.IdCompania, actor.IdUsuario, e = EntidadAlta, n = numero.Trim() })).FirstOrDefault();
        if (g.numero is null) return null;
        long? idCliente = g.estado == "Aplicada" && long.TryParse(g.llave_registro, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        return new EstadoAltaApi(g.numero, g.estado, idCliente);
    }

    public async Task<string?> NumeroClienteAsync(long idInterlocutor)
    {
        using var conexion = NuevaConexion();
        return await conexion.ExecuteScalarAsync<string?>("SELECT numero FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor });
    }

    /// <summary>Id del cliente creado por una gestion de alta ya aplicada, o null si sigue pendiente.</summary>
    public async Task<long?> ClienteCreadoPorAltaAsync(long idGestion)
    {
        using var conexion = NuevaConexion();
        var llave = await conexion.ExecuteScalarAsync<string?>(
            "SELECT llave_registro FROM apr.GESTION WHERE id_gestion = @idGestion AND entidad_negocio = @e AND estado = 'Aplicada'",
            new { idGestion, e = EntidadAlta });
        return long.TryParse(llave, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    // ----- Aplicar (al aprobar el ultimo nivel) -------------------------------------------------

    private static string? Dato(Dictionary<string, string?> d, string k) => d.TryGetValue(k, out var v) ? v : null;
    private static long? Num(string? v) => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    private static DateOnly? Fch(string? v) => DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    private static async Task<ResultadoOperacion?> AplicarAltaInterlocutorAsync(
        SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string solicitadoPor, Func<string, Task<string>>? siguienteNumero)
    {
        if (siguienteNumero is null)
            return ResultadoOperacion.Fallo(500, "numeracion_no_disponible", "No se pudo asignar el numero de cliente.");

        var filas = (await c.QueryAsync(
            "SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion ORDER BY id_gestion_detalle", new { idGestion }, tx)).ToList();

        var nucleo = new Dictionary<string, string?>();
        var hijos = new Dictionary<string, SortedDictionary<int, Dictionary<string, string?>>> { ["ident"] = new(), ["dir"] = new(), ["cont"] = new(), ["nom"] = new(), ["act"] = new(), ["nac"] = new(), ["rf"] = new(), ["ing"] = new(), ["orig"] = new(), ["pt"] = new(), ["cx"] = new() };
        foreach (var f in filas)
        {
            var campo = (string)f.nombre_campo;
            var m = PatronCampoHijo.Match(campo);
            if (!m.Success) { nucleo[campo] = (string?)f.valor_nuevo; continue; }
            var lista = hijos[m.Groups[1].Value];
            var n = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (!lista.TryGetValue(n, out var d)) lista[n] = d = new();
            d[m.Groups[4].Value] = (string?)f.valor_nuevo;
        }

        var identificaciones = hijos["ident"].Values.Select(d => new IdentificacionDto(
            null, Num(Dato(d, "id_tipo_identificacion")) ?? 0, Dato(d, "numero_identificacion") ?? "", Num(Dato(d, "id_institucion_emisora")),
            Fch(Dato(d, "fecha_emision")), Fch(Dato(d, "fecha_vencimiento")), Dato(d, "es_principal") == "1")).ToList();
        var direcciones = hijos["dir"].Values.Select(d => new DireccionDto(
            null, Num(Dato(d, "id_tipo_direccion")) ?? 0, Num(Dato(d, "id_pais")) ?? 0,
            Enumerable.Range(1, 4).Select(k => new NivelDireccionDto(Num(Dato(d, $"nivel{k}_id")), Dato(d, $"nivel{k}_texto"))).ToList(),
            Dato(d, "codigo_postal"), Dato(d, "detalle") ?? "", Dato(d, "es_principal") == "1", Dato(d, "detalle2"), Dato(d, "detalle3"))).ToList();
        var contactos = hijos["cont"].Values.Select(d => new ContactoDto(
            null, Num(Dato(d, "id_tipo_contacto")) ?? 0, Dato(d, "valor") ?? "", Dato(d, "es_principal") == "1")).ToList();

        if (Dato(nucleo, "tipo_persona") == GrupoServicio.Tipo)
            return await AplicarAltaGrupoAsync(c, tx, actor, idGestion, solicitadoPor, siguienteNumero, nucleo,
                filas.Select(f => ((string)f.nombre_campo, (string?)f.valor_nuevo)).ToList());

        var esOrg = Dato(nucleo, "tipo_persona") == "Juridica";
        OrganizacionDto? organizacion = null;
        if (esOrg)
            organizacion = new OrganizacionDto(
                Dato(nucleo, "razon_social") ?? "",
                hijos["nom"].Values.Select(d => new NombreComercialDto(null, Dato(d, "nombre") ?? "")).ToList(),
                Num(Dato(nucleo, "id_forma_juridica")) ?? 0, Num(Dato(nucleo, "id_pais_constitucion")) ?? 0,
                Fch(Dato(nucleo, "fecha_constitucion")), Dato(nucleo, "referencia_registral"),
                Num(Dato(nucleo, "id_estado_legal")) ?? 0, Fch(Dato(nucleo, "fecha_disolucion")),
                hijos["act"].Values.Select(d => new ActividadDto(null, Num(Dato(d, "id_actividad")) ?? 0, Dato(d, "descripcion"),
                    Dato(d, "es_principal") == "1", Fch(Dato(d, "fecha_inicio")))).ToList());

        var identidad = esOrg ? null : new IdentidadPersonaDto(
            Dato(nucleo, "nombre_conocido"), Num(Dato(nucleo, "id_pais_nacimiento")), Num(Dato(nucleo, "id_sexo")), Num(Dato(nucleo, "id_estado_civil")),
            hijos["nac"].Values.Select(d => new NacionalidadDto(null, Num(Dato(d, "id_pais")) ?? 0)).ToList());

        static decimal? Dec(string? v) => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : null;
        var perfil = new PerfilFinancieroDto(
            new PerfilEconomicoDto(
                Num(Dato(nucleo, "pe_id_condicion_laboral")), Num(Dato(nucleo, "pe_id_ocupacion")), Dato(nucleo, "pe_empleador"), Dec(Dato(nucleo, "pe_gastos_estimados")),
                Dec(Dato(nucleo, "pe_activos_declarados")), Dec(Dato(nucleo, "pe_pasivos_declarados")), Dec(Dato(nucleo, "pe_patrimonio_declarado")),
                Num(Dato(nucleo, "pe_id_moneda_balance")), Fch(Dato(nucleo, "pe_fecha_corte"))),
            hijos["rf"].Values.Select(d => new ResidenciaFiscalDto(null, Num(Dato(d, "id_pais")) ?? 0, Dato(d, "identificador_tributario"), Dato(d, "es_principal") == "1",
                Fch(Dato(d, "fecha_desde")), Fch(Dato(d, "fecha_hasta")))).ToList(),
            hijos["ing"].Values.Select(d => new IngresoDto(null, Num(Dato(d, "id_tipo_ingreso")) ?? 0, Dec(Dato(d, "monto")) ?? 0, Num(Dato(d, "id_moneda")) ?? 0, Num(Dato(d, "id_periodicidad")) ?? 0)).ToList(),
            hijos["orig"].Values.Select(d => new OrigenDto(null, Dato(d, "clase") ?? "", Num(Dato(d, "id_origen")) ?? 0)).ToList(),
            hijos["pt"].Values.Select(d => new PerfilTransaccionalDto(null, Num(Dato(d, "id_tipo_transaccion")) ?? 0, Num(Dato(d, "id_frecuencia")) ?? 0, Dec(Dato(d, "monto_minimo")), Dec(Dato(d, "monto_maximo")),
                Num(Dato(d, "id_moneda")), Num(Dato(d, "id_pais_origen")), Num(Dato(d, "id_pais_destino")))).ToList(),
            hijos["cx"].Values.Select(d => new CuentaExternaDto(null, Num(Dato(d, "id_entidad")) ?? 0, Dato(d, "numero_cuenta_externa") ?? "", Num(Dato(d, "id_tipo_cuenta")) ?? 0, Num(Dato(d, "id_moneda")))).ToList());

        var req = new InterlocutorAltaRequest(
            Num(Dato(nucleo, "id_pais_regulador")) ?? 0, Num(Dato(nucleo, "id_unidad")),
            Dato(nucleo, "nombre1") ?? "", Dato(nucleo, "nombre2"), Dato(nucleo, "apellido1") ?? "", Dato(nucleo, "apellido2"),
            Fch(Dato(nucleo, "fecha_nacimiento")), identificaciones, direcciones, contactos, null,
            TipoPersona: esOrg ? "Juridica" : "Fisica", Organizacion: organizacion, Identidad: identidad, PerfilFinanciero: perfil);
        var idCanal = Num(Dato(nucleo, "id_canal_origen"));

        // Se vuelve a validar: entre la solicitud y la aprobacion pudo cambiar un catalogo o crearse otro cliente igual.
        var cat = await InterlocutorRepositorio.CargarCatalogosAsync(c, tx);
        var (idRolAlta, reglas, erroresRol) = await RolServicio.ValidarRolAltaAsync(c, tx, actor.IdCompania, Num(Dato(nucleo, RolServicio.CampoRolAlta)), esOrg ? "Juridica" : "Fisica");
        var errores = esOrg
            ? InterlocutorValidador.ValidarOrganizacion(organizacion, identificaciones, direcciones, contactos, cat, reglas)
            : InterlocutorValidador.Validar(req.Nombre1, req.Nombre2, req.Apellido1, req.Apellido2, req.FechaNacimiento, identificaciones, direcciones, contactos, cat, reglas, identidad);
        errores.AddRange(erroresRol);
        var esAltaEmpleado = await EmpleadoServicio.EsRolEmpleadoAsync(c, tx, idRolAlta);
        var datosEmpleado = esAltaEmpleado ? new EmpleadoDatosDto(Num(Dato(nucleo, EmpleadoServicio.CampoUnidad)) ?? 0, Dato(nucleo, EmpleadoServicio.CampoNumero) ?? "") : null;
        if (esAltaEmpleado) errores.AddRange(await EmpleadoServicio.ValidarAsync(c, tx, actor.IdCompania, datosEmpleado, null));
        errores.AddRange(await PerfilFinancieroServicio.ValidarAsync(c, tx, actor.IdCompania, esOrg ? "Juridica" : "Fisica", perfil, reglas));
        var paisOk = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END", new { p = req.IdPaisRegulador }, tx);
        if (!paisOk) errores.Add("El pais regulador no existe o esta inactivo.");
        var (erroresGeo, dirNorm) = await DireccionGeografia.ValidarAsync(c, tx, direcciones, reglas.Pide(ReglasCampos.DirNiveles), reglas.Pide(ReglasCampos.DirCodigoPostal));
        errores.AddRange(erroresGeo);
        var errorUnidad = await InterlocutorRepositorio.ValidarUnidadAsync(c, tx, actor.IdCompania, req.IdUnidad, null);
        if (errorUnidad is not null) errores.Add(errorUnidad);
        var errorCanal = await InterlocutorRepositorio.ValidarCanalAsync(c, tx, idCanal, null);
        if (errorCanal is not null) errores.Add(errorCanal);
        if (errores.Count > 0)
            return ResultadoOperacion.Fallo(409, "alta_no_aplicable", "El alta ya no es valida: " + string.Join(" ", errores) + " Rechace o devuelva la gestion.");

        foreach (var i in identificaciones)
        {
            var dup = await InterlocutorRepositorio.BuscarDuplicadoAsync(c, tx, actor.IdCompania, i.IdTipoIdentificacion, i.Numero, null);
            if (dup is not null)
                return ResultadoOperacion.Fallo(409, "identificacion_duplicada",
                    $"La identificacion {i.Numero.Trim()} ya pertenece al cliente N.º {dup}; rechace o devuelva la gestion.");
        }

        // Promocion de prospecto: sigue vigente? (entre la solicitud y la aprobacion pudo descartarse)
        var idProspecto = Num(Dato(nucleo, ProspectoRepositorio.CampoVinculo));
        if (idProspecto is not null)
        {
            var estadoPro = await c.ExecuteScalarAsync<string?>(
                "SELECT estado FROM cli.INTERLOCUTOR_PROSPECTO WHERE id_interlocutor_prospecto = @id", new { id = idProspecto }, tx);
            if (estadoPro != "Prospecto")
                return ResultadoOperacion.Fallo(409, "alta_no_aplicable", "El prospecto de origen ya no esta vigente; rechace o devuelva la gestion.");
        }

        var idRolCliente = idRolAlta;
        if (idRolCliente is null)
            return ResultadoOperacion.Fallo(500, "rol_no_configurado", "Falta el valor CLIENTE en la lista TIPO_ROL_INTERLOCUTOR; correr interlocutor_multiples.sql.");

        string numero;
        try { numero = await siguienteNumero("CLIENTE"); }
        catch (NumeracionException ex) { return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

        var idInterlocutor = await InterlocutorRepositorio.InsertarClienteAsync(
            c, tx, actor.IdCompania, numero, req, dirNorm, cat, idRolCliente.Value, req.IdUnidad, idCanal, solicitadoPor);
        await PerfilFinancieroServicio.InsertarAsync(c, tx, idInterlocutor, perfil, solicitadoPor);
        if (datosEmpleado is not null) await EmpleadoServicio.AplicarAsync(c, tx, actor, idInterlocutor, idRolCliente.Value, datosEmpleado, idGestion, solicitadoPor);

        // La gestion queda apuntando al cliente creado.
        await c.ExecuteAsync(
            "UPDATE apr.GESTION SET llave_registro = @llave WHERE id_gestion = @idGestion",
            new { idGestion, llave = idInterlocutor.ToString(CultureInfo.InvariantCulture) }, tx);
        await Auditoria.CambioAsync(c, tx, actor, EntidadInterlocutor, idInterlocutor, "alta", null,
            $"Cliente N.º {numero} creado por la gestion de alta", idGestion);
        if (idProspecto is not null)
            await ProspectoRepositorio.MarcarConvertidoAsync(c, tx, actor, idProspecto.Value, idInterlocutor, idGestion);
        return null;
    }
}
