import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { fakeUsersApi, problem } from '../test/fakeUsersApi';
import {
  ApiError,
  activateUser,
  changeUserRole,
  cancelInvitation,
  deactivateUser,
  getUser,
  inviteUser,
  listUsers,
  renewInvitation,
  requestPasswordReset,
  resetTwoFactor,
  revokeUserSessions,
  updateUser,
} from './usersApi';

beforeEach(() => {
  signInForTest('token-de-teste');
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('usersApi', () => {
  it('GET /users com o token da sessão', async () => {
    const api = fakeUsersApi({ 'GET /users': { status: 200, body: [] } });

    await expect(listUsers()).resolves.toEqual([]);

    expect(api.calls('GET /users')).toEqual([
      { method: 'GET', path: '/users', body: undefined, authorization: 'Bearer token-de-teste' },
    ]);
  });

  it.each<[string, () => Promise<unknown>, unknown]>([
    [
      'POST /users/invitations',
      () => inviteUser({ nome: 'Marcos Lima', email: 'marcos@exemplo.test', papel: 'operador', senha: 'minha-senha' }),
      { nome: 'Marcos Lima', email: 'marcos@exemplo.test', papel: 'operador', senha: 'minha-senha' },
    ],
    ['POST /users/u%201/invitation/renew', () => renewInvitation('u 1', 'minha-senha'), { senha: 'minha-senha' }],
  ])('%s leva o corpo do contrato', async (key, call, body) => {
    const api = fakeUsersApi({ [key]: { status: 200, body: {} } });

    await call();

    const [request] = api.calls(key);
    expect(request.body).toEqual(body);
    expect(request.authorization).toBe('Bearer token-de-teste');
  });

  it('o cancelamento não leva corpo e aceita o 204', async () => {
    const api = fakeUsersApi({ 'POST /users/u-1/invitation/cancel': { status: 204 } });

    await expect(cancelInvitation('u-1')).resolves.toBeUndefined();

    expect(api.calls('POST /users/u-1/invitation/cancel')[0].body).toBeUndefined();
  });

  it('o 400 traz os erros por campo', async () => {
    fakeUsersApi({
      'POST /users/invitations': {
        status: 400,
        body: { title: 'One or more validation errors occurred.', errors: { email: ['Informe um e-mail válido.'] } },
      },
    });

    const error = await inviteUser({ nome: 'x', email: 'y', papel: 'leitor', senha: 'z' }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).problem?.errors?.email).toEqual(['Informe um e-mail válido.']);
  });

  it('o 423 traz o Retry-After', async () => {
    fakeUsersApi({
      'POST /users/invitations': problem(423, 'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.', {
        'Retry-After': '300',
      }),
    });

    const error = (await inviteUser({ nome: 'x', email: 'y', papel: 'leitor', senha: 'z' }).catch((e: unknown) => e)) as ApiError;

    expect(error.status).toBe(423);
    expect(error.retryAfter).toBe(300);
    expect(error.title).toBe('Acesso pausado por tentativas seguidas. Tente de novo mais tarde.');
  });
  describe('Editar usuário (change editar-usuario-painel)', () => {
    it.each<[string, () => Promise<unknown>, unknown]>([
      ['GET /users/u%201', () => getUser('u 1'), undefined],
      [
        'PUT /users/u%201',
        () => updateUser('u 1', { nome: 'Rafael', email: 'r@exemplo.test' }),
        { nome: 'Rafael', email: 'r@exemplo.test' },
      ],
      ['POST /users/u%201/deactivate', () => deactivateUser('u 1'), undefined],
      ['POST /users/u%201/activate', () => activateUser('u 1'), undefined],
      ['POST /users/u%201/sessions/revoke', () => revokeUserSessions('u 1'), undefined],
      // `PUT /users/{id}/role` (change papeis-no-painel): rebaixar não leva senha; promover, sim.
      ['PUT /users/u%201/role', () => changeUserRole('u 1', { papel: 'leitor' }), { papel: 'leitor' }],
      [
        'PUT /users/u%202/role',
        () => changeUserRole('u 2', { papel: 'admin', senha: 'minha-senha' }),
        { papel: 'admin', senha: 'minha-senha' },
      ],
      [
        'POST /users/u%201/password-reset',
        () => requestPasswordReset('u 1', 'minha-senha'),
        { senha: 'minha-senha' },
      ],
    ])('%s leva o método, o caminho e o corpo do contrato', async (key, call, body) => {
      const api = fakeUsersApi({ [key]: { status: 200, body: {} } });

      await call();

      const [request] = api.calls(key);
      expect(request.body).toEqual(body);
      expect(request.authorization).toBe('Bearer token-de-teste');
    });

    it('o reset da verificação com 204 devolve undefined', async () => {
      const api = fakeUsersApi({ 'POST /users/u-1/two-factor/reset': { status: 204 } });

      await expect(resetTwoFactor('u-1')).resolves.toBeUndefined();
      expect(api.calls('POST /users/u-1/two-factor/reset')[0].body).toBeUndefined();
    });

    it('o 404 do detalhe vira ApiError com o title', async () => {
      fakeUsersApi({ 'GET /users/u-x': problem(404, 'Usuário não encontrado.') });

      await expect(getUser('u-x')).rejects.toMatchObject({
        status: 404,
        title: 'Usuário não encontrado.',
      });
    });
  });
});
