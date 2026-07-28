import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentDetailPage } from './AgentDetailPage';
import { ApiError, activateAgent, deactivateAgent, getAgent } from '../api/agentsApi';
import type { Agent } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return {
    ...actual,
    getAgent: vi.fn(),
    activateAgent: vi.fn(),
    deactivateAgent: vi.fn(),
  };
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
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
};

const inactiveAgent: Agent = { ...activeAgent, isActive: false };

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
    vi.mocked(activateAgent).mockReset();
    vi.mocked(deactivateAgent).mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe nome, instruções, datas, estado, link de edição e ação de desativar de um agente ativo', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    expect(await screen.findByRole('heading', { name: activeAgent.name })).toBeInTheDocument();
    expect(screen.getByText(activeAgent.instructions)).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /editar/i })).toHaveAttribute(
      'href',
      `/agents/${activeAgent.id}/edit`,
    );
    expect(screen.getByRole('button', { name: /desativar/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^ativar$/i })).not.toBeInTheDocument();
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
    await screen.findByRole('dialog');
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(deactivateAgent).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByText('Ativo')).toBeInTheDocument();
  });

  it('confirmar a desativação envia a requisição, notifica sucesso e atualiza o indicador para inativo', async () => {
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

  it('em falha de rede/servidor ao ativar ou desativar, notifica erro genérico e mantém o estado anterior', async () => {
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
});
