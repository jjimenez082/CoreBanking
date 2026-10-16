using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using CoreBancario.Api.Modelos;

namespace CoreBancario.Api.Datos;

/// <summary>
/// Perfil financiero de personas y organizaciones: residencia fiscal, perfil economico (1:1), ingresos, origen de fondos/patrimonio y
/// perfil transaccional esperado. Todo es opcional de entrada; lo que se exige lo define cada rol en Administracion › Requisitos por rol
/// (codigos RF_*, PF_*, PT_*). Los datos viajan por la misma gestion que el resto del cliente (alta o modificacion).
/// </summary>
public static class PerfilFinancieroServicio
{
    public const string ListaCondicionLaboral = "CONDICION_LABORAL";
    public const string ListaOcupacion = "OCUPACION";
    public const string ListaTipoIngreso = "TIPO_INGRESO";
    public const string ListaPeriodicidad = "PERIODICIDAD_INGRESO";
    public const string ListaOrigenFondos = "ORIGEN_FONDOS";
    public const string ListaOrigenPatrimonio = "ORIGEN_PATRIMONIO";
    public const string ListaTipoTransaccion = "TIPO_TRANSACCION";
    public const string ListaFrecuencia = "FRECUENCIA_TRANSACCION";
    public const string ListaPais = "PAIS";
    public const string ListaTipoCuentaExterna = "TIPO_CUENTA_EXTERNA";
    public const string ListaEntidadFinanciera = "ENTIDAD_FINANCIERA";

    public const string ClaseFondos = "Fondos";
    public const string ClasePatrimonio = "Patrimonio";
    private const decimal MontoMaximo = 9_999_999_999_999m;

    public static string Texto(decimal? d) => d?.ToString("0.00", CultureInfo.InvariantCulture)!;

