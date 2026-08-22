import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { ChannelDetailPage } from './ChannelDetailPage';
import {
  ApiError,
  activateChannel,
  deactivateChannel,
  getChannel,
} from '../api/channelsApi';
import { listAgents } from '../../agents/api/agentsApi';
import type { Channel } from '../types/channel';
import type { Agent } from '../../agents/types/agent';

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
  delegatesTo: [],
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

function renderPage(id: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/channels/${id}`]}>
          <Routes>
            <Route path="/channels/:id" element={<ChannelDetailPage />} />
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
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe nome, tipo, agente responsável, estado e webhookUrl de um canal', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);

    renderPage(wahaChannel.id);

    expect(await screen.findByRole('heading', { name: wahaChannel.name })).toBeInTheDocument();
    expect(await screen.findByText(agent.name, { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByLabelText(/url de webhook/i)).toHaveValue(wahaChannel.webhookUrl);
  });

  it('canal WAHA exibe instrução de configuração manual da sessão', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);

    renderPage(wahaChannel.id);

    expect(
      await screen.findByText(/configure manualmente a sessão do waha/i),
    ).toBeInTheDocument();
  });

  it('canal Telegram exibe indicação de que o webhook já foi configurado automaticamente', async () => {
    vi.mocked(getChannel).mockResolvedValue(telegramChannel);

    renderPage(telegramChannel.id);

    expect(
      await screen.findByText(/webhook já foi configurado automaticamente/i),
    ).toBeInTheDocument();
  });

  it('exibe estado de "não encontrado" quando o canal não existe', async () => {
    vi.mocked(getChannel).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage('inexistente');

    expect(await screen.findByText('Canal não encontrado.')).toBeInTheDocument();
  });

  it('ao ativar, envia a requisição imediatamente sem confirmação e atualiza o indicador', async () => {
    vi.mocked(getChannel).mockResolvedValueOnce(inactiveChannel).mockResolvedValue(wahaChannel);
    vi.mocked(activateChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(inactiveChannel.id);

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

    renderPage(wahaChannel.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(deactivateChannel).not.toHaveBeenCalled();
  });

  it('cancelar a confirmação fecha o modal sem enviar a requisição e mantém o canal ativo', async () => {
    vi.mocked(getChannel).mockResolvedValue(wahaChannel);
    const user = userEvent.setup();

    renderPage(wahaChannel.id);

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

    renderPage(wahaChannel.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));
    await screen.findByRole('dialog');
    await user.click(screen.getByRole('button', { name: /confirmar desativação/i }));

    expect(deactivateChannel).toHaveBeenCalledWith(wahaChannel.id);
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(await screen.findByText('Inativo')).toBeInTheDocument();
  });
});
