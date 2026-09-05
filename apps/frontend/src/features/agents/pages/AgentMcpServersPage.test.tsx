import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentMcpServersPage } from './AgentMcpServersPage';
import { ApiError, getAgent, replaceAgentMcpServers } from '../api/agentsApi';
import { listMcpServers, listMcpServerTools } from '../../mcp-servers/api/mcpServersApi';
import type { Agent } from '../types/agent';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return {
    ...actual,
    getAgent: vi.fn(),
    replaceAgentMcpServers: vi.fn(),
  };
});

vi.mock('../../mcp-servers/api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../mcp-servers/api/mcpServersApi')>();
  return {
    ...actual,
    listMcpServers: vi.fn(),
    listMcpServerTools: vi.fn(),
  };
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
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [{ id: 'srv-1', name: 'Zendesk MCP', allowedTools: ['read', 'ghost-tool'] }],
  description: null,
  skills: [],
  delegatesTo: [],
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

function renderPage(id: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/agents/${id}/mcp-servers`]}>
          <Routes>
            <Route path="/agents/:id/mcp-servers" element={<AgentMcpServersPage />} />
            <Route path="/agents/:id" element={<div>Detalhe do agente</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

async function row(serverId: string) {
  return within(await screen.findByTestId(`agent-mcp-server-row-${serverId}`));
}

describe('AgentMcpServersPage', () => {
  beforeEach(() => {
    vi.mocked(getAgent).mockReset();
    vi.mocked(replaceAgentMcpServers).mockReset();
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listMcpServerTools).mockReset();
    vi.mocked(notifications.show).mockReset();

    vi.mocked(getAgent).mockResolvedValue(agent);
    vi.mocked(listMcpServers).mockResolvedValue(catalog);
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
          failureReason: 'HostUnreachable',
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

  it('pré-seleciona servidores já vinculados; ao expandir, marca tools permitidas e não exibe a que teve drift', async () => {
    const user = userEvent.setup();
    renderPage(agent.id);

    expect((await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i })).toBeChecked();
    expect((await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i })).not.toBeChecked();
    expect((await row('srv-3')).getByRole('checkbox', { name: /github mcp/i })).not.toBeChecked();

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));

    expect(await (await row('srv-1')).findByRole('checkbox', { name: 'read' })).toBeChecked();
    expect((await row('srv-1')).getByRole('checkbox', { name: 'write' })).not.toBeChecked();
    expect((await row('srv-1')).queryByText('ghost-tool')).not.toBeInTheDocument();
  });

  it('não dispara busca de tools de nenhum servidor ao carregar a página', async () => {
    renderPage(agent.id);

    await screen.findByRole('checkbox', { name: /zendesk mcp/i });
    expect(listMcpServerTools).not.toHaveBeenCalled();
  });

  it('permite selecionar/desselecionar um servidor e marcar/desmarcar uma tool individual', async () => {
    const user = userEvent.setup();
    renderPage(agent.id);

    const githubCheckbox = (await row('srv-3')).getByRole('checkbox', { name: /github mcp/i });
    await user.click(githubCheckbox);
    expect(githubCheckbox).toBeChecked();
    await user.click(githubCheckbox);
    expect(githubCheckbox).not.toBeChecked();

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));
    const writeCheckbox = await (await row('srv-1')).findByRole('checkbox', { name: 'write' });
    await user.click(writeCheckbox);
    expect(writeCheckbox).toBeChecked();
    await user.click(writeCheckbox);
    expect(writeCheckbox).not.toBeChecked();
  });

  it('submit envia o body envelopado com um item por servidor selecionado', async () => {
    vi.mocked(replaceAgentMcpServers).mockResolvedValue(agent);
    const user = userEvent.setup();
    renderPage(agent.id);

    await screen.findByRole('checkbox', { name: /zendesk mcp/i });
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    await waitFor(() =>
      expect(replaceAgentMcpServers).toHaveBeenCalledWith(agent.id, [
        { mcpServerId: 'srv-1', allowedTools: ['read', 'ghost-tool'] },
      ]),
    );
    expect(await screen.findByText('Detalhe do agente')).toBeInTheDocument();
  });

  it('submit sem nenhum servidor selecionado envia mcpServers: []', async () => {
    vi.mocked(replaceAgentMcpServers).mockResolvedValue({ ...agent, mcpServers: [] });
    const user = userEvent.setup();
    renderPage(agent.id);

    const zendeskCheckbox = await screen.findByRole('checkbox', { name: /zendesk mcp/i });
    await user.click(zendeskCheckbox);
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    await waitFor(() => expect(replaceAgentMcpServers).toHaveBeenCalledWith(agent.id, []));
  });

  it('em erro atômico 502, identifica o servidor culpado e preserva a seleção dos demais', async () => {
    vi.mocked(replaceAgentMcpServers).mockRejectedValue(
      new ApiError(502, 'Não foi possível validar as tools do servidor MCP srv-1.', {
        title: 'Não foi possível validar as tools do servidor MCP srv-1.',
        detail: 'Host inalcançável durante o handshake de validação.',
      }),
    );
    const user = userEvent.setup();
    renderPage(agent.id);

    const githubCheckbox = (await row('srv-3')).getByRole('checkbox', { name: /github mcp/i });
    await user.click(githubCheckbox);
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    expect(
      await screen.findByText('Não foi possível validar as tools do servidor MCP srv-1.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Host inalcançável durante o handshake de validação.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Detalhe do agente')).not.toBeInTheDocument();
    expect((await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i })).toBeChecked();
    expect(githubCheckbox).toBeChecked();
  });

  it('falha na descoberta de tools de um servidor exibe o motivo e permite tentar novamente, sem bloquear os demais', async () => {
    const user = userEvent.setup();
    renderPage(agent.id);

    await user.click((await row('srv-3')).getByRole('button', { name: /ver tools/i }));
    expect(
      await (await row('srv-3')).findByText('Não foi possível conectar ao host informado.'),
    ).toBeInTheDocument();

    // outro servidor continua funcionando normalmente
    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));
    expect(await (await row('srv-1')).findByRole('checkbox', { name: 'read' })).toBeInTheDocument();

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

  it('catálogo de servidores MCP vazio exibe indicação e não permite submit', async () => {
    vi.mocked(listMcpServers).mockResolvedValue([]);
    renderPage(agent.id);

    expect(await screen.findByText(/nenhum servidor mcp cadastrado/i)).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /salvar vínculo/i })).not.toBeInTheDocument();
  });

  it('agente inexistente exibe estado de "não encontrado"', async () => {
    vi.mocked(getAgent).mockRejectedValue(new ApiError(404, 'Não encontrado'));
    renderPage('inexistente');

    expect(await screen.findByText('Agente não encontrado.')).toBeInTheDocument();
  });

  it('recolher e reabrir um servidor não repete a busca de tools', async () => {
    const user = userEvent.setup();
    renderPage(agent.id);

    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));
    await (await row('srv-1')).findByRole('checkbox', { name: 'read' });
    expect(listMcpServerTools).toHaveBeenCalledTimes(1);

    await user.click((await row('srv-1')).getByRole('button', { name: /ocultar tools/i }));
    await user.click((await row('srv-1')).getByRole('button', { name: /ver tools/i }));

    await (await row('srv-1')).findByRole('checkbox', { name: 'read' });
    expect(listMcpServerTools).toHaveBeenCalledTimes(1);
  });

  it('servidor inativo continua selecionável (checkbox não desabilitado)', async () => {
    const user = userEvent.setup();
    renderPage(agent.id);

    const slackCheckbox = (await row('srv-2')).getByRole('checkbox', { name: /slack mcp/i });
    expect(slackCheckbox).toBeEnabled();

    await user.click(slackCheckbox);
    expect(slackCheckbox).toBeChecked();
  });

  it('servidor com falha de descoberta continua selecionável, entrando com allowedTools: []', async () => {
    vi.mocked(replaceAgentMcpServers).mockResolvedValue(agent);
    const user = userEvent.setup();
    renderPage(agent.id);

    await user.click((await row('srv-3')).getByRole('button', { name: /ver tools/i }));
    const githubCheckbox = (await row('srv-3')).getByRole('checkbox', { name: /github mcp/i });
    expect(githubCheckbox).toBeEnabled();

    await user.click(githubCheckbox);
    expect(githubCheckbox).toBeChecked();

    // desmarca o vínculo pré-existente pra isolar o binding do srv-3 no submit
    await user.click((await row('srv-1')).getByRole('checkbox', { name: /zendesk mcp/i }));
    await user.click(screen.getByRole('button', { name: /salvar vínculo/i }));

    await waitFor(() =>
      expect(replaceAgentMcpServers).toHaveBeenCalledWith(agent.id, [
        { mcpServerId: 'srv-3', allowedTools: [] },
      ]),
    );
  });
});
