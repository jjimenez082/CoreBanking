namespace CoreBancario.Api.Infraestructura;

/// <summary>
/// Identidad de quien realiza la solicitud, ya resuelta contra sec.USUARIO o
/// sec.USUARIO_INTEGRACION dentro de la base de datos del tenant.
/// Scoped: una instancia por request, poblada por <see cref="ActorResolutionMiddleware"/>
/// despues de que el tenant ya fue resuelto.
/// </summary>
public class ActorContext
{
    public long IdCompania { get; private set; }
    public long? IdUsuario { get; private set; }
    public long? IdUsuarioIntegracion { get; private set; }
    public string NombreDescriptivo { get; private set; } = string.Empty;

    private HashSet<string> _codigosPermiso = new();

    /// <summary>True si quien llama es una integracion (Client Credentials). Su IdUsuario es el usuario tecnico asociado.</summary>
    public bool EsIntegracion => IdUsuarioIntegracion.HasValue;

    /// <summary>True si quien llama es una persona (sec.USUARIO de tipo FISICO); false si es una integracion.</summary>
    public bool EsUsuarioHumano => IdUsuario.HasValue && !EsIntegracion;

    public bool Resuelto { get; private set; }

    /// <summary>
    /// Codigos de sec.OBJETO_AUTORIZACION que el actor tiene, resueltos via
    /// sus roles (sec.USUARIO_ROL). Para una integracion son los de su usuario tecnico.
    /// </summary>
    public IReadOnlyCollection<string> CodigosPermiso => _codigosPermiso;

    public void EstablecerUsuario(long idCompania, long idUsuario, string nombreCompleto)
    {
        IdCompania = idCompania;
        IdUsuario = idUsuario;
        NombreDescriptivo = nombreCompleto;
        Resuelto = true;
    }

    /// <summary>La integracion actua con los permisos de su usuario tecnico asociado (idUsuarioTecnico).</summary>
    public void EstablecerUsuarioIntegracion(long idCompania, long idUsuarioIntegracion, long idUsuarioTecnico, string nombre)
    {
        IdCompania = idCompania;
        IdUsuario = idUsuarioTecnico;
        IdUsuarioIntegracion = idUsuarioIntegracion;
        NombreDescriptivo = nombre;
        Resuelto = true;
    }

    public void EstablecerPermisos(IEnumerable<string> codigos)
    {
        _codigosPermiso = codigos.ToHashSet();
    }

    private IReadOnlyList<long> _adjuntosTemporales = Array.Empty<long>();

    /// <summary>
    /// Ids de documentos temporales (ya subidos) que acompanan a esta solicitud; llegan en el encabezado X-Adjuntos y
    /// se vinculan a la gestion en el momento en que esta se crea (MotorAprobacion.IniciarAsync).
    /// </summary>
    public IReadOnlyList<long> AdjuntosTemporales => _adjuntosTemporales;

    public void EstablecerAdjuntosTemporales(IEnumerable<long> ids)
    {
        _adjuntosTemporales = ids.Distinct().Take(50).ToList();
    }

    /// <summary>
    /// RBAC (SEG-002): true si el actor puede ejecutar la accion identificada por ese codigo de
    /// sec.OBJETO_AUTORIZACION. Aplica igual a personas e integraciones: una integracion hereda los
    /// permisos de su usuario tecnico (sus roles); sin permiso -> 403 permiso_denegado.
    /// </summary>
    public bool TienePermiso(string codigo) => _codigosPermiso.Contains(codigo);
}
