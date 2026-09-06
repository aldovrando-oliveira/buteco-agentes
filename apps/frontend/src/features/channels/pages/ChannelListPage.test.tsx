import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { ChannelListPage } from './ChannelListPage';
import { listChannels } from '../api/channelsApi';
import { listAgents } from '../../agents/api/agentsApi';
import type { Channel } from '../types/channel';

vi.mock('../api/channelsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/channelsApi')>();
  return { ...actual, listChannels: vi.fn() };
});

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

const channel: Channel = {
  id: '77777777-7777-7777-7777-777777777777',
  channelType: 'waha',
  name: 'Canal WAHA',
  agentId: '11111111-1111-1111-1111-111111111111',
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
          <ChannelListPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('ChannelListPage', () => {
  beforeEach(() => {
    vi.mocked(listChannels).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
  });

  it('exibe um indicador de carregamento enquanto a lista não chega', () => {
    vi.mocked(listChannels).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(screen.getByText('Carregando canais...')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Novo canal' })).toBeInTheDocument();
  });

  it('renderiza a lista quando existem canais cadastrados', async () => {
    vi.mocked(listChannels).mockResolvedValue([channel]);

    renderPage();

    expect(await screen.findByRole('link', { name: channel.name })).toBeInTheDocument();
  });

  it('exibe mensagem de lista vazia e mantém o botão de criar visível', async () => {
    vi.mocked(listChannels).mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText('Nenhum canal cadastrado ainda.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Novo canal' })).toBeInTheDocument();
  });

  it('exibe estado de erro quando a lista falha ao carregar', async () => {
    vi.mocked(listChannels).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(await screen.findByText('Não foi possível carregar os canais.')).toBeInTheDocument();
  });

  it('busca por nome, tipo ou agente, ignorando maiúsculas e acentuação', async () => {
    const telegram: Channel = {
      ...channel,
      id: '88888888-8888-8888-8888-888888888888',
      channelType: 'telegram',
      name: 'Canal Telegram',
    };
    vi.mocked(listChannels).mockResolvedValue([channel, telegram]);

    renderPage();
    await screen.findByText(channel.name);

    await userEvent.type(screen.getByLabelText('Buscar por nome, tipo ou agente'), 'telegram');

    expect(screen.getByText(telegram.name)).toBeInTheDocument();
    expect(screen.queryByText(channel.name)).not.toBeInTheDocument();
  });

  it('distingue lista vazia por busca de lista vazia por falta de cadastro', async () => {
    vi.mocked(listChannels).mockResolvedValue([channel]);

    renderPage();
    await screen.findByText(channel.name);

    await userEvent.type(screen.getByLabelText('Buscar por nome, tipo ou agente'), 'inexistente');

    expect(screen.getByText('Nenhum canal corresponde à busca.')).toBeInTheDocument();
    expect(screen.queryByText('Nenhum canal cadastrado ainda.')).not.toBeInTheDocument();
  });

  it('não exibe o campo de busca com o catálogo vazio', async () => {
    vi.mocked(listChannels).mockResolvedValue([]);

    renderPage();
    await screen.findByText('Nenhum canal cadastrado ainda.');

    expect(screen.queryByLabelText('Buscar por nome, tipo ou agente')).not.toBeInTheDocument();
  });
});
