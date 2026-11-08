// O fluxo inteiro, na costura (design.md da change painel-entra-pelo-emissor, D12):
// rota protegida → /entrar → a senha e o segundo passo obrigatório → authorize (navegação
// simulada) → /entrar/retorno → /connect/token → a rota protegida carrega com o Authorization do
// emissor. Desde a change verificacao-2-etapas-obrigatoria, o POST /auth/session não responde
// 204: quem não tem a verificação cadastra o app antes da sessão. Sem guarda próprio: cada parte
// tem o seu.
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

const SEGREDO = 'JBSWY3DPEHPK3PXP';
const URI = 'otpauth://totp/Buteco%20Agentes:ana%40exemplo.test?secret=JBSWY3DPEHPK3PXP&issuer=Buteco%20Agentes';
const CODIGOS = Array.from({ length: 10 }, (_, i) => `C0D${i}-AAAA-BBBB-CCCC`);

describe('login pelo emissor, de ponta a ponta', () => {
  it('da rota protegida até a lista de agentes com o token do emissor', async () => {
    const agentsAuth: Array<string | undefined> = [];
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input === '/auth/session') return jsonResponse({ segundoPasso: 'cadastro' }, 200);
      if (input === '/auth/session/enrollment/setup') return jsonResponse({ segredo: SEGREDO, otpauthUri: URI }, 200);
      if (input === '/auth/session/enrollment/confirm') return jsonResponse({ codigosRecuperacao: CODIGOS }, 200);
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

    // 2. E-mail e senha abrem o cadastro; o QR, o código e os códigos de recuperação; "Continuar"
    //    navega ao authorize com o cookie da sessão completa.
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('E-mail'), 'ana@exemplo.test');
    await user.type(screen.getByLabelText('Senha'), 'senha-certa');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));
    await screen.findByRole('img', { name: 'QR para o app autenticador' });
    await user.type(screen.getByLabelText(/Digite o código de 6 dígitos/), '123456');
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
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
      expect.arrayContaining(['/auth/session', '/auth/session/enrollment/setup', '/auth/session/enrollment/confirm', '/connect/token']),
    );
  });

  // A sessão completa do confirm dura 5 minutos, como a do segundo passo (spec
  // `operator-login-ui`, "Continuar depois de 5 minutos"): o authorize manda ao /entrar, e a
  // pessoa entra com a senha e o código do app que acabou de cadastrar. O lado do servidor é o
  // TwoFactorRequiredLoginTests.ContinueAfterFiveMinutes… do apps/api.
  it('Continuar depois de 5 minutos: o authorize manda ao /entrar, e a senha com o código do app entra', async () => {
    let cadastrou = false;
    const fetchMock = vi.fn(async (input: string) => {
      if (input === '/auth/session') return jsonResponse({ segundoPasso: cadastrou ? 'totp' : 'cadastro' }, 200);
      if (input === '/auth/session/enrollment/setup') return jsonResponse({ segredo: SEGREDO, otpauthUri: URI }, 200);
      if (input === '/auth/session/enrollment/confirm') {
        cadastrou = true;
        return jsonResponse({ codigosRecuperacao: CODIGOS }, 200);
      }
      if (input === '/auth/session/second-factor') return new Response(null, { status: 204 });
      if (input === '/connect/token') return tokenResponse(1);
      return jsonResponse([]);
    });
    vi.stubGlobal('fetch', fetchMock);
    const navigations: string[] = [];
    vi.spyOn(authRuntime, 'assign').mockImplementation((url) => {
      navigations.push(url);
    });
    const montar = (entrada: string) => {
      const router = createMemoryRouter(appRoutes, { initialEntries: [entrada] });
      return render(
        <MantineProvider theme={theme}>
          <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </MantineProvider>,
      );
    };
    const primeiraPagina = montar('/agents');
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText('E-mail'), 'ana@exemplo.test');
    await user.type(screen.getByLabelText('Senha'), 'senha-certa');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));
    await screen.findByRole('img', { name: 'QR para o app autenticador' });
    await user.type(screen.getByLabelText(/Digite o código de 6 dígitos/), '123456');
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    await user.click(await screen.findByRole('button', { name: 'Continuar' }));
    await waitFor(() => expect(navigations).toHaveLength(1));
    const primeiro = new URL(navigations[0], 'http://localhost');

    // O authorize com o cookie vencido responde 302 para /entrar?retorno=<o pedido>: uma página
    // nova no navegador, que aqui é uma árvore nova.
    const retorno = `${primeiro.pathname}${primeiro.search}`;
    primeiraPagina.unmount();
    montar(`/entrar?retorno=${encodeURIComponent(retorno)}`);
    await user.type(await screen.findByLabelText('E-mail'), 'ana@exemplo.test');
    await user.type(screen.getByLabelText('Senha'), 'senha-certa');
    await user.click(screen.getByRole('button', { name: 'Entrar' }));
    await user.type(await screen.findByLabelText('Código'), '654321');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    await waitFor(() => expect(navigations).toHaveLength(2));
    expect(navigations[1]).toBe(retorno);
    expect(fetchMock.mock.calls.map(([url]) => url)).toContain('/auth/session/second-factor');
  });
});
