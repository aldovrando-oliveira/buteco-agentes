import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentCreatePage } from './AgentCreatePage';
import { ApiError, createAgent } from '../api/agentsApi';
import { listProviders } from '../api/providersApi';
import type { Agent, ProviderCatalogEntry } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return { ...actual, createAgent: vi.fn() };
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

const createdAgent: Agent = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  delegatesTo: [],
};

const defaultProviders: ProviderCatalogEntry[] = [
  { id: 'openai', models: ['gpt-5.6-sol', 'gpt-5.6-terra'] },
];

function renderPage() {
  const queryClient = new QueryClient();
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AgentCreatePage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

async function selectOption(
  user: ReturnType<typeof userEvent.setup>,
  label: RegExp,
  optionName: string,
) {
  await user.click(screen.getByRole('combobox', { name: label }));
  await user.click(await screen.findByRole('option', { name: optionName }));
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByLabelText(/nome/i);
  await user.type(screen.getByLabelText(/nome/i), createdAgent.name);
  await user.type(screen.getByLabelText(/instruções/i), createdAgent.instructions);
  await selectOption(user, /provedor/i, 'openai');
  await selectOption(user, /modelo/i, 'gpt-5.6-sol');
  await user.click(screen.getByRole('button', { name: /criar agente/i }));
}

describe('AgentCreatePage', () => {
  beforeEach(() => {
    vi.mocked(createAgent).mockReset();
    vi.mocked(listProviders).mockReset();
    vi.mocked(listProviders).mockResolvedValue(defaultProviders);
    navigateMock.mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('em sucesso, navega para o detalhe do agente criado e notifica sucesso', async () => {
    vi.mocked(createAgent).mockResolvedValue(createdAgent);
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith(`/agents/${createdAgent.id}`));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
  });

  it('em erro 400, aplica os erros nos campos certos do formulário e não navega', async () => {
    vi.mocked(createAgent).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { name: ['O nome do agente é obrigatório.'] },
      }),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(await screen.findByText('O nome do agente é obrigatório.')).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em falha de rede/servidor, notifica erro genérico, não navega e preserva os dados do formulário', async () => {
    vi.mocked(createAgent).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/nome/i)).toHaveValue(createdAgent.name);
    expect(screen.getByLabelText(/instruções/i)).toHaveValue(createdAgent.instructions);
  });

  it('ao clicar em "Cancelar", navega para a listagem de agentes sem enviar requisição', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByLabelText(/nome/i);
    await user.type(screen.getByLabelText(/nome/i), createdAgent.name);
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(navigateMock).toHaveBeenCalledWith('/agents');
    expect(createAgent).not.toHaveBeenCalled();
  });

  it('quando GET /providers não retorna nenhum provider configurado, exibe mensagem de bloqueio em vez do formulário', async () => {
    vi.mocked(listProviders).mockResolvedValue([]);
    renderPage();

    expect(await screen.findByText(/nenhum provedor de llm configurado/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/nome/i)).not.toBeInTheDocument();
  });
});
