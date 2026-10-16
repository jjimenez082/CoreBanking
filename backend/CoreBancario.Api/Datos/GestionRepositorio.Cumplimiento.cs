using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestion CLI_KYC: actualizacion del KYC y cumplimiento de un cliente (Persona u Organizacion). Mismo mecanismo que la modificacion de datos:
/// cada diferencia es una fila de apr.GESTION_DETALLE (kyc_* campos 1:1; kmr / kex / scr colecciones) y se aplica con
/// AplicarCambiosInterlocutorAsync al aprobarse. Las evaluaciones PEP / sanciones son siempre registros nuevos (historial inmutable).
/// </summary>
public partial class GestionRepositorio
{
    public const string TipoKyc = "CLI_KYC";
    /// <summary>Carril propio del KYC: su gestion pendiente no choca con la de modificacion de datos ni con la de bloqueos.</summary>
    public const string EntidadKyc = "INTERLOCUTOR_KYC";

    public async Task<CumplimientoResponse?> ObtenerCumplimientoAsync(ActorContext actor, long idInterlocutor)
    {
        using var conexion = NuevaConexion();
        conexion.Open();
        return await CumplimientoServicio.ObtenerAsync(conexion, actor.IdCompania, idInterlocutor);
    }

    public async Task<ResultadoOperacion> SolicitarCumplimientoAsync(ActorContext actor, long idInterlocutor, CumplimientoRequest req)
    {
        // Un usuario de integracion (API) puede enviar una gestion de KYC; queda pendiente de aprobacion como cualquier otra
        // (id_usuario_solicitante nulo y creado_por con el nombre de la integracion, igual que el alta por API).
        if (req is null || req.Kyc is null)
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "Faltan los datos de KYC.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var actual = await conexion.QuerySingleOrDefaultAsync(
                "SELECT estado, tipo_persona FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania",
                new { idInterlocutor, actor.IdCompania }, tx);
            if (actual is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el cliente indicado.");
            if ((string)actual.tipo_persona == "Grupo")
                return ResultadoOperacion.Fallo(409, "grupo_no_aplica", "El KYC de un grupo se gestiona por sus miembros; por ahora solo aplica a personas y organizaciones.");
            if ((string)actual.estado != "Activo")
                return ResultadoOperacion.Fallo(409, "cliente_no_activo", $"El cliente esta '{(string)actual.estado}' y no se puede modificar.");

            var llave = idInterlocutor.ToString(CultureInfo.InvariantCulture);
            var pendiente = await conexion.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id_gestion FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
                new { e = EntidadKyc, llave, p = EstadoPendiente }, tx);
            if (pendiente is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"El cliente ya tiene la gestion #{pendiente} pendiente de aprobacion; debe resolverse antes de solicitar otro cambio.");
            var fusionPend = await FusionServicio.GestionPendienteAsync(conexion, tx, actor.IdCompania, idInterlocutor);
            if (fusionPend is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente", $"El cliente esta en la gestion {fusionPend} pendiente de aprobacion; debe resolverse antes de solicitar otro cambio.");

            // La proxima revision es automatica: se ignora la que venga en la solicitud y se calcula con la ultima revision y la frecuencia del riesgo.
            var kycActual = await KycVigenciaServicio.LeerAsync(conexion, tx, idInterlocutor);
            var hoyCompania = await KycVigenciaServicio.HoyAsync(conexion, tx, actor.IdCompania);
            var parametrosKyc = await KycVigenciaServicio.LeerParametrosAsync(conexion, tx, actor.IdCompania);
            var codigoRiesgo = await KycVigenciaServicio.CodigoRiesgoAsync(conexion, tx, req.Kyc.IdClasificacionRiesgo);
            var proximaCalculada = KycVigenciaServicio.CalcularProxima(req.Kyc.FechaUltimaRevision, kycActual?.Ultima, codigoRiesgo, kycActual?.Proxima, parametrosKyc, hoyCompania);
            req = req with { Kyc = req.Kyc with { FechaProximaRevision = proximaCalculada } };

            var vigenteCumpl = await CumplimientoServicio.LeerVigenteAsync(conexion, tx, idInterlocutor);
            var errores = await CumplimientoServicio.ValidarAsync(conexion, tx, req, vigenteCumpl);
            if (errores.Count > 0) return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

            var cambios = new List<Cambio>();
            string? Id(long? v) => v?.ToString(CultureInfo.InvariantCulture);

            // --- KYC (1:1) ---
            var k = req.Kyc;
            var deseado = new Dictionary<string, string?>
            {
                ["kyc_id_estado_kyc"] = Id(k.IdEstadoKyc),
                ["kyc_id_nivel_dd"] = Id(k.IdNivelDebidaDiligencia),
                ["kyc_id_riesgo"] = Id(k.IdClasificacionRiesgo),
                ["kyc_fecha_ultima_revision"] = Fecha(k.FechaUltimaRevision),
                ["kyc_fecha_proxima_revision"] = Fecha(k.FechaProximaRevision),
                ["kyc_id_segmento_cliente"] = Id(k.IdSegmentoCliente),
                ["kyc_id_segmento_pricing"] = Id(k.IdSegmentoPricing),
                ["kyc_id_segmento_riesgo"] = Id(k.IdSegmentoRiesgo),
            };
            var vigente = await LeerUnoAUnoAsync(conexion, tx, idInterlocutor, TablaKyc);
            foreach (var kv in deseado)
                if (Norm(vigente.GetValueOrDefault(kv.Key)) != Norm(kv.Value)) cambios.Add(new Cambio(kv.Key, vigente.GetValueOrDefault(kv.Key), kv.Value));

            // --- Motivos de riesgo: el conjunto deseado contra los vigentes ---
            var colMotivo = ColeccionPorPrefijo("kmr");
            var motivosVigentes = await LeerVigentesAsync(conexion, tx, colMotivo, idInterlocutor);
            var deseadosMotivos = (req.IdsMotivosRiesgo ?? []).Distinct().Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();
            foreach (var (id, fila) in motivosVigentes)
                if (!deseadosMotivos.Contains(fila["id_motivo"] ?? ""))
                    cambios.Add(new Cambio($"kmr[{id}].estado", "Activo", "Inactivo"));
            var yaTiene = motivosVigentes.Values.Select(f => f["id_motivo"]).ToHashSet();
            var nm = 0;
            foreach (var m in (req.IdsMotivosRiesgo ?? []).Distinct().Select(x => x.ToString(CultureInfo.InvariantCulture)).Where(m => !yaTiene.Contains(m)))
                cambios.Add(new Cambio($"kmr[+{++nm}].id_motivo", null, m));

            // --- Referencias de expediente ---
            var colExp = ColeccionPorPrefijo("kex");
            var expVigentes = await LeerVigentesAsync(conexion, tx, colExp, idInterlocutor);
            var expPedidos = req.Expedientes ?? [];
            foreach (var x in expPedidos.Where(x => x.Id is not null))
                if (!expVigentes.ContainsKey(x.Id!.Value))
                    return ResultadoOperacion.Fallo(400, "registro_inexistente", $"Referencia de expediente #{x.Id} no existe o ya fue dada de baja; recargue la pantalla.");
            foreach (var (id, fila) in expVigentes)
            {
                var p = expPedidos.FirstOrDefault(x => x.Id == id);
                if (p is null) { cambios.Add(new Cambio($"kex[{id}].estado", "Activo", "Inactivo")); continue; }
                void Dif(string campo, string? nuevo) { var ant = V(fila.GetValueOrDefault(campo)); if (ant != nuevo) cambios.Add(new Cambio($"kex[{id}].{campo}", ant, nuevo)); }
                Dif("referencia", V(p.Referencia)); Dif("sistema", V(p.Sistema)); Dif("descripcion", V(p.Descripcion));
            }
            var ne = 0;
            foreach (var x in expPedidos.Where(x => x.Id is null))
            {
                ne++;
                cambios.Add(new Cambio($"kex[+{ne}].referencia", null, V(x.Referencia)));
                if (V(x.Sistema) is { } s) cambios.Add(new Cambio($"kex[+{ne}].sistema", null, s));
                if (V(x.Descripcion) is { } d) cambios.Add(new Cambio($"kex[+{ne}].descripcion", null, d));
            }

            // --- Evaluaciones nuevas (historial: solo altas) ---
            var nv = 0;
            foreach (var ev in req.NuevasEvaluaciones ?? [])
            {
                nv++;
                cambios.Add(new Cambio($"scr[+{nv}].tipo", null, ev.Tipo));
                cambios.Add(new Cambio($"scr[+{nv}].id_resultado", null, Id(ev.IdResultado)));
                cambios.Add(new Cambio($"scr[+{nv}].fecha_screening", null, Fecha(ev.Fecha)));
                cambios.Add(new Cambio($"scr[+{nv}].fuente", null, V(ev.Fuente)));
                if (V(ev.Observacion) is { } o) cambios.Add(new Cambio($"scr[+{nv}].observacion", null, o));
            }

            if (cambios.Count == 0)
                return ResultadoOperacion.Fallo(400, "sin_cambios", "No hay diferencias con los datos actuales.");

            var idTipoGestion = await conexion.ExecuteScalarAsync<long?>(
                "SELECT id_tipo_gestion FROM apr.TIPO_GESTION WHERE codigo = @c AND estado = 'Activo'", new { c = TipoKyc }, tx);
            if (idTipoGestion is null)
                return ResultadoOperacion.Fallo(500, "tipo_gestion_no_configurado", $"Falta el tipo de gestion {TipoKyc}; correr kyc_cumplimiento_ronda30.sql.");

            string numeroGestion;
            try { numeroGestion = await _numeracion.SiguienteAsync(_tenantContext.ConnectionString, actor.IdCompania, "GESTION"); }
            catch (NumeracionException ex) { tx.Rollback(); return ResultadoOperacion.Fallo(409, ex.Codigo, ex.Message); }

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, numero, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio, llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @numeroGestion, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, @comentario, @creadoPor)
                """,
                new
                {
                    numeroGestion, actor.IdCompania, idTipoGestion, estado = EstadoPendiente, idUsuario = actor.IdUsuario,
                    entidad = EntidadKyc, llave, comentario = V(req.Comentario), creadoPor = actor.NombreDescriptivo
                }, tx);
            foreach (var c in cambios)
                await conexion.ExecuteAsync(
                    "INSERT INTO apr.GESTION_DETALLE (id_gestion, nombre_campo, valor_anterior, valor_nuevo) VALUES (@idGestion, @campo, @anterior, @nuevo)",
                    new { idGestion, campo = c.Campo, anterior = c.Anterior, nuevo = c.Nuevo }, tx);

            var inicio = await MotorAprobacion.IniciarAsync(conexion, tx, actor, idGestion, idTipoGestion.Value);
            var errorDevuelta = await CerrarDevueltaAsync(conexion, tx, actor, req.IdGestionDevuelta, idGestion, EntidadKyc, llave);
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
            tx.Rollback();
            return ResultadoOperacion.Fallo(409, "gestion_pendiente", "El cliente ya tiene una gestion pendiente de aprobacion.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
