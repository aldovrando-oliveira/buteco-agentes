// O fluxo inteiro, na costura (design.md da change painel-entra-pelo-emissor, D12):
// rota protegida → /entrar → 204 → authorize (navegação simulada) → /entrar/retorno →
// /connect/token → a rota protegida carrega com o Authorization do emissor. Sem guarda
// próprio: cada parte tem o seu.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../theme';
import { appRoutes } from '../../app/routes';
import { authRuntime } from '../../auth/session';
import { jsonResponse, tokenResponse } from '../../test/authFakes';

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('login pelo emissor, de ponta a ponta', () => {
  it('da rota protegida até a lista de agentes com o token do emissor', async () => {
    const agentsAuth: Array<string | undefined> = [];
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input === '/auth/session') return new Response(null, { status: 204 });
      if (input === '/connect/token') return tokenResponse(1);
      if (input.endsWith('/agents')) {
        agentsAuth.push((init?.headers as Record<string, string>).Authorization);
        return jsonResponse([]);
      }
      return jsonResponse([]);
    });
    vi.stubGlobal('fetch', fetchMock);
    const navigations: string[] = [];
    vi.spyOn(authRuntime, 'assign').mockImplementation((url) => {
      navigations.push(url);
    });

    const router = createMemoryRouter(appRoutes, { initialEntries: ['/agents'] });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <MantineProvider theme={theme}>
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </MantineProvider>,
    );

    // 1. Sem sessão, a rota protegida manda ao /entrar, guardando a rota pedida.
    await screen.findByLabelText('E-mail');
    expect(router.state.location.pathname).toBe('/entrar');

    // 2. E-mail e senha; o 204 grava o cookie do emissor, e o painel navega ao authorize.
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('E-mail'), 'ana@exemplo.test');
    await user.type(screen.getByLabelText('Senha'), 'senha-certa');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));
    await waitFor(() => expect(navigations).toHaveLength(1));
    const authorize = new URL(navigations[0], 'http://localhost');
    expect(authorize.pathname).toBe('/connect/authorize');

    // 3. O authorize, com o cookie válido, devolve ao redirect_uri com código e state.
    await act(() =>
      router.navigate(`/entrar/retorno?code=c1&state=${authorize.searchParams.get('state')}`),
    );

    // 4. A troca grava a sessão, e o painel volta à rota pedida, que carrega com o token.
    expect(await screen.findByRole('heading', { name: 'Agentes' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/agents');
    await waitFor(() => expect(agentsAuth).toContain('Bearer access-1'));
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual(
      expect.arrayContaining(['/auth/session', '/connect/token']),
    );
  });
});
