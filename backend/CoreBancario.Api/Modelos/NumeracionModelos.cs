namespace CoreBancario.Api.Modelos;

public record RangoNumeracionResponse(
    long IdRangoNumeracion,
    string Objeto,
    string ObjetoNombre,
    long IdAlcance,
    int Longitud,
    long NumeroDesde,
    long NumeroHasta,
    long UltimoAsignado,
    int TamanoBloque,
    string Estado,
    long Disponibles,
    string? Proximo,
    string? ModificadoPor,
    DateTime? ModificadoEn);

public record RangoNumeracionCrearRequest(
    string Objeto, int Longitud, long NumeroDesde, long NumeroHasta, int TamanoBloque);

public record RangoNumeracionEditarRequest(
    int Longitud, long NumeroDesde, long NumeroHasta, int TamanoBloque, string Estado);
