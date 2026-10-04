import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentDetailPage } from './AgentDetailPage';
import { ApiError, activateAgent, deactivateAgent, getAgent, listAgents } from '../api/agentsApi';
import { listMcpServers, listMcpServerTools } from '../../mcp-servers/api/mcpServersApi';
import { listKnowledgeBases } from '../../knowledge-bases/api/knowledgeBasesApi';
import { getAgentInsights } from '../../insights/api/insightsApi';
import { agentInsightsFixture } from '../../insights/test/agentInsightsFixture';
import { DEFAULT_INSIGHTS_PERIOD } from '../../insights/utils/insightsWindow';
import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';
import type { Agent } from '../types/agent';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return {
    ...actual,
    getAgent: vi.fn(),
    listAgents: vi.fn(),
    activateAgent: vi.fn(),
    deactivateAgent: vi.fn(),
  };
});

vi.mock('../../mcp-servers/api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../mcp-servers/api/mcpServersApi')>();
  return { ...actual, listMcpServers: vi.fn(), listMcpServerTools: vi.fn() };
});

vi.mock('../../knowledge-bases/api/knowledgeBasesApi', async (importOriginal) => {
  const actual =
    await importOriginal<typeof import('../../knowledge-bases/api/knowledgeBasesApi')>();
  return { ...actual, listKnowledgeBases: vi.fn() };
});

vi.mock('../../insights/api/insightsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../insights/api/insightsApi')>();
  return { ...actual, getAgentInsights: vi.fn() };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

const activeAgent: Agent = {
  id: '33333333-3333-3333-3333-333333333333',
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

const inactiveAgent: Agent = { ...activeAgent, isActive: false };

const otherAgent: Agent = {
  ...activeAgent,
  id: '77777777-7777-7777-7777-777777777777',
  name: 'Cobrança',
};

const mcpServer: McpServer = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Zendesk MCP',
  description: '',
  url: 'https://mcp.exemplo.com/sse',
  authType: 'None',
  isActive: true,
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
};

const knowledgeBase: KnowledgeBase = {
  id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  name: 'Cardápio',
  description: 'Pratos, porções e preços.',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  contentMode: 'Manual',
  syncSource: null,
};

function renderPage(id: string, search = '') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/agents/:id', element: <AgentDetailPage /> },
      { path: '/agents', element: <p>listagem de agentes</p> },
      { path: '/agents/:id/edit', element: <p>edição de agente</p> },
      { path: '/mcp-servers/new', element: <p>cadastro de servidor MCP</p> },
    ],
    { initialEntries: [`/agents/${id}${search}`] },
  );

  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );

  return router;
}

function tab(name: RegExp) {
  return screen.getByRole('tab', { name });
}

