namespace CoreBancario.Api.Modelos;

/// <summary>
/// Una lista de valores (gen.LISTA_VALORES_TIPO), ej. TIPO_IDENTIFICACION_FISICA.
/// EsSistema = el codigo la usa por nombre: no se puede desactivar ni borrar.
/// </summary>
public record ListaTipoResponse(
    long IdListaValoresTipo,
    string Codigo,
    string Nombre,
    string? Descripcion,
    string Estado,
    bool EsSistema,
    int CantidadItems
);

public record ListaTipoCrearRequest(string Codigo, string Nombre, string? Descripcion);

public record ListaTipoActualizarRequest(string Nombre, string? Descripcion);

/// <summary>
/// Un valor de una lista (gen.LISTA_VALORES_ITEM). EnUso = algun registro de
/// cualquier tabla lo referencia (se detecta por las llaves foraneas de la BD).
/// </summary>
public record ListaItemResponse(
    long IdListaValoresItem,
    long IdListaValoresTipo,
    string Codigo,
    string Nombre,
    int? Orden,
    string Estado,
    bool EsSistema,
    bool EnUso,
    string? Descripcion = null
);

public record ListaItemCrearRequest(string Codigo, string Nombre, int? Orden, string? Descripcion = null);

public record ListaItemActualizarRequest(string Nombre, int? Orden, string? Descripcion = null);

public record CambiarEstadoRequest(string Estado);
