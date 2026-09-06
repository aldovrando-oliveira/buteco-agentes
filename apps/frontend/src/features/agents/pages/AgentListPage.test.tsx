import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { AgentListPage } from './AgentListPage';
import { listAgents } from '../api/agentsApi';
import type { Agent } from '../types/agent';

vi.mock('../api/agentsApi', () => ({
  listAgents: vi.fn(),
}));

const agent: Agent = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AgentListPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('AgentListPage', () => {
  beforeEach(() => {
    vi.mocked(listAgents).mockReset();
  });

  it('exibe um indicador de carregamento enquanto a lista não chega', () => {
    vi.mocked(listAgents).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(screen.getByText('Carregando agentes...')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Novo agente' })).toBeInTheDocument();
  });

  it('renderiza a lista quando existem agentes cadastrados', async () => {
    vi.mocked(listAgents).mockResolvedValue([agent]);

    renderPage();

    expect(await screen.findByRole('link', { name: agent.name })).toBeInTheDocument();
  });

  it('exibe mensagem de lista vazia e mantém o botão de criar visível', async () => {
    vi.mocked(listAgents).mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText('Nenhum agente cadastrado ainda.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Novo agente' })).toBeInTheDocument();
  });

  it('exibe estado de erro quando a lista falha ao carregar', async () => {
    vi.mocked(listAgents).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(await screen.findByText('Não foi possível carregar os agentes.')).toBeInTheDocument();
  });

  const cobranca: Agent = {
    ...agent,
    id: '22222222-2222-2222-2222-222222222222',
    name: 'Cobrança Ativa',
    description: 'Negocia dívidas em atraso',
  };

  const inativo: Agent = {
    ...agent,
    id: '33333333-3333-3333-3333-333333333333',
    name: 'Triagem',
    isActive: false,
  };

  const semProvedor: Agent = {
    ...agent,
    id: '44444444-4444-4444-4444-444444444444',
    name: 'Legado',
    provider: null,
    model: null,
  };

  async function renderWithAgents(agents: Agent[]) {
    vi.mocked(listAgents).mockResolvedValue(agents);
    renderPage();
    await screen.findByLabelText('Buscar');
  }

  // Restrito à tabela: fora dela existe o link de cadastrar agente.
  function visibleAgentNames() {
    const table = screen.queryByRole('table');
    return table ? within(table).getAllByRole('link').map((link) => link.textContent) : [];
  }

  it('exibe a quantidade de agentes cadastrados', async () => {
    await renderWithAgents([agent, cobranca]);

    expect(screen.getByText(/2 agentes cadastrados/)).toBeInTheDocument();
  });

  it('usa o singular quando há um único agente cadastrado', async () => {
    await renderWithAgents([agent]);

    expect(screen.getByText(/1 agente cadastrado/)).toBeInTheDocument();
  });

  it('busca por nome do agente', async () => {
    const user = userEvent.setup();
    await renderWithAgents([agent, cobranca]);

    await user.type(screen.getByLabelText('Buscar'), 'triagem');

    expect(screen.getByText('Nenhum agente corresponde à busca.')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Buscar'));
    await user.type(screen.getByLabelText('Buscar'), 'atendente');

    expect(visibleAgentNames()).toContain('Atendente');
    expect(visibleAgentNames()).not.toContain('Cobrança Ativa');
  });

  it('busca também pela descrição do agente', async () => {
    const user = userEvent.setup();
    await renderWithAgents([agent, cobranca]);

    await user.type(screen.getByLabelText('Buscar'), 'dívidas');

    expect(visibleAgentNames()).toEqual(['Cobrança Ativa']);
  });

  it('busca ignorando acentuação e caixa', async () => {
    const user = userEvent.setup();
    await renderWithAgents([agent, cobranca]);

    await user.type(screen.getByLabelText('Buscar'), 'COBRANCA');

    expect(visibleAgentNames()).toEqual(['Cobrança Ativa']);
  });

  it('filtra por agentes ativos e inativos', async () => {
    const user = userEvent.setup();
    await renderWithAgents([agent, inativo]);

    await user.click(screen.getByRole('radio', { name: 'Ativos' }));
    expect(visibleAgentNames()).toEqual(['Atendente']);

    await user.click(screen.getByRole('radio', { name: 'Inativos' }));
    expect(visibleAgentNames()).toEqual(['Triagem']);

    await user.click(screen.getByRole('radio', { name: 'Todos' }));
    expect(visibleAgentNames()).toHaveLength(2);
  });

  it('filtra pelos agentes que precisam de reconfiguração', async () => {
    const user = userEvent.setup();
    await renderWithAgents([agent, semProvedor]);

    await user.click(screen.getByRole('radio', { name: 'Reconfigurar' }));

    expect(visibleAgentNames()).toEqual(['Legado']);
  });

  it('aplica busca e filtro em conjunto', async () => {
    const user = userEvent.setup();
    await renderWithAgents([agent, cobranca, inativo]);

    await user.type(screen.getByLabelText('Buscar'), 'a');
    await user.click(screen.getByRole('radio', { name: 'Inativos' }));

    expect(visibleAgentNames()).toEqual(['Triagem']);
  });

  it('mensagem de nenhum resultado é distinta da de catálogo vazio', async () => {
    const user = userEvent.setup();
    await renderWithAgents([agent]);

    await user.type(screen.getByLabelText('Buscar'), 'inexistente');

    expect(screen.getByText('Nenhum agente corresponde à busca.')).toBeInTheDocument();
    expect(screen.queryByText('Nenhum agente cadastrado ainda.')).not.toBeInTheDocument();
  });

  it('catálogo vazio não exibe campo de busca nem filtro', async () => {
    vi.mocked(listAgents).mockResolvedValue([]);

    renderPage();

    await screen.findByText('Nenhum agente cadastrado ainda.');
    expect(screen.queryByLabelText('Buscar')).not.toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: 'Ativos' })).not.toBeInTheDocument();
  });
});