describe('AgentDetailPage', () => {
  beforeEach(() => {
    vi.mocked(getAgent).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([activeAgent]);
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listMcpServers).mockResolvedValue([mcpServer]);
    vi.mocked(listKnowledgeBases).mockReset();
    vi.mocked(listKnowledgeBases).mockResolvedValue([knowledgeBase]);
    vi.mocked(listMcpServerTools).mockReset();
    vi.mocked(listMcpServerTools).mockResolvedValue({
      success: true,
      tools: [{ name: 'buscar_chamado', description: 'Busca um chamado' }],
      failureReason: null,
      message: null,
    });
    vi.mocked(getAgentInsights).mockReset();
    vi.mocked(getAgentInsights).mockResolvedValue(
      agentInsightsFixture({ agentId: activeAgent.id }),
    );
    vi.mocked(activateAgent).mockReset();
    vi.mocked(deactivateAgent).mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe nome, estado, link de edição e ação de desativar no cabeçalho de um agente ativo', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    expect(await screen.findByRole('heading', { name: activeAgent.name })).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /editar/i })).toHaveAttribute(
      'href',
      `/agents/${activeAgent.id}/edit`,
    );
    expect(screen.getByRole('button', { name: /desativar/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^ativar$/i })).not.toBeInTheDocument();
  });

  it('exibe a descrição do agente quando presente e a indicação de ausência quando nula', async () => {
    vi.mocked(getAgent).mockResolvedValue({ ...activeAgent, description: 'Atende o financeiro' });

    const { unmount } = render(<div />);
    unmount();
    renderPage(activeAgent.id);

    expect(await screen.findByText('Atende o financeiro')).toBeInTheDocument();
    expect(screen.queryByText('Sem descrição.')).not.toBeInTheDocument();
  });

  it('exibe a indicação de ausência de descrição quando o agente não tem descrição', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    expect(await screen.findByText('Sem descrição.')).toBeInTheDocument();
  });

  it('exibe as cinco abas na ordem certa, com a visão geral ativa por padrão', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    expect(await screen.findByRole('tab', { name: /visão geral/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(tab(/ferramentas/i)).toBeInTheDocument();
    expect(tab(/conhecimento/i)).toBeInTheDocument();
    expect(tab(/delegações/i)).toBeInTheDocument();
    expect(tab(/insights/i)).toBeInTheDocument();
    // Conhecimento é a terceira, entre Ferramentas e Delegações, e Insights
    // fecha a lista.
    expect(screen.getAllByRole('tab').map((element) => element.textContent)).toEqual([
      'Visão geral',
      'Ferramentas',
      'Conhecimento',
      'Delegações',
      'Insights',
    ]);
    expect(screen.getByText(activeAgent.instructions)).toBeInTheDocument();
  });

  it('exibe contadores nas abas conforme os vínculos do agente', async () => {
    vi.mocked(getAgent).mockResolvedValue({
      ...activeAgent,
      mcpServers: [{ id: mcpServer.id, name: mcpServer.name, allowedTools: ['buscar_chamado'] }],
      delegatesTo: [
        { id: '99999999-9999-9999-9999-999999999999', name: 'Cobrança' },
        { id: '88888888-8888-8888-8888-888888888888', name: 'Financeiro' },
      ],
      knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }],
      a2a: null,
    });

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    expect(within(tab(/ferramentas/i)).getByText('1')).toBeInTheDocument();
    expect(within(tab(/conhecimento/i)).getByText('1')).toBeInTheDocument();
    expect(within(tab(/delegações/i)).getByText('2')).toBeInTheDocument();
  });

  it('oculta o contador da aba quando o vínculo está vazio', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    expect(within(tab(/ferramentas/i)).queryByText('0')).not.toBeInTheDocument();
    expect(within(tab(/conhecimento/i)).queryByText('0')).not.toBeInTheDocument();
    expect(within(tab(/delegações/i)).queryByText('0')).not.toBeInTheDocument();
  });

  it('acionar uma aba passa a identificá-la na URL', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    const router = renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    await user.click(tab(/ferramentas/i));

    await waitFor(() => expect(router.state.location.search).toBe('?tab=ferramentas'));
  });

  it('abre a aba identificada na URL ao carregar a página', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    vi.mocked(listAgents).mockResolvedValue([activeAgent, otherAgent]);

    renderPage(activeAgent.id, '?tab=delegacoes');

    expect(await screen.findByRole('tab', { name: /delegações/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(screen.getByLabelText('Buscar por nome')).toBeInTheDocument();
  });

  it('endereço sem identificação de aba abre a visão geral sem alterar a URL', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    const router = renderPage(activeAgent.id);

    expect(await screen.findByRole('tab', { name: /visão geral/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(router.state.location.search).toBe('');
  });

  it('identificação de aba desconhecida abre a visão geral sem quebrar a página', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id, '?tab=inexistente');

    expect(await screen.findByRole('tab', { name: /visão geral/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(screen.getByText(activeAgent.instructions)).toBeInTheDocument();
  });

  // A IDENTIFICAÇÃO DA ABA É UMA CHAVE DO ENDEREÇO, NÃO O ENDEREÇO INTEIRO.
  //
  // O escritor fazia `setSearchParams(next === OVERVIEW_TAB ? {} : { tab: next })`,
  // e AS DUAS PERNAS substituíam a busca inteira: `{}` esvazia, `{ tab: next }`
  // descarta tudo que não seja `tab`. Era inofensivo enquanto `tab` fosse a única
  // chave — e passou a ser defeito no instante em que o período entrou no endereço.
  //
  // O SINTOMA QUE ISSO PRODUZ É O DESTA PRÓPRIA ISSUE, por outro caminho: quem
  // chega à aba em 90 dias, vai para "Visão geral" e volta, perde a janela em
  // silêncio. Nada quebra, nada reprova, e o número passa a responder outra
  // pergunta — que é exactamente o que a #85 existe para fechar.
  //
  // OS DOIS CASOS SEMEIAM O SEGUNDO PARÂMETRO NO ARRANJO, e é o que os torna
  // guardas do defeito em vez de guardas da forma: sem semeá-lo, não há nada a
  // perder, e eles passariam verdes contra o escritor velho.
  //
  // A irmã deste defeito, em `KnowledgeBaseDetailPage.tsx:98`, tem a MESMA forma e
  // ficou de fora por não ter parâmetro concorrente hoje — é a #96, com gatilho
  // observável.
  it('trocar de aba preserva os demais parâmetros do endereço', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    const router = renderPage(activeAgent.id, '?tab=ferramentas&period=90d');

    await screen.findByRole('heading', { name: activeAgent.name });
    await user.click(tab(/conhecimento/i));

    await waitFor(() =>
      expect(new URLSearchParams(router.state.location.search).get('tab')).toBe(
        'conhecimento',
      ),
    );
    expect(new URLSearchParams(router.state.location.search).get('period')).toBe('90d');
  });

  it('voltar à visão geral remove SÓ a identificação da aba', async () => {
    // A visão geral continua sendo a AUSÊNCIA de `tab`, não `tab=visao-geral` — o
    // contrato de `parseTab` fica intacto. O que muda é o que o escritor preserva
    // ao redor dele, e esta é a perna que o `{}` tornava total.
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    const router = renderPage(activeAgent.id, '?tab=ferramentas&period=7d');

    await screen.findByRole('heading', { name: activeAgent.name });
    await user.click(tab(/visão geral/i));

    await waitFor(() =>
      expect(new URLSearchParams(router.state.location.search).has('tab')).toBe(false),
    );
    expect(new URLSearchParams(router.state.location.search).get('period')).toBe('7d');
  });

  it('o conteúdo da aba inativa não está presente na página', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    vi.mocked(listAgents).mockResolvedValue([activeAgent, otherAgent]);
    const user = userEvent.setup();

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    expect(screen.queryByLabelText('Buscar por nome')).not.toBeInTheDocument();

    await user.click(tab(/delegações/i));

    expect(await screen.findByLabelText('Buscar por nome')).toBeInTheDocument();
    expect(screen.queryByText(activeAgent.instructions)).not.toBeInTheDocument();
  });

  it('não exibe nenhum link para a página separada de gestão de servidores MCP', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    expect(
      screen.queryByRole('link', { name: /gerenciar servidores mcp/i }),
    ).not.toBeInTheDocument();
    const hrefs = screen.getAllByRole('link').map((link) => link.getAttribute('href'));
    expect(hrefs).not.toContain(`/agents/${activeAgent.id}/mcp-servers`);
  });

  it('exibe o cabeçalho antes do conteúdo da aba ativa, na ordem do DOM', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    const editLink = await screen.findByRole('link', { name: /editar/i });
    const instructions = screen.getByText(activeAgent.instructions);

    expect(
      editLink.compareDocumentPosition(instructions) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('exibe estado de "não encontrado" quando o agente não existe', async () => {
    vi.mocked(getAgent).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage('inexistente');

    expect(await screen.findByText('Agente não encontrado.')).toBeInTheDocument();
  });

  it('exibe a ação "Ativar" (e não "Desativar") para um agente inativo', async () => {
    vi.mocked(getAgent).mockResolvedValue(inactiveAgent);

    renderPage(inactiveAgent.id);

    expect(await screen.findByText('Inativo')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^ativar$/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /desativar/i })).not.toBeInTheDocument();
  });

  it('ao ativar, envia a requisição imediatamente sem confirmação e atualiza o indicador', async () => {
    vi.mocked(getAgent).mockResolvedValueOnce(inactiveAgent).mockResolvedValue(activeAgent);
    vi.mocked(activateAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    renderPage(inactiveAgent.id);

    await user.click(await screen.findByRole('button', { name: /^ativar$/i }));

    expect(activateAgent).toHaveBeenCalledWith(inactiveAgent.id);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(await screen.findByText('Ativo')).toBeInTheDocument();
  });

  it('ao clicar em "Desativar", abre o modal de confirmação sem enviar a requisição', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    renderPage(activeAgent.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(deactivateAgent).not.toHaveBeenCalled();
  });

  it('cancelar a confirmação fecha o modal sem enviar a requisição e mantém o agente ativo', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    renderPage(activeAgent.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: /cancelar/i }));

    expect(deactivateAgent).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByText('Ativo')).toBeInTheDocument();
  });

  it('confirmar a desativação envia a requisição, notifica sucesso e atualiza o indicador', async () => {
    vi.mocked(getAgent).mockResolvedValueOnce(activeAgent).mockResolvedValue(inactiveAgent);
    vi.mocked(deactivateAgent).mockResolvedValue(inactiveAgent);
    const user = userEvent.setup();

    renderPage(activeAgent.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));
    await screen.findByRole('dialog');
    await user.click(screen.getByRole('button', { name: /confirmar desativação/i }));

    expect(deactivateAgent).toHaveBeenCalledWith(activeAgent.id);
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(await screen.findByText('Inativo')).toBeInTheDocument();
  });

  it('em falha ao desativar, notifica erro genérico e mantém o estado anterior', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    vi.mocked(deactivateAgent).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();

    renderPage(activeAgent.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));
    await screen.findByRole('dialog');
    await user.click(screen.getByRole('button', { name: /confirmar desativação/i }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(screen.getByText('Ativo')).toBeInTheDocument();
  });

  it('só busca o catálogo de servidores MCP quando a aba de ferramentas está ativa', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    expect(listMcpServers).not.toHaveBeenCalled();

    await user.click(tab(/ferramentas/i));

    await waitFor(() => expect(listMcpServers).toHaveBeenCalled());
  });

  it('quando o catálogo de agentes tem apenas o próprio agente, a aba de delegações explica a ausência', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    vi.mocked(listAgents).mockResolvedValue([activeAgent]);

    renderPage(activeAgent.id, '?tab=delegacoes');

    expect(
      await screen.findByText('Nenhum outro agente cadastrado para receber delegações.'),
    ).toBeInTheDocument();
  });

  it('oferece volta para a listagem, inclusive em acesso direto pela URL', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    // O harness monta a página de detalhe como primeira entrada do histórico:
    // não há para onde voltar, e o link precisa funcionar mesmo assim.
    const volta = await screen.findByRole('link', { name: 'Agentes' });

    expect(volta).toHaveAttribute('href', '/agents');
  });
});

