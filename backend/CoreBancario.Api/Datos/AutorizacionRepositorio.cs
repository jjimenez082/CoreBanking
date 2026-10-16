using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Resuelve los permisos efectivos (RBAC, SEG-002) de un usuario nombrado:
/// sec.USUARIO_ROL -> sec.ROL_AUTORIZACION_OBJETO -> sec.OBJETO_AUTORIZACION.codigo.
///
/// No aplica a sec.USUARIO_INTEGRACION (cuentas de servicio): el modelo de
/// RBAC de este MVP solo cubre usuarios humanos (USUARIO_ROL referencia
/// sec.USUARIO). Una cuenta de servicio recibe acceso total por diseno —
/// ver ActorContext.TienePermiso.
/// </summary>
public class AutorizacionRepositorio
{
    private readonly TenantContext _tenantContext;

    public AutorizacionRepositorio(TenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public async Task<HashSet<string>> ObtenerCodigosPermisoAsync(long idUsuario)
    {
        using var conexion = new SqlConnection(_tenantContext.ConnectionString);

        const string sql = """
            SELECT DISTINCT oa.codigo
            FROM sec.USUARIO_ROL ur
            JOIN sec.ROL_AUTORIZACION ra ON ra.id_rol_autorizacion = ur.id_rol_autorizacion
            JOIN sec.ROL_AUTORIZACION_OBJETO rao ON rao.id_rol_autorizacion = ra.id_rol_autorizacion
            JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
            WHERE ur.id_usuario = @idUsuario
              AND ra.estado = 'Activo'
              AND oa.estado = 'Activo'
            """;

        var codigos = await conexion.QueryAsync<string>(sql, new { idUsuario });
        return codigos.ToHashSet();
    }
}
