import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { AppShell } from '../components/layout/AppShell';
import { AgentListPage } from '../features/agents/pages/AgentListPage';
import { AgentCreatePage } from '../features/agents/pages/AgentCreatePage';
import { AgentDetailPage } from '../features/agents/pages/AgentDetailPage';
import { AgentEditPage } from '../features/agents/pages/AgentEditPage';
import { McpServerListPage } from '../features/mcp-servers/pages/McpServerListPage';
import { McpServerCreatePage } from '../features/mcp-servers/pages/McpServerCreatePage';
import { McpServerDetailPage } from '../features/mcp-servers/pages/McpServerDetailPage';
import { McpServerEditPage } from '../features/mcp-servers/pages/McpServerEditPage';

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
          <Route path="mcp-servers">
            <Route index element={<McpServerListPage />} />
            <Route path="new" element={<McpServerCreatePage />} />
            <Route path=":id" element={<McpServerDetailPage />} />
            <Route path=":id/edit" element={<McpServerEditPage />} />
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
