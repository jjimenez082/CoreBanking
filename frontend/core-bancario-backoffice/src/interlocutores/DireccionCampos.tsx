import { useEffect, useState, type ReactNode } from "react";
import { useApiClient } from "../api/useApiClient";
import type { CatalogoItemResponse, DireccionConfig, DivisionItem, NivelConfig } from "../api/tipos";
import { nivelesVacios, type FilaDir, type NivelFila } from "./formCliente";

type Parche = Partial<FilaDir>;

function Campo({ etiqueta, obligatorio, error, children }: { etiqueta: string; obligatorio?: boolean; error?: string; children: ReactNode }) {
  return (
    <label className={`wiz-campo ${error ? "wiz-campo--error" : ""}`}>
      <span>
        {etiqueta}
        {obligatorio && <span className="wiz-campo__req" aria-hidden="true"> *</span>}
      </span>
      {children}
      {error && <span className="wiz-campo__error">{error}</span>}
    </label>
  );
}

/** Combo de un nivel de lista: sus opciones dependen del nivel anterior elegido. */
function NivelLista({
  idPais, nivel, idPadre, valor, nombreActual, onElegir, onSinOpciones,
}: {
  idPais: number; nivel: NivelConfig; idPadre: number | null; valor: number | null; nombreActual?: string;
  onElegir: (id: number | null, nombre?: string) => void; onSinOpciones: (sin: boolean) => void;
}) {
  const api = useApiClient();
  const [opciones, setOpciones] = useState<DivisionItem[] | null>(null);
  const esperaPadre = nivel.nivel > 1 && idPadre === null;

  useEffect(() => {
    let cancelado = false;
    if (esperaPadre) { setOpciones([]); onSinOpciones(false); return; }
    setOpciones(null);
    api.get<DivisionItem[]>("/api/catalogos/direccion/divisiones", {
      params: { idPais, nivel: nivel.nivel, idPadre: idPadre ?? undefined },
    }).then((r) => {
      if (cancelado) return;
      setOpciones(r.data);
      onSinOpciones(r.data.length === 0);
    }).catch(() => { if (!cancelado) setOpciones([]); });
    return () => { cancelado = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, idPais, nivel.nivel, idPadre]);

  const enLista = valor ? (opciones ?? []).some((o) => o.id === valor) : true;
  return (
    <select
      value={valor ?? 0}
      disabled={esperaPadre || opciones === null}
      onChange={(e) => {
        const id = Number(e.target.value) || null;
        onElegir(id, id ? opciones?.find((o) => o.id === id)?.nombre : undefined);
      }}
    >
      <option value={0}>{esperaPadre ? "Elija primero el nivel anterior" : opciones === null ? "Cargando..." : opciones.length === 0 ? "Sin opciones" : "Seleccione..."}</option>
      {!enLista && valor ? <option value={valor}>{nombreActual ?? valor} (inactivo)</option> : null}
      {(opciones ?? []).map((o) => (<option key={o.id} value={o.id}>{o.nombre}</option>))}
    </select>
  );
}

/**
 * Campos de una direccion: pais, niveles segun la configuracion del pais (listas dependientes o texto),
 * codigo postal y direccion exacta. Al cambiar un nivel se limpian los de abajo.
 */
export function DireccionCampos({
  d, cfg, paises, errores, onCambiar, onSinOpciones, exigirNiveles = true, exigirCp = true,
}: {
  d: FilaDir;
  cfg: DireccionConfig | undefined;
  paises: CatalogoItemResponse[];
  errores: Record<string, string>;
  onCambiar: (parche: Parche) => void;
  onSinOpciones: (clave: string, nivel: number, sin: boolean) => void;
  /** Si el rol exige la división territorial / el código postal (parametrizado por rol). */
  exigirNiveles?: boolean;
  exigirCp?: boolean;
}) {
  const err = (c: string) => errores[`${d.clave}.${c}`];

  const cambiarNivel = (k: number, nuevo: NivelFila) => {
    const niveles = d.niveles.map((n, i) => (i === k ? nuevo : i > k ? { id: null, texto: "" } : n));
    onCambiar({ niveles });
  };

  const enListaPais = d.idPais ? paises.some((p) => p.id === d.idPais) : true;

  return (
    <>
      <div className="wiz__rejilla">
        <Campo etiqueta="País" obligatorio error={err("pais")}>
          <select
            value={d.idPais || 0}
            onChange={(e) => onCambiar({ idPais: Number(e.target.value), niveles: nivelesVacios(), codigoPostal: "" })}
          >
            <option value={0}>Seleccione...</option>
            {!enListaPais && d.idPais ? <option value={d.idPais}>{d.paisNombre ?? d.idPais} (inactivo)</option> : null}
            {paises.map((p) => (<option key={p.id} value={p.id}>{p.nombre}</option>))}
          </select>
        </Campo>

        {cfg && cfg.niveles.map((nv, k) => {
          const fila = d.niveles[k] ?? { id: null, texto: "" };
          const padre = k > 0 ? d.niveles[k - 1]?.id ?? null : null;
          return (
            <Campo key={nv.nivel} etiqueta={nv.nombre} obligatorio={exigirNiveles && nv.esObligatorio} error={err(`n${nv.nivel}`)}>
              {nv.modo === "Lista" ? (
                <NivelLista
                  idPais={d.idPais} nivel={nv} idPadre={padre} valor={fila.id} nombreActual={fila.nombre}
                  onElegir={(id, nombre) => cambiarNivel(k, { id, texto: "", nombre })}
                  onSinOpciones={(sin) => onSinOpciones(d.clave, nv.nivel, sin)}
                />
              ) : (
                <input type="text" maxLength={150} value={fila.texto}
                  onChange={(e) => cambiarNivel(k, { id: null, texto: e.target.value })} />
              )}
            </Campo>
          );
        })}

        {cfg?.etiquetaCodigoPostal && (
          <Campo etiqueta={cfg.etiquetaCodigoPostal} obligatorio={exigirCp && cfg.codigoPostalObligatorio} error={err("cp")}>
            <input type="text" maxLength={20} value={d.codigoPostal} onChange={(e) => onCambiar({ codigoPostal: e.target.value })} />
          </Campo>
        )}
      </div>
      {d.idPais !== 0 && !cfg && <div className="nota">Cargando la estructura de dirección del país...</div>}
      {cfg?.esPorDefecto && <div className="nota">Este país no tiene estructura propia: los datos se capturan como texto libre.</div>}
    </>
  );
}
