import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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
      a2a: null,
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

  const outroServidor: McpServer = {
    ...mcpServer,
    id: 'ffffffff-ffff-ffff-ffff-ffffffffffff',
    name: 'Notas Fiscais',
    url: 'https://mcp.notas.example/sse',
  };

  async function renderWithServers(servers: McpServer[]) {
    vi.mocked(listMcpServers).mockResolvedValue(servers);
    renderPage();
    await screen.findByLabelText('Buscar por nome ou url');
  }

  // Restrito à tabela: fora dela existe o link de cadastrar servidor.
  function visibleServerNames() {
    const table = screen.queryByRole('table');
    return table
      ? within(table)
          .getAllByRole('link')
          .map((link) => link.textContent)
      : [];
  }

  it('exibe a quantidade de servidores cadastrados', async () => {
    await renderWithServers([mcpServer, outroServidor]);

    expect(screen.getByText(/2 servidores cadastrados/)).toBeInTheDocument();
  });

  it('usa o singular quando há um único servidor cadastrado', async () => {
    await renderWithServers([mcpServer]);

    expect(screen.getByText(/1 servidor cadastrado/)).toBeInTheDocument();
  });

  it('busca pelo nome do servidor', async () => {
    const user = userEvent.setup();
    await renderWithServers([mcpServer, outroServidor]);

    await user.type(screen.getByLabelText('Buscar por nome ou url'), 'notas');

    expect(visibleServerNames()).toEqual(['Notas Fiscais']);
  });

  it('busca pela url do servidor', async () => {
    const user = userEvent.setup();
    await renderWithServers([mcpServer, outroServidor]);

    await user.type(screen.getByLabelText('Buscar por nome ou url'), 'notas.example');

    expect(visibleServerNames()).toEqual(['Notas Fiscais']);
  });

  it('busca ignorando acentuação e caixa', async () => {
    const user = userEvent.setup();
    await renderWithServers([mcpServer, outroServidor]);

    await user.type(screen.getByLabelText('Buscar por nome ou url'), 'NOTAS FISCAIS');

    expect(visibleServerNames()).toEqual(['Notas Fiscais']);
  });

  it('mensagem de nenhum resultado é distinta da de catálogo vazio', async () => {
    const user = userEvent.setup();
    await renderWithServers([mcpServer]);

    await user.type(screen.getByLabelText('Buscar por nome ou url'), 'inexistente');

    expect(screen.getByText('Nenhum servidor corresponde à busca.')).toBeInTheDocument();
    expect(screen.queryByText('Nenhum servidor MCP cadastrado ainda.')).not.toBeInTheDocument();
  });

  it('catálogo vazio não exibe campo de busca', async () => {
    vi.mocked(listMcpServers).mockResolvedValue([]);

    renderPage();

    await screen.findByText('Nenhum servidor MCP cadastrado ainda.');
    expect(screen.queryByLabelText('Buscar por nome ou url')).not.toBeInTheDocument();
  });

  it('mantém a busca alcançável sem rótulo visível', async () => {
    await renderWithServers([mcpServer]);

    // O rótulo visível saiu; o texto de exemplo virou o nome acessível. Sem
    // isto, a busca deixaria de ser alcançável por quem usa leitor de tela.
    expect(screen.getByLabelText('Buscar por nome ou url')).toBeInTheDocument();
    expect(screen.queryByText('Buscar')).not.toBeInTheDocument();
  });
});