    /// <summary>Valida el bloque. dto null = no se envio (modificacion: no se toca, solo se exige si el rol lo pide y se envia).</summary>
    public static async Task<List<string>> ValidarAsync(
        SqlConnection c, SqlTransaction? tx, long idCompania, string tipoPersona, PerfilFinancieroDto? dto, ReglasCampos reglas)
    {
        var e = new List<string>();
        var esOrg = tipoPersona == "Juridica";
        var eco = dto?.Economico;
        var rf = dto?.ResidenciasFiscales ?? [];
        var ing = dto?.Ingresos ?? [];
        var ori = dto?.Origenes ?? [];
        var pt = dto?.PerfilTransaccional ?? [];
        var cx = dto?.CuentasExternas ?? [];

        // --- Obligatoriedad por rol ---
        if (reglas.Pide(ReglasCampos.RfResidencia) && rf.Count == 0) e.Add("Debe registrar al menos una residencia fiscal.");
        if (reglas.Pide(ReglasCampos.RfIdentTributario) && rf.Any(x => string.IsNullOrWhiteSpace(x.IdentificadorTributario))) e.Add("Cada residencia fiscal debe tener su identificador tributario.");
        if (!esOrg)
        {
            if (reglas.Pide(ReglasCampos.PfCondicionLaboral) && eco?.IdCondicionLaboral is null) e.Add("La condición laboral es obligatoria.");
            if (reglas.Pide(ReglasCampos.PfOcupacion) && eco?.IdOcupacion is null) e.Add("La ocupación o profesión es obligatoria.");
            if (reglas.Pide(ReglasCampos.PfEmpleador) && string.IsNullOrWhiteSpace(eco?.Empleador)) e.Add("El empleador o negocio es obligatorio.");
            if (reglas.Pide(ReglasCampos.PfGastos) && eco?.GastosEstimados is null) e.Add("Los gastos estimados son obligatorios.");
        }
        else if (reglas.Pide(ReglasCampos.PfPatrimonio) && eco?.PatrimonioDeclarado is null) e.Add("El patrimonio declarado es obligatorio.");
        if (reglas.Pide(ReglasCampos.PfActivos) && eco?.ActivosDeclarados is null) e.Add("Los activos declarados son obligatorios.");
        if (reglas.Pide(ReglasCampos.PfPasivos) && eco?.PasivosDeclarados is null) e.Add("Los pasivos declarados son obligatorios.");
        if (reglas.Pide(ReglasCampos.PfIngreso) && ing.Count == 0) e.Add("Debe registrar al menos un ingreso.");
        if (reglas.Pide(ReglasCampos.PfOrigenFondos) && !ori.Any(x => x.Clase == ClaseFondos)) e.Add("Debe indicar al menos un origen de fondos.");
        if (reglas.Pide(ReglasCampos.PfOrigenPatrimonio) && !ori.Any(x => x.Clase == ClasePatrimonio)) e.Add("Debe indicar al menos un origen del patrimonio.");
        if (reglas.Pide(ReglasCampos.CxCuentaExterna) && cx.Count == 0) e.Add("Debe registrar al menos una cuenta externa (IBAN).");
        if (reglas.Pide(ReglasCampos.PtPerfil) && pt.Count == 0) e.Add("Debe registrar al menos una transacción esperada en el perfil transaccional.");

        if (dto is null) return e;

        // --- Reglas propias de los datos ---
        if (esOrg && eco is not null && (eco.IdCondicionLaboral is not null || eco.IdOcupacion is not null || !string.IsNullOrWhiteSpace(eco.Empleador) || eco.GastosEstimados is not null))
            e.Add("Condición laboral, ocupación, empleador y gastos solo aplican a personas.");
        if (!esOrg && eco?.PatrimonioDeclarado is not null) e.Add("El patrimonio declarado solo aplica a organizaciones.");
        if (eco is not null)
        {
            if (eco.Empleador is { Length: > 200 }) e.Add("El empleador no puede superar 200 caracteres.");
            foreach (var (valor, nombre) in new (decimal? V, string N)[]
                     { (eco.GastosEstimados, "Los gastos estimados"), (eco.ActivosDeclarados, "Los activos declarados"), (eco.PasivosDeclarados, "Los pasivos declarados"), (eco.PatrimonioDeclarado, "El patrimonio declarado") })
                if (valor is { } v && (v < 0 || v > MontoMaximo)) e.Add($"{nombre} deben ser un monto entre 0 y {MontoMaximo:N0}.");
            var hayMontos = eco.GastosEstimados is not null || eco.ActivosDeclarados is not null || eco.PasivosDeclarados is not null || eco.PatrimonioDeclarado is not null;
            if (hayMontos && eco.IdMonedaBalance is null) e.Add("Indique la moneda de los montos del perfil económico.");
            if (!hayMontos && eco.IdMonedaBalance is not null) e.Add("Indicó una moneda del perfil económico pero ningún monto.");
            if (eco.FechaCorte is { } fc && (fc > DateOnly.FromDateTime(DateTime.UtcNow) || fc.Year < 1900)) e.Add("La fecha de corte no es válida.");
        }

        var n = 0;
        foreach (var r in rf)
        {
            n++;
            if (r.IdentificadorTributario is { Length: > 50 }) e.Add($"Residencia fiscal #{n}: el identificador tributario no puede superar 50 caracteres.");
            if (r.FechaDesde is { } d && r.FechaHasta is { } h && h < d) e.Add($"Residencia fiscal #{n}: la fecha «hasta» es anterior a «desde».");
        }
        if (rf.Select(x => x.IdPais).Distinct().Count() != rf.Count) e.Add("Un mismo país no puede repetirse en las residencias fiscales.");
        if (rf.Count > 0 && rf.Count(x => x.EsPrincipal) != 1) e.Add("Indique una (y solo una) residencia fiscal principal.");

        n = 0;
        foreach (var i in ing)
        {
            n++;
            if (i.Monto < 0 || i.Monto > MontoMaximo) e.Add($"Ingreso #{n}: el monto debe estar entre 0 y {MontoMaximo:N0}.");
        }
        foreach (var g in ori.GroupBy(x => (x.Clase, x.IdOrigen)).Where(g => g.Count() > 1)) e.Add("Un origen de fondos o de patrimonio no puede repetirse.");
        n = 0;
        foreach (var p in pt)
        {
            n++;
            foreach (var (v, nombreMonto) in new (decimal? V, string N)[] { (p.MontoMinimo, "mínimo"), (p.MontoMaximo, "máximo") })
                if (v is { } m && (m < 0 || m > MontoMaximo)) e.Add($"Transacción esperada #{n}: el monto {nombreMonto} debe estar entre 0 y {MontoMaximo:N0}.");
            if ((p.MontoMinimo is null) != (p.MontoMaximo is null)) e.Add($"Transacción esperada #{n}: indique el rango completo (monto mínimo y máximo).");
            else if (p.MontoMinimo is { } mi && p.MontoMaximo is { } ma && ma < mi) e.Add($"Transacción esperada #{n}: el monto máximo no puede ser menor que el mínimo.");
            if (p.MontoMinimo is not null && p.IdMoneda is null) e.Add($"Transacción esperada #{n}: indique la moneda del rango.");
            if (p.MontoMinimo is null && p.MontoMaximo is null && p.IdMoneda is not null) e.Add($"Transacción esperada #{n}: indicó moneda pero no rango de montos.");
        }

        n = 0;
        foreach (var x in cx)
        {
            n++;
            if (string.IsNullOrWhiteSpace(x.NumeroCuenta)) e.Add($"Cuenta externa #{n}: indique el número de cuenta (IBAN).");
            else if (IbanValidador.Validar(x.NumeroCuenta) is { } errorIban) e.Add($"Cuenta externa #{n}: {errorIban}");
        }
        if (cx.GroupBy(x => IbanValidador.Normalizar(x.NumeroCuenta)).Any(g => g.Count() > 1))
            e.Add("Una misma cuenta externa (IBAN) no puede repetirse.");

        // --- Existencia de valores de lista y monedas (una consulta) ---
        var requeridos = new List<(long Id, string Lista, string Descripcion)>();
        void L(long? id, string lista, string desc) { if (id is not null) requeridos.Add((id.Value, lista, desc)); }
        if (eco is not null)
        {
            L(eco.IdCondicionLaboral, ListaCondicionLaboral, "la condición laboral");
            L(eco.IdOcupacion, ListaOcupacion, "la ocupación");
        }
        foreach (var r in rf) L(r.IdPais, ListaPais, "el país de la residencia fiscal");
        foreach (var i in ing) { L(i.IdTipoIngreso, ListaTipoIngreso, "el tipo de ingreso"); L(i.IdPeriodicidad, ListaPeriodicidad, "la periodicidad del ingreso"); }
        foreach (var o in ori)
        {
            if (o.Clase is not (ClaseFondos or ClasePatrimonio)) { e.Add("La clase de origen debe ser Fondos o Patrimonio."); continue; }
            L(o.IdOrigen, o.Clase == ClaseFondos ? ListaOrigenFondos : ListaOrigenPatrimonio, o.Clase == ClaseFondos ? "el origen de fondos" : "el origen del patrimonio");
        }
        foreach (var p in pt)
        {
            L(p.IdTipoTransaccion, ListaTipoTransaccion, "el tipo de transacción"); L(p.IdFrecuencia, ListaFrecuencia, "la frecuencia");
            L(p.IdPaisOrigen, ListaPais, "el país de origen"); L(p.IdPaisDestino, ListaPais, "el país de destino");
        }
        foreach (var x in cx) { L(x.IdTipoCuenta, ListaTipoCuentaExterna, "el tipo de cuenta externa"); L(x.IdEntidad, ListaEntidadFinanciera, "la entidad financiera"); }
        if (requeridos.Count > 0)
        {
            var ids = requeridos.Select(x => x.Id).Distinct().ToList();
            var existentes = (await c.QueryAsync(
                """
                SELECT i.id_lista_valores_item AS id, t.codigo AS lista
                FROM gen.LISTA_VALORES_ITEM i JOIN gen.LISTA_VALORES_TIPO t ON t.id_lista_valores_tipo = i.id_lista_valores_tipo
                WHERE i.estado = 'Activo' AND i.id_lista_valores_item IN @ids
                """, new { ids }, tx)).ToDictionary(x => (long)x.id, x => (string)x.lista);
            foreach (var (id, lista, desc) in requeridos.Distinct())
                if (!existentes.TryGetValue(id, out var l) || l != lista) e.Add($"Valor no válido para {desc}.");
        }
        var monedas = new List<long>();
        if (eco?.IdMonedaBalance is { } mb) monedas.Add(mb);
        monedas.AddRange(ing.Select(x => x.IdMoneda));
        monedas.AddRange(pt.Where(x => x.IdMoneda is not null).Select(x => x.IdMoneda!.Value));
        monedas.AddRange(cx.Where(x => x.IdMoneda is not null).Select(x => x.IdMoneda!.Value));
        if (monedas.Count > 0)
        {
            var distintas = monedas.Distinct().ToList();
            var ok = await c.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM gen.MONEDA WHERE id_compania = @idCompania AND estado = 'Activo' AND id_moneda IN @distintas", new { idCompania, distintas }, tx);
            if (ok != distintas.Count) e.Add("Alguna moneda indicada no existe o está inactiva.");
        }
        return e.Distinct().ToList();
    }

