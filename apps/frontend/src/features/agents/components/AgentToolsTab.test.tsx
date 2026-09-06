import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentToolsTab } from './AgentToolsTab';
import { ApiError, replaceAgentMcpServers } from '../api/agentsApi';
import { listMcpServerTools } from '../../mcp-servers/api/mcpServersApi';
import type { Agent } from '../types/agent';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return { ...actual, replaceAgentMcpServers: vi.fn() };
});

vi.mock('../../mcp-servers/api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../mcp-servers/api/mcpServersApi')>();
  return { ...actual, listMcpServerTools: vi.fn() };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

const agent: Agent = {
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
  mcpServers: [{ id: 'srv-1', name: 'Zendesk MCP', allowedTools: ['read', 'ghost-tool'] }],
  delegatesTo: [],
  a2a: null,
};

function mcpServer(overrides: Partial<McpServer>): McpServer {
  return {
    id: 'srv-1',
    name: 'Zendesk MCP',
    description: 'Servidor MCP do Zendesk',
    url: 'https://mcp.zendesk.example/sse',
    authType: 'None',
    isActive: true,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

const catalog: McpServer[] = [
  mcpServer({ id: 'srv-1', name: 'Zendesk MCP', isActive: true }),
  mcpServer({ id: 'srv-2', name: 'Slack MCP', isActive: false }),
  mcpServer({ id: 'srv-3', name: 'Github MCP', isActive: true }),
];

function renderTab(overrides?: { agent?: Agent; mcpServers?: McpServer[] }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      {
        path: '/agents/:id',
        element: (
          <AgentToolsTab
            agent={overrides?.agent ?? agent}
            mcpServers={overrides?.mcpServers ?? catalog}
          />
        ),
      },
      { path: '/mcp-servers/new', element: <p>cadastro de servidor MCP</p> },
    ],
    { initialEntries: [`/agents/${agent.id}?tab=ferramentas`] },
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

async function row(serverId: string) {
  return within(await screen.findByTestId(`agent-mcp-server-row-${serverId}`));
}

describe('AgentToolsTab', () => {
  beforeEach(() => {
    vi.mocked(replaceAgentMcpServers).mockReset();
    vi.mocked(listMcpServerTools).mockReset();
    vi.mocked(notifications.show).mockReset();

    vi.mocked(listMcpServerTools).mockImplementation((id: string) => {
      if (id === 'srv-1') {
        return Promise.resolve({
          success: true,
          tools: [
            { name: 'read', description: 'Lê dados' },
            { name: 'write', description: 'Escreve dados' },
          ],
          failureReason: null,
          message: null,
        });
      }
      if (id === 'srv-3') {
        return Promise.resolve({
          success: false,
          tools: null,
          failureReason: 'HostUnreachable' as const,
          message: 'Não foi possível conectar ao host informado.',
        });
      }
      return Promise.resolve({
        success: true,
        tools: [{ name: 'post', description: 'Posta mensagem' }],
        failureReason: null,
        message: null,
      });
    });
  });

  it('pré-seleciona servidores já vinculados; ao expandir, marca tools permitidas e some com a que teve drift', async () => {
    const user = userEvent.setup();
    renderTab();

    expect((await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i })).toBeChecked();
    expect((await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i })).not.toBeChecked();
    expect((await row('srv-3')).getByRole('checkbox', { name: /github mcp/i })).not.toBeChecked();

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));

    expect(await (await row('srv-1')).findByRole('checkbox', { name: /^read/ })).toBeChecked();
    expect((await row('srv-1')).getByRole('checkbox', { name: /^write/ })).not.toBeChecked();
    expect((await row('srv-1')).queryByText('ghost-tool')).not.toBeInTheDocument();
  });

  it('não dispara busca de tools de nenhum servidor ao abrir a aba', async () => {
    renderTab();

    await screen.findByRole('checkbox', { name: /zendesk mcp/i });
    expect(listMcpServerTools).not.toHaveBeenCalled();
  });

  it('marcar um servidor vincula, expande e dispara a descoberta no mesmo gesto', async () => {
    const user = userEvent.setup();
    renderTab();

    const slackCheckbox = (await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i });
    await user.click(slackCheckbox);

    expect(slackCheckbox).toBeChecked();
    await waitFor(() => expect(listMcpServerTools).toHaveBeenCalledWith('srv-2'));
    expect(
      await (await row('srv-2')).findByRole('checkbox', { name: /^post/ }),
    ).toBeInTheDocument();
  });

  it('expandir sem marcar busca as tools e as exibe desabilitadas', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click((await row('srv-2')).getByRole('button', { name: /ver tools/i }));

    const postCheckbox = await (await row('srv-2')).findByRole('checkbox', { name: /^post/ });
    expect(postCheckbox).toBeDisabled();
    expect((await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i })).not.toBeChecked();
  });

  it('permite selecionar e desselecionar um servidor e marcar e desmarcar uma tool individual', async () => {
    const user = userEvent.setup();
    renderTab();

    const githubCheckbox = (await row('srv-3')).getByRole('checkbox', { name: /github mcp/i });
    await user.click(githubCheckbox);
    expect(githubCheckbox).toBeChecked();
    await user.click(githubCheckbox);
    expect(githubCheckbox).not.toBeChecked();

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));
    const writeCheckbox = await (await row('srv-1')).findByRole('checkbox', { name: /^write/ });
    await user.click(writeCheckbox);
    expect(writeCheckbox).toBeChecked();
    await user.click(writeCheckbox);
    expect(writeCheckbox).not.toBeChecked();
  });

  it('exibe o resumo de servidores vinculados e de tools permitidas', async () => {
    renderTab();

    expect(await screen.findByText(/1 servidor vinculado/)).toBeInTheDocument();
    expect(screen.getByText(/2 tools permitidas/)).toBeInTheDocument();
  });

  it('exibe o contador de tools por servidor e a indicação de não vinculado', async () => {
    const user = userEvent.setup();
    renderTab();

    expect((await row('srv-2')).getByText('Não vinculado')).toBeInTheDocument();

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));

    expect(await (await row('srv-1')).findByText('1 de 2 selecionadas')).toBeInTheDocument();
  });

  it('sinaliza servidor vinculado sem nenhuma tool, na linha e no topo da aba', async () => {
    const user = userEvent.setup();
    renderTab();

    expect(screen.queryByTestId('bound-without-tools-callout')).not.toBeInTheDocument();

    await user.click((await row('srv-3')).getByRole('checkbox', { name: /github mcp/i }));

    const callout = await screen.findByTestId('bound-without-tools-callout');
    expect(within(callout).getByText(/Github MCP/)).toBeInTheDocument();
    expect((await row('srv-3')).getByText('Sem tools')).toBeInTheDocument();
  });

  it('nomeia todos os servidores vinculados sem tools no aviso do topo', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click((await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i }));
    await user.click((await row('srv-3')).getByRole('checkbox', { name: /github mcp/i }));

    const callout = await screen.findByTestId('bound-without-tools-callout');
    expect(within(callout).getByText(/Slack MCP, Github MCP/)).toBeInTheDocument();
  });

  it('não trata como vínculo sem tools um servidor que não está vinculado', async () => {
    renderTab();

    await screen.findByRole('checkbox', { name: /zendesk mcp/i });
    expect(screen.queryByTestId('bound-without-tools-callout')).not.toBeInTheDocument();
    expect((await row('srv-2')).queryByText('Sem tools')).not.toBeInTheDocument();
  });

  it('avisa que um servidor inativo vinculado não oferece suas tools ao agente', async () => {
    const user = userEvent.setup();
    renderTab();

    expect(screen.queryByTestId('inactive-bound-callout-srv-2')).not.toBeInTheDocument();

    await user.click((await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i }));

    expect(await screen.findByTestId('inactive-bound-callout-srv-2')).toBeInTheDocument();
  });

  it('servidor inativo continua selecionável', async () => {
    const user = userEvent.setup();
    renderTab();

    const slackCheckbox = (await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i });
    expect(slackCheckbox).toBeEnabled();

    await user.click(slackCheckbox);
    expect(slackCheckbox).toBeChecked();
  });

  it('a barra de alterações não salvas só aparece quando há diferença, e some ao desfazer', async () => {
    const user = userEvent.setup();
    renderTab();

    await screen.findByRole('checkbox', { name: /zendesk mcp/i });
    expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument();

    const githubCheckbox = (await row('srv-3')).getByRole('checkbox', { name: /github mcp/i });
    await user.click(githubCheckbox);
    expect(await screen.findByTestId('unsaved-changes-bar')).toBeInTheDocument();

    await user.click(githubCheckbox);
    await waitFor(() =>
      expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument(),
    );
  });

  it('descartar devolve o rascunho ao vínculo salvo', async () => {
    const user = userEvent.setup();
    renderTab();

    const zendeskCheckbox = (await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i });
    await user.click(zendeskCheckbox);
    expect(zendeskCheckbox).not.toBeChecked();

    await user.click(await screen.findByRole('button', { name: 'Descartar' }));

    expect(zendeskCheckbox).toBeChecked();
    expect(replaceAgentMcpServers).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument(),
    );
  });

  it('submit envia um item por servidor selecionado e permanece na aba', async () => {
    vi.mocked(replaceAgentMcpServers).mockResolvedValue(agent);
    const user = userEvent.setup();
    const router = renderTab();

    await user.click((await row('srv-3')).getByRole('checkbox', { name: /github mcp/i }));
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    await waitFor(() =>
      expect(replaceAgentMcpServers).toHaveBeenCalledWith(agent.id, [
        { mcpServerId: 'srv-1', allowedTools: ['read', 'ghost-tool'] },
        { mcpServerId: 'srv-3', allowedTools: [] },
      ]),
    );
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(router.state.location.pathname).toBe(`/agents/${agent.id}`);
    expect(router.state.location.search).toBe('?tab=ferramentas');
  });

  it('submit sem nenhum servidor selecionado envia uma lista vazia', async () => {
    vi.mocked(replaceAgentMcpServers).mockResolvedValue({ ...agent, mcpServers: [] });
    const user = userEvent.setup();
    renderTab();

    await user.click((await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i }));
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    await waitFor(() => expect(replaceAgentMcpServers).toHaveBeenCalledWith(agent.id, []));
  });

  it('durante o salvamento indica a validação sem afirmar em qual servidor está', async () => {
    let resolveSave: ((value: Agent) => void) | undefined;
    vi.mocked(replaceAgentMcpServers).mockReturnValue(
      new Promise<Agent>((resolve) => {
        resolveSave = resolve;
      }),
    );
    const user = userEvent.setup();
    renderTab();

    await user.click((await row('srv-3')).getByRole('checkbox', { name: /github mcp/i }));
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    const bar = await screen.findByTestId('unsaved-changes-bar');
    expect(within(bar).getByText('Validando as tools nos servidores MCP…')).toBeInTheDocument();
    expect(within(bar).queryByText(/\d+\s*\/\s*\d+/)).not.toBeInTheDocument();
    expect(within(bar).getByRole('button', { name: 'Descartar' })).toBeDisabled();

    resolveSave?.(agent);
  });

  it('em erro 502, identifica o servidor culpado, mantém o rascunho e não sai da aba', async () => {
    vi.mocked(replaceAgentMcpServers).mockRejectedValue(
      new ApiError(502, 'Não foi possível validar as tools do servidor MCP srv-1.', {
        title: 'Não foi possível validar as tools do servidor MCP srv-1.',
        detail: 'Host inalcançável durante o handshake de validação.',
      }),
    );
    const user = userEvent.setup();
    renderTab();

    const githubCheckbox = (await row('srv-3')).getByRole('checkbox', { name: /github mcp/i });
    await user.click(githubCheckbox);
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    const alert = await screen.findByTestId('binding-submit-error');
    expect(
      within(alert).getByText('Não foi possível validar as tools do servidor MCP srv-1.'),
    ).toBeInTheDocument();
    expect(
      within(alert).getByText('Host inalcançável durante o handshake de validação.'),
    ).toBeInTheDocument();
    expect((await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i })).toBeChecked();
    expect(githubCheckbox).toBeChecked();
  });

  it('falha na descoberta de um servidor exibe o motivo e permite tentar novamente, sem bloquear os demais', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click((await row('srv-3')).getByRole('button', { name: /ver tools/i }));
    expect(
      await (await row('srv-3')).findByText('Não foi possível conectar ao host informado.'),
    ).toBeInTheDocument();

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));
    expect(
      await (await row('srv-1')).findByRole('checkbox', { name: /^read/ }),
    ).toBeInTheDocument();

    const callsBeforeRetry = vi
      .mocked(listMcpServerTools)
      .mock.calls.filter(([id]) => id === 'srv-3').length;
    await user.click((await row('srv-3')).getByRole('button', { name: /tentar novamente/i }));

    await waitFor(() =>
      expect(vi.mocked(listMcpServerTools).mock.calls.filter(([id]) => id === 'srv-3').length).toBe(
        callsBeforeRetry + 1,
      ),
    );
  });

  it('servidor com falha de descoberta continua selecionável, entrando com uma lista vazia de tools', async () => {
    vi.mocked(replaceAgentMcpServers).mockResolvedValue(agent);
    const user = userEvent.setup();
    renderTab();

    await user.click((await row('srv-3')).getByRole('button', { name: /ver tools/i }));
    const githubCheckbox = (await row('srv-3')).getByRole('checkbox', { name: /github mcp/i });
    expect(githubCheckbox).toBeEnabled();

    await user.click(githubCheckbox);
    await user.click((await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i }));
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    await waitFor(() =>
      expect(replaceAgentMcpServers).toHaveBeenCalledWith(agent.id, [
        { mcpServerId: 'srv-3', allowedTools: [] },
      ]),
    );
  });

  it('recolher e reabrir um servidor não repete a busca de tools', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));
    await (await row('srv-1')).findByRole('checkbox', { name: /^read/ });
    expect(listMcpServerTools).toHaveBeenCalledTimes(1);

    await user.click((await row('srv-1')).getByRole('button', { name: /ocultar tools/i }));
    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));

    await (await row('srv-1')).findByRole('checkbox', { name: /^read/ });
    expect(listMcpServerTools).toHaveBeenCalledTimes(1);
  });

  it('catálogo de servidores MCP vazio indica a ausência e oferece o cadastro, sem lista de seleção', async () => {
    renderTab({ mcpServers: [] });

    expect(await screen.findByText(/nenhum servidor mcp cadastrado/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /cadastrar servidor mcp/i })).toHaveAttribute(
      'href',
      '/mcp-servers/new',
    );
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /salvar vínculo/i })).not.toBeInTheDocument();
  });
});
