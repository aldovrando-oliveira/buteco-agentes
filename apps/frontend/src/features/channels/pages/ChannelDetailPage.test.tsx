import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { ChannelDetailPage } from './ChannelDetailPage';
import { ApiError, activateChannel, deactivateChannel, getChannel } from '../api/channelsApi';
import { listAgents } from '../../agents/api/agentsApi';
import {
  ApiError as SessionsApiError,
  getSessionMessages,
  listChannelSessions,
} from '../../sessions/api/sessionsApi';
import type { Channel } from '../types/channel';
import type { Agent } from '../../agents/types/agent';
import type { ChannelSession } from '../../sessions/types/session';

vi.mock('../api/channelsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/channelsApi')>();
  return {
    ...actual,
    getChannel: vi.fn(),
    activateChannel: vi.fn(),
    deactivateChannel: vi.fn(),
  };
});

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

vi.mock('../../sessions/api/sessionsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../sessions/api/sessionsApi')>();
  return { ...actual, listChannelSessions: vi.fn(), getSessionMessages: vi.fn() };
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

const wahaChannel: Channel = {
  id: '77777777-7777-7777-7777-777777777777',
  channelType: 'waha',
  name: 'Canal WAHA',
  agentId: agent.id,
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
  webhookUrl: 'https://inbox.exemplo.com/webhooks/77777777-7777-7777-7777-777777777777',
};

const telegramChannel: Channel = {
  ...wahaChannel,
  id: '99999999-9999-9999-9999-999999999999',
  channelType: 'telegram',
};

const inactiveChannel: Channel = { ...wahaChannel, isActive: false };

const session: ChannelSession = {
  sessionId: '55555555-5555-5555-5555-555555555555',
  contactId: '66666666-6666-6666-6666-666666666666',
  contactExternalId: '5511999999999',
  contactDisplayName: 'Maria',
  lastActivityAt: '2026-08-20T12:00:00Z',
  lastMessage: null,
};

