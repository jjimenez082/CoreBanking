namespace CoreBancario.Api.Modelos;

// ----- KYC y cumplimiento (gestion CLI_KYC) ---------------------------------------------------------------------------

public record KycDto(
    long? IdEstadoKyc, long? IdNivelDebidaDiligencia, long? IdClasificacionRiesgo,
    DateOnly? FechaUltimaRevision, DateOnly? FechaProximaRevision,
    long? IdSegmentoCliente, long? IdSegmentoPricing, long? IdSegmentoRiesgo);

/// <summary>Referencia al expediente en el gestor documental (no el documento). Id null = nueva.</summary>
public record ExpedienteDto(long? Id, string Referencia, string? Sistema, string? Descripcion);

/// <summary>Nueva evaluacion PEP o de sanciones. El historial no se edita: cada evaluacion es un registro nuevo.</summary>
public record EvaluacionNuevaDto(string Tipo, long IdResultado, DateOnly Fecha, string Fuente, string? Observacion);

/// <summary>Estado deseado del bloque de cumplimiento: se compara con lo vigente y las diferencias viajan en una gestion CLI_KYC.</summary>
public record CumplimientoRequest(
    KycDto Kyc,
    List<long> IdsMotivosRiesgo,
    List<ExpedienteDto> Expedientes,
    List<EvaluacionNuevaDto> NuevasEvaluaciones,
    string? Comentario = null,
    long? IdGestionDevuelta = null);

public record KycResponse(
    long? IdEstadoKyc, string? EstadoKyc, string? CodigoEstadoKyc,
    long? IdNivelDebidaDiligencia, string? NivelDebidaDiligencia,
    long? IdClasificacionRiesgo, string? ClasificacionRiesgo, string? CodigoRiesgo,
    DateOnly? FechaUltimaRevision, DateOnly? FechaProximaRevision, bool RevisionVencida,
    long? IdSegmentoCliente, string? SegmentoCliente,
    long? IdSegmentoPricing, string? SegmentoPricing,
    long? IdSegmentoRiesgo, string? SegmentoRiesgo);

public record MotivoRiesgoResponse(long Id, long IdMotivo, string Motivo);
public record ExpedienteResponse(long Id, string Referencia, string? Sistema, string? Descripcion);
public record EvaluacionResponse(
    long Id, string Tipo, long IdResultado, string Resultado, string CodigoResultado, DateOnly Fecha, string Fuente, string? Observacion,
    bool Vigente, string CreadoPor, DateTime CreadoEn);

public record CumplimientoResponse(
    long IdInterlocutor, string TipoPersona, KycResponse? Kyc,
    IReadOnlyList<MotivoRiesgoResponse> MotivosRiesgo,
    IReadOnlyList<ExpedienteResponse> Expedientes,
    IReadOnlyList<EvaluacionResponse> Evaluaciones,
    KycVigenciaResponse? Vigencia = null);

// ----- Vigencia del KYC (calculada por fechas; la mantiene el sistema) ----------------------------------------------------

/// <summary>Situacion de vigencia del KYC al dia de hoy (zona horaria de la compania): Vigente, Por vencer, Vencido o Sin KYC.</summary>
public record KycVigenciaResponse(
    string Situacion, DateOnly? ProximaRevision, int? DiasParaRevision, DateOnly? FechaBloqueoPrevista, bool PermiteApertura, string? Mensaje);

public record KycSituacionItemResponse(
    long IdInterlocutor, string Numero, string Nombre, string TipoPersona, string? EstadoKyc, string? Riesgo,
    DateOnly? UltimaRevision, DateOnly? ProximaRevision, string Situacion, int? DiasParaRevision, bool Bloqueado);

public record KycAperturaResponse(bool Permitido, string? Codigo, string? Mensaje);
