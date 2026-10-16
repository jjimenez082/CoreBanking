using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Infraestructura;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Gestion de modificacion de un cliente (Persona Fisica) con sus colecciones
/// 1:n. Cada diferencia se guarda como una fila de apr.GESTION_DETALLE con un
/// nombre de campo que dice a que registro se refiere:
///
///   nombre1                              campo del interlocutor / persona fisica
///   ident[12].numero_identificacion      campo de la identificacion 12 (existente)
///   dir[+1].detalle                      campo de la direccion NUEVA numero 1
///   cont[7].estado  Activo -> Inactivo   baja del contacto 7 (baja logica)
///
/// Prefijos: ident = identificaciones, dir = direcciones, cont = contactos.
/// Tabla y columna salen SOLO de las listas blancas de abajo, nunca del usuario.
/// </summary>
public partial class GestionRepositorio
{
    private enum Tipo { Texto, Entero, Fecha, Bit }

    private record Campo(string Columna, Tipo Tipo, string Etiqueta);

    private record Coleccion(string Prefijo, string Tabla, string Pk, string Singular, Dictionary<string, Campo> Campos);

    // Campos del interlocutor y de la persona fisica (1:1).
    private static readonly Dictionary<string, (string Tabla, Campo Campo)> CamposInterlocutor = new()
    {
        ["id_pais_regulador"] = ("cli.INTERLOCUTOR", new("id_pais_regulador", Tipo.Entero, "País regulador")),
        ["id_unidad"] = ("cli.INTERLOCUTOR", new("id_unidad", Tipo.Entero, "Unidad (sucursal)")),
        ["nombre1"] = ("cli.INTERLOCUTOR_FISICA", new("nombre1", Tipo.Texto, "Primer nombre")),
        ["nombre2"] = ("cli.INTERLOCUTOR_FISICA", new("nombre2", Tipo.Texto, "Segundo nombre")),
        ["apellido1"] = ("cli.INTERLOCUTOR_FISICA", new("apellido1", Tipo.Texto, "Primer apellido")),
        ["apellido2"] = ("cli.INTERLOCUTOR_FISICA", new("apellido2", Tipo.Texto, "Segundo apellido")),
        ["fecha_nacimiento"] = ("cli.INTERLOCUTOR_FISICA", new("fecha_nacimiento", Tipo.Fecha, "Fecha de nacimiento")),
    };

    private static readonly Coleccion[] Colecciones =
    [
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
    ];

    private static readonly Regex PatronCampoHijo = new(@"^(ident|dir|cont)\[(\+?)(\d+)\]\.([a-z0-9_]+)$", RegexOptions.Compiled);

    private static Coleccion ColeccionPorPrefijo(string prefijo) => Colecciones.First(c => c.Prefijo == prefijo);

    private static string ExpresionTexto(string columna, Tipo tipo) => tipo switch
    {
        Tipo.Fecha => $"CONVERT(nvarchar(10), {columna}, 23)",
        Tipo.Bit => $"CONVERT(nvarchar(1), {columna})",
        _ => $"CONVERT(nvarchar(1000), {columna})"
    };

    private static object? ValorSql(Tipo tipo, string? texto) => tipo switch
    {
        Tipo.Entero => texto is null ? null : long.Parse(texto, CultureInfo.InvariantCulture),
        Tipo.Fecha => texto is null ? null : DateTime.ParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Tipo.Bit => texto == "1",
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

        var m = PatronCampoHijo.Match(campo);
        if (!m.Success) return campo;

        var col = ColeccionPorPrefijo(m.Groups[1].Value);
        var etiquetaCampo = col.Campos.TryGetValue(m.Groups[4].Value, out var c) ? c.Etiqueta : m.Groups[4].Value;
        var quien = m.Groups[2].Value == "+" ? $"{col.Singular} nueva #{m.Groups[3].Value}" : $"{col.Singular} #{m.Groups[3].Value}";
        return $"{quien} · {etiquetaCampo}";
    }

    internal static string? TextoCampo(string campo, string? valor, Dictionary<long, string> items, Dictionary<long, string> paises, Dictionary<long, string> divisiones, Dictionary<long, string> unidades)
    {
        if (valor is null) return null;

        var nombreColumna = campo;
        var m = PatronCampoHijo.Match(campo);
        if (m.Success) nombreColumna = m.Groups[4].Value;

        if (nombreColumna == "es_principal") return valor == "1" ? "Sí" : "No";
        if (!long.TryParse(valor, out var id)) return valor;

        return nombreColumna switch
        {
            "nivel1_id" or "nivel2_id" or "nivel3_id" or "nivel4_id" => divisiones.TryGetValue(id, out var dv) ? dv : valor,
            "id_unidad" => unidades.TryGetValue(id, out var un) ? un : valor,
            "id_pais" => items.TryGetValue(id, out var pn) ? pn : valor,
            "id_pais_regulador" => paises.TryGetValue(id, out var p) ? p : valor,
            "id_tipo_identificacion" or "id_institucion_emisora" or "id_tipo_direccion" or "id_tipo_contacto"
                => items.TryGetValue(id, out var n) ? n : valor,
            _ => valor
        };
    }

