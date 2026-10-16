namespace CoreBancario.Api.Modelos;

// ----- Bases de calculo ------------------------------------------------------------------------------------
public record BaseCalculoResponse(long IdBaseCalculo, string Codigo, string Nombre, string? Descripcion, string TipoConteo, int? DiasAnioFijo, bool UsaAnioReal, string Estado);

// ----- Indicadores financieros (tasas de referencia) -----------------------------------------------------
public record IndicadorResponse(long IdIndicador, string Codigo, string Nombre, string? Descripcion, long IdTipo, string TipoNombre, string? MonedaCodigo,
    string Frecuencia, string Estado, decimal? ValorVigente, DateOnly? FechaVigente);
public record IndicadorGuardarRequest(string Codigo, string Nombre, string? Descripcion, long IdTipo, string? MonedaCodigo, string Frecuencia);
public record IndicadorValorResponse(long IdValor, DateOnly FechaVigencia, decimal Valor, string Origen, long? IdGestion, string? NumeroGestion, string CreadoPor, DateTime CreadoEn);
public record IndicadorValorSolicitudRequest(DateOnly FechaVigencia, decimal Valor, string? Comentario);
// Carga por lote (Excel): una gestion por fila valida; el resultado informa fila por fila
public record IndicadorLoteFila(int Fila, string Codigo, DateOnly FechaVigencia, decimal Valor, string? Comentario);
public record IndicadorLoteRequest(IReadOnlyList<IndicadorLoteFila> Filas);
public record IndicadorLoteResultado(int Fila, string Codigo, DateOnly FechaVigencia, bool Exito, long? IdGestion, string? NumeroGestion, string? Mensaje);

// ----- Calendarios ------------------------------------------------------------------------------------------
public record CalendarioResponse(long IdCalendario, string Codigo, string Nombre, long IdPais, string PaisNombre,
    bool Lunes, bool Martes, bool Miercoles, bool Jueves, bool Viernes, bool Sabado, bool Domingo, bool EsDefecto, string Estado);
public record CalendarioGuardarRequest(string Codigo, string Nombre, long IdPais,
    bool Lunes, bool Martes, bool Miercoles, bool Jueves, bool Viernes, bool Sabado, bool Domingo, bool EsDefecto);

// ----- Categorias regulatorias -------------------------------------------------------------------------------
public record CategoriaRegulatoriaResponse(long IdCategoria, long IdPaisRegulador, string PaisNombre, string Codigo, string Nombre, string? Descripcion, string Estado);
public record CategoriaRegulatoriaGuardarRequest(long IdPaisRegulador, string Codigo, string Nombre, string? Descripcion);

// ----- Producto operativo -----------------------------------------------------------------------------------
public record ProductoOperativoResponse(long IdProducto, string Codigo, string Nombre, string? Descripcion, string Dominio, long IdCategoria, string CategoriaNombre,
    long? IdCategoriaRegulatoria, string? CategoriaRegulatoriaNombre, string EstrategiaVersionado, string TipoPersona, long? IdCalendario, string? CalendarioNombre,
    string Estado, IReadOnlyList<ProductoMonedaResponse> Monedas, int CantidadParametros, int CantidadComerciales);
public record ProductoMonedaResponse(long IdMoneda, string Codigo, string Nombre, bool EsPrincipal);
public record ProductoOperativoGuardarRequest(string Codigo, string Nombre, string? Descripcion, string Dominio, long IdCategoria, long? IdCategoriaRegulatoria,
    string EstrategiaVersionado, string TipoPersona, long? IdCalendario, IReadOnlyList<ProductoMonedaRequest> Monedas, bool SembrarPlantilla);
public record ProductoMonedaRequest(long IdMoneda, bool EsPrincipal);

public record ParametroProductoResponse(long IdParametro, string Codigo, string Nombre, string? Descripcion, string Grupo, string TipoDato, long? IdListaTipo, string? ListaNombre, string? ListaCodigo,
    decimal? Minimo, decimal? Maximo, bool Obligatorio, string? ValorDefecto, bool EsComponenteEconomico, string? TipoCalculoEconomico, long? IdBaseComponente, string? BaseComponenteNombre,
    string? CodigoTipoEvento, int Orden, string Estado);
public record ParametroProductoGuardarRequest(string Codigo, string Nombre, string? Descripcion, string Grupo, string TipoDato, long? IdListaTipo, decimal? Minimo, decimal? Maximo,
    bool Obligatorio, string? ValorDefecto, bool EsComponenteEconomico, string? TipoCalculoEconomico, long? IdBaseComponente, string? CodigoTipoEvento, int Orden);

// ----- Producto comercial -----------------------------------------------------------------------------------
public record ProductoComercialResponse(long IdProducto, long IdProductoOperativo, string OperativoNombre, string Codigo, string Nombre, string? Descripcion,
    long? IdProductoBase, string? BaseNombre, string Estado, string EstrategiaVersionado, int? VersionVigente, int? VersionEnEdicion, int? VersionProgramada);
public record ProductoComercialGuardarRequest(long IdProductoOperativo, string Codigo, string Nombre, string? Descripcion, long? IdProductoBase);
public record VersionResponse(long IdVersion, int Numero, string Estado, DateOnly? Desde, DateOnly? Hasta, string? Comentario, long? IdGestion, string? NumeroGestion,
    string? EstadoGestion, string CreadoPor, DateTime CreadoEn);
public record VersionValorResponse(long IdParametro, string Codigo, string Nombre, string Grupo, string TipoDato, string? Valor, string? ValorHeredado, string? ValorDefecto,
    bool Obligatorio, decimal? Minimo, decimal? Maximo, long? IdListaTipo, string? ListaCodigo, bool EsComponenteEconomico, string? TipoCalculoEconomico, string? BaseComponenteNombre);
public record VersionDetalleResponse(VersionResponse Version, IReadOnlyList<VersionValorResponse> Valores);
public record VersionValorRequest(long IdParametro, string? Valor);
public record VersionValoresGuardarRequest(IReadOnlyList<VersionValorRequest> Valores, string? Comentario);
public record VersionPublicarRequest(DateOnly FechaVigenciaDesde, string? Comentario);
