import { NotificacionesPage } from "./pages/NotificacionesPage";
import { Navigate, Route, Routes, useLocation, useSearchParams } from "react-router-dom";
import { RutaProtegida } from "./components/RutaProtegida";
import { Layout } from "./components/Layout";
import { PermisosProvider } from "./contexts/PermisosContext";
import { DashboardPage } from "./pages/DashboardPage";
import { GestorDocumentalPage } from "./pages/GestorDocumentalPage";
import { DocumentosInicioPage } from "./pages/DocumentosInicioPage";
import { CargarDocumentosPage } from "./pages/CargarDocumentosPage";
import { GestionesPage } from "./pages/GestionesPage";
import { GestionesInicioPage } from "./pages/GestionesInicioPage";
import { GestionesReasignacionPage } from "./pages/GestionesReasignacionPage";
import { InterlocutoresInicioPage } from "./pages/InterlocutoresInicioPage";
import { InterlocutoresConsultaPage } from "./pages/InterlocutoresConsultaPage";
import { Interlocutor360Page } from "./pages/Interlocutor360Page";
import { FusionClientesPage } from "./pages/FusionClientesPage";
import { RolesClientePage } from "./pages/RolesClientePage";
import { RelacionFormPage } from "./pages/RelacionFormPage";
import { TerceroRolPage } from "./pages/TerceroRolPage";
import { RolesRequisitosPage } from "./pages/RolesRequisitosPage";
import { ClienteWizardPage } from "./pages/ClienteWizardPage";
import { GrupoNuevoPage } from "./pages/GrupoNuevoPage";
import { GrupoEditarPage } from "./pages/GrupoEditarPage";
import { CumplimientoPage } from "./pages/CumplimientoPage";
import { BloqueosPage } from "./pages/BloqueosPage";
import { BloqueosClientePage } from "./pages/BloqueosClientePage";
import { ProspectosPage } from "./pages/ProspectosPage";
import { BorradoresPage } from "./pages/BorradoresPage";
import { SeguridadPage } from "./pages/SeguridadPage";
import { ListasValoresPage } from "./pages/ListasValoresPage";
import { NumeracionPage } from "./pages/NumeracionPage";
import { MensajesSistemaPage } from "./pages/MensajesSistemaPage";
import { ReglasAprobacionPage } from "./pages/ReglasAprobacionPage";
import { GruposPage } from "./pages/GruposPage";
import { DivisionTerritorialPage } from "./pages/DivisionTerritorialPage";
import { UnidadesPage } from "./pages/UnidadesPage";
import { UsuariosPage } from "./pages/UsuariosPage";
import { MonedasPage } from "./pages/MonedasPage";
import { PerfilesDatosPage } from "./pages/PerfilesDatosPage";
import { DocumentosAdminPage } from "./pages/DocumentosAdminPage";
import { TipoCambioPage } from "./pages/TipoCambioPage";
import { FeriadosPage } from "./pages/FeriadosPage";
import { PricingStudioInicioPage } from "./pages/PricingStudioInicioPage";
import { ReglasInicioPage } from "./pages/ReglasInicioPage";
import { ReglasCondicionesPage } from "./pages/ReglasCondicionesPage";
import { ReglasAccionesPage } from "./pages/ReglasAccionesPage";
import { ReglasConjuntosPage } from "./pages/ReglasConjuntosPage";
import { ReglasEvaluacionesPage } from "./pages/ReglasEvaluacionesPage";
import { IndicadoresPage } from "./pages/IndicadoresPage";
import { CalendariosPage } from "./pages/CalendariosPage";
import { BasesCalculoPage } from "./pages/BasesCalculoPage";
import { CategoriasRegulatoriasPage } from "./pages/CategoriasRegulatoriasPage";
import { ProductosOperativosPage } from "./pages/ProductosOperativosPage";
import { ProductosComercialesPage } from "./pages/ProductosComercialesPage";
import { ParametrosPage } from "./pages/ParametrosPage";
import { ConsentimientosClientePage } from "./pages/ConsentimientosClientePage";
import { RestriccionesBloqueoPage } from "./pages/RestriccionesBloqueoPage";
import { AuditoriaPage } from "./pages/AuditoriaPage";
import { AdministracionInicioPage } from "./pages/AdministracionInicioPage";
import { IntegracionesPage } from "./pages/IntegracionesPage";

/** /gestiones es el launchpad; los enlaces con ?ver=N (campanita, auditoría) abren ese detalle en la consulta. */
function GestionesRuta() {
  const [params] = useSearchParams();
  const location = useLocation();
  const ver = params.get("ver");
  return ver
    ? <Navigate to={`/gestiones/consulta?ver=${encodeURIComponent(ver)}`} state={location.state} replace />
    : <GestionesInicioPage />;
}

