namespace CoreBancario.Api.Modelos;

/// <summary>Un campo cambiado dentro de una gestion de modo 'Campos' (apr.GESTION_DETALLE).</summary>
public record GestionCampoResponse(
    string NombreCampo,
    string Etiqueta,
    string? ValorAnterior,
    string? ValorNuevo
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
    IReadOnlyList<GestionCampoResponse> Campos
);

public record GestionRechazarRequest(string Motivo);

/// <summary>Resultado de una operacion de negocio con error controlado (HTTP 4xx).</summary>
public record ResultadoOperacion(bool Exito, int Estado, string? Error, string? Mensaje, long? Id = null)
{
    public static ResultadoOperacion Ok(long? id = null) => new(true, 200, null, null, id);
    public static ResultadoOperacion Fallo(int estado, string error, string mensaje) => new(false, estado, error, mensaje);
}
