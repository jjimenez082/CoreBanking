namespace CoreBancario.Api.Modelos;

// ----- Monedas -------------------------------------------------------------------------------------------
public record MonedaResponse(long IdMoneda, string Codigo, string Nombre, string Simbolo, int Decimales, bool EsBase, string Estado, int CantidadTiposCambio);
public record MonedaGuardarRequest(string Codigo, string Nombre, string Simbolo, int Decimales);
public record EstadoRequest(string Estado);

// ----- Tipo de cambio --------------------------------------------------------------------------------------
public record TipoCambioResponse(
    long IdTipoCambio, long IdMoneda, string MonedaCodigo, string MonedaNombre, DateOnly FechaVigencia,
    decimal Compra, decimal Venta, decimal Contable, string Origen, long? IdGestion, string CreadoPor, DateTime CreadoEn, string? NumeroGestion = null);

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
    string? Anterior, string? Nuevo, long? IdGestion, string Usuario, string EntidadNombre, string? NumeroGestion = null);
public record AuditoriaEntidadResponse(string Codigo, string Nombre);
/// <summary>Una linea de detalle (un campo / un registro) dentro de un evento de auditoria.</summary>
public record AuditoriaDetalleResponse(long IdLog, string Accion, string? Campo, string? Anterior, string? Nuevo);
/// <summary>Encabezado de auditoria: una operacion sobre un registro (misma gestion, o mismo usuario y segundo) con todos sus campos en el detalle.</summary>
public record AuditoriaEventoResponse(
    string Clave, DateTime FechaHora, string Usuario, string Entidad, string EntidadNombre, string Llave, string Accion, string Resumen,
    long? IdGestion, string? NumeroGestion, int Cantidad, IReadOnlyList<AuditoriaDetalleResponse> Detalle);
public record AuditoriaEventoPaginaResponse(IReadOnlyList<AuditoriaEventoResponse> Items, int Pagina, int TamanoPagina, int Total);
public record AuditoriaPaginaResponse(IReadOnlyList<AuditoriaFilaResponse> Items, int Pagina, int TamanoPagina, int Total);

// ----- Usuarios de integracion ------------------------------------------------------------------------------
public record IntegracionResponse(long IdUsuarioIntegracion, string ClientId, string Nombre, string Estado, long? IdCanal, string? CanalNombre, long? IdUsuario = null, string? UsuarioNombre = null);
public record IntegracionCanalRequest(long IdCanal);
public record IntegracionUsuarioRequest(long IdUsuario);
/// <summary>Identificador: parte propia del client_id (la API le antepone el codigo de la compania).</summary>
public record IntegracionCrearRequest(string Identificador, string Nombre, long IdCanal, long IdUsuario = 0);
/// <summary>Credenciales de una integracion. El secret solo se devuelve al crear o regenerar.</summary>
public record IntegracionCredencialResponse(long IdUsuarioIntegracion, string ClientId, string ClientSecret, string TokenUrl);

// ----- Auditoria de un cliente (linea de tiempo anio > mes > dia) ----------------------------------------------
/// <summary>Un periodo (anio, mes o dia segun el nivel) con la cantidad de eventos de auditoria. Valor = anio, mes (1-12) o dia (1-31).</summary>
public record AuditoriaPeriodoResponse(int Valor, int Cantidad, DateTime Ultimo);
public record AuditoriaClienteFusionado(long IdInterlocutor, string Numero);
public record AuditoriaPeriodosResponse(
    string Nivel, int? Anio, int? Mes, IReadOnlyList<AuditoriaPeriodoResponse> Periodos,
    DateTime? UltimoMovimiento, int TotalEventos, IReadOnlyList<AuditoriaClienteFusionado> Fusionados);

