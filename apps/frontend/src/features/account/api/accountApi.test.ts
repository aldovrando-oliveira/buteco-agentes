import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { fakeAccountApi, problem } from '../test/fakeAccountApi';
import {
  ApiError,
  changePassword,
  connectFederated,
  disconnectOwnFederated,
  getAccount,
  getTwoFactor,
  listSessions,
  regenerateRecoveryCodes,
  revokeOtherSessions,
  revokeSession,
} from './accountApi';

beforeEach(() => {
  signInForTest('token-de-teste');
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('accountApi', () => {
  it.each<[string, () => Promise<unknown>]>([
    ['GET /account', () => getAccount()],
    ['GET /account/two-factor', () => getTwoFactor()],
    ['GET /account/sessions', () => listSessions()],
  ])('%s com o token da sessão', async (key, call) => {
    const api = fakeAccountApi({ [key]: { status: 200, body: {} } });

    await call();

    expect(api.calls(key)).toEqual([
      { method: 'GET', path: key.split(' ')[1], body: undefined, authorization: 'Bearer token-de-teste' },
    ]);
  });

  it.each<[string, () => Promise<unknown>, unknown]>([
    ['POST /account/password', () => changePassword('atual', 'nova-senha-longa'), { senhaAtual: 'atual', novaSenha: 'nova-senha-longa' }],
    ['POST /account/two-factor/recovery-codes', () => regenerateRecoveryCodes('abcd-efgh'), { codigo: 'abcd-efgh' }],
    ['POST /account/sessions/s%201/revoke', () => revokeSession('s 1'), undefined],
    ['POST /account/sessions/revoke-others', () => revokeOtherSessions(), undefined],
  ])('%s leva o corpo do contrato', async (key, call, body) => {
    const api = fakeAccountApi({ [key]: { status: 200, body: {} } });

    await call();

    const [request] = api.calls(key);
    expect(request.body).toEqual(body);
    expect(request.authorization).toBe('Bearer token-de-teste');
    const init = api.fetch.mock.calls[0][1] as RequestInit;
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
  });

  it('204 volta sem corpo', async () => {
    fakeAccountApi({ 'POST /account/password': { status: 204 } });

    await expect(changePassword('a', 'b')).resolves.toBeUndefined();
  });

  it('a recusa da rota vira ApiError com o title e o Retry-After, sem deslogar', async () => {
    const api = fakeAccountApi({
      'POST /account/password': problem(423, 'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.', {
        'Retry-After': '300',
      }),
    });

    const error = (await changePassword('a', 'b').catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(423);
    expect(error.title).toBe('Acesso pausado por tentativas seguidas. Tente de novo mais tarde.');
    expect(error.retryAfter).toBe(300);
    expect(api.calls('POST /account/password')).toHaveLength(1);
  });

  it('o 401 de senha errada vira ApiError sem renovar nem repetir', async () => {
    const api = fakeAccountApi({ 'POST /account/password': problem(401, 'Senha atual inválida.') });

    const error = (await changePassword('errada', 'nova-senha-longa').catch((e: unknown) => e)) as ApiError;

    expect(error.status).toBe(401);
    expect(error.title).toBe('Senha atual inválida.');
    expect(api.calls('POST /account/password')).toHaveLength(1);
    expect(authRuntime.assign).not.toHaveBeenCalled();
  });
});

// As contas de provedores (change telas-contas-federadas, D1): o corpo com UM campo da confirmação, e
// o `DELETE` sem corpo e sem senha.
describe('accountApi, contas de provedores', () => {
  it.each<[string, string, () => Promise<unknown>, unknown]>([
    ['POST /account/federated/google/connect', 'senha', () => connectFederated('google', { senha: 'minha-senha' }), { senha: 'minha-senha' }],
    ['POST /account/federated/google/connect', 'codigo', () => connectFederated('google', { codigo: '123456' }), { codigo: '123456' }],
  ])('%s leva só o campo %s', async (key, _campo, call, body) => {
    const api = fakeAccountApi({ [key]: { status: 200, body: { url: 'https://accounts.google.com/x' } } });

    await expect(call()).resolves.toEqual({ url: 'https://accounts.google.com/x' });

    expect(api.calls(key)[0].body).toEqual(body);
  });

  it('a chave vai codificada no caminho', async () => {
    const api = fakeAccountApi({ 'POST /account/federated/a%20b/connect': { status: 200, body: { url: 'u' } } });

    await connectFederated('a b', { senha: 's' });

    expect(api.calls('POST /account/federated/a%20b/connect')).toHaveLength(1);
  });

  it('o DELETE da própria conta não leva corpo', async () => {
    const api = fakeAccountApi({ 'DELETE /account/federated/google': { status: 204 } });

    await expect(disconnectOwnFederated('google')).resolves.toBeUndefined();

    expect(api.calls('DELETE /account/federated/google')).toEqual([
      { method: 'DELETE', path: '/account/federated/google', body: undefined, authorization: 'Bearer token-de-teste' },
    ]);
  });

  it('o 401 do código chega com o title, sem renovar a sessão', async () => {
    const api = fakeAccountApi({ 'POST /account/federated/google/connect': problem(401, 'Código inválido.') });

    await expect(connectFederated('google', { codigo: '000000' })).rejects.toMatchObject({ status: 401, title: 'Código inválido.' });
    expect(api.calls('POST /connect/token')).toHaveLength(0);
  });
});

