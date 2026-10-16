using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Consultas de roles de un cliente y mantenimiento de los datos minimos que exige cada rol (cli.TIPO_ROL_REQUISITO).
/// El cambio de roles de un cliente NO pasa por aqui: es la gestion CLI_ROL (GestionRepositorio.Rol.cs).
/// </summary>
public class RolRequisitoRepositorio
{
    public const string ObjetoAdmin = "ROL_ADMINISTRACION";

    private readonly TenantContext _tenant;
    public RolRequisitoRepositorio(TenantContext tenant) => _tenant = tenant;
    private SqlConnection Nueva() => new(_tenant.ConnectionString);

    /// <summary>Roles del catalogo con los campos que exige cada uno (para el alta, la modificacion y la pantalla de administracion).</summary>
    public async Task<IReadOnlyList<RolRequisitosResponse>> ListarAsync(long idCompania, bool soloActivos, string tipoPersona = "Fisica")
    {
        using var c = Nueva();
        var catalogo = await RolServicio.CatalogoRolesAsync(c, null);
        var res = new List<RolRequisitosResponse>();
        foreach (var r in catalogo.Where(x => (!soloActivos || x.Estado == "Activo") && RolServicio.AplicaATipo(x.Codigo, tipoPersona)))
        {
            var reglas = await RolServicio.RequisitosAsync(c, null, idCompania, r.IdTipoRol, tipoPersona);
            res.Add(new RolRequisitosResponse(r.IdTipoRol, r.Codigo, r.Nombre, ReglasCampos.CatalogoDe(tipoPersona).Where(x => reglas.Pide(x.Codigo)).Select(x => x.Codigo).ToList()));
        }
        return res;
    }

    public static IReadOnlyList<RolRequisitoCampoResponse> Campos(string tipoPersona = "Fisica")
        => ReglasCampos.CatalogoDe(tipoPersona).Select(x => new RolRequisitoCampoResponse(x.Codigo, x.Seccion, x.Etiqueta, x.Aplica)).ToList();

