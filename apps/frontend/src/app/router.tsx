import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { AppShell } from '../components/layout/AppShell';
import { AgentListPage } from '../features/agents/pages/AgentListPage';
import { AgentCreatePage } from '../features/agents/pages/AgentCreatePage';
import { AgentDetailPage } from '../features/agents/pages/AgentDetailPage';
import { AgentEditPage } from '../features/agents/pages/AgentEditPage';

export function AppRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppShell />}>
          <Route index element={<Navigate to="/agents" replace />} />
          <Route path="agents">
            <Route index element={<AgentListPage />} />
            <Route path="new" element={<AgentCreatePage />} />
            <Route path=":id" element={<AgentDetailPage />} />
            <Route path=":id/edit" element={<AgentEditPage />} />
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
