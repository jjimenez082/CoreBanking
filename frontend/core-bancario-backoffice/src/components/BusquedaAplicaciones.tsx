import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { usePermisos } from "../contexts/PermisosContext";
import { catalogoAplicaciones } from "../aplicaciones";

const sinTildes = (s: string) => s.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();

/** Busca entre las aplicaciones a las que el usuario tiene acceso (por nombre o modulo) y las abre. */
export function BusquedaAplicaciones() {
  const navigate = useNavigate();
  const { tienePermiso } = usePermisos();
  const [texto, setTexto] = useState("");
  const [abierto, setAbierto] = useState(false);
  const contenedor = useRef<HTMLDivElement>(null);

  const accesibles = useMemo(
    () => catalogoAplicaciones().filter((a) => !a.permiso || tienePermiso(a.permiso)),
    [tienePermiso],
  );
  const q = sinTildes(texto.trim());
  const resultados = accesibles.filter((a) => q === "" || sinTildes(`${a.titulo} ${a.detalle}`).includes(q)).slice(0, 12);

  useEffect(() => {
    const fuera = (e: MouseEvent) => {
      if (contenedor.current && !contenedor.current.contains(e.target as Node)) setAbierto(false);
    };
    document.addEventListener("mousedown", fuera);
    return () => document.removeEventListener("mousedown", fuera);
  }, []);

  function ir(ruta: string) {
    setAbierto(false);
    setTexto("");
    navigate(ruta);
  }

  return (
    <div className="busqueda-apps" ref={contenedor}>
      <input
        type="search"
        placeholder="Buscar aplicaciones"
        aria-label="Buscar aplicaciones"
        value={texto}
        onChange={(e) => { setTexto(e.target.value); setAbierto(true); }}
        onFocus={() => setAbierto(true)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && resultados[0]) ir(resultados[0].ruta);
          if (e.key === "Escape") setAbierto(false);
        }}
      />
      {abierto && (
        <div className="busqueda-global__panel">
          <div className="busqueda-global__grupo">Aplicaciones</div>
          {resultados.map((a) => (
            <button key={`${a.ruta}|${a.titulo}`} type="button" className="busqueda-global__resultado" onClick={() => ir(a.ruta)}>
              {a.titulo}
              <span className="busqueda-global__detalle">{a.detalle}</span>
            </button>
          ))}
          {resultados.length === 0 && <div className="busqueda-global__grupo">Sin coincidencias</div>}
        </div>
      )}
    </div>
  );
}
