import { describe, expect, it, vi } from 'vitest';
import {
  PENDING_LOGIN_KEY,
  beginLogin,
  exchangeCode,
  followableAuthorizeReturn,
  safeReturnPath,
  takePendingLogin,
} from './loginFlow';
import { codeChallengeS256 } from './pkce';
import { createRenewal } from './renewal';
import { createSessionStore } from './sessionStore';
import { fakeRuntime, jsonResponse, tokenResponse } from '../test/authFakes';
import type { AuthRuntime } from './runtime';

const ORIGIN = 'https://painel.exemplo.test';

describe('retorno do authorize', () => {
  it.each([
    ['/connect/authorize?client_id=buteco-painel&state=x', '/connect/authorize?client_id=buteco-painel&state=x'],
    ['/connect/authorize?a=1', '/connect/authorize?a=1'],
  ])('segue %s', (retorno, esperado) => {
    expect(followableAuthorizeReturn(retorno)).toBe(esperado);
  });

  it.each([
    ['ausente', null],
    ['vazio', ''],
    ['absoluto', 'https://outro.exemplo/connect/authorize?x'],
    ['começa por //', '//outro.exemplo/connect/authorize?x'],
    ['começa por /\\', '/\\outro.exemplo/connect/authorize?x'],
    ['outro caminho com o mesmo prefixo', '/connect/authorizex?x'],
    ['sem query', '/connect/authorize'],
    ['logout', '/connect/logout?x'],
    ['codificado duas vezes', '%2Fconnect%2Fauthorize%3Fclient_id%3Dbuteco-painel'],
    ['tela do painel', '/agents'],
  ])('recusa %s', (_, retorno) => {
    expect(followableAuthorizeReturn(retorno)).toBeNull();
  });

  it('devolve exatamente a string conferida, sem decodificar de novo nem recompor', () => {
    // Já decodificado uma vez pela leitura da query; o que sobra codificado é parte do
    // valor (o redirect_uri e o state do authorize original) e vai como está.
    const retorno =
      '/connect/authorize?client_id=buteco-painel&redirect_uri=https%3A%2F%2Fpainel.exemplo.test%2Fentrar%2Fretorno&state=a%2Bb%3D&scope=openid%20offline_access';

    expect(followableAuthorizeReturn(retorno)).toBe(retorno);
  });
});

describe('voltarPara', () => {
  it.each([
    ['/knowledge-bases', '/knowledge-bases'],
    ['/agents/1?tab=ferramentas', '/agents/1?tab=ferramentas'],
    ['/', '/'],
  ])('aceita %s', (valor, esperado) => {
    expect(safeReturnPath(valor)).toBe(esperado);
  });

  it.each([
    [null],
    [''],
    ['//host-externo/x'],
    ['/\\host-externo/x'],
    ['https://host-externo/x'],
    ['agents'],
    ['/entrar'],
    ['/entrar/retorno'],
    ['/entrar?motivo=sessao-expirada'],
  ])('recusa %s, que vira /', (valor) => {
    expect(safeReturnPath(valor)).toBe('/');
  });
});

describe('beginLogin', () => {
  it('grava o login em andamento e monta o authorize com PKCE S256, state e os quatro escopos', async () => {
    const tab = fakeRuntime();

    const url = new URL(await beginLogin(tab.runtime, '/knowledge-bases'), ORIGIN);

    const pending = JSON.parse(tab.tabStorage.getItem(PENDING_LOGIN_KEY)!);
    expect(pending).toMatchObject({ voltarPara: '/knowledge-bases', criadoEm: tab.clock.now });
    expect(url.pathname).toBe('/connect/authorize');
    expect(Object.fromEntries(url.searchParams)).toEqual({
      response_type: 'code',
      client_id: 'buteco-painel',
      redirect_uri: `${ORIGIN}/entrar/retorno`,
      scope: 'openid offline_access buteco-api buteco-inbox buteco-connectors',
      code_challenge: await codeChallengeS256(pending.verifier),
      code_challenge_method: 'S256',
      state: pending.state,
    });
  });

  it('um início novo substitui o anterior', async () => {
    const tab = fakeRuntime();
    await beginLogin(tab.runtime, '/a');
    const first = tab.tabStorage.getItem(PENDING_LOGIN_KEY);

    await beginLogin(tab.runtime, '/b');

    expect(tab.tabStorage.getItem(PENDING_LOGIN_KEY)).not.toBe(first);
    expect(JSON.parse(tab.tabStorage.getItem(PENDING_LOGIN_KEY)!).voltarPara).toBe('/b');
  });

  it('voltarPara de outra origem vira /', async () => {
    const tab = fakeRuntime();
    await beginLogin(tab.runtime, '//host-externo/x');

    expect(JSON.parse(tab.tabStorage.getItem(PENDING_LOGIN_KEY)!).voltarPara).toBe('/');
  });
});

