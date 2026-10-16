import type { Dispatch, SetStateAction } from "react";
import { Campo, SelectCat } from "../components/CamposWizard";
import {
  cxVacia, ingVacio, monedaPorDefecto, ptVacio, rfVacia, nuevaClave,
  type CatalogosCliente, type ErroresPaso, type FilaCx, type FilaIng, type FilaOrig, type FilaPT, type FilaRF, type FormCliente, type PerfilEco, type PerfilForm,
} from "./formCliente";

/**
 * Paso «Perfil financiero» del asistente de cliente (persona y organizacion): residencia fiscal, perfil economico, ingresos,
 * origen de fondos y de patrimonio, y perfil transaccional esperado. Todo opcional salvo lo que exija un rol
 * (Administracion › Requisitos por rol); lo obligatorio se marca con asterisco.
 */
interface Props {
  form: FormCliente;
  setForm: Dispatch<SetStateAction<FormCliente>>;
  cat: CatalogosCliente;
  errores: ErroresPaso;
  pide: (codigo: string) => boolean;
}

function SelectMoneda({ cat, valor, onCambiar }: { cat: CatalogosCliente; valor: number; onCambiar: (id: number) => void }) {
  return (
    <select value={valor} onChange={(e) => onCambiar(Number(e.target.value))}>
      <option value={0}>Seleccione...</option>
      {cat.monedas.map((m) => (<option key={m.id} value={m.id}>{m.codigo} · {m.nombre}</option>))}
    </select>
  );
}

const Monto = ({ valor, onCambiar, placeholder }: { valor: string; onCambiar: (v: string) => void; placeholder?: string }) => (
  <input type="text" inputMode="decimal" placeholder={placeholder ?? "0.00"} value={valor} onChange={(e) => onCambiar(e.target.value)} />
);

