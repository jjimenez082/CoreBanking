using System.Text.Json;

namespace CoreBancario.Api.Modelos;

// ----- Catalogos del sistema -------------------------------------------------------------------------------
public record ReglaContextoResponse(long IdContexto, string Codigo, string Nombre, string Estado);
public record ReglaAtributoResponse(long IdAtributo, string Codigo, string Nombre, string? Descripcion, string TipoDato, string? ListaCodigo, bool EsMultivalor);
public record ReglaOperadorResponse(long IdOperador, string Codigo, string Nombre, string? Simbolo, int Aridad, IReadOnlyList<string> TiposDato);
public record ReglaFuncionResponse(long IdFuncion, string Codigo, string Nombre, string? Descripcion, string TipoEntrada, string TipoRetorno, int CantidadParametros);
public record ReglaCatalogosResponse(IReadOnlyList<ReglaContextoResponse> Contextos, IReadOnlyList<ReglaAtributoResponse> Atributos,
    IReadOnlyList<ReglaOperadorResponse> Operadores, IReadOnlyList<ReglaFuncionResponse> Funciones);

// ----- Pool de condiciones y acciones ----------------------------------------------------------------------
public record ReglaCondicionResponse(long IdCondicion, string Codigo, string Nombre, string? Descripcion, long IdAtributo, string AtributoCodigo, string AtributoNombre,
    long? IdFuncion, string? FuncionCodigo, string? ParametroFuncion, long IdOperador, string OperadorCodigo, string OrigenValor, string? Valor1, string? Valor2,
    string? ValoresLista, long? IdAtributoValor, string? AtributoValorCodigo, string Estado, string Resumen, int Usos, bool Congelada);
public record CondicionGuardarRequest(string Codigo, string Nombre, string? Descripcion, long IdAtributo, long? IdFuncion, string? ParametroFuncion, long IdOperador,
    string OrigenValor, string? Valor1, string? Valor2, string? ValoresLista, long? IdAtributoValor);
public record CondicionDuplicarRequest(string Codigo, string Nombre);

public record AccionResponse(long IdAccion, string Codigo, string Nombre, string? Descripcion, string TipoAccion, string? Resultado, string? Clave, string? Valor,
    string? Mensaje, string Estado, string Resumen, int Usos, bool Congelada);
public record AccionGuardarRequest(string Codigo, string Nombre, string? Descripcion, string TipoAccion, string? Resultado, string? Clave, string? Valor, string? Mensaje);

// ----- Conjuntos y versiones -------------------------------------------------------------------------------
public record ConjuntoReglasResponse(long IdConjunto, long IdContexto, string ContextoCodigo, string ContextoNombre, string Codigo, string Nombre, string? Descripcion,
    string PoliticaResolucion, string Estado, int? VersionVigente, int? VersionEnEdicion, int? VersionProgramada, int Referencias);
public record ConjuntoReglasGuardarRequest(long IdContexto, string Codigo, string Nombre, string? Descripcion, string PoliticaResolucion);
public record ConjuntoReferenciaResponse(string TipoConsumidor, long IdConsumidor, string Descripcion);
public record ElegibilidadRequest(long? IdConjunto);

public record ReglaMiembroDetalle(long IdCondicion, string Codigo, string Nombre, string Resumen, bool Negar, string Estado);
public record ReglaGrupoDetalle(string Operador, IReadOnlyList<ReglaMiembroDetalle> Miembros, IReadOnlyList<ReglaGrupoDetalle> Subgrupos);
public record ReglaAccionDetalle(long IdAccion, string Codigo, string Nombre, string Resumen, string Estado);
public record ReglaDetalle(long IdRegla, int Orden, string Codigo, string Nombre, string? Descripcion, bool Activa, ReglaGrupoDetalle? Condiciones, IReadOnlyList<ReglaAccionDetalle> Acciones);
public record ReglasVersionDetalleResponse(VersionResponse Version, long IdConjunto, string ContextoCodigo, IReadOnlyList<ReglaDetalle> Reglas);

public record ReglaGrupoGuardar(string Operador, IReadOnlyList<ReglaMiembroGuardar>? Miembros, IReadOnlyList<ReglaGrupoGuardar>? Subgrupos);
public record ReglaMiembroGuardar(long IdCondicion, bool Negar);
public record ReglaGuardar(string Codigo, string Nombre, string? Descripcion, bool Activa, ReglaGrupoGuardar? Condiciones, IReadOnlyList<long>? Acciones);
public record ReglasGuardarRequest(IReadOnlyList<ReglaGuardar> Reglas, string? Comentario);

// ----- Evaluacion y pruebas --------------------------------------------------------------------------------
public record EvaluarRequest(Dictionary<string, JsonElement> Datos, long? IdVersion, DateOnly? FechaProceso);
public record TrazaCondicion(string Condicion, string Atributo, string? Observado, string Operador, string? Esperado, bool Negada, bool Resultado, string? Error);
public record TrazaRegla(string Regla, string Nombre, bool Cumplida, IReadOnlyList<TrazaCondicion> Condiciones);
public record EvaluacionResultado(string? Decision, IReadOnlyDictionary<string, string> Asignaciones, IReadOnlyList<string> Mensajes,
    IReadOnlyList<string> ReglasAplicadas, bool CoincidioAlguna, IReadOnlyList<TrazaRegla> Traza);
public record ReglaEvaluacionResponse(long? IdEvaluacion, long IdVersion, int NumeroVersion, string Modo, EvaluacionResultado Resultado);
public record EvaluacionFilaResponse(long IdEvaluacion, long IdVersion, string ConjuntoCodigo, int NumeroVersion, string Modo, string? Referencia, string? Decision, string? ReglaAplicada, string EvaluadoPor, DateTime EvaluadoEn);
public record EvaluacionDetalleResponse(EvaluacionFilaResponse Fila, string Entrada, string Resultado, string? Traza);

public record CasoPruebaResponse(long IdCaso, long IdConjunto, string Nombre, string Entrada, string? DecisionEsperada, string? ReglaEsperada, string Estado);
public record CasoPruebaGuardarRequest(string Nombre, Dictionary<string, JsonElement> Entrada, string? DecisionEsperada, string? ReglaEsperada);
public record CasoPruebaResultado(long IdCaso, string Nombre, bool Paso, string? DecisionObtenida, string? ReglaObtenida, string? DecisionEsperada, string? ReglaEsperada, string? Detalle);
public record CasosEjecutarResponse(long IdVersion, int NumeroVersion, int Total, int Fallidos, IReadOnlyList<CasoPruebaResultado> Casos);
