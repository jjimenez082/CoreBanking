namespace CoreBancario.Api.Modelos;

/// <summary>
/// Opcion de un combo box (valor activo de una lista de valores o un pais
/// regulador). EsPredeterminado marca la opcion que conviene preseleccionar.
/// </summary>
public record CatalogoItemResponse(
    long Id,
    string Codigo,
    string Nombre,
    bool EsPredeterminado,
    string? Descripcion
);
