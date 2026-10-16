using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Fusion de clientes duplicados. Calcula que se traslada, que se descarta por repetido y que impide la fusion, y la aplica.
/// El cliente absorbido NUNCA se borra: queda 'Fusionado' con id_interlocutor_sobreviviente (alias). Todo lo que hoy cuelga de el
/// (identificaciones, direcciones, contactos, roles, relaciones, bloqueos, cumplimiento, etc.) pasa al sobreviviente, que conserva
/// sus propias marcas de «principal». Punto de extension: cuando existan Cuentas y Creditos, sus titularidades se trasladan aqui.
/// </summary>
public static class FusionServicio
{
    public const string Entidad = "INTERLOCUTOR_FUSION";
    public const string CampoSobreviviente = "fus_sobreviviente";
    public const string CampoDuplicado = "fus_duplicado";
    public const string CampoIncorpora = "fus_incorpora";
    public const string CampoRetira = "fus_retira";
    private sealed record Evento(string Desc, bool Traslada, string? Nota);
    public const string CampoMotivo = "fus_motivo";
    public const string PrefijoDireccion = "fus_dir[";

    // ----- Datos intermedios del calculo --------------------------------------------------

    public sealed record Calculo(
        FusionPlan Plan, List<string> Impedimentos, List<string> Advertencias,
        long? IdOtra, string NumeroSobreviviente, string NumeroDuplicado, string NombreSobreviviente, string NombreDuplicado);

