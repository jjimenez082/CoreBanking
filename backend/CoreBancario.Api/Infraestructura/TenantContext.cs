namespace CoreBancario.Api.Infraestructura;

/// <summary>
/// Contexto del tenant (cliente SaaS) resuelto para la solicitud HTTP actual.
/// Se registra como Scoped: una instancia por request, poblada por
/// <see cref="TenantResolutionMiddleware"/> antes de llegar a cualquier endpoint.
/// </summary>
public class TenantContext
{
    /// <summary>Codigo corto del cliente (ARQ-020), ej. "DEV".</summary>
    public string CodigoCliente { get; private set; } = string.Empty;

    /// <summary>Nombre de la base de datos del tenant, ej. "CoreBancario_DEV".</summary>
    public string NombreBaseDatos { get; private set; } = string.Empty;

    /// <summary>Cadena de conexion ya armada contra la base de datos del tenant.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>True una vez que el middleware resolvio el tenant exitosamente.</summary>
    public bool Resuelto { get; private set; }

    public void Establecer(string codigoCliente, string nombreBaseDatos, string connectionString)
    {
        CodigoCliente = codigoCliente;
        NombreBaseDatos = nombreBaseDatos;
        ConnectionString = connectionString;
        Resuelto = true;
    }
}
