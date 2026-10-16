import { useMemo, useState } from "react";
import type { GestionCampoResponse } from "../api/tipos";

const PATRON = /^(ident|dir|cont)\[(\+?)(\d+)\]\.(.+)$/;
const SECCIONES: Record<string, { titulo: string; singular: string }> = {
  ident: { titulo: "Identificaciones", singular: "Identificación" },
  dir: { titulo: "Direcciones", singular: "Dirección" },
  cont: { titulo: "Contactos", singular: "Contacto" },
};

interface Registro {
  clave: string;
  titulo: string;
  baja: boolean;
  campos: GestionCampoResponse[];
}

/** Etiqueta corta: «Identificación nueva #1 · Número» -> «Número». */
const etiquetaCorta = (c: GestionCampoResponse) => {
  const i = c.etiqueta.lastIndexOf(" · ");
  return i >= 0 ? c.etiqueta.slice(i + 3) : c.etiqueta;
};

/** Agrupa los campos de una gestion: datos generales y, por coleccion, una ficha por registro. */
export function agruparCampos(campos: GestionCampoResponse[]) {
  const generales: GestionCampoResponse[] = [];
  const colecciones: Record<string, Map<string, Registro>> = { ident: new Map(), dir: new Map(), cont: new Map() };
  for (const c of campos) {
    const m = PATRON.exec(c.nombreCampo);
    if (!m) { generales.push(c); continue; }
    const [, pref, nuevo, num, col] = m;
    const clave = `${nuevo}${num}`;
    const mapa = colecciones[pref];
    if (!mapa.has(clave)) mapa.set(clave, { clave, titulo: `${SECCIONES[pref].singular} ${nuevo ? "nueva " : ""}#${num}`, baja: false, campos: [] });
    const r = mapa.get(clave)!;
    if (col === "estado" && c.nuevoCrudo === "Inactivo") r.baja = true;
    r.campos.push(c);
  }
  return { generales, colecciones };
}

export function resumenCampos(campos: GestionCampoResponse[]): string {
  const { colecciones } = agruparCampos(campos);
  const partes: string[] = [];
  const conteo = (p: string, uno: string, varios: string) => {
    const n = colecciones[p].size;
    if (n > 0) partes.push(`${n} ${n === 1 ? uno : varios}`);
  };
  conteo("ident", "identificación", "identificaciones");
  conteo("dir", "dirección", "direcciones");
  conteo("cont", "contacto", "contactos");
  return partes.join(" · ");
}

function Tabla({ campos, conAnterior }: { campos: GestionCampoResponse[]; conAnterior: boolean }) {
  return (
    <table className="tabla gestion-datos__tabla">
      <thead>
        <tr><th>Campo</th>{conAnterior && <th>Valor anterior</th>}<th>{conAnterior ? "Valor nuevo" : "Valor"}</th></tr>
      </thead>
      <tbody>
        {campos.map((c) => (
          <tr key={c.nombreCampo}>
            <td>{etiquetaCorta(c)}</td>
            {conAnterior && <td>{c.valorAnterior ?? "—"}</td>}
            <td><strong>{c.valorNuevo ?? "—"}</strong></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Pestaña «Datos» de una gestion: secciones plegables, una ficha por registro y filtro por texto. */
export function GestionDatos({ campos }: { campos: GestionCampoResponse[] }) {
  const [filtro, setFiltro] = useState("");
  const q = filtro.trim().toLowerCase();
  const coincide = (c: GestionCampoResponse) =>
    q === "" || `${c.etiqueta} ${c.valorAnterior ?? ""} ${c.valorNuevo ?? ""}`.toLowerCase().includes(q);

  const { generales, colecciones } = useMemo(() => agruparCampos(campos), [campos]);
  const conAnterior = (cs: GestionCampoResponse[]) => cs.some((c) => c.valorAnterior != null);
  const gen = generales.filter(coincide);

  let hayAlgo = gen.length > 0;
  const secciones = (["ident", "dir", "cont"] as const).map((p) => {
    const regs = [...colecciones[p].values()]
      .map((r) => ({ ...r, campos: r.campos.filter(coincide) }))
      .filter((r) => r.campos.length > 0);
    if (regs.length > 0) hayAlgo = true;
    return { p, regs };
  });

  return (
    <div className="gestion-datos">
      {campos.length > 12 && (
        <input
          type="search" className="gestion-datos__buscar" placeholder="Buscar en los datos de la gestión"
          value={filtro} onChange={(e) => setFiltro(e.target.value)} aria-label="Buscar en los datos"
        />
      )}
      {!hayAlgo && <p className="nota">Sin coincidencias.</p>}

      {gen.length > 0 && (
        <details open className="gestion-datos__seccion">
          <summary>Datos generales <span className="gestion-datos__cuenta">{gen.length}</span></summary>
          <Tabla campos={gen} conAnterior={conAnterior(gen)} />
        </details>
      )}

      {secciones.filter((s) => s.regs.length > 0).map(({ p, regs }) => (
        <details key={p} open className="gestion-datos__seccion">
          <summary>{SECCIONES[p].titulo} <span className="gestion-datos__cuenta">{regs.length}</span></summary>
          <div className="gestion-datos__fichas">
            {regs.map((r) => (
              <div key={r.clave} className={`gestion-datos__ficha${r.baja ? " gestion-datos__ficha--baja" : ""}`}>
                <div className="gestion-datos__ficha-titulo">{r.titulo}{r.baja && <span className="gestion-datos__baja">Baja</span>}</div>
                <Tabla campos={r.campos} conAnterior={conAnterior(r.campos)} />
              </div>
            ))}
          </div>
        </details>
      ))}
    </div>
  );
}
