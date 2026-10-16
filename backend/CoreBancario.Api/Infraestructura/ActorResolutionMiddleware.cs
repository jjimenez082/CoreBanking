using System.Security.Claims;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Datos;

namespace CoreBancario.Api.Infraestructura;

/// <summary>
/// Segundo eslabon del pipeline multi-tenant. Ya con el TenantContext resuelto,
/// identifica QUIEN llama dentro de ese tenant:
///   - Si el token viene de un login interactivo (Authorization Code + PKCE),
///     el 'sub' del JWT se busca en sec.USUARIO.subject_iam.
///   - Si el token viene de Client Credentials (cuenta de servicio), se usa
///     el claim 'azp' (o 'client_id') contra sec.USUARIO_INTEGRACION.client_id_iam.
///
/// Debe registrarse DESPUES de TenantResolutionMiddleware y ANTES de los
/// endpoints de negocio.
/// </summary>
public class ActorResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public ActorResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        TenantContext tenantContext,
        ActorContext actorContext,
        AutorizacionRepositorio autorizacionRepositorio)
    {
        if (!tenantContext.Resuelto)
        {
            // Ruta publica sin tenant (ej. /health); no hay actor que resolver.
            await _next(context);
            return;
        }

        using var conexion = new SqlConnection(tenantContext.ConnectionString);

        var subjectIam = context.User.FindFirstValue("sub");
        var clientId = context.User.FindFirstValue("azp") ?? context.User.FindFirstValue("client_id");

        // 1. Intentar como usuario humano (login interactivo).
        if (!string.IsNullOrWhiteSpace(subjectIam))
        {
            const string sqlUsuario = """
                SELECT id_compania, id_usuario, nombre_completo
                FROM sec.USUARIO
                WHERE subject_iam = @subjectIam AND estado = 'Activo' AND tipo_usuario = 'FISICO'
                """;

            var usuario = await conexion.QuerySingleOrDefaultAsync(sqlUsuario, new { subjectIam });

            if (usuario is not null)
            {
                actorContext.EstablecerUsuario(
                    (long)usuario.id_compania,
                    (long)usuario.id_usuario,
                    (string)usuario.nombre_completo);

                // RBAC (SEG-002): resolver los permisos efectivos del usuario
                // via sus roles, para que RequierePermisoFiltro pueda validar
                // cada endpoint mas abajo en el pipeline.
                var codigosPermiso = await autorizacionRepositorio.ObtenerCodigosPermisoAsync((long)usuario.id_usuario);
                actorContext.EstablecerPermisos(codigosPermiso);

                await _next(context);
                return;
            }
        }

        // 2. Intentar como cuenta de servicio (Client Credentials, SEG-010).
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            // La integracion actua como su usuario tecnico asociado (sec.USUARIO tipo INTEGRACION): ambos deben estar activos.
            const string sqlIntegracion = """
                SELECT ui.id_compania, ui.id_usuario_integracion, ui.nombre, u.id_usuario
                FROM sec.USUARIO_INTEGRACION ui
                JOIN sec.USUARIO u ON u.id_usuario = ui.id_usuario
                WHERE ui.client_id_iam = @clientId AND ui.estado = 'Activo'
                  AND u.estado = 'Activo' AND u.tipo_usuario = 'INTEGRACION'
                """;

            var integracion = await conexion.QuerySingleOrDefaultAsync(sqlIntegracion, new { clientId });

            if (integracion is not null)
            {
                actorContext.EstablecerUsuarioIntegracion(
                    (long)integracion.id_compania,
                    (long)integracion.id_usuario_integracion,
                    (long)integracion.id_usuario,
                    (string)integracion.nombre);

                var codigosTecnico = await autorizacionRepositorio.ObtenerCodigosPermisoAsync((long)integracion.id_usuario);
                actorContext.EstablecerPermisos(codigosTecnico);

                await _next(context);
                return;
            }
        }

        // Token valido y tenant valido, pero la identidad no esta dada de alta
        // en sec.USUARIO ni en sec.USUARIO_INTEGRACION dentro de ese tenant.
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "identidad_no_registrada",
            mensaje = "El token es valido pero no corresponde a una persona ni a una integracion activa (con usuario tecnico activo) en este tenant."
        });
    }
}
