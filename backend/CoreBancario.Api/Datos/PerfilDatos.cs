using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Catalogo (en codigo) de lo que un perfil de datos puede restringir: atributos para filtrar filas y campos sensibles enmascarables.
/// Los codigos son estables: se guardan en sec.PERFIL_DATOS_FILTRO y sec.PERFIL_DATOS_CAMPO.
/// </summary>
public static class PerfilDatosCatalogo
{
    public enum TipoDato { Texto, Correo, Monto, Fecha }

    /// <summary>Atributo del cliente por el que se filtran filas. {a} = alias de cli.INTERLOCUTOR; la expresion devuelve texto.</summary>
    public record Atributo(string Codigo, string Etiqueta, string Expresion);

    public static readonly IReadOnlyList<Atributo> Atributos =
    [
        new("TIPO_PERSONA", "Tipo de cliente", "{a}.tipo_persona"),
        new("ESTADO", "Estado del cliente", "{a}.estado"),
        new("UNIDAD", "Sucursal o unidad", "CAST({a}.id_unidad AS nvarchar(50))"),
        new("ES_EMPLEADO", "Es empleado (tiene el rol Empleado)",
            "CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_ROL er JOIN gen.LISTA_VALORES_ITEM et ON et.id_lista_valores_item = er.id_tipo_rol " +
            "WHERE er.id_interlocutor = {a}.id_interlocutor AND er.estado = 'Activo' AND et.codigo = 'EMPLEADO') THEN '1' ELSE '0' END"),
    ];

    public static readonly IReadOnlyList<(string Valor, string Nombre)> TiposPersona = [("Fisica", "Persona"), ("Juridica", "Organización"), ("Grupo", "Grupo")];
    public static readonly IReadOnlyList<(string Valor, string Nombre)> Estados = [("Activo", "Activo"), ("Fusionado", "Fusionado"), ("Fallecido", "Fallecido"), ("Disuelto", "Disuelto")];
    public static readonly IReadOnlyList<(string Valor, string Nombre)> SiNo = [("1", "Sí"), ("0", "No")];

    /// <summary>Campo sensible enmascarable.</summary>
    public record Campo(string Codigo, string Etiqueta, string Descripcion, TipoDato Tipo);

    public const string Identificacion = "CLI_IDENTIFICACION";
    public const string Correo = "CLI_CORREO";
    public const string Telefono = "CLI_TELEFONO";
    public const string Direccion = "CLI_DIRECCION";
    public const string FechaNacimiento = "CLI_FECHA_NACIMIENTO";
    public const string MontosPerfil = "PF_MONTOS";
    public const string CuentaExterna = "CE_NUMERO_CUENTA";

    public static readonly IReadOnlyList<Campo> Campos =
    [
        new(Identificacion, "Número de identificación", "Cédula, pasaporte, cédula jurídica y demás identificaciones del cliente.", TipoDato.Texto),
        new(Correo, "Correo electrónico", "Contactos de tipo correo.", TipoDato.Correo),
        new(Telefono, "Teléfonos", "Contactos de tipo teléfono, móvil o WhatsApp.", TipoDato.Texto),
        new(Direccion, "Detalle de la dirección", "Las líneas de detalle de cada dirección (no la provincia, cantón ni distrito).", TipoDato.Texto),
        new(FechaNacimiento, "Fecha de nacimiento", "Fecha de nacimiento de la persona.", TipoDato.Fecha),
        new(MontosPerfil, "Montos del perfil financiero", "Gastos, activos, pasivos, patrimonio, ingresos y rangos de transacciones esperadas.", TipoDato.Monto),
        new(CuentaExterna, "Número de cuentas externas", "Número de cuenta (IBAN) de las cuentas que el cliente tiene en otras entidades.", TipoDato.Texto),
    ];

    public static Campo? BuscarCampo(string codigo) => Campos.FirstOrDefault(c => c.Codigo == codigo);
    public static Atributo? BuscarAtributo(string codigo) => Atributos.FirstOrDefault(a => a.Codigo == codigo);