    // ----- Solicitar la modificacion de un cliente -------------------------------------

    private record Cambio(string Campo, string? Anterior, string? Nuevo);

    public async Task<ResultadoOperacion> SolicitarModificacionInterlocutorAsync(
        ActorContext actor, long idInterlocutor, InterlocutorModificacionRequest req)
    {
        if (actor.IdUsuario is null)
            return ResultadoOperacion.Fallo(400, "actor_no_humano", "Solo un usuario nombrado puede solicitar una gestion.");

        using var conexion = NuevaConexion();
        conexion.Open();
        using var tx = conexion.BeginTransaction();
        try
        {
            var actual = await conexion.QuerySingleOrDefaultAsync(
                """
                SELECT i.estado, i.id_pais_regulador, i.id_unidad,
                       f.nombre1, f.nombre2, f.apellido1, f.apellido2, CONVERT(nvarchar(10), f.fecha_nacimiento, 23) AS fecha_nacimiento
                FROM cli.INTERLOCUTOR i
                LEFT JOIN cli.INTERLOCUTOR_FISICA f ON f.id_interlocutor = i.id_interlocutor
                WHERE i.id_interlocutor = @idInterlocutor AND i.id_compania = @idCompania AND i.tipo_persona = 'Fisica'
                """, new { idInterlocutor, actor.IdCompania }, tx);
            if (actual is null)
                return ResultadoOperacion.Fallo(404, "no_encontrado", "No existe el cliente (Persona Fisica) indicado.");

            if ((string)actual.estado != "Activo")
                return ResultadoOperacion.Fallo(409, "cliente_no_activo", $"El cliente esta '{(string)actual.estado}' y no se puede modificar.");

            var llave = idInterlocutor.ToString(CultureInfo.InvariantCulture);

            var pendiente = await conexion.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id_gestion FROM apr.GESTION WHERE entidad_negocio = @e AND llave_registro = @llave AND estado = @p",
                new { e = EntidadInterlocutor, llave, p = EstadoPendiente }, tx);
            if (pendiente is not null)
                return ResultadoOperacion.Fallo(409, "gestion_pendiente",
                    $"El cliente ya tiene la gestion #{pendiente} pendiente de aprobacion; debe resolverse antes de solicitar otro cambio.");

            // Misma validacion que el alta.
            var cat = await InterlocutorRepositorio.CargarCatalogosAsync(conexion, tx);
            var errores = InterlocutorValidador.Validar(
                req.Nombre1, req.Apellido1, req.FechaNacimiento, req.Identificaciones, req.Direcciones, req.Contactos, cat);
            var errorUnidad = await InterlocutorRepositorio.ValidarUnidadAsync(conexion, tx, actor.IdCompania, req.IdUnidad, (long?)actual.id_unidad);
            if (errorUnidad is not null) errores.Add(errorUnidad);
            var paisOk = await conexion.ExecuteScalarAsync<bool>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM gen.PAIS_REGULADOR WHERE id_pais_regulador = @p AND estado = 'Activo') THEN 1 ELSE 0 END",
                new { p = req.IdPaisRegulador }, tx);
            if (!paisOk && req.IdPaisRegulador != (long)actual.id_pais_regulador) errores.Add("El pais regulador no existe o esta inactivo.");
            var (erroresGeo, direccionesNorm) = await DireccionGeografia.ValidarAsync(conexion, tx, req.Direcciones);
            errores.AddRange(erroresGeo);
            if (errores.Count > 0)
                return ResultadoOperacion.Fallo(400, "datos_invalidos", string.Join("\n", errores));

            var cambios = new List<Cambio>();

            // --- Campos del cliente (1:1) ---
            var deseado = new Dictionary<string, string?>
            {
                ["id_pais_regulador"] = req.IdPaisRegulador.ToString(CultureInfo.InvariantCulture),
                ["id_unidad"] = req.IdUnidad?.ToString(CultureInfo.InvariantCulture),
                ["nombre1"] = V(req.Nombre1),
                ["nombre2"] = V(req.Nombre2),
                ["apellido1"] = V(req.Apellido1),
                ["apellido2"] = V(req.Apellido2),
                ["fecha_nacimiento"] = req.FechaNacimiento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
            var vigente = new Dictionary<string, string?>
            {
                ["id_pais_regulador"] = ((long)actual.id_pais_regulador).ToString(CultureInfo.InvariantCulture),
                ["id_unidad"] = ((long?)actual.id_unidad)?.ToString(CultureInfo.InvariantCulture),
                ["nombre1"] = V((string?)actual.nombre1),
                ["nombre2"] = V((string?)actual.nombre2),
                ["apellido1"] = V((string?)actual.apellido1),
                ["apellido2"] = V((string?)actual.apellido2),
                ["fecha_nacimiento"] = (string?)actual.fecha_nacimiento,
            };
            foreach (var kv in deseado)
                if (vigente[kv.Key] != kv.Value) cambios.Add(new Cambio(kv.Key, vigente[kv.Key], kv.Value));

            // --- Colecciones 1:n ---
            var porIdent = req.Identificaciones.Select(i => (i.Id, Campos: new Dictionary<string, string?>
            {
                ["id_tipo_identificacion"] = i.IdTipoIdentificacion.ToString(CultureInfo.InvariantCulture),
                ["numero_identificacion"] = V(i.Numero),
                ["id_institucion_emisora"] = i.IdInstitucionEmisora?.ToString(CultureInfo.InvariantCulture),
                ["fecha_emision"] = Fecha(i.FechaEmision),
                ["fecha_vencimiento"] = Fecha(i.FechaVencimiento),
                ["es_principal"] = Bit(i.EsPrincipal),
            })).ToList();

            var porDir = direccionesNorm.Select(d => (d.Id, Campos: new Dictionary<string, string?>
            {
                ["id_tipo_direccion"] = d.IdTipoDireccion.ToString(CultureInfo.InvariantCulture),
                ["id_pais"] = d.IdPais.ToString(CultureInfo.InvariantCulture),
                ["nivel1_id"] = d.Niveles![0].Id?.ToString(CultureInfo.InvariantCulture),
                ["nivel2_id"] = d.Niveles[1].Id?.ToString(CultureInfo.InvariantCulture),
                ["nivel3_id"] = d.Niveles[2].Id?.ToString(CultureInfo.InvariantCulture),
                ["nivel4_id"] = d.Niveles[3].Id?.ToString(CultureInfo.InvariantCulture),
                ["nivel1_texto"] = V(d.Niveles[0].Texto),
                ["nivel2_texto"] = V(d.Niveles[1].Texto),
                ["nivel3_texto"] = V(d.Niveles[2].Texto),
                ["nivel4_texto"] = V(d.Niveles[3].Texto),
                ["codigo_postal"] = V(d.CodigoPostal),
                ["detalle"] = V(d.Detalle),
                ["detalle2"] = V(d.Detalle2),
                ["detalle3"] = V(d.Detalle3),
                ["es_principal"] = Bit(d.EsPrincipal),
            })).ToList();

            var porCont = (req.Contactos ?? []).Select(c => (c.Id, Campos: new Dictionary<string, string?>
            {
                ["id_tipo_contacto"] = c.IdTipoContacto.ToString(CultureInfo.InvariantCulture),
                ["valor"] = InterlocutorValidador.NormalizarContacto(cat.Buscar(CatalogosInterlocutor.ListaTipoContacto, c.IdTipoContacto)!.Codigo, c.Valor),
                ["es_principal"] = Bit(c.EsPrincipal),
            })).ToList();

            var pedidos = new Dictionary<string, List<(long? Id, Dictionary<string, string?> Campos)>>
            {
                ["ident"] = porIdent, ["dir"] = porDir, ["cont"] = porCont
            };

            foreach (var col in Colecciones)
            {
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

            var idGestion = await conexion.QuerySingleAsync<long>(
                """
                INSERT INTO apr.GESTION (id_compania, id_tipo_gestion, estado, id_usuario_solicitante, entidad_negocio,
                                         llave_registro, comentario, creado_por)
                OUTPUT INSERTED.id_gestion
                VALUES (@idCompania, @idTipoGestion, @estado, @idUsuario, @entidad, @llave, @comentario, @creadoPor)
                """,
                new
                {
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

            await conexion.ExecuteAsync(
                "INSERT INTO apr.GESTION_APROBACION (id_gestion, nivel) VALUES (@idGestion, 1)", new { idGestion }, tx);

            var errorDevuelta = await CerrarDevueltaAsync(conexion, tx, actor, req.IdGestionDevuelta, idGestion, EntidadInterlocutor, llave);
            if (errorDevuelta is not null) return errorDevuelta;

            tx.Commit();
            return ResultadoOperacion.Ok(idGestion);
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
                $"{col.Campos[x.Campo].Etiqueta}: {TextoCampo(x.Campo, x.Valor, catalogos.Items, catalogos.Paises, catalogos.Divisiones, catalogos.Unidades)}"));
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
