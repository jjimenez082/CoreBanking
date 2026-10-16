import { TileApp } from "../components/TileApp";
import { usePermisos } from "../contexts/PermisosContext";
import { ZONAS_ADMINISTRACION as ZONAS } from "../zonasAdministracion";

/** Launchpad de Administración: mosaicos por zona, visibles solo con el permiso de cada opción (la API igual lo exige). */
export function AdministracionInicioPage() {
  const { tienePermiso } = usePermisos();
  const zonas = ZONAS.map((z) => ({ ...z, tiles: z.tiles.filter((t) => tienePermiso(t.permiso)) })).filter((z) => z.tiles.length > 0);

  return (
    <div className="lp">
      <h1>Administración</h1>
      {zonas.length === 0 && <p className="nota">Su perfil no tiene aplicaciones habilitadas en este módulo.</p>}
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