function App() {
  return (
    <RutaProtegida>
      <PermisosProvider>
        <Layout>
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/notificaciones" element={<NotificacionesPage />} />
            <Route path="/documentos" element={<DocumentosInicioPage />} />
            <Route path="/documentos/expediente" element={<GestorDocumentalPage />} />
            <Route path="/documentos/cargar" element={<CargarDocumentosPage />} />
            <Route path="/pricing-studio" element={<PricingStudioInicioPage />} />
            <Route path="/pricing-studio/productos-operativos" element={<ProductosOperativosPage />} />
            <Route path="/pricing-studio/productos-comerciales" element={<ProductosComercialesPage />} />
            <Route path="/pricing-studio/indicadores" element={<IndicadoresPage />} />
            <Route path="/calendarios" element={<CalendariosPage />} />
            <Route path="/pricing-studio/bases-calculo" element={<BasesCalculoPage />} />
            <Route path="/pricing-studio/categorias-regulatorias" element={<CategoriasRegulatoriasPage />} />
            <Route path="/reglas" element={<ReglasInicioPage />} />
            <Route path="/reglas/condiciones" element={<ReglasCondicionesPage />} />
            <Route path="/reglas/acciones" element={<ReglasAccionesPage />} />
            <Route path="/reglas/conjuntos" element={<ReglasConjuntosPage />} />
            <Route path="/reglas/evaluaciones" element={<ReglasEvaluacionesPage />} />
            <Route path="/gestiones" element={<GestionesRuta />} />
            <Route path="/gestiones/consulta" element={<GestionesPage modo="consulta" />} />
            <Route path="/gestiones/aprobacion" element={<GestionesPage modo="aprobacion" />} />
            <Route path="/gestiones/reasignacion" element={<GestionesReasignacionPage />} />
            <Route path="/interlocutores" element={<InterlocutoresInicioPage />} />
            <Route path="/interlocutores/consulta" element={<InterlocutoresConsultaPage />} />
            <Route path="/interlocutores/clientes/nuevo" element={<ClienteWizardPage />} />
            <Route path="/interlocutores/clientes/:id/modificar" element={<ClienteWizardPage />} />
            <Route path="/interlocutores/grupos/nuevo" element={<GrupoNuevoPage />} />
            <Route path="/interlocutores/:id/grupo/editar" element={<GrupoEditarPage />} />
            <Route path="/interlocutores/:id/grupo/disolver" element={<GrupoEditarPage />} />
            <Route path="/interlocutores/bloqueos" element={<BloqueosPage />} />
            <Route path="/interlocutores/:id/bloqueos" element={<BloqueosClientePage />} />
            <Route path="/interlocutores/:id/consentimientos" element={<ConsentimientosClientePage />} />
            <Route path="/interlocutores/:id/cumplimiento" element={<CumplimientoPage />} />
            <Route path="/interlocutores/prospectos" element={<ProspectosPage />} />
            <Route path="/interlocutores/borradores" element={<BorradoresPage />} />
            <Route path="/interlocutores/terceros/alta" element={<TerceroRolPage />} />
            <Route path="/interlocutores/terceros/registro" element={<ClienteWizardPage />} />
            <Route path="/interlocutores/:id/fusion" element={<FusionClientesPage />} />
            <Route path="/interlocutores/:id/roles" element={<RolesClientePage />} />
            <Route path="/interlocutores/:id/relaciones/nueva" element={<RelacionFormPage />} />
            <Route path="/interlocutores/:id/relaciones/:idRel/editar" element={<RelacionFormPage />} />
            <Route path="/interlocutores/:id" element={<Interlocutor360Page />} />
            <Route path="/administracion" element={<AdministracionInicioPage />} />
            <Route path="/seguridad" element={<SeguridadPage />} />
            <Route path="/listas-valores" element={<ListasValoresPage />} />
            <Route path="/numeracion" element={<NumeracionPage />} />
            <Route path="/mensajes" element={<MensajesSistemaPage />} />
            <Route path="/reglas-aprobacion" element={<ReglasAprobacionPage />} />
            <Route path="/grupos" element={<GruposPage />} />
            <Route path="/division-territorial" element={<DivisionTerritorialPage />} />
            <Route path="/unidades" element={<UnidadesPage />} />
            <Route path="/usuarios" element={<UsuariosPage />} />
            <Route path="/monedas" element={<MonedasPage />} />
            <Route path="/perfiles-datos" element={<PerfilesDatosPage />} />
            <Route path="/tipo-cambio" element={<TipoCambioPage />} />
            <Route path="/feriados" element={<FeriadosPage />} />
            <Route path="/parametros" element={<ParametrosPage />} />
            <Route path="/restricciones-bloqueo" element={<RestriccionesBloqueoPage />} />
            <Route path="/roles-requisitos" element={<RolesRequisitosPage />} />
            <Route path="/documentos-admin" element={<DocumentosAdminPage />} />
            <Route path="/auditoria" element={<AuditoriaPage />} />
            <Route path="/integraciones" element={<IntegracionesPage />} />
          </Routes>
        </Layout>
      </PermisosProvider>
    </RutaProtegida>
  );
}

export default App;
