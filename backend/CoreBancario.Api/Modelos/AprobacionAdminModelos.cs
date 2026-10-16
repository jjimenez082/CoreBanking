namespace CoreBancario.Api.Modelos;

// ----- Reglas de aprobacion -----
public record TipoGestionAdminResponse(long IdTipoGestion, string Codigo, string Nombre, string Categoria, bool PermiteRepetir, int ReglasActivas, int ReglasTotal);
public record CondicionDto(string Variable, string Operador, string Valor);
public record NivelReglaDto(string Tipo, long? IdUsuario, long? IdGrupo);
public record CondicionResponse(string Variable, string Operador, string Valor, string ValorTexto);
public record NivelReglaResponse(int Nivel, string Tipo, long? IdUsuario, long? IdGrupo, string Destino);
public record ReglaAdminResponse(long IdRegla, long IdTipoGestion, string Nombre, int Prioridad, string Estado,
    IReadOnlyList<CondicionResponse> Condiciones, IReadOnlyList<NivelReglaResponse> Niveles, int GestionesUsandola);
public record ReglaGuardarRequest(long IdTipoGestion, string Nombre, IReadOnlyList<CondicionDto> Condiciones, IReadOnlyList<NivelReglaDto> Niveles);
public record ReglaOrdenRequest(long IdTipoGestion, IReadOnlyList<long> IdsReglas);
public record PermiteRepetirRequest(bool PermiteRepetir);

public record SimulacionRequest(long IdTipoGestion, long IdUsuario, string Canal);
public record SimulacionCondicionResponse(string Texto, bool Cumple);
public record SimulacionReglaResponse(long IdRegla, string Nombre, int Prioridad, bool Aplica, IReadOnlyList<SimulacionCondicionResponse> Condiciones);
public record SimulacionNivelResponse(int Nivel, string Tipo, string Aprobador, string? Nota);
public record SimulacionResponse(string Solicitante, string Resumen, long? IdReglaAplicada, string? ReglaAplicada,
    IReadOnlyList<SimulacionReglaResponse> Reglas, IReadOnlyList<SimulacionNivelResponse> Niveles);

// ----- Grupos de distribucion -----
public record GrupoAdminResponse(long IdGrupo, string Codigo, string Nombre, string? Descripcion, string Estado, int Miembros, int ReglasQueLoUsan);
public record GrupoGuardarRequest(string Codigo, string Nombre, string? Descripcion);
public record GrupoMiembroResponse(long IdUsuario, string Nombre, string? Username, string? Unidad);
public record GrupoMiembrosRequest(IReadOnlyList<long> IdsUsuarios);
public record GrupoCatalogoResponse(long IdGrupo, string Codigo, string Nombre);