    /// <summary>
    /// Guarda los campos que exige cada rol enviado. Se escriben TODOS los campos del catalogo del rol (marcado = obligatorio) para que
    /// «sin fila» solo signifique «rol sin configurar»; se audita unicamente lo que cambia. Afecta a altas y modificaciones NUEVAS.
    /// </summary>
    public async Task<ResultadoOperacion> GuardarAsync(ActorContext actor, RolRequisitosGuardarRequest req)
    {
        if (req.Roles is null || req.Roles.Count == 0)
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "No hay roles que guardar.");
        var tipo = req.TipoPersona ?? "Fisica";
        if (!ReglasCampos.TipoValido(tipo))
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "Tipo de interlocutor invalido (Fisica o Juridica).");
        var catalogoCampos = ReglasCampos.CatalogoDe(tipo);
        var codigosTipo = catalogoCampos.Select(x => x.Codigo).ToHashSet();
        if (req.Roles.Any(r => (r.Requeridos ?? []).Any(x => !codigosTipo.Contains(x))))
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "Alguno de los campos indicados no es parametrizable.");
        using var c = Nueva();
        c.Open();
        using var tx = c.BeginTransaction();
        var catalogo = await RolServicio.CatalogoRolesAsync(c, tx);
        foreach (var e in req.Roles)
        {
            var rol = catalogo.FirstOrDefault(x => x.IdTipoRol == e.IdTipoRol);
            if (rol is null) { tx.Rollback(); return ResultadoOperacion.Fallo(404, "no_encontrado", "Uno de los roles no existe."); }
            var actual = await RolServicio.RequisitosAsync(c, tx, actor.IdCompania, e.IdTipoRol, tipo);
            var nuevos = (e.Requeridos ?? []).ToHashSet();
            foreach (var campo in catalogoCampos)
            {
                var antes = actual.Pide(campo.Codigo);
                var ahora = nuevos.Contains(campo.Codigo);
                var filas = await c.ExecuteAsync(
                    """
                    UPDATE cli.TIPO_ROL_REQUISITO SET es_requerido = @v, modificado_por = @por, modificado_en = SYSUTCDATETIME()
                    WHERE id_compania = @idCompania AND id_tipo_rol = @idTipoRol AND tipo_persona = @tipo AND requisito = @req AND es_requerido <> @v
                    """, new { v = ahora, por = actor.NombreDescriptivo, actor.IdCompania, idTipoRol = e.IdTipoRol, req = campo.Codigo, tipo }, tx);
                if (filas == 0)
                    await c.ExecuteAsync(
                        """
                        IF NOT EXISTS (SELECT 1 FROM cli.TIPO_ROL_REQUISITO WHERE id_compania = @idCompania AND id_tipo_rol = @idTipoRol AND tipo_persona = @tipo AND requisito = @req)
                            INSERT INTO cli.TIPO_ROL_REQUISITO (id_compania, id_tipo_rol, tipo_persona, requisito, es_requerido, creado_por)
                            VALUES (@idCompania, @idTipoRol, @tipo, @req, @v, @por)
                        """, new { actor.IdCompania, idTipoRol = e.IdTipoRol, req = campo.Codigo, v = ahora, por = actor.NombreDescriptivo, tipo }, tx);
                if (antes != ahora)
                    await Auditoria.CambioAsync(c, tx, actor, "ROL_REQUISITO", e.IdTipoRol, "requisito",
                        $"{rol.Nombre} ({(tipo == "Juridica" ? "Organización" : "Persona")}) · {campo.Etiqueta}: {(antes ? "Obligatorio" : "Opcional")}", $"{rol.Nombre} ({(tipo == "Juridica" ? "Organización" : "Persona")}) · {campo.Etiqueta}: {(ahora ? "Obligatorio" : "Opcional")}");
            }
        }
        tx.Commit();
        return ResultadoOperacion.Ok(0);
    }

    /// <summary>
    /// Busca la identificacion y dice que hacer para que la persona asuma el rol: pedir todos los datos (no existe), agregarle el rol
    /// por gestion sin tocar sus datos (ya es cliente), o por que no se puede. NO crea nada: el alta o el cambio de rol son gestiones aparte.
    /// </summary>
    public async Task<RolIdentificacionResponse> ValidarIdentificacionAsync(ActorContext actor, long idTipoIdentificacion, string numero, long idTipoRol)
    {
        RolIdentificacionResponse R(string estado, string mensaje, long? id = null, long? sobrev = null, IReadOnlyList<string>? faltan = null, bool puede = false)
            => new(estado, mensaje, id, sobrev, faltan ?? [], null, puede);

        numero = (numero ?? "").Trim();
        if (idTipoIdentificacion <= 0 || numero.Length == 0)
            return R("NO_DISPONIBLE", "Indique el tipo y el número de identificación.");

        using var c = Nueva();
        var catalogo = await RolServicio.CatalogoRolesAsync(c, null);
        var rol = catalogo.FirstOrDefault(x => x.IdTipoRol == idTipoRol);
        if (rol is null || rol.Estado != "Activo") return R("NO_DISPONIBLE", "El rol indicado no existe o está inactivo.");
        if (RolServicio.EsRolDeProducto(rol.Codigo)) return R("NO_DISPONIBLE", $"El rol {rol.Nombre} se solicita desde «Gestionar roles» del cliente, no como alta de tercero.");

        var hallados = (await c.QueryAsync(
            """
            SELECT i.id_interlocutor, i.numero, i.estado, i.tipo_persona, i.id_interlocutor_sobreviviente AS sobrev
            FROM cli.INTERLOCUTOR_IDENTIFICACION x
            JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = x.id_interlocutor AND i.id_compania = @idCompania
            WHERE x.id_compania = @idCompania AND x.estado = 'Activo' AND x.id_tipo_identificacion = @idTipo AND x.numero_identificacion = @numero
            ORDER BY CASE WHEN i.estado = 'Fusionado' THEN 1 ELSE 0 END
            """, new { actor.IdCompania, idTipo = idTipoIdentificacion, numero })).ToList();

        if (hallados.Count == 0)
        {
            var pendiente = await c.ExecuteScalarAsync<string?>(
                """
                SELECT TOP 1 g.numero
                FROM apr.GESTION g
                JOIN apr.GESTION_DETALLE dn ON dn.id_gestion = g.id_gestion AND dn.nombre_campo LIKE N'ident[[]+%].numero_identificacion'
                JOIN apr.GESTION_DETALLE dt ON dt.id_gestion = g.id_gestion
                                           AND dt.nombre_campo = REPLACE(dn.nombre_campo, N'numero_identificacion', N'id_tipo_identificacion')
                WHERE g.id_compania = @idCompania AND g.entidad_negocio = N'INTERLOCUTOR_ALTA' AND g.estado = N'Pendiente de Aprobacion'
                  AND dn.valor_nuevo = @numero AND dt.valor_nuevo = @tipo
                """, new { actor.IdCompania, numero, tipo = idTipoIdentificacion.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return pendiente is not null
                ? R("ALTA_PENDIENTE", $"Esa identificación ya está en el alta pendiente de aprobación de la gestión {pendiente}.")
                : R("NUEVO", "La persona no está registrada. Se le pedirán sus datos, incluidos los que exige el rol.", puede: true);
        }

        var h = hallados[0];
        long id = (long)h.id_interlocutor; string estado = (string)h.estado; string numCli = (string)h.numero;
        if (estado == "Fusionado")
            return R("FUSIONADO", $"Esa identificación pertenecía a un cliente fusionado. Use el cliente que se conservó.", id, (long?)h.sobrev);
        if (estado != "Activo")
            return R("NO_DISPONIBLE", $"El cliente N.º {numCli} está '{estado}' y no admite cambios de rol.", id);
        if ((string)h.tipo_persona != "Fisica")
            return R("NO_DISPONIBLE", "Por ahora los roles se administran solo para Persona Física.", id);

        var activos = (await c.QueryAsync<long>("SELECT id_tipo_rol FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @id AND estado = 'Activo'", new { id })).ToHashSet();
        if (activos.Contains(idTipoRol)) return R("YA_TIENE_ROL", $"El cliente N.º {numCli} ya tiene el rol {rol.Nombre}.", id);

        var pend = await FusionServicio.GestionPendienteAsync(c, null, actor.IdCompania, id);
        if (pend is not null) return R("GESTION_PENDIENTE", $"El cliente N.º {numCli} tiene la gestión {pend} pendiente de aprobación; debe resolverse antes.", id);

        var faltan = await RolServicio.FaltantesEnClienteAsync(c, null, id, await RolServicio.RequisitosAsync(c, null, actor.IdCompania, idTipoRol));
        if (faltan.Count > 0)
            return R("FALTAN_DATOS", RolServicio.MensajeFaltantes(rol.Nombre, faltan) + " Complete los datos del cliente (Modificar datos) y vuelva a intentarlo.", id, faltan: faltan);

        return R("PUEDE_AGREGAR", $"La persona ya es el cliente N.º {numCli}. Solo se le agregará el rol {rol.Nombre}; sus datos no cambian.", id,
            puede: actor.TienePermiso("CLI_ROL"));
    }

    /// <summary>Roles actuales del cliente, roles que podria asumir y los datos que le faltan para cada uno.</summary>
    public async Task<RolesClienteResponse?> RolesDeClienteAsync(long idCompania, long idInterlocutor)
    {
        using var c = Nueva();
        var cli = await c.QuerySingleOrDefaultAsync(
            "SELECT numero, estado, tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania",
            new { idInterlocutor, idCompania });
        if (cli is null) return null;
        var tipoPersona = (string)cli.tipo_persona;

        var catalogo = await RolServicio.CatalogoRolesAsync(c, null);
        var filas = (await c.QueryAsync(
            "SELECT id_tipo_rol, estado, fecha_asignacion FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor })).ToList();

        var empleado = await c.QuerySingleOrDefaultAsync<string?>(
            """
            SELECT TOP 1 CONCAT(N'Unidad: ', un.nombre, N' · N.º de empleado: ', em.numero_empleado)
            FROM cli.INTERLOCUTOR_ROL r JOIN cli.INTERLOCUTOR_ROL_EMPLEADO em ON em.id_interlocutor_rol = r.id_interlocutor_rol AND em.estado = 'Activo'
            JOIN sec.DEPARTAMENTO un ON un.id_departamento = em.id_unidad
            WHERE r.id_interlocutor = @idInterlocutor AND r.estado = 'Activo'
            """, new { idInterlocutor });
        var roles = filas
            .Select(f => (Rol: catalogo.FirstOrDefault(x => x.IdTipoRol == (long)f.id_tipo_rol), Estado: (string)f.estado, Fecha: (DateTime)f.fecha_asignacion))
            .Where(x => x.Rol is not null)
            .OrderBy(x => x.Estado == "Activo" ? 0 : 1).ThenBy(x => x.Rol!.Nombre)
            .Select(x => new RolClienteResponse(x.Rol!.IdTipoRol, x.Rol.Codigo, x.Rol.Nombre, x.Estado, x.Fecha, x.Rol.Codigo == EmpleadoServicio.CodigoRol && x.Estado == "Activo" ? empleado : null))
            .ToList();

        var activos = roles.Where(r => r.Estado == "Activo").Select(r => r.IdTipoRol).ToHashSet();
        var disponibles = new List<RolDisponibleResponse>();
        foreach (var r in catalogo.Where(x => x.Estado == "Activo" && !activos.Contains(x.IdTipoRol) && tipoPersona != "Grupo" && RolServicio.AplicaATipo(x.Codigo, tipoPersona)))
        {
            // Roles de producto (Deudor, Cuentahabiente): solo se ofrecen si el cliente ya tiene el rol prerrequisito (Cliente Básico).
            if (RolServicio.Prerrequisitos.TryGetValue(r.Codigo, out var codPre)
                && !catalogo.Any(x => x.Codigo == codPre && activos.Contains(x.IdTipoRol))) continue;
            var faltan = await RolServicio.FaltantesEnClienteAsync(c, null, idInterlocutor, await RolServicio.RequisitosAsync(c, null, idCompania, r.IdTipoRol, tipoPersona));
            disponibles.Add(new RolDisponibleResponse(r.IdTipoRol, r.Codigo, r.Nombre, faltan));
        }

        var pend = await FusionServicio.GestionPendienteAsync(c, null, idCompania, idInterlocutor);
        var historial = await RolServicio.HistorialAsync(c, null, idInterlocutor);
        return new RolesClienteResponse(idInterlocutor, (string)cli.numero, (string)cli.estado, roles, disponibles, pend, historial, tipoPersona);
    }
}
