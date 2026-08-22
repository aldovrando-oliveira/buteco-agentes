import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { AppShell } from '../components/layout/AppShell';
import { ProtectedRoute } from './ProtectedRoute';
import { LoginPage } from '../features/auth/pages/LoginPage';
import { AgentListPage } from '../features/agents/pages/AgentListPage';
import { AgentCreatePage } from '../features/agents/pages/AgentCreatePage';
import { AgentDetailPage } from '../features/agents/pages/AgentDetailPage';
import { AgentEditPage } from '../features/agents/pages/AgentEditPage';
import { AgentMcpServersPage } from '../features/agents/pages/AgentMcpServersPage';
import { McpServerListPage } from '../features/mcp-servers/pages/McpServerListPage';
import { McpServerCreatePage } from '../features/mcp-servers/pages/McpServerCreatePage';
import { McpServerDetailPage } from '../features/mcp-servers/pages/McpServerDetailPage';
import { McpServerEditPage } from '../features/mcp-servers/pages/McpServerEditPage';
import { ChannelListPage } from '../features/channels/pages/ChannelListPage';
import { ChannelCreatePage } from '../features/channels/pages/ChannelCreatePage';
import { ChannelDetailPage } from '../features/channels/pages/ChannelDetailPage';
import { ChannelEditPage } from '../features/channels/pages/ChannelEditPage';

export function AppRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="login" element={<LoginPage />} />
        <Route element={<ProtectedRoute />}>
          <Route element={<AppShell />}>
            <Route index element={<Navigate to="/agents" replace />} />
            <Route path="agents">
              <Route index element={<AgentListPage />} />
              <Route path="new" element={<AgentCreatePage />} />
              <Route path=":id" element={<AgentDetailPage />} />
              <Route path=":id/edit" element={<AgentEditPage />} />
              <Route path=":id/mcp-servers" element={<AgentMcpServersPage />} />
            </Route>
            <Route path="mcp-servers">
              <Route index element={<McpServerListPage />} />
              <Route path="new" element={<McpServerCreatePage />} />
              <Route path=":id" element={<McpServerDetailPage />} />
              <Route path=":id/edit" element={<McpServerEditPage />} />
            </Route>
            <Route path="channels">
              <Route index element={<ChannelListPage />} />
              <Route path="new" element={<ChannelCreatePage />} />
              <Route path=":id" element={<ChannelDetailPage />} />
              <Route path=":id/sessions/:sessionId" element={<ChannelDetailPage />} />
              <Route path=":id/edit" element={<ChannelEditPage />} />
            </Route>
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
