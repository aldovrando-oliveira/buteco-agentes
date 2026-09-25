import { Navigate, Route, createRoutesFromElements, redirect } from 'react-router';
import type { RouteObject } from 'react-router';
import { AppShell } from '../components/layout/AppShell';
import { ProtectedRoute } from './ProtectedRoute';
import { LoginPage } from '../features/auth/pages/LoginPage';
import { AgentListPage } from '../features/agents/pages/AgentListPage';
import { AgentCreatePage } from '../features/agents/pages/AgentCreatePage';
import { AgentDetailPage } from '../features/agents/pages/AgentDetailPage';
import { AgentEditPage } from '../features/agents/pages/AgentEditPage';
import { McpServerListPage } from '../features/mcp-servers/pages/McpServerListPage';
import { McpServerCreatePage } from '../features/mcp-servers/pages/McpServerCreatePage';
import { McpServerDetailPage } from '../features/mcp-servers/pages/McpServerDetailPage';
import { McpServerEditPage } from '../features/mcp-servers/pages/McpServerEditPage';
import { KnowledgeBaseListPage } from '../features/knowledge-bases/pages/KnowledgeBaseListPage';
import { KnowledgeBaseCreatePage } from '../features/knowledge-bases/pages/KnowledgeBaseCreatePage';
import { KnowledgeBaseDetailPage } from '../features/knowledge-bases/pages/KnowledgeBaseDetailPage';
import { KnowledgeBaseEditPage } from '../features/knowledge-bases/pages/KnowledgeBaseEditPage';
import { ChannelListPage } from '../features/channels/pages/ChannelListPage';
import { ChannelCreatePage } from '../features/channels/pages/ChannelCreatePage';
import { ChannelDetailPage } from '../features/channels/pages/ChannelDetailPage';
import { ChannelEditPage } from '../features/channels/pages/ChannelEditPage';
import { InventoryPage } from '../features/inventory/pages/InventoryPage';
import { SystemInsightsPage } from '../features/insights/pages/SystemInsightsPage';

// A árvore de rotas mora aqui, e não em router.tsx, porque router.tsx cria
// o browser router no escopo do módulo — importá-lo tem efeito colateral
// sobre o histórico global. Quem só precisa das rotas (os testes, que as
// montam com createMemoryRouter) importa este módulo e não paga esse custo.
// Ver Decision 2 do design.md da change frontend-roteamento-data-router.
//
// A árvore é escrita como JSX aninhado e convertida por
// createRoutesFromElements em vez de declarada como objetos: a migração
// para data mode não deveria mudar nenhuma rota, e essa forma mantém o
// diff restrito ao invólucro (Decision 1).
export const appRoutes: RouteObject[] = createRoutesFromElements(
  <>
    <Route path="login" element={<LoginPage />} />
    <Route element={<ProtectedRoute />}>
      <Route element={<AppShell />}>
        {/* A raiz redireciona para o inventário; ela não É o inventário. O item
            ativo da casca é calculado por `location.pathname.startsWith(to)`
            (AppShell.tsx:65), e um item com `to="/"` ficaria permanentemente
            ativo em toda rota do painel (design.md, D1). */}
        <Route index element={<Navigate to="/inventory" replace />} />
        <Route path="inventory" element={<InventoryPage />} />
        <Route path="insights" element={<SystemInsightsPage />} />
        <Route path="agents">
          <Route index element={<AgentListPage />} />
          <Route path="new" element={<AgentCreatePage />} />
          <Route path=":id" element={<AgentDetailPage />} />
          <Route path=":id/edit" element={<AgentEditPage />} />
          {/* A gestão do vínculo virou aba do detalhe do agente. A rota
              continua existindo, sem componente, só para que links salvos
              antes da mudança não quebrem. É o único loader do projeto: não
              busca nada, apenas traduz uma rota morta (Decision 4 do
              design.md da change frontend-agente-detalhe-abas). */}
          <Route
            path=":id/mcp-servers"
            loader={({ params }) => redirect(`/agents/${params.id}?tab=ferramentas`)}
          />
        </Route>
        <Route path="mcp-servers">
          <Route index element={<McpServerListPage />} />
          <Route path="new" element={<McpServerCreatePage />} />
          <Route path=":id" element={<McpServerDetailPage />} />
          <Route path=":id/edit" element={<McpServerEditPage />} />
        </Route>
        <Route path="knowledge-bases">
          <Route index element={<KnowledgeBaseListPage />} />
          <Route path="new" element={<KnowledgeBaseCreatePage />} />
          <Route path=":id" element={<KnowledgeBaseDetailPage />} />
          <Route path=":id/edit" element={<KnowledgeBaseEditPage />} />
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
  </>,
);
