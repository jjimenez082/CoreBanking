using CoreBancario.Api.Datos;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Endpoints;

/// <summary>
/// Mantenimiento del modulo de Seguridad (RBAC, SEG-002) via API, para que
/// el Backoffice ofrezca una pantalla de administracion en vez de requerir
/// SQL directo cada vez que se crea un rol o se reasignan permisos.
/// Todo el grupo exige el permiso SEG_ADMINISTRACION (sembrado en
/// sembrar_roles_clientes.sql) ademas del JWT valido.
/// </summary>
public static class SeguridadEndpoints
{
    private const string PermisoAdministracion = "SEG_ADMINISTRACION";

    public static void MapSeguridadEndpoints(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/seguridad")
            .RequireAuthorization()
            .RequierePermiso(PermisoAdministracion);

        grupo.MapGet("/objetos-autorizacion", ListarObjetosAsync);

        grupo.MapGet("/roles", ListarRolesAsync);
        grupo.MapPost("/roles", CrearRolAsync);
        grupo.MapGet("/roles/{idRol:long}", ObtenerRolAsync);
        grupo.MapPut("/roles/{idRol:long}/objetos", ActualizarObjetosRolAsync);
        grupo.MapPost("/roles/{idRol:long}/copiar", CopiarRolAsync);

        grupo.MapGet("/usuarios", ListarUsuariosAsync);
        grupo.MapPut("/usuarios/{idUsuario:long}/roles", ActualizarRolesUsuarioAsync);
    }

    private static async Task<IResult> ListarObjetosAsync(SeguridadRepositorio repositorio)
    {
        var objetos = await repositorio.ListarObjetosAutorizacionAsync();
        return Results.Ok(objetos);
    }

    private static async Task<IResult> ListarRolesAsync(ActorContext actorContext, SeguridadRepositorio repositorio)
    {
        var roles = await repositorio.ListarRolesAsync(actorContext.IdCompania);
        return Results.Ok(roles);
    }

    private static async Task<IResult> CrearRolAsync(
        RolCrearRequest request,
        ActorContext actorContext,
        SeguridadRepositorio repositorio)
    {
        if (string.IsNullOrWhiteSpace(request.Codigo) || string.IsNullOrWhiteSpace(request.Nombre))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["codigo/nombre"] = ["Son obligatorios."]
            });
        }

        // Codigo en mayusculas por convencion (igual que CLI_ALTA, CLI_CONSULTA, etc.)
        var codigoNormalizado = request.Codigo.Trim().ToUpperInvariant().Replace(' ', '_');

        try
        {
            var idRol = await repositorio.CrearRolAsync(actorContext, codigoNormalizado, request.Nombre.Trim());

            var creado = await repositorio.ObtenerRolAsync(idRol);
            return Results.Created($"/api/seguridad/roles/{idRol}", creado);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Violacion de UQ_ROL_AUTORIZACION_compania_codigo: ya existe un
            // rol con ese codigo para esta compania (o de plantilla).
            return Results.Conflict(new
            {
                error = "codigo_duplicado",
                mensaje = $"Ya existe un rol con el codigo '{codigoNormalizado}'."
            });
        }
    }

    private static async Task<IResult> CopiarRolAsync(long idRol, RolCopiarRequest request, ActorContext actorContext, SeguridadRepositorio repositorio)
    {
        var (idNuevo, error) = await repositorio.CopiarRolAsync(actorContext, idRol, request.Nombre);
        if (idNuevo is null)
            return error is null
                ? Results.NotFound(new { error = "rol_no_encontrado", idRol })
                : Results.Conflict(new { error = "rol_copia_invalida", mensaje = error });
        var creado = await repositorio.ObtenerRolAsync(idNuevo.Value);
        return Results.Created($"/api/seguridad/roles/{idNuevo}", creado);
    }

    private static async Task<IResult> ObtenerRolAsync(long idRol, SeguridadRepositorio repositorio)
    {
        var rol = await repositorio.ObtenerRolAsync(idRol);
        return rol is null ? Results.NotFound(new { error = "rol_no_encontrado", idRol }) : Results.Ok(rol);
    }

    private static async Task<IResult> ActualizarObjetosRolAsync(
        long idRol,
        RolActualizarObjetosRequest request,
        ActorContext actorContext,
        SeguridadRepositorio repositorio)
    {
        var rolExistente = await repositorio.ObtenerRolAsync(idRol);
        if (rolExistente is null)
        {
            return Results.NotFound(new { error = "rol_no_encontrado", idRol });
        }

        await repositorio.ReemplazarObjetosRolAsync(actorContext, idRol, request.CodigosObjetoAutorizacion);

        var actualizado = await repositorio.ObtenerRolAsync(idRol);
        return Results.Ok(actualizado);
    }

    private static async Task<IResult> ListarUsuariosAsync(ActorContext actorContext, SeguridadRepositorio repositorio)
    {
        var usuarios = await repositorio.ListarUsuariosAsync(actorContext.IdCompania);
        return Results.Ok(usuarios);
    }

    private static async Task<IResult> ActualizarRolesUsuarioAsync(
        long idUsuario,
        UsuarioActualizarRolesRequest request,
        ActorContext actorContext,
        SeguridadRepositorio repositorio)
    {
        var usuarioExistente = await repositorio.ObtenerUsuarioAsync(actorContext.IdCompania, idUsuario);
        if (usuarioExistente is null)
        {
            return Results.NotFound(new { error = "usuario_no_encontrado", idUsuario });
        }

        await repositorio.ReemplazarRolesUsuarioAsync(actorContext.IdCompania, idUsuario, request.CodigosRol);

        var actualizado = await repositorio.ObtenerUsuarioAsync(actorContext.IdCompania, idUsuario);
        return Results.Ok(actualizado);
    }
}
