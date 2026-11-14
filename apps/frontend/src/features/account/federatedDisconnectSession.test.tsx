import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { waitFor } from '@testing-library/react';
import { jsonResponse, session, signOutForTest } from '../../test/authFakes';
import { authRuntime } from '../../auth/session';
import { defaultRoutes, fakeAccountApi } from './test/fakeAccountApi';
import { renderAccountPage } from './test/renderAccountPage';

// A sessão de quem o Admin desconectou (spec `panel-federated-accounts`, "A sessão de quem o Admin
// desconectou"; design.md da change telas-contas-federadas, D6). Um caso, sem guarda (decisão do dono):
// o backend troca o stamp e revoga as autorizações da pessoa, e o painel dela não consulta nada; na
// próxima renovação, o `invalid_grant` encerra a sessão pelo `sessionFetch` de sempre.
// Arranjo: a sessão a 30 s de vencer (dentro da margem de renovação), o emissor respondendo
// `invalid_grant`, e a rota respondendo `401` sem corpo ao pedido que sai sem token.

beforeEach(() => {
  const s = session(1, Date.now() + 30_000, { accessToken: 'token-de-antes' }, 'admin');
  window.localStorage.setItem('buteco.sessao', JSON.stringify(s));
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('A sessão de quem o Admin desconectou', () => {
  it('A renovação depois de desconectada', async () => {
    fakeAccountApi({
      ...defaultRoutes(),
      'POST /connect/token': jsonResponse({ error: 'invalid_grant' }, 400),
      'GET /account': { status: 401 },
    });
    renderAccountPage('/account');

    await waitFor(() =>
      expect(authRuntime.assign).toHaveBeenCalledWith(
        expect.stringMatching(/^\/entrar\?motivo=sessao-expirada/),
      ),
    );
    expect(window.localStorage.getItem('buteco.sessao')).toBeNull();
  });
});
