namespace CoreBancario.Api.Modelos;

public record IdentificacionCoincidenciaDto(long IdTipoIdentificacion, string Numero);

/// <summary>Datos de la persona que se quiere registrar, para buscar personas parecidas ya registradas.</summary>
public record CoincidenciaRequest(
    string? Nombre1,
    string? Nombre2,
    string? Apellido1,
    string? Apellido2,
    DateTime? FechaNacimiento,
    IReadOnlyList<IdentificacionCoincidenciaDto>? Identificaciones,
    IReadOnlyList<string>? Telefonos,
    IReadOnlyList<string>? Correos,
    long? ExcluirProspecto = null,
    long? ExcluirInterlocutor = null,
    string? TipoPersona = null,          // Fisica (por defecto) o Juridica: en Juridica se usan RazonSocial, NombresComerciales y FechaConstitucion
    string? RazonSocial = null,
    IReadOnlyList<string>? NombresComerciales = null,
    DateTime? FechaConstitucion = null);

public record CoincidenciaResponse(
    string Origen,                  // Cliente | Persona (sin rol de cliente) | Organización | Prospecto
    long Id,
    string? Numero,
    string Nombre,
    DateTime? FechaNacimiento,
    string Identificaciones,
    string Roles,
    string Estado,
    int Porcentaje,
    IReadOnlyList<string> Motivos,
    bool Fuerte);

public record CoincidenciasResultado(
    IReadOnlyList<CoincidenciaResponse> Items,
    int UmbralAlerta,
    int UmbralFuerte,
    bool FuerteBloquea);

/// <summary>Decision de quien registra tras revisar las coincidencias: «no es la misma persona» + motivo.</summary>
public record RevisionDuplicadosDto(bool Confirmado, string? Motivo);
