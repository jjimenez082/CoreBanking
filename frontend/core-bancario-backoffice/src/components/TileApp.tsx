import { Link } from "react-router-dom";
import { idDeTile, type Tile } from "../aplicaciones";
import { useFavoritas } from "../api/useFavoritas";

/** Mosaico de launchpad: enlace a la APP + estrella para marcarla como favorita (aparece en el Dashboard). */
export function TileApp({ tile }: { tile: Tile }) {
  const { esFavorita, alternar } = useFavoritas();
  const id = idDeTile(tile);
  const fav = esFavorita(id);
  return (
    <div className="lp-tile-wrap">
      <Link to={tile.ruta!} className="lp-tile">
        <span className="lp-tile__icono" aria-hidden="true">{tile.icono}</span>
        <span className="lp-tile__titulo">{tile.titulo}</span>
        <span className="lp-tile__sub">{tile.sub}</span>
      </Link>
      {id && (
        <button
          type="button"
          className={`lp-tile__fav${fav ? " lp-tile__fav--on" : ""}`}
          aria-pressed={fav}
          aria-label={fav ? `Quitar ${tile.titulo} de favoritas` : `Marcar ${tile.titulo} como favorita`}
          title={fav ? "Quitar de favoritas" : "Marcar como favorita"}
          onClick={() => alternar(id)}
        >
          {fav ? "★" : "☆"}
        </button>
      )}
    </div>
  );
}