    /// <summary>Guarda el perfil de un cliente recien creado (alta). Solo inserta lo que viene con datos.</summary>
    public static async Task InsertarAsync(SqlConnection c, SqlTransaction tx, long idInterlocutor, PerfilFinancieroDto? dto, string por)
    {
        if (dto is null) return;
        if (dto.Economico is { } eco && (eco.IdCondicionLaboral is not null || eco.IdOcupacion is not null || !string.IsNullOrWhiteSpace(eco.Empleador) || eco.GastosEstimados is not null
            || eco.ActivosDeclarados is not null || eco.PasivosDeclarados is not null || eco.PatrimonioDeclarado is not null || eco.IdMonedaBalance is not null
            || eco.FechaCorte is not null))
        {
            await c.ExecuteAsync(
                """
                INSERT INTO cli.INTERLOCUTOR_PERFIL_ECONOMICO (id_interlocutor, id_condicion_laboral, id_ocupacion, empleador, gastos_estimados, activos_declarados,
                                                               pasivos_declarados, patrimonio_declarado, id_moneda_balance, fecha_corte, creado_por)
                VALUES (@idInterlocutor, @cl, @oc, @emp, @g, @a, @p, @pa, @mb, @fc, @por)
                """,
                new
                {
                    idInterlocutor, cl = eco.IdCondicionLaboral, oc = eco.IdOcupacion, emp = string.IsNullOrWhiteSpace(eco.Empleador) ? null : eco.Empleador.Trim(),
                    g = eco.GastosEstimados, a = eco.ActivosDeclarados, p = eco.PasivosDeclarados, pa = eco.PatrimonioDeclarado, mb = eco.IdMonedaBalance,
                    fc = eco.FechaCorte?.ToDateTime(TimeOnly.MinValue), por
                }, tx);
        }
        foreach (var r in dto.ResidenciasFiscales ?? [])
            await c.ExecuteAsync(
                """
                INSERT INTO cli.INTERLOCUTOR_RESIDENCIA_FISCAL (id_interlocutor, id_pais, identificador_tributario, es_principal, fecha_desde, fecha_hasta, creado_por)
                VALUES (@idInterlocutor, @pais, @ident, @principal, @desde, @hasta, @por)
                """,
                new
                {
                    idInterlocutor, pais = r.IdPais, ident = string.IsNullOrWhiteSpace(r.IdentificadorTributario) ? null : r.IdentificadorTributario.Trim(),
                    principal = r.EsPrincipal, desde = r.FechaDesde?.ToDateTime(TimeOnly.MinValue), hasta = r.FechaHasta?.ToDateTime(TimeOnly.MinValue), por
                }, tx);
        foreach (var i in dto.Ingresos ?? [])
            await c.ExecuteAsync(
                "INSERT INTO cli.INTERLOCUTOR_INGRESO (id_interlocutor, id_tipo_ingreso, monto, id_moneda, id_periodicidad, creado_por) VALUES (@idInterlocutor, @t, @m, @mo, @pe, @por)",
                new { idInterlocutor, t = i.IdTipoIngreso, m = i.Monto, mo = i.IdMoneda, pe = i.IdPeriodicidad, por }, tx);
        foreach (var o in dto.Origenes ?? [])
            await c.ExecuteAsync(
                "INSERT INTO cli.INTERLOCUTOR_ORIGEN (id_interlocutor, clase, id_origen, creado_por) VALUES (@idInterlocutor, @clase, @origen, @por)",
                new { idInterlocutor, clase = o.Clase, origen = o.IdOrigen, por }, tx);
        foreach (var p in dto.PerfilTransaccional ?? [])
            await c.ExecuteAsync(
                """
                INSERT INTO cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL (id_interlocutor, id_tipo_transaccion, id_frecuencia, monto_minimo, monto_maximo, id_moneda, id_pais_origen, id_pais_destino, creado_por)
                VALUES (@idInterlocutor, @t, @f, @mi, @ma, @mo, @po, @pd, @por)
                """,
                new { idInterlocutor, t = p.IdTipoTransaccion, f = p.IdFrecuencia, mi = p.MontoMinimo, ma = p.MontoMaximo, mo = p.IdMoneda, po = p.IdPaisOrigen, pd = p.IdPaisDestino, por }, tx);
        foreach (var x in dto.CuentasExternas ?? [])
            await c.ExecuteAsync(
                "INSERT INTO cli.INTERLOCUTOR_CUENTA_EXTERNA (id_interlocutor, id_entidad, numero_cuenta_externa, id_tipo_cuenta, id_moneda, creado_por) VALUES (@idInterlocutor, @e, @n, @t, @mo, @por)",
                new { idInterlocutor, e = x.IdEntidad, n = IbanValidador.Normalizar(x.NumeroCuenta), t = x.IdTipoCuenta, mo = x.IdMoneda, por }, tx);
    }

