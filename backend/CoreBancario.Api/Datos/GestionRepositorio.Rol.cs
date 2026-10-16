using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestion CLI_ROL: agregar o quitar roles (Firmante, Beneficiario, Fiador, Codeudor, Autorizado...) a un cliente existente.
/// La llave de la gestion es el id del cliente, asi que el indice unico de «una gestion pendiente por registro» impide dos solicitudes de rol a la vez.
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoCambioRol = "CLI_ROL";
    public const string EntidadRol = RolServicio.Entidad;

    public async Task<ResultadoOperacion> SolicitarCambioRolAsync(ActorContext actor, long idInterlocutor, RolSolicitudRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar un cambio de roles.");
        var motivo = (req.Motivo ?? "").Trim();
        if (motivo.Length < 10) return ResultadoOperacion.Fallo(400, "motivo_requerido", "Indique el motivo del cambio de roles (mínimo 10 caracteres).");
        if (motivo.Length > 500) return ResultadoOperacion.Fallo(400, "motivo_largo", "El motivo no puede superar 500 caracteres.");
        var agregar = (req.Agregar ?? []).Distinct().ToList();
        var quitar = (req.Quitar ?? []).Distinct().ToList();

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var calc = await RolServicio.ValidarCambioAsync(conexion, tx, actor.IdCompania, idInterlocutor, agregar, quitar, null);
            if (calc.Errores.Count > 0)
                return ResultadoOperacion.Fallo(409, "cambio_rol_no_valido", string.Join("\n", calc.Errores));

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoCambioRol }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoCambioRol}; correr roles_ronda20.sql.");

            var catalogoRoles = calc.Roles;
            var idEmpleado = catalogoRoles.FirstOrDefault(r => r.Codigo == EmpleadoServicio.CodigoRol)?.IdTipoRol;
            var agregaEmpleado = idEmpleado is not null && agregar.Contains(idEmpleado.Value);
            if (agregaEmpleado)
            {
                var errEmp = await EmpleadoServicio.ValidarAsync(conexion, tx, actor.IdCompania, req.Empleado, idInterlocutor);
                if (errEmp.Count > 0) return ResultadoOperacion.Fallo(400, "empleado_no_valido", string.Join("\n", errEmp));
            }

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, NULL, @creadoPor)
                """,
                new
                {
                    actor.IdCompania, numeroGestion, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario,
                    entidad = EntidadRol, llave = idInterlocutor.ToString(CultureInfo.InvariantCulture), creadoPor = actor.NombreDescriptivo
                }, tx);

            var cambios = new List<Cambio> { new(RolServicio.CampoMotivo, null, motivo) };
            cambios.AddRange(agregar.Select(id => new Cambio($"{RolServicio.PrefijoAgregar}{id}]", null, id.ToString(CultureInfo.InvariantCulture))));
            cambios.AddRange(quitar.Select(id => new Cambio($"{RolServicio.PrefijoQuitar}{id}]", null, id.ToString(CultureInfo.InvariantCulture))));
            if (agregaEmpleado && req.Empleado is not null)
                cambios.AddRange(EmpleadoServicio.Campos(req.Empleado).Select(x => new Cambio(x.Campo, null, x.Valor)));
            foreach (var c in cambios)
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, NULL, @nuevo)",
                    new { idGestion, campo = c.Campo, nuevo = c.Nuevo }, tx);

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value);
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
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "El cliente ya tiene una gestión de roles pendiente; debe resolverse antes.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
