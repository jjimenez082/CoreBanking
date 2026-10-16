// Tipos de la API de Créditos (ronda 42, cimientos).
export interface BaseCalculo { idBaseCalculo: number; codigo: string; nombre: string; descripcion?: string | null; tipoConteo: string; diasAnioFijo?: number | null; usaAnioReal: boolean; estado: string }
export interface Indicador { idIndicador: number; codigo: string; nombre: string; descripcion?: string | null; idTipo: number; tipoNombre: string; monedaCodigo?: string | null; frecuencia: string; estado: string; valorVigente?: number | null; fechaVigente?: string | null }
export interface IndicadorValor { idValor: number; fechaVigencia: string; valor: number; origen: string; idGestion?: number | null; numeroGestion?: string | null; creadoPor: string; creadoEn: string }
export interface Calendario { idCalendario: number; codigo: string; nombre: string; idPais: number; paisNombre: string; lunes: boolean; martes: boolean; miercoles: boolean; jueves: boolean; viernes: boolean; sabado: boolean; domingo: boolean; esDefecto: boolean; estado: string }
export interface CategoriaRegulatoria { idCategoria: number; idPaisRegulador: number; paisNombre: string; codigo: string; nombre: string; descripcion?: string | null; estado: string }
export interface ProductoMoneda { idMoneda: number; codigo: string; nombre: string; esPrincipal: boolean }
export interface ProductoOperativo {
  idProducto: number; codigo: string; nombre: string; descripcion?: string | null; dominio: string; idCategoria: number; categoriaNombre: string;
  idCategoriaRegulatoria?: number | null; categoriaRegulatoriaNombre?: string | null; estrategiaVersionado: string; tipoPersona: string;
  idCalendario?: number | null; calendarioNombre?: string | null; estado: string; monedas: ProductoMoneda[]; cantidadParametros: number; cantidadComerciales: number;
}
export interface ParametroProducto {
  idParametro: number; codigo: string; nombre: string; descripcion?: string | null; grupo: string; tipoDato: string; idListaTipo?: number | null; listaNombre?: string | null; listaCodigo?: string | null;
  minimo?: number | null; maximo?: number | null; obligatorio: boolean; valorDefecto?: string | null; esComponenteEconomico: boolean; tipoCalculoEconomico?: string | null;
  idBaseComponente?: number | null; baseComponenteNombre?: string | null; codigoTipoEvento?: string | null; orden: number; estado: string;
}
export interface ProductoComercial {
  idProducto: number; idProductoOperativo: number; operativoNombre: string; codigo: string; nombre: string; descripcion?: string | null;
  idProductoBase?: number | null; baseNombre?: string | null; estado: string; estrategiaVersionado: string; versionVigente?: number | null; versionEnEdicion?: number | null; versionProgramada?: number | null;
}
export interface VersionProducto { idVersion: number; numero: number; estado: string; desde?: string | null; hasta?: string | null; comentario?: string | null; idGestion?: number | null; numeroGestion?: string | null; estadoGestion?: string | null; creadoPor: string; creadoEn: string }
export interface VersionValor {
  idParametro: number; codigo: string; nombre: string; grupo: string; tipoDato: string; valor?: string | null; valorHeredado?: string | null; valorDefecto?: string | null; obligatorio: boolean;
  minimo?: number | null; maximo?: number | null; idListaTipo?: number | null; listaCodigo?: string | null; esComponenteEconomico: boolean; tipoCalculoEconomico?: string | null; baseComponenteNombre?: string | null;
}
export interface VersionDetalle { version: VersionProducto; valores: VersionValor[] }

export interface IndicadorLoteFila { fila: number; codigo: string; fechaVigencia: string; valor: number; comentario?: string | null }
export interface IndicadorLoteResultado { fila: number; codigo: string; fechaVigencia: string; exito: boolean; idGestion?: number | null; numeroGestion?: string | null; mensaje?: string | null }
