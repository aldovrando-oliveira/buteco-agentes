import { Navigate, Route, createRoutesFromElements, redirect } from 'react-router';
import type { RouteObject } from 'react-router';
import { AppShell } from '../components/layout/AppShell';
import { ProtectedRoute } from './ProtectedRoute';
import { RequirePapel } from './RequirePapel';
import { ADMIN_E_OPERADOR, SO_ADMIN } from './routeRoles';
import { EntrarPage } from '../features/auth/pages/EntrarPage';
import { ApplicationsListPage } from '../features/applications/pages/ApplicationsListPage';
import { ApplicationCreatePage } from '../features/applications/pages/ApplicationCreatePage';
import { ApplicationDetailPage } from '../features/applications/pages/ApplicationDetailPage';
import { ApplicationEditPage } from '../features/applications/pages/ApplicationEditPage';
import { EntrarRetornoPage } from '../features/auth/pages/EntrarRetornoPage';
import { ConvitePage, RedefinirSenhaPage } from '../features/auth/pages/AccessLinkPage';
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
import { AccountPage } from '../features/account/pages/AccountPage';
import { UsersPage } from '../features/users/pages/UsersPage';
import { UserDetailPage } from '../features/users/pages/UserDetailPage';
import { IdentityPage } from '../features/identity/pages/IdentityPage';

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
    {/* As rotas de entrada, sem sessão. `/login` fica para links e favoritos de antes do
        login pelo emissor (design.md da change painel-entra-pelo-emissor, D8). */}
    <Route path="entrar" element={<EntrarPage />} />
    <Route path="entrar/retorno" element={<EntrarRetornoPage />} />
    <Route path="login" element={<Navigate to="/entrar" replace />} />
    {/* As páginas do link de uso único, sem sessão. Os caminhos são os que o apps/api
        monta no link (`AccessLinkUrls`, #181 D9): mudar um exige mudar o outro (design.md
        da change entrada-publica-painel, D7). */}
    <Route path="convite" element={<ConvitePage />} />
    <Route path="redefinir-senha" element={<RedefinirSenhaPage />} />
    <Route element={<ProtectedRoute />}>
      <Route element={<AppShell />}>
        {/* A raiz redireciona para o inventário; ela não É o inventário. O item
            ativo da casca é calculado por `location.pathname.startsWith(to)`
            (AppShell.tsx:65), e um item com `to="/"` ficaria permanentemente
            ativo em toda rota do painel (design.md, D1). */}
        <Route index element={<Navigate to="/inventory" replace />} />
        <Route path="inventory" element={<InventoryPage />} />
        <Route path="insights" element={<SystemInsightsPage />} />
        {/* Minha conta: a entrada é o bloco do nome no rodapé da casca, sem item no menu
            (design.md da change minha-conta-painel, D1). */}
        <Route path="account" element={<AccountPage />} />
        {/* A seção "Acesso" do menu (design.md da change usuarios-convites-identidade-painel,
            D1), e Editar usuário (design.md da change editar-usuario-painel, D1). */}
        {/* Só o Admin (spec `panel-roles`, "Guarda de rota por papel"; design.md da change
            papeis-no-painel, D3). A guarda não substitui o `403` do servidor. */}
        <Route element={<RequirePapel papeis={SO_ADMIN} />}>
          <Route path="users" element={<UsersPage />} />
          <Route path="users/:id" element={<UserDetailPage />} />
          <Route path="identity" element={<IdentityPage />} />
          {/* Aplicações externas, só do Admin, como `/applications/*` no apps/api (#186). */}
          <Route path="applications" element={<ApplicationsListPage />} />
          <Route path="applications/new" element={<ApplicationCreatePage />} />
          <Route path="applications/:id" element={<ApplicationDetailPage />} />
          <Route path="applications/:id/edit" element={<ApplicationEditPage />} />
        </Route>
        <Route path="agents">
          <Route index element={<AgentListPage />} />
          <Route path=":id" element={<AgentDetailPage />} />
          <Route element={<RequirePapel papeis={ADMIN_E_OPERADOR} />}>
            <Route path="new" element={<AgentCreatePage />} />
            <Route path=":id/edit" element={<AgentEditPage />} />
          </Route>
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
          <Route path=":id" element={<McpServerDetailPage />} />
          <Route element={<RequirePapel papeis={ADMIN_E_OPERADOR} />}>
            <Route path="new" element={<McpServerCreatePage />} />
            <Route path=":id/edit" element={<McpServerEditPage />} />
          </Route>
        </Route>
        <Route path="knowledge-bases">
          <Route index element={<KnowledgeBaseListPage />} />
          <Route path=":id" element={<KnowledgeBaseDetailPage />} />
          <Route element={<RequirePapel papeis={ADMIN_E_OPERADOR} />}>
            <Route path="new" element={<KnowledgeBaseCreatePage />} />
            <Route path=":id/edit" element={<KnowledgeBaseEditPage />} />
          </Route>
        </Route>
        <Route path="channels">
          <Route index element={<ChannelListPage />} />
          <Route path=":id" element={<ChannelDetailPage />} />
          {/* A sessão é o histórico de conversas, que o Leitor não vê. */}
          <Route element={<RequirePapel papeis={ADMIN_E_OPERADOR} />}>
            <Route path="new" element={<ChannelCreatePage />} />
            <Route path=":id/sessions/:sessionId" element={<ChannelDetailPage />} />
            <Route path=":id/edit" element={<ChannelEditPage />} />
          </Route>
        </Route>
      </Route>
    </Route>
  </>,
);
