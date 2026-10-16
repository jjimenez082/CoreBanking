namespace CoreBancario.Api.Modelos;

// ----- Restricciones por tipo de bloqueo: que acciones impide cada tipo ----------------------------------------------------

public record AccionRestringibleResponse(long Id, string Codigo, string Nombre, string? Descripcion);

/// <summary>Alta o edicion de un tipo de bloqueo (el codigo solo se define al crearlo).</summary>
public record TipoBloqueoRequest(string? Codigo, string Nombre, string? Descripcion);

/// <summary>Un tipo de bloqueo y las acciones que impide mientras haya un bloqueo vigente de ese tipo.</summary>
public record TipoBloqueoRestriccionResponse(long IdTipoBloqueo, string Codigo, string Nombre, IReadOnlyList<long> IdsAcciones, string? Descripcion = null);

public record MatrizRestriccionResponse(IReadOnlyList<AccionRestringibleResponse> Acciones, IReadOnlyList<TipoBloqueoRestriccionResponse> Tipos);

public record TipoBloqueoRestriccionDto(long IdTipoBloqueo, List<long> IdsAcciones);
public record MatrizRestriccionRequest(List<TipoBloqueoRestriccionDto> Tipos);

/// <summary>Lo que el cliente puede o no hacer hoy, segun sus bloqueos vigentes (y su KYC para abrir productos).</summary>
public record RestriccionClienteItemResponse(string Codigo, string Accion, bool Permitida, string? Motivo);
public record RestriccionesClienteResponse(long IdInterlocutor, bool Bloqueado, IReadOnlyList<RestriccionClienteItemResponse> Acciones);
