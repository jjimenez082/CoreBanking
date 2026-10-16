// Tipos de la API del Motor de reglas (ronda 43).
export interface ReglaContexto { idContexto: number; codigo: string; nombre: string; estado: string }
export interface ReglaAtributo { idAtributo: number; codigo: string; nombre: string; descripcion?: string | null; tipoDato: string; listaCodigo?: string | null; esMultivalor: boolean }
export interface ReglaOperador { idOperador: number; codigo: string; nombre: string; simbolo?: string | null; aridad: number; tiposDato: string[] }
export interface ReglaFuncion { idFuncion: number; codigo: string; nombre: string; descripcion?: string | null; tipoEntrada: string; tipoRetorno: string; cantidadParametros: number }
export interface ReglaCatalogos { contextos: ReglaContexto[]; atributos: ReglaAtributo[]; operadores: ReglaOperador[]; funciones: ReglaFuncion[] }

export interface ReglaCondicion {
  idCondicion: number; codigo: string; nombre: string; descripcion?: string | null; idAtributo: number; atributoCodigo: string; atributoNombre: string;
  idFuncion?: number | null; funcionCodigo?: string | null; parametroFuncion?: string | null; idOperador: number; operadorCodigo: string; origenValor: string;
  valor1?: string | null; valor2?: string | null; valoresLista?: string | null; idAtributoValor?: number | null; atributoValorCodigo?: string | null;
  estado: string; resumen: string; usos: number; congelada: boolean;
}
export interface ReglaAccion {
  idAccion: number; codigo: string; nombre: string; descripcion?: string | null; tipoAccion: string; resultado?: string | null; clave?: string | null; valor?: string | null;
  mensaje?: string | null; estado: string; resumen: string; usos: number; congelada: boolean;
}
export interface ConjuntoReglas {
  idConjunto: number; idContexto: number; contextoCodigo: string; contextoNombre: string; codigo: string; nombre: string; descripcion?: string | null;
  politicaResolucion: string; estado: string; versionVigente?: number | null; versionEnEdicion?: number | null; versionProgramada?: number | null; referencias: number;
}
export interface ConjuntoReferencia { tipoConsumidor: string; idConsumidor: number; descripcion: string }
export interface VersionConjunto { idVersion: number; numero: number; estado: string; desde?: string | null; hasta?: string | null; comentario?: string | null; idGestion?: number | null; numeroGestion?: string | null; estadoGestion?: string | null; creadoPor: string; creadoEn: string }

export interface ReglaMiembroDetalle { idCondicion: number; codigo: string; nombre: string; resumen: string; negar: boolean; estado: string }
export interface ReglaGrupoDetalle { operador: string; miembros: ReglaMiembroDetalle[]; subgrupos: ReglaGrupoDetalle[] }
export interface ReglaAccionDetalle { idAccion: number; codigo: string; nombre: string; resumen: string; estado: string }
export interface ReglaDetalle { idRegla: number; orden: number; codigo: string; nombre: string; descripcion?: string | null; activa: boolean; condiciones?: ReglaGrupoDetalle | null; acciones: ReglaAccionDetalle[] }
export interface ReglasVersionDetalle { version: VersionConjunto; idConjunto: number; contextoCodigo: string; reglas: ReglaDetalle[] }

export interface TrazaCondicion { condicion: string; atributo: string; observado?: string | null; operador: string; esperado?: string | null; negada: boolean; resultado: boolean; error?: string | null }
export interface TrazaRegla { regla: string; nombre: string; cumplida: boolean; condiciones: TrazaCondicion[] }
export interface EvaluacionResultado { decision?: string | null; asignaciones: Record<string, string>; mensajes: string[]; reglasAplicadas: string[]; coincidioAlguna: boolean; traza: TrazaRegla[] }
export interface ReglaEvaluacion { idEvaluacion?: number | null; idVersion: number; numeroVersion: number; modo: string; resultado: EvaluacionResultado }
export interface EvaluacionFila { idEvaluacion: number; idVersion: number; conjuntoCodigo: string; numeroVersion: number; modo: string; referencia?: string | null; decision?: string | null; reglaAplicada?: string | null; evaluadoPor: string; evaluadoEn: string }
export interface EvaluacionDetalle { fila: EvaluacionFila; entrada: string; resultado: string; traza?: string | null }

export interface CasoPrueba { idCaso: number; idConjunto: number; nombre: string; entrada: string; decisionEsperada?: string | null; reglaEsperada?: string | null; estado: string }
export interface CasoPruebaResultado { idCaso: number; nombre: string; paso: boolean; decisionObtenida?: string | null; reglaObtenida?: string | null; decisionEsperada?: string | null; reglaEsperada?: string | null; detalle?: string | null }
export interface CasosEjecutar { idVersion: number; numeroVersion: number; total: number; fallidos: number; casos: CasoPruebaResultado[] }
