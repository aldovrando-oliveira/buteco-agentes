import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentEditPage } from './AgentEditPage';
import { ApiError, getAgent, updateAgent } from '../api/agentsApi';
import type { Agent } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return { ...actual, getAgent: vi.fn(), updateAgent: vi.fn() };
});

const navigateMock = vi.fn();
vi.mock('react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router')>();
  return { ...actual, useNavigate: () => navigateMock };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

const agent: Agent = {
  id: '66666666-6666-6666-6666-666666666666',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
};

function renderPage(id: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/agents/${id}/edit`]}>
          <Routes>
            <Route path="/agents/:id/edit" element={<AgentEditPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('AgentEditPage', () => {
  beforeEach(() => {
    vi.mocked(getAgent).mockReset();
    vi.mocked(updateAgent).mockReset();
    navigateMock.mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe o formulário pré-preenchido e, em sucesso, navega para o detalhe e notifica sucesso', async () => {
    vi.mocked(getAgent).mockResolvedValue(agent);
    const updatedAgent = { ...agent, name: 'Atendente Sênior' };
    vi.mocked(updateAgent).mockResolvedValue(updatedAgent);
    const user = userEvent.setup();

    renderPage(agent.id);

    expect(await screen.findByLabelText(/nome/i)).toHaveValue(agent.name);
    expect(screen.getByLabelText(/instruções/i)).toHaveValue(agent.instructions);

    await user.clear(screen.getByLabelText(/nome/i));
    await user.type(screen.getByLabelText(/nome/i), updatedAgent.name);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith(`/agents/${agent.id}`));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
  });

  it('em erro 400, aplica os erros nos campos certos do formulário e não navega', async () => {
    vi.mocked(getAgent).mockResolvedValue(agent);
    vi.mocked(updateAgent).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { name: ['O nome do agente é obrigatório.'] },
      }),
    );
    const user = userEvent.setup();

    renderPage(agent.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(await screen.findByText('O nome do agente é obrigatório.')).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em falha de rede/servidor, notifica erro genérico, não navega e preserva os dados editados', async () => {
    vi.mocked(getAgent).mockResolvedValue(agent);
    vi.mocked(updateAgent).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();

    renderPage(agent.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/nome/i)).toHaveValue(agent.name);
  });
});
