namespace CoreBancario.Api.Modelos;

// ----- Estructura organizativa (unidades) -----

/// <summary>Unidad de la estructura organizativa (sec.DEPARTAMENTO). Ruta = "Casa matriz > Sucursal Centro > Cajas".</summary>
public record UnidadResponse(
    long IdUnidad, string Codigo, string Nombre,
    long IdTipoUnidad, string TipoNombre,
    long? IdPadre, string? NombrePadre,
    long? IdResponsable, string? ResponsableNombre,
    string Estado, int CantidadHijos, int CantidadUsuarios, string Ruta, int Profundidad);

public record UnidadGuardarRequest(string Codigo, string Nombre, long IdTipoUnidad, long? IdPadre, long? IdResponsable);

public record UnidadEstadoRequest(string Estado);

/// <summary>Unidad activa para llenar un combo (Ruta ayuda a distinguir homonimas).</summary>
public record UnidadItemResponse(long Id, string Codigo, string Nombre, string Ruta, string TipoNombre, bool EsPredeterminado);

// ----- Usuarios (mantenimiento) -----

public record UsuarioAdminResponse(
    long IdUsuario, string? Username, string NombreCompleto, string? Nombres, string? Apellidos, string Correo, string Estado,
    long? IdUnidad, string? UnidadNombre, long? IdSuperior, string? SuperiorNombre,
    IReadOnlyCollection<string> CodigosRol, DateTime CreadoEn, string CreadoPor, DateTime? ModificadoEn, string? ModificadoPor);

public record UsuarioCrearRequest(
    string Username, string Nombres, string Apellidos, string Correo,
    long? IdUnidad, long? IdSuperior, IReadOnlyCollection<string>? CodigosRol);

public record UsuarioEditarRequest(
    string Nombres, string Apellidos, string Correo,
    long? IdUnidad, long? IdSuperior, IReadOnlyCollection<string>? CodigosRol);

public record UsuarioEstadoRequest(string Estado);

/// <summary>Resultado de crear o restablecer: la contrasena temporal se muestra UNA sola vez.</summary>
public record UsuarioCredencialResponse(long IdUsuario, string Username, string PasswordTemporal);

/// <summary>Usuario activo para llenar un combo (superior, responsable).</summary>
public record UsuarioItemResponse(long Id, string Nombre, string? Username);
