import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { fakeAccountApi } from '../../account/test/fakeAccountApi';
import { getFederatedProviders } from './identityApi';

// `GET /identity/federated-providers` (change telas-contas-federadas, D1 e D7): a lista sai de
// `provedores`, com o token da sessão.

beforeEach(() => {
  signInForTest('token-de-teste');
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('identityApi, provedores federados', () => {
  it('a lista vem de provedores, com o token da sessão', async () => {
    const provedor = {
      chave: 'google',
      tipo: 'google',
      nome: 'Google',
      issuer: 'i',
      usadoPara: ['login'],
      clientId: 'c',
      uriDeRetorno: 'u',
      vinculos: 2,
    };
    const api = fakeAccountApi({
      'GET /identity/federated-providers': { status: 200, body: { provedores: [provedor] } },
    });

    await expect(getFederatedProviders()).resolves.toEqual([provedor]);

    expect(api.calls('GET /identity/federated-providers')[0].authorization).toBe(
      'Bearer token-de-teste',
    );
  });
});
