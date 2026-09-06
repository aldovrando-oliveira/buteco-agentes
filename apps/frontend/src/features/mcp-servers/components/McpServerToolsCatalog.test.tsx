import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { McpServerToolsCatalog } from './McpServerToolsCatalog';
import { listMcpServerTools } from '../api/mcpServersApi';
import type { Agent } from '../../agents/types/agent';

vi.mock('../api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/mcpServersApi')>();
  return { ...actual, listMcpServerTools: vi.fn() };
});

const MCP_SERVER_ID = 'srv-1';

function agent(overrides: Partial<Agent>): Agent {
  return {
    id: 'agent-1',
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
    a2a: null,
    ...overrides,
  };
}

function renderCatalog(agents?: Agent[]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <McpServerToolsCatalog mcpServerId={MCP_SERVER_ID} agents={agents} />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('McpServerToolsCatalog', () => {
  beforeEach(() => {
    vi.mocked(listMcpServerTools).mockReset();
    vi.mocked(listMcpServerTools).mockResolvedValue({
      success: true,
      tools: [
        { name: 'read', description: 'Lê dados' },
        { name: 'write', description: 'Escreve dados' },
      ],
      failureReason: null,
      message: null,
    });
  });

  it('começa ocioso, sem consultar o servidor', () => {
    renderCatalog();

    expect(screen.getByText(/As tools são descobertas ao vivo/)).toBeInTheDocument();
    expect(listMcpServerTools).not.toHaveBeenCalled();
  });

  it('consultar exibe o nome e a descrição de cada tool', async () => {
    const user = userEvent.setup();
    renderCatalog();

    await user.click(screen.getByRole('button', { name: 'Atualizar' }));

    expect(await screen.findByText('read')).toBeInTheDocument();
    expect(screen.getByText('Lê dados')).toBeInTheDocument();
    expect(screen.getByText('write')).toBeInTheDocument();
  });

  it('indica quando o servidor não oferece nenhuma tool', async () => {
    vi.mocked(listMcpServerTools).mockResolvedValue({
      success: true,
      tools: [],
      failureReason: null,
      message: null,
    });
    const user = userEvent.setup();
    renderCatalog();

    await user.click(screen.getByRole('button', { name: 'Atualizar' }));

    expect(await screen.findByText('Este servidor não oferece nenhuma tool.')).toBeInTheDocument();
  });

  it('exibe o motivo da falha e permite tentar novamente', async () => {
    vi.mocked(listMcpServerTools).mockResolvedValue({
      success: false,
      tools: null,
      failureReason: 'HostUnreachable',
      message: 'Não foi possível conectar ao host informado.',
    });
    const user = userEvent.setup();
    renderCatalog();

    await user.click(screen.getByRole('button', { name: 'Atualizar' }));

    expect(
      await screen.findByText('Não foi possível conectar ao host informado.'),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /tentar novamente/i }));

    await waitFor(() => expect(listMcpServerTools).toHaveBeenCalledTimes(2));
  });

  it('indica em quantos agentes cada tool está permitida', async () => {
    const user = userEvent.setup();
    renderCatalog([
      agent({ id: 'a1', mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: ['read'] }] }),
      agent({ id: 'a2', mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: ['read'] }] }),
    ]);

    await user.click(screen.getByRole('button', { name: 'Atualizar' }));

    const readRow = within(await screen.findByTestId('tool-row-read'));
    expect(readRow.getByText('permitida em 2 agentes')).toBeInTheDocument();
  });

  it('identifica tool que não está permitida em nenhum agente', async () => {
    const user = userEvent.setup();
    renderCatalog([
      agent({ mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: ['read'] }] }),
    ]);

    await user.click(screen.getByRole('button', { name: 'Atualizar' }));

    const writeRow = within(await screen.findByTestId('tool-row-write'));
    expect(writeRow.getByText('não permitida em nenhum agente')).toBeInTheDocument();
  });

  it('usa o singular quando a tool está permitida em um único agente', async () => {
    const user = userEvent.setup();
    renderCatalog([
      agent({ mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: ['read'] }] }),
    ]);

    await user.click(screen.getByRole('button', { name: 'Atualizar' }));

    const readRow = within(await screen.findByTestId('tool-row-read'));
    expect(readRow.getByText('permitida em 1 agente')).toBeInTheDocument();
  });
});
