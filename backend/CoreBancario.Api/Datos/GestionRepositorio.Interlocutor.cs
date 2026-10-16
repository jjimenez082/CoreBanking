using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestion de modificacion de un cliente (Persona u Organizacion) con sus colecciones
/// 1:n. Cada diferencia se guarda como una fila de apr.GESTION_DETALLE con un
/// nombre de campo que dice a que registro se refiere:
///
///   nombre1                              campo del interlocutor / persona fisica
///   ident[12].numero_identificacion      campo de la identificacion 12 (existente)
///   dir[+1].detalle                      campo de la direccion NUEVA numero 1
///   cont[7].estado  Activo -> Inactivo   baja del contacto 7 (baja logica)
///
/// Prefijos: ident = identificaciones, dir = direcciones, cont = contactos, nom = nombres comerciales, act = actividades economicas, nac = nacionalidades.
/// Tabla y columna salen SOLO de las listas blancas de abajo, nunca del usuario.
/// </summary>
public partial class GestionRepositorio
{
    private enum Tipo { Texto, Entero, Fecha, Bit, Decimal }

    private const string TablaPerfilEconomico = "cli.INTERLOCUTOR_PERFIL_ECONOMICO";
    private const string TablaKyc = "cli.INTERLOCUTOR_KYC";

    /// <summary>Tablas 1:1 cuya fila se crea al aplicar el primer dato.</summary>
    private static bool EsTablaUnoAUnoOpcional(string tabla) => tabla == TablaPerfilEconomico || tabla == TablaKyc;

    private record Campo(string Columna, Tipo Tipo, string Etiqueta);

    private record Coleccion(string Prefijo, string Tabla, string Pk, string Singular, Dictionary<string, Campo> Campos);

    // Campos del interlocutor y de la persona fisica (1:1).
    private static readonly Dictionary<string, (string Tabla, Campo Campo)> CamposInterlocutor = new()
    {
        ["id_pais_regulador"] = ("cli.INTERLOCUTOR", new("id_pais_regulador", Tipo.Entero, "País regulador")),
        ["id_unidad"] = ("cli.INTERLOCUTOR", new("id_unidad", Tipo.Entero, "Unidad (sucursal)")),
        ["id_canal_origen"] = ("cli.INTERLOCUTOR", new("id_canal_origen", Tipo.Entero, "Canal de origen")),
        ["nombre1"] = ("cli.INTERLOCUTOR_FISICA", new("nombre1", Tipo.Texto, "Primer nombre")),
        ["nombre2"] = ("cli.INTERLOCUTOR_FISICA", new("nombre2", Tipo.Texto, "Segundo nombre")),
        ["apellido1"] = ("cli.INTERLOCUTOR_FISICA", new("apellido1", Tipo.Texto, "Primer apellido")),
        ["apellido2"] = ("cli.INTERLOCUTOR_FISICA", new("apellido2", Tipo.Texto, "Segundo apellido")),
        ["fecha_nacimiento"] = ("cli.INTERLOCUTOR_FISICA", new("fecha_nacimiento", Tipo.Fecha, "Fecha de nacimiento")),
        ["nombre_conocido"] = ("cli.INTERLOCUTOR_FISICA", new("nombre_conocido", Tipo.Texto, "Nombre conocido")),
        ["id_pais_nacimiento"] = ("cli.INTERLOCUTOR_FISICA", new("id_pais_nacimiento", Tipo.Entero, "País de nacimiento")),
        ["id_sexo"] = ("cli.INTERLOCUTOR_FISICA", new("id_sexo", Tipo.Entero, "Sexo")),
        ["id_estado_civil"] = ("cli.INTERLOCUTOR_FISICA", new("id_estado_civil", Tipo.Entero, "Estado civil")),
        // Organizacion (persona juridica): razon_social vive en la columna nombre_org1.
        ["razon_social"] = ("cli.INTERLOCUTOR_JURIDICA", new("nombre_org1", Tipo.Texto, "Razón social")),
        ["id_forma_juridica"] = ("cli.INTERLOCUTOR_JURIDICA", new("id_forma_juridica", Tipo.Entero, "Forma jurídica")),
        ["id_pais_constitucion"] = ("cli.INTERLOCUTOR_JURIDICA", new("id_pais_constitucion", Tipo.Entero, "País de constitución")),
        ["fecha_constitucion"] = ("cli.INTERLOCUTOR_JURIDICA", new("fecha_constitucion", Tipo.Fecha, "Fecha de constitución")),
        ["referencia_registral"] = ("cli.INTERLOCUTOR_JURIDICA", new("referencia_registral", Tipo.Texto, "Referencia registral")),
        ["id_estado_legal"] = ("cli.INTERLOCUTOR_JURIDICA", new("id_estado_legal", Tipo.Entero, "Estado legal")),
        ["fecha_disolucion"] = ("cli.INTERLOCUTOR_JURIDICA", new("fecha_disolucion", Tipo.Fecha, "Fecha de disolución")),
        // Grupo: el nombre (razon_social) se refleja tambien en INTERLOCUTOR_GRUPO al aplicar; estado = disolucion del grupo.
        [GrupoServicio.CampoTipoGrupo] = ("cli.INTERLOCUTOR_GRUPO", new("id_tipo_grupo", Tipo.Entero, "Tipo de grupo")),
        [GrupoServicio.CampoDescripcion] = ("cli.INTERLOCUTOR_GRUPO", new("descripcion", Tipo.Texto, "Descripción del grupo")),
        [GrupoServicio.CampoFormacion] = ("cli.INTERLOCUTOR_GRUPO", new("fecha_formacion", Tipo.Fecha, "Fecha de formación del grupo")),
        ["estado"] = ("cli.INTERLOCUTOR", new("estado", Tipo.Texto, "Estado")),
        // Perfil economico (1:1; la fila se crea al aplicar el primer dato). Prefijo pe_ para no chocar con otros campos.
        ["pe_id_condicion_laboral"] = (TablaPerfilEconomico, new("id_condicion_laboral", Tipo.Entero, "Condición laboral")),
        ["pe_id_ocupacion"] = (TablaPerfilEconomico, new("id_ocupacion", Tipo.Entero, "Ocupación o profesión")),
        ["pe_empleador"] = (TablaPerfilEconomico, new("empleador", Tipo.Texto, "Empleador o negocio")),
        ["pe_gastos_estimados"] = (TablaPerfilEconomico, new("gastos_estimados", Tipo.Decimal, "Gastos estimados")),
        ["pe_activos_declarados"] = (TablaPerfilEconomico, new("activos_declarados", Tipo.Decimal, "Activos declarados")),
        ["pe_pasivos_declarados"] = (TablaPerfilEconomico, new("pasivos_declarados", Tipo.Decimal, "Pasivos declarados")),
        ["pe_patrimonio_declarado"] = (TablaPerfilEconomico, new("patrimonio_declarado", Tipo.Decimal, "Patrimonio declarado")),
        ["pe_id_moneda_balance"] = (TablaPerfilEconomico, new("id_moneda_balance", Tipo.Entero, "Moneda de los montos")),
        ["pe_fecha_corte"] = (TablaPerfilEconomico, new("fecha_corte", Tipo.Fecha, "Fecha de corte")),
        // KYC y cumplimiento (1:1; gestion CLI_KYC). Prefijo kyc_.
        ["kyc_id_estado_kyc"] = (TablaKyc, new("id_estado_kyc", Tipo.Entero, "Estado KYC")),
        ["kyc_id_nivel_dd"] = (TablaKyc, new("id_nivel_dd", Tipo.Entero, "Nivel de debida diligencia")),
        ["kyc_id_riesgo"] = (TablaKyc, new("id_riesgo", Tipo.Entero, "Clasificación de riesgo")),
        ["kyc_fecha_ultima_revision"] = (TablaKyc, new("fecha_ultima_revision", Tipo.Fecha, "Última revisión KYC")),
        ["kyc_fecha_proxima_revision"] = (TablaKyc, new("fecha_proxima_revision", Tipo.Fecha, "Próxima revisión KYC")),
        ["kyc_id_segmento_cliente"] = (TablaKyc, new("id_segmento_cliente", Tipo.Entero, "Segmento de cliente")),
        ["kyc_id_segmento_pricing"] = (TablaKyc, new("id_segmento_pricing", Tipo.Entero, "Segmento de pricing")),
        ["kyc_id_segmento_riesgo"] = (TablaKyc, new("id_segmento_riesgo", Tipo.Entero, "Segmento de riesgo")),
    };

