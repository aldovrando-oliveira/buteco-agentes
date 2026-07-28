import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { theme } from '../../../theme';
import { AgentDetailPage } from './AgentDetailPage';
import { ApiError, getAgent } from '../api/agentsApi';
import type { Agent } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return { ...actual, getAgent: vi.fn() };
});

const agent: Agent = {
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
};

function renderPage(id: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/agents/${id}`]}>
          <Routes>
            <Route path="/agents/:id" element={<AgentDetailPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('AgentDetailPage', () => {
  beforeEach(() => {
    vi.mocked(getAgent).mockReset();
  });

  it('exibe nome, instruções e datas do agente, sem controles de edição/exclusão', async () => {
    vi.mocked(getAgent).mockResolvedValue(agent);

    renderPage(agent.id);

    expect(await screen.findByRole('heading', { name: agent.name })).toBeInTheDocument();
    expect(screen.getByText(agent.instructions)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /excluir/i })).not.toBeInTheDocument();
  });

  it('exibe estado de "não encontrado" quando o agente não existe', async () => {
    vi.mocked(getAgent).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage('inexistente');

    expect(await screen.findByText('Agente não encontrado.')).toBeInTheDocument();
  });
});
