using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Roles del interlocutor (Cliente, Firmante, Beneficiario, Fiador, Codeudor, Autorizado). Un rol se agrega o se quita SIEMPRE por gestion
/// (CLI_ROL) y cada rol puede exigir datos propios (cli.TIPO_ROL_REQUISITO: direccion, correo, telefono). Quien ya es cliente conserva su
/// registro unico: asumir otro rol no duplica a la persona. Tambien aplica al alta de una persona que solo sera, por ejemplo, Firmante.
/// Punto de extension: cuando existan Cuentas y Creditos, quitar un rol debe validar que no haya productos que dependan de el.
/// </summary>
public static class RolServicio
{
    public const string Entidad = "INTERLOCUTOR_ROL";
    public const string PrefijoAgregar = "rol_add[";
    public const string PrefijoQuitar = "rol_del[";
    public const string CampoMotivo = "rol_motivo";
    public const string CampoRolAlta = "rol_alta";

    private static readonly string[] CodigosTelefono = ["TELEFONO_MOVIL", "TELEFONO_FIJO", "WHATSAPP"];

    public sealed record RolItem(long IdTipoRol, string Codigo, string Nombre, string Estado);

    public static async Task<List<RolItem>> CatalogoRolesAsync(SqlConnection c, SqlTransaction? tx)
        => (await c.QueryAsync(
            """
            SELECT i.id_lista_valores_item AS id, i.codigo, i.nombre, i.estado
            FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = N'TIPO_ROL_INTERLOCUTOR' ORDER BY i.orden, i.nombre
            """, null, tx)).Select(x => new RolItem((long)x.id, (string)x.codigo, (string)x.nombre, (string)x.estado)).ToList();

