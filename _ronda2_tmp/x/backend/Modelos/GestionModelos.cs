namespace CoreBancario.Api.Modelos;

/// <summary>Un campo cambiado dentro de una gestion de modo 'Campos' (apr.GESTION_DETALLE). *Crudo = valor tal como se guardo (ids, fechas ISO).</summary>
public record GestionCampoResponse(
    string NombreCampo,
    string Etiqueta,
    string? ValorAnterior,
    string? ValorNuevo,
    string? AnteriorCrudo = null,
    string? NuevoCrudo = null
);

public record GestionDetalleResponse(
    long IdGestion,
    string TipoGestion,
    string Estado,
    string EntidadNegocio,
    string LlaveRegistro,
    string SolicitadoPor,
    string? Comentario,
    string? MotivoRechazo,
    DateTime CreadoEn,
    DateTime? FechaResolucion,
    IReadOnlyList<GestionCampoResponse> Campos,
    string LlaveCruda = "",
    bool EsMia = false,
    bool PuedeResolver = false,
    string? ComentarioDevolucion = null,
    string? DevueltoPor = null,
    DateTime? FechaDevolucion = null
);

/// <summary>Fila de la bandeja de gestiones.</summary>
public record GestionResponse(
    long IdGestion,
    string TipoGestion,
    string Estado,
    string EntidadNegocio,
    string LlaveRegistro,
    string SolicitadoPor,
    DateTime CreadoEn,
    bool EsMia = false,
    bool PuedeResolver = false,
    DateTime? FechaResolucion = null
);

public record GestionRechazarRequest(string Motivo);

/// <summary>Devuelve la gestion al solicitante para que la corrija; el comentario es obligatorio.</summary>
public record GestionDevolverRequest(string Comentario);

/// <summary>Resultado de una operacion de negocio con error controlado (HTTP 4xx).</summary>
public record ResultadoOperacion(bool Exito, int Estado, string? Error, string? Mensaje, long? Id = null)
{
    public static ResultadoOperacion Ok(long? id = null) => new(true, 200, null, null, id);
    public static ResultadoOperacion Fallo(int estado, string error, string mensaje) => new(false, estado, error, mensaje);
}
