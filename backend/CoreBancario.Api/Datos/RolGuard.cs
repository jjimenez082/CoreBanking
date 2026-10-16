using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Guard central de productos: no se abre una cuenta sin rol CLIENTE_CUENTAHABIENTE vigente ni un crédito sin CLIENTE_DEUDOR; en los productos,
/// beneficiarios, fiadores, codeudores, firmantes y autorizados solo se ligan personas con ese rol vigente. Lo usarán Cuentas y Créditos
/// (hoy no existen); el error es tipificado (rol_requerido) y lleva el código del rol para que la UI ofrezca «Solicitar rol».
/// </summary>
public static class RolGuard
{
    public const string ErrorRolRequerido = "rol_requerido";

    /// <summary>null = el interlocutor tiene el rol vigente; si no, el error (409 rol_requerido).</summary>
    public static async Task<ResultadoOperacion?> RequerirRolVigenteAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor, string codigoRol)
    {
        var fila = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.numero, i.estado, i.tipo_persona,
                   (SELECT TOP 1 it.nombre FROM gen.LISTA_VALORES_ITEM it JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = it.id_lista_valores_tipo
                     WHERE t.codigo = N'TIPO_ROL_INTERLOCUTOR' AND it.codigo = @codigoRol) AS rol_nombre,
                   CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_ROL r JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
                                      WHERE r.id_interlocutor = i.id_interlocutor AND r.estado = 'Activo' AND it.codigo = @codigoRol) THEN 1 ELSE 0 END AS tiene
            FROM cli.INTERLOCUTOR i WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania
            """, new { idInterlocutor, idCompania, codigoRol }, tx);
        if (fila is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "El interlocutor no existe.");
        var rol = (string?)fila.rol_nombre ?? codigoRol;
        if ((string)fila.estado != "Activo")
            return ResultadoOperacion.Fallo(409, ErrorRolRequerido, $"El interlocutor N.º {(string)fila.numero} está '{(string)fila.estado}' y no puede usarse como {rol}.");
        if ((int)fila.tiene != 1)
            return ResultadoOperacion.Fallo(409, ErrorRolRequerido, $"El interlocutor N.º {(string)fila.numero} no tiene el rol {rol} vigente. Solicite el rol (Gestionar roles) antes de continuar.");
        return null;
    }
}
