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

  it('exibe as quatro abas na ordem certa, com a visão geral ativa por padrão', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    expect(await screen.findByRole('tab', { name: /visão geral/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(tab(/ferramentas/i)).toBeInTheDocument();
    expect(tab(/conhecimento/i)).toBeInTheDocument();
    expect(tab(/delegações/i)).toBeInTheDocument();
    // Conhecimento é a terceira, entre Ferramentas e Delegações.
    expect(screen.getAllByRole('tab').map((element) => element.textContent)).toEqual([
      'Visão geral',
      'Ferramentas',
      'Conhecimento',
      'Delegações',
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
  beforeEach(() => {
    vi.mocked(getAgent).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([activeAgent]);
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listMcpServers).mockResolvedValue([mcpServer]);
    vi.mocked(listKnowledgeBases).mockReset();
    vi.mocked(listKnowledgeBases).mockResolvedValue([knowledgeBase]);
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
});
