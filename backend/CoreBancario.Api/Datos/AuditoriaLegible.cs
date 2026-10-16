using Dapper;
using Microsoft.Data.SqlClient;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Nombres funcionales para la bitacora (aud.LOG_AUDITORIA). En la base se guarda el nombre tecnico del campo y los
/// valores como id (estable para consultas); al mostrarlos se traducen a etiquetas y descripciones que entienda el usuario.
/// </summary>
public static class AuditoriaLegible
{
    public sealed record Catalogos(
        Dictionary<long, string> Items, Dictionary<long, string> Paises, Dictionary<long, string> Divisiones,
        Dictionary<long, string> Unidades, Dictionary<long, string> Usuarios, Dictionary<long, string> Monedas);

    private static readonly Dictionary<string, string> Entidades = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INTERLOCUTOR"] = "Cliente",
        ["REGLA_APROBACION"] = "Regla de aprobación",
        ["GRUPO_USUARIOS"] = "Grupo de distribución",
        ["TIPO_GESTION"] = "Tipo de gestión",
        ["USUARIO"] = "Usuario",
        ["ROL"] = "Rol",
        ["UNIDAD_ORG"] = "Unidad organizativa",
        ["USUARIO_INTEGRACION"] = "Usuario de integración",
        ["MONEDA"] = "Moneda",
        ["TIPO_CAMBIO"] = "Tipo de cambio",
        ["FERIADO"] = "Feriado",
        ["PARAMETRO"] = "Parámetro",
        ["GESTION"] = "Gestión",
        ["ROL_REQUISITO"] = "Requisito de rol",
        ["BASE_CALCULO"] = "Base de cálculo", ["INDICADOR_FINANCIERO"] = "Indicador financiero", ["INDICADOR_VALOR"] = "Valor de indicador",
        ["CALENDARIO"] = "Calendario", ["CATEGORIA_REGULATORIA"] = "Categoría regulatoria", ["PRODUCTO_OPERATIVO"] = "Producto operativo",
        ["PARAMETRO_PRODUCTO"] = "Parámetro de producto", ["PRODUCTO_COMERCIAL"] = "Producto comercial", ["PRODUCTO_VERSION"] = "Versión de producto",
        ["CONJUNTO_REGLAS"] = "Conjunto de reglas", ["CONJUNTO_VERSION"] = "Versión de conjunto de reglas", ["REGLA_CONDICION"] = "Condición de regla", ["REGLA_ACCION"] = "Acción de regla", ["REGLA_CASO"] = "Caso de prueba de reglas",
    };

    // Campos propios de las pantallas de administracion (los de cliente salen de GestionRepositorio.EtiquetaCampo).
    private static readonly Dictionary<string, string> Campos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nombre"] = "Nombre", ["client_secret"] = "Secreto del cliente", ["alta"] = "Alta de cliente", ["estado"] = "Estado", ["codigo"] = "Código", ["correo"] = "Correo", ["roles"] = "Roles", ["permisos"] = "Permisos",
        ["password"] = "Contraseña", ["es_base"] = "Moneda base", ["simbolo"] = "Símbolo", ["decimales"] = "Decimales",
        ["fecha"] = "Fecha", ["es_recurrente"] = "Se repite cada año", ["id_pais"] = "País", ["id_canal"] = "Canal", ["id_usuario"] = "Usuario asociado",
        ["id_unidad"] = "Unidad (sucursal)", ["id_tipo_unidad"] = "Tipo de unidad", ["id_departamento_padre"] = "Unidad superior",
        ["id_responsable"] = "Responsable", ["id_superior"] = "Superior directo", ["permite_registros"] = "Permite registros",
        ["id_moneda"] = "Moneda", ["fecha_vigencia"] = "Vigente desde", ["compra"] = "Compra", ["venta"] = "Venta", ["contable"] = "Valor contable",
        ["valor"] = "Valor", ["puesto"] = "Puesto", ["prioridad"] = "Prioridad", ["condiciones"] = "Condiciones", ["niveles"] = "Niveles",
        ["miembros"] = "Integrantes", ["descripcion"] = "Descripción", ["permite_repetir"] = "Mismo usuario en varios niveles", ["orden"] = "Orden de las reglas",
        ["requisito"] = "Campo obligatorio por rol",
    };

    public static string EntidadNombre(string entidad) => Entidades.GetValueOrDefault(entidad, entidad);

    public static IReadOnlyDictionary<string, string> NombresEntidad => Entidades;

    public static async Task<Catalogos> CargarAsync(SqlConnection c, SqlTransaction? tx = null)
    {
        async Task<Dictionary<long, string>> D(string sql) =>
            (await c.QueryAsync(sql, null, tx)).ToDictionary(x => (long)x.id, x => (string)x.nombre);
        return new Catalogos(
            await D("SELECT id_lista_valores_item AS id, nombre FROM gen.LISTA_VALORES_ITEM"),
            await D("SELECT id_pais_regulador AS id, nombre_pais AS nombre FROM gen.PAIS_REGULADOR"),
            await D("SELECT id_division AS id, nombre FROM gen.DIVISION_TERRITORIAL"),
            await D("SELECT id_departamento AS id, nombre FROM sec.DEPARTAMENTO"),
            await D("SELECT id_usuario AS id, nombre_completo AS nombre FROM sec.USUARIO"),
            await D("SELECT id_moneda AS id, codigo + ' · ' + nombre AS nombre FROM gen.MONEDA"));
    }

    /// <summary>Nombre funcional del campo: «Dirección #12 · Dirección exacta (línea 1)», «Unidad (sucursal)», «Primer nombre»...</summary>
    public static string? Campo(string entidad, string? campo)
    {
        if (campo is null) return null;
        if (campo.StartsWith("revelado.", StringComparison.Ordinal))
            return "Reveló dato sensible: " + (PerfilDatosCatalogo.BuscarCampo(campo["revelado.".Length..])?.Etiqueta ?? campo["revelado.".Length..]);
        if (campo.StartsWith("aprobador_nivel_", StringComparison.Ordinal)) return $"Aprobador del nivel {campo["aprobador_nivel_".Length..]}";
        if (entidad.Equals("INTERLOCUTOR", StringComparison.OrdinalIgnoreCase))
        {
            var e = GestionRepositorio.EtiquetaCampo(campo);
            if (e != campo) return e;
        }
        return Campos.GetValueOrDefault(campo, campo);
    }

    /// <summary>Valor en texto legible: ids de listas, unidades, usuarios o monedas por su descripcion; 1/0 como Sí/No.</summary>
    public static string? Valor(string entidad, string? campo, string? valor, Catalogos cat)
    {
        if (valor is null || campo is null) return valor;

        if (entidad.Equals("INTERLOCUTOR", StringComparison.OrdinalIgnoreCase))
        {
            var t = GestionRepositorio.TextoCampo(campo, valor, cat.Items, cat.Paises, cat.Divisiones, cat.Unidades, cat.Monedas);
            if (t != valor) return t;
        }

        var col = campo;
        var corchete = col.LastIndexOf('.');
        if (corchete >= 0) col = col[(corchete + 1)..];

        if (col is "es_base" or "es_recurrente" or "es_principal" && (valor is "0" or "1")) return valor == "1" ? "Sí" : "No";
        if (!long.TryParse(valor, out var id)) return valor;

        return col switch
        {
            "id_unidad" or "id_departamento_padre" => cat.Unidades.GetValueOrDefault(id, valor),
            "id_tipo_unidad" or "id_canal" or "id_pais" => cat.Items.GetValueOrDefault(id, valor),
            "id_superior" or "id_responsable" or "id_usuario" => cat.Usuarios.GetValueOrDefault(id, valor),
            "id_moneda" or "id_moneda_balance" => cat.Monedas.GetValueOrDefault(id, valor),
            _ => valor
        };
    }
}
