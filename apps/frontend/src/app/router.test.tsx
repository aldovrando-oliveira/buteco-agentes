import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../theme';
import { AppRouter } from './router';
import { appRoutes } from './routes';
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

function renderProviders(children: React.ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MantineProvider>,
  );
}

// Monta a mesma árvore de rotas da aplicação em um router de memória. As
// asserções são sobre o conteúdo renderizado, e não sobre
// window.location.pathname: o router de produção é criado uma única vez no
// escopo do módulo (Decision 2 do design.md), então empurrar o histórico
// global por teste não o reposicionaria — a localização vazaria de um caso
// para o outro. Ver Decision 3.
function renderRoutesFrom(initialEntry: string) {
  return renderProviders(
    <RouterProvider router={createMemoryRouter(appRoutes, { initialEntries: [initialEntry] })} />,
  );
}

describe('appRoutes', () => {
  beforeEach(() => {
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
    vi.mocked(listProviders).mockReset();
    vi.mocked(listProviders).mockResolvedValue([{ id: 'openai', models: ['gpt-5.6-sol'] }]);
    vi.mocked(listChannels).mockReset();
    vi.mocked(listChannels).mockResolvedValue([]);
    setToken('token-de-teste');
  });

  afterEach(() => {
    clearToken();
  });

  it('sem token armazenado, leva para o login em vez do conteúdo protegido', async () => {
    clearToken();

    renderRoutesFrom('/');

    expect(await screen.findByLabelText(/usuário/i)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Agentes' })).not.toBeInTheDocument();
  });

  it('redireciona a rota raiz para a listagem de agentes sem exibir página vazia', async () => {
    renderRoutesFrom('/');

    expect(await screen.findByRole('heading', { name: 'Agentes' })).toBeInTheDocument();
  });

  it('preserva o layout do AppShell ao navegar entre rotas', async () => {
    const user = userEvent.setup();
    renderRoutesFrom('/');

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
    renderRoutesFrom('/');

    await screen.findByRole('heading', { name: 'Agentes' });
    await user.click(screen.getByRole('link', { name: 'Canais' }));

    expect(await screen.findByRole('heading', { name: 'Canais' })).toBeInTheDocument();
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();
  });

  it('monta a partir de uma rota interna, com o mesmo layout e a mesma proteção', async () => {
    renderRoutesFrom('/channels');

    expect(await screen.findByRole('heading', { name: 'Canais' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toBeInTheDocument();
  });

  it('monta a partir de uma rota interna sem token e leva para o login', async () => {
    clearToken();

    renderRoutesFrom('/channels');

    expect(await screen.findByLabelText(/usuário/i)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Canais' })).not.toBeInTheDocument();
  });
});

describe('AppRouter', () => {
  beforeEach(() => {
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
    setToken('token-de-teste');
  });

  afterEach(() => {
    clearToken();
  });

  // Fumaça sobre o ponto de entrada real: prova que o browser router criado
  // no escopo do módulo compõe com appRoutes e monta a aplicação. Não
  // navega, para não depender da ordem dos testes nem do window.location
  // compartilhado do jsdom.
  it('monta a aplicação com o browser router de produção', async () => {
    renderProviders(<AppRouter />);

    expect(await screen.findByRole('heading', { name: 'Agentes' })).toBeInTheDocument();
  });
});
