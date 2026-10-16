namespace CoreBancario.Api.Modelos;

/// <summary>Miembro de un grupo: persona u organizacion ya registrada, con su funcion opcional en el grupo (sin % de participacion).</summary>
public record MiembroGrupoDto(long IdMiembro, string? Funcion = null);

/// <summary>Cambio de los datos de un grupo (no incluye miembros: esos se gestionan como relaciones).</summary>
public record GrupoModificacionRequest(string Nombre, long IdTipoGrupo, string? Descripcion, DateOnly? FechaFormacion, string? Comentario, long? IdGestionDevuelta = null);

/// <summary>Disolucion de un grupo: cierra sus relaciones de miembro y lo deja 'Disuelto'. El motivo es obligatorio.</summary>
public record GrupoDisolucionRequest(string Motivo);

/// <summary>Datos de un grupo para el alta: nombre, tipo (lista TIPO_GRUPO), descripcion, fecha de formacion y miembros (minimo 2).</summary>
public record GrupoDto(
    string Nombre,
    long IdTipoGrupo,
    string? Descripcion,
    DateOnly? FechaFormacion,
    IReadOnlyList<MiembroGrupoDto> Miembros);

public record GrupoResponse(
    string Nombre,
    long? IdTipoGrupo,
    string? TipoGrupo,
    string? Descripcion,
    DateOnly? FechaFormacion,
    int CantidadMiembros);