    private static readonly Coleccion[] Colecciones =
    [
        new("nac", "cli.INTERLOCUTOR_NACIONALIDAD", "id_nacionalidad_interlocutor", "Nacionalidad", new()
        {
            ["id_pais"] = new("id_pais", Tipo.Entero, "País"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("ident", "cli.INTERLOCUTOR_IDENTIFICACION", "id_identificacion", "Identificación", new()
        {
            ["id_tipo_identificacion"] = new("id_tipo_identificacion", Tipo.Entero, "Tipo"),
            ["numero_identificacion"] = new("numero_identificacion", Tipo.Texto, "Número"),
            ["id_institucion_emisora"] = new("id_institucion_emisora", Tipo.Entero, "Institución emisora"),
            ["fecha_emision"] = new("fecha_emision", Tipo.Fecha, "Fecha de emisión"),
            ["fecha_vencimiento"] = new("fecha_vencimiento", Tipo.Fecha, "Fecha de vencimiento"),
            ["es_principal"] = new("es_principal", Tipo.Bit, "Principal"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("dir", "cli.INTERLOCUTOR_DIRECCION", "id_direccion", "Dirección", new()
        {
            ["id_tipo_direccion"] = new("id_tipo_direccion", Tipo.Entero, "Tipo"),
            ["id_pais"] = new("id_pais", Tipo.Entero, "País"),
            ["nivel1_id"] = new("nivel1_id", Tipo.Entero, "Nivel 1"),
            ["nivel2_id"] = new("nivel2_id", Tipo.Entero, "Nivel 2"),
            ["nivel3_id"] = new("nivel3_id", Tipo.Entero, "Nivel 3"),
            ["nivel4_id"] = new("nivel4_id", Tipo.Entero, "Nivel 4"),
            ["nivel1_texto"] = new("nivel1_texto", Tipo.Texto, "Nivel 1"),
            ["nivel2_texto"] = new("nivel2_texto", Tipo.Texto, "Nivel 2"),
            ["nivel3_texto"] = new("nivel3_texto", Tipo.Texto, "Nivel 3"),
            ["nivel4_texto"] = new("nivel4_texto", Tipo.Texto, "Nivel 4"),
            ["codigo_postal"] = new("codigo_postal", Tipo.Texto, "Código postal"),
            ["detalle"] = new("detalle", Tipo.Texto, "Dirección exacta (línea 1)"),
            ["detalle2"] = new("detalle2", Tipo.Texto, "Dirección exacta (línea 2)"),
            ["detalle3"] = new("detalle3", Tipo.Texto, "Dirección exacta (línea 3)"),
            ["es_principal"] = new("es_principal", Tipo.Bit, "Principal"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("cont", "cli.INTERLOCUTOR_CONTACTO", "id_contacto", "Contacto", new()
        {
            ["id_tipo_contacto"] = new("id_tipo_contacto", Tipo.Entero, "Tipo"),
            ["valor"] = new("valor", Tipo.Texto, "Dato"),
            ["es_principal"] = new("es_principal", Tipo.Bit, "Principal"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("nom", "cli.INTERLOCUTOR_NOMBRE_COMERCIAL", "id_nombre_comercial", "Nombre comercial", new()
        {
            ["nombre"] = new("nombre", Tipo.Texto, "Nombre comercial"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("act", "cli.INTERLOCUTOR_ACTIVIDAD", "id_actividad_interlocutor", "Actividad económica", new()
        {
            ["id_actividad"] = new("id_actividad", Tipo.Entero, "Actividad"),
            ["descripcion"] = new("descripcion", Tipo.Texto, "Descripción"),
            ["es_principal"] = new("es_principal", Tipo.Bit, "Principal"),
            ["fecha_inicio"] = new("fecha_inicio", Tipo.Fecha, "Inicio de la actividad"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("rf", "cli.INTERLOCUTOR_RESIDENCIA_FISCAL", "id_residencia_fiscal", "Residencia fiscal", new()
        {
            ["id_pais"] = new("id_pais", Tipo.Entero, "País"),
            ["identificador_tributario"] = new("identificador_tributario", Tipo.Texto, "Identificador tributario"),
            ["es_principal"] = new("es_principal", Tipo.Bit, "Principal"),
            ["fecha_desde"] = new("fecha_desde", Tipo.Fecha, "Residente desde"),
            ["fecha_hasta"] = new("fecha_hasta", Tipo.Fecha, "Residente hasta"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("ing", "cli.INTERLOCUTOR_INGRESO", "id_ingreso", "Ingreso", new()
        {
            ["id_tipo_ingreso"] = new("id_tipo_ingreso", Tipo.Entero, "Tipo de ingreso"),
            ["monto"] = new("monto", Tipo.Decimal, "Monto"),
            ["id_moneda"] = new("id_moneda", Tipo.Entero, "Moneda"),
            ["id_periodicidad"] = new("id_periodicidad", Tipo.Entero, "Periodicidad"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("orig", "cli.INTERLOCUTOR_ORIGEN", "id_origen_registro", "Origen de fondos o patrimonio", new()
        {
            ["clase"] = new("clase", Tipo.Texto, "Clase"),
            ["id_origen"] = new("id_origen", Tipo.Entero, "Origen"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("cx", "cli.INTERLOCUTOR_CUENTA_EXTERNA", "id_interlocutor_cuenta_externa", "Cuenta externa", new()
        {
            ["id_entidad"] = new("id_entidad", Tipo.Entero, "Entidad financiera"),
            ["numero_cuenta_externa"] = new("numero_cuenta_externa", Tipo.Texto, "Número de cuenta (IBAN)"),
            ["id_tipo_cuenta"] = new("id_tipo_cuenta", Tipo.Entero, "Tipo de cuenta"),
            ["id_moneda"] = new("id_moneda", Tipo.Entero, "Moneda"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("pt", "cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL", "id_perfil_transaccional", "Transacción esperada", new()
        {
            ["id_tipo_transaccion"] = new("id_tipo_transaccion", Tipo.Entero, "Tipo de transacción"),
            ["id_frecuencia"] = new("id_frecuencia", Tipo.Entero, "Frecuencia"),
            ["monto_minimo"] = new("monto_minimo", Tipo.Decimal, "Monto mínimo esperado (por periodo)"),
            ["monto_maximo"] = new("monto_maximo", Tipo.Decimal, "Monto máximo esperado (por periodo)"),
            ["id_moneda"] = new("id_moneda", Tipo.Entero, "Moneda"),
            ["id_pais_origen"] = new("id_pais_origen", Tipo.Entero, "País de origen"),
            ["id_pais_destino"] = new("id_pais_destino", Tipo.Entero, "País de destino"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
    ];

    /// <summary>Colecciones de KYC y cumplimiento (gestion CLI_KYC): no forman parte del alta ni de la modificacion de datos.</summary>
    private static readonly Coleccion[] ColeccionesCumplimiento =
    [
        new("kmr", "cli.INTERLOCUTOR_MOTIVO_RIESGO", "id_motivo_riesgo", "Motivo de riesgo", new()
        {
            ["id_motivo"] = new("id_motivo", Tipo.Entero, "Motivo"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("kex", "cli.INTERLOCUTOR_EXPEDIENTE", "id_expediente", "Referencia de expediente", new()
        {
            ["referencia"] = new("referencia", Tipo.Texto, "Referencia"),
            ["sistema"] = new("sistema", Tipo.Texto, "Sistema o gestor documental"),
            ["descripcion"] = new("descripcion", Tipo.Texto, "Descripción"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
        new("scr", "cli.INTERLOCUTOR_SCREENING", "id_screening", "Evaluación", new()
        {
            ["tipo"] = new("tipo", Tipo.Texto, "Tipo de evaluación"),
            ["id_resultado"] = new("id_resultado", Tipo.Entero, "Resultado"),
            ["fecha_screening"] = new("fecha_screening", Tipo.Fecha, "Fecha de la evaluación"),
            ["fuente"] = new("fuente", Tipo.Texto, "Fuente o lista consultada"),
            ["observacion"] = new("observacion", Tipo.Texto, "Observación"),
            ["estado"] = new("estado", Tipo.Texto, "Estado"),
        }),
    ];

    /// <summary>Colecciones del perfil financiero: solo se comparan si la solicitud trae ese bloque.</summary>
    private static bool EsDePerfilFinanciero(string prefijo) => prefijo is "rf" or "ing" or "orig" or "pt" or "cx";

    private static readonly Regex PatronCampoHijo = new(@"^(ident|dir|cont|nom|act|nac|rf|ing|orig|pt|cx|kmr|kex|scr)\[(\+?)(\d+)\]\.([a-z0-9_]+)$", RegexOptions.Compiled);

    private static Coleccion ColeccionPorPrefijo(string prefijo) => Colecciones.Concat(ColeccionesCumplimiento).First(c => c.Prefijo == prefijo);

    private static string ExpresionTexto(string columna, Tipo tipo) => tipo switch
    {
        Tipo.Fecha => $"CONVERT(nvarchar(10), {columna}, 23)",
        Tipo.Bit => $"CONVERT(nvarchar(1), {columna})",
        Tipo.Decimal => $"CONVERT(nvarchar(40), {columna})",
        _ => $"CONVERT(nvarchar(1000), {columna})"
    };

    private static object? ValorSql(Tipo tipo, string? texto) => tipo switch
    {
        Tipo.Entero => texto is null ? null : long.Parse(texto, CultureInfo.InvariantCulture),
        Tipo.Fecha => texto is null ? null : DateTime.ParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Tipo.Bit => texto == "1",
        Tipo.Decimal => texto is null ? null : decimal.Parse(texto, NumberStyles.Number, CultureInfo.InvariantCulture),
        _ => texto
    };

    private static string? Fecha(DateOnly? f) => f?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Bit(bool b) => b ? "1" : "0";
    private static string? V(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ----- Etiquetas y textos para mostrar el detalle de una gestion ------------------

    /// <summary>Texto legible del campo, p. ej. «Identificación #12 · Número» o «Dirección nueva #1 · Dirección exacta».</summary>
    public static string EtiquetaCampo(string campo)
    {
        if (CamposInterlocutor.TryGetValue(campo, out var def)) return def.Campo.Etiqueta;
        if (campo == "tipo_persona") return "Tipo de interlocutor";
        if (campo == BloqueoServicio.CampoAccion) return "Acción";
        if (campo == BloqueoServicio.CampoTipo) return "Tipo de bloqueo";
        if (campo == BloqueoServicio.CampoComentario) return "Comentario";
        if (campo == FallecimientoServicio.CampoFecha) return "Fecha de fallecimiento";
        if (campo == FallecimientoServicio.CampoComentario) return "Observación";
        if (campo == ConsentimientoServicio.CampoCanal) return "Canal";
        if (campo == ConsentimientoServicio.CampoComentario) return "Comentario del consentimiento";
        if (ConsentimientoServicio.EsCampoTipo(campo)) return "Consentimiento";
        if (DocumentoGestionServicio.EsCampo(campo)) return DocumentoGestionServicio.EtiquetaCampo(campo);
        if (campo == GrupoServicio.CampoTipoGrupo) return "Tipo de grupo";
        if (campo == GrupoServicio.CampoDescripcion) return "Descripción del grupo";
        if (campo == GrupoServicio.CampoFormacion) return "Fecha de formación del grupo";
        if (campo == "dup_revision") return "Revisión de posibles duplicados";
        if (campo == FusionServicio.CampoSobreviviente) return "Cliente que se conserva";
        if (campo == FusionServicio.CampoDuplicado) return "Cliente que se absorbe";
        if (campo == FusionServicio.CampoMotivo) return "Motivo de la fusión";
        if (campo == FusionServicio.CampoIncorpora) return "Incorporado por fusión";
        if (campo == FusionServicio.CampoRetira) return "Retirado por fusión";
        if (campo == "fusion") return "Fusión";
        if (campo == RelacionServicio.CampoMotivo) return "Motivo del cambio de relaciones";
        if (campo.StartsWith(RelacionServicio.PrefijoBaja, StringComparison.Ordinal)) return "Inactivar relación de interlocutor";
        var mm = RelacionServicio.PatronMod.Match(campo);
        if (mm.Success)
        {
            var et = mm.Groups[2].Value switch
            {
                "relacion" => "Relación", "porcentaje_participacion" => "% de participación", "porcentaje_voto" => "% de voto", "porcentaje_control" => "% de control",
                "es_principal" => "Principal", "funcion" => "Función", "fecha_inicio" => "Fecha de inicio", var o => o
            };
            return $"Modificar relación #{mm.Groups[1].Value} · {et}";
        }
        var mr = RelacionServicio.PatronNueva.Match(campo);
        if (mr.Success)
        {
            var et = mr.Groups[2].Value switch
            {
                "id_otro" => "Otro interlocutor", "id_tipo_relacion" => "Tipo de relación", "sentido" => "Sentido",
                "porcentaje_participacion" => "% de participación", "porcentaje_voto" => "% de voto", "porcentaje_control" => "% de control",
                "es_principal" => "Principal", "funcion" => "Función", "fecha_inicio" => "Fecha de inicio", var o => o
            };
            return $"Relación nueva #{mr.Groups[1].Value} · {et}";
        }
        if (campo == RolServicio.CampoRolAlta) return "Rol que asumirá";
        if (campo == EmpleadoServicio.CampoUnidad) return "Unidad del empleado";
        if (campo == EmpleadoServicio.CampoNumero) return "Número de empleado";
        if (campo == RolServicio.CampoMotivo) return "Motivo del cambio de roles";
        if (campo.StartsWith(RolServicio.PrefijoAgregar, StringComparison.Ordinal)) return "Agregar rol";
        if (campo.StartsWith(RolServicio.PrefijoQuitar, StringComparison.Ordinal)) return "Quitar rol";
        if (campo.StartsWith(FusionServicio.PrefijoDireccion, StringComparison.Ordinal)) return $"Dirección #{campo[FusionServicio.PrefijoDireccion.Length..^1]} del cliente absorbido";
        if (campo == ProspectoRepositorio.CampoVinculo) return "Prospecto de origen (N.º interno)";

        var m = PatronCampoHijo.Match(campo);
        if (!m.Success) return campo;

        var col = ColeccionPorPrefijo(m.Groups[1].Value);
        var etiquetaCampo = col.Campos.TryGetValue(m.Groups[4].Value, out var c) ? c.Etiqueta : m.Groups[4].Value;
        var quien = m.Groups[2].Value == "+" ? $"{col.Singular} nueva #{m.Groups[3].Value}" : $"{col.Singular} #{m.Groups[3].Value}";
        return $"{quien} · {etiquetaCampo}";
    }

    internal static string? TextoCampo(string campo, string? valor, Dictionary<long, string> items, Dictionary<long, string> paises, Dictionary<long, string> divisiones, Dictionary<long, string> unidades, Dictionary<long, string>? monedas = null)
    {
        if (valor is null) return null;

        if (campo == BloqueoServicio.CampoTipo)
            return long.TryParse(valor, out var idTipoBloq) && items.TryGetValue(idTipoBloq, out var nombreTipoBloq) ? nombreTipoBloq : valor;
        if (campo is BloqueoServicio.CampoAccion or BloqueoServicio.CampoComentario) return valor;
        if (campo == FallecimientoServicio.CampoFecha)
            return DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fFall) ? fFall.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : valor;
        if (campo == FallecimientoServicio.CampoComentario) return valor;
        if (campo == ConsentimientoServicio.CampoCanal)
            return long.TryParse(valor, out var idCanalCons) && items.TryGetValue(idCanalCons, out var nombreCanalCons) ? nombreCanalCons : valor;
        if (ConsentimientoServicio.EsCampoTipo(campo))
        {
            var idTipoCons = long.Parse(campo[ConsentimientoServicio.PrefijoTipo.Length..]);
            return (valor == ConsentimientoServicio.Otorgar ? "Otorgar" : "Revocar") + " · " + (items.TryGetValue(idTipoCons, out var nombreTipoCons) ? nombreTipoCons : idTipoCons.ToString());
        }
        if (ConsentimientoServicio.EsCampo(campo)) return valor;

        if (campo == EmpleadoServicio.CampoUnidad)
            return long.TryParse(valor, out var idUnidadEmp) && unidades.TryGetValue(idUnidadEmp, out var nombreUnidadEmp) ? nombreUnidadEmp : valor;
        if (campo == RolServicio.CampoRolAlta || campo.StartsWith(RolServicio.PrefijoAgregar, StringComparison.Ordinal) || campo.StartsWith(RolServicio.PrefijoQuitar, StringComparison.Ordinal))
            return long.TryParse(valor, out var idRolTxt) && items.TryGetValue(idRolTxt, out var nombreRol) ? nombreRol : valor;
        if (campo.StartsWith(RelacionServicio.PrefijoBaja, StringComparison.Ordinal) || (RelacionServicio.PatronNueva.IsMatch(campo) && campo.EndsWith(".id_otro", StringComparison.Ordinal)))
            return valor.Contains('|') ? valor[(valor.IndexOf('|') + 1)..] : valor;
        if (RelacionServicio.PatronMod.IsMatch(campo))
        {
            if (campo.EndsWith(".relacion", StringComparison.Ordinal)) return valor.Contains('|') ? valor[(valor.IndexOf('|') + 1)..] : valor;
            if (campo.EndsWith(".es_principal", StringComparison.Ordinal)) return valor == "1" ? "Sí" : "No";
            if (campo.Contains(".porcentaje_", StringComparison.Ordinal)) return valor + " %";
            return valor;
        }
        if (RelacionServicio.PatronNueva.IsMatch(campo))
        {
            if (campo.EndsWith(".sentido", StringComparison.Ordinal)) return valor == "Origen" ? "Este interlocutor es quien ejerce la relación" : "El otro interlocutor es quien ejerce la relación";
            if (campo.EndsWith(".es_principal", StringComparison.Ordinal)) return valor == "1" ? "Sí" : "No";
            if (campo.EndsWith(".id_tipo_relacion", StringComparison.Ordinal)) return long.TryParse(valor, out var idTr) && items.TryGetValue(idTr, out var ntr) ? ntr : valor;
            if (campo.Contains(".porcentaje_", StringComparison.Ordinal)) return valor + " %";
            return valor;
        }
        if (campo is FusionServicio.CampoSobreviviente or FusionServicio.CampoDuplicado)
            return valor.Contains('|') ? valor[(valor.IndexOf('|') + 1)..] : valor;
        if (campo.StartsWith(FusionServicio.PrefijoDireccion, StringComparison.Ordinal))
        {
            if (valor == "D") return "Descartar (no se traslada)";
            var partes = valor.Split('|');
            return partes.Length == 2 && long.TryParse(partes[1], out var tipoDir) && items.TryGetValue(tipoDir, out var nt)
                ? $"Trasladar como «{nt}»" : "Trasladar";
        }

        var nombreColumna = campo;
        var m = PatronCampoHijo.Match(campo);
        if (m.Success) nombreColumna = m.Groups[4].Value;
        else if (campo.StartsWith("pe_", StringComparison.Ordinal)) nombreColumna = campo[3..];
        else if (campo.StartsWith("kyc_", StringComparison.Ordinal)) nombreColumna = campo[4..];

        if (nombreColumna == "es_principal") return valor == "1" ? "Sí" : "No";
        if (campo == "tipo_persona") return valor == "Juridica" ? "Organización" : valor == "Fisica" ? "Persona" : valor == "Grupo" ? "Grupo" : valor;
        if (!long.TryParse(valor, out var id)) return valor;

        return nombreColumna switch
        {
            "nivel1_id" or "nivel2_id" or "nivel3_id" or "nivel4_id" => divisiones.TryGetValue(id, out var dv) ? dv : valor,
            "id_unidad" => unidades.TryGetValue(id, out var un) ? un : valor,
            "id_pais" => items.TryGetValue(id, out var pn) ? pn : valor,
            "id_pais_regulador" => paises.TryGetValue(id, out var p) ? p : valor,
            "id_tipo_identificacion" or "id_institucion_emisora" or "id_tipo_direccion" or "id_tipo_contacto" or "id_canal_origen"
                or "id_forma_juridica" or "id_pais_constitucion" or "id_estado_legal" or "id_actividad" or "id_tipo_grupo"
                or "id_pais_nacimiento" or "id_sexo" or "id_estado_civil"
                or "id_condicion_laboral" or "id_ocupacion" or "id_tipo_ingreso" or "id_periodicidad" or "id_origen"
                or "id_entidad" or "id_tipo_cuenta" or "id_tipo_transaccion" or "id_frecuencia" or "id_pais_origen" or "id_pais_destino"
                or "id_estado_kyc" or "id_nivel_dd" or "id_riesgo" or "id_segmento_cliente" or "id_segmento_pricing" or "id_segmento_riesgo" or "id_motivo" or "id_resultado"
                => items.TryGetValue(id, out var n) ? n : valor,
            "id_moneda" or "id_moneda_balance" => monedas is not null && monedas.TryGetValue(id, out var mn) ? mn : valor,
            _ => valor
        };
    }

    // ----- Solicitar la modificacion de un cliente -------------------------------------

    private record Cambio(string Campo, string? Anterior, string? Nuevo);

    public async Task<ResultadoOperacion> SolicitarModificacionInterlocutorAsync(
        ActorContext actor, long idInterlocutor, InterlocutorModificacionRequest req)
    {
        if (actor.EsIntegracion)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var actual = await conexion.QuerySingleOrDefaultAsync(
                """
                SELECT i.estado, i.tipo_persona, i.id_pais_regulador, i.id_unidad,
                       f.nombre1, f.nombre2, f.apellido1, f.apellido2, CONVERT(nvarchar(10), f.fecha_nacimiento, 23) AS fecha_nacimiento,
                       f.nombre_conocido, f.id_pais_nacimiento, f.id_sexo, f.id_estado_civil,
                       j.nombre_org1, j.id_forma_juridica, j.id_pais_constitucion, CONVERT(nvarchar(10), j.fecha_constitucion, 23) AS fecha_constitucion,
                       j.referencia_registral, j.id_estado_legal, CONVERT(nvarchar(10), j.fecha_disolucion, 23) AS fecha_disolucion
                FROM cli.INTERLOCUTOR i
                LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
                LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
                WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania AND i.tipo_persona IN ('Fisica', 'Juridica')
                """, new { idInterlocutor, actor.IdCompania }, tx);
            if (actual is null)
            {
                var esGrupo = await conexion.ExecuteScalarAsync<bool>(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania AND tipo_persona = 'Grupo') THEN 1 ELSE 0 END",
                    new { idInterlocutor, actor.IdCompania }, tx);
                return esGrupo
                    ? ResultadoOperacion.Fallo(409, "grupo_no_modificable", "Los datos de un grupo no se modifican por ahora; sus miembros se gestionan en la pestaña Relaciones.")
                    : ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el cliente (Persona u Organizacion) indicado.");
            }
            var esOrg = (string)actual.tipo_persona == "Juridica";
            if (esOrg && req.Organizacion is null)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", "Faltan los datos de la organizacion.");

            if ((string)actual.estado != "Activo")
                return ResultadoOperacion.Fallo(409, "cliente_no_activo", $"El cliente esta '{(string)actual.estado}' y no se puede modificar.");

            var llave = idInterlocutor.ToString(CultureInfo.InvariantCulture);

            var pendiente = await conexion.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id_gestion FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
                new { e = EntidadInterlocutor, llave, p = EstadoPendiente }, tx);
            if (pendiente is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente",
                    $"El cliente ya tiene la gestion #{pendiente} pendiente de aprobacion; debe resolverse antes de solicitar otro cambio.");
            var fusionPend = await FusionServicio.GestionPendienteAsync(conexion, tx, actor.IdCompania, idInterlocutor);
            if (fusionPend is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"El cliente esta en la gestion {fusionPend} pendiente de aprobacion; debe resolverse antes de solicitar otro cambio.");

            // Misma validacion que el alta.
            var cat = await InterlocutorRepositorio.CargarCatalogosAsync(conexion, tx);
            // Campos obligatorios: la union de lo que piden los roles activos del cliente (Administracion › Requisitos por rol).
            var reglas = await RolServicio.ReglasDeClienteAsync(conexion, tx, actor.IdCompania, idInterlocutor);
            var errores = esOrg
                ? InterlocutorValidador.ValidarOrganizacion(req.Organizacion, req.Identificaciones, req.Direcciones, req.Contactos, cat, reglas)
                : InterlocutorValidador.Validar(
                    req.Nombre1, req.Nombre2, req.Apellido1, req.Apellido2, req.FechaNacimiento, req.Identificaciones, req.Direcciones, req.Contactos, cat, reglas, req.Identidad);
            if (req.PerfilFinanciero is not null)
                errores.AddRange(await PerfilFinancieroServicio.ValidarAsync(conexion, tx, actor.IdCompania, esOrg ? "Juridica" : "Fisica", req.PerfilFinanciero, reglas));
            var errorUnidad = await InterlocutorRepositorio.ValidarUnidadAsync(conexion, tx, actor.IdCompania, req.IdUnidad, (long?)actual.id_unidad);
            if (errorUnidad is not null) errores.Add(errorUnidad);
            var paisOk = await conexion.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { p = req.IdPaisRegulador }, tx);
            if (!paisOk && req.IdPaisRegulador != (long)actual.id_pais_regulador) errores.Add("El pais regulador no existe o esta inactivo.");
            var (erroresGeo, direccionesNorm) = await DireccionGeografia.ValidarAsync(conexion, tx, req.Direcciones, reglas.Pide(ReglasCampos.DirNiveles), reglas.Pide(ReglasCampos.DirCodigoPostal));
            errores.AddRange(erroresGeo);
            if (errores.Count > 0)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

            var cambios = new List<Cambio>();

            // --- Campos del cliente (1:1) ---
            var deseado = new Dictionary<string, string?>
            {
                ["id_pais_regulador"] = req.IdPaisRegulador.ToString(CultureInfo.InvariantCulture),
                ["id_unidad"] = req.IdUnidad?.ToString(CultureInfo.InvariantCulture),
            };
            var vigente = new Dictionary<string, string?>
            {
                ["id_pais_regulador"] = ((long)actual.id_pais_regulador).ToString(CultureInfo.InvariantCulture),
                ["id_unidad"] = ((long?)actual.id_unidad)?.ToString(CultureInfo.InvariantCulture),
            };
            if (esOrg)
            {
                var o = req.Organizacion!;
                deseado["razon_social"] = V(o.RazonSocial);
                deseado["id_forma_juridica"] = o.IdFormaJuridica.ToString(CultureInfo.InvariantCulture);
                deseado["id_pais_constitucion"] = o.IdPaisConstitucion.ToString(CultureInfo.InvariantCulture);
                deseado["fecha_constitucion"] = Fecha(o.FechaConstitucion);
                deseado["referencia_registral"] = V(o.ReferenciaRegistral);
                deseado["id_estado_legal"] = o.IdEstadoLegal.ToString(CultureInfo.InvariantCulture);
                deseado["fecha_disolucion"] = Fecha(o.FechaDisolucion);
                vigente["razon_social"] = V((string?)actual.nombre_org1);
                vigente["id_forma_juridica"] = ((long?)actual.id_forma_juridica)?.ToString(CultureInfo.InvariantCulture);
                vigente["id_pais_constitucion"] = ((long?)actual.id_pais_constitucion)?.ToString(CultureInfo.InvariantCulture);
                vigente["fecha_constitucion"] = (string?)actual.fecha_constitucion;
                vigente["referencia_registral"] = V((string?)actual.referencia_registral);
                vigente["id_estado_legal"] = ((long?)actual.id_estado_legal)?.ToString(CultureInfo.InvariantCulture);
                vigente["fecha_disolucion"] = (string?)actual.fecha_disolucion;
            }
            else
            {
                deseado["nombre1"] = V(req.Nombre1);
                deseado["nombre2"] = V(req.Nombre2);
                deseado["apellido1"] = V(req.Apellido1);
                deseado["apellido2"] = V(req.Apellido2);
                deseado["fecha_nacimiento"] = req.FechaNacimiento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                vigente["nombre1"] = V((string?)actual.nombre1);
                vigente["nombre2"] = V((string?)actual.nombre2);
                vigente["apellido1"] = V((string?)actual.apellido1);
                vigente["apellido2"] = V((string?)actual.apellido2);
                vigente["fecha_nacimiento"] = (string?)actual.fecha_nacimiento;
                // Identidad: si el bloque no viene, no se toca (ni se compara).
                if (req.Identidad is { } idt)
                {
                    deseado["nombre_conocido"] = V(idt.NombreConocido);
                    deseado["id_pais_nacimiento"] = idt.IdPaisNacimiento?.ToString(CultureInfo.InvariantCulture);
                    deseado["id_sexo"] = idt.IdSexo?.ToString(CultureInfo.InvariantCulture);
                    deseado["id_estado_civil"] = idt.IdEstadoCivil?.ToString(CultureInfo.InvariantCulture);
                    vigente["nombre_conocido"] = V((string?)actual.nombre_conocido);
                    vigente["id_pais_nacimiento"] = ((long?)actual.id_pais_nacimiento)?.ToString(CultureInfo.InvariantCulture);
                    vigente["id_sexo"] = ((long?)actual.id_sexo)?.ToString(CultureInfo.InvariantCulture);
                    vigente["id_estado_civil"] = ((long?)actual.id_estado_civil)?.ToString(CultureInfo.InvariantCulture);
                }
            }
            // Perfil economico (1:1): solo si el bloque viene en la solicitud.
            if (req.PerfilFinanciero is { } pfm)
            {
                var vigentePe = await LeerPerfilEconomicoAsync(conexion, tx, idInterlocutor);
                foreach (var (k, v) in PerfilEconomicoDeseado(pfm.Economico, esOrg))
                {
                    deseado[k] = v;
                    vigente[k] = vigentePe.GetValueOrDefault(k);
                }
            }
            foreach (var kv in deseado)
                if (vigente[kv.Key] != kv.Value) cambios.Add(new Cambio(kv.Key, vigente[kv.Key], kv.Value));

            // --- Colecciones 1:n ---
            var pedidos = ConstruirPedidos(req.Identificaciones, direccionesNorm, req.Contactos, cat, esOrg ? req.Organizacion : null, esOrg ? null : req.Identidad, req.PerfilFinanciero);

            foreach (var col in Colecciones)
            {
                if (EsDePerfilFinanciero(col.Prefijo) && req.PerfilFinanciero is null) continue;
                // Nacionalidades: solo se comparan si el bloque de identidad viene en la solicitud (una persona).
                if (col.Prefijo == "nac" && (esOrg || req.Identidad is null)) continue;
                var vigentes = await LeerVigentesAsync(conexion, tx, col, idInterlocutor);
                var pedido = pedidos[col.Prefijo];

                foreach (var p in pedido.Where(x => x.Id is not null))
                    if (!vigentes.ContainsKey(p.Id!.Value))
                        return ResultadoOperacion.Fallo(400, "registro_inexistente",
                            $"{col.Singular} #{p.Id} no existe o ya fue dada de baja; recargue la pantalla.");

                foreach (var (id, fila) in vigentes)
                {
                    var enPedido = pedido.FirstOrDefault(x => x.Id == id);
                    if (enPedido.Campos is null)
                    {
                        cambios.Add(new Cambio($"{col.Prefijo}[{id}].estado", "Activo", "Inactivo"));
                        continue;
                    }
                    foreach (var (nombre, nuevo) in enPedido.Campos)
                    {
                        var anterior = V(fila.GetValueOrDefault(nombre));
                        if (anterior != nuevo)
                            cambios.Add(new Cambio($"{col.Prefijo}[{id}].{nombre}", anterior, nuevo));
                    }
                }

                var n = 0;
                foreach (var nuevoItem in pedido.Where(x => x.Id is null))
                {
                    n++;
                    foreach (var (nombre, valor) in nuevoItem.Campos)
                    {
                        if (valor is null) continue;
                        if (nombre == "es_principal" && valor == "0") continue;
                        cambios.Add(new Cambio($"{col.Prefijo}[+{n}].{nombre}", null, valor));
                    }
                }
            }

            if (cambios.Count == 0)
                return ResultadoOperacion.Fallo(400, "sin_cambios", "No hay diferencias con los datos actuales.");

            // Identificacion duplicada en otro cliente: aviso temprano (la BD igual lo impide al aplicar).
            foreach (var i in req.Identificaciones)
            {
                var dup = await InterlocutorRepositorio.BuscarDuplicadoAsync(
                    conexion, tx, actor.IdCompania, i.IdTipoIdentificacion, i.Numero, idInterlocutor);
                if (dup is not null)
                    return ResultadoOperacion.Fallo(409, "identificacion_duplicada",
                        $"La identificacion {i.Numero.Trim()} ya pertenece al cliente N.º {dup}.");
            }

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'",
                new { c = TipoModificacionCliente }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado",
                    $"Falta el tipo de gestion {TipoModificacionCliente}; correr edicion_clientes_gestion.sql.");

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio,
                                         llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, @comentario, @creadoPor)
                """,
                new
                {
                    numeroGestion,
                    actor.IdCompania, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario,
                    entidad = EntidadInterlocutor, llave,
                    comentario = string.IsNullOrWhiteSpace(req.Comentario) ? null : req.Comentario.Trim(),
                    creadoPor = actor.NombreDescriptivo
                }, tx);

            foreach (var c in cambios)
            {
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, @anterior, @nuevo)",
                    new { idGestion, campo = c.Campo, anterior = c.Anterior, nuevo = c.Nuevo }, tx);
            }

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value);

            var errorDevuelta = await CerrarDevueltaAsync(conexion, tx, actor, req.IdGestionDevuelta, idGestion, EntidadInterlocutor, llave);
            if (errorDevuelta is not null) return errorDevuelta;

            if (inicio.AplicarYa)
            {
                var errorAuto = await AplicarAutomaticaAsync(conexion, tx, actor, idGestion, inicio.NombreRegla);
                if (errorAuto is not null) { tx.Rollback(); return errorAuto; }
            }

            tx.Commit();
            return ResultadoOperacion.Ok(idGestion) with { Numero = numeroGestion };
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Indice unico de gestion pendiente por registro: otra solicitud se adelanto.
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "El cliente ya tiene una gestion pendiente de aprobacion.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Estado deseado del perfil economico como pares campo/valor (persona: laboral y gastos; organizacion: patrimonio). Null = vacio.</summary>
    internal static Dictionary<string, string?> PerfilEconomicoDeseado(PerfilEconomicoDto? e, bool esOrg)
    {
        var r = new Dictionary<string, string?>();
        if (!esOrg)
        {
            r["pe_id_condicion_laboral"] = e?.IdCondicionLaboral?.ToString(CultureInfo.InvariantCulture);
            r["pe_id_ocupacion"] = e?.IdOcupacion?.ToString(CultureInfo.InvariantCulture);
            r["pe_empleador"] = V(e?.Empleador);
            r["pe_gastos_estimados"] = PerfilFinancieroServicio.Texto(e?.GastosEstimados);
        }
        else r["pe_patrimonio_declarado"] = PerfilFinancieroServicio.Texto(e?.PatrimonioDeclarado);
        r["pe_activos_declarados"] = PerfilFinancieroServicio.Texto(e?.ActivosDeclarados);
        r["pe_pasivos_declarados"] = PerfilFinancieroServicio.Texto(e?.PasivosDeclarados);
        r["pe_id_moneda_balance"] = e?.IdMonedaBalance?.ToString(CultureInfo.InvariantCulture);
        r["pe_fecha_corte"] = Fecha(e?.FechaCorte);
        return r;
    }

    private static Task<Dictionary<string, string?>> LeerPerfilEconomicoAsync(SqlConnection conexion, SqlTransaction tx, long idInterlocutor)
        => LeerUnoAUnoAsync(conexion, tx, idInterlocutor, TablaPerfilEconomico);

    /// <summary>Valores vigentes (texto normalizado) de los campos de una tabla 1:1 (clave: nombre del campo en el detalle de la gestion).</summary>
    private static async Task<Dictionary<string, string?>> LeerUnoAUnoAsync(SqlConnection conexion, SqlTransaction tx, long idInterlocutor, string tabla)
    {
        var defs = CamposInterlocutor.Where(k => k.Value.Tabla == tabla).ToList();
        var columnas = string.Join(", ", defs.Select((k, i) => $"{ExpresionTexto(k.Value.Campo.Columna, k.Value.Campo.Tipo)} AS c{i}"));
        var fila = (IDictionary<string, object>?)await conexion.QuerySingleOrDefaultAsync(
            $"SELECT {columnas} FROM {tabla} WHERE id_interlocutor = @id", new { id = idInterlocutor }, tx);
        return defs.Select((k, i) => (k.Key, Valor: fila?[$"c{i}"] as string)).ToDictionary(x => x.Key, x => x.Valor);
    }

    /// <summary>Registros activos de una coleccion, con cada campo como texto normalizado (clave: id del registro).</summary>
    private static async Task<Dictionary<long, Dictionary<string, string?>>> LeerVigentesAsync(
        SqlConnection conexion, SqlTransaction tx, Coleccion col, long idInterlocutor)
    {
        var columnas = string.Join(", ", col.Campos.Values.Select(c => $"{ExpresionTexto(c.Columna, c.Tipo)} AS {c.Columna}"));
        var filas = await conexion.QueryAsync(
            $"SELECT {col.Pk} AS __id, {columnas} FROM {col.Tabla} WHERE id_interlocutor = @id AND estado = 'Activo'",
            new { id = idInterlocutor }, tx);

        var resultado = new Dictionary<long, Dictionary<string, string?>>();
        foreach (IDictionary<string, object> fila in filas)
        {
            var id = Convert.ToInt64(fila["__id"], CultureInfo.InvariantCulture);
            resultado[id] = col.Campos.Keys.ToDictionary(k => k, k => fila[k] as string);
        }
        return resultado;
    }

    // ----- Aplicar (al aprobar) ----------------------------------------------------------

    private record Detalle(string Nombre, string? Anterior, string? Nuevo);

    // Aplica los campos de la gestion sobre el cliente y sus colecciones, y deja
    // una linea en aud.LOG_AUDITORIA por campo. Si el dato vigente ya no
    // coincide con el "valor anterior" de la solicitud (alguien lo cambio por
    // otra via), no aplica: evita pisar un cambio mas reciente.
    //
    // Orden (para respetar los indices de "un solo principal"):
    //   1) campos del cliente
    //   2) principal -> No (existentes)
    //   3) bajas (estado -> Inactivo)
    //   4) demas cambios de registros existentes
    //   5) altas de registros nuevos
    //   6) principal -> Si (existentes y nuevos)
    private static async Task<ResultadoOperacion?> AplicarCambiosInterlocutorAsync(
        SqlConnection conexion, SqlTransaction tx, ActorContext actor, long idGestion, long idInterlocutor, string solicitadoPor)
    {
        var detalle = (await conexion.QueryAsync(
            "SELECT nombre_campo, valor_anterior, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion ORDER BY id_gestion_detalle",
            new { idGestion }, tx)).Select(d => new Detalle((string)d.nombre_campo, (string?)d.valor_anterior, (string?)d.valor_nuevo)).ToList();

        var padre = new List<Detalle>();
        var existentes = new List<(Coleccion Col, long Id, string Campo, Detalle D)>();
        var nuevos = new Dictionary<(string Prefijo, string Numero), List<(string Campo, string? Valor, Detalle D)>>();

        foreach (var d in detalle)
        {
            if (CamposInterlocutor.ContainsKey(d.Nombre)) { padre.Add(d); continue; }

            var m = PatronCampoHijo.Match(d.Nombre);
            if (!m.Success)
                return ResultadoOperacion.Fallo(500, "campo_no_soportado", $"El campo '{d.Nombre}' no es editable por gestion.");

            var col = ColeccionPorPrefijo(m.Groups[1].Value);
            var campo = m.Groups[4].Value;
            if (!col.Campos.ContainsKey(campo))
                return ResultadoOperacion.Fallo(500, "campo_no_soportado", $"El campo '{d.Nombre}' no es editable por gestion.");

            if (m.Groups[2].Value == "+")
            {
                var llave = (col.Prefijo, m.Groups[3].Value);
                if (!nuevos.TryGetValue(llave, out var lista)) nuevos[llave] = lista = [];
                lista.Add((campo, d.Nuevo, d));
            }
            else
            {
                existentes.Add((col, long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), campo, d));
            }
        }

        async Task Auditar(Detalle d) => await conexion.ExecuteAsync(
            """
            INSERT INTO aud.LOG_AUDITORIA (id_compania, id_usuario, entidad_negocio, llave_registro, accion,
                                           nombre_campo, valor_anterior, valor_nuevo, id_gestion)
            VALUES (@idCompania, @idUsuario, @entidad, @llave, 'Cambio', @campo, @anterior, @nuevo, @idGestion)
            """,
            new
            {
                actor.IdCompania, idUsuario = actor.IdUsuario, entidad = EntidadInterlocutor,
                llave = idInterlocutor.ToString(CultureInfo.InvariantCulture), campo = d.Nombre,
                anterior = d.Anterior, nuevo = d.Nuevo, idGestion
            }, tx);

        // 1) Campos del cliente
        foreach (var d in padre)
        {
            var (tabla, def) = CamposInterlocutor[d.Nombre];
            if (EsTablaUnoAUnoOpcional(tabla))
                await conexion.ExecuteAsync(
                    $"IF NOT EXISTS (SELECT 1 FROM {tabla} WHERE id_interlocutor = @id) INSERT INTO {tabla} (id_interlocutor, creado_por) VALUES (@id, @por)",
                    new { id = idInterlocutor, por = solicitadoPor }, tx);
            var vigente = await conexion.ExecuteScalarAsync<string?>(
                $"SELECT {ExpresionTexto(def.Columna, def.Tipo)} FROM {tabla} WHERE id_interlocutor = @id",
                new { id = idInterlocutor }, tx);
            if (Norm(vigente) != Norm(d.Anterior))
                return ResultadoOperacion.Fallo(409, "registro_cambio",
                    $"El campo '{def.Etiqueta}' cambio desde que se solicito la gestion; rechacela y solicite el cambio de nuevo.");

            await conexion.ExecuteAsync(
                $"UPDATE {tabla} SET {def.Columna} = @valor WHERE id_interlocutor = @id",
                new { valor = ValorSql(def.Tipo, d.Nuevo), id = idInterlocutor }, tx);
            await Auditar(d);
        }

        foreach (var tablaUnoAUno in padre.Select(x => CamposInterlocutor[x.Nombre].Tabla).Where(EsTablaUnoAUnoOpcional).Distinct())
            await conexion.ExecuteAsync($"UPDATE {tablaUnoAUno} SET modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @id",
                new { por = solicitadoPor, id = idInterlocutor }, tx);

        // 1b) Grupo: espejo del nombre y la fecha en INTERLOCUTOR_JURIDICA/GRUPO, y disolucion (cierra los miembros).
        if (padre.Count > 0 && await conexion.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR WHERE id_interlocutor = @id AND tipo_persona = 'Grupo') THEN 1 ELSE 0 END",
                new { id = idInterlocutor }, tx))
        {
            foreach (var d in padre)
            {
                if (d.Nombre == "razon_social")
                    await conexion.ExecuteAsync("UPDATE cli.INTERLOCUTOR_GRUPO SET nombre_grupo1 = @v WHERE id_interlocutor = @id", new { v = d.Nuevo, id = idInterlocutor }, tx);
                else if (d.Nombre == GrupoServicio.CampoFormacion)
                    await conexion.ExecuteAsync("UPDATE cli.INTERLOCUTOR_JURIDICA SET fecha_constitucion = @v WHERE id_interlocutor = @id",
                        new { v = ValorSql(Tipo.Fecha, d.Nuevo), id = idInterlocutor }, tx);
                else if (d.Nombre == "estado" && d.Nuevo == GrupoServicio.EstadoDisuelto)
                {
                    var miembros = (await conexion.QueryAsync(
                        """
                        SELECT r.id_interlocutor_relacion AS id, r.id_interlocutor_origen AS miembro
                        FROM cli.INTERLOCUTOR_RELACION r
                        JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_relacion AND it.codigo = N'MIEMBRO_GRUPO'
                        WHERE r.id_interlocutor_destino = @id AND r.fecha_fin IS NULL
                        """, new { id = idInterlocutor }, tx)).ToList();
                    foreach (var m in miembros)
                    {
                        await conexion.ExecuteAsync("UPDATE cli.INTERLOCUTOR_RELACION SET fecha_fin = SYSUTCDATETIME() WHERE id_interlocutor_relacion = @id", new { id = (long)m.id }, tx);
                        await Auditoria.CambioAsync(conexion, tx, actor, EntidadInterlocutor, (long)m.miembro, "Membresía de grupo", "Vigente", "Cerrada por disolución del grupo", idGestion);
                    }
                    await Auditoria.CambioAsync(conexion, tx, actor, EntidadInterlocutor, idInterlocutor, "Miembros", $"{miembros.Count} vigentes", "Todos cerrados por la disolución", idGestion);
                }
            }
        }

        async Task<ResultadoOperacion?> ActualizarExistente((Coleccion Col, long Id, string Campo, Detalle D) e)
        {
            var def = e.Col.Campos[e.Campo];
            var vigente = await conexion.ExecuteScalarAsync<string?>(
                $"SELECT {ExpresionTexto(def.Columna, def.Tipo)} FROM {e.Col.Tabla} WHERE {e.Col.Pk} = @id AND id_interlocutor = @interlocutor",
                new { id = e.Id, interlocutor = idInterlocutor }, tx);
            if (Norm(vigente) != Norm(e.D.Anterior))
                return ResultadoOperacion.Fallo(409, "registro_cambio",
                    $"'{EtiquetaCampo(e.D.Nombre)}' cambio desde que se solicito la gestion; rechacela y solicite el cambio de nuevo.");

            await conexion.ExecuteAsync(
                $"UPDATE {e.Col.Tabla} SET {def.Columna} = @valor, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE {e.Col.Pk} = @id AND id_interlocutor = @interlocutor",
                new { valor = ValorSql(def.Tipo, e.D.Nuevo), por = solicitadoPor, id = e.Id, interlocutor = idInterlocutor }, tx);
            await Auditar(e.D);
            return null;
        }

        // 2) principal -> No
        foreach (var e in existentes.Where(x => x.Campo == "es_principal" && x.D.Nuevo == "0"))
            if (await ActualizarExistente(e) is { } err) return err;

        // 3) bajas
        foreach (var e in existentes.Where(x => x.Campo == "estado"))
            if (await ActualizarExistente(e) is { } err) return err;

        // 4) demas cambios
        foreach (var e in existentes.Where(x => x.Campo != "estado" && x.Campo != "es_principal"))
            if (await ActualizarExistente(e) is { } err) return err;

        // 5) altas (con principal = No; se marca en el paso 6)
        var principalNuevos = new List<(Coleccion Col, long IdNuevo)>();
        AuditoriaLegible.Catalogos? catalogos = null;
        foreach (var ((prefijo, _), campos) in nuevos)
        {
            var col = ColeccionPorPrefijo(prefijo);
            var parametros = new DynamicParameters();
            parametros.Add("idInterlocutor", idInterlocutor);
            parametros.Add("por", solicitadoPor);

            var columnas = new List<string> { "id_interlocutor" };
            var valores = new List<string> { "@idInterlocutor" };
            if (col.Prefijo == "ident")
            {
                columnas.Add("id_compania");
                valores.Add("@idCompania");
                parametros.Add("idCompania", actor.IdCompania);
            }

            var i = 0;
            var quiereSerPrincipal = false;
            foreach (var (campo, valor, _) in campos)
            {
                if (campo == "es_principal") { quiereSerPrincipal = valor == "1"; continue; }
                var def = col.Campos[campo];
                columnas.Add(def.Columna);
                valores.Add($"@p{i}");
                parametros.Add($"p{i}", ValorSql(def.Tipo, valor));
                i++;
            }
            columnas.Add("creado_por");
            valores.Add("@por");

            var idNuevo = await conexion.QuerySingleAsync<long>(
                $"INSERT INTO {col.Tabla} ({string.Join(", ", columnas)}) OUTPUT INSERTED.{col.Pk} VALUES ({string.Join(", ", valores)})",
                parametros, tx);
            if (quiereSerPrincipal) principalNuevos.Add((col, idNuevo));

            // Un registro nuevo (direccion, identificacion, contacto) es un "Alta": una sola linea con el resumen legible.
            catalogos ??= await AuditoriaLegible.CargarAsync(conexion, tx);
            var resumen = string.Join(" · ", campos.Where(x => x.Valor is not null).Select(x =>
                $"{col.Campos[x.Campo].Etiqueta}: {TextoCampo(x.Campo, x.Valor, catalogos.Items, catalogos.Paises, catalogos.Divisiones, catalogos.Unidades, catalogos.Monedas)}"));
            await conexion.ExecuteAsync(
                """
                INSERT INTO aud.LOG_AUDITORIA (id_compania, id_usuario, entidad_negocio, llave_registro, accion,
                                               nombre_campo, valor_anterior, valor_nuevo, id_gestion)
                VALUES (@idCompania, @idUsuario, @entidad, @llave, 'Alta', @campo, NULL, @nuevo, @idGestion)
                """,
                new
                {
                    actor.IdCompania, idUsuario = actor.IdUsuario, entidad = EntidadInterlocutor,
                    llave = idInterlocutor.ToString(CultureInfo.InvariantCulture), campo = $"{col.Singular} #{idNuevo}",
                    nuevo = resumen, idGestion
                }, tx);
        }

        // 6) principal -> Si
        foreach (var e in existentes.Where(x => x.Campo == "es_principal" && x.D.Nuevo == "1"))
            if (await ActualizarExistente(e) is { } err) return err;
        foreach (var (col, idNuevo) in principalNuevos)
            await conexion.ExecuteAsync($"UPDATE {col.Tabla} SET es_principal = 1 WHERE {col.Pk} = @id", new { id = idNuevo }, tx);

        await conexion.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR SET modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @id",
            new { por = solicitadoPor, id = idInterlocutor }, tx);

        return null;
    }
}
