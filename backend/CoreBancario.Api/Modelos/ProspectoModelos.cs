namespace CoreBancario.Api.Modelos;

/// <summary>Resultado de buscar una identificacion en la compania: ¿existe la persona?, ¿ya es cliente?, ¿es prospecto?</summary>
public record IdentificacionValidacionResponse(
    bool ExistePersona,
    long? IdInterlocutor,
    string? NumeroInterlocutor,
    string? Nombre,
    bool EsCliente,
    IReadOnlyList<string> Roles,
    long? IdProspecto,
    string? EstadoProspecto,
    string? NombreProspecto);

public record ProspectoResponse(
    long IdProspecto,
    string Nombre,
    string? Nombre1,
    string? Nombre2,
    string? Apellido1,
    string? Apellido2,
    DateTime? FechaNacimiento,
    long? IdTipoIdentificacion,
    string? TipoIdentificacion,
    string? NumeroIdentificacion,
    string? Telefono,
    string? Correo,
    string? CanalOrigen,
    string Estado,
    DateTime FechaRegistro,
    DateTime? FechaConversion,
    long? IdInterlocutor,
    string CreadoPor,
    long? IdUnidad,
    string? UnidadNombre,
    DireccionDto? Direccion,
    long? IdGestionPendiente,
    string? NumeroGestionPendiente);

public record ProspectoCrearRequest(
    string? Nombre1,
    string? Nombre2,
    string? Apellido1,
    string? Apellido2,
    DateTime? FechaNacimiento,
    long? IdTipoIdentificacion,
    string? NumeroIdentificacion,
    string? Telefono,
    string? Correo,
    string? CanalOrigen,
    DireccionDto? Direccion);

public record ProspectoEditarRequest(string? Nombre1, string? Nombre2, string? Apellido1, string? Apellido2, DateTime? FechaNacimiento, string? Telefono, string? Correo, string? CanalOrigen, DireccionDto? Direccion);

public record ProspectoEstadoRequest(string Estado);   // Descartado | Prospecto
