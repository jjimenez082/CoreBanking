namespace CoreBancario.Api.Modelos;

/// <summary>Cliente que participa en una fusion (se reutiliza la ficha completa para mostrar el antes).</summary>
public record FusionVistaPreviaRequest(long IdSobreviviente, long IdDuplicado);

/// <summary>Decision sobre una direccion del cliente que se absorbe: incorporarla (con el tipo elegido) o descartarla.</summary>
public record FusionDireccionDecision(long IdDireccion, bool Incorporar, long? IdTipoDireccion);

public record FusionSolicitudRequest(
    long IdSobreviviente,
    long IdDuplicado,
    string Motivo,
    IReadOnlyList<FusionDireccionDecision>? Direcciones);

/// <summary>Que pasa con una direccion del absorbido: tipo sugerido (OTRA si el sobreviviente ya tiene una de ese tipo).</summary>
public record FusionDireccionPlan(long IdDireccion, long IdTipoDireccionOriginal, long IdTipoDireccionSugerido, bool Conflicto, bool Incorporar);

/// <summary>Contacto del absorbido: se traslada o se descarta por estar repetido (mismo tipo y mismo dato).</summary>
public record FusionContactoPlan(long IdContacto, string Accion, bool SeraPrincipal);

public record FusionIdentificacionPlan(long IdIdentificacion, string Accion);

public record FusionRolPlan(long IdTipoRol, string Nombre, string Accion);

public record FusionConteo(string Concepto, int Cantidad);

public record FusionPlan(
    IReadOnlyList<FusionIdentificacionPlan> Identificaciones,
    IReadOnlyList<FusionDireccionPlan> Direcciones,
    IReadOnlyList<FusionContactoPlan> Contactos,
    IReadOnlyList<FusionRolPlan> Roles,
    IReadOnlyList<FusionConteo> Otros);

public record FusionVistaPreviaResponse(
    InterlocutorDetalleResponse Sobreviviente,
    InterlocutorDetalleResponse Duplicado,
    FusionPlan Plan,
    IReadOnlyList<string> Impedimentos,
    IReadOnlyList<string> Advertencias);

/// <summary>Cliente de «posibles duplicados» de la ficha: solo clientes/personas vigentes.</summary>
public record FusionResultadoSolicitud(long IdGestion, string? Numero);
