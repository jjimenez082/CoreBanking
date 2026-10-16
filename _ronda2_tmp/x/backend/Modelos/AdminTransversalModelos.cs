namespace CoreBancario.Api.Modelos;

// ----- Monedas -------------------------------------------------------------------------------------------
public record MonedaResponse(long IdMoneda, string Codigo, string Nombre, string Simbolo, int Decimales, bool EsBase, string Estado, int CantidadTiposCambio);
public record MonedaGuardarRequest(string Codigo, string Nombre, string Simbolo, int Decimales);
public record EstadoRequest(string Estado);

// ----- Tipo de cambio --------------------------------------------------------------------------------------
public record TipoCambioResponse(
    long IdTipoCambio, long IdMoneda, string MonedaCodigo, string MonedaNombre, DateOnly FechaVigencia,
    decimal Compra, decimal Venta, decimal Contable, string Origen, long? IdGestion, string CreadoPor, DateTime CreadoEn);

public record TipoCambioVigenteResponse(
    long IdMoneda, string MonedaCodigo, string MonedaNombre, string MonedaBase, DateOnly? FechaVigencia,
    decimal? Compra, decimal? Venta, decimal? Contable);

public record TipoCambioSolicitudRequest(long IdMoneda, DateOnly FechaVigencia, decimal Compra, decimal Venta, decimal Contable, string? Comentario, long? IdGestionDevuelta = null);

// ----- Calendario de feriados ---------------------------------------------------------------------------
public record FeriadoResponse(long IdFeriado, long IdPais, string PaisNombre, DateOnly Fecha, string Nombre, bool EsRecurrente, string Estado);
public record FeriadoGuardarRequest(long IdPais, DateOnly Fecha, string Nombre, bool EsRecurrente);
public record DiaHabilResponse(DateOnly Fecha, bool EsHabil, string? Motivo);
public record ProximoHabilResponse(DateOnly Desde, DateOnly Resultado, int DiasAvanzados);

// ----- Parametros generales -------------------------------------------------------------------------------
public record ParametroResponse(long IdParametro, string Codigo, string Nombre, string? Descripcion, string Categoria, string TipoDato, string Valor, string ValorDefecto, bool EsDefecto, string? ModificadoPor, DateTime? ModificadoEn);
public record ParametroValorRequest(string Valor);

// ----- Consulta de auditoria ---------------------------------------------------------------------------------
public record AuditoriaFilaResponse(
    long IdLog, DateTime FechaHora, string Entidad, string Llave, string Accion, string? Campo,
    string? Anterior, string? Nuevo, long? IdGestion, string Usuario);
public record AuditoriaPaginaResponse(IReadOnlyList<AuditoriaFilaResponse> Items, int Pagina, int TamanoPagina, int Total);

// ----- Usuarios de integracion ------------------------------------------------------------------------------
public record IntegracionResponse(long IdUsuarioIntegracion, string ClientId, string Nombre, string Estado, long? IdCanal, string? CanalNombre);
public record IntegracionCanalRequest(long IdCanal);
