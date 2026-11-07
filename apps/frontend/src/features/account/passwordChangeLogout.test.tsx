import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../theme';
import { appRoutes } from '../../app/routes';
import { authRuntime, leaving } from '../../auth/session';
import { signInForTest, signOutForTest } from '../../test/authFakes';
import { PASSWORD_CHANGED_KEY } from '../auth/passwordChangedNotice';
import { EntrarPage } from '../auth/pages/EntrarPage';
import { defaultRoutes, fakeAccountApi } from './test/fakeAccountApi';

// A troca de senha de ponta a ponta até o /entrar de verdade (design.md da change
// minha-conta-painel, D3; decisão do dono, opção 3). O logout é o real: ele apaga o
// registro e chama `authRuntime.assign('/connect/logout?…')`, que aqui só registra a
// navegação. Como no navegador, a página NÃO é descarregada nesse instante: o React
// continua vivo e reage ao registro apagado. É nesse intervalo que o /entrar interno
// aparecia e consumia o aviso (o achado da conferência no navegador, 11.4).

const NOVA = 'uma-senha-nova-longa';

beforeEach(() => {
  signInForTest();
  sessionStorage.clear();
  leaving.reset();
});

afterEach(() => {
  signOutForTest();
  leaving.reset();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function renderPainel(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
  return router;
}

describe('troca de senha até o /entrar de verdade', () => {
  it('a página antiga mostra "Saindo…", sem o /entrar interno, e a chave do aviso chega ao /entrar real', async () => {
    const user = userEvent.setup();
    fakeAccountApi({ ...defaultRoutes(), 'POST /account/password': { status: 204 } });
    const navegacoes: string[] = [];
    vi.spyOn(authRuntime, 'assign').mockImplementation((url: string) => {
      navegacoes.push(url);
    });
    const router = renderPainel('/account');

    await user.click(await screen.findByRole('button', { name: 'Trocar senha' }));
    await user.type(screen.getByLabelText('Senha atual'), 'a-senha-atual');
    await user.type(screen.getByLabelText('Nova senha'), NOVA);
    await user.type(screen.getByLabelText('Repita a nova senha'), NOVA);
    await user.click(screen.getByRole('button', { name: 'Trocar senha' }));

    await waitFor(() => expect(navegacoes).toHaveLength(1));
    expect(new URL(navegacoes[0], 'http://localhost').pathname).toBe('/connect/logout');
    // A página ainda vive: o React reagiu ao registro apagado, e não foi ao /entrar interno.
    expect(await screen.findByText('Saindo…')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/account');
    expect(screen.queryByLabelText('E-mail')).not.toBeInTheDocument();
    expect(sessionStorage.getItem(PASSWORD_CHANGED_KEY)).not.toBeNull();

    // O /entrar de verdade: o documento novo que o /connect/logout carrega.
    const { unmount } = render(
      <MantineProvider theme={theme}>
        <MemoryRouter initialEntries={['/entrar']}>
          <EntrarPage />
        </MemoryRouter>
      </MantineProvider>,
    );
    expect(screen.getByText('Senha alterada. Entre com a nova senha.')).toBeInTheDocument();
    expect(sessionStorage.getItem(PASSWORD_CHANGED_KEY)).toBeNull();
    unmount();
  });

  it('o "Sair" comum também mostra "Saindo…", sem o /entrar interno', async () => {
    const user = userEvent.setup();
    fakeAccountApi(defaultRoutes());
    const navegacoes: string[] = [];
    vi.spyOn(authRuntime, 'assign').mockImplementation((url: string) => {
      navegacoes.push(url);
    });
    const router = renderPainel('/account');

    await user.click(await screen.findByRole('button', { name: 'Sair' }));

    await waitFor(() => expect(navegacoes).toHaveLength(1));
    expect(await screen.findByText('Saindo…')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/account');
    expect(screen.queryByLabelText('E-mail')).not.toBeInTheDocument();
  });
});
