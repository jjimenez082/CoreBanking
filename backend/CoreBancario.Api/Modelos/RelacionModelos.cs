namespace CoreBancario.Api.Modelos;

/// <summary>
/// Una relacion nueva vista desde el interlocutor de la ficha. Sentido = "Origen": este interlocutor ES [tipo] del otro
/// (p. ej. Maria es Representante legal de la empresa); "Destino": el otro es [tipo] de este interlocutor.
/// </summary>
public record RelacionNuevaDto(
    long IdOtro, long IdTipoRelacion, string Sentido,
    decimal? PorcentajeParticipacion = null, decimal? PorcentajeVoto = null, decimal? PorcentajeControl = null,
    bool EsPrincipal = false, string? Funcion = null, DateOnly? FechaInicio = null);

/// <summary>Valores deseados de una relacion vigente (el tipo y el otro interlocutor no se pueden cambiar: para eso se inactiva y se crea otra).</summary>
public record RelacionEdicionDto(
    long IdRelacion, decimal? PorcentajeParticipacion = null, decimal? PorcentajeVoto = null, decimal? PorcentajeControl = null,
    bool EsPrincipal = false, string? Funcion = null, DateOnly? FechaInicio = null);

/// <summary>Cambio de relaciones: relaciones nuevas, relaciones vigentes a modificar y a inactivar (ids), con el motivo (obligatorio).</summary>
public record RelacionSolicitudRequest(IReadOnlyList<RelacionNuevaDto>? Agregar, IReadOnlyList<long>? Cerrar, string Motivo, IReadOnlyList<RelacionEdicionDto>? Modificar = null);

public record RelacionTipoResponse(
    long IdTipoRelacion, string Codigo, string Nombre, string EtiquetaInversa, bool UsaPorcentajes, bool EsSimetrica,
    IReadOnlyList<string> TiposOrigen, IReadOnlyList<string> TiposDestino);

public record RelacionDetalleResponse(
    long IdRelacion, long IdOtro, string NumeroOtro, string NombreOtro, string TipoOtro,
    long IdTipoRelacion, string Etiqueta, string Sentido,
    decimal? PorcentajeParticipacion, decimal? PorcentajeVoto, decimal? PorcentajeControl,
    bool EsPrincipal, string? Funcion, DateTime FechaInicio, DateTime? FechaFin, string? CreadoPor, string? CerradoPor);

public record RelacionesInterlocutorResponse(
    long IdInterlocutor, string Numero, string Estado, string TipoPersona,
    IReadOnlyList<RelacionDetalleResponse> Vigentes, IReadOnlyList<RelacionDetalleResponse> Historial,
    string? NumeroGestionPendiente, bool PuedeSolicitar);
