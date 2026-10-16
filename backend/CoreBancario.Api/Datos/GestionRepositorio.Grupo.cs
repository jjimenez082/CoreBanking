using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Alta de un Grupo por gestion (CLI_ALTA, tipo_persona = Grupo). El detalle guarda los campos del grupo y, por cada miembro,
/// las mismas filas rel[+n].* de una relacion nueva (el miembro es quien «es miembro del grupo», sentido Destino). Al aprobar
/// se crea el grupo y se aplican las relaciones con RelacionServicio, que revalida (participacion, estado de cada miembro).
/// </summary>
public partial class GestionRepositorio
{
    private async Task<ResultadoOperacion> SolicitarAltaGrupoAsync(ActorContext actor, InterlocutorAltaRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "Por ahora el alta de grupos se hace desde el Backoffice.");
        if (req.IdProspecto is not null)
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "Los prospectos solo aplican a personas; un grupo se da de alta directamente.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var calc = await GrupoServicio.ValidarAsync(conexion, tx, actor.IdCompania, req.Grupo, null);
            var errores = calc.Errores;

            var paisOk = await conexion.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { p = req.IdPaisRegulador }, tx);
            if (!paisOk) errores.Add("El pais regulador no existe o esta inactivo.");
            var idUnidad = req.IdUnidad ?? await InterlocutorRepositorio.UnidadRegistroPorDefectoAsync(conexion, tx, actor.IdUsuario.Value);
            var errorUnidad = await InterlocutorRepositorio.ValidarUnidadAsync(conexion, tx, actor.IdCompania, idUnidad, null);
            if (errorUnidad is not null) errores.Add(errorUnidad);
            var idCanal = await InterlocutorRepositorio.CanalPorCodigoAsync(conexion, tx, "SUCURSAL");
            if (idCanal is null) errores.Add("Falta el canal SUCURSAL en la lista CANAL; correr administracion_transversal.sql.");
            if (errores.Count > 0)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

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

            var g = req.Grupo!;
            var cambios = new List<Cambio>
            {
                new("id_pais_regulador", null, req.IdPaisRegulador.ToString(CultureInfo.InvariantCulture)),
                new("id_unidad", null, idUnidad?.ToString(CultureInfo.InvariantCulture)),
                new("id_canal_origen", null, idCanal?.ToString(CultureInfo.InvariantCulture)),
                new("tipo_persona", null, GrupoServicio.Tipo),
                new("razon_social", null, V(g.Nombre)),   // mismo campo que la organizacion: asi los titulos de la gestion muestran el nombre
                new(GrupoServicio.CampoTipoGrupo, null, g.IdTipoGrupo.ToString(CultureInfo.InvariantCulture)),
                new(GrupoServicio.CampoDescripcion, null, V(g.Descripcion)),
                new(GrupoServicio.CampoFormacion, null, Fecha(g.FechaFormacion)),
            };
            var n = 0;
            foreach (var m in g.Miembros)
            {
                n++;
                var info = calc.Miembros.First(x => x.Id == m.IdMiembro);
                cambios.Add(new($"rel[+{n}].id_otro", null, $"{m.IdMiembro}|{info.Numero} · {info.Nombre}"));
                cambios.Add(new($"rel[+{n}].id_tipo_relacion", null, calc.IdTipoRelacion.ToString(CultureInfo.InvariantCulture)));
                cambios.Add(new($"rel[+{n}].sentido", null, "Destino"));
                if (V(m.Funcion) is { } f) cambios.Add(new($"rel[+{n}].funcion", null, f));
            }
            foreach (var c in cambios.Where(c => c.Nuevo is not null))
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, NULL, @nuevo)",
                    new { idGestion, campo = c.Campo, nuevo = c.Nuevo }, tx);

            if (req.IdBorrador is not null)
                await conexion.ExecuteAsync(
                    "DELETE FROM cli.INTERLOCUTOR_BORRADOR WHERE id_borrador = @id AND id_usuario = @u AND id_compania = @c",
                    new { id = req.IdBorrador, u = actor.IdUsuario, c = actor.IdCompania }, tx);

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value, false);
            var errorDevuelta = await CerrarDevueltaAsync(conexion, tx, actor, req.IdGestionDevuelta, idGestion, EntidadAlta, PrefijoLlaveAlta + numeroGestion);
            if (errorDevuelta is not null) return errorDevuelta;
            if (inicio.AplicarYa)
            {
                var errorAuto = await AplicarAutomaticaAsync(conexion, tx, actor, idGestion, inicio.NombreRegla, SiguienteNumeroDe(actor));
                if (errorAuto is not null) { tx.Rollback(); return errorAuto; }
            }
            tx.Commit();
            return ResultadoOperacion.Ok(idGestion) with { Numero = numeroGestion };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task<ResultadoOperacion?> AplicarAltaGrupoAsync(
        SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string solicitadoPor,
        Func<string, Task<string>> siguienteNumero, Dictionary<string, string?> nucleo, List<(string Campo, string? Valor)> filas)
    {
        var (agregar, _, _, _) = RelacionServicio.LeerDetalle(filas);
        var grupo = new GrupoDto(
            Dato(nucleo, "razon_social") ?? "", Num(Dato(nucleo, GrupoServicio.CampoTipoGrupo)) ?? 0,
            Dato(nucleo, GrupoServicio.CampoDescripcion), Fch(Dato(nucleo, GrupoServicio.CampoFormacion)),
            agregar.Select(a => new MiembroGrupoDto(a.IdOtro, a.Funcion)).ToList());
        var idPais = Num(Dato(nucleo, "id_pais_regulador")) ?? 0;
        var idUnidad = Num(Dato(nucleo, "id_unidad"));
        var idCanal = Num(Dato(nucleo, "id_canal_origen"));

        // Se vuelve a validar: entre la solicitud y la aprobacion pudo cambiar un miembro o crearse otro grupo igual.
        var calc = await GrupoServicio.ValidarAsync(c, tx, actor.IdCompania, grupo, idGestion);
        var errores = calc.Errores;
        var paisOk = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END", new { p = idPais }, tx);
        if (!paisOk) errores.Add("El pais regulador no existe o esta inactivo.");
        var errorUnidad = await InterlocutorRepositorio.ValidarUnidadAsync(c, tx, actor.IdCompania, idUnidad, null);
        if (errorUnidad is not null) errores.Add(errorUnidad);
        var errorCanal = await InterlocutorRepositorio.ValidarCanalAsync(c, tx, idCanal, null);
        if (errorCanal is not null) errores.Add(errorCanal);
        if (errores.Count > 0)
            return ResultadoOperacion.Fallo(409, "alta_no_aplicable", "El alta del grupo ya no es valida: " + string.Join(" ", errores) + " Rechace o devuelva la gestion.");

        string numero;
        try { numero = await siguienteNumero("CLIENTE"); }
        catch (NumeracionException ex) { return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

        var idGrupo = await GrupoServicio.InsertarAsync(c, tx, actor.IdCompania, numero, idPais, idUnidad, idCanal, grupo, solicitadoPor);
        await c.ExecuteAsync("UPDATE apr.GESTION SET llave_registro = @llave WHERE id_gestion = @idGestion",
            new { idGestion, llave = idGrupo.ToString(CultureInfo.InvariantCulture) }, tx);
        await Auditoria.CambioAsync(c, tx, actor, EntidadInterlocutor, idGrupo, "alta", null,
            $"Grupo N.º {numero} creado por la gestion de alta", idGestion);

        // Los miembros entran como relaciones MIEMBRO_GRUPO (con su propia auditoria en cada extremo).
        return await RelacionServicio.AplicarAsync(c, tx, actor, idGestion, idGrupo, solicitadoPor);
    }

    // ----- Modificar o disolver un grupo (gestion CLI_MODIFICACION_DATOS sobre INTERLOCUTOR) ------------------------------------

    public Task<ResultadoOperacion> SolicitarModificacionGrupoAsync(ActorContext actor, long idGrupo, GrupoModificacionRequest req)
        => SolicitarCambioGrupoAsync(actor, idGrupo, req.Comentario, req.IdGestionDevuelta, async (c, tx, actual) =>
        {
            var errores = await GrupoServicio.ValidarDatosAsync(c, tx, actor.IdCompania, req.Nombre, req.IdTipoGrupo, req.Descripcion, req.FechaFormacion, null, idGrupo);
            var cambios = new List<Cambio>();
            void Dif(string campo, string? anterior, string? nuevo) { if (anterior != nuevo) cambios.Add(new(campo, anterior, nuevo)); }
            Dif("razon_social", V((string?)actual.nombre), V(req.Nombre));
            Dif(GrupoServicio.CampoTipoGrupo, ((long?)actual.id_tipo_grupo)?.ToString(CultureInfo.InvariantCulture), req.IdTipoGrupo.ToString(CultureInfo.InvariantCulture));
            Dif(GrupoServicio.CampoDescripcion, V((string?)actual.descripcion), V(req.Descripcion));
            Dif(GrupoServicio.CampoFormacion, (string?)actual.fecha_formacion, Fecha(req.FechaFormacion));
            return (errores, cambios);
        });

    public Task<ResultadoOperacion> SolicitarDisolucionGrupoAsync(ActorContext actor, long idGrupo, GrupoDisolucionRequest req)
        => SolicitarCambioGrupoAsync(actor, idGrupo, req.Motivo, null, (c, tx, actual) =>
        {
            var errores = new List<string>();
            if (string.IsNullOrWhiteSpace(req.Motivo) || req.Motivo.Trim().Length < 10) errores.Add("Indique el motivo de la disolución (mínimo 10 caracteres).");
            return Task.FromResult((errores, new List<Cambio> { new("estado", "Activo", GrupoServicio.EstadoDisuelto) }));
        });

    private async Task<ResultadoOperacion> SolicitarCambioGrupoAsync(
        ActorContext actor, long idGrupo, string? comentario, long? idGestionDevuelta,
        Func<SqlConnection, SqlTransaction, dynamic, Task<(List<string> Errores, List<Cambio> Cambios)>> calcular)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var actual = await conexion.QuerySingleOrDefaultAsync(
                """
                SELECT i.estado, g.nombre_grupo1 AS nombre, g.id_tipo_grupo, g.descripcion, CONVERT(nvarchar(10), g.fecha_formacion, 23) AS fecha_formacion
                FROM cli.INTERLOCUTOR i JOIN cli.INTERLOCUTOR_GRUPO g ON g.id_interlocutor = i.id_interlocutor
                WHERE i.id_interlocutor = @idGrupo AND i.id_compania = @idCompania AND i.tipo_persona = 'Grupo'
                """, new { idGrupo, actor.IdCompania }, tx);
            if (actual is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el grupo indicado.");
            if ((string)actual.estado != "Activo")
                return ResultadoOperacion.Fallo(409, "cliente_no_activo", $"El grupo esta '{(string)actual.estado}' y no se puede modificar.");

            var llave = idGrupo.ToString(CultureInfo.InvariantCulture);
            var pendiente = await conexion.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id_gestion FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
                new { e = EntidadInterlocutor, llave, p = EstadoPendiente }, tx);
            if (pendiente is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"El grupo ya tiene la gestion #{pendiente} pendiente de aprobacion; debe resolverse antes de solicitar otro cambio.");
            var otraPend = await FusionServicio.GestionPendienteAsync(conexion, tx, actor.IdCompania, idGrupo);
            if (otraPend is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"El grupo esta en la gestion {otraPend} pendiente de aprobacion; debe resolverse antes de solicitar otro cambio.");

            (List<string> errores, List<Cambio> cambios) = await calcular(conexion, tx, (object)actual);
            if (errores.Count > 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));
            if (cambios.Count == 0) return ResultadoOperacion.Fallo(400, "sin_cambios", "No hay diferencias con los datos actuales.");

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoModificacionCliente }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoModificacionCliente}; correr edicion_clientes_gestion.sql.");

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, @comentario, @creadoPor)
                """,
                new
                {
                    numeroGestion, actor.IdCompania, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario,
                    entidad = EntidadInterlocutor, llave, comentario = V(comentario), creadoPor = actor.NombreDescriptivo
                }, tx);
            foreach (var c in cambios)
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, @anterior, @nuevo)",
                    new { idGestion, campo = c.Campo, anterior = c.Anterior, nuevo = c.Nuevo }, tx);

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value);
            var errorDevuelta = await CerrarDevueltaAsync(conexion, tx, actor, idGestionDevuelta, idGestion, EntidadInterlocutor, llave);
            if (errorDevuelta is not null) return errorDevuelta;
            if (inicio.AplicarYa)
            {
                var errorAuto = await AplicarAutomaticaAsync(conexion, tx, actor, idGestion, inicio.NombreRegla);
                if (errorAuto is not null) { tx.Rollback(); return errorAuto; }
            }
            tx.Commit();
            return ResultadoOperacion.Ok(idGestion) with { Numero = numeroGestion };
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "El grupo ya tiene una gestion pendiente de aprobacion.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
