import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../theme';
import { AppRouter } from './router';
import { appRoutes } from './routes';
import { getAgent, listAgents } from '../features/agents/api/agentsApi';
import { listMcpServers } from '../features/mcp-servers/api/mcpServersApi';
import type { Agent } from '../features/agents/types/agent';
import { listProviders } from '../features/agents/api/providersApi';
import { listChannels } from '../features/channels/api/channelsApi';
import {
  listKnowledgeBaseIndexingSummary,
  listKnowledgeBases,
} from '../features/knowledge-bases/api/knowledgeBasesApi';
import { getMessagesSummary, getSessionsSummary } from '../features/sessions/api/sessionsApi';
import { getSystemInsights } from '../features/insights/api/insightsApi';
import { systemInsightsFixture } from '../features/insights/test/systemInsightsFixture';
import { clearToken, setToken } from '../auth/token';

// importOriginal preserva ApiError: o detalhe do agente faz `instanceof
// ApiError` a cada render, e um mock que apagasse a classe quebraria a
// página antes de qualquer asserção.
vi.mock('../features/agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn(), getAgent: vi.fn(), createAgent: vi.fn() };
});

vi.mock('../features/mcp-servers/api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/mcp-servers/api/mcpServersApi')>();
  return { ...actual, listMcpServers: vi.fn(), listMcpServerTools: vi.fn() };
});

vi.mock('../features/agents/api/providersApi', () => ({
  listProviders: vi.fn(),
}));

vi.mock('../features/channels/api/channelsApi', () => ({
  listChannels: vi.fn(),
}));

// MOCK PARCIAL: `importOriginal` preserva `request`/`ApiError` e substitui as
// funções de rota. Toda função deste módulo que alguma rota da árvore chame
// precisa estar aqui — a que faltar **escapa para a rede**, e este arquivo não
// stuba `fetch`.
//
// O modo de falha é indireto e por isso caro: contra uma API real a chamada volta
// 401, `request<T>` chama `clearToken()` e navega para `/login`, e quem reprova é
// o teste SEGUINTE, renderizando a tela de login em vez da rota pedida. Aconteceu
// com `listKnowledgeBaseIndexingSummary` quando ela nasceu.
vi.mock('../features/knowledge-bases/api/knowledgeBasesApi', async (importOriginal) => {
  const actual =
    await importOriginal<typeof import('../features/knowledge-bases/api/knowledgeBasesApi')>();
  return { ...actual, listKnowledgeBases: vi.fn(), listKnowledgeBaseIndexingSummary: vi.fn() };
});

// MOCK PARCIAL, pelo mesmo motivo do aviso acima: a raiz leva ao inventário, e o
// inventário chama os dois resumos de atividade de apps/inbox. Sem eles aqui, as
// duas chamadas escapam para a rede. `importOriginal` preserva `request`/`ApiError`
// e as funções que as telas de canal usam (`listChannelSessions`,
// `getSessionMessages`) — só os dois resumos são substituídos.
//
// Registro do apply: sem este mock o arquivo ainda passava, mas só porque
// apps/inbox não estava de pé na máquina — a chamada falhava como erro de rede,
// sem 401 e sem redirecionar para /login. Com o inbox rodando, o modo de falha
// descrito acima apareceria. Passar por ambiente não é passar.
vi.mock('../features/sessions/api/sessionsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/sessions/api/sessionsApi')>();
  return { ...actual, getSessionsSummary: vi.fn(), getMessagesSummary: vi.fn() };
});

// Mesmo motivo do de cima: sem este mock a rota /insights escaparia para a rede.
vi.mock('../features/insights/api/insightsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../features/insights/api/insightsApi')>();
  return { ...actual, getSystemInsights: vi.fn() };
});

const agent: Agent = {
  id: '55555555-5555-5555-5555-555555555555',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  description: null,
  skills: [],
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  delegatesTo: [],
  knowledgeBases: [],
  a2a: null,
};

function renderProviders(children: React.ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MantineProvider>,
  );
}

// Monta a mesma árvore de rotas da aplicação em um router de memória. As
// asserções são sobre o conteúdo renderizado, e não sobre
// window.location.pathname: o router de produção é criado uma única vez no
// escopo do módulo (Decision 2 do design.md), então empurrar o histórico
// global por teste não o reposicionaria — a localização vazaria de um caso
// para o outro. Ver Decision 3.
function renderRoutesFrom(initialEntry: string) {
  return renderProviders(
    <RouterProvider router={createMemoryRouter(appRoutes, { initialEntries: [initialEntry] })} />,
  );
}