describe('AgentDetailPage — aba Conhecimento', () => {
  // beforeEach próprio: o do describe acima não alcança este bloco, e sem o
  // reset as contagens de chamada acumulam entre os testes daqui.
  //
  // `getAgentInsights` FALTAVA NESTA LISTA, e isso custou uma reprovação: o guarda
  // novo do período leu `mock.calls[0]` e recebeu a janela de 30 dias de um teste
  // ANTERIOR do bloco, não a de 90 que ele próprio pediu — `expected 30 to be 90`,
  // com a produção correta. É exatamente o acúmulo que o comentário acima já
  // nomeava, num espião que a lista não cobria.
  beforeEach(() => {
    vi.mocked(getAgent).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([activeAgent]);
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listMcpServers).mockResolvedValue([mcpServer]);
    vi.mocked(listKnowledgeBases).mockReset();
    vi.mocked(listKnowledgeBases).mockResolvedValue([knowledgeBase]);
    vi.mocked(getAgentInsights).mockReset();
    vi.mocked(getAgentInsights).mockResolvedValue(
      agentInsightsFixture({ agentId: activeAgent.id }),
    );
  });

  it('abre a aba pelo endereço e lista as bases vinculadas', async () => {
    vi.mocked(getAgent).mockResolvedValue({
      ...activeAgent,
      knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }],
    });

    renderPage(activeAgent.id, '?tab=conhecimento');

    expect(await screen.findByRole('tab', { name: /conhecimento/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(await screen.findByTestId(`knowledge-row-${knowledgeBase.id}`)).toBeInTheDocument();
  });

  it('acionar a aba passa a identificá-la na URL', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    const router = renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    await user.click(tab(/conhecimento/i));

    await waitFor(() => expect(router.state.location.search).toBe('?tab=conhecimento'));
  });

  // Guarda de D7: são 100+ bases declaradas, e as outras três abas não precisam
  // delas. O `enabled` de useKnowledgeBasesQuery existe desde a 5a-1 justamente
  // para isto.
  it('não requisita o catálogo de bases enquanto a aba não está ativa', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    expect(listKnowledgeBases).not.toHaveBeenCalled();
  });

  it('requisita o catálogo ao abrir a aba', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    const user = userEvent.setup();

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    await user.click(tab(/conhecimento/i));

    await waitFor(() => expect(listKnowledgeBases).toHaveBeenCalled());
  });

  it('falha ao carregar o catálogo informa o erro e não exibe a lista pela metade', async () => {
    vi.mocked(getAgent).mockResolvedValue({
      ...activeAgent,
      knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }],
    });
    vi.mocked(listKnowledgeBases).mockRejectedValue(new Error('falhou'));

    renderPage(activeAgent.id, '?tab=conhecimento');

    expect(
      await screen.findByText('Não foi possível carregar as bases de conhecimento.'),
    ).toBeInTheDocument();
    expect(screen.queryByTestId(`knowledge-row-${knowledgeBase.id}`)).not.toBeInTheDocument();
  });

  it('conteúdo da aba de conhecimento não está na página enquanto ela não é a ativa', async () => {
    vi.mocked(getAgent).mockResolvedValue({
      ...activeAgent,
      knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }],
    });

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    expect(screen.queryByTestId('knowledge-summary')).not.toBeInTheDocument();
    expect(screen.queryByText('Bases vinculadas')).not.toBeInTheDocument();
  });

  describe('a aba de Insights', () => {
    it('NUNCA exibe contador, por mais vínculos que o agente tenha', async () => {
      // As outras três contam ITENS VINCULADOS. Esta mede, e um número ao lado
      // do rótulo afirmaria uma quantidade que ela não tem.
      vi.mocked(getAgent).mockResolvedValue({
        ...activeAgent,
        mcpServers: [{ id: mcpServer.id, name: mcpServer.name, allowedTools: [] }],
        knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }],
        delegatesTo: [{ id: '99999999-9999-9999-9999-999999999999', name: 'Cobrança' }],
      });

      renderPage(activeAgent.id);

      await screen.findByRole('heading', { name: activeAgent.name });
      expect(tab(/insights/i).textContent).toBe('Insights');
      expect(within(tab(/insights/i)).queryByText(/^\d+$/)).not.toBeInTheDocument();
    });

    it('NÃO consulta as métricas enquanto a aba não está ativa', async () => {
      // O carregamento tardio sai do `keepMounted={false}`, não de um
      // `enabled:` — e é comportamento que se perde em silêncio se alguém
      // trocar `keepMounted`. A rota agregada é cara: a da página do sistema
      // levou 2,24 s contra o banco de dev.
      vi.mocked(getAgent).mockResolvedValue(activeAgent);

      renderPage(activeAgent.id);

      await screen.findByRole('heading', { name: activeAgent.name });
      expect(getAgentInsights).not.toHaveBeenCalled();
    });

    it('consulta as métricas ao abrir a aba', async () => {
      vi.mocked(getAgent).mockResolvedValue(activeAgent);
      const user = userEvent.setup();

      renderPage(activeAgent.id);

      await screen.findByRole('heading', { name: activeAgent.name });
      await user.click(tab(/insights/i));

      await waitFor(() => expect(getAgentInsights).toHaveBeenCalled());
      expect(vi.mocked(getAgentInsights).mock.calls[0][0]).toBe(activeAgent.id);
    });

    it('?tab=insights reabre a aba direto, e o conteúdo dela aparece', async () => {
      vi.mocked(getAgent).mockResolvedValue(activeAgent);

      renderPage(activeAgent.id, '?tab=insights');

      expect(await screen.findByTestId('aba-insights-do-agente')).toBeInTheDocument();
      expect(await screen.findByTestId('kpis-do-agente')).toBeInTheDocument();
    });

    it('acionar a aba escreve o endereço', async () => {
      vi.mocked(getAgent).mockResolvedValue(activeAgent);
      const user = userEvent.setup();

      const router = renderPage(activeAgent.id);

      await screen.findByRole('heading', { name: activeAgent.name });
      await user.click(tab(/insights/i));

      await waitFor(() => expect(router.state.location.search).toBe('?tab=insights'));
    });

    it('o conteúdo NÃO está na página enquanto a aba não é a ativa', async () => {
      vi.mocked(getAgent).mockResolvedValue(activeAgent);

      renderPage(activeAgent.id);

      await screen.findByRole('heading', { name: activeAgent.name });
      expect(screen.queryByTestId('aba-insights-do-agente')).not.toBeInTheDocument();
    });

    // ------------------------------ O PERÍODO NO ENDEREÇO (#85)

    it('o endereço decide o período da aba, e a consulta usa aquela janela', async () => {
      // A metade de DESTINO da travessia. A de origem está em
      // `SystemInsightsPage.test.tsx`; o caminho inteiro, em `router.test.tsx`,
      // porque guarda que afirma o meio do caminho não prova o fim dele.
      vi.mocked(getAgent).mockResolvedValue(activeAgent);

      renderPage(activeAgent.id, '?tab=insights&period=90d');

      await screen.findByTestId('kpis-do-agente');

      // A LARGURA, não os instantes (D8).
      const [, from, to] = vi.mocked(getAgentInsights).mock.calls[0];
      const dias = (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000);
      expect(dias).toBe(90);
      expect(screen.getByRole('radio', { name: '90d' })).toBeChecked();
    });

    it('aba SEM período no endereço abre no padrão, e o endereço NÃO é reescrito', async () => {
      // O caminho de todo link já compartilhado — inclusive o que a
      // `fechamento-da-l4` entregou, que leva `?tab=insights` e nada mais.
      vi.mocked(getAgent).mockResolvedValue(activeAgent);

      const router = renderPage(activeAgent.id, '?tab=insights');

      await screen.findByTestId('kpis-do-agente');

      expect(screen.getByRole('radio', { name: DEFAULT_INSIGHTS_PERIOD })).toBeChecked();
      expect(router.state.location.search).toBe('?tab=insights');
    });

    it('período NÃO RECONHECIDO abre no padrão sem quebrar, e o endereço NÃO é reescrito', async () => {
      // Mesmo contrato que `parseTab` já declara para a aba ao lado, e é por
      // simetria: duas chaves do mesmo endereço com disciplinas opostas seriam
      // duas regras sobre a mesma barra de endereços.
      vi.mocked(getAgent).mockResolvedValue(activeAgent);

      const router = renderPage(activeAgent.id, '?tab=insights&period=180d');

      await screen.findByTestId('kpis-do-agente');

      expect(screen.getByRole('radio', { name: DEFAULT_INSIGHTS_PERIOD })).toBeChecked();
      expect(router.state.location.search).toBe('?tab=insights&period=180d');
    });

    it('trocar o período escreve o endereço E mantém a aba', async () => {
      // As duas metades importam: `setSearchParams({ period })` escreveria o
      // período e apagaria `tab`, jogando o operador na visão geral — a aba
      // desapareceria debaixo do clique. É a régua da D5 do outro lado.
      vi.mocked(getAgent).mockResolvedValue(activeAgent);
      const user = userEvent.setup();

      const router = renderPage(activeAgent.id, '?tab=insights&period=90d');
      await screen.findByTestId('kpis-do-agente');

      await user.click(screen.getByRole('radio', { name: '7d' }));

      await waitFor(() =>
        expect(new URLSearchParams(router.state.location.search).get('period')).toBe('7d'),
      );
      expect(new URLSearchParams(router.state.location.search).get('tab')).toBe('insights');
      expect(await screen.findByTestId('aba-insights-do-agente')).toBeInTheDocument();
    });

    it('agente INATIVO abre a aba normalmente', async () => {
      // Inatividade é estado de cadastro, não ausência de sujeito: a rota
      // responde 200, e o que ele executou enquanto ativo continua medido.
      vi.mocked(getAgent).mockResolvedValue({ ...activeAgent, isActive: false });

      renderPage(activeAgent.id, '?tab=insights');

      expect(await screen.findByTestId('aba-insights-do-agente')).toBeInTheDocument();
      expect(screen.getByText('Inativo')).toBeInTheDocument();
    });
  });
});
