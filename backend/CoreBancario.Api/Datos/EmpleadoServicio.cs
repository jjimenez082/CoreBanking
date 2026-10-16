using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Datos propios del rol EMPLEADO (solo personas): la unidad de la estructura organizativa (sucursal, agencia o departamento de sec.DEPARTAMENTO) y el
/// numero de empleado, unico entre los empleados activos de la compania. Viajan en la gestion como emp_unidad y emp_numero y se aplican al aprobarse.
/// </summary>
public static class EmpleadoServicio
{
    public const string CodigoRol = "EMPLEADO";
    public const string CampoUnidad = "emp_unidad";
    public const string CampoNumero = "emp_numero";

    private static readonly Regex PatronNumero = new(@"^[A-Za-z0-9][A-Za-z0-9\-_./]{0,29}$", RegexOptions.Compiled);

    public static async Task<bool> EsRolEmpleadoAsync(SqlConnection c, SqlTransaction? tx, long? idTipoRol)
        => idTipoRol is not null && await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_item = @id AND codigo = N'EMPLEADO') THEN 1 ELSE 0 END", new { id = idTipoRol }, tx);

    public static bool EsCampo(string campo) => campo is CampoUnidad or CampoNumero;

    public static async Task<List<string>> ValidarAsync(SqlConnection c, SqlTransaction? tx, long idCompania, EmpleadoDatosDto? d, long? idInterlocutorExcluir)
    {
        var errores = new List<string>();
        if (d is null) { errores.Add("Para el rol Empleado indique la unidad (sucursal o departamento) y el número de empleado."); return errores; }
        var numero = (d.NumeroEmpleado ?? "").Trim();
        if (numero.Length == 0) errores.Add("El número de empleado es obligatorio.");
        else if (!PatronNumero.IsMatch(numero)) errores.Add("El número de empleado admite letras, números y los signos - _ . / (máximo 30 caracteres).");
        var unidadOk = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM sec.DEPARTAMENTO WHERE id_departamento = @u AND id_compania = @idCompania AND estado = 'Activo') THEN 1 ELSE 0 END",
            new { u = d.IdUnidad, idCompania }, tx);
        if (!unidadOk) errores.Add("La unidad del empleado no existe o está inactiva.");
        if (numero.Length > 0)
        {
            var repetido = await c.ExecuteScalarAsync<string?>(
                """
                SELECT TOP 1 i.numero FROM cli.INTERLOCUTOR_ROL_EMPLEADO e
                JOIN cli.INTERLOCUTOR_ROL r ON r.id_interlocutor_rol = e.id_interlocutor_rol
                JOIN cli.INTERLOCUTOR i ON i.id_interlocutor = r.id_interlocutor
                WHERE e.id_compania = @idCompania AND e.numero_empleado = @numero AND e.estado = 'Activo'
                  AND (@excluir IS NULL OR r.id_interlocutor <> @excluir)
                """, new { idCompania, numero, excluir = idInterlocutorExcluir }, tx);
            if (repetido is not null) errores.Add($"El número de empleado {numero} ya lo tiene el cliente N.º {repetido}.");
        }
        return errores;
    }

    /// <summary>Datos de la gestion como cambios (emp_unidad, emp_numero).</summary>
    public static IEnumerable<(string Campo, string Valor)> Campos(EmpleadoDatosDto d)
    {
        yield return (CampoUnidad, d.IdUnidad.ToString(CultureInfo.InvariantCulture));
        yield return (CampoNumero, d.NumeroEmpleado.Trim());
    }

    public static EmpleadoDatosDto? Leer(IEnumerable<(string Campo, string? Valor)> filas)
    {
        var lista = filas.ToList();
        var unidad = lista.FirstOrDefault(x => x.Campo == CampoUnidad).Valor;
        var numero = lista.FirstOrDefault(x => x.Campo == CampoNumero).Valor;
        return long.TryParse(unidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) && numero is not null ? new EmpleadoDatosDto(u, numero) : null;
    }

    /// <summary>Guarda los datos del empleado en el rol (crea o reactiva la fila).</summary>
    public static async Task AplicarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idInterlocutor, long idTipoRol, EmpleadoDatosDto d, long? idGestion, string porQuien)
    {
        var idRol = await c.ExecuteScalarAsync<long>(
            "SELECT id_interlocutor_rol FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @idInterlocutor AND id_tipo_rol = @idTipoRol", new { idInterlocutor, idTipoRol }, tx);
        var numero = d.NumeroEmpleado.Trim();
        var existe = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_ROL_EMPLEADO WHERE id_interlocutor_rol = @idRol) THEN 1 ELSE 0 END", new { idRol }, tx);
        if (existe)
            await c.ExecuteAsync(
                "UPDATE cli.INTERLOCUTOR_ROL_EMPLEADO SET id_unidad = @u, numero_empleado = @numero, estado = 'Activo', modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor_rol = @idRol",
                new { idRol, u = d.IdUnidad, numero, por = porQuien }, tx);
        else
            await c.ExecuteAsync(
                "INSERT INTO cli.INTERLOCUTOR_ROL_EMPLEADO (id_interlocutor_rol, id_compania, id_unidad, numero_empleado, creado_por) VALUES (@idRol, @c, @u, @numero, @por)",
                new { idRol, c = actor.IdCompania, u = d.IdUnidad, numero, por = porQuien }, tx);
        var unidad = await c.ExecuteScalarAsync<string?>("SELECT nombre FROM sec.DEPARTAMENTO WHERE id_departamento = @u", new { u = d.IdUnidad }, tx);
        await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idInterlocutor, "empleado", null, $"Unidad: {unidad} · N.º de empleado: {numero}", idGestion);
    }

    public static Task InactivarAsync(SqlConnection c, SqlTransaction tx, long idInterlocutor, long idTipoRol, string porQuien)
        => c.ExecuteAsync(
            """
            UPDATE e SET estado = 'Inactivo', modificado_por = @por, modificado_en = SYSUTCDATETIME()
            FROM cli.INTERLOCUTOR_ROL_EMPLEADO e JOIN cli.INTERLOCUTOR_ROL r ON r.id_interlocutor_rol = e.id_interlocutor_rol
            WHERE r.id_interlocutor = @idInterlocutor AND r.id_tipo_rol = @idTipoRol
            """, new { idInterlocutor, idTipoRol, por = porQuien }, tx);
}