    /// <summary>Tipos de mascara que tienen sentido para el tipo de dato del campo.</summary>
    public static IReadOnlyList<string> TiposMascaraCompatibles(TipoDato t) => t switch
    {
        TipoDato.Texto => ["PARCIAL", "OCULTO"],
        TipoDato.Correo => ["CORREO", "PARCIAL", "OCULTO"],
        TipoDato.Monto => ["RANGO", "OCULTO"],
        TipoDato.Fecha => ["ANIO", "OCULTO"],
        _ => ["OCULTO"],
    };
}

/// <summary>Mascara del catalogo (sec.MASCARA).</summary>
public record MascaraDef(long Id, string Codigo, string Nombre, string Tipo, int Inicio, int Fin, char Caracter)
{
    /// <summary>Cuanto revela: mayor = menos restrictiva. Sirve para elegir la mascara cuando varios perfiles enmascaran el mismo campo.</summary>
    public int Apertura => Tipo switch { "OCULTO" => 0, "RANGO" => 1, "ANIO" => 2, "CORREO" => 4, _ => Inicio + Fin };

    public string? Aplicar(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return valor;
        switch (Tipo)
        {
            case "OCULTO": return "••••";
            case "CORREO":
            {
                var arroba = valor.IndexOf('@');
                if (arroba <= 0) return Parcial(valor, 0, 0);
                return valor[0] + new string(Caracter, 3) + valor[arroba..];
            }
            case "ANIO":
                return DateOnly.TryParse(valor, CultureInfo.InvariantCulture, out var f) ? f.Year.ToString(CultureInfo.InvariantCulture) : "••••";
            case "RANGO":
                return decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? Rango(m) : "••••";
            default:
                return Parcial(valor, Inicio, Fin);
        }
    }

