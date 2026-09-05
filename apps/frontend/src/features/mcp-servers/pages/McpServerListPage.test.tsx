import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { McpServerListPage } from './McpServerListPage';
import { listMcpServers } from '../api/mcpServersApi';
import { listAgents } from '../../agents/api/agentsApi';
import type { McpServer } from '../types/mcpServer';
import type { Agent } from '../../agents/types/agent';

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

vi.mock('../api/mcpServersApi', () => ({
  listMcpServers: vi.fn(),
}));

const mcpServer: McpServer = {
  id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'BearerToken',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <McpServerListPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('McpServerListPage', () => {
  beforeEach(() => {
    vi.mocked(listMcpServers).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
  });

  it('exibe um indicador de carregamento enquanto a lista não chega', () => {
    vi.mocked(listMcpServers).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(screen.getByText('Carregando servidores MCP...')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Novo servidor MCP' })).toBeInTheDocument();
  });

  it('renderiza a lista quando existem servidores MCP cadastrados', async () => {
    vi.mocked(listMcpServers).mockResolvedValue([mcpServer]);

    renderPage();

    expect(await screen.findByRole('link', { name: mcpServer.name })).toBeInTheDocument();
  });

  it('exibe mensagem de lista vazia e mantém o botão de criar visível', async () => {
    vi.mocked(listMcpServers).mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText('Nenhum servidor MCP cadastrado ainda.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Novo servidor MCP' })).toBeInTheDocument();
  });

  it('exibe estado de erro quando a lista falha ao carregar', async () => {
    vi.mocked(listMcpServers).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(
      await screen.findByText('Não foi possível carregar os servidores MCP.'),
    ).toBeInTheDocument();
  });

  it('exibe a quantidade de agentes que usam cada servidor', async () => {
    vi.mocked(listMcpServers).mockResolvedValue([mcpServer]);
    const agent: Agent = {
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
      mcpServers: [{ id: mcpServer.id, name: mcpServer.name, allowedTools: [] }],
      delegatesTo: [],
    };
    vi.mocked(listAgents).mockResolvedValue([agent]);

    renderPage();

    expect(await screen.findByText('1 agente')).toBeInTheDocument();
    expect(screen.getByText('1 sem tools')).toBeInTheDocument();
  });

  it('falha ao carregar o catálogo de agentes não quebra a listagem de servidores', async () => {
    vi.mocked(listMcpServers).mockResolvedValue([mcpServer]);
    vi.mocked(listAgents).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(await screen.findByRole('link', { name: mcpServer.name })).toBeInTheDocument();
    expect(screen.getByText(mcpServer.url)).toBeInTheDocument();
  });
});
