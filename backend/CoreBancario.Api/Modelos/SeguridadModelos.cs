namespace CoreBancario.Api.Modelos;

/// <summary>
/// Catalogo de acciones protegibles (sec.OBJETO_AUTORIZACION). Es solo
/// lectura desde esta API — el catalogo de objetos lo define cada endpoint
/// nuevo que se construye, no un administrador desde la UI.
/// </summary>
public record ObjetoAutorizacionResponse(
    long IdObjetoAutorizacion,
    string Codigo,
    string Nombre,
    string TipoObjeto,
    string? Descripcion
);

/// <summary>
/// Un rol de autorizacion (sec.ROL_AUTORIZACION) junto con los codigos de
/// sec.OBJETO_AUTORIZACION que tiene asignados.
/// </summary>
public record RolResponse(
    long IdRolAutorizacion,
    string Codigo,
    string Nombre,
    string Estado,
    IReadOnlyCollection<string> CodigosObjetoAutorizacion
);

public record RolCrearRequest(
    string Codigo,
    string Nombre
);

/// <summary>Copia un rol: solo se pide el nombre nuevo; el código se deriva y los permisos se copian del rol origen.</summary>
public record RolCopiarRequest(string Nombre);

public record RolActualizarObjetosRequest(
    IReadOnlyCollection<string> CodigosObjetoAutorizacion
);

/// <summary>
/// Un usuario nombrado (sec.USUARIO) de la compania actual, junto con los
/// codigos de rol (sec.ROL_AUTORIZACION) que tiene asignados.
/// </summary>
public record UsuarioResponse(
    long IdUsuario,
    string NombreCompleto,
    string Correo,
    string Estado,
    IReadOnlyCollection<string> CodigosRol
);

public record UsuarioActualizarRolesRequest(
    IReadOnlyCollection<string> CodigosRol
);