    public string? Aplicar(DateOnly? fecha) => fecha is null ? null : Aplicar(fecha.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    public string? Aplicar(decimal? monto) => monto is null ? null : Aplicar(monto.Value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Enmascara solo letras y digitos (los separadores se conservan); deja visibles los primeros y los ultimos. Si el valor es corto, no muestra nada.</summary>
    private string Parcial(string valor, int inicio, int fin)
    {
        var n = valor.Count(char.IsLetterOrDigit);
        var sb = new StringBuilder(valor.Length);
        var k = 0;
        foreach (var ch in valor)
        {
            if (!char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
            var visible = n > inicio + fin && (k < inicio || k >= n - fin);
            sb.Append(visible ? ch : Caracter);
            k++;
        }
        return sb.ToString();
    }

    private static string Rango(decimal monto)
    {
        var x = Math.Abs(monto);
        if (x < 10) return "0 – 9";
        var digitos = (int)Math.Floor(Math.Log10((double)x));
        var bajo = (decimal)Math.Pow(10, digitos);
        var alto = bajo * 10 - 1;
        string F(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
        return $"{F(bajo)} – {F(alto)}";
    }
}

/// <summary>Mascara efectiva de un campo para el usuario, y si puede revelarlo.</summary>
public record ReglaCampo(MascaraDef Mascara, bool PuedeRevelar);

/// <summary>
/// Perfil de datos efectivo de un usuario: union de sus perfiles activos. Filas = union de lo que permite cada perfil;
/// campos = se enmascara un campo solo si TODOS los perfiles lo enmascaran, con la mascara menos restrictiva.
/// Sin perfiles = sin restricciones.
/// </summary>
public class PerfilEfectivo
{
    public record ReglaFila(string Atributo, string Modo, IReadOnlyList<string> Valores);

    public static readonly PerfilEfectivo SinRestriccion = new();

    /// <summary>Un grupo de reglas por perfil. Un perfil sin reglas de filas ve todas las filas.</summary>
    public IReadOnlyList<IReadOnlyList<ReglaFila>> FilasPorPerfil { get; init; } = [];
    public IReadOnlyDictionary<string, ReglaCampo> Campos { get; init; } = new Dictionary<string, ReglaCampo>();

    public bool RestringeFilas => FilasPorPerfil.Count > 0 && FilasPorPerfil.All(p => p.Count > 0);
    public bool HayMascaras => Campos.Count > 0;

    public bool Enmascara(string campo) => Campos.ContainsKey(campo);
    public bool PuedeRevelar(string campo) => Campos.TryGetValue(campo, out var r) && r.PuedeRevelar;

    public string? Texto(string campo, string? valor) => Campos.TryGetValue(campo, out var r) ? r.Mascara.Aplicar(valor) : valor;
    public string? Fecha(string campo, DateOnly? valor) => Campos.TryGetValue(campo, out var r) ? r.Mascara.Aplicar(valor) : valor?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string? Monto(string campo, decimal? valor) => Campos.TryGetValue(campo, out var r) ? r.Mascara.Aplicar(valor) : valor?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Fragmento SQL "AND (...)" para limitar cli.INTERLOCUTOR (alias dado) a las filas que el usuario puede ver; vacio si no hay restriccion.
    /// Los valores viajan como parametros (prefijo pdf).
    /// </summary>
    public string PredicadoFilas(string alias, DynamicParameters parametros)
    {
        if (!RestringeFilas) return string.Empty;
        var grupos = new List<string>();
        var n = 0;
        foreach (var perfil in FilasPorPerfil)
        {
            var condiciones = new List<string>();
            foreach (var regla in perfil)
            {
                var atributo = PerfilDatosCatalogo.BuscarAtributo(regla.Atributo);
                if (atributo is null || regla.Valores.Count == 0) continue;
                var expr = atributo.Expresion.Replace("{a}", alias);
                var nombres = new List<string>();
                foreach (var v in regla.Valores)
                {
                    var nombre = $"pdf{n++}";
                    parametros.Add(nombre, v);
                    nombres.Add("@" + nombre);
                }
                var lista = string.Join(", ", nombres);
                condiciones.Add(regla.Modo == "EXCLUIR" ? $"({expr} NOT IN ({lista}) OR {expr} IS NULL)" : $"({expr} IN ({lista}))");
            }
            // Un perfil cuyas reglas quedaron vacias ve todo.
            if (condiciones.Count == 0) return string.Empty;
            grupos.Add("(" + string.Join(" AND ", condiciones) + ")");
        }
        return " AND (" + string.Join(" OR ", grupos) + ")";
    }
}

/// <summary>
/// Resuelve el perfil de datos efectivo del actor (una vez por request) y ofrece la verificacion de filas y el revelado auditado.
/// Una integracion actua con los perfiles de su usuario tecnico (ActorContext.IdUsuario).
/// </summary>
public class PerfilDatosServicio
{
    private readonly TenantContext _tenant;
    private readonly ActorContext _actor;
    private PerfilEfectivo? _cache;

    public PerfilDatosServicio(TenantContext tenant, ActorContext actor) { _tenant = tenant; _actor = actor; }

    public async Task<PerfilEfectivo> ObtenerAsync()
    {
        if (_cache is not null) return _cache;
        if (_actor.IdUsuario is null) return _cache = PerfilEfectivo.SinRestriccion;
        using var c = new SqlConnection(_tenant.ConnectionString);
        _cache = await CargarAsync(c, null, _actor.IdCompania, _actor.IdUsuario.Value);
        return _cache;
    }

    public static async Task<PerfilEfectivo> CargarAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idUsuario)
    {
        var perfiles = (await c.QueryAsync<long>(
            """
            SELECT p.id_perfil_datos FROM sec.USUARIO_PERFIL_DATOS up
            JOIN sec.PERFIL_DATOS p ON p.id_perfil_datos = up.id_perfil_datos
            WHERE up.id_usuario = @idUsuario AND p.id_compania = @idCompania AND p.estado = 'Activo'
            """, new { idUsuario, idCompania }, tx)).ToList();
        if (perfiles.Count == 0) return PerfilEfectivo.SinRestriccion;

        var filtros = (await c.QueryAsync("SELECT id_perfil_datos AS p, atributo, modo, valor FROM sec.PERFIL_DATOS_FILTRO WHERE id_perfil_datos IN @perfiles",
            new { perfiles }, tx)).ToList();
        var porPerfil = perfiles.Select(id => (IReadOnlyList<PerfilEfectivo.ReglaFila>)filtros
            .Where(f => (long)f.p == id)
            .GroupBy(f => (string)f.atributo)
            .Select(g => new PerfilEfectivo.ReglaFila(g.Key, (string)g.First().modo, g.Select(x => (string)x.valor).ToList()))
            .ToList()).ToList();

        var campos = (await c.QueryAsync(
            """
            SELECT pc.id_perfil_datos AS p, pc.campo, pc.permite_revelar AS rev, m.id_mascara, m.codigo, m.nombre, m.tipo, m.visibles_inicio, m.visibles_fin, m.caracter
            FROM sec.PERFIL_DATOS_CAMPO pc JOIN sec.MASCARA m ON m.id_mascara = pc.id_mascara
            WHERE pc.id_perfil_datos IN @perfiles
            """, new { perfiles }, tx)).ToList();
        var resultado = new Dictionary<string, ReglaCampo>();
        foreach (var grupo in campos.GroupBy(x => (string)x.campo))
        {
            // Se enmascara solo si todos los perfiles del usuario lo enmascaran (gana el menos restrictivo).
            if (grupo.Select(x => (long)x.p).Distinct().Count() < perfiles.Count) continue;
            var defs = grupo.Select(x => (Def: new MascaraDef((long)x.id_mascara, (string)x.codigo, (string)x.nombre, (string)x.tipo,
                    (int)x.visibles_inicio, (int)x.visibles_fin, ((string)x.caracter)[0]), Rev: (bool)x.rev)).ToList();
            var elegida = defs.OrderByDescending(d => d.Def.Apertura).First();
            resultado[grupo.Key] = new ReglaCampo(elegida.Def, defs.Any(d => d.Rev));
        }
        return new PerfilEfectivo { FilasPorPerfil = porPerfil, Campos = resultado };
    }

    /// <summary>¿El actor puede ver este cliente segun las reglas de filas de su perfil?</summary>
    public async Task<bool> PuedeVerClienteAsync(long idInterlocutor)
    {
        var perfil = await ObtenerAsync();
        if (!perfil.RestringeFilas) return true;
        var p = new DynamicParameters();
        p.Add("id", idInterlocutor);
        p.Add("c", _actor.IdCompania);
        var predicado = perfil.PredicadoFilas("i", p);
        using var conexion = new SqlConnection(_tenant.ConnectionString);
        return await conexion.ExecuteScalarAsync<bool>(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR i WHERE i.id_interlocutor = @id AND i.id_compania = @c{predicado}) THEN 1 ELSE 0 END", p);
    }

    /// <summary>Registra en la bitacora que el actor reveló un dato enmascarado.</summary>
    public async Task RegistrarRevelacionAsync(string campo, long idInterlocutor, string? contexto)
    {
        using var c = new SqlConnection(_tenant.ConnectionString);
        await c.ExecuteAsync(
            """
            INSERT INTO aud.LOG_CONSULTA_DATO_SENSIBLE (id_compania, id_usuario, id_usuario_integracion, entidad_negocio, llave_registro, campo, contexto)
            VALUES (@idCompania, @idUsuario, @idIntegracion, N'INTERLOCUTOR', @llave, @campo, @contexto)
            """,
            new
            {
                _actor.IdCompania, idUsuario = _actor.IdUsuario, idIntegracion = _actor.IdUsuarioIntegracion,
                llave = idInterlocutor.ToString(CultureInfo.InvariantCulture), campo, contexto
            });
        // Tambien en la auditoria general (pantalla Auditoria y pestana Auditoria de la ficha del cliente).
        await Auditoria.ConsultaAsync(c, null, _actor, "INTERLOCUTOR", idInterlocutor, "revelado." + campo, contexto is null ? "Vio el dato completo" : $"Vio el dato completo ({contexto})");
    }
}
