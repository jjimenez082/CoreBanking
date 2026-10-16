using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>Creacion generica de una gestion de aprobacion en modo Campos (una sola pendiente por entidad y llave). La transaccion la maneja quien llama.</summary>
public static class GestionSolicitudes
{
    public static async Task<ResultadoOperacion> CrearAsync(
        SqlConnection c, SqlTransaction tx, TenantContext tenant, NumeracionServicio numeracion, ActorContext actor,
        string tipoGestion, string entidad, string llave, string? comentario, IEnumerable<(string Campo, string? Anterior, string? Nuevo)> detalle)
    {
        if (actor.EsIntegracion) return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");
        var pendiente = await c.ExecuteScalarAsync<long?>(
            "SELECT TOP 1 id_gestion FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
            new { e = entidad, llave, p = GestionRepositorio.EstadoPendiente }, tx);
        if (pendiente is not null) return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"Ya hay una gestion (#{pendiente}) pendiente de aprobacion para este registro.");

        var idTipo = await c.ExecuteScalarAsync<long?>("SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = tipoGestion }, tx);
        if (idTipo is null) return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {tipoGestion}; correr creditos_cimientos_ronda42.sql.");

        string numero;
        try { numero = await numeracion.SiguienteAsync(tenant.ConnectionString, actor.IdCompania, "GESTION"); }
        catch (NumeracionException ex) { return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

        var idGestion = await c.QuerySingleAsync<long>(
            """
            INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
            OUTPUT INSERTED.id_gestion VALUES (@c, @numero, @tipo, @estado, @u, @entidad, @llave, @comentario, @por)
            """,
            new { c = actor.IdCompania, numero, tipo = idTipo, estado = GestionRepositorio.EstadoPendiente, u = actor.IdUsuario, entidad, llave,
                  comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(), por = actor.NombreDescriptivo }, tx);
        foreach (var d in detalle)
            await c.ExecuteAsync("INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@g, @campo, @a, @n)",
                new { g = idGestion, campo = d.Campo, a = d.Anterior, n = d.Nuevo }, tx);

        var inicio = await MotorAprobacion.IniciarAsync(c, tx, actor, idGestion, idTipo.Value);
        if (inicio.AplicarYa)
        {
            var error = await GestionRepositorio.AplicarAutomaticaAsync(c, tx, actor, idGestion, inicio.NombreRegla);
            if (error is not null) return error;
        }
        return ResultadoOperacion.Ok(idGestion) with { Numero = numero };
    }
}
