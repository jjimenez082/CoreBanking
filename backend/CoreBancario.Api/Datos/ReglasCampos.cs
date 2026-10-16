namespace CoreBancario.Api.Datos;

/// <summary>Un campo cuya obligatoriedad se parametriza por rol (Administracion › Requisitos por rol).</summary>
/// <param name="Aplica">Tipo de interlocutor al que aplica: Fisica, Juridica o Ambos.</param>
public sealed record CampoRequisito(string Codigo, string Seccion, string Etiqueta, string Aplica = "Ambos");

/// <summary>
/// Que campos son obligatorios para una persona, segun sus roles. Se parametriza «por exclusion»: si el campo esta marcado para el rol es
/// obligatorio, si no es opcional. Siempre obligatorios (no parametrizables): primer nombre, primer apellido, tipo y numero de identificacion,
/// al menos una identificacion principal y la linea 1 de cualquier direccion que se registre. Una persona con varios roles debe cumplir
/// la UNION de lo que piden todos sus roles activos.
/// </summary>
public sealed class ReglasCampos
{
    public const string Nombre2 = "PERSONA_NOMBRE2";
    public const string Apellido2 = "PERSONA_APELLIDO2";
    public const string FechaNacimiento = "PERSONA_FECHA_NACIMIENTO";
    public const string NombreConocido = "PERSONA_NOMBRE_CONOCIDO";
    public const string PaisNacimiento = "PERSONA_PAIS_NACIMIENTO";
    public const string Nacionalidad = "PERSONA_NACIONALIDAD";   // al menos una nacionalidad
    public const string Sexo = "PERSONA_SEXO";
    public const string EstadoCivil = "PERSONA_ESTADO_CIVIL";
    public const string IdentInstitucion = "IDENT_INSTITUCION";
    public const string IdentFechaEmision = "IDENT_FECHA_EMISION";
    public const string IdentFechaVencimiento = "IDENT_FECHA_VENCIMIENTO";
    public const string Direccion = "DIRECCION";                 // al menos una direccion
    public const string DirNiveles = "DIR_NIVELES";              // aplica los niveles obligatorios que define el pais (provincia, canton...)
    public const string DirCodigoPostal = "DIR_CODIGO_POSTAL";   // aplica la obligatoriedad del codigo postal que define el pais
    public const string DirLinea2 = "DIR_LINEA2";
    public const string DirLinea3 = "DIR_LINEA3";
    public const string Correo = "CORREO";                       // al menos un correo
    public const string Telefono = "TELEFONO";                   // al menos un telefono
    // Solo organizaciones (persona juridica)
    public const string OrgNombreComercial = "ORG_NOMBRE_COMERCIAL";
    public const string OrgFechaConstitucion = "ORG_FECHA_CONSTITUCION";
    public const string OrgReferenciaRegistral = "ORG_REFERENCIA_REGISTRAL";
    public const string OrgActividad = "ORG_ACTIVIDAD";          // al menos una actividad economica
    // Perfil financiero (persona y organizacion)
    public const string RfResidencia = "RF_RESIDENCIA";                  // al menos una residencia fiscal
    public const string RfIdentTributario = "RF_IDENT_TRIBUTARIO";       // identificador tributario en cada residencia fiscal
    public const string PfCondicionLaboral = "PF_CONDICION_LABORAL";     // persona
    public const string PfOcupacion = "PF_OCUPACION";                    // persona
    public const string PfEmpleador = "PF_EMPLEADOR";                    // persona
    public const string PfIngreso = "PF_INGRESO";                        // al menos un ingreso
    public const string PfGastos = "PF_GASTOS";                          // persona
    public const string PfActivos = "PF_ACTIVOS";
    public const string PfPasivos = "PF_PASIVOS";
    public const string PfPatrimonio = "PF_PATRIMONIO";                  // organizacion
    public const string PfOrigenFondos = "PF_ORIGEN_FONDOS";             // al menos un origen de fondos
    public const string PfOrigenPatrimonio = "PF_ORIGEN_PATRIMONIO";     // al menos un origen del patrimonio
    public const string CxCuentaExterna = "CX_CUENTA_EXTERNA";           // al menos una cuenta externa (IBAN)
    public const string PtPerfil = "PT_PERFIL";                          // al menos una transaccion esperada
    // Relaciones requeridas de una organizacion: se verifican al asumir el rol sobre un cliente ya registrado (en el alta aun no existen)
    public const string RelRepresentanteLegal = "REL_REPRESENTANTE_LEGAL";
    public const string RelBeneficiarioFinal = "REL_BENEFICIARIO_FINAL";
    // Cumplimiento: se verifican al asumir un rol sobre un cliente ya registrado (el KYC se completa despues del alta, por gestion CLI_KYC)
    public const string KycEstadoCompleto = "KYC_ESTADO_COMPLETO";       // estado COMPLETO y revision vigente
    public const string KycPepEvaluado = "KYC_PEP_EVALUADO";             // evaluacion PEP vigente y resuelta
    public const string KycSancionesEvaluado = "KYC_SANCIONES_EVALUADO"; // evaluacion de sanciones vigente, resuelta y sin coincidencia confirmada

