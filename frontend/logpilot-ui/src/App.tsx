import { Navigate, Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import DashboardPage from './pages/DashboardPage';
import LogsPage from './pages/LogsPage';
import ScenariosPage from './pages/ScenariosPage';
import IncidentsPage from './pages/IncidentsPage';
import AssistantPage from './pages/AssistantPage';
import KnowledgePage from './pages/KnowledgePage';

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="assistant" element={<AssistantPage />} />
        <Route path="logs" element={<LogsPage />} />
        <Route path="incidents" element={<IncidentsPage />} />
        <Route path="knowledge" element={<KnowledgePage />} />
        <Route path="scenarios" element={<ScenariosPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
