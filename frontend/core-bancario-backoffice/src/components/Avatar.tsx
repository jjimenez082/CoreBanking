interface AvatarProps {
  nombre?: string;
}

function iniciales(nombre?: string): string {
  if (!nombre) return "?";
  const partes = nombre.trim().split(/\s+/);
  const primeras = partes.slice(0, 2).map((p) => p[0]?.toUpperCase() ?? "");
  return primeras.join("") || "?";
}

/** Avatar circular con iniciales — no manejamos fotos de perfil en este MVP. */
export function Avatar({ nombre }: AvatarProps) {
  return <div className="avatar">{iniciales(nombre)}</div>;
}
