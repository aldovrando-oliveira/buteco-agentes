import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { ChannelEditPage } from './ChannelEditPage';
import { ApiError, getChannel, updateChannel } from '../api/channelsApi';
import { listAgents } from '../../agents/api/agentsApi';
import type { Channel } from '../types/channel';
import type { Agent } from '../../agents/types/agent';

vi.mock('../api/channelsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/channelsApi')>();
  return { ...actual, getChannel: vi.fn(), updateChannel: vi.fn() };
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
  a2a: null,
};

const channel: Channel = {
  id: '77777777-7777-7777-7777-777777777777',
  channelType: 'waha',
  name: 'Canal WAHA',
  agentId: agent.id,
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
  webhookUrl: 'https://inbox.exemplo.com/webhooks/77777777-7777-7777-7777-777777777777',
};

function renderPage(id: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/channels/${id}/edit`]}>
          <Routes>
            <Route path="/channels/:id/edit" element={<ChannelEditPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('ChannelEditPage', () => {
  beforeEach(() => {
    vi.mocked(getChannel).mockReset();
    vi.mocked(updateChannel).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([agent]);
    navigateMock.mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe o formulário pré-preenchido, com tipo de canal fixo e credencial em branco', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);

    renderPage(channel.id);

    expect(await screen.findByRole('textbox', { name: 'Nome' })).toHaveValue(channel.name);
    expect(screen.getByLabelText(/tipo de canal/i)).toHaveValue('WAHA');
    expect(screen.getByLabelText(/tipo de canal/i)).toBeDisabled();
    expect(screen.getByLabelText(/url do serviço/i)).toHaveValue('');
  });

  it('edição mantendo a credencial em branco preserva a persistida (credential omitido)', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);
    const updated = { ...channel, name: 'Canal WAHA (renomeado)' };
    vi.mocked(updateChannel).mockResolvedValue(updated);
    const user = userEvent.setup();

    renderPage(channel.id);

    await screen.findByRole('textbox', { name: 'Nome' });
    await user.clear(screen.getByRole('textbox', { name: 'Nome' }));
    await user.type(screen.getByRole('textbox', { name: 'Nome' }), updated.name);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(updateChannel).toHaveBeenCalledWith(channel.id, {
        name: updated.name,
        agentId: agent.id,
        credential: undefined,
      }),
    );
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith(`/channels/${channel.id}`));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
  });

  it('edição preenchendo toda a credencial substitui a persistida por inteiro', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);
    vi.mocked(updateChannel).mockResolvedValue(channel);
    const user = userEvent.setup();

    renderPage(channel.id);

    await screen.findByRole('textbox', { name: 'Nome' });
    await user.type(screen.getByLabelText(/url do serviço/i), 'http://localhost:4000');
    await user.type(screen.getByLabelText(/nome da sessão/i), 'nova-sessao');
    await user.type(screen.getByLabelText(/token de autenticação/i), 'novo-token');
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(updateChannel).toHaveBeenCalledWith(channel.id, {
        name: channel.name,
        agentId: agent.id,
        credential: JSON.stringify({
          ServiceUrl: 'http://localhost:4000',
          SessionName: 'nova-sessao',
          AuthToken: 'novo-token',
        }),
      }),
    );
  });

  it('em erro 400, aplica os erros nos campos certos do formulário e não navega', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);
    vi.mocked(updateChannel).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { name: ['O nome do canal é obrigatório.'] },
      }),
    );
    const user = userEvent.setup();

    renderPage(channel.id);

    await screen.findByRole('textbox', { name: 'Nome' });
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(await screen.findByText('O nome do canal é obrigatório.')).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em erro 502 (ProvisioningFailed) na edição, exibe alerta dedicado e mantém o formulário preenchido', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);
    vi.mocked(updateChannel).mockRejectedValue(
      new ApiError(502, 'Falha ao provisionar', {
        title: 'Falha ao provisionar',
        status: 502,
        detail:
          'Não foi possível configurar o webhook do canal junto à plataforma externa: token inválido.',
      }),
    );
    const user = userEvent.setup();

    renderPage(channel.id);

    await screen.findByRole('textbox', { name: 'Nome' });
    await user.type(screen.getByLabelText(/url do serviço/i), 'http://localhost:4000');
    await user.type(screen.getByLabelText(/nome da sessão/i), 'nova-sessao');
    await user.type(screen.getByLabelText(/token de autenticação/i), 'token-invalido');
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(
      await screen.findByText(
        'Não foi possível configurar o webhook do canal junto à plataforma externa: token inválido.',
      ),
    ).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/url do serviço/i)).toHaveValue('http://localhost:4000');
  });

  it('em falha de rede/servidor, notifica erro genérico, não navega e preserva os dados editados', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);
    vi.mocked(updateChannel).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();

    renderPage(channel.id);

    await screen.findByRole('textbox', { name: 'Nome' });
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByRole('textbox', { name: 'Nome' })).toHaveValue(channel.name);
  });

  it('ao clicar em "Cancelar", navega para o detalhe do canal em edição sem enviar requisição', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);
    const user = userEvent.setup();

    renderPage(channel.id);

    await screen.findByRole('textbox', { name: 'Nome' });
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(navigateMock).toHaveBeenCalledWith(`/channels/${channel.id}`);
    expect(updateChannel).not.toHaveBeenCalled();
  });

  it('exibe estado de "não encontrado" quando o canal não existe', async () => {
    vi.mocked(getChannel).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage('inexistente');

    expect(await screen.findByText('Canal não encontrado.')).toBeInTheDocument();
  });

  it('volta para o registro que está sendo editado, e não para a listagem', async () => {
    vi.mocked(getChannel).mockResolvedValue(channel);

    renderPage(channel.id);

    // De "editar X" quer-se voltar para X, que é de onde se veio — não para a
    // lista inteira.
    expect(await screen.findByRole('link', { name: 'Voltar ao canal' })).toHaveAttribute(
      'href',
      `/channels/${channel.id}`,
    );
  });
});
