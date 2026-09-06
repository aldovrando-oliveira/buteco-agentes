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
import { listProviders } from '../api/providersApi';
import type { Agent, ProviderCatalogEntry } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return { ...actual, getAgent: vi.fn(), updateAgent: vi.fn() };
});

vi.mock('../api/providersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/providersApi')>();
  return { ...actual, listProviders: vi.fn() };
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
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
};

const defaultProviders: ProviderCatalogEntry[] = [
  { id: 'openai', models: ['gpt-5.6-sol', 'gpt-5.6-terra'] },
  { id: 'anthropic', models: ['claude-opus-5', 'claude-sonnet-5'] },
];

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
    vi.mocked(listProviders).mockReset();
    vi.mocked(listProviders).mockResolvedValue(defaultProviders);
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
    expect(screen.getByRole('combobox', { name: /provedor/i })).toHaveValue('openai');
    expect(screen.getByRole('combobox', { name: /modelo/i })).toHaveValue('gpt-5.6-sol');

    await user.clear(screen.getByLabelText(/nome/i));
    await user.type(screen.getByLabelText(/nome/i), updatedAgent.name);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith(`/agents/${agent.id}`));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
  });

  it('salvar alterando só o nome preserva description e skills carregadas do agente (regressão: PUT sem os campos apagava os dois)', async () => {
    const agentWithSkills: Agent = {
      ...agent,
      description: 'Atende o financeiro',
      skills: [
        { name: 'Segunda via de boleto', description: 'Emite boleto atualizado' },
        { name: 'Cobrança', description: null },
      ],
    };
    vi.mocked(getAgent).mockResolvedValue(agentWithSkills);
    vi.mocked(updateAgent).mockResolvedValue({ ...agentWithSkills, name: 'Financeiro' });
    const user = userEvent.setup();

    renderPage(agent.id);

    expect(await screen.findByLabelText(/^descrição$/i)).toHaveValue('Atende o financeiro');
    await user.clear(screen.getByLabelText(/^nome\s*\*?$/i));
    await user.type(screen.getByLabelText(/^nome\s*\*?$/i), 'Financeiro');
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(updateAgent).toHaveBeenCalledWith(agent.id, {
        name: 'Financeiro',
        instructions: agent.instructions,
        provider: 'openai',
        model: 'gpt-5.6-sol',
        description: 'Atende o financeiro',
        skills: [
          { name: 'Segunda via de boleto', description: 'Emite boleto atualizado' },
          { name: 'Cobrança', description: null },
        ],
      }),
    );
  });

  it('limpar a descrição e remover todas as skills envia description: null e skills: [] explicitamente', async () => {
    const agentWithSkills: Agent = {
      ...agent,
      description: 'Atende o financeiro',
      skills: [{ name: 'Cobrança', description: null }],
    };
    vi.mocked(getAgent).mockResolvedValue(agentWithSkills);
    vi.mocked(updateAgent).mockResolvedValue(agent);
    const user = userEvent.setup();

    renderPage(agent.id);

    await user.clear(await screen.findByLabelText(/^descrição$/i));
    await user.click(screen.getByRole('button', { name: 'Remover skill 1' }));
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(updateAgent).toHaveBeenCalledWith(
        agent.id,
        expect.objectContaining({ description: null, skills: [] }),
      ),
    );
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

  it('ao clicar em "Cancelar", navega para o detalhe do agente em edição sem enviar requisição', async () => {
    vi.mocked(getAgent).mockResolvedValue(agent);
    const user = userEvent.setup();

    renderPage(agent.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(navigateMock).toHaveBeenCalledWith(`/agents/${agent.id}`);
    expect(updateAgent).not.toHaveBeenCalled();
  });

  it('quando GET /providers não retorna nenhum provider configurado, exibe mensagem de bloqueio em vez do formulário', async () => {
    vi.mocked(getAgent).mockResolvedValue(agent);
    vi.mocked(listProviders).mockResolvedValue([]);

    renderPage(agent.id);

    expect(await screen.findByText(/nenhum provedor de llm configurado/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/nome/i)).not.toBeInTheDocument();
  });

  it('quando o provider do agente não consta nas opções disponíveis, exibe a opção sintética desabilitada', async () => {
    vi.mocked(getAgent).mockResolvedValue({
      ...agent,
      provider: 'gemini',
      model: 'gemini-3.6-flash',
    });
    const user = userEvent.setup();

    renderPage(agent.id);

    const providerSelect = await screen.findByRole('combobox', { name: /provedor/i });
    expect(providerSelect).toHaveValue('gemini (indisponível)');

    await user.click(providerSelect);
    const staleOption = screen.getByRole('option', { name: 'gemini (indisponível)' });
    expect(staleOption).toHaveAttribute('data-combobox-disabled', 'true');
  });

  it('volta para o registro que está sendo editado, e não para a listagem', async () => {
    vi.mocked(getAgent).mockResolvedValue(agent);

    renderPage(agent.id);

    // De "editar X" quer-se voltar para X, que é de onde se veio — não para a
    // lista inteira.
    expect(await screen.findByRole('link', { name: 'Voltar ao agente' })).toHaveAttribute(
      'href',
      `/agents/${agent.id}`,
    );
  });
});
