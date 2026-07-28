import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
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
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
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
});
