using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Restricciones por tipo de bloqueo. Cada tipo de bloqueo (KYC, Cumplimiento, Fraude...) define que acciones impide mientras el cliente tenga un bloqueo
/// vigente de ese tipo: por ejemplo KYC solo impide abrir productos nuevos, mientras que Fraude impide ademas pagos, retiros, depositos y desembolsos.
/// La matriz se mantiene en Administracion (permiso BLO_RESTRICCIONES, con auditoria) y los modulos de Cuentas y Creditos la consultan con ValidarAccionAsync.
/// Las acciones son una lista de valores (ACCION_RESTRINGIBLE); el codigo de cada accion es el que invoca el modulo operativo.
/// </summary>
public class RestriccionBloqueoRepositorio
{
    public const string ObjetoAdministracion = "BLO_RESTRICCIONES";
    public const string ListaAccion = "ACCION_RESTRINGIBLE";
    public const string Entidad = "TIPO_BLOQUEO";

    // Codigos de accion que invocan los modulos operativos.
    public const string AbrirCuenta = "ABRIR_CUENTA";
    public const string SolicitarCredito = "SOLICITAR_CREDITO";
    public const string Desembolso = "DESEMBOLSO";
    public const string PagoCredito = "PAGO_CREDITO";
    public const string Deposito = "DEPOSITO";
    public const string Retiro = "RETIRO";
    public const string Transferencia = "TRANSFERENCIA";

    private readonly TenantContext _tenant;
    public RestriccionBloqueoRepositorio(TenantContext tenant) { _tenant = tenant; }
    private SqlConnection NuevaConexion() => new(_tenant.ConnectionString);

    public async Task<MatrizRestriccionResponse> ObtenerMatrizAsync()
    {
        using var c = NuevaConexion();
        var acciones = (await c.QueryAsync(
            """
            SELECT i.id_lista_valores_item AS id, i.codigo, i.nombre, i.descripcion FROM gen.LISTA_VALORES_ITEM i
            JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = @lista AND i.estado = 'Activo' ORDER BY ISNULL(i.orden, 2147483647), i.nombre
            """, new { lista = ListaAccion })).Select(x => new AccionRestringibleResponse((long)x.id, (string)x.codigo, (string)x.nombre, (string?)x.descripcion)).ToList();
        var tipos = (await c.QueryAsync(
            """
            SELECT i.id_lista_valores_item AS id, i.codigo, i.nombre, i.descripcion FROM gen.LISTA_VALORES_ITEM i
            JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = @lista AND i.estado = 'Activo' ORDER BY ISNULL(i.orden, 2147483647), i.nombre
            """, new { lista = BloqueoServicio.ListaTipo })).ToList();
        var filas = (await c.QueryAsync("SELECT id_tipo_bloqueo, id_accion FROM cli.TIPO_BLOQUEO_ACCION")).ToList();
        var tiposResp = tipos.Select(t => new TipoBloqueoRestriccionResponse(
            (long)t.id, (string)t.codigo, (string)t.nombre,
            filas.Where(f => (long)f.id_tipo_bloqueo == (long)t.id).Select(f => (long)f.id_accion).ToList(), (string?)t.descripcion)).ToList();
        return new MatrizRestriccionResponse(acciones, tiposResp);
    }