    private static string Clave(string? s) => new((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NombreCliente(string? n1, string? n2, string? a1, string? a2)
        => string.Join(' ', new[] { n1, n2, a1, a2 }.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <summary>Gestion pendiente que involucra al cliente (modificacion, fusion como absorbido o como sobreviviente), o null.</summary>
    public static async Task<string?> GestionPendienteAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor, long? excepto = null, bool paraFusion = false)
        => await c.ExecuteScalarAsync<string?>(
            """
            SELECT TOP 1 g.numero FROM apr.GESTION g
            WHERE g.id_compania = @idCompania AND g.estado = N'Pendiente de Aprobacion' AND (@excepto IS NULL OR g.id_gestion <> @excepto)
              AND ( (g.entidad_negocio IN (N'INTERLOCUTOR', N'INTERLOCUTOR_ROL', N'INTERLOCUTOR_RELACION', N'INTERLOCUTOR_FALLECIMIENTO', @entidad) AND g.llave_registro = @llave)
                 OR (@paraFusion = 1 AND g.entidad_negocio = N'INTERLOCUTOR_CONSENTIMIENTO' AND g.llave_registro = @llave)
                 OR (@paraFusion = 1 AND g.entidad_negocio = N'INTERLOCUTOR_KYC' AND g.llave_registro = @llave)
                 OR (@paraFusion = 1 AND g.entidad_negocio = N'INTERLOCUTOR_DOCUMENTO' AND g.llave_registro = @llave)
                 OR (@paraFusion = 1 AND g.entidad_negocio = N'INTERLOCUTOR_BLOQUEO' AND g.llave_registro LIKE @prefijo)
                 OR (g.entidad_negocio = @entidad AND EXISTS (SELECT 1 FROM apr.GESTION_DETALLE d WHERE d.id_gestion = g.id_gestion
                                                              AND d.nombre_campo = @campoSobrev AND d.valor_nuevo LIKE @prefijo)) )
            """,
            new
            {
                idCompania, excepto, paraFusion, entidad = Entidad, campoSobrev = CampoSobreviviente,
                llave = idInterlocutor.ToString(CultureInfo.InvariantCulture),
                prefijo = idInterlocutor.ToString(CultureInfo.InvariantCulture) + "|%"
            }, tx);

    // ----- Calculo del plan -------------------------------------------------------------------

    public static async Task<Calculo> CalcularAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, long idS, long idD,
        IReadOnlyDictionary<long, FusionDireccionDecision>? decisiones, long? idGestionActual = null)
    {
        var impedimentos = new List<string>();
        var advertencias = new List<string>();
        var vacio = new FusionPlan([], [], [], [], []);

        if (idS == idD)
            return new(vacio, ["No se puede fusionar un cliente consigo mismo."], advertencias, null, "", "", "", "");

        async Task<dynamic?> Cargar(long id) => await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.id_interlocutor, i.numero, i.estado, i.tipo_persona, i.id_interlocutor_sobreviviente,
                   f.nombre1, f.nombre2, f.apellido1, f.apellido2, f.fecha_nacimiento, j.nombre_org1, j.fecha_constitucion
            FROM cli.INTERLOCUTOR i LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
            LEFT JOIN cli.INTERLOCUTOR_JURIDICA j ON j.id_interlocutor = i.id_interlocutor
            WHERE i.id_interlocutor = @id AND i.id_compania = @idCompania
            """, new { id, idCompania }, tx);

        var s = await Cargar(idS);
        var d = await Cargar(idD);
        if (s is null || d is null)
            return new(vacio, ["Alguno de los clientes no existe."], advertencias, null, "", "", "", "");

        var numS = (string)s.numero; var numD = (string)d.numero;
        var nomS = (string?)s.nombre_org1 ?? NombreCliente((string?)s.nombre1, (string?)s.nombre2, (string?)s.apellido1, (string?)s.apellido2);
        var nomD = (string?)d.nombre_org1 ?? NombreCliente((string?)d.nombre1, (string?)d.nombre2, (string?)d.apellido1, (string?)d.apellido2);

        var tipoS = (string)s.tipo_persona; var tipoD = (string)d.tipo_persona;
        var esOrg = tipoS == "Juridica" && tipoD == "Juridica";
        if (tipoS != tipoD)
            impedimentos.Add("Solo se pueden fusionar dos personas o dos organizaciones; no una persona con una organización.");
        else if (tipoS is not ("Fisica" or "Juridica"))
            impedimentos.Add("Por ahora solo se fusionan personas y organizaciones.");
        if ((string)s.estado != "Activo") impedimentos.Add($"El cliente sobreviviente N.º {numS} está '{(string)s.estado}'.");
        if ((string)d.estado != "Activo") impedimentos.Add($"El cliente N.º {numD} está '{(string)d.estado}'" +
            ((string)d.estado == "Fusionado" ? " y ya fue fusionado en otro cliente." : "."));

        var pendS = await GestionPendienteAsync(c, tx, idCompania, idS, idGestionActual, true);
        if (pendS is not null) impedimentos.Add($"El cliente N.º {numS} tiene la gestión {pendS} pendiente de aprobación; debe resolverse antes.");
        var pendD = await GestionPendienteAsync(c, tx, idCompania, idD, idGestionActual, true);
        if (pendD is not null) impedimentos.Add($"El cliente N.º {numD} tiene la gestión {pendD} pendiente de aprobación; debe resolverse antes.");

        // Un cliente con bloqueo vigente no se fusiona (evita «limpiar» un bloqueo por fraude, orden judicial o cumplimiento).
        foreach (var (idx, numx) in new[] { (idS, numS), (idD, numD) })
        {
            var tipos = await BloqueoServicio.TiposVigentesAsync(c, tx, idx);
            if (tipos.Count > 0)
                impedimentos.Add($"El cliente N.º {numx} tiene bloqueos vigentes ({string.Join(", ", tipos)}); deben levantarse antes de fusionar.");
        }

        // Grupos: si ambos son miembros del mismo grupo, la membresia del absorbido se cierra (quedaria repetida) y el grupo pierde un miembro.
        var gruposComunes = await c.QueryAsync(
            """
            SELECT g.numero, (SELECT COUNT(*) FROM cli.INTERLOCUTOR_RELACION x WHERE x.id_interlocutor_destino = rd.id_interlocutor_destino
                                AND x.id_tipo_relacion = rd.id_tipo_relacion AND x.fecha_fin IS NULL) AS miembros
            FROM cli.INTERLOCUTOR_RELACION rd
            JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = rd.id_tipo_relacion AND it.codigo = N'MIEMBRO_GRUPO'
            JOIN cli.INTERLOCUTOR g ON g.id_interlocutor = rd.id_interlocutor_destino
            WHERE rd.id_interlocutor_origen = @idD AND rd.fecha_fin IS NULL
              AND EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_RELACION rs WHERE rs.id_interlocutor_origen = @idS AND rs.id_interlocutor_destino = rd.id_interlocutor_destino
                                                                      AND rs.id_tipo_relacion = rd.id_tipo_relacion AND rs.fecha_fin IS NULL)
            """, new { idS, idD }, tx);
        foreach (var gc in gruposComunes)
        {
            if ((int)gc.miembros - 1 < GrupoServicio.MinimoMiembros)
                impedimentos.Add($"Ambos son miembros del grupo N.º {(string)gc.numero}, que quedaría con menos de {GrupoServicio.MinimoMiembros} miembros. Agregue otro miembro al grupo antes de fusionar.");
            else
                advertencias.Add($"Ambos son miembros del grupo N.º {(string)gc.numero}; quedará una sola membresía (la del cliente que se conserva).");
        }

        // Advertencias: señales de que quiza NO son la misma persona (la decision es de quien aprueba).
        if (esOrg)
        {
            if (s.fecha_constitucion is DateTime cs && d.fecha_constitucion is DateTime cd && cs.Date != cd.Date)
                advertencias.Add($"Las fechas de constitución no coinciden ({cs:dd/MM/yyyy} y {cd:dd/MM/yyyy}).");
            var a = MotorCoincidencias.NormalizarOrganizacion(nomS); var b = MotorCoincidencias.NormalizarOrganizacion(nomD);
            if (MotorCoincidencias.Ratio(a, b) < 0.7)
                advertencias.Add("Las razones sociales son muy distintas; confirme que se trata de la misma organización.");
        }
        else
        {
            if (s.fecha_nacimiento is DateTime fs && d.fecha_nacimiento is DateTime fd && fs.Date != fd.Date)
                advertencias.Add($"Las fechas de nacimiento no coinciden ({fs:dd/MM/yyyy} y {fd:dd/MM/yyyy}).");
            if (MotorCoincidencias.SimilitudNombre((string?)s.nombre1, (string?)s.nombre2, (string?)s.apellido1, (string?)s.apellido2,
                    (string?)d.nombre1, (string?)d.nombre2, (string?)d.apellido1, (string?)d.apellido2) < 0.7)
                advertencias.Add("Los nombres son muy distintos; confirme que se trata de la misma persona.");
        }

        var idOtra = await c.ExecuteScalarAsync<long?>(
            """
            SELECT TOP 1 i.id_lista_valores_item FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = N'TIPO_DIRECCION' AND i.codigo = N'OTRA' AND i.estado = 'Activo'
            """, null, tx);

        // ----- Identificaciones -----
        var identD = (await c.QueryAsync("SELECT id_identificacion, id_tipo_identificacion, numero_identificacion, estado FROM cli.INTERLOCUTOR_IDENTIFICACION WHERE id_interlocutor = @idD", new { idD }, tx)).ToList();
        var identS = (await c.QueryAsync("SELECT id_tipo_identificacion, numero_identificacion FROM cli.INTERLOCUTOR_IDENTIFICACION WHERE id_interlocutor = @idS AND estado = 'Activo'", new { idS }, tx)).ToList();
        foreach (var iD in identD.Where(x => (string)x.estado == "Activo"))
            foreach (var iS in identS.Where(x => (long)x.id_tipo_identificacion == (long)iD.id_tipo_identificacion
                                                  && Clave((string)x.numero_identificacion) != Clave((string)iD.numero_identificacion)))
                advertencias.Add($"Ambos tienen una identificación del mismo tipo pero con distinto número ({(string)iS.numero_identificacion} y {(string)iD.numero_identificacion}); se conservarán las dos.");
        var planIdent = identD.Select(x => new FusionIdentificacionPlan((long)x.id_identificacion, "TRASLADAR")).ToList();

        // ----- Direcciones -----
        var dirD = (await c.QueryAsync("SELECT id_direccion, id_tipo_direccion, id_pais, detalle, es_principal FROM cli.INTERLOCUTOR_DIRECCION WHERE id_interlocutor = @idD AND estado = 'Activo' ORDER BY id_direccion", new { idD }, tx)).ToList();
        var dirS = (await c.QueryAsync("SELECT id_tipo_direccion, id_pais, detalle FROM cli.INTERLOCUTOR_DIRECCION WHERE id_interlocutor = @idS AND estado = 'Activo'", new { idS }, tx)).ToList();
        var tiposS = dirS.Select(x => (long)x.id_tipo_direccion).ToHashSet();
        var planDir = new List<FusionDireccionPlan>();
        foreach (var x in dirD)
        {
            var id = (long)x.id_direccion; var tipo = (long)x.id_tipo_direccion;
            var repetida = dirS.Any(y => Clave((string)y.detalle) == Clave((string)x.detalle) && (long?)y.id_pais == (long?)x.id_pais);
            var conflicto = tiposS.Contains(tipo);
            var sugerido = conflicto && idOtra is not null ? idOtra.Value : tipo;
            var incorporar = !repetida;
            if (decisiones is not null && decisiones.TryGetValue(id, out var dec))
            {
                incorporar = dec.Incorporar;
                if (dec.Incorporar && dec.IdTipoDireccion is not null) sugerido = dec.IdTipoDireccion.Value;
            }
            planDir.Add(new FusionDireccionPlan(id, tipo, sugerido, conflicto, incorporar));
        }

        // ----- Contactos -----
        var contD = (await c.QueryAsync("SELECT id_contacto, id_tipo_contacto, valor, es_principal FROM cli.INTERLOCUTOR_CONTACTO WHERE id_interlocutor = @idD AND estado = 'Activo' ORDER BY id_contacto", new { idD }, tx)).ToList();
        var contS = (await c.QueryAsync("SELECT id_tipo_contacto, valor, es_principal FROM cli.INTERLOCUTOR_CONTACTO WHERE id_interlocutor = @idS AND estado = 'Activo'", new { idS }, tx)).ToList();
        var tiposConPrincipal = contS.Where(x => (bool)x.es_principal).Select(x => (long)x.id_tipo_contacto).ToHashSet();
        var planCont = new List<FusionContactoPlan>();
        foreach (var x in contD)
        {
            var tipo = (long)x.id_tipo_contacto;
            var repetido = contS.Any(y => (long)y.id_tipo_contacto == tipo && Clave((string)y.valor) == Clave((string)x.valor));
            if (repetido) { planCont.Add(new FusionContactoPlan((long)x.id_contacto, "DESCARTAR_REPETIDO", false)); continue; }
            var principal = (bool)x.es_principal && !tiposConPrincipal.Contains(tipo);
            if (principal) tiposConPrincipal.Add(tipo);
            planCont.Add(new FusionContactoPlan((long)x.id_contacto, "TRASLADAR", principal));
        }

        // ----- Roles -----
        var rolD = (await c.QueryAsync(
            """
            SELECT r.id_tipo_rol, it.nombre, r.estado FROM cli.INTERLOCUTOR_ROL r JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = r.id_tipo_rol
            WHERE r.id_interlocutor = @idD AND r.estado = 'Activo'
            """, new { idD }, tx)).ToList();
        var rolS = (await c.QueryAsync("SELECT id_tipo_rol, estado FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @idS", new { idS }, tx)).ToList();
        var planRol = rolD.Select(x =>
        {
            var propio = rolS.FirstOrDefault(y => (long)y.id_tipo_rol == (long)x.id_tipo_rol);
            var accion = propio is null ? "TRASLADAR" : (string)propio.estado == "Activo" ? "YA_EXISTE" : "REACTIVAR";
            return new FusionRolPlan((long)x.id_tipo_rol, (string)x.nombre, accion);
        }).ToList();

        // ----- Otros (solo conteos informativos) -----
        async Task<int> N(string sql) => await c.ExecuteScalarAsync<int>(sql, new { idD }, tx);
        var otros = new List<FusionConteo>
        {
            new("Relaciones con otras personas", await N("SELECT COUNT(*) FROM cli.INTERLOCUTOR_RELACION WHERE (id_interlocutor_origen = @idD OR id_interlocutor_destino = @idD) AND fecha_fin IS NULL")),
            new("Historial de bloqueos (ya levantados)", await N("SELECT COUNT(*) FROM cli.INTERLOCUTOR_BLOQUEO WHERE id_interlocutor = @idD")),
            new("Cuentas externas (otras instituciones)", await N("SELECT COUNT(*) FROM cli.INTERLOCUTOR_CUENTA_EXTERNA WHERE id_interlocutor = @idD AND estado = 'Activo'")),
            new("Datos de KYC (estado, riesgo, segmentos)", await N("SELECT COUNT(*) FROM cli.INTERLOCUTOR_KYC WHERE id_interlocutor = @idD")),
            new("Motivos de riesgo, expedientes y evaluaciones PEP/sanciones", await N(
                """
                SELECT (SELECT COUNT(*) FROM cli.INTERLOCUTOR_MOTIVO_RIESGO WHERE id_interlocutor = @idD AND estado = 'Activo')
                     + (SELECT COUNT(*) FROM cli.INTERLOCUTOR_EXPEDIENTE WHERE id_interlocutor = @idD AND estado = 'Activo')
                     + (SELECT COUNT(*) FROM cli.INTERLOCUTOR_SCREENING WHERE id_interlocutor = @idD)
                """)),
            new("Perfil financiero (perfil económico, residencias, ingresos, orígenes, transacciones)", await N(
                """
                SELECT (SELECT COUNT(*) FROM cli.INTERLOCUTOR_PERFIL_ECONOMICO WHERE id_interlocutor = @idD)
                     + (SELECT COUNT(*) FROM cli.INTERLOCUTOR_RESIDENCIA_FISCAL WHERE id_interlocutor = @idD AND estado = 'Activo')
                     + (SELECT COUNT(*) FROM cli.INTERLOCUTOR_INGRESO WHERE id_interlocutor = @idD AND estado = 'Activo')
                     + (SELECT COUNT(*) FROM cli.INTERLOCUTOR_ORIGEN WHERE id_interlocutor = @idD AND estado = 'Activo')
                     + (SELECT COUNT(*) FROM cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL WHERE id_interlocutor = @idD AND estado = 'Activo')
                """)),
            new("Segmentos", await N("SELECT COUNT(*) FROM cli.INTERLOCUTOR_SEGMENTO WHERE id_interlocutor = @idD")),
            new("Consentimientos", await N("SELECT COUNT(*) FROM cli.INTERLOCUTOR_CONSENTIMIENTO WHERE id_interlocutor = @idD")),
            new("Registros de cumplimiento (KYC/AML)", await N("SELECT COUNT(*) FROM cli.INTERLOCUTOR_CUMPLIMIENTO WHERE id_interlocutor = @idD")),
            new("Representaciones legales", await N("SELECT COUNT(*) FROM cli.REPRESENTACION_LEGAL WHERE id_representante = @idD OR id_representado = @idD")),
            new("Documentos adjuntos", await DocumentoRepositorio.ContarDeClienteAsync(c, tx, idD)),
        }.Where(x => x.Cantidad > 0).ToList();
        if (otros.Any(x => x.Concepto.StartsWith("Datos de KYC", StringComparison.Ordinal) || x.Concepto.StartsWith("Perfil financiero", StringComparison.Ordinal)))
            advertencias.Add("Los datos de KYC y del perfil financiero se combinan: prevalecen los del cliente que se conserva y solo se completan los datos que le falten; revise la vigencia del KYC después de fusionar.");

        return new(new FusionPlan(planIdent, planDir, planCont, planRol, otros), impedimentos, advertencias, idOtra, numS, numD, nomS, nomD);
    }

    // ----- Decisiones guardadas en el detalle de la gestion ---------------------------------

    public static string TextoDecisionDireccion(FusionDireccionPlan p) => p.Incorporar ? $"I|{p.IdTipoDireccionSugerido}" : "D";

    public static Dictionary<long, FusionDireccionDecision> LeerDecisiones(IEnumerable<(string Campo, string? Valor)> filas)
    {
        var r = new Dictionary<long, FusionDireccionDecision>();
        foreach (var (campo, valor) in filas)
        {
            if (!campo.StartsWith(PrefijoDireccion, StringComparison.Ordinal) || !campo.EndsWith(']')) continue;
            if (!long.TryParse(campo[PrefijoDireccion.Length..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;
            if (valor is null) continue;
            if (valor == "D") { r[id] = new FusionDireccionDecision(id, false, null); continue; }
            var partes = valor.Split('|');
            if (partes.Length == 2 && partes[0] == "I" && long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var tipo))
                r[id] = new FusionDireccionDecision(id, true, tipo);
        }
        return r;
    }

    public static long? IdDeTexto(string? valor)
    {
        if (valor is null) return null;
        var i = valor.IndexOf('|');
        return long.TryParse(i < 0 ? valor : valor[..i], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    // ----- Aplicacion --------------------------------------------------------------------------

    public static async Task<ResultadoOperacion?> AplicarAsync(SqlConnection c, SqlTransaction tx, ActorContext actor, long idGestion, string porQuien)
    {
        var g = await c.QuerySingleAsync("SELECT llave_registro FROM apr.GESTION WHERE id_gestion = @idGestion", new { idGestion }, tx);
        var filas = (await c.QueryAsync("SELECT nombre_campo, valor_nuevo FROM apr.GESTION_DETALLE WHERE id_gestion = @idGestion", new { idGestion }, tx))
            .Select(f => ((string)f.nombre_campo, (string?)f.valor_nuevo)).ToList();
        var idD = IdDeTexto((string)g.llave_registro);
        var idS = IdDeTexto(filas.FirstOrDefault(f => f.Item1 == CampoSobreviviente).Item2);
        var motivo = filas.FirstOrDefault(f => f.Item1 == CampoMotivo).Item2 ?? "";
        if (idD is null || idS is null)
            return ResultadoOperacion.Fallo(500, "fusion_incompleta", "La gestión de fusión no tiene claramente los dos clientes.");

        // Se vuelve a calcular: entre la solicitud y la aprobacion pudo cambiar cualquiera de los dos.
        var calc = await CalcularAsync(c, tx, actor.IdCompania, idS.Value, idD.Value, LeerDecisiones(filas), idGestion);
        if (calc.Impedimentos.Count > 0)
            return ResultadoOperacion.Fallo(409, "fusion_no_aplicable", "La fusión ya no es válida: " + string.Join(" ", calc.Impedimentos) + " Rechace o devuelva la gestión.");

        var p = new { s = idS.Value, d = idD.Value, por = porQuien };
        var resumen = new Dictionary<string, int>();

        // Rastro de auditoria campo a campo: que se incorporo al sobreviviente y que se retiro/descarto del absorbido.
        var items = (await c.QueryAsync("SELECT id_lista_valores_item AS id, nombre FROM gen.LISTA_VALORES_ITEM", null, tx)).ToDictionary(x => (long)x.id, x => (string)x.nombre);
        string It(long id) => items.GetValueOrDefault(id, "#" + id);
        var eventos = new List<Evento>();
        var descIdent = (await c.QueryAsync("SELECT id_tipo_identificacion, numero_identificacion, estado FROM cli.INTERLOCUTOR_IDENTIFICACION WHERE id_interlocutor = @d", p, tx)).ToList();
        foreach (var x in descIdent)
            eventos.Add(new($"Identificación {It((long)x.id_tipo_identificacion)} {(string)x.numero_identificacion}" + ((string)x.estado == "Activo" ? "" : " (inactiva)"), true, null));
        var descDir = (await c.QueryAsync("SELECT id_direccion, id_tipo_direccion, detalle, estado FROM cli.INTERLOCUTOR_DIRECCION WHERE id_interlocutor = @d", p, tx))
            .ToDictionary(x => (long)x.id_direccion, x => (Tipo: (long)x.id_tipo_direccion, Detalle: (string)x.detalle, Estado: (string)x.estado));
        var descCont = (await c.QueryAsync("SELECT id_contacto, id_tipo_contacto, valor, estado FROM cli.INTERLOCUTOR_CONTACTO WHERE id_interlocutor = @d", p, tx))
            .ToDictionary(x => (long)x.id_contacto, x => (Tipo: (long)x.id_tipo_contacto, Valor: (string)x.valor, Estado: (string)x.estado));
        foreach (var x in descDir.Where(y => y.Value.Estado != "Activo"))
            eventos.Add(new($"Dirección «{It(x.Value.Tipo)}» (inactiva): {x.Value.Detalle}", true, null));
        foreach (var x in descCont.Where(y => y.Value.Estado != "Activo"))
            eventos.Add(new($"Contacto {It(x.Value.Tipo)} (inactivo): {x.Value.Valor}", true, null));

        // Organizaciones: nombres comerciales y actividades economicas activas pasan al sobreviviente (sin repetir).
        var comerciales = (await c.QueryAsync("SELECT id_nombre_comercial, nombre FROM cli.INTERLOCUTOR_NOMBRE_COMERCIAL WHERE id_interlocutor = @d AND estado = 'Activo'", p, tx)).ToList();
        if (comerciales.Count > 0)
        {
            var propios = (await c.QueryAsync<string>("SELECT nombre FROM cli.INTERLOCUTOR_NOMBRE_COMERCIAL WHERE id_interlocutor = @s AND estado = 'Activo'", p, tx))
                .Select(MotorCoincidencias.NormalizarOrganizacion).ToHashSet();
            foreach (var x in comerciales)
            {
                var id = (long)x.id_nombre_comercial; var nombre = (string)x.nombre;
                if (propios.Contains(MotorCoincidencias.NormalizarOrganizacion(nombre)))
                {
                    await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_NOMBRE_COMERCIAL SET estado = 'Inactivo', modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_nombre_comercial = @id", new { id, por = porQuien }, tx);
                    eventos.Add(new($"Nombre comercial «{nombre}»", false, "Descartado: el cliente que se conserva ya tiene uno igual"));
                }
                else
                {
                    await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_NOMBRE_COMERCIAL SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_nombre_comercial = @id", new { s = idS.Value, id, por = porQuien }, tx);
                    propios.Add(MotorCoincidencias.NormalizarOrganizacion(nombre));
                    eventos.Add(new($"Nombre comercial «{nombre}»", true, null));
                }
            }
        }
        var actividades = (await c.QueryAsync("SELECT id_actividad_interlocutor, id_actividad FROM cli.INTERLOCUTOR_ACTIVIDAD WHERE id_interlocutor = @d AND estado = 'Activo'", p, tx)).ToList();
        if (actividades.Count > 0)
        {
            var propias = (await c.QueryAsync<long>("SELECT id_actividad FROM cli.INTERLOCUTOR_ACTIVIDAD WHERE id_interlocutor = @s AND estado = 'Activo'", p, tx)).ToHashSet();
            var tienePrincipal = propias.Count > 0;
            foreach (var x in actividades)
            {
                var id = (long)x.id_actividad_interlocutor; var act = (long)x.id_actividad;
                var nombre = It(act);
                if (propias.Contains(act))
                {
                    await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_ACTIVIDAD SET estado = 'Inactivo', es_principal = 0, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_actividad_interlocutor = @id", new { id, por = porQuien }, tx);
                    eventos.Add(new($"Actividad económica «{nombre}»", false, "Descartada: el cliente que se conserva ya la tiene"));
                    continue;
                }
                // Principal solo si el sobreviviente no tiene actividades.
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_ACTIVIDAD SET id_interlocutor = @s, es_principal = 0, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_actividad_interlocutor = @id", new { s = idS.Value, id, por = porQuien }, tx);
                if (!tienePrincipal)
                {
                    await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_ACTIVIDAD SET es_principal = 1 WHERE id_actividad_interlocutor = @id", new { id }, tx);
                    tienePrincipal = true;
                }
                propias.Add(act);
                eventos.Add(new($"Actividad económica «{nombre}»", true, null));
            }
        }

        // Identificaciones (todas, incluso inactivas, para no perder historial). Principal solo si el sobreviviente no tiene.
        resumen["identificaciones"] = await c.ExecuteAsync(
            """
            UPDATE cli.INTERLOCUTOR_IDENTIFICACION
               SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME(),
                   es_principal = CASE WHEN es_principal = 1 AND estado = 'Activo'
                                        AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_IDENTIFICACION x WHERE x.id_interlocutor = @s AND x.es_principal = 1 AND x.estado = 'Activo')
                                       THEN 1 ELSE 0 END
             WHERE id_interlocutor = @d
            """, p, tx);

        // Direcciones: segun la decision (trasladar con el tipo elegido o dejar en el absorbido, dada de baja).
        var survTienePrincipalDir = await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_DIRECCION WHERE id_interlocutor = @s AND es_principal = 1 AND estado = 'Activo') THEN 1 ELSE 0 END", p, tx);
        var dirOriginal = (await c.QueryAsync("SELECT id_direccion, es_principal FROM cli.INTERLOCUTOR_DIRECCION WHERE id_interlocutor = @d AND estado = 'Activo'", p, tx))
            .ToDictionary(x => (long)x.id_direccion, x => (bool)x.es_principal);
        int dirTras = 0, dirDesc = 0;
        foreach (var x in calc.Plan.Direcciones)
        {
            if (!x.Incorporar)
            {
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_DIRECCION SET estado = 'Inactivo', es_principal = 0, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_direccion = @id", new { id = x.IdDireccion, por = porQuien }, tx);
                dirDesc++;
                if (descDir.TryGetValue(x.IdDireccion, out var dd0))
                    eventos.Add(new($"Dirección «{It(dd0.Tipo)}»: {dd0.Detalle}", false, "Descartada (no se traslada); queda inactiva en el cliente absorbido"));
                continue;
            }
            var principal = dirOriginal[x.IdDireccion] && !survTienePrincipalDir;
            if (principal) survTienePrincipalDir = true;
            await c.ExecuteAsync(
                "UPDATE cli.INTERLOCUTOR_DIRECCION SET id_interlocutor = @s, id_tipo_direccion = @tipo, es_principal = @pr, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_direccion = @id",
                new { s = idS.Value, id = x.IdDireccion, tipo = x.IdTipoDireccionSugerido, pr = principal, por = porQuien }, tx);
            dirTras++;
            if (descDir.TryGetValue(x.IdDireccion, out var dd1))
                eventos.Add(new($"Dirección «{It(x.IdTipoDireccionSugerido)}»: {dd1.Detalle}" + (x.IdTipoDireccionSugerido != dd1.Tipo ? $" (era «{It(dd1.Tipo)}»)" : "") + (principal ? " · principal" : ""), true, null));
        }
        // Direcciones inactivas del absorbido: tambien pasan (historial).
        await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_DIRECCION SET id_interlocutor = @s, es_principal = 0 WHERE id_interlocutor = @d AND estado <> 'Activo'", p, tx);
        resumen["direccionesTrasladadas"] = dirTras; resumen["direccionesDescartadas"] = dirDesc;

        // Contactos
        int contTras = 0, contRep = 0;
        foreach (var x in calc.Plan.Contactos)
        {
            if (x.Accion == "DESCARTAR_REPETIDO")
            {
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_CONTACTO SET estado = 'Inactivo', es_principal = 0, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_contacto = @id", new { id = x.IdContacto, por = porQuien }, tx);
                contRep++;
                if (descCont.TryGetValue(x.IdContacto, out var cc0))
                    eventos.Add(new($"Contacto {It(cc0.Tipo)}: {cc0.Valor}", false, "Descartado: ya existe igual en el cliente que se conserva"));
                continue;
            }
            // Primero sin principal (evita chocar con el indice unico), luego se marca si corresponde.
            await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_CONTACTO SET id_interlocutor = @s, es_principal = 0, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_contacto = @id", new { s = idS.Value, id = x.IdContacto, por = porQuien }, tx);
            if (x.SeraPrincipal)
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_CONTACTO SET es_principal = 1 WHERE id_contacto = @id", new { id = x.IdContacto }, tx);
            contTras++;
            if (descCont.TryGetValue(x.IdContacto, out var cc1))
                eventos.Add(new($"Contacto {It(cc1.Tipo)}: {cc1.Valor}" + (x.SeraPrincipal ? " · principal" : ""), true, null));
        }
        await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_CONTACTO SET id_interlocutor = @s, es_principal = 0 WHERE id_interlocutor = @d AND estado <> 'Activo'", p, tx);
        resumen["contactosTrasladados"] = contTras; resumen["contactosRepetidos"] = contRep;

        // Roles: los que el sobreviviente no tiene se trasladan; si ya los tiene inactivos se reactivan; el resto del absorbido se da de baja.
        int rolTras = 0, rolReact = 0;
        foreach (var r in calc.Plan.Roles)
        {
            if (r.Accion == "TRASLADAR")
            {
                var propioInactivo = await c.ExecuteScalarAsync<long?>("SELECT id_interlocutor_rol FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @s AND id_tipo_rol = @t", new { s = idS.Value, t = r.IdTipoRol }, tx);
                if (propioInactivo is not null) { eventos.Add(new($"Rol {r.Nombre}", false, "No se traslada: el cliente que se conserva ya tiene ese rol")); continue; }
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_ROL SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d AND id_tipo_rol = @t", new { s = idS.Value, d = idD.Value, t = r.IdTipoRol, por = porQuien }, tx);
                rolTras++;
                eventos.Add(new($"Rol {r.Nombre}", true, null));
            }
            else if (r.Accion == "REACTIVAR")
            {
                var idRolPropio = await c.ExecuteScalarAsync<long>("SELECT id_interlocutor_rol FROM cli.INTERLOCUTOR_ROL WHERE id_interlocutor = @s AND id_tipo_rol = @t", new { s = idS.Value, t = r.IdTipoRol }, tx);
                await RolServicio.ReactivarAsync(c, tx, idRolPropio, idGestion, porQuien);
                rolReact++;
                eventos.Add(new($"Rol {r.Nombre} (reactivado en el cliente que se conserva)", true, null));
            }
        }
        foreach (var r in calc.Plan.Roles.Where(y => y.Accion == "YA_EXISTE"))
            eventos.Add(new($"Rol {r.Nombre}", false, "Ya lo tenía el cliente que se conserva; se da de baja en el absorbido"));
        await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_ROL SET estado = 'Inactivo', fecha_baja = SYSUTCDATETIME(), modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d AND estado = 'Activo'", p, tx);
        resumen["rolesTrasladados"] = rolTras; resumen["rolesReactivados"] = rolReact;

        // Relaciones con otras personas: se re-apuntan, salvo las que quedarian consigo mismo o repetidas (esas se cierran).
        var rels = (await c.QueryAsync(
            "SELECT id_interlocutor_relacion, id_interlocutor_origen, id_interlocutor_destino, id_tipo_relacion FROM cli.INTERLOCUTOR_RELACION WHERE (id_interlocutor_origen = @d OR id_interlocutor_destino = @d) AND fecha_fin IS NULL", p, tx)).ToList();
        int relTras = 0, relCerr = 0;
        foreach (var r in rels)
        {
            var origen = (long)r.id_interlocutor_origen == idD.Value ? idS.Value : (long)r.id_interlocutor_origen;
            var destino = (long)r.id_interlocutor_destino == idD.Value ? idS.Value : (long)r.id_interlocutor_destino;
            var existe = origen == destino || await c.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_RELACION WHERE id_interlocutor_origen = @o AND id_interlocutor_destino = @de AND id_tipo_relacion = @t AND fecha_fin IS NULL) THEN 1 ELSE 0 END",
                new { o = origen, de = destino, t = (long)r.id_tipo_relacion }, tx);
            var otroId = (long)r.id_interlocutor_origen == idD.Value ? (long)r.id_interlocutor_destino : (long)r.id_interlocutor_origen;
            var otroNum = otroId == idD.Value ? "el mismo cliente" : "N.º " + await c.ExecuteScalarAsync<string?>("SELECT numero FROM cli.INTERLOCUTOR WHERE id_interlocutor = @otroId", new { otroId }, tx);
            var descRel = $"Relación «{It((long)r.id_tipo_relacion)}» con {otroNum}";
            if (existe)
            {
                eventos.Add(new(descRel, false, "Cerrada: quedaría repetida o consigo mismo en el cliente que se conserva"));
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_RELACION SET fecha_fin = SYSUTCDATETIME() WHERE id_interlocutor_relacion = @id", new { id = (long)r.id_interlocutor_relacion }, tx);
                relCerr++;
            }
            else
            {
                await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_RELACION SET id_interlocutor_origen = @o, id_interlocutor_destino = @de WHERE id_interlocutor_relacion = @id", new { o = origen, de = destino, id = (long)r.id_interlocutor_relacion }, tx);
                relTras++;
                eventos.Add(new(descRel, true, null));
            }
        }
        resumen["relacionesTrasladadas"] = relTras; resumen["relacionesCerradas"] = relCerr;

        // KYC, perfil financiero, consentimientos y cuentas externas: prevalece lo del cliente que se conserva; del absorbido se
        // traslada lo que complementa (datos que faltan y registros no repetidos). Lo repetido o en conflicto queda en el absorbido.
        var kyc = await CombinarKycAsync(c, tx, idS.Value, idD.Value, porQuien);
        resumen["kyc"] = kyc.Datos; resumen["kycMotivos"] = kyc.Motivos; resumen["kycExpedientes"] = kyc.Expedientes; resumen["kycEvaluaciones"] = kyc.Evaluaciones;
        var perfil = await CombinarPerfilFinancieroAsync(c, tx, idS.Value, idD.Value, porQuien);
        resumen["perfilEconomico"] = perfil.Economico; resumen["residenciasFiscales"] = perfil.Residencias; resumen["ingresos"] = perfil.Ingresos;
        resumen["origenes"] = perfil.Origenes; resumen["perfilTransaccional"] = perfil.Transacciones;

        // Cuentas externas: se trasladan las que el sobreviviente no tiene (mismo IBAN).
        resumen["cuentasExternas"] = await c.ExecuteAsync(
            """
            UPDATE x SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            FROM cli.INTERLOCUTOR_CUENTA_EXTERNA x
            WHERE x.id_interlocutor = @d AND x.estado = 'Activo'
              AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_CUENTA_EXTERNA y WHERE y.id_interlocutor = @s AND y.estado = 'Activo'
                              AND UPPER(y.numero_cuenta_externa) = UPPER(x.numero_cuenta_externa))
            """, p, tx);
        var cuentasRep = await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR_CUENTA_EXTERNA SET estado = 'Inactivo', modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d AND estado = 'Activo'", p, tx);
        if (cuentasRep > 0) eventos.Add(new($"{cuentasRep} cuenta(s) externa(s)", false, "No se trasladan: el cliente que se conserva ya las tiene"));

        // Consentimientos: el historial (revocados) pasa; un consentimiento vigente pasa solo si el sobreviviente no tiene ningun registro de ese tipo
        // (nunca se «reactiva» un consentimiento que el sobreviviente ya revoco o ya tiene vigente).
        var consTras = await c.ExecuteAsync(
            """
            UPDATE x SET id_interlocutor = @s
            FROM cli.INTERLOCUTOR_CONSENTIMIENTO x
            WHERE x.id_interlocutor = @d
              AND (x.estado = 'Revocado'
                   OR NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_CONSENTIMIENTO y WHERE y.id_interlocutor = @s AND y.id_tipo_consentimiento = x.id_tipo_consentimiento))
            """, p, tx);
        var consQuedan = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cli.INTERLOCUTOR_CONSENTIMIENTO WHERE id_interlocutor = @d", p, tx);
        resumen["consentimientos"] = consTras;
        if (consQuedan > 0) eventos.Add(new($"{consQuedan} consentimiento(s) vigente(s)", false, "No se trasladan: el cliente que se conserva ya tiene un registro de ese tipo; prevalece el suyo"));

        // Resto: pasan tal cual al sobreviviente.
        var segTotal = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cli.INTERLOCUTOR_SEGMENTO WHERE id_interlocutor = @d", p, tx);
        // Bloqueos: solo puede haber historial (los vigentes impiden la fusion); el estado se llama «Levantado» y la tabla no lleva columnas de modificacion.
        resumen["bloqueos"] = await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_BLOQUEO SET id_interlocutor = @s WHERE id_interlocutor = @d AND estado <> 'Vigente'", p, tx);
        resumen["cumplimiento"] = await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_CUMPLIMIENTO SET id_interlocutor = @s WHERE id_interlocutor = @d", p, tx);
        resumen["idsExternos"] = await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_ID_EXTERNO SET id_interlocutor = @s WHERE id_interlocutor = @d", p, tx);
        resumen["segmentos"] = await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR_SEGMENTO SET id_interlocutor = @s WHERE id_interlocutor = @d AND id_dimension_segmento NOT IN (SELECT id_dimension_segmento FROM cli.INTERLOCUTOR_SEGMENTO WHERE id_interlocutor = @s)", p, tx);
        resumen["representaciones"] =
            await c.ExecuteAsync("UPDATE cli.REPRESENTACION_LEGAL SET id_representante = @s WHERE id_representante = @d AND id_representado <> @s", p, tx)
          + await c.ExecuteAsync("UPDATE cli.REPRESENTACION_LEGAL SET id_representado = @s WHERE id_representado = @d AND id_representante <> @s", p, tx);
        resumen["prospectos"] = await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_PROSPECTO SET id_interlocutor = @s WHERE id_interlocutor = @d", p, tx);
        // Documentos adjuntos (r44a): el arbol del absorbido pasa al sobreviviente; los unicos repetidos quedan obsoletos.
        var (docTras, docObs) = await DocumentoRepositorio.TrasladarPorFusionAsync(c, tx, idS.Value, idD.Value, porQuien);
        resumen["documentos"] = docTras; resumen["documentosObsoletos"] = docObs;
        foreach (var (clave, etiqueta) in new[] { ("bloqueos", "registro(s) del historial de bloqueos"), ("cuentasExternas", "cuenta(s) externa(s)"), ("consentimientos", "consentimiento(s)"),
                     ("kyc", "dato(s) de KYC completado(s)"), ("kycMotivos", "motivo(s) de riesgo"), ("kycExpedientes", "referencia(s) de expediente"), ("kycEvaluaciones", "evaluación(es) PEP/sanciones"),
                     ("perfilEconomico", "dato(s) del perfil económico"), ("residenciasFiscales", "residencia(s) fiscal(es)"), ("ingresos", "ingreso(s)"),
                     ("origenes", "origen(es) de fondos o patrimonio"), ("perfilTransaccional", "transacción(es) esperada(s)"),
                     ("cumplimiento", "registro(s) de cumplimiento (KYC/AML)"), ("idsExternos", "identificador(es) externo(s)"), ("segmentos", "segmento(s)"),
                     ("representaciones", "representación(es) legal(es)"), ("prospectos", "prospecto(s) vinculado(s)"),
                     ("documentos", "documento(s) adjunto(s)") })
            if (resumen[clave] > 0) eventos.Add(new($"{resumen[clave]} {etiqueta}", true, null));
        if (segTotal > resumen["segmentos"])
            eventos.Add(new($"{segTotal - resumen["segmentos"]} segmento(s)", false, "No se trasladan: el cliente que se conserva ya tiene esa dimensión"));

        // El absorbido queda como alias del sobreviviente.
        await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR SET estado = 'Fusionado', id_interlocutor_sobreviviente = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d", p, tx);

        await c.ExecuteAsync(
            """
            INSERT INTO cli.INTERLOCUTOR_FUSION (id_compania, id_gestion, id_sobreviviente, id_fusionado, motivo, resumen, aplicada_por)
            VALUES (@idCompania, @idGestion, @s, @d, @motivo, @resumen, @por)
            """, new { actor.IdCompania, idGestion, s = idS.Value, d = idD.Value, motivo, resumen = JsonSerializer.Serialize(resumen), por = porQuien }, tx);

        foreach (var e in eventos)
        {
            if (e.Traslada)
            {
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idS.Value, CampoIncorpora, null, $"{e.Desc} (desde el cliente N.º {calc.NumeroDuplicado})", idGestion);
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idD.Value, CampoRetira, e.Desc, $"Trasladado al cliente N.º {calc.NumeroSobreviviente}", idGestion);
            }
            else
                await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idD.Value, CampoRetira, e.Desc, e.Nota, idGestion);
        }
        await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idD.Value, "estado", "Activo", $"Fusionado en el cliente N.º {calc.NumeroSobreviviente}", idGestion);
        await Auditoria.CambioAsync(c, tx, actor, "INTERLOCUTOR", idS.Value, "fusion", null, $"Absorbió al cliente N.º {calc.NumeroDuplicado} ({calc.NombreDuplicado})", idGestion);
        return null;
    }

    private record ResultadoKyc(int Datos, int Motivos, int Expedientes, int Evaluaciones);
    private record ResultadoPerfil(int Economico, int Residencias, int Ingresos, int Origenes, int Transacciones);

    /// <summary>
    /// KYC (1:1): si el sobreviviente no tiene registro se traslada el del absorbido; si lo tiene, solo se completan sus campos vacios (el par de fechas de
    /// revision se toma completo y solo si el sobreviviente no tiene ninguna). Motivos de riesgo (sin repetir), expedientes y evaluaciones PEP/sanciones pasan.
    /// </summary>
    private static async Task<ResultadoKyc> CombinarKycAsync(SqlConnection c, SqlTransaction tx, long idS, long idD, string por)
    {
        var p = new { s = idS, d = idD, por };
        var datos = 0;
        var hayD = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_KYC WHERE id_interlocutor = @d) THEN 1 ELSE 0 END", p, tx);
        if (hayD)
        {
            var hayS = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_KYC WHERE id_interlocutor = @s) THEN 1 ELSE 0 END", p, tx);
            if (!hayS)
            {
                datos = await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_KYC SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d", p, tx);
            }
            else
            {
                datos = await c.ExecuteAsync(
                    """
                    UPDATE k SET
                        id_estado_kyc = COALESCE(k.id_estado_kyc, d.id_estado_kyc),
                        id_nivel_dd = COALESCE(k.id_nivel_dd, d.id_nivel_dd),
                        id_riesgo = COALESCE(k.id_riesgo, d.id_riesgo),
                        id_segmento_cliente = COALESCE(k.id_segmento_cliente, d.id_segmento_cliente),
                        id_segmento_pricing = COALESCE(k.id_segmento_pricing, d.id_segmento_pricing),
                        id_segmento_riesgo = COALESCE(k.id_segmento_riesgo, d.id_segmento_riesgo),
                        fecha_ultima_revision = CASE WHEN k.fecha_ultima_revision IS NULL AND k.fecha_proxima_revision IS NULL THEN d.fecha_ultima_revision ELSE k.fecha_ultima_revision END,
                        fecha_proxima_revision = CASE WHEN k.fecha_ultima_revision IS NULL AND k.fecha_proxima_revision IS NULL THEN d.fecha_proxima_revision ELSE k.fecha_proxima_revision END,
                        modificado_por = @por, modificado_en = SYSUTCDATETIME()
                    FROM cli.INTERLOCUTOR_KYC k JOIN cli.INTERLOCUTOR_KYC d ON d.id_interlocutor = @d
                    WHERE k.id_interlocutor = @s
                      AND ((k.id_estado_kyc IS NULL AND d.id_estado_kyc IS NOT NULL) OR (k.id_nivel_dd IS NULL AND d.id_nivel_dd IS NOT NULL)
                        OR (k.id_riesgo IS NULL AND d.id_riesgo IS NOT NULL) OR (k.id_segmento_cliente IS NULL AND d.id_segmento_cliente IS NOT NULL)
                        OR (k.id_segmento_pricing IS NULL AND d.id_segmento_pricing IS NOT NULL) OR (k.id_segmento_riesgo IS NULL AND d.id_segmento_riesgo IS NOT NULL)
                        OR (k.fecha_ultima_revision IS NULL AND k.fecha_proxima_revision IS NULL AND (d.fecha_ultima_revision IS NOT NULL OR d.fecha_proxima_revision IS NOT NULL)))
                    """, p, tx);
            }
        }
        var motivos = await c.ExecuteAsync(
            """
            UPDATE x SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            FROM cli.INTERLOCUTOR_MOTIVO_RIESGO x
            WHERE x.id_interlocutor = @d AND x.estado = 'Activo'
              AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_MOTIVO_RIESGO y WHERE y.id_interlocutor = @s AND y.estado = 'Activo' AND y.id_motivo = x.id_motivo)
            """, p, tx);
        await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_MOTIVO_RIESGO SET estado = 'Inactivo', modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d AND estado = 'Activo'", p, tx);
        var expedientes = await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR_EXPEDIENTE SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d AND estado = 'Activo'", p, tx);
        // Evaluaciones: todo el historial pasa; la vigente de cada tipo es la mas reciente del conjunto.
        var evaluaciones = await c.ExecuteAsync(
            "UPDATE cli.INTERLOCUTOR_SCREENING SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d", p, tx);
        return new(datos, motivos, expedientes, evaluaciones);
    }

    /// <summary>
    /// Perfil financiero: el perfil economico (1:1) se completa campo a campo con lo que le falte al sobreviviente (los montos con su moneda y fecha de corte
    /// viajan juntos y solo si el sobreviviente no tiene ninguno). Residencias fiscales, ingresos, origenes y transacciones esperadas se trasladan si no estan repetidos.
    /// </summary>
    private static async Task<ResultadoPerfil> CombinarPerfilFinancieroAsync(SqlConnection c, SqlTransaction tx, long idS, long idD, string por)
    {
        var p = new { s = idS, d = idD, por };
        var economico = 0;
        var hayD = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_PERFIL_ECONOMICO WHERE id_interlocutor = @d) THEN 1 ELSE 0 END", p, tx);
        if (hayD)
        {
            var hayS = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_PERFIL_ECONOMICO WHERE id_interlocutor = @s) THEN 1 ELSE 0 END", p, tx);
            if (!hayS)
                economico = await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_PERFIL_ECONOMICO SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_interlocutor = @d", p, tx);
            else
                economico = await c.ExecuteAsync(
                    """
                    UPDATE k SET
                        id_condicion_laboral = COALESCE(k.id_condicion_laboral, d.id_condicion_laboral),
                        id_ocupacion = COALESCE(k.id_ocupacion, d.id_ocupacion),
                        empleador = COALESCE(k.empleador, d.empleador),
                        gastos_estimados = CASE WHEN sinMontos.v = 1 THEN d.gastos_estimados ELSE k.gastos_estimados END,
                        activos_declarados = CASE WHEN sinMontos.v = 1 THEN d.activos_declarados ELSE k.activos_declarados END,
                        pasivos_declarados = CASE WHEN sinMontos.v = 1 THEN d.pasivos_declarados ELSE k.pasivos_declarados END,
                        patrimonio_declarado = CASE WHEN sinMontos.v = 1 THEN d.patrimonio_declarado ELSE k.patrimonio_declarado END,
                        id_moneda_balance = CASE WHEN sinMontos.v = 1 THEN d.id_moneda_balance ELSE k.id_moneda_balance END,
                        fecha_corte = CASE WHEN sinMontos.v = 1 THEN d.fecha_corte ELSE k.fecha_corte END,
                        modificado_por = @por, modificado_en = SYSUTCDATETIME()
                    FROM cli.INTERLOCUTOR_PERFIL_ECONOMICO k
                    JOIN cli.INTERLOCUTOR_PERFIL_ECONOMICO d ON d.id_interlocutor = @d
                    CROSS APPLY (SELECT CASE WHEN k.gastos_estimados IS NULL AND k.activos_declarados IS NULL AND k.pasivos_declarados IS NULL AND k.patrimonio_declarado IS NULL
                                              AND k.id_moneda_balance IS NULL
                                              AND (d.gastos_estimados IS NOT NULL OR d.activos_declarados IS NOT NULL OR d.pasivos_declarados IS NOT NULL OR d.patrimonio_declarado IS NOT NULL)
                                             THEN 1 ELSE 0 END AS v) sinMontos
                    WHERE k.id_interlocutor = @s
                      AND ((k.id_condicion_laboral IS NULL AND d.id_condicion_laboral IS NOT NULL) OR (k.id_ocupacion IS NULL AND d.id_ocupacion IS NOT NULL)
                        OR (k.empleador IS NULL AND d.empleador IS NOT NULL) OR sinMontos.v = 1)
                    """, p, tx);
        }

        // Residencias fiscales: un pais no se repite y solo hay una principal.
        var sinPrincipal = !await c.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_RESIDENCIA_FISCAL WHERE id_interlocutor = @s AND estado = 'Activo' AND es_principal = 1) THEN 1 ELSE 0 END", p, tx);
        var idPrincipalD = sinPrincipal ? await c.ExecuteScalarAsync<long?>(
            "SELECT TOP 1 id_residencia_fiscal FROM cli.INTERLOCUTOR_RESIDENCIA_FISCAL x WHERE x.id_interlocutor = @d AND x.estado = 'Activo' AND x.es_principal = 1 " +
            "AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_RESIDENCIA_FISCAL y WHERE y.id_interlocutor = @s AND y.estado = 'Activo' AND y.id_pais = x.id_pais)", p, tx) : null;
        var residencias = await c.ExecuteAsync(
            """
            UPDATE x SET id_interlocutor = @s, es_principal = 0, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            FROM cli.INTERLOCUTOR_RESIDENCIA_FISCAL x
            WHERE x.id_interlocutor = @d AND x.estado = 'Activo'
              AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_RESIDENCIA_FISCAL y WHERE y.id_interlocutor = @s AND y.estado = 'Activo' AND y.id_pais = x.id_pais)
            """, p, tx);
        if (idPrincipalD is not null)
            await c.ExecuteAsync("UPDATE cli.INTERLOCUTOR_RESIDENCIA_FISCAL SET es_principal = 1 WHERE id_residencia_fiscal = @id AND id_interlocutor = @s", new { id = idPrincipalD.Value, s = idS }, tx);

        var ingresos = await c.ExecuteAsync(
            """
            UPDATE x SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            FROM cli.INTERLOCUTOR_INGRESO x
            WHERE x.id_interlocutor = @d AND x.estado = 'Activo'
              AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_INGRESO y WHERE y.id_interlocutor = @s AND y.estado = 'Activo' AND y.id_tipo_ingreso = x.id_tipo_ingreso
                              AND y.monto = x.monto AND y.id_moneda = x.id_moneda AND y.id_periodicidad = x.id_periodicidad)
            """, p, tx);
        var origenes = await c.ExecuteAsync(
            """
            UPDATE x SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            FROM cli.INTERLOCUTOR_ORIGEN x
            WHERE x.id_interlocutor = @d AND x.estado = 'Activo'
              AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_ORIGEN y WHERE y.id_interlocutor = @s AND y.estado = 'Activo' AND y.clase = x.clase AND y.id_origen = x.id_origen)
            """, p, tx);
        var transacciones = await c.ExecuteAsync(
            """
            UPDATE x SET id_interlocutor = @s, modificado_por = @por, modificado_en = SYSUTCDATETIME()
            FROM cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL x
            WHERE x.id_interlocutor = @d AND x.estado = 'Activo'
              AND NOT EXISTS (SELECT 1 FROM cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL y WHERE y.id_interlocutor = @s AND y.estado = 'Activo'
                              AND y.id_tipo_transaccion = x.id_tipo_transaccion AND y.id_frecuencia = x.id_frecuencia
                              AND ISNULL(y.monto_minimo, -1) = ISNULL(x.monto_minimo, -1) AND ISNULL(y.monto_maximo, -1) = ISNULL(x.monto_maximo, -1) AND ISNULL(y.id_moneda, 0) = ISNULL(x.id_moneda, 0)
                              AND ISNULL(y.id_pais_origen, 0) = ISNULL(x.id_pais_origen, 0) AND ISNULL(y.id_pais_destino, 0) = ISNULL(x.id_pais_destino, 0))
            """, p, tx);
        return new(economico, residencias, ingresos, origenes, transacciones);
    }
}
