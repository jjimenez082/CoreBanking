import { useEffect, useMemo, useRef, useState } from "react";
import type { ObjetoAutorizacionResponse } from "../api/tipos";

/**
 * Arbol de permisos para editar un rol: categorías (por prefijo del código) que se despliegan y muestran sus permisos.
 * La casilla de la categoría marca/desmarca todos sus permisos (queda "a medias" si solo hay algunos).
 */

const CATEGORIAS: Record<string, string> = {
  CLI: "Interlocutores y clientes",
  GES: "Gestiones y aprobaciones",
  SEG: "Seguridad y usuarios",
  ORG: "Estructura organizativa",
  LOV: "Listas de valores",
  NUM: "Rangos de numeración",
  DIV: "División territorial",
  MON: "Monedas",
  TC: "Tipo de cambio",
  CAL: "Calendario de feriados",
  PAR: "Parámetros generales",
  AUD: "Auditoría",
  INT: "Usuarios de integración",
};

const prefijo = (codigo: string) => codigo.split("_")[0];

interface Props {
  objetos: ObjetoAutorizacionResponse[];
  marcados: Set<string>;
  onCambiar: (nuevo: Set<string>) => void;
}

function CasillaCategoria({ estado, onClick, etiqueta }: { estado: "todos" | "algunos" | "ninguno"; onClick: () => void; etiqueta: string }) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (ref.current) ref.current.indeterminate = estado === "algunos";
  }, [estado]);
  return <input ref={ref} type="checkbox" checked={estado === "todos"} onChange={onClick} aria-label={etiqueta} />;
}

export function ArbolPermisos({ objetos, marcados, onCambiar }: Props) {
  const [abiertas, setAbiertas] = useState<Record<string, boolean>>({});
  const [filtro, setFiltro] = useState("");

  const categorias = useMemo(() => {
    const mapa = new Map<string, ObjetoAutorizacionResponse[]>();
    for (const o of objetos) {
      const k = prefijo(o.codigo);
      if (!mapa.has(k)) mapa.set(k, []);
      mapa.get(k)!.push(o);
    }
    return Array.from(mapa.entries())
      .map(([clave, items]) => ({ clave, nombre: CATEGORIAS[clave] ?? clave, items: items.sort((a, b) => a.codigo.localeCompare(b.codigo)) }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre, "es"));
  }, [objetos]);

  const f = filtro.trim().toLowerCase();
  const visibles = categorias
    .map((c) => ({ ...c, items: f ? c.items.filter((o) => `${o.codigo} ${o.nombre} ${o.descripcion ?? ""} ${c.nombre}`.toLowerCase().includes(f)) : c.items }))
    .filter((c) => c.items.length > 0);

  function alternarPermiso(codigo: string) {
    const nuevo = new Set(marcados);
    if (nuevo.has(codigo)) nuevo.delete(codigo); else nuevo.add(codigo);
    onCambiar(nuevo);
  }

  function alternarCategoria(items: ObjetoAutorizacionResponse[]) {
    const nuevo = new Set(marcados);
    const todos = items.every((o) => nuevo.has(o.codigo));
    items.forEach((o) => (todos ? nuevo.delete(o.codigo) : nuevo.add(o.codigo)));
    onCambiar(nuevo);
  }

  const estaAbierta = (clave: string) => (f ? true : abiertas[clave] ?? false);
  const marcadosTotal = objetos.filter((o) => marcados.has(o.codigo)).length;

  return (
    <div className="arbol-permisos">
      <div className="arbol-permisos__barra">
        <input type="search" placeholder="Buscar permiso" value={filtro} onChange={(e) => setFiltro(e.target.value)} />
        <button type="button" className="boton-secundario" onClick={() => setAbiertas(Object.fromEntries(categorias.map((c) => [c.clave, true])))}>Expandir todo</button>
        <button type="button" className="boton-secundario" onClick={() => setAbiertas({})}>Contraer todo</button>
        <span className="nota">{marcadosTotal} de {objetos.length} permisos seleccionados</span>
      </div>

      {visibles.length === 0 && <p className="nota">Ningún permiso coincide con la búsqueda.</p>}

      <ul className="arbol-permisos__lista">
        {visibles.map((c) => {
          const total = categorias.find((x) => x.clave === c.clave)!.items;
          const n = total.filter((o) => marcados.has(o.codigo)).length;
          const estado = n === 0 ? "ninguno" : n === total.length ? "todos" : "algunos";
          const abierta = estaAbierta(c.clave);
          return (
            <li key={c.clave} className="arbol-permisos__categoria">
              <div className="arbol-permisos__cabecera">
                <button
                  type="button" className="arbol-permisos__flecha" aria-expanded={abierta}
                  aria-label={`${abierta ? "Contraer" : "Desplegar"} ${c.nombre}`}
                  onClick={() => setAbiertas((a) => ({ ...a, [c.clave]: !abierta }))}
                >
                  {abierta ? "▾" : "▸"}
                </button>
                <label>
                  <CasillaCategoria estado={estado} onClick={() => alternarCategoria(total)} etiqueta={`Todos los permisos de ${c.nombre}`} />
                  <strong>{c.nombre}</strong>
                </label>
                <span className="arbol-permisos__conteo">{n}/{total.length}</span>
              </div>
              {abierta && (
                <ul className="arbol-permisos__hijos">
                  {c.items.map((o) => (
                    <li key={o.codigo}>
                      <label title={o.descripcion ?? undefined}>
                        <input type="checkbox" checked={marcados.has(o.codigo)} onChange={() => alternarPermiso(o.codigo)} />
                        <strong>{o.codigo}</strong> — {o.nombre}
                      </label>
                    </li>
                  ))}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
