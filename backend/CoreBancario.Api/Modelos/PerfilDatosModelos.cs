namespace CoreBancario.Api.Modelos;

// Administracion de perfiles de datos y mascaras (permiso SEG_PERFILES).

public record MascaraResponse(
    long IdMascara, string Codigo, string Nombre, string? Descripcion, string Tipo, int VisiblesInicio, int VisiblesFin, string Caracter,
    bool EsSistema, string Estado, string Ejemplo, int PerfilesQueLaUsan);

public record MascaraGuardarRequest(string Codigo, string Nombre, string? Descripcion, string Tipo, int VisiblesInicio, int VisiblesFin, string? Caracter);

public record PerfilFiltroDto(string Atributo, string Modo, IReadOnlyList<string> Valores);
public record PerfilCampoDto(string Campo, long IdMascara, bool PermiteRevelar);

public record PerfilDatosResumenResponse(
    long IdPerfilDatos, string Codigo, string Nombre, string? Descripcion, string Estado, string Filas, int Campos, int Usuarios);

public record PerfilDatosResponse(
    long IdPerfilDatos, string Codigo, string Nombre, string? Descripcion, string Estado,
    IReadOnlyList<PerfilFiltroDto> Filtros, IReadOnlyList<PerfilCampoDto> Campos, IReadOnlyList<PerfilUsuarioResponse> Usuarios);

public record PerfilDatosGuardarRequest(
    string Codigo, string Nombre, string? Descripcion, IReadOnlyList<PerfilFiltroDto>? Filtros, IReadOnlyList<PerfilCampoDto>? Campos);

public record PerfilUsuarioResponse(long IdUsuario, string Codigo, string Nombre);

/// <summary>Usuarios con los perfiles de datos que tienen asignados.</summary>
public record UsuarioPerfilesResponse(long IdUsuario, string Codigo, string Nombre, string Tipo, string Estado, IReadOnlyList<long> Perfiles);
public record UsuarioPerfilesGuardarRequest(IReadOnlyList<long> Perfiles);

public record OpcionValorResponse(string Valor, string Nombre);
public record CatalogoAtributoResponse(string Codigo, string Etiqueta, IReadOnlyList<OpcionValorResponse> Valores);
public record CatalogoCampoResponse(string Codigo, string Etiqueta, string Descripcion, string Tipo, IReadOnlyList<string> TiposMascara);
public record PerfilCatalogoResponse(IReadOnlyList<CatalogoAtributoResponse> Atributos, IReadOnlyList<CatalogoCampoResponse> Campos, IReadOnlyList<MascaraResponse> Mascaras);

/// <summary>Revelado de un dato enmascarado: Campo del catalogo; IdItem = id de la identificacion, contacto, direccion o cuenta externa cuando el campo es por item.</summary>
public record RevelarRequest(string Campo, long? IdItem);
public record RevelarResponse(string Campo, long? IdItem, IReadOnlyDictionary<string, string?> Valores);
