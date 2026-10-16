namespace CoreBancario.Api.Modelos;

/// <summary>Fila del maestro de mensajes (Administracion > Mensajes del sistema).</summary>
public record MensajeSistemaResponse(
    long IdMensaje, string Codigo, string Nombre, string Severidad, string Modulo, string Resultado, int HttpEstado, string Texto, string TextoDefecto,
    string? Descripcion, bool Modificado, string? ModificadoPor, DateTime? ModificadoEn);

public record MensajeEditarRequest(string Texto);

/// <summary>
/// Respuesta unica de las altas por API. Tipo = severidad (ERROR | WARNING | INFORMATIVO); Resultado = lo ocurrido
/// (CLIENTE_CREADO con numeroCliente | GESTION_PENDIENTE con numeroGestion + motivo | RECHAZADO); Codigo = ER_CLIE_001...; Nombre = clave legible.
/// Nunca incluye datos de otros clientes mas alla del diagnostico de coincidencias.
/// </summary>
public record RespuestaAltaApi(
    string Tipo, string Resultado, string Codigo, string Nombre, string Mensaje, string IdCorrelacion,
    string? NumeroCliente = null, long? IdCliente = null, string? NumeroGestion = null, string? Motivo = null,
    IReadOnlyList<string>? Detalles = null,
    IReadOnlyList<CoincidenciaApi>? Coincidencias = null);

/// <summary>Diagnostico de una coincidencia para quien integra: numero del registro parecido, porcentaje y criterios; sin nombre ni otros datos.</summary>
public record CoincidenciaApi(string Origen, string? Numero, int Porcentaje, bool Fuerte, IReadOnlyList<string> Criterios);
