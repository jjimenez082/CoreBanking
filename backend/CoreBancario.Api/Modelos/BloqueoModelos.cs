namespace CoreBancario.Api.Modelos;

// ----- Bloqueo de clientes (gestiones CLI_BLOQUEO y CLI_DESBLOQUEO) --------------------------------------------------

/// <summary>Solicitud de bloqueo o de desbloqueo de un tipo de bloqueo concreto, con su comentario obligatorio.</summary>
public record BloqueoSolicitudRequest(long IdTipoBloqueo, string Comentario);

/// <summary>Bloqueo vigente del cliente (resumen que viaja en la ficha y en la API).</summary>
public record BloqueoVigenteResponse(long IdTipoBloqueo, string CodigoTipo, string Tipo, DateTime Desde);

public record BloqueoResponse(
    long Id, long IdTipoBloqueo, string CodigoTipo, string Tipo, string Comentario, string Estado,
    DateTime FechaBloqueo, string BloqueadoPor, long? IdGestionBloqueo,
    DateTime? FechaLevantamiento, string? ComentarioLevantamiento, string? LevantadoPor, long? IdGestionLevantamiento);

/// <summary>Solicitud de bloqueo o desbloqueo todavia pendiente de aprobacion (una por cliente y tipo).</summary>
public record BloqueoPendienteResponse(long IdGestion, string Numero, string Accion, long IdTipoBloqueo, string Tipo, string SolicitadoPor);

public record BloqueosClienteResponse(
    long IdInterlocutor, string Numero, string Nombre, string TipoPersona, string Estado, bool Bloqueado,
    IReadOnlyList<BloqueoResponse> Bloqueos, IReadOnlyList<BloqueoPendienteResponse> Pendientes);

public record BloqueoListadoItemResponse(
    long IdBloqueo, long IdInterlocutor, string Numero, string Nombre, string TipoPersona, long IdTipoBloqueo, string Tipo,
    string Comentario, DateTime FechaBloqueo, string BloqueadoPor);

// ----- Consentimientos del cliente (gestion CLI_CONSENTIMIENTO) ------------------------------------------------------

public record ConsentimientoCambioDto(long IdTipoConsentimiento, bool Otorgar);

/// <summary>Solicitud de otorgar y/o revocar consentimientos; el canal es comun a todos los cambios.</summary>
public record ConsentimientoSolicitudRequest(List<ConsentimientoCambioDto> Cambios, long IdCanal, string? Comentario);

public record ConsentimientoResponse(
    long Id, long IdTipoConsentimiento, string Tipo, string Estado, DateTime FechaOtorgamiento, string Canal, string? Comentario, string OtorgadoPor,
    long? IdGestionOtorgamiento, DateTime? FechaRevocacion, string? CanalRevocacion, string? ComentarioRevocacion, string? RevocadoPor, long? IdGestionRevocacion);

public record ConsentimientoPendienteResponse(long IdGestion, string Numero, string SolicitadoPor);

public record ConsentimientosClienteResponse(
    long IdInterlocutor, string Numero, string Nombre, string TipoPersona, string Estado,
    IReadOnlyList<ConsentimientoResponse> Consentimientos, ConsentimientoPendienteResponse? Pendiente);