    public static readonly IReadOnlyList<CampoRequisito> Catalogo =
    [
        new(Nombre2, "Datos personales", "Segundo nombre", "Fisica"),
        new(Apellido2, "Datos personales", "Segundo apellido", "Fisica"),
        new(FechaNacimiento, "Datos personales", "Fecha de nacimiento", "Fisica"),
        new(NombreConocido, "Datos personales", "Nombre conocido", "Fisica"),
        new(PaisNacimiento, "Datos personales", "País de nacimiento", "Fisica"),
        new(Nacionalidad, "Datos personales", "Al menos una nacionalidad", "Fisica"),
        new(Sexo, "Datos personales", "Sexo", "Fisica"),
        new(EstadoCivil, "Datos personales", "Estado civil", "Fisica"),
        new(OrgNombreComercial, "Datos de la organización", "Al menos un nombre comercial", "Juridica"),
        new(OrgFechaConstitucion, "Datos de la organización", "Fecha de constitución", "Juridica"),
        new(OrgReferenciaRegistral, "Datos de la organización", "Referencia registral", "Juridica"),
        new(OrgActividad, "Actividad económica", "Al menos una actividad económica", "Juridica"),
        new(RfResidencia, "Residencia fiscal", "Al menos una residencia fiscal"),
        new(RfIdentTributario, "Residencia fiscal", "Identificador tributario de cada residencia"),
        new(PfCondicionLaboral, "Perfil económico", "Condición laboral", "Fisica"),
        new(PfOcupacion, "Perfil económico", "Ocupación o profesión", "Fisica"),
        new(PfEmpleador, "Perfil económico", "Empleador o negocio", "Fisica"),
        new(PfGastos, "Perfil económico", "Gastos estimados", "Fisica"),
        new(PfPatrimonio, "Perfil económico", "Patrimonio declarado", "Juridica"),
        new(PfActivos, "Perfil económico", "Activos declarados"),
        new(PfPasivos, "Perfil económico", "Pasivos declarados"),
        new(PfIngreso, "Ingresos y origen de recursos", "Al menos un ingreso"),
        new(PfOrigenFondos, "Ingresos y origen de recursos", "Origen de fondos"),
        new(PfOrigenPatrimonio, "Ingresos y origen de recursos", "Origen del patrimonio"),
        new(PtPerfil, "Perfil transaccional", "Al menos una transacción esperada"),
        new(CxCuentaExterna, "Cuentas externas", "Al menos una cuenta en otra institución (IBAN)"),
        new(RelRepresentanteLegal, "Relaciones requeridas", "Representante legal (relación vigente)", "Juridica"),
        new(RelBeneficiarioFinal, "Relaciones requeridas", "Beneficiario final (relación vigente)", "Juridica"),
        new(KycEstadoCompleto, "Cumplimiento (KYC)", "KYC completo y vigente"),
        new(KycPepEvaluado, "Cumplimiento (KYC)", "Evaluación PEP vigente y resuelta"),
        new(KycSancionesEvaluado, "Cumplimiento (KYC)", "Evaluación de sanciones vigente, resuelta y sin coincidencia confirmada"),
        new(IdentInstitucion, "Identificación", "Institución emisora"),
        new(IdentFechaEmision, "Identificación", "Fecha de emisión"),
        new(IdentFechaVencimiento, "Identificación", "Fecha de vencimiento"),
        new(Direccion, "Direcciones", "Al menos una dirección"),
        new(DirNiveles, "Direcciones", "División territorial (según el país)"),
        new(DirCodigoPostal, "Direcciones", "Código postal (según el país)"),
        new(DirLinea2, "Direcciones", "Dirección exacta (línea 2)"),
        new(DirLinea3, "Direcciones", "Dirección exacta (línea 3)"),
        new(Correo, "Contactos", "Al menos un correo electrónico"),
        new(Telefono, "Contactos", "Al menos un teléfono"),
    ];

    public static readonly IReadOnlySet<string> Codigos = Catalogo.Select(c => c.Codigo).ToHashSet();

    /// <summary>Campos parametrizables que aplican al tipo de interlocutor (Fisica / Juridica).</summary>
    public static IReadOnlyList<CampoRequisito> CatalogoDe(string tipoPersona)
        => Catalogo.Where(c => c.Aplica == "Ambos" || c.Aplica == tipoPersona).ToList();

    public static bool TipoValido(string? t) => t is "Fisica" or "Juridica";

    /// <summary>Lo que se exigia antes de parametrizar (valores iniciales de todos los roles y regla de respaldo).</summary>
    public static readonly IReadOnlyList<string> Base = [FechaNacimiento, IdentInstitucion, IdentFechaEmision, DirNiveles, DirCodigoPostal];

    private readonly HashSet<string> _requeridos;
    private readonly HashSet<long> _documentos;
    /// <param name="documentos">Tipos de documento (doc.TIPO_DOCUMENTO) que el rol exige tener vigentes en el expediente.</param>
    public ReglasCampos(IEnumerable<string> requeridos, IEnumerable<long>? documentos = null)
    {
        _requeridos = requeridos.Where(Codigos.Contains).ToHashSet();
        _documentos = (documentos ?? []).ToHashSet();
    }

    /// <summary>Lo que se exigia en las organizaciones antes de parametrizar.</summary>
    public static readonly IReadOnlyList<string> BaseOrg = [IdentInstitucion, DirNiveles, DirCodigoPostal, OrgActividad];

    public static ReglasCampos Legado => new(Base);
    public static ReglasCampos LegadoDe(string tipoPersona) => new(tipoPersona == "Juridica" ? BaseOrg : Base);
    public bool Pide(string codigo) => _requeridos.Contains(codigo);
    public IReadOnlyCollection<string> Lista => _requeridos;
    public IReadOnlyCollection<long> Documentos => _documentos;
    public ReglasCampos Union(ReglasCampos otra) => new(_requeridos.Union(otra._requeridos), _documentos.Union(otra._documentos));
}