    /// <summary>Campos que exige un rol. Si el rol nunca se configuro (sin filas) se usan las reglas de siempre.</summary>
    public static async Task<ReglasCampos> RequisitosAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idTipoRol, string tipoPersona = "Fisica")
    {
        var filas = (await c.QueryAsync(
            "SELECT requisito, es_requerido FROM cli.TIPO_ROL_REQUISITO WHERE id_compania = @idCompania AND id_tipo_rol = @idTipoRol AND tipo_persona = @tipoPersona",
            new { idCompania, idTipoRol, tipoPersona }, tx)).ToList();
        var documentos = await DocumentoRequisitoServicio.TiposExigidosAsync(c, tx, idCompania, idTipoRol, tipoPersona);
        if (filas.Count == 0) return new ReglasCampos(ReglasCampos.LegadoDe(tipoPersona).Lista, documentos);
        return new ReglasCampos(filas.Where(x => (bool)x.es_requerido).Select(x => (string)x.requisito), documentos);
    }

    /// <summary>Union de lo que exigen varios roles: una persona con varios roles debe cumplir todos.</summary>
    public static async Task<ReglasCampos> ReglasDeRolesAsync(SqlConnection c, SqlTransaction? tx, long idCompania, IEnumerable<long> idsRol, string tipoPersona = "Fisica")
    {
        var total = new ReglasCampos([]);
        var hubo = false;
        foreach (var id in idsRol.Distinct())
        {
            total = total.Union(await RequisitosAsync(c, tx, idCompania, id, tipoPersona));
            hubo = true;
        }
        return hubo ? total : ReglasCampos.LegadoDe(tipoPersona);
    }

    /// <summary>Reglas que debe cumplir un cliente ya registrado al modificar sus datos: la union de sus roles activos.</summary>
    public static async Task<ReglasCampos> ReglasDeClienteAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor)
    {
        var roles = await c.QueryAsync<long>("SELECT id_tipo_rol FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo'", new { idInterlocutor }, tx);
        var tipo = await c.ExecuteScalarAsync<string?>("SELECT tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor }, tx) ?? "Fisica";
        return await ReglasDeRolesAsync(c, tx, idCompania, roles, tipo == "Juridica" ? "Juridica" : "Fisica");
    }

    /// <summary>Datos que le faltan a un cliente ya registrado (registros activos) para cumplir las reglas indicadas.</summary>
    public static async Task<List<string>> FaltantesEnClienteAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor, ReglasCampos r)
    {
        var faltan = new List<string>();
        static bool Vacio(object? v) => v is null || (v is string s && string.IsNullOrWhiteSpace(s));

        var p = await c.QuerySingleOrDefaultAsync("SELECT nombre2, apellido2, fecha_nacimiento, nombre_conocido, id_pais_nacimiento, id_sexo, id_estado_civil FROM cli.INTERLOCUTOR_FISICA WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor }, tx);
        if (p is not null)
        {
            if (r.Pide(ReglasCampos.Nombre2) && Vacio((object?)p.nombre2)) faltan.Add("el segundo nombre");
            if (r.Pide(ReglasCampos.Apellido2) && Vacio((object?)p.apellido2)) faltan.Add("el segundo apellido");
            if (r.Pide(ReglasCampos.FechaNacimiento) && p.fecha_nacimiento is null) faltan.Add("la fecha de nacimiento");
            if (r.Pide(ReglasCampos.NombreConocido) && Vacio((object?)p.nombre_conocido)) faltan.Add("el nombre conocido");
            if (r.Pide(ReglasCampos.PaisNacimiento) && p.id_pais_nacimiento is null) faltan.Add("el país de nacimiento");
            if (r.Pide(ReglasCampos.Sexo) && p.id_sexo is null) faltan.Add("el sexo");
            if (r.Pide(ReglasCampos.EstadoCivil) && p.id_estado_civil is null) faltan.Add("el estado civil");
            if (r.Pide(ReglasCampos.Nacionalidad) && await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cli.INTERLOCUTOR_NACIONALIDAD WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo'", new { idInterlocutor }, tx) == 0) faltan.Add("al menos una nacionalidad");
        }

        var o = await c.QuerySingleOrDefaultAsync("SELECT fecha_constitucion, referencia_registral FROM cli.INTERLOCUTOR_JURIDICA WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor }, tx);
        if (o is not null)
        {
            if (r.Pide(ReglasCampos.OrgFechaConstitucion) && o.fecha_constitucion is null) faltan.Add("la fecha de constitución");
            if (r.Pide(ReglasCampos.OrgReferenciaRegistral) && Vacio((object?)o.referencia_registral)) faltan.Add("la referencia registral");
            if (r.Pide(ReglasCampos.OrgNombreComercial) && await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cli.INTERLOCUTOR_NOMBRE_COMERCIAL WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo'", new { idInterlocutor }, tx) == 0) faltan.Add("al menos un nombre comercial");
            if (r.Pide(ReglasCampos.OrgActividad) && await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cli.INTERLOCUTOR_ACTIVIDAD WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo'", new { idInterlocutor }, tx) == 0) faltan.Add("al menos una actividad económica");
        }

        var idents = (await c.QueryAsync(
            "SELECT id_institucion_emisora, fecha_emision, fecha_vencimiento FROM cli.INTERLOCUTOR_IDENTIFICACION WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo'",
            new { idInterlocutor }, tx)).ToList();
        if (r.Pide(ReglasCampos.IdentInstitucion) && idents.Any(i => i.id_institucion_emisora is null)) faltan.Add("la institución emisora de la identificación");
        if (r.Pide(ReglasCampos.IdentFechaEmision) && idents.Any(i => i.fecha_emision is null)) faltan.Add("la fecha de emisión de la identificación");
        if (r.Pide(ReglasCampos.IdentFechaVencimiento) && idents.Any(i => i.fecha_vencimiento is null)) faltan.Add("la fecha de vencimiento de la identificación");

        var dirs = (await c.QueryAsync(
            """
            SELECT id_tipo_direccion, id_pais, nivel1_id, nivel2_id, nivel3_id, nivel4_id, nivel1_texto, nivel2_texto, nivel3_texto, nivel4_texto,
                   codigo_postal, detalle, detalle2, detalle3, es_principal
            FROM cli.INTERLOCUTOR_DIRECCION WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo'
            """, new { idInterlocutor }, tx)).ToList();
        if (r.Pide(ReglasCampos.Direccion) && dirs.Count == 0) faltan.Add("al menos una dirección");
        if (r.Pide(ReglasCampos.DirLinea2) && dirs.Any(d => Vacio((object?)d.detalle2))) faltan.Add("la línea 2 de la dirección");
        if (r.Pide(ReglasCampos.DirLinea3) && dirs.Any(d => Vacio((object?)d.detalle3))) faltan.Add("la línea 3 de la dirección");
        if (dirs.Count > 0 && (r.Pide(ReglasCampos.DirNiveles) || r.Pide(ReglasCampos.DirCodigoPostal)))
        {
            var dtos = dirs.Select(d => new DireccionDto(null, (long)d.id_tipo_direccion, (long)d.id_pais,
                new List<NivelDireccionDto>
                {
                    new((long?)d.nivel1_id, (string?)d.nivel1_texto), new((long?)d.nivel2_id, (string?)d.nivel2_texto),
                    new((long?)d.nivel3_id, (string?)d.nivel3_texto), new((long?)d.nivel4_id, (string?)d.nivel4_texto),
                },
                (string?)d.codigo_postal, (string)d.detalle, (bool)d.es_principal, (string?)d.detalle2, (string?)d.detalle3)).ToList();
            var (errGeo, _) = await DireccionGeografia.ValidarAsync(c, tx, dtos, r.Pide(ReglasCampos.DirNiveles), r.Pide(ReglasCampos.DirCodigoPostal));
            faltan.AddRange(errGeo.Select(e => e.TrimEnd('.').Replace("Direccion ", "dirección ", StringComparison.Ordinal)));
        }

        var codigos = (await c.QueryAsync<string>(
            """
            SELECT it.codigo FROM cli.INTERLOCUTOR_CONTACTO k JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = k.id_tipo_contacto
            WHERE k.id_interlocutor = @idInterlocutor AND k.estado = 'Activo'
            """, new { idInterlocutor }, tx)).ToList();
        if (r.Pide(ReglasCampos.Correo) && !codigos.Contains("CORREO")) faltan.Add("un correo electrónico");
        if (r.Pide(ReglasCampos.Telefono) && !codigos.Any(x => CodigosTelefono.Contains(x))) faltan.Add("un teléfono");

        var tipoPersona = o is not null ? "Juridica" : "Fisica";
        faltan.AddRange(await PerfilFinancieroServicio.FaltantesAsync(c, tx, idInterlocutor, tipoPersona, r));
        faltan.AddRange(await CumplimientoServicio.FaltantesAsync(c, tx, idInterlocutor, r));
        faltan.AddRange(await DocumentoRequisitoServicio.FaltantesAsync(c, tx, idInterlocutor, r.Documentos));
        return faltan;
    }

    public static string MensajeFaltantes(string rolNombre, IEnumerable<string> faltantes)
        => $"Para el rol {rolNombre} debe registrar: {string.Join(", ", faltantes)}.";

    /// <summary>Roles que una organizacion (persona juridica) NO puede asumir (decision 13-oct-2026). Puede ser Cliente (y luego Deudor / Cuentahabiente).</summary>
    public static readonly IReadOnlySet<string> RolesNoAplicanAOrganizacion =
        new HashSet<string> { "BENEFICIARIO", "FIADOR", "CODEUDOR", "FIRMANTE", "AUTORIZADO", "EMPLEADO" };

    /// <summary>Roles de producto: se solicitan sobre un cliente ya registrado (no en el alta) y exigen el rol prerrequisito (Cliente Básico).</summary>
    public static readonly IReadOnlyDictionary<string, string> Prerrequisitos =
        new Dictionary<string, string> { ["CLIENTE_DEUDOR"] = "CLIENTE", ["CLIENTE_CUENTAHABIENTE"] = "CLIENTE" };

    public static bool EsRolDeProducto(string codigoRol) => Prerrequisitos.ContainsKey(codigoRol);

    /// <summary>Un grupo no asume ningún rol; una organización no asume los roles de persona (decisión 13-oct-2026).</summary>
    public static bool AplicaATipo(string codigoRol, string tipoPersona)
        => tipoPersona != "Grupo" && !(tipoPersona == "Juridica" && RolesNoAplicanAOrganizacion.Contains(codigoRol));

    /// <summary>
    /// Rol con el que se da de alta a la persona (null = CLIENTE). Devuelve el id del rol, las reglas de campos que exige y los errores:
    /// el rol debe existir y estar activo. Los datos que exige se validan junto con el resto del alta (InterlocutorValidador).
    /// </summary>
    public static async Task<(long? IdRol, ReglasCampos Reglas, List<string> Errores)> ValidarRolAltaAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, long? idTipoRol, string tipoPersona = "Fisica")
    {
        var errores = new List<string>();
        var roles = await CatalogoRolesAsync(c, tx);
        RolItem? rol = idTipoRol is null
            ? roles.FirstOrDefault(r => r.Codigo == "CLIENTE")
            : roles.FirstOrDefault(r => r.IdTipoRol == idTipoRol);
        if (rol is null)
        {
            errores.Add(idTipoRol is null ? "Falta el valor CLIENTE en la lista TIPO_ROL_INTERLOCUTOR." : "El rol indicado no existe.");
            return (null, ReglasCampos.LegadoDe(tipoPersona), errores);
        }
        if (rol.Estado != "Activo") { errores.Add($"El rol {rol.Nombre} esta inactivo."); return (null, ReglasCampos.LegadoDe(tipoPersona), errores); }
        if (!AplicaATipo(rol.Codigo, tipoPersona)) { errores.Add($"El rol {rol.Nombre} no aplica a una organización."); return (null, ReglasCampos.LegadoDe(tipoPersona), errores); }
        if (EsRolDeProducto(rol.Codigo)) { errores.Add($"El rol {rol.Nombre} se solicita sobre un cliente ya registrado, después del alta."); return (null, ReglasCampos.LegadoDe(tipoPersona), errores); }
        return (rol.IdTipoRol, await RequisitosAsync(c, tx, idCompania, rol.IdTipoRol, tipoPersona), errores);
    }

    // ----- Validacion de un cambio de roles -----------------------------------------------------

    public sealed record Calculo(List<string> Errores, string Numero, string Nombre, List<RolItem> Roles);

    public static async Task<Calculo> ValidarCambioAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor,
        IReadOnlyCollection<long> agregar, IReadOnlyCollection<long> quitar, long? idGestionActual)
    {
        var errores = new List<string>();
        var catalogo = await CatalogoRolesAsync(c, tx);

        var cli = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.numero, i.estado, i.tipo_persona, f.nombre1, f.nombre2, f.apellido1, f.apellido2, j.nombre_org1
            FROM cli.INTERLOCUTOR i LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania
            """, new { idInterlocutor, idCompania }, tx);
        if (cli is null) { errores.Add("El cliente no existe."); return new(errores, "", "", catalogo); }

        var numero = (string)cli.numero;
        var nombre = (string?)cli.nombre_org1 ?? string.Join(' ', new[] { (string?)cli.nombre1, (string?)cli.nombre2, (string?)cli.apellido1, (string?)cli.apellido2 }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if ((string)cli.estado != "Activo") errores.Add($"El cliente N.º {numero} está '{(string)cli.estado}' y no admite cambios de rol.");
        if ((string)cli.tipo_persona == "Grupo") errores.Add("Un grupo no recibe roles de cliente.");
        else if ((string)cli.tipo_persona != "Fisica")
        {
            var noAplican = agregar.Select(id => catalogo.FirstOrDefault(x => x.IdTipoRol == id)).Where(r => r is not null && !AplicaATipo(r.Codigo, (string)cli.tipo_persona)).Select(r => r!.Nombre).ToList();
            if (noAplican.Count > 0) errores.Add($"Una organización no puede asumir los roles: {string.Join(", ", noAplican)}.");
        }

        var pend = await FusionServicio.GestionPendienteAsync(c, tx, idCompania, idInterlocutor, idGestionActual);
        if (pend is not null) errores.Add($"El cliente N.º {numero} tiene la gestión {pend} pendiente de aprobación; debe resolverse antes.");

        if (agregar.Count == 0 && quitar.Count == 0) errores.Add("Indique al menos un rol a agregar o a quitar.");
        if (agregar.Intersect(quitar).Any()) errores.Add("Un mismo rol no puede agregarse y quitarse a la vez.");

        var actuales = (await c.QueryAsync("SELECT id_tipo_rol, estado FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor }, tx))
            .ToDictionary(x => (long)x.id_tipo_rol, x => (string)x.estado);
        var activos = actuales.Where(x => x.Value == "Activo").Select(x => x.Key).ToHashSet();

        foreach (var id in agregar)
        {
            var rol = catalogo.FirstOrDefault(x => x.IdTipoRol == id);
            if (rol is null) { errores.Add($"El rol #{id} no existe."); continue; }
            if (rol.Estado != "Activo") { errores.Add($"El rol {rol.Nombre} está inactivo."); continue; }
            if (activos.Contains(id)) { errores.Add($"El cliente ya tiene el rol {rol.Nombre}."); continue; }
            if (Prerrequisitos.TryGetValue(rol.Codigo, out var codPre))
            {
                var pre = catalogo.FirstOrDefault(x => x.Codigo == codPre);
                var tendra = pre is not null && (activos.Contains(pre.IdTipoRol) || agregar.Contains(pre.IdTipoRol)) && !quitar.Contains(pre.IdTipoRol);
                if (!tendra) { errores.Add($"Para asumir el rol {rol.Nombre} el cliente debe tener el rol {pre?.Nombre ?? codPre}."); continue; }
            }
            var faltan = await FaltantesEnClienteAsync(c, tx, idInterlocutor, await RequisitosAsync(c, tx, idCompania, id, (string)cli.tipo_persona == "Juridica" ? "Juridica" : "Fisica"));
            if (faltan.Count > 0)
                errores.Add($"{MensajeFaltantes(rol.Nombre, faltan)} Complete los datos del cliente (Modificar datos) y vuelva a solicitar el rol.");
        }
        foreach (var id in quitar)
        {
            var rol = catalogo.FirstOrDefault(x => x.IdTipoRol == id);
            if (rol is null) { errores.Add($"El rol #{id} no existe."); continue; }
            if (rol.Codigo == "CLIENTE") { errores.Add("El rol Cliente Básico no se puede quitar desde aquí."); continue; }
            if (!activos.Contains(id)) errores.Add($"El cliente no tiene el rol {rol.Nombre} activo.");
        }
        var quedan = activos.Except(quitar).Union(agregar.Where(id => catalogo.Any(x => x.IdTipoRol == id))).Count();
        if (quitar.Count > 0 && quedan == 0) errores.Add("El cliente debe conservar al menos un rol activo.");

        return new(errores, numero, nombre, catalogo);
    }

    // ----- Historial de periodos ------------------------------------------------------------------

    /// <summary>
    /// Vuelve a activar un rol que estaba inactivo: el periodo anterior (desde su asignacion hasta su baja) se guarda en
    /// cli.INTERLOCUTOR_ROL_PERIODO y arranca un periodo nuevo. Asi se conserva «fue fiador del X al Y» aunque lo vuelva a ser.
    /// </summary>
    public static async Task ReactivarAsync(SqlConnection c, SqlTransaction tx, long idInterlocutorRol, long? idGestion, string porQuien)
    {
        await c.ExecuteAsync(
            """
            INSERT INTO cli.INTERLOCUTOR_ROL_PERIODO (id_interlocutor_rol, fecha_desde, fecha_hasta, id_gestion, creado_por)
            SELECT id_interlocutor_rol, fecha_asignacion, ISNULL(fecha_baja, ISNULL(modificado_en, fecha_asignacion)), @idGestion, @por
            FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor_rol = @id AND estado = 'Inactivo'
            """, new { id = idInterlocutorRol, idGestion, por = porQuien }, tx);
        await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR_ROL SET estado = 'Activo', fecha_asignacion = SYSUTCDATETIME(), fecha_baja = NULL, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor_rol = @id",
            new { id = idInterlocutorRol, por = porQuien }, tx);
    }

    /// <summary>Todos los periodos de rol de la persona (cerrados y vigentes), del mas reciente al mas antiguo.</summary>
    public static async Task<List<RolPeriodoResponse>> HistorialAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor)
        => (await c.QueryAsync(
            """
            SELECT id_tipo_rol, codigo, nombre, desde, hasta FROM (
                SELECT r.id_tipo_rol, it.codigo, it.nombre, p.fecha_desde AS desde, p.fecha_hasta AS hasta
                FROM cli.INTERLOCUTOR_ROL_PERIODO p
                JOIN cli.INTERLOCUTOR_ROL r ON r.id_interlocutor_rol = p.id_interlocutor_rol
                JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
                WHERE r.id_interlocutor = @idInterlocutor
                UNION ALL
                SELECT r.id_tipo_rol, it.codigo, it.nombre, r.fecha_asignacion,
                       CASE WHEN r.estado = 'Inactivo' THEN ISNULL(r.fecha_baja, ISNULL(r.modificado_en, r.fecha_asignacion)) END
                FROM cli.INTERLOCUTOR_ROL r
                JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
                WHERE r.id_interlocutor = @idInterlocutor
            ) x ORDER BY desde DESC, nombre
            """, new { idInterlocutor }, tx))
            .Select(x => new RolPeriodoResponse((long)x.id_tipo_rol, (string)x.codigo, (string)x.nombre, (DateTime)x.desde, (DateTime?)x.hasta)).ToList();

    // ----- Aplicar (al aprobar) -----------------------------------------------------------------

    public static (List<long> Agregar, List<long> Quitar, string Motivo) LeerDetalle(IEnumerable<(string Campo, string? Valor)> filas)
    {
        var agregar = new List<long>(); var quitar = new List<long>(); var motivo = "";
        foreach (var (campo, valor) in filas)
        {
            if (campo.StartsWith(PrefijoAgregar, StringComparison.Ordinal) && long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)) agregar.Add(a);
            else if (campo.StartsWith(PrefijoQuitar, StringComparison.Ordinal) && long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)) quitar.Add(q);
            else if (campo == CampoMotivo) motivo = valor ?? "";
        }
        return (agregar, quitar, motivo);
    }

    public static async Task<ResultadoOperacion?> AplicarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, long idInterlocutor, string porQuien)
    {
        var filas = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion ORDER BY id_gestion_detalle", new { idGestion }, tx))
            .Select(x => ((string)x.nombre_campo, (string?)x.valor_nuevo)).ToList();
        var (agregar, quitar, _) = LeerDetalle(filas);

        // Se vuelve a validar: entre la solicitud y la aprobacion pudo cambiar el cliente o el catalogo de roles.
        var calc = await ValidarCambioAsync(c, tx, actor.IdCompania, idInterlocutor, agregar, quitar, idGestion);
        if (calc.Errores.Count > 0)
            return ResultadoOperacion.Fallo(409, "rol_no_aplicable", "El cambio de roles ya no es válido: " + string.Join(" ", calc.Errores) + " Rechace o devuelva la gestión.");

        string Nombre(long id) => calc.Roles.First(x => x.IdTipoRol == id).Nombre;
        var idEmpleado = calc.Roles.FirstOrDefault(x => x.Codigo == EmpleadoServicio.CodigoRol)?.IdTipoRol;
        EmpleadoDatosDto? datosEmpleado = null;
        if (idEmpleado is not null && agregar.Contains(idEmpleado.Value))
        {
            datosEmpleado = EmpleadoServicio.Leer(filas);
            var errEmp = await EmpleadoServicio.ValidarAsync(c, tx, actor.IdCompania, datosEmpleado, idInterlocutor);
            if (errEmp.Count > 0)
                return ResultadoOperacion.Fallo(409, "rol_no_aplicable", "Los datos del empleado ya no son válidos: " + string.Join(" ", errEmp) + " Rechace o devuelva la gestión.");
        }
        foreach (var id in agregar)
        {
            var existe = await c.ExecuteScalarAsync<long?>("SELECT id_interlocutor_rol FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @idInterlocutor AND id_tipo_rol = @id", new { idInterlocutor, id }, tx);
            if (existe is null)
                await c.ExecuteAsync("INSERT INTO cli.INTERLOCUTOR_ROL (id_interlocutor, id_tipo_rol, creado_por) VALUES (@idInterlocutor, @id, @por)", new { idInterlocutor, id, por = porQuien }, tx);
            else
                await ReactivarAsync(c, tx, existe.Value, idGestion, porQuien);
            if (id == idEmpleado && datosEmpleado is not null) await EmpleadoServicio.AplicarAsync(c, tx, actor, idInterlocutor, id, datosEmpleado, idGestion, porQuien);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, "roles", $"{Nombre(id)}: sin asignar", $"{Nombre(id)}: Activo", idGestion);
        }
        foreach (var id in quitar)
        {
            await c.ExecuteAsync(
                "UPDATE cli.INTERLOCUTOR_ROL SET estado = 'Inactivo', fecha_baja = SYSUTCDATETIME(), modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @idInterlocutor AND id_tipo_rol = @id",
                new { idInterlocutor, id, por = porQuien }, tx);
            if (id == idEmpleado) await EmpleadoServicio.InactivarAsync(c, tx, idInterlocutor, id, porQuien);
            await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, "roles", $"{Nombre(id)}: Activo", $"{Nombre(id)}: Inactivo", idGestion);
        }
        return null;
    }
}