describe('appRoutes', () => {
  beforeEach(() => {
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
    vi.mocked(listProviders).mockReset();
    vi.mocked(listProviders).mockResolvedValue([{ id: 'openai', models: ['gpt-5.6-sol'] }]);
    vi.mocked(listChannels).mockReset();
    vi.mocked(listChannels).mockResolvedValue([]);
    vi.mocked(listKnowledgeBases).mockReset();
    vi.mocked(listKnowledgeBases).mockResolvedValue([]);
    vi.mocked(listKnowledgeBaseIndexingSummary).mockReset();
    vi.mocked(listKnowledgeBaseIndexingSummary).mockResolvedValue([]);
    vi.mocked(getAgent).mockReset();
    vi.mocked(getAgent).mockResolvedValue(agent);
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listMcpServers).mockResolvedValue([]);
    vi.mocked(getSessionsSummary).mockReset();
    vi.mocked(getSessionsSummary).mockResolvedValue({ startedCount: 0 });
    vi.mocked(getMessagesSummary).mockReset();
    vi.mocked(getMessagesSummary).mockResolvedValue({ inboundCount: 0 });
    vi.mocked(getSystemInsights).mockReset();
    vi.mocked(getSystemInsights).mockResolvedValue(systemInsightsFixture());
    setToken('token-de-teste');
  });

  afterEach(() => {
    clearToken();
  });

  it('sem token armazenado, leva para o login em vez do conteúdo protegido', async () => {
    clearToken();

    renderRoutesFrom('/');

    expect(await screen.findByLabelText(/usuário/i)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Inventário' })).not.toBeInTheDocument();
  });

  it('redireciona a rota raiz para o inventário sem exibir página vazia', async () => {
    renderRoutesFrom('/');

    expect(await screen.findByRole('heading', { name: 'Inventário' })).toBeInTheDocument();
  });

  it('a rota /insights resolve para a página de Insights do sistema', async () => {
    renderRoutesFrom('/insights');

    expect(await screen.findByRole('heading', { name: 'Insights' })).toBeInTheDocument();
  });

  it('preserva o layout do AppShell ao navegar entre rotas', async () => {
    const user = userEvent.setup();
    // Parte de /agents, e não da raiz: o que este caso prova é que a casca
    // sobrevive à navegação. O destino da raiz tem caso próprio acima, e fazê-lo
    // atravessar o inventário aqui só acrescentaria um passo sem asserção.
    renderRoutesFrom('/agents');

    // O item de navegação é procurado dentro da barra lateral: a página de
    // criação também tem um link chamado "Agentes", o de voltar para a
    // listagem, e a asserção precisa dizer de qual dos dois fala.
    const navegacao = () => within(screen.getByRole('navigation'));

    await screen.findByRole('heading', { name: 'Agentes' });
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
    expect(navegacao().getByRole('link', { name: 'Agentes' })).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Novo agente' }));

    expect(await screen.findByRole('heading', { name: 'Novo agente' })).toBeInTheDocument();
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
    expect(navegacao().getByRole('link', { name: 'Agentes' })).toBeInTheDocument();
  });

  it('navega para a listagem de canais pelo item de navegação "Canais"', async () => {
    const user = userEvent.setup();
    renderRoutesFrom('/agents');

    await screen.findByRole('heading', { name: 'Agentes' });
    await user.click(screen.getByRole('link', { name: 'Canais' }));

    expect(await screen.findByRole('heading', { name: 'Canais' })).toBeInTheDocument();
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
  });

  it('navega para o catálogo de bases pelo item de navegação "Conhecimento"', async () => {
    const user = userEvent.setup();
    renderRoutesFrom('/agents');

    await screen.findByRole('heading', { name: 'Agentes' });
    await user.click(screen.getByRole('link', { name: 'Conhecimento' }));

    expect(
      await screen.findByRole('heading', { name: 'Bases de conhecimento' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
  });

  it('monta as quatro rotas do grupo de conhecimento', async () => {
    renderRoutesFrom('/knowledge-bases/new');

    expect(
      await screen.findByRole('heading', { name: 'Nova base de conhecimento' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toBeInTheDocument();
  });

  it('a rota antiga de gestão do vínculo leva à aba de ferramentas do detalhe', async () => {
    renderRoutesFrom(`/agents/${agent.id}/mcp-servers`);

    expect(await screen.findByRole('tab', { name: /ferramentas/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(await screen.findByText(/nenhum servidor mcp cadastrado/i)).toBeInTheDocument();
  });

  it('monta a partir de uma rota interna, com o mesmo layout e a mesma proteção', async () => {
    renderRoutesFrom('/channels');

    expect(await screen.findByRole('heading', { name: 'Canais' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toBeInTheDocument();
  });

  it('a contagem do inventário e a da listagem de agentes não divergem', async () => {
    const user = userEvent.setup();
    vi.mocked(listAgents).mockResolvedValue([
      { ...agent, id: '11111111-1111-1111-1111-111111111111', name: 'Atendente' },
      { ...agent, id: '22222222-2222-2222-2222-222222222222', name: 'Cobrança' },
      { ...agent, id: '33333333-3333-3333-3333-333333333333', name: 'Triagem' },
    ]);

    renderRoutesFrom('/');

    // A contagem é LIDA do inventário, não escrita aqui. Fixar o número no
    // teste provaria outra coisa — que o mock tem três agentes —, e é o acordo
    // entre as duas telas que este caso existe para provar.
    // Espera pelo NÚMERO, não pelo card: o card renderiza de imediato, com o
    // esqueleto de carregamento, e só depois ganha a contagem.
    const contagem = (await screen.findByTestId('inventario-agents-contagem')).textContent?.trim();
    const item = screen.getByTestId('inventario-agents');

    // Guarda do próprio teste: sem ela, uma contagem vazia faria a asserção
    // final virar /  agentes cadastrados/ e casar por acidente.
    //
    // Checa que é UM número, nunca QUAL número: prender ao valor mockado faria
    // este caso reprovar ao mudar a fixture, por um motivo que não é o que ele
    // prova. O número certo quem confere é a asserção final, contra a listagem.
    expect(contagem).toMatch(/^\d+$/);

    await user.click(within(item).getByRole('link', { name: 'Ver agentes' }));

    expect(await screen.findByRole('heading', { name: 'Agentes' })).toBeInTheDocument();
    // Mesmo idioma de asserção de AgentListPage.test.tsx:126, com o número
    // vindo do card e não do teste.
    expect(screen.getByText(new RegExp(`${contagem} agentes cadastrados`))).toBeInTheDocument();
  });

  it('monta a partir de uma rota interna sem token e leva para o login', async () => {
    clearToken();

    renderRoutesFrom('/channels');

    expect(await screen.findByLabelText(/usuário/i)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Canais' })).not.toBeInTheDocument();
  });
});

describe('AppRouter', () => {
  beforeEach(() => {
    // AS SEIS, e não só listAgents: a raiz leva ao inventário, que consulta os
    // quatro catálogos e os dois resumos de atividade. Configurar só uma deixaria
    // as outras dependendo do `mockResolvedValue` VAZADO do describe anterior —
    // vitest não limpa mocks entre describes e vite.config.ts não liga
    // `clearMocks`, então o caso passaria por ordem de execução, não por estar
    // correto.
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listMcpServers).mockResolvedValue([]);
    vi.mocked(listKnowledgeBases).mockReset();
    vi.mocked(listKnowledgeBases).mockResolvedValue([]);
    vi.mocked(listChannels).mockReset();
    vi.mocked(listChannels).mockResolvedValue([]);
    vi.mocked(getSessionsSummary).mockReset();
    vi.mocked(getSessionsSummary).mockResolvedValue({ startedCount: 0 });
    vi.mocked(getMessagesSummary).mockReset();
    vi.mocked(getMessagesSummary).mockResolvedValue({ inboundCount: 0 });
    vi.mocked(getSystemInsights).mockReset();
    vi.mocked(getSystemInsights).mockResolvedValue(systemInsightsFixture());
    setToken('token-de-teste');
  });

  afterEach(() => {
    clearToken();
  });

  // Fumaça sobre o ponto de entrada real: prova que o browser router criado
  // no escopo do módulo compõe com appRoutes e monta a aplicação. Não
  // navega, para não depender da ordem dos testes nem do window.location
  // compartilhado do jsdom.
  it('monta a aplicação com o browser router de produção', async () => {
    renderProviders(<AppRouter />);

    expect(await screen.findByRole('heading', { name: 'Inventário' })).toBeInTheDocument();
  });
});
