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
import { getAgentInsights, getSystemInsights } from '../features/insights/api/insightsApi';
import {
  systemInsightsFixture,
  tokensFixture,
} from '../features/insights/test/systemInsightsFixture';
import { agentInsightsFixture } from '../features/insights/test/agentInsightsFixture';
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
  // `getAgentInsights` ENTRA AQUI porque a aba de Insights do agente é
  // alcançável pela árvore de rotas — pelo link do card "Consumo por agente" —,
  // e o aviso acima vale para ela: função de rota que falta escapa para a rede,
  // e quem reprova é o caso SEGUINTE, na tela de login.
  return { ...actual, getSystemInsights: vi.fn(), getAgentInsights: vi.fn() };
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
    vi.mocked(getAgentInsights).mockReset();
    vi.mocked(getAgentInsights).mockResolvedValue(agentInsightsFixture({ agentId: agent.id }));
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

  // A TRAVESSIA DO RANKING ATÉ O DIAGNÓSTICO, E POR QUE ELA PRECISA DE CASO
  // PRÓPRIO (#67, caminho 2).
  //
  // Existem dois guardas nas duas pontas desta costura, e NENHUM dos dois prova
  // o caminho inteiro:
  //
  //   - `AgentConsumptionCard.test.tsx` afirma o **href** do link;
  //   - `AgentDetailPage.test.tsx` afirma que `?tab=insights` **abre o painel**.
  //
  // Os dois podem estar verdes com a travessia quebrada, porque nenhum deles
  // NAVEGA. É a régua que esta base já encontrou duas vezes — **guarda que
  // afirma o meio do caminho não prova o fim dele** —, e o custo de não a ter
  // aqui é direto: a decisão da #67 se apoia em a profundidade estar a UM
  // clique, então o clique é o comportamento, não o atributo.
  it('do ranking do sistema, o nome do agente leva DIRETO à aba de Insights dele', async () => {
    const user = userEvent.setup();
    vi.mocked(listAgents).mockResolvedValue([agent]);
    vi.mocked(getSystemInsights).mockResolvedValue(
      systemInsightsFixture({
        tokens: tokensFixture({
          byAgent: [{ agentId: agent.id, inputTokens: 1000, outputTokens: 500 }],
        }),
      }),
    );

    renderRoutesFrom('/insights');

    await user.click(await screen.findByTestId(`agente-${agent.id}-nome`));

    // A aba ATIVA, e não só a URL: o episódio de 26/09 foi exatamente o endereço
    // certo com a aba errada, e é isso que esta asserção pega.
    expect(await screen.findByRole('tab', { name: /insights/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(await screen.findByTestId('aba-insights-do-agente')).toBeInTheDocument();
  });

  // E A JANELA TAMBÉM ATRAVESSA — CASO PRÓPRIO, NÃO ASSERÇÃO DENTRO DO DE CIMA
  // (#85).
  //
  // O caso acima prova que o clique chega à aba certa. Este prova que ele chega
  // MEDINDO A MESMA COISA, que é a outra metade da decisão da #67: a profundidade
  // está a um clique, e um clique que troca a janela em silêncio não é a mesma
  // passagem.
  //
  // CASO PRÓPRIO, e é régua: numa tela cujo valor está no que ela afirma, cada
  // afirmação é um `it()` — acrescentar esta asserção dentro do caso acima a faria
  // sumir na primeira refatoração que "limpasse" o teste.
  //
  // E É AQUI, não nas pontas: `AgentConsumptionCard.test.tsx` afirma o `href` com
  // `period=90d` e `AgentDetailPage.test.tsx` afirma que `?period=90d` abre em 90
  // dias. Os dois podem estar verdes com a travessia quebrada, porque nenhum
  // NAVEGA. Guarda que afirma o meio do caminho não prova o fim dele.
  //
  // A LARGURA, E NÃO OS INSTANTES: `(to - from)` não depende de quando o teste
  // roda, então não há relógio a controlar — o painel não tem `TimeProvider` e a
  // feature de insights não usa `vi.setSystemTime` em teste nenhum. É consequência
  // da decisão de o endereço levar o NOME da janela e não os seus limites: com
  // instantes no endereço, este guarda precisaria de relógio fixo para ser
  // reprodutível.
  it('do ranking do sistema, a JANELA escolhida atravessa até a aba do agente', async () => {
    const user = userEvent.setup();
    vi.mocked(listAgents).mockResolvedValue([agent]);
    vi.mocked(getSystemInsights).mockResolvedValue(
      systemInsightsFixture({
        tokens: tokensFixture({
          byAgent: [{ agentId: agent.id, inputTokens: 1000, outputTokens: 500 }],
        }),
      }),
    );

    renderRoutesFrom('/insights');

    // 90d, que NÃO é o padrão: em 30d o guarda passaria com o defeito presente.
    await user.click(await screen.findByRole('radio', { name: '90d' }));
    await user.click(await screen.findByTestId(`agente-${agent.id}-nome`));

    expect(await screen.findByTestId('aba-insights-do-agente')).toBeInTheDocument();

    const chamadas = vi.mocked(getAgentInsights).mock.calls;
    expect(chamadas.length).toBeGreaterThan(0);
    const dias = chamadas.map(
      ([, from, to]) => (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000),
    );

    expect(dias).toContain(90);
    // O SELETOR DA ABA marcado, e não só a consulta: é o que o operador lê para
    // saber sobre qual período o número fala.
    expect(
      within(await screen.findByTestId('aba-insights-do-agente')).getByRole('radio', {
        name: '90d',
      }),
    ).toBeChecked();
  });

  it('NEGATIVO: a travessia não consulta a aba com a janela PADRÃO', async () => {
    // O par do caso acima, e sem ele o modo de falha desta issue passaria: uma
    // consulta a mais com os 30 dias do padrão — pela aba nascendo no default e
    // sendo corrigida depois, ou por um link que não leva o parâmetro — entregaria
    // números de outro período antes dos certos, e `toContain(90)` continuaria
    // verde.
    const user = userEvent.setup();
    vi.mocked(listAgents).mockResolvedValue([agent]);
    vi.mocked(getSystemInsights).mockResolvedValue(
      systemInsightsFixture({
        tokens: tokensFixture({
          byAgent: [{ agentId: agent.id, inputTokens: 1000, outputTokens: 500 }],
        }),
      }),
    );

    renderRoutesFrom('/insights');

    await user.click(await screen.findByRole('radio', { name: '90d' }));
    await user.click(await screen.findByTestId(`agente-${agent.id}-nome`));
    await screen.findByTestId('aba-insights-do-agente');

    const dias = vi
      .mocked(getAgentInsights)
      .mock.calls.map(
        ([, from, to]) =>
          (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000),
      );

    expect(dias).not.toContain(30);
    expect(new Set(dias)).toEqual(new Set([90]));
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
