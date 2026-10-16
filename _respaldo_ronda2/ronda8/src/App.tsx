import { NotificacionesPage } from "./pages/NotificacionesPage";
import { Route, Routes } from "react-router-dom";
import { RutaProtegida } from "./components/RutaProtegida";
import { Layout } from "./components/Layout";
import { PermisosProvider } from "./contexts/PermisosContext";
import { DashboardPage } from "./pages/DashboardPage";
import { GestionesPage } from "./pages/GestionesPage";
import { InterlocutoresInicioPage } from "./pages/InterlocutoresInicioPage";
import { InterlocutoresConsultaPage } from "./pages/InterlocutoresConsultaPage";
import { Interlocutor360Page } from "./pages/Interlocutor360Page";
import { ClienteWizardPage } from "./pages/ClienteWizardPage";
import { BorradoresPage } from "./pages/BorradoresPage";
import { SeguridadPage } from "./pages/SeguridadPage";
import { ListasValoresPage } from "./pages/ListasValoresPage";
import { NumeracionPage } from "./pages/NumeracionPage";
import { DivisionTerritorialPage } from "./pages/DivisionTerritorialPage";
import { UnidadesPage } from "./pages/UnidadesPage";
import { UsuariosPage } from "./pages/UsuariosPage";
import { MonedasPage } from "./pages/MonedasPage";
import { TipoCambioPage } from "./pages/TipoCambioPage";
import { FeriadosPage } from "./pages/FeriadosPage";
import { ParametrosPage } from "./pages/ParametrosPage";
import { AuditoriaPage } from "./pages/AuditoriaPage";
import { AdministracionInicioPage } from "./pages/AdministracionInicioPage";
import { IntegracionesPage } from "./pages/IntegracionesPage";

function App() {
  return (
    <RutaProtegida>
      <PermisosProvider>
        <Layout>
          <Routes>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/notificaciones" element={<NotificacionesPage />} />
            <Route path="/gestiones" element={<GestionesPage />} />
            <Route path="/interlocutores" element={<InterlocutoresInicioPage />} />
            <Route path="/interlocutores/consulta" element={<InterlocutoresConsultaPage />} />
            <Route path="/interlocutores/clientes/nuevo" element={<ClienteWizardPage />} />
            <Route path="/interlocutores/clientes/:id/modificar" element={<ClienteWizardPage />} />
            <Route path="/interlocutores/borradores" element={<BorradoresPage />} />
            <Route path="/interlocutores/:id" element={<Interlocutor360Page />} />
            <Route path="/administracion" element={<AdministracionInicioPage />} />
            <Route path="/seguridad" element={<SeguridadPage />} />
            <Route path="/listas-valores" element={<ListasValoresPage />} />
            <Route path="/numeracion" element={<NumeracionPage />} />
            <Route path="/division-territorial" element={<DivisionTerritorialPage />} />
            <Route path="/unidades" element={<UnidadesPage />} />
            <Route path="/usuarios" element={<UsuariosPage />} />
            <Route path="/monedas" element={<MonedasPage />} />
            <Route path="/tipo-cambio" element={<TipoCambioPage />} />
            <Route path="/feriados" element={<FeriadosPage />} />
            <Route path="/parametros" element={<ParametrosPage />} />
            <Route path="/auditoria" element={<AuditoriaPage />} />
            <Route path="/integraciones" element={<IntegracionesPage />} />
          </Routes>
        </Layout>
      </PermisosProvider>
    </RutaProtegida>
  );
}

export default App;