describe('login em andamento', () => {
  it('é de uso único', async () => {
    const tab = fakeRuntime();
    await beginLogin(tab.runtime, '/a');

    expect(takePendingLogin(tab.runtime)).not.toBeNull();
    expect(takePendingLogin(tab.runtime)).toBeNull();
  });

  it('com 10 minutos ainda vale', async () => {
    const tab = fakeRuntime();
    await beginLogin(tab.runtime, '/a');
    tab.clock.now += 10 * 60_000;

    expect(takePendingLogin(tab.runtime)).not.toBeNull();
  });

  it('com 11 minutos é recusado', async () => {
    const tab = fakeRuntime();
    await beginLogin(tab.runtime, '/a');
    tab.clock.now += 11 * 60_000;

    expect(takePendingLogin(tab.runtime)).toBeNull();
  });

  it('ilegível é ausência', () => {
    const tab = fakeRuntime();
    tab.tabStorage.setItem(PENDING_LOGIN_KEY, '{nada');

    expect(takePendingLogin(tab.runtime)).toBeNull();
  });
});

describe('troca do código', () => {
  function setup(fetch: AuthRuntime['fetch']) {
    const tab = fakeRuntime({ fetch });
    const store = createSessionStore(tab.runtime.storage, tab.runtime.events);
    return { ...tab, store, renewal: createRenewal(tab.runtime, store) };
  }

  it('200 grava a sessão, com o pedido de authorization_code', async () => {
    const fetch = vi.fn<AuthRuntime['fetch']>(async () => tokenResponse(1));
    const tab = setup(fetch);

    expect(await exchangeCode(tab.runtime, tab.renewal, { code: 'abc', verifier: 'v' })).toBe('ok');

    expect(tab.store.read()).toMatchObject({ accessToken: 'access-1', nome: 'Ana Souza' });
    expect(Object.fromEntries(new URLSearchParams(String(fetch.mock.calls[0][1]?.body)))).toEqual({
      grant_type: 'authorization_code',
      code: 'abc',
      redirect_uri: `${ORIGIN}/entrar/retorno`,
      client_id: 'buteco-painel',
      code_verifier: 'v',
    });
  });

  it('invalid_grant é recusa, sem repetir e sem gravar', async () => {
    const fetch = vi.fn<AuthRuntime['fetch']>(async () => jsonResponse({ error: 'invalid_grant' }, 400));
    const tab = setup(fetch);

    expect(await exchangeCode(tab.runtime, tab.renewal, { code: 'abc', verifier: 'v' })).toBe('rejected');
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(tab.store.read()).toBeNull();
  });

  it('503 repete uma vez depois do Retry-After, e a segunda resposta vale', async () => {
    const fetch = vi
      .fn<AuthRuntime['fetch']>()
      .mockResolvedValueOnce(jsonResponse({}, 503, { 'Retry-After': '3' }))
      .mockResolvedValueOnce(tokenResponse(1));
    const tab = setup(fetch);

    expect(await exchangeCode(tab.runtime, tab.renewal, { code: 'abc', verifier: 'v' })).toBe('ok');
    expect(tab.sleeps).toEqual([3_000]);
  });

  it('invalid_client duas vezes é indisponível', async () => {
    const fetch = vi.fn<AuthRuntime['fetch']>(async () => jsonResponse({ error: 'invalid_client' }, 400));
    const tab = setup(fetch);

    expect(await exchangeCode(tab.runtime, tab.renewal, { code: 'abc', verifier: 'v' })).toBe('unavailable');
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});
