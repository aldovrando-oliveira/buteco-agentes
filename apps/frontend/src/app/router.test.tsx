import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { theme } from '../theme';
import { AppRouter } from './router';
import { listAgents } from '../features/agents/api/agentsApi';
import { listProviders } from '../features/agents/api/providersApi';
import { listChannels } from '../features/channels/api/channelsApi';
import { clearToken, setToken } from '../auth/token';

vi.mock('../features/agents/api/agentsApi', () => ({
  listAgents: vi.fn(),
  getAgent: vi.fn(),
  createAgent: vi.fn(),
}));

vi.mock('../features/agents/api/providersApi', () => ({
  listProviders: vi.fn(),
}));

vi.mock('../features/channels/api/channelsApi', () => ({
  listChannels: vi.fn(),
}));

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <AppRouter />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('AppRouter', () => {
  beforeEach(() => {
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
    vi.mocked(listProviders).mockReset();
    vi.mocked(listProviders).mockResolvedValue([{ id: 'openai', models: ['gpt-5.6-sol'] }]);
    vi.mocked(listChannels).mockReset();
    vi.mocked(listChannels).mockResolvedValue([]);
    window.history.pushState({}, '', '/');
    setToken('token-de-teste');
  });

  afterEach(() => {
    clearToken();
  });

  it('sem token armazenado, redireciona para /login em vez do conteúdo protegido', async () => {
    clearToken();

    renderApp();

    await waitFor(() => expect(window.location.pathname).toBe('/login'));
    expect(screen.queryByRole('heading', { name: 'Agentes' })).not.toBeInTheDocument();
  });

  it('redireciona a rota raiz para /agents sem exibir página vazia', async () => {
    renderApp();

    await waitFor(() => expect(window.location.pathname).toBe('/agents'));
    expect(await screen.findByRole('heading', { name: 'Agentes' })).toBeInTheDocument();
  });

  it('preserva o layout do AppShell ao navegar entre rotas', async () => {
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole('heading', { name: 'Agentes' });
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Agentes' })).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Novo agente' }));

    expect(await screen.findByRole('heading', { name: 'Novo agente' })).toBeInTheDocument();
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Agentes' })).toBeInTheDocument();
  });

  it('navega para a listagem de canais pelo item de navegação "Canais"', async () => {
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole('heading', { name: 'Agentes' });
    await user.click(screen.getByRole('link', { name: 'Canais' }));

    expect(await screen.findByRole('heading', { name: 'Canais' })).toBeInTheDocument();
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
  });
});