    /// <summary>Datos que le faltan al cliente (registros activos) para cumplir lo que piden los roles, incluidas las relaciones requeridas.</summary>
    public static async Task<List<string>> FaltantesAsync(SqlConnection c, SqlTransaction? tx, long idInterlocutor, string tipoPersona, ReglasCampos r)
    {
        var faltan = new List<string>();
        var esOrg = tipoPersona == "Juridica";
        var eco = await c.QuerySingleOrDefaultAsync("SELECT * FROM cli.INTERLOCUTOR_PERFIL_ECONOMICO WHERE id_interlocutor = @idInterlocutor", new { idInterlocutor }, tx);
        static bool Vacio(object? v) => v is null || (v is string s && string.IsNullOrWhiteSpace(s));
        async Task<int> Cuenta(string tabla, string extra = "") => await c.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {tabla} WHERE id_interlocutor = @idInterlocutor AND estado = 'Activo' {extra}", new { idInterlocutor }, tx);

        if (r.Pide(ReglasCampos.RfResidencia) && await Cuenta("cli.INTERLOCUTOR_RESIDENCIA_FISCAL") == 0) faltan.Add("al menos una residencia fiscal");
        if (r.Pide(ReglasCampos.RfIdentTributario) && await Cuenta("cli.INTERLOCUTOR_RESIDENCIA_FISCAL", "AND (identificador_tributario IS NULL OR LTRIM(RTRIM(identificador_tributario)) = N'')") > 0)
            faltan.Add("el identificador tributario de la residencia fiscal");
        if (!esOrg)
        {
            if (r.Pide(ReglasCampos.PfCondicionLaboral) && Vacio((object?)eco?.id_condicion_laboral)) faltan.Add("la condición laboral");
            if (r.Pide(ReglasCampos.PfOcupacion) && Vacio((object?)eco?.id_ocupacion)) faltan.Add("la ocupación");
            if (r.Pide(ReglasCampos.PfEmpleador) && Vacio((object?)eco?.empleador)) faltan.Add("el empleador o negocio");
            if (r.Pide(ReglasCampos.PfGastos) && Vacio((object?)eco?.gastos_estimados)) faltan.Add("los gastos estimados");
        }
        else if (r.Pide(ReglasCampos.PfPatrimonio) && Vacio((object?)eco?.patrimonio_declarado)) faltan.Add("el patrimonio declarado");
        if (r.Pide(ReglasCampos.PfActivos) && Vacio((object?)eco?.activos_declarados)) faltan.Add("los activos declarados");
        if (r.Pide(ReglasCampos.PfPasivos) && Vacio((object?)eco?.pasivos_declarados)) faltan.Add("los pasivos declarados");
        if (r.Pide(ReglasCampos.PfIngreso) && await Cuenta("cli.INTERLOCUTOR_INGRESO") == 0) faltan.Add("al menos un ingreso");
        if (r.Pide(ReglasCampos.PfOrigenFondos) && await Cuenta("cli.INTERLOCUTOR_ORIGEN", "AND clase = N'Fondos'") == 0) faltan.Add("el origen de fondos");
        if (r.Pide(ReglasCampos.PfOrigenPatrimonio) && await Cuenta("cli.INTERLOCUTOR_ORIGEN", "AND clase = N'Patrimonio'") == 0) faltan.Add("el origen del patrimonio");
        if (r.Pide(ReglasCampos.CxCuentaExterna) && await Cuenta("cli.INTERLOCUTOR_CUENTA_EXTERNA") == 0) faltan.Add("al menos una cuenta externa (IBAN)");
        if (r.Pide(ReglasCampos.PtPerfil) && await Cuenta("cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL") == 0) faltan.Add("el perfil transaccional");

        // Relaciones requeridas (solo se exigen al asumir un rol sobre un cliente ya registrado: en el alta aun no pueden existir).
        async Task<bool> TieneRelacion(string codigo) => await c.ExecuteScalarAsync<bool>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM cli.INTERLOCUTOR_RELACION x JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = x.id_tipo_relacion
                WHERE it.codigo = @codigo AND x.fecha_fin IS NULL AND (x.id_interlocutor_origen = @idInterlocutor OR x.id_interlocutor_destino = @idInterlocutor)) THEN 1 ELSE 0 END
            """, new { codigo, idInterlocutor }, tx);
        if (esOrg && r.Pide(ReglasCampos.RelRepresentanteLegal) && !await TieneRelacion("REPRESENTANTE_LEGAL")) faltan.Add("un representante legal (relación)");
        if (esOrg && r.Pide(ReglasCampos.RelBeneficiarioFinal) && !await TieneRelacion("BENEFICIARIO_FINAL")) faltan.Add("al menos un beneficiario final (relación)");
        return faltan;
    }

    public static async Task<PerfilFinancieroResponse> ObtenerAsync(SqlConnection c, long idInterlocutor)
    {
        static DateOnly? A(object? o) => o is DateTime d ? DateOnly.FromDateTime(d) : null;
        var p = new { idInterlocutor };

        var e = await c.QuerySingleOrDefaultAsync(
            """
            SELECT x.id_condicion_laboral, cl.nombre AS condicion, x.id_ocupacion, oc.nombre AS ocupacion, x.empleador, x.gastos_estimados, x.activos_declarados,
                   x.pasivos_declarados, x.patrimonio_declarado, x.id_moneda_balance, mo.codigo AS moneda, x.fecha_corte
            FROM cli.INTERLOCUTOR_PERFIL_ECONOMICO x
            LEFT JOIN gen.LISTA_VALORES_ITEM cl ON cl.id_lista_valores_item = x.id_condicion_laboral
            LEFT JOIN gen.LISTA_VALORES_ITEM oc ON oc.id_lista_valores_item = x.id_ocupacion
            LEFT JOIN gen.MONEDA mo ON mo.id_moneda = x.id_moneda_balance
            WHERE x.id_interlocutor = @idInterlocutor
            """, p);
        PerfilEconomicoResponse? eco = e is null ? null : new PerfilEconomicoResponse(
            (long?)e.id_condicion_laboral, (string?)e.condicion, (long?)e.id_ocupacion, (string?)e.ocupacion, (string?)e.empleador, (decimal?)e.gastos_estimados,
            (decimal?)e.activos_declarados, (decimal?)e.pasivos_declarados, (decimal?)e.patrimonio_declarado, (long?)e.id_moneda_balance, (string?)e.moneda,
            A((object?)e.fecha_corte));

        var rf = (await c.QueryAsync(
            """
            SELECT x.id_residencia_fiscal, x.id_pais, p.nombre AS pais, x.identificador_tributario, x.es_principal, x.fecha_desde, x.fecha_hasta
            FROM cli.INTERLOCUTOR_RESIDENCIA_FISCAL x JOIN gen.LISTA_VALORES_ITEM p ON p.id_lista_valores_item = x.id_pais
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo' ORDER BY x.es_principal DESC, x.id_residencia_fiscal
            """, p)).Select(x => new ResidenciaFiscalResponse((long)x.id_residencia_fiscal, (long)x.id_pais, (string)x.pais, (string?)x.identificador_tributario,
                (bool)x.es_principal, A((object?)x.fecha_desde), A((object?)x.fecha_hasta))).ToList();

        var ing = (await c.QueryAsync(
            """
            SELECT x.id_ingreso, x.id_tipo_ingreso, t.nombre AS tipo, x.monto, x.id_moneda, m.codigo AS moneda, x.id_periodicidad, pe.nombre AS periodicidad
            FROM cli.INTERLOCUTOR_INGRESO x
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_ingreso
            JOIN gen.LISTA_VALORES_ITEM pe ON pe.id_lista_valores_item = x.id_periodicidad
            JOIN gen.MONEDA m ON m.id_moneda = x.id_moneda
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo' ORDER BY x.id_ingreso
            """, p)).Select(x => new IngresoResponse((long)x.id_ingreso, (long)x.id_tipo_ingreso, (string)x.tipo, (decimal)x.monto, (long)x.id_moneda, (string)x.moneda,
                (long)x.id_periodicidad, (string)x.periodicidad)).ToList();

        var ori = (await c.QueryAsync(
            """
            SELECT x.id_origen_registro, x.clase, x.id_origen, it.nombre
            FROM cli.INTERLOCUTOR_ORIGEN x JOIN gen.LISTA_VALORES_ITEM it ON it.id_lista_valores_item = x.id_origen
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo' ORDER BY x.clase, x.id_origen_registro
            """, p)).Select(x => new OrigenResponse((long)x.id_origen_registro, (string)x.clase, (long)x.id_origen, (string)x.nombre)).ToList();

        var pt = (await c.QueryAsync(
            """
            SELECT x.id_perfil_transaccional, x.id_tipo_transaccion, t.nombre AS tipo, x.id_frecuencia, f.nombre AS frecuencia, x.monto_minimo, x.monto_maximo,
                   x.id_moneda, m.codigo AS moneda, x.id_pais_origen, po.nombre AS pais_origen, x.id_pais_destino, pd.nombre AS pais_destino
            FROM cli.INTERLOCUTOR_PERFIL_TRANSACCIONAL x
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_transaccion
            JOIN gen.LISTA_VALORES_ITEM f ON f.id_lista_valores_item = x.id_frecuencia
            LEFT JOIN gen.MONEDA m ON m.id_moneda = x.id_moneda
            LEFT JOIN gen.LISTA_VALORES_ITEM po ON po.id_lista_valores_item = x.id_pais_origen
            LEFT JOIN gen.LISTA_VALORES_ITEM pd ON pd.id_lista_valores_item = x.id_pais_destino
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo' ORDER BY x.id_perfil_transaccional
            """, p)).Select(x => new PerfilTransaccionalResponse((long)x.id_perfil_transaccional, (long)x.id_tipo_transaccion, (string)x.tipo, (long)x.id_frecuencia,
                (string)x.frecuencia, (decimal?)x.monto_minimo, (decimal?)x.monto_maximo, (long?)x.id_moneda, (string?)x.moneda, (long?)x.id_pais_origen, (string?)x.pais_origen,
                (long?)x.id_pais_destino, (string?)x.pais_destino)).ToList();

        var cx = (await c.QueryAsync(
            """
            SELECT x.id_interlocutor_cuenta_externa AS id, x.id_entidad, en.nombre AS entidad, x.numero_cuenta_externa, x.id_tipo_cuenta, t.nombre AS tipo, x.id_moneda, m.codigo AS moneda
            FROM cli.INTERLOCUTOR_CUENTA_EXTERNA x
            JOIN gen.LISTA_VALORES_ITEM t ON t.id_lista_valores_item = x.id_tipo_cuenta
            LEFT JOIN gen.LISTA_VALORES_ITEM en ON en.id_lista_valores_item = x.id_entidad
            LEFT JOIN gen.MONEDA m ON m.id_moneda = x.id_moneda
            WHERE x.id_interlocutor = @idInterlocutor AND x.estado = 'Activo' ORDER BY x.id_interlocutor_cuenta_externa
            """, p)).Select(x => new CuentaExternaResponse((long)x.id, (long)(x.id_entidad ?? 0L), (string?)x.entidad ?? "—", (string)x.numero_cuenta_externa, (long)x.id_tipo_cuenta,
                (string)x.tipo, (long?)x.id_moneda, (string?)x.moneda)).ToList();

        return new PerfilFinancieroResponse(eco, rf, ing, ori, pt, cx);
    }
}
