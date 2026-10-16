interface LogoProps {
  /** "claro" = texto/slash claros, para fondo navy. "oscuro" = para fondo blanco. */
  variante?: "claro" | "oscuro";
  /** Texto pequeño debajo del wordmark, ej. "BACKOFFICE". Omitir para no mostrarlo. */
  subtitulo?: string;
  tamano?: "sm" | "md" | "lg";
}

const ALTURAS: Record<NonNullable<LogoProps["tamano"]>, number> = {
  sm: 20,
  md: 28,
  lg: 40,
};

/** Trazo simplificado de América (norte y sur), centrado en un viewBox de 100x100. */
const AMERICA = "M16 30 L24 24 L30 16 L44 12 L58 14 L62 22 L54 28 L60 32 L70 28 L80 34 L74 42 L66 46 L64 54 L68 62 L62 62 L56 56 L48 58 L46 64 L52 68 L48 70 L56 72 L66 72 L76 76 L88 82 L80 92 L66 98 L58 104 L52 94 L48 84 L42 76 L44 70 L36 66 L30 58 L26 48 L22 42 L20 34 Z";

/** La "o" de "Core": un globo sin cuadricula que mira hacia América. Mide .76 em, en el color del texto. */
export function GloboAmerica({ color }: { color: string }) {
  return (
    <svg width="0.76em" height="0.76em" viewBox="0 0 100 100" aria-hidden="true" style={{ margin: "0 0.03em", flex: "none" }}>
      <circle cx="50" cy="50" r="46" fill="none" stroke={color} strokeWidth="7" />
      <path d={AMERICA} transform="translate(50 50) scale(.78) translate(-52 -58)" fill={color} stroke={color} strokeWidth="2.4" strokeLinejoin="round" />
    </svg>
  );
}

/**
 * Wordmark "/BD Core" (la o de Core es un globo que mira a America), siguiendo el mismo lenguaje visual que /assist-me y
 * /om: una barra diagonal (paralelogramo) seguida del nombre del producto.
 * El slash siempre es gris neutro (--color-marca-slash); solo el texto y el
 * subtitulo cambian de color segun el fondo donde se use.
 */
export function Logo({ variante = "oscuro", subtitulo, tamano = "md" }: LogoProps) {
  const altura = ALTURAS[tamano];
  const colorTexto = variante === "claro" ? "#ffffff" : "#0b1145";
  const colorSubtitulo = variante === "claro" ? "rgba(255,255,255,0.65)" : "#8a8fa3";

  return (
    <div className="logo" style={{ display: "inline-flex", flexDirection: "column", gap: 2 }}>
      <div style={{ display: "inline-flex", alignItems: "center", gap: altura * 0.28 }}>
        <svg
          width={altura * 0.42}
          height={altura}
          viewBox="0 0 20 48"
          fill="none"
          aria-hidden="true"
        >
          <polygon points="16,0 20,0 4,48 0,48" fill="#9aa0b4" />
        </svg>
        <span
          style={{
            fontSize: altura * 0.72,
            fontWeight: 700,
            color: colorTexto,
            lineHeight: 1,
            letterSpacing: "-0.01em",
          }}
        >
          BD C
          <GloboAmerica color={colorTexto} />
          re
        </span>
      </div>
      {subtitulo && (
        <span
          style={{
            fontSize: altura * 0.24,
            fontWeight: 600,
            letterSpacing: "0.14em",
            color: colorSubtitulo,
            marginLeft: altura * 0.7,
          }}
        >
          {subtitulo}
        </span>
      )}
    </div>
  );
}
