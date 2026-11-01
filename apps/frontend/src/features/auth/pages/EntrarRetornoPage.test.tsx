import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { EntrarRetornoPage } from './EntrarRetornoPage';
import { PENDING_LOGIN_KEY, beginLogin } from '../../../auth/loginFlow';
import { authRuntime } from '../../../auth/session';
import { SESSION_KEY } from '../../../auth/sessionStore';
import { jsonResponse, tokenResponse } from '../../../test/authFakes';

type Router = ReturnType<typeof createMemoryRouter>;

function routerAt(url: string) {
  return createMemoryRouter(
    [
      { path: '/entrar/retorno', element: <EntrarRetornoPage /> },
      { path: '/entrar', element: <p>tela do entrar</p> },
      { path: '/agents', element: <p>lista de agentes</p> },
    ],
    { initialEntries: [url] },
  );
}

// O router é criado antes do render: o efeito da página roda dentro do render, e o
// `fetch` falso lê a URL do router no instante do pedido.
function show(router: Router, { strict = false } = {}) {
  const tree = (
    <MantineProvider theme={theme}>
      <RouterProvider router={router} />
    </MantineProvider>
  );
  render(strict ? <StrictMode>{tree}</StrictMode> : tree);
}

/** Começa um fluxo de verdade, para ter o login em andamento e o state dele. */
async function pendingState(voltarPara = '/agents') {
  await beginLogin(authRuntime, voltarPara);
  return JSON.parse(sessionStorage.getItem(PENDING_LOGIN_KEY)!).state as string;
}

/**
 * `fetch` falso do `/connect/token`, que guarda a URL da página e a ação do histórico
 * NO INSTANTE do pedido.
 */
function tokenEndpoint(router: () => Router, ...responses: Response[]) {
  const seen: Array<{ search: string; action: string; body: URLSearchParams }> = [];
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input !== '/connect/token') throw new Error(`rota inesperada: ${input}`);
    seen.push({
      search: router().state.location.search,
      action: router().state.historyAction,
      body: new URLSearchParams(String(init?.body)),
    });
    return responses.shift() ?? tokenResponse(1);
  });
  vi.stubGlobal('fetch', fetchMock);
  return { fetchMock, seen };
}

beforeEach(() => {
  sessionStorage.clear();
  localStorage.clear();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('EntrarRetornoPage', () => {
  it('a URL fica sem o código antes da troca, e a troca leva o código capturado', async () => {
    const state = await pendingState();
    const router = routerAt(`/entrar/retorno?code=abc&state=${state}&iss=https%3A%2F%2Femissor`);
    const { seen } = tokenEndpoint(() => router);

    show(router);

    await waitFor(() => expect(router.state.location.pathname).toBe('/agents'));
    expect(seen).toHaveLength(1);
    expect(seen[0].search).toBe('');
    expect(seen[0].action).toBe('REPLACE');
    expect(seen[0].body.get('code')).toBe('abc');
    expect(seen[0].body.get('grant_type')).toBe('authorization_code');
    expect(JSON.parse(localStorage.getItem(SESSION_KEY)!).accessToken).toBe('access-1');
  });

  it('no StrictMode, o retorno é processado uma vez só', async () => {
    const state = await pendingState();
    const router = routerAt(`/entrar/retorno?code=abc&state=${state}`);
    const { fetchMock } = tokenEndpoint(() => router);

    show(router, { strict: true });

    await waitFor(() => expect(router.state.location.pathname).toBe('/agents'));
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('a URL fica sem query também no erro, e o error_description vai ao console', async () => {
    const state = await pendingState();
    const router = routerAt(`/entrar/retorno?error=access_denied&error_description=The+user+said+no&state=${state}`);
    const { fetchMock } = tokenEndpoint(() => router);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});

    show(router);

    expect(await screen.findByText('Não foi possível concluir a entrada.')).toBeInTheDocument();
    expect(router.state.location.search).toBe('');
    expect(router.state.historyAction).toBe('REPLACE');
    expect(screen.getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument();
    expect(screen.queryByText(/user said no/)).not.toBeInTheDocument();
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('The user said no'));
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('state diferente não troca o código', async () => {
    await pendingState();
    const router = routerAt('/entrar/retorno?code=abc&state=outro');
    const { fetchMock } = tokenEndpoint(() => router);

    show(router);

    expect(
      await screen.findByText('Não foi possível concluir a entrada. Entre de novo.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('o login em andamento é de uso único: o mesmo retorno de novo não troca nada', async () => {
    const state = await pendingState();
    let router = routerAt(`/entrar/retorno?code=abc&state=${state}`);
    const { fetchMock } = tokenEndpoint(() => router);
    show(router);
    await waitFor(() => expect(router.state.location.pathname).toBe('/agents'));

    router = routerAt(`/entrar/retorno?code=abc&state=${state}`);

    show(router);

    expect(
      await screen.findByText('Não foi possível concluir a entrada. Entre de novo.'),
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('login_required vai ao /entrar sem aviso de erro', async () => {
    const state = await pendingState();
    const router = routerAt(`/entrar/retorno?error=login_required&state=${state}`);
    tokenEndpoint(() => router);

    show(router);

    await waitFor(() => expect(router.state.location.pathname).toBe('/entrar'));
    expect(router.state.location.search).toBe('');
  });

  it('código já usado volta ao /entrar com o aviso genérico, sem gravar sessão', async () => {
    const state = await pendingState('/agents');
    const router = routerAt(`/entrar/retorno?code=abc&state=${state}`);
    tokenEndpoint(() => router, jsonResponse({ error: 'invalid_grant' }, 400));

    show(router);

    await waitFor(() => expect(router.state.location.pathname).toBe('/entrar'));
    expect(new URLSearchParams(router.state.location.search).get('motivo')).toBe('falha-na-entrada');
    expect(new URLSearchParams(router.state.location.search).get('voltarPara')).toBe('/agents');
    expect(localStorage.getItem(SESSION_KEY)).toBeNull();
  });

  it('503 duas vezes mostra "O painel está iniciando" com Tentar de novo', async () => {
    const state = await pendingState();
    const router = routerAt(`/entrar/retorno?code=abc&state=${state}`);
    const { fetchMock } = tokenEndpoint(
      () => router,
      jsonResponse({}, 503, { 'Retry-After': '0' }),
      jsonResponse({}, 503, { 'Retry-After': '0' }),
    );

    show(router);

    expect(
      await screen.findByText('O painel está iniciando. Tente de novo em alguns segundos.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
