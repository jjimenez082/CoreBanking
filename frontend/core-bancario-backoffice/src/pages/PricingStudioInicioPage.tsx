import { TileApp } from "../components/TileApp";
import { usePermisos } from "../contexts/PermisosContext";
import { ZONAS_PRICING as ZONAS } from "../aplicaciones";

/** Launchpad de Pricing Studio: cada mosaico se muestra solo con su permiso (la API igual lo exige). */
export function PricingStudioInicioPage() {
  const { tienePermiso } = usePermisos();
  const zonas = ZONAS.map((z) => ({ ...z, tiles: z.tiles.filter((t) => !t.permiso || tienePermiso(t.permiso)) })).filter((z) => z.tiles.length > 0);

  return (
    <div className="lp">
      <h1>Pricing Studio</h1>
      {zonas.map((z) => (
        <section key={z.titulo} className="lp__zona">
          <h2>{z.titulo}</h2>
          <div className="lp__fila">
            {z.tiles.map((t) => (
              <TileApp key={t.titulo} tile={t} />
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}