function renderPage(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/channels/:id" element={<ChannelDetailPage />} />
            <Route path="/channels/:id/sessions/:sessionId" element={<ChannelDetailPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('ChannelDetailPage', () => {
  beforeEach(() => {
    vi.mocked(getChannel).mockReset();
    vi.mocked(activateChannel).mockReset();
    vi.mocked(deactivateChannel).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([agent]);
    vi.mocked(listChannelSessions).mockReset();
    vi.mocked(listChannelSessions).mockResolvedValue([]);
    vi.mocked(getSessionMessages).mockReset();
    vi.mocked(getSessionMessages).mockResolvedValue([]);
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe nome, tipo, agente responsável, estado e webhookUrl de um canal', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${wahaChannel.id}`);
    await user.click(await screen.findByRole('tab', { name: 'Configuração' }));

    expect(await screen.findByRole('heading', { name: wahaChannel.name })).toBeInTheDocument();
    expect(await screen.findByText(agent.name, { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByLabelText(/url de webhook/i)).toHaveValue(wahaChannel.webhookUrl);
  });

  it('canal WAHA exibe instrução de configuração manual da sessão', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${wahaChannel.id}`);
    await user.click(await screen.findByRole('tab', { name: 'Configuração' }));

    expect(await screen.findByText(/configure manualmente a sessão do waha/i)).toBeInTheDocument();
  });

  it('canal Telegram exibe indicação de que o webhook já foi configurado automaticamente', async () => {
    vi.mocked(getChannel).mockResolvedValue(telegramChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${telegramChannel.id}`);
    await user.click(await screen.findByRole('tab', { name: 'Configuração' }));

    expect(
      await screen.findByText(/webhook já foi configurado automaticamente/i),
    ).toBeInTheDocument();
  });

  it('exibe estado de "não encontrado" quando o canal não existe', async () => {
    vi.mocked(getChannel).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage('/channels/inexistente');

    expect(await screen.findByText('Canal não encontrado.')).toBeInTheDocument();
  });

  it('ao ativar, envia a requisição imediatamente sem confirmação e atualiza o indicador', async () => {
    vi.mocked(getChannel).mockResolvedValueOnce(inactiveChannel).mockResolvedValue(wahaChannel);
    vi.mocked(activateChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${inactiveChannel.id}`);
    await user.click(await screen.findByRole('tab', { name: 'Configuração' }));

    await user.click(await screen.findByRole('button', { name: /^ativar$/i }));

    expect(activateChannel).toHaveBeenCalledWith(inactiveChannel.id);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(await screen.findByText('Ativo')).toBeInTheDocument();
  });

  it('ao clicar em "Desativar", abre o modal de confirmação sem enviar a requisição', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${wahaChannel.id}`);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(deactivateChannel).not.toHaveBeenCalled();
  });

  it('cancelar a confirmação fecha o modal sem enviar a requisição e mantém o canal ativo', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${wahaChannel.id}`);
    await user.click(await screen.findByRole('tab', { name: 'Configuração' }));

    await user.click(await screen.findByRole('button', { name: /desativar/i }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: /cancelar/i }));

    expect(deactivateChannel).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByText('Ativo')).toBeInTheDocument();
  });

  it('confirmar a desativação envia a requisição, notifica sucesso e atualiza o indicador para inativo', async () => {
    vi.mocked(getChannel).mockResolvedValueOnce(wahaChannel).mockResolvedValue(inactiveChannel);
    vi.mocked(deactivateChannel).mockResolvedValue(inactiveChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${wahaChannel.id}`);
    await user.click(await screen.findByRole('tab', { name: 'Configuração' }));

    await user.click(await screen.findByRole('button', { name: /desativar/i }));
    await screen.findByRole('dialog');
    await user.click(screen.getByRole('button', { name: /confirmar desativação/i }));

    expect(deactivateChannel).toHaveBeenCalledWith(wahaChannel.id);
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(await screen.findByText('Inativo')).toBeInTheDocument();
  });

  it('aba Sessões é a aba padrão', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    vi.mocked(listChannelSessions).mockResolvedValue([session]);

    renderPage(`/channels/${wahaChannel.id}`);

    expect(await screen.findByRole('tab', { name: 'Sessões', selected: true })).toBeInTheDocument();
    expect(await screen.findByText('Maria')).toBeInTheDocument();
  });

  it('aba Configuração renderiza o card existente sem alteração de comportamento', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(`/channels/${wahaChannel.id}`);

    await user.click(await screen.findByRole('tab', { name: 'Configuração' }));

    expect(await screen.findByRole('heading', { name: wahaChannel.name })).toBeInTheDocument();
    expect(screen.getByText(agent.name, { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByLabelText(/url de webhook/i)).toHaveValue(wahaChannel.webhookUrl);
  });

  it('canal sem nenhuma sessão renderiza estado vazio na aba Sessões', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    vi.mocked(listChannelSessions).mockResolvedValue([]);

    renderPage(`/channels/${wahaChannel.id}`);

    expect(await screen.findByText('Nenhuma sessão para este canal ainda.')).toBeInTheDocument();
  });

  it('sessionId inexistente na URL renderiza o estado de erro genérico da timeline, não uma tela em branco', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    vi.mocked(listChannelSessions).mockResolvedValue([session]);
    vi.mocked(getSessionMessages).mockRejectedValue(
      new SessionsApiError(404, 'Sessão não encontrada'),
    );

    renderPage(`/channels/${wahaChannel.id}/sessions/inexistente`);

    expect(
      await screen.findByText('Não foi possível carregar as mensagens desta sessão.'),
    ).toBeInTheDocument();
  });

  it('oferece volta para a listagem, inclusive em acesso direto pela URL', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);

    renderPage(`/channels/${wahaChannel.id}`);

    // O harness monta a página de detalhe como primeira entrada do histórico:
    // não há para onde voltar, e o link precisa funcionar mesmo assim.
    const volta = await screen.findByRole('link', { name: 'Canais' });

    expect(volta).toHaveAttribute('href', '/channels');
  });
});
