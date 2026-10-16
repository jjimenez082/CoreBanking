using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Lectura de catalogos para llenar combo boxes. Solo devuelve registros
/// Activos: lo Inactivo deja de ofrecerse en registros nuevos (ver
/// Listas de Valores), aunque los registros existentes lo conserven.
/// </summary>
public class CatalogosRepositorio
{
    private readonly TenantContext _tenantContext;

    public CatalogosRepositorio(TenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    private SqlConnection NuevaConexion() => new(_tenantContext.ConnectionString);

    /// <summary>Valores activos de una lista; null si la lista no existe o esta inactiva.</summary>
    public async Task<IReadOnlyList<CatalogoItemResponse>?> ObtenerListaAsync(string codigoLista)
    {
        using var conexion = NuevaConexion();

        var idTipo = await conexion.ExecuteScalarAsync<long?>(
            "SELECT id_lista_valores_tipo FROM gen.LISTA_VALORES_TIPO WHERE codigo = @codigoLista AND estado = 'Activo'",
            new { codigoLista });
        if (idTipo is null) return null;

        var filas = await conexion.QueryAsync<CatalogoItemResponse>(
            """
            SELECT id_lista_valores_item AS Id, codigo AS Codigo, nombre AS Nombre, CAST(0 AS bit) AS EsPredeterminado, descripcion AS Descripcion
            FROM gen.LISTA_VALORES_ITEM
            WHERE id_lista_valores_tipo = @idTipo AND estado = 'Activo'
            ORDER BY ISNULL(orden, 2147483647), nombre
            """, new { idTipo });
        return filas.ToList();
    }

    /// <summary>Paises reguladores activos; el de la compania del actor viene preseleccionado.</summary>
    public async Task<IReadOnlyList<CatalogoItemResponse>> ObtenerPaisesReguladoresAsync(long idCompania)
    {
        using var conexion = NuevaConexion();
        var filas = await conexion.QueryAsync<CatalogoItemResponse>(
            """
            SELECT p.id_pais_regulador AS Id, p.codigo_iso_pais AS Codigo, p.nombre_pais AS Nombre,
                   CASE WHEN c.id_compania IS NOT NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS EsPredeterminado, CAST(NULL AS nvarchar(500)) AS Descripcion
            FROM gen.PAIS_REGULADOR p
            LEFT JOIN gen.COMPANIA c ON c.id_pais_regulador = p.id_pais_regulador AND c.id_compania = @idCompania
            WHERE p.estado = 'Activo'
            ORDER BY p.nombre_pais
            """, new { idCompania });
        return filas.ToList();
    }
}
