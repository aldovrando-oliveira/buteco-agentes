import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentDetailPage } from './AgentDetailPage';
import { ApiError, activateAgent, deactivateAgent, getAgent, listAgents } from '../api/agentsApi';
import type { Agent } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return {
    ...actual,
    getAgent: vi.fn(),
    listAgents: vi.fn(),
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
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
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
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([activeAgent]);
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

  it('renderiza o card de skills depois do card de detalhe, com as skills do agente', async () => {
    vi.mocked(getAgent).mockResolvedValue({
      ...activeAgent,
      skills: [{ name: 'Segunda via de boleto', description: 'Emite boleto atualizado' }],
    });

    renderPage(activeAgent.id);

    await screen.findByRole('heading', { name: activeAgent.name });
    const skillsCard = screen.getByTestId('agent-skills-card');
    expect(within(skillsCard).getByText('Segunda via de boleto')).toBeInTheDocument();
    expect(within(skillsCard).getByText('Emite boleto atualizado')).toBeInTheDocument();

    const instructions = screen.getByText(activeAgent.instructions);
    expect(
      instructions.compareDocumentPosition(skillsCard) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('exibe o link para a página de gestão de servidores MCP', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    expect(await screen.findByRole('link', { name: /gerenciar servidores mcp/i })).toHaveAttribute(
      'href',
      `/agents/${activeAgent.id}/mcp-servers`,
    );
  });

  it('exibe o bloco de ações (Editar, Ativar/Desativar) antes dos dados do agente na ordem do DOM', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);

    renderPage(activeAgent.id);

    const editLink = await screen.findByRole('link', { name: /editar/i });
    const nameHeading = screen.getByRole('heading', { name: activeAgent.name });

    expect(
      editLink.compareDocumentPosition(nameHeading) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
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
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: /cancelar/i }));

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

  it('exibe a seção de delegações posicionada depois do card de detalhe', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    vi.mocked(listAgents).mockResolvedValue([activeAgent]);

    renderPage(activeAgent.id);

    const nameHeading = await screen.findByRole('heading', { name: activeAgent.name });
    const delegationsHeading = await screen.findByRole('heading', { name: /delegações de saída/i });

    expect(
      nameHeading.compareDocumentPosition(delegationsHeading) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('quando o catálogo de agentes tem apenas o próprio agente, a página renderiza normalmente sem opções de delegação', async () => {
    vi.mocked(getAgent).mockResolvedValue(activeAgent);
    vi.mocked(listAgents).mockResolvedValue([activeAgent]);

    renderPage(activeAgent.id);

    expect(
      await screen.findByRole('heading', { name: /delegações de saída/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /salvar delegações/i })).toBeInTheDocument();
  });
});
