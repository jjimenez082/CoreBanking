using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Mantenimiento de Listas de Valores (gen.LISTA_VALORES_TIPO / _ITEM).
/// Reglas: un valor se desactiva (estado Inactivo) en vez de borrarse cuando
/// ya se uso; solo se borra si ninguna tabla lo referencia. "En uso" se
/// detecta con las llaves foraneas reales de SQL Server, de modo que las
/// tablas futuras (Cuentas, Creditos) quedan cubiertas sin tocar este codigo.
/// </summary>
public class ListasValoresRepositorio
{
    public const string ObjetoAdministracion = "LOV_ADMINISTRACION";
    public const string PrefijoObjetoLista = "LOV_";

    private readonly TenantContext _tenantContext;

    public ListasValoresRepositorio(TenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    private SqlConnection NuevaConexion() => new(_tenantContext.ConnectionString);

    // ----- Listas (tipos) ---------------------------------------------------

    public async Task<IReadOnlyList<ListaTipoResponse>> ListarTiposAsync()
    {
        using var conexion = NuevaConexion();
        const string sql = """
            SELECT t.id_lista_valores_tipo AS IdListaValoresTipo,
                   t.codigo AS Codigo, t.nombre AS Nombre, t.descripcion AS Descripcion,
                   t.estado AS Estado, t.es_sistema AS EsSistema,
                   (SELECT COUNT(*) FROM gen.LISTA_VALORES_ITEM i
                     WHERE i.id_lista_valores_tipo = t.id_lista_valores_tipo) AS CantidadItems
            FROM gen.LISTA_VALORES_TIPO t
            ORDER BY t.codigo
            """;
        return (await conexion.QueryAsync<ListaTipoResponse>(sql)).ToList();
    }

    public async Task<ListaTipoResponse?> ObtenerTipoAsync(long idTipo)
    {
        using var conexion = NuevaConexion();
        const string sql = """
            SELECT t.id_lista_valores_tipo AS IdListaValoresTipo,
                   t.codigo AS Codigo, t.nombre AS Nombre, t.descripcion AS Descripcion,
                   t.estado AS Estado, t.es_sistema AS EsSistema,
                   (SELECT COUNT(*) FROM gen.LISTA_VALORES_ITEM i
                     WHERE i.id_lista_valores_tipo = t.id_lista_valores_tipo) AS CantidadItems
            FROM gen.LISTA_VALORES_TIPO t
            WHERE t.id_lista_valores_tipo = @idTipo
            """;
        return await conexion.QuerySingleOrDefaultAsync<ListaTipoResponse>(sql, new { idTipo });
    }

    /// <summary>
    /// Crea la lista y, en la misma transaccion, su objeto de autorizacion
    /// LOV_&lt;codigo&gt; (permiso para mantener los valores de esa lista).
    /// </summary>
    public async Task<long> CrearTipoAsync(string codigo, string nombre, string? descripcion, string creadoPor)
    {
        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var id = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO gen.LISTA_VALORES_TIPO (codigo, nombre, descripcion, creado_por)
                OUTPUT INSERTED.id_lista_valores_tipo
                VALUES (@codigo, @nombre, @descripcion, @creadoPor)
                """,
                new { codigo, nombre, descripcion, creadoPor }, tx);

            await conexion.ExecuteAsync(
                """
                INSERT INTO sec.OBJETO_AUTORIZACION (codigo, nombre, tipo_objeto, descripcion, creado_por)
                VALUES (@codigoObjeto, @nombreObjeto, N'Transaccion', @descripcionObjeto, @creadoPor)
                """,
                new
                {
                    codigoObjeto = PrefijoObjetoLista + codigo,
                    nombreObjeto = "Mantener lista de valores: " + nombre,
                    descripcionObjeto = "Crear, editar, activar/desactivar y borrar los valores de la lista " + codigo + ".",
                    creadoPor
                }, tx);

            tx.Commit();
            return id;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task ActualizarTipoAsync(long idTipo, string nombre, string? descripcion, string modificadoPor)
    {
        using var conexion = NuevaConexion();
        await conexion.ExecuteAsync(
            """
            UPDATE gen.LISTA_VALORES_TIPO
               SET nombre = @nombre, descripcion = @descripcion,
                   modificado_por = @modificadoPor, modificado_en = SYSUTCDATETIME()
             WHERE id_lista_valores_tipo = @idTipo
            """,
            new { idTipo, nombre, descripcion, modificadoPor });
    }

    public async Task CambiarEstadoTipoAsync(long idTipo, string estado, string modificadoPor)
    {
        using var conexion = NuevaConexion();
        await conexion.ExecuteAsync(
            """
            UPDATE gen.LISTA_VALORES_TIPO
               SET estado = @estado, modificado_por = @modificadoPor, modificado_en = SYSUTCDATETIME()
             WHERE id_lista_valores_tipo = @idTipo
            """,
            new { idTipo, estado, modificadoPor });
    }

    /// <summary>True si otra lista cuelga de esta como lista padre.</summary>
    public async Task<bool> TipoEsPadreAsync(long idTipo)
    {
        using var conexion = NuevaConexion();
        return await conexion.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_TIPO WHERE id_tipo_lista_padre = @idTipo) THEN 1 ELSE 0 END",
            new { idTipo });
    }

    /// <summary>Borra la lista (debe estar vacia) y su objeto de autorizacion LOV_&lt;codigo&gt;.</summary>
    public async Task BorrarTipoAsync(long idTipo, string codigo)
    {
        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var codigoObjeto = PrefijoObjetoLista + codigo;

            await conexion.ExecuteAsync(
                """
                DELETE rao
                  FROM sec.ROL_AUTORIZACION_OBJETO rao
                  JOIN sec.OBJETO_AUTORIZACION oa ON oa.id_objeto_autorizacion = rao.id_objeto_autorizacion
                 WHERE oa.codigo = @codigoObjeto
                """, new { codigoObjeto }, tx);

            await conexion.ExecuteAsync(
                "DELETE FROM sec.OBJETO_AUTORIZACION WHERE codigo = @codigoObjeto",
                new { codigoObjeto }, tx);

            await conexion.ExecuteAsync(
                "DELETE FROM gen.LISTA_VALORES_TIPO WHERE id_lista_valores_tipo = @idTipo",
                new { idTipo }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ----- Valores (items) --------------------------------------------------

    // Devuelve los ids de item (del filtro dado) referenciados por cualquier
    // columna con FK hacia gen.LISTA_VALORES_ITEM. Los nombres de tabla/columna
    // salen del catalogo del sistema y se escapan con QUOTENAME; el filtro es
    // un texto fijo de este archivo, nunca entrada del usuario.
    private const string SqlItemsEnUsoPlantilla = """
        DECLARE @sql nvarchar(max) = N'';
        SELECT @sql += CASE WHEN @sql = N'' THEN N'' ELSE N' UNION ' END
             + N'SELECT ' + QUOTENAME(c.name) + N' AS id FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
             + N' WHERE ' + QUOTENAME(c.name) + N' IN (SELECT id_lista_valores_item FROM gen.LISTA_VALORES_ITEM WHERE {FILTRO})'
        FROM sys.foreign_key_columns fkc
        JOIN sys.tables  t ON t.object_id = fkc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        WHERE fkc.referenced_object_id = OBJECT_ID(N'gen.LISTA_VALORES_ITEM');
        IF @sql = N'' SET @sql = N'SELECT CAST(NULL AS bigint) AS id WHERE 1 = 0';
        EXEC sp_executesql @sql, N'@idTipo bigint, @idItem bigint', @idTipo = @idTipo, @idItem = @idItem;
        """;

    private static readonly string SqlItemsEnUsoPorTipo =
        SqlItemsEnUsoPlantilla.Replace("{FILTRO}", "id_lista_valores_tipo = @idTipo");

    private static readonly string SqlItemsEnUsoPorItem =
        SqlItemsEnUsoPlantilla.Replace("{FILTRO}", "id_lista_valores_item = @idItem");

    public async Task<IReadOnlyList<ListaItemResponse>> ListarItemsAsync(long idTipo)
    {
        using var conexion = NuevaConexion();

        var filas = (await conexion.QueryAsync(
            """
            SELECT id_lista_valores_item, id_lista_valores_tipo, codigo, nombre, orden, estado, es_sistema, descripcion
            FROM gen.LISTA_VALORES_ITEM
            WHERE id_lista_valores_tipo = @idTipo
            ORDER BY ISNULL(orden, 2147483647), codigo
            """, new { idTipo })).ToList();

        var enUso = (await conexion.QueryAsync<long?>(SqlItemsEnUsoPorTipo, new { idTipo, idItem = 0L }))
            .Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();

        return filas.Select(f => new ListaItemResponse(
            (long)f.id_lista_valores_item,
            (long)f.id_lista_valores_tipo,
            (string)f.codigo,
            (string)f.nombre,
            (int?)f.orden,
            (string)f.estado,
            (bool)f.es_sistema,
            enUso.Contains((long)f.id_lista_valores_item),
            (string?)f.descripcion)).ToList();
    }

    public async Task<ListaItemResponse?> ObtenerItemAsync(long idItem)
    {
        using var conexion = NuevaConexion();

        var f = await conexion.QuerySingleOrDefaultAsync(
            """
            SELECT id_lista_valores_item, id_lista_valores_tipo, codigo, nombre, orden, estado, es_sistema, descripcion
            FROM gen.LISTA_VALORES_ITEM
            WHERE id_lista_valores_item = @idItem
            """, new { idItem });
        if (f is null) return null;

        var enUso = (await conexion.QueryAsync<long?>(SqlItemsEnUsoPorItem, new { idTipo = 0L, idItem }))
            .Any(x => x.HasValue);

        return new ListaItemResponse(
            (long)f.id_lista_valores_item, (long)f.id_lista_valores_tipo, (string)f.codigo,
            (string)f.nombre, (int?)f.orden, (string)f.estado, (bool)f.es_sistema, enUso, (string?)f.descripcion);
    }

    public async Task<long> CrearItemAsync(long idTipo, string codigo, string nombre, int? orden, string creadoPor, string? descripcion = null)
    {
        using var conexion = NuevaConexion();
        return await conexion.QuerySingleAsync<long>(
            """
            INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, orden, creado_por, descripcion)
            OUTPUT INSERTED.id_lista_valores_item
            VALUES (@idTipo, @codigo, @nombre, @orden, @creadoPor, @descripcion)
            """, new { idTipo, codigo, nombre, orden, creadoPor, descripcion });
    }

    public async Task ActualizarItemAsync(long idItem, string nombre, int? orden, string modificadoPor, string? descripcion = null)
    {
        using var conexion = NuevaConexion();
        await conexion.ExecuteAsync(
            """
            UPDATE gen.LISTA_VALORES_ITEM
               SET nombre = @nombre, orden = @orden, descripcion = @descripcion,
                   modificado_por = @modificadoPor, modificado_en = SYSUTCDATETIME()
             WHERE id_lista_valores_item = @idItem
            """, new { idItem, nombre, orden, modificadoPor, descripcion });
    }

    public async Task CambiarEstadoItemAsync(long idItem, string estado, string modificadoPor)
    {
        using var conexion = NuevaConexion();
        await conexion.ExecuteAsync(
            """
            UPDATE gen.LISTA_VALORES_ITEM
               SET estado = @estado, modificado_por = @modificadoPor, modificado_en = SYSUTCDATETIME()
             WHERE id_lista_valores_item = @idItem
            """, new { idItem, estado, modificadoPor });
    }

    public async Task BorrarItemAsync(long idItem)
    {
        using var conexion = NuevaConexion();
        await conexion.ExecuteAsync(
            "DELETE FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_item = @idItem", new { idItem });
    }
}
