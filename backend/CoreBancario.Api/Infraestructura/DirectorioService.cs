using Dapper;
using Microsoft.Data.SqlClient;

namespace CoreBancario.Api.Infraestructura;

/// <summary>
/// Registro leido de dir.CLIENTE_SAAS (BD Directorio / CoreBancarioDir).
/// </summary>
public record ClienteSaas(string CodigoCliente, string NombreBaseDatos, string RealmIam, string Estado);

/// <summary>
/// Encapsula el acceso a la BD Directorio (control-plane), el unico lugar donde
/// el backend sabe que existe mas de un cliente. Ver DAD Seccion 6.4/19.8 y
/// Modelo_Fisico_BD_Directorio.md.
/// </summary>
public class DirectorioService
{
    private readonly string _connectionStringDirectorio;

    public DirectorioService(IConfiguration configuration)
    {
        _connectionStringDirectorio = configuration.GetConnectionString("Directorio")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Directorio en la configuracion.");
    }

    /// <summary>
    /// Busca el cliente SaaS por codigo_cliente (viene del claim 'compania' del JWT).
    /// Devuelve null si no existe o no esta Activo.
    /// </summary>
    public async Task<ClienteSaas?> BuscarClienteActivoAsync(string codigoCliente)
    {
        using var conexion = new SqlConnection(_connectionStringDirectorio);

        const string sql = """
            SELECT codigo_cliente AS CodigoCliente,
                   nombre_base_datos AS NombreBaseDatos,
                   realm_iam AS RealmIam,
                   estado AS Estado
            FROM dir.CLIENTE_SAAS
            WHERE codigo_cliente = @codigoCliente
            """;

        var cliente = await conexion.QuerySingleOrDefaultAsync<ClienteSaas>(sql, new { codigoCliente });

        if (cliente is null || !string.Equals(cliente.Estado, "Activo", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return cliente;
    }

    /// <summary>
    /// Arma la cadena de conexion hacia la base de datos del tenant, reutilizando
    /// el mismo servidor/credenciales de la cadena de la BD Directorio y
    /// sustituyendo unicamente el nombre de la base de datos.
    /// </summary>
    public string ArmarConnectionStringTenant(string nombreBaseDatos)
    {
        var builder = new SqlConnectionStringBuilder(_connectionStringDirectorio)
        {
            InitialCatalog = nombreBaseDatos
        };
        return builder.ConnectionString;
    }
}