    public async Task<ResultadoOperacion> GuardarMatrizAsync(ActorContext actor, MatrizRestriccionRequest req)
    {
        if (req?.Tipos is null) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Faltan los datos de la matriz.");
        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var acciones = (await c.QueryAsync(
                """
                SELECT i.id_lista_valores_item AS id, i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE t.codigo = @lista AND i.estado = 'Activo'
                """, new { lista = ListaAccion }, tx)).ToDictionary(x => (long)x.id, x => (string)x.nombre);
            var tipos = (await c.QueryAsync(
                """
                SELECT i.id_lista_valores_item AS id, i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE t.codigo = @lista AND i.estado = 'Activo'
                """, new { lista = BloqueoServicio.ListaTipo }, tx)).ToDictionary(x => (long)x.id, x => (string)x.nombre);

            foreach (var t in req.Tipos)
            {
                if (!tipos.ContainsKey(t.IdTipoBloqueo)) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Hay un tipo de bloqueo que no existe o está inactivo; recargue la pantalla.");
                if ((t.IdsAcciones ?? []).Any(a => !acciones.ContainsKey(a))) return ResultadoOperacion.Fallo(400, "datos_invalidos", "Hay una acción que no existe o está inactiva; recargue la pantalla.");
            }

            foreach (var t in req.Tipos)
            {
                var nuevas = (t.IdsAcciones ?? []).Distinct().ToHashSet();
                var actuales = (await c.QueryAsync<long>("SELECT id_accion FROM cli.TIPO_BLOQUEO_ACCION WHERE id_tipo_bloqueo = @id", new { id = t.IdTipoBloqueo }, tx)).ToHashSet();
                if (nuevas.SetEquals(actuales)) continue;
                string Texto(IEnumerable<long> ids) { var l = ids.Select(i => acciones.GetValueOrDefault(i, "(inactiva)")).OrderBy(x => x).ToList(); return l.Count == 0 ? "(ninguna)" : string.Join(", ", l); }
                await c.ExecuteAsync("DELETE FROM cli.TIPO_BLOQUEO_ACCION WHERE id_tipo_bloqueo = @id", new { id = t.IdTipoBloqueo }, tx);
                foreach (var a in nuevas)
                    await c.ExecuteAsync("INSERT INTO cli.TIPO_BLOQUEO_ACCION (id_tipo_bloqueo, id_accion, creado_por) VALUES (@t, @a, @por)", new { t = t.IdTipoBloqueo, a, por = actor.NombreDescriptivo }, tx);
                await Auditoria.CambioAsync(c, tx, actor, Entidad, t.IdTipoBloqueo, $"Acciones restringidas por «{tipos[t.IdTipoBloqueo]}»", Texto(actuales), Texto(nuevas));
            }
            tx.Commit();
            return ResultadoOperacion.Ok();
        }
        catch { tx.Rollback(); throw; }
    }

    // ----- Tipos de bloqueo: el cliente puede definir los suyos --------------------------------------------------------
    // Las acciones restringibles NO se crean desde aqui: cada una tiene implicaciones en los productos y las define el sistema.

    /// <summary>Crea un tipo de bloqueo nuevo (valor de la lista TIPO_BLOQUEO). Nace sin restricciones; se marcan en la matriz.</summary>
    public async Task<ResultadoOperacion> CrearTipoBloqueoAsync(ActorContext actor, TipoBloqueoRequest req)
    {
        var codigo = System.Text.RegularExpressions.Regex.Replace((req?.Codigo ?? "").Trim().ToUpperInvariant(), @"\s+", "_");
        var nombre = (req?.Nombre ?? "").Trim();
        var descripcion = string.IsNullOrWhiteSpace(req?.Descripcion) ? null : req!.Descripcion!.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(codigo, "^[A-Z][A-Z0-9_]{2,49}$"))
            return ResultadoOperacion.Fallo(400, "datos_invalidos", "El código debe tener de 3 a 50 caracteres: letras, números y guion bajo, empezando por letra.");
        if (nombre.Length is < 3 or > 200) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El nombre debe tener entre 3 y 200 caracteres.");
        if (descripcion is { Length: > 500 }) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La descripción no puede superar 500 caracteres.");

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var idLista = await c.ExecuteScalarAsync<long?>("SELECT id_lista_valores_tipo FROM gen.LISTA_VALORES_TIPO WHERE codigo = @lista", new { lista = BloqueoServicio.ListaTipo }, tx);
            if (idLista is null) return ResultadoOperacion.Fallo(500, "lista_no_configurada", "Falta la lista TIPO_BLOQUEO; correr bloqueos_ronda31.sql.");
            var existe = await c.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @idLista AND codigo = @codigo) THEN 1 ELSE 0 END", new { idLista, codigo }, tx);
            if (existe) return ResultadoOperacion.Fallo(409, "codigo_duplicado", $"Ya existe un tipo de bloqueo con código «{codigo}».");
            var id = await c.QuerySingleAsync<long>(
                """
                INSERT INTO gen.LISTA_VALORES_ITEM (id_lista_valores_tipo, codigo, nombre, descripcion, orden, es_sistema, creado_por)
                OUTPUT INSERTED.id_lista_valores_item
                VALUES (@idLista, @codigo, @nombre, @descripcion,
                        (SELECT CASE WHEN MAX(CASE WHEN orden < 99 THEN orden END) IS NULL THEN 1 ELSE MAX(CASE WHEN orden < 99 THEN orden END) + 1 END FROM gen.LISTA_VALORES_ITEM WHERE id_lista_valores_tipo = @idLista), 0, @por)
                """, new { idLista, codigo, nombre, descripcion, por = actor.NombreDescriptivo }, tx);
            await Auditoria.CambioAsync(c, tx, actor, Entidad, id, "Tipo de bloqueo", null, $"Creado: {codigo} · {nombre}");
            tx.Commit();
            return ResultadoOperacion.Ok(id);
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<ResultadoOperacion> ActualizarTipoBloqueoAsync(ActorContext actor, long idTipo, TipoBloqueoRequest req)
    {
        var nombre = (req?.Nombre ?? "").Trim();
        var descripcion = string.IsNullOrWhiteSpace(req?.Descripcion) ? null : req!.Descripcion!.Trim();
        if (nombre.Length is < 3 or > 200) return ResultadoOperacion.Fallo(400, "datos_invalidos", "El nombre debe tener entre 3 y 200 caracteres.");
        if (descripcion is { Length: > 500 }) return ResultadoOperacion.Fallo(400, "datos_invalidos", "La descripción no puede superar 500 caracteres.");

        using var c = NuevaConexion();
        c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var actual = await c.QuerySingleOrDefaultAsync(
                """
                SELECT i.codigo, i.nombre, i.descripcion FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE i.id_lista_valores_item = @idTipo AND t.codigo = @lista
                """, new { idTipo, lista = BloqueoServicio.ListaTipo }, tx);
            if (actual is null) return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el tipo de bloqueo indicado.");
            await c.ExecuteAsync(
                "UPDATE gen.LISTA_VALORES_ITEM SET nombre = @nombre, descripcion = @descripcion, modificado_por = @por, modificado_en = SYSUTCDATETIME() WHERE id_lista_valores_item = @idTipo",
                new { idTipo, nombre, descripcion, por = actor.NombreDescriptivo }, tx);
            await Auditoria.CambioAsync(c, tx, actor, Entidad, idTipo, $"Tipo de bloqueo {(string)actual.codigo}",
                $"{(string)actual.nombre} | {(string?)actual.descripcion}", $"{nombre} | {descripcion}");
            tx.Commit();
            return ResultadoOperacion.Ok(idTipo);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Lo que el cliente puede o no hacer hoy (para la pantalla de bloqueos y para consumo por API).</summary>
    public async Task<RestriccionesClienteResponse?> RestriccionesClienteAsync(long idCompania, long idInterlocutor)
    {
        using var c = NuevaConexion();
        var existe = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania", new { idInterlocutor, idCompania });
        if (existe == 0) return null;
        var acciones = (await c.QueryAsync(
            """
            SELECT i.id_lista_valores_item AS id, i.codigo, i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = @lista AND i.estado = 'Activo' ORDER BY i.orden, i.nombre
            """, new { lista = ListaAccion })).ToList();
        var bloqueos = (await c.QueryAsync(
            """
            SELECT b.id_tipo_bloqueo, it.nombre FROM cli.INTERLOCUTOR_BLOQUEO b JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = b.id_tipo_bloqueo
            WHERE b.id_interlocutor = @idInterlocutor AND b.estado = 'Vigente'
            """, new { idInterlocutor })).ToList();
        var matriz = (await c.QueryAsync("SELECT id_tipo_bloqueo, id_accion FROM cli.TIPO_BLOQUEO_ACCION")).ToList();
        var kycVencido = (await KycVigenciaServicio.ValidarAperturaProductoAsync(c, null, idCompania, idInterlocutor)) is not null;

        var lista = new List<RestriccionClienteItemResponse>();
        foreach (var a in acciones)
        {
            var impiden = bloqueos.Where(b => matriz.Any(m => (long)m.id_tipo_bloqueo == (long)b.id_tipo_bloqueo && (long)m.id_accion == (long)a.id)).Select(b => (string)b.nombre).ToList();
            var codigo = (string)a.codigo;
            var porKyc = kycVencido && codigo is AbrirCuenta or SolicitarCredito;
            string? motivo = null;
            if (impiden.Count > 0) motivo = "Bloqueo: " + string.Join(", ", impiden);
            if (porKyc) motivo = (motivo is null ? "" : motivo + " · ") + "KYC vencido";
            lista.Add(new RestriccionClienteItemResponse(codigo, (string)a.nombre, impiden.Count == 0 && !porKyc, motivo));
        }
        return new RestriccionesClienteResponse(idInterlocutor, bloqueos.Count > 0, lista);
    }

    /// <summary>
    /// Guardia para los modulos operativos: ¿el cliente puede realizar esta accion? Considera los bloqueos vigentes segun la matriz y, para abrir cuentas o solicitar
    /// creditos, el KYC vencido (comparando fechas en el momento). Devuelve null si puede, o el error (409 accion_restringida / kyc_vencido / cliente_fallecido).
    /// </summary>
    public static async Task<ResultadoOperacion?> ValidarAccionAsync(SqlConnection c, SqlTransaction? tx, long idCompania, long idInterlocutor, string codigoAccion)
    {
        // Regla fija: un cliente Fallecido no opera. Solo quedan permitidos los depositos y los pagos de credito (los atiende el proceso de sucesion).
        var estadoCliente = await c.ExecuteScalarAsync<string?>(
            "SELECT estado FROM cli.INTERLOCUTOR WHERE id_interlocutor = @idInterlocutor AND id_compania = @idCompania", new { idInterlocutor, idCompania }, tx);
        if (estadoCliente == "Fallecido" && codigoAccion is not (Deposito or PagoCredito))
            return ResultadoOperacion.Fallo(409, "cliente_fallecido", "El cliente está fallecido; la operación no está permitida (solo depósitos y pagos de crédito, por el proceso de sucesión).");

        var accion = await c.QuerySingleOrDefaultAsync(
            """
            SELECT i.id_lista_valores_item AS id, i.nombre FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
            WHERE t.codigo = @lista AND i.codigo = @codigo
            """, new { lista = ListaAccion, codigo = codigoAccion }, tx);
        if (accion is not null)
        {
            var impiden = (await c.QueryAsync<string>(
                """
                SELECT DISTINCT it.nombre FROM cli.INTERLOCUTOR_BLOQUEO b
                JOIN cli.TIPO_BLOQUEO_ACCION ta ON ta.id_tipo_bloqueo = b.id_tipo_bloqueo AND ta.id_accion = @idAccion
                JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = b.id_tipo_bloqueo
                WHERE b.id_interlocutor = @idInterlocutor AND b.estado = 'Vigente'
                """, new { idAccion = (long)accion.id, idInterlocutor }, tx)).ToList();
            if (impiden.Count > 0)
                return ResultadoOperacion.Fallo(409, "accion_restringida", $"El cliente tiene bloqueos vigentes ({string.Join(", ", impiden)}) que no permiten: {(string)accion.nombre}.");
        }
        if (codigoAccion is AbrirCuenta or SolicitarCredito)
            return await KycVigenciaServicio.ValidarAperturaProductoAsync(c, tx, idCompania, idInterlocutor);
        return null;
    }
}
