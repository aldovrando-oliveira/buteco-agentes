import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { ChannelCreatePage } from './ChannelCreatePage';
import { ApiError, createChannel } from '../api/channelsApi';
import { listAgents } from '../../agents/api/agentsApi';
import type { Channel } from '../types/channel';
import type { Agent } from '../../agents/types/agent';

vi.mock('../api/channelsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/channelsApi')>();
  return { ...actual, createChannel: vi.fn() };
});

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
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

const createdChannel: Channel = {
  id: '77777777-7777-7777-7777-777777777777',
  channelType: 'waha',
  name: 'Canal WAHA',
  agentId: agent.id,
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
  webhookUrl: 'https://inbox.exemplo.com/webhooks/77777777-7777-7777-7777-777777777777',
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ChannelCreatePage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('textbox', { name: 'Nome' });
  await user.type(screen.getByRole('textbox', { name: 'Nome' }), createdChannel.name);
  await user.click(screen.getByRole('combobox', { name: /agente responsável/i }));
  await user.click(await screen.findByRole('option', { name: agent.name }));
  await user.type(screen.getByLabelText(/url do serviço/i), 'http://localhost:3000');
  await user.type(screen.getByLabelText(/nome da sessão/i), 'default');
  await user.type(screen.getByLabelText(/token de autenticação/i), 'token-abc');
  await user.click(screen.getByRole('button', { name: /cadastrar canal/i }));
}

describe('ChannelCreatePage', () => {
  beforeEach(() => {
    vi.mocked(createChannel).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([agent]);
    navigateMock.mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('em sucesso, navega para o detalhe do canal criado e notifica sucesso', async () => {
    vi.mocked(createChannel).mockResolvedValue(createdChannel);
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith(`/channels/${createdChannel.id}`),
    );
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
    expect(createChannel).toHaveBeenCalledWith({
      name: createdChannel.name,
      agentId: agent.id,
      channelType: 'waha',
      credential: JSON.stringify({
        ServiceUrl: 'http://localhost:3000',
        SessionName: 'default',
        AuthToken: 'token-abc',
      }),
    });
  });

  it('em erro 400 (InvalidCredential), aplica todas as mensagens de credential e não navega', async () => {
    vi.mocked(createChannel).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: {
          credential: [
            'serviceUrl deve ser uma URL absoluta (ex. http://localhost:3000).',
            'sessionName é obrigatório.',
          ],
        },
      }),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(
      await screen.findByText('serviceUrl deve ser uma URL absoluta (ex. http://localhost:3000).'),
    ).toBeInTheDocument();
    expect(screen.getByText('sessionName é obrigatório.')).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em erro 400 (AgentNotFound), aplica o erro no campo certo do formulário', async () => {
    vi.mocked(createChannel).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { agentId: ['O agente informado não foi encontrado em apps/api.'] },
      }),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(
      await screen.findByText('O agente informado não foi encontrado em apps/api.'),
    ).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em erro 502 (ProvisioningFailed), exibe alerta dedicado com a mensagem recebida, distinto do erro de campo', async () => {
    vi.mocked(createChannel).mockRejectedValue(
      new ApiError(502, 'Falha ao provisionar', {
        title: 'Falha ao provisionar',
        status: 502,
        detail:
          'Não foi possível configurar o webhook do canal junto à plataforma externa: token inválido.',
      }),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(
      await screen.findByText(
        'Não foi possível configurar o webhook do canal junto à plataforma externa: token inválido.',
      ),
    ).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.queryByText('O nome do canal é obrigatório.')).not.toBeInTheDocument();
  });

  it('em falha de rede/servidor, notifica erro genérico, não navega e preserva os dados do formulário', async () => {
    vi.mocked(createChannel).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByRole('textbox', { name: 'Nome' })).toHaveValue(createdChannel.name);
  });

  it('ao clicar em "Cancelar", navega para a listagem de canais sem enviar requisição', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByRole('textbox', { name: 'Nome' });
    await user.type(screen.getByRole('textbox', { name: 'Nome' }), createdChannel.name);
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(navigateMock).toHaveBeenCalledWith('/channels');
    expect(createChannel).not.toHaveBeenCalled();
  });
});