export function PerfilFinancieroPaso({ form, setForm, cat, errores, pide }: Props) {
  const esOrg = form.tipoPersona === "Juridica";
  const p = form.perfil;
  const mon = monedaPorDefecto(cat);

  const setPerfil = (fn: (x: PerfilForm) => PerfilForm) => setForm((f) => ({ ...f, perfil: fn(f.perfil) }));
  const eco = (cambio: Partial<PerfilEco>) => setPerfil((x) => ({ ...x, economico: { ...x.economico, ...cambio } }));
  const actRf = (clave: string, cambio: Partial<FilaRF>) => setPerfil((x) => ({ ...x, residenciasFiscales: x.residenciasFiscales.map((r) => (r.clave === clave ? { ...r, ...cambio } : r)) }));
  const actIng = (clave: string, cambio: Partial<FilaIng>) => setPerfil((x) => ({ ...x, ingresos: x.ingresos.map((r) => (r.clave === clave ? { ...r, ...cambio } : r)) }));
  const actCx = (clave: string, cambio: Partial<FilaCx>) => setPerfil((x) => ({ ...x, cuentasExternas: x.cuentasExternas.map((r) => (r.clave === clave ? { ...r, ...cambio } : r)) }));
  /** Texto «(total por mes)» según la frecuencia elegida: el rango es lo que se espera mover en ese periodo. */
  const periodo = (t: FilaPT) => {
    const f = cat.frecuencias.find((x) => x.id === t.idFrecuencia)?.nombre;
    return f ? ` (total, ${f.toLowerCase()})` : "";
  };
  const actPt = (clave: string, cambio: Partial<FilaPT>) => setPerfil((x) => ({ ...x, perfilTransaccional: x.perfilTransaccional.map((r) => (r.clave === clave ? { ...r, ...cambio } : r)) }));

  function marcarPrincipal(r: FilaRF) {
    setPerfil((x) => ({ ...x, residenciasFiscales: x.residenciasFiscales.map((y) => ({ ...y, esPrincipal: y.clave === r.clave })) }));
  }
  function alternarOrigen(clase: FilaOrig["clase"], idOrigen: number) {
    setPerfil((x) => {
      const existente = x.origenes.find((o) => o.clase === clase && o.idOrigen === idOrigen);
      return {
        ...x,
        origenes: existente
          ? x.origenes.filter((o) => o !== existente)
          : [...x.origenes, { clave: nuevaClave(), id: null, clase, idOrigen }],
      };
    });
  }
  const sinMonedas = cat.monedas.length === 0;

  const bloqueOrigen = (clase: FilaOrig["clase"], titulo: string, items: typeof cat.origenesFondos, codigoReq: string, claveError: string) => (
    <>
      <div className="wiz__encabezado" style={{ marginTop: 12 }}>
        <div>
          <h2>{titulo}{pide(codigoReq) ? " *" : ""}</h2>
          <div className="nota">Marque todos los que apliquen.</div>
        </div>
      </div>
      {errores[claveError] && <div className="wiz__alerta" role="alert">{errores[claveError]}</div>}
      {items.length === 0 && <div className="wiz__vacio">La lista aún no tiene valores (Administración › Listas de valores).</div>}
      <div className="wiz__rejilla">
        {items.map((i) => {
          const marcado = p.origenes.some((o) => o.clase === clase && o.idOrigen === i.id);
          return (
            <label key={i.id} className="wiz-fila__principal">
              <input type="checkbox" checked={marcado} onChange={() => alternarOrigen(clase, i.id)} /> {i.nombre}
            </label>
          );
        })}
      </div>
    </>
  );

  return (
    <div className="wiz__grupo">
      <div className="wiz__encabezado">
        <div>
          <h2>Residencia fiscal{pide("RF_RESIDENCIA") ? " *" : ""}</h2>
          <div className="nota">
            País(es) donde {esOrg ? "tributa la organización" : "tributa la persona"} y su identificador tributario en cada uno. Este paso es opcional salvo lo que el rol exija.
          </div>
        </div>
        <button type="button" className="boton-secundario"
          onClick={() => setPerfil((x) => ({ ...x, residenciasFiscales: [...x.residenciasFiscales, rfVacia(x.residenciasFiscales.length === 0)] }))}>
          + Agregar residencia fiscal
        </button>
      </div>
      {errores.residenciasFiscales && <div className="wiz__alerta" role="alert">{errores.residenciasFiscales}</div>}
      {p.residenciasFiscales.length === 0 && <div className="wiz__vacio">Sin residencia fiscal registrada.</div>}
      {p.residenciasFiscales.map((r) => (
        <div key={r.clave} className="wiz-fila">
          <div className="wiz__rejilla">
            <Campo etiqueta="País" obligatorio error={errores[`${r.clave}.pais`]}>
              <SelectCat items={cat.paisesDireccion} valor={r.idPais} respaldoNombre={r.paisNombre} onCambiar={(v) => actRf(r.clave, { idPais: v })} />
            </Campo>
            <Campo etiqueta="Identificador tributario" obligatorio={pide("RF_IDENT_TRIBUTARIO")} error={errores[`${r.clave}.ident`]}>
              <input type="text" maxLength={50} placeholder="NIF / TIN / cédula tributaria" value={r.identificadorTributario}
                onChange={(e) => actRf(r.clave, { identificadorTributario: e.target.value })} />
            </Campo>
            <Campo etiqueta="Residente desde">
              <input type="date" value={r.fechaDesde} onChange={(e) => actRf(r.clave, { fechaDesde: e.target.value })} />
            </Campo>
            <Campo etiqueta="Residente hasta" error={errores[`${r.clave}.hasta`]}>
              <input type="date" value={r.fechaHasta} onChange={(e) => actRf(r.clave, { fechaHasta: e.target.value })} />
            </Campo>
          </div>
          <div className="wiz-fila__acciones">
            <label className="wiz-fila__principal">
              <input type="checkbox" checked={r.esPrincipal} onChange={() => marcarPrincipal(r)} /> Principal
            </label>
            <button type="button" className="wiz-enlace wiz-enlace--peligro"
              onClick={() => setPerfil((x) => {
                const resto = x.residenciasFiscales.filter((y) => y.clave !== r.clave);
                if (resto.length > 0 && !resto.some((y) => y.esPrincipal)) resto[0] = { ...resto[0], esPrincipal: true };
                return { ...x, residenciasFiscales: resto };
              })}>Quitar</button>
          </div>
        </div>
      ))}

      <div className="wiz__encabezado" style={{ marginTop: 12 }}>
        <div>
          <h2>Perfil económico</h2>
          <div className="nota">
            {esOrg ? "Activos, pasivos y patrimonio declarados por la organización." : "Situación laboral y montos declarados por la persona."}
            {" "}Los montos van en una sola moneda y con su fecha de corte.
          </div>
        </div>
      </div>
      {sinMonedas && <div className="wiz__aviso">No hay monedas activas; configúrelas en Administración › Monedas para registrar montos.</div>}
      <div className="wiz__rejilla">
        {!esOrg && (
          <>
            <Campo etiqueta="Condición laboral" obligatorio={pide("PF_CONDICION_LABORAL")} error={errores["eco.condicion"]}>
              <SelectCat items={cat.condicionesLaborales} valor={p.economico.idCondicionLaboral} onCambiar={(v) => eco({ idCondicionLaboral: v })} />
            </Campo>
            <Campo etiqueta="Ocupación o profesión" obligatorio={pide("PF_OCUPACION")} error={errores["eco.ocupacion"]}>
              <SelectCat items={cat.ocupaciones} valor={p.economico.idOcupacion} onCambiar={(v) => eco({ idOcupacion: v })} />
            </Campo>
            <Campo etiqueta="Empleador o negocio" obligatorio={pide("PF_EMPLEADOR")} error={errores["eco.empleador"]}>
              <input type="text" maxLength={200} value={p.economico.empleador} onChange={(e) => eco({ empleador: e.target.value })} />
            </Campo>
            <Campo etiqueta="Gastos estimados (mensuales)" obligatorio={pide("PF_GASTOS")} error={errores["eco.gastos"]}>
              <Monto valor={p.economico.gastosEstimados} onCambiar={(v) => eco({ gastosEstimados: v })} />
            </Campo>
          </>
        )}
        <Campo etiqueta="Activos declarados" obligatorio={pide("PF_ACTIVOS")} error={errores["eco.activos"]}>
          <Monto valor={p.economico.activosDeclarados} onCambiar={(v) => eco({ activosDeclarados: v })} />
        </Campo>
        <Campo etiqueta="Pasivos declarados" obligatorio={pide("PF_PASIVOS")} error={errores["eco.pasivos"]}>
          <Monto valor={p.economico.pasivosDeclarados} onCambiar={(v) => eco({ pasivosDeclarados: v })} />
        </Campo>
        {esOrg && (
          <Campo etiqueta="Patrimonio declarado" obligatorio={pide("PF_PATRIMONIO")} error={errores["eco.patrimonio"]}>
            <Monto valor={p.economico.patrimonioDeclarado} onCambiar={(v) => eco({ patrimonioDeclarado: v })} />
          </Campo>
        )}
        <Campo etiqueta="Moneda de los montos" error={errores["eco.moneda"]}>
          <SelectMoneda cat={cat} valor={p.economico.idMonedaBalance} onCambiar={(v) => eco({ idMonedaBalance: v })} />
        </Campo>
        <Campo etiqueta="Fecha de corte" error={errores["eco.corte"]}>
          <input type="date" value={p.economico.fechaCorte} onChange={(e) => eco({ fechaCorte: e.target.value })} />
        </Campo>
      </div>

      <div className="wiz__encabezado" style={{ marginTop: 12 }}>
        <div>
          <h2>Ingresos{pide("PF_INGRESO") ? " *" : ""}</h2>
          <div className="nota">Cada ingreso con su monto por periodo.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => setPerfil((x) => ({ ...x, ingresos: [...x.ingresos, ingVacio(mon)] }))}>
          + Agregar ingreso
        </button>
      </div>
      {errores.ingresos && <div className="wiz__alerta" role="alert">{errores.ingresos}</div>}
      {p.ingresos.length === 0 && <div className="wiz__vacio">Sin ingresos registrados.</div>}
      {p.ingresos.map((i) => (
        <div key={i.clave} className="wiz-fila">
          <div className="wiz__rejilla">
            <Campo etiqueta="Tipo de ingreso" obligatorio error={errores[`${i.clave}.tipo`]}>
              <SelectCat items={cat.tiposIngreso} valor={i.idTipoIngreso} respaldoNombre={i.tipoNombre} onCambiar={(v) => actIng(i.clave, { idTipoIngreso: v })} />
            </Campo>
            <Campo etiqueta="Monto" obligatorio error={errores[`${i.clave}.monto`]}>
              <Monto valor={i.monto} onCambiar={(v) => actIng(i.clave, { monto: v })} />
            </Campo>
            <Campo etiqueta="Moneda" obligatorio error={errores[`${i.clave}.moneda`]}>
              <SelectMoneda cat={cat} valor={i.idMoneda} onCambiar={(v) => actIng(i.clave, { idMoneda: v })} />
            </Campo>
            <Campo etiqueta="Periodicidad" obligatorio error={errores[`${i.clave}.periodicidad`]}>
              <SelectCat items={cat.periodicidades} valor={i.idPeriodicidad} respaldoNombre={i.periodicidadNombre} onCambiar={(v) => actIng(i.clave, { idPeriodicidad: v })} />
            </Campo>
          </div>
          <div className="wiz-fila__acciones">
            <button type="button" className="wiz-enlace wiz-enlace--peligro" onClick={() => setPerfil((x) => ({ ...x, ingresos: x.ingresos.filter((y) => y.clave !== i.clave) }))}>Quitar</button>
          </div>
        </div>
      ))}

      {bloqueOrigen("Fondos", "Origen de fondos", cat.origenesFondos, "PF_ORIGEN_FONDOS", "origenFondos")}
      {bloqueOrigen("Patrimonio", "Origen del patrimonio", cat.origenesPatrimonio, "PF_ORIGEN_PATRIMONIO", "origenPatrimonio")}

      <div className="wiz__encabezado" style={{ marginTop: 12 }}>
        <div>
          <h2>Perfil transaccional esperado{pide("PT_PERFIL") ? " *" : ""}</h2>
          <div className="nota">Qué movimientos se espera que haga el cliente, con qué frecuencia y por qué monto.</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => setPerfil((x) => ({ ...x, perfilTransaccional: [...x.perfilTransaccional, ptVacio(mon)] }))}>
          + Agregar transacción esperada
        </button>
      </div>
      {errores.perfilTransaccional && <div className="wiz__alerta" role="alert">{errores.perfilTransaccional}</div>}
      {p.perfilTransaccional.length === 0 && <div className="wiz__vacio">Sin transacciones esperadas registradas.</div>}
      {p.perfilTransaccional.map((t) => (
        <div key={t.clave} className="wiz-fila">
          <div className="wiz__rejilla">
            <Campo etiqueta="Tipo de transacción" obligatorio error={errores[`${t.clave}.tipo`]}>
              <SelectCat items={cat.tiposTransaccion} valor={t.idTipoTransaccion} respaldoNombre={t.tipoNombre} onCambiar={(v) => actPt(t.clave, { idTipoTransaccion: v })} />
            </Campo>
            <Campo etiqueta="Frecuencia" obligatorio error={errores[`${t.clave}.frecuencia`]}>
              <SelectCat items={cat.frecuencias} valor={t.idFrecuencia} respaldoNombre={t.frecuenciaNombre} onCambiar={(v) => actPt(t.clave, { idFrecuencia: v })} />
            </Campo>
            <Campo etiqueta={`Monto mínimo esperado${periodo(t)}`} error={errores[`${t.clave}.minimo`]}>
              <Monto valor={t.montoMinimo} onCambiar={(v) => actPt(t.clave, { montoMinimo: v })} />
            </Campo>
            <Campo etiqueta={`Monto máximo esperado${periodo(t)}`} error={errores[`${t.clave}.maximo`]}>
              <Monto valor={t.montoMaximo} onCambiar={(v) => actPt(t.clave, { montoMaximo: v })} />
            </Campo>
            <Campo etiqueta="Moneda" error={errores[`${t.clave}.moneda`]}>
              <SelectMoneda cat={cat} valor={t.idMoneda} onCambiar={(v) => actPt(t.clave, { idMoneda: v })} />
            </Campo>
            <Campo etiqueta="País de origen de los fondos">
              <SelectCat items={cat.paisesDireccion} valor={t.idPaisOrigen} onCambiar={(v) => actPt(t.clave, { idPaisOrigen: v })} />
            </Campo>
            <Campo etiqueta="País de destino">
              <SelectCat items={cat.paisesDireccion} valor={t.idPaisDestino} onCambiar={(v) => actPt(t.clave, { idPaisDestino: v })} />
            </Campo>
          </div>
          <div className="wiz-fila__acciones">
            <button type="button" className="wiz-enlace wiz-enlace--peligro"
              onClick={() => setPerfil((x) => ({ ...x, perfilTransaccional: x.perfilTransaccional.filter((y) => y.clave !== t.clave) }))}>Quitar</button>
          </div>
        </div>
      ))}

      <div className="wiz__encabezado" style={{ marginTop: 12 }}>
        <div>
          <h2>Cuentas en otras instituciones{pide("CX_CUENTA_EXTERNA") ? " *" : ""}</h2>
          <div className="nota">Cuentas bancarias o billeteras que el cliente mantiene en otras entidades (útil para depósitos, retiros y verificación).</div>
        </div>
        <button type="button" className="boton-secundario" onClick={() => setPerfil((x) => ({ ...x, cuentasExternas: [...x.cuentasExternas, cxVacia()] }))}>
          + Agregar cuenta externa
        </button>
      </div>
      {errores.cuentasExternas && <div className="wiz__alerta" role="alert">{errores.cuentasExternas}</div>}
      {p.cuentasExternas.length === 0 && <div className="wiz__vacio">Sin cuentas externas registradas.</div>}
      {p.cuentasExternas.map((x) => (
        <div key={x.clave} className="wiz-fila">
          <div className="wiz__rejilla">
            <Campo etiqueta="Entidad financiera" obligatorio error={errores[`${x.clave}.entidad`]}>
              <SelectCat items={cat.entidadesFinancieras} valor={x.idEntidad} respaldoNombre={x.entidadNombre} onCambiar={(v) => actCx(x.clave, { idEntidad: v })} />
            </Campo>
            <Campo etiqueta="Número de cuenta (IBAN)" obligatorio error={errores[`${x.clave}.numero`]}>
              <input type="text" maxLength={42} placeholder="CR05015202001026284066" value={x.numeroCuenta}
                onChange={(ev) => actCx(x.clave, { numeroCuenta: ev.target.value.toUpperCase() })} />
            </Campo>
            <Campo etiqueta="Tipo de cuenta" obligatorio error={errores[`${x.clave}.tipo`]}>
              <SelectCat items={cat.tiposCuentaExterna} valor={x.idTipoCuenta} respaldoNombre={x.tipoCuentaNombre} onCambiar={(v) => actCx(x.clave, { idTipoCuenta: v })} />
            </Campo>
            <Campo etiqueta="Moneda">
              <SelectMoneda cat={cat} valor={x.idMoneda} onCambiar={(v) => actCx(x.clave, { idMoneda: v })} />
            </Campo>
          </div>
          <div className="wiz-fila__acciones">
            <button type="button" className="wiz-enlace wiz-enlace--peligro"
              onClick={() => setPerfil((y) => ({ ...y, cuentasExternas: y.cuentasExternas.filter((z) => z.clave !== x.clave) }))}>Quitar</button>
          </div>
        </div>
      ))}
    </div>
  );
}
