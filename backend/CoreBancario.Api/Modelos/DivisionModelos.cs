namespace CoreBancario.Api.Modelos;

/// <summary>Nivel de la direccion de un pais: nombre, modo (Lista/Texto) y si es obligatorio.</summary>
public record NivelConfigResponse(int Nivel, string Nombre, string Modo, bool EsObligatorio);

/// <summary>
/// Estructura de la direccion de un pais. EsPorDefecto = el pais no tiene configuracion propia
/// y usa 4 niveles de texto libre.
/// </summary>
public record DireccionConfigResponse(
    long IdPais,
    string PaisNombre,
    IReadOnlyList<NivelConfigResponse> Niveles,
    string? EtiquetaCodigoPostal,
    string? PatronCodigoPostal,
    bool CodigoPostalObligatorio,
    bool EsPorDefecto);

/// <summary>Division activa para llenar un combo (nivel N, hijos de IdPadre).</summary>
public record DivisionItemResponse(long Id, string Codigo, string Nombre);

// ----- Administracion -----

public record DivisionResponse(
    long IdDivision, long IdPais, int Nivel, long? IdDivisionPadre, string? NombrePadre,
    string Codigo, string Nombre, string Estado, int CantidadHijos);

public record NivelConfigRequest(int Nivel, string Nombre, string Modo, bool EsObligatorio);

public record DireccionConfigGuardarRequest(
    IReadOnlyList<NivelConfigRequest> Niveles,
    string? EtiquetaCodigoPostal,
    string? PatronCodigoPostal,
    bool CodigoPostalObligatorio);

public record DivisionCrearRequest(long IdPais, int Nivel, long? IdDivisionPadre, string Codigo, string Nombre);

public record DivisionEditarRequest(string Codigo, string Nombre, string Estado);

/// <summary>Fila de importacion CSV. Se identifica por (pais, codigo): si existe se actualiza el nombre, si no se crea.</summary>
public record DivisionImportFila(int Nivel, string Codigo, string? CodigoPadre, string Nombre);

public record DivisionImportRequest(long IdPais, IReadOnlyList<DivisionImportFila> Filas);

public record DivisionImportResponse(int Creadas, int Actualizadas, IReadOnlyList<string> Errores);
