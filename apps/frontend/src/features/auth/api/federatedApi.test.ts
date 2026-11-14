import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse } from '../../../test/authFakes';
import {
  classify404,
  KNOWN_PROVIDER_TYPES,
  listLoginProviders,
  readRefusalEmail,
  START_404_TITLES,
  startFederatedInvitation,
  startFederatedLogin,
} from './federatedApi';

// A API federada do painel (spec `panel-federated-login` e `panel-access-link-pages`; design.md da
// change telas-login-google, D1 a D3).

afterEach(() => {
  vi.unstubAllGlobals();
});

function stub(response: Response | Error) {
  const fetchMock = vi.fn<(input: string, init?: RequestInit) => Promise<Response>>(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('os títulos dos 404 do início', () => {
  // Os valores de hoje do backend, que separam os dois 404 só pelo `title`:
  // - apps/api/src/Buteco.Api/Identity/Federated/FederatedLoginEndpoints.cs, `NotConfiguredMessage`;
  // - apps/api/src/Buteco.Api/Users/AccessLinks/AccessLinkPublicEndpoints.cs, `InvalidLinkMessage`.
  // Se o backend mudar um deles, este caso reprova antes da tela confundir os dois.
  it('o do provedor não configurado é o do FederatedLoginEndpoints', () => {
    expect(START_404_TITLES.providerNotConfigured).toBe('Provedor não configurado.');
  });

  it('o do link inválido é o do AccessLinkPublicEndpoints', () => {
    expect(START_404_TITLES.invalidLink).toBe(
      'Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.',
    );
  });

  it('classifica o 404 pelo title exato', () => {
    expect(
      classify404({ kind: 'http', status: 404, title: START_404_TITLES.providerNotConfigured }),
    ).toBe('provider-not-configured');
    expect(classify404({ kind: 'http', status: 404, title: START_404_TITLES.invalidLink })).toBe(
      'invalid-link',
    );
    expect(classify404({ kind: 'http', status: 404 })).toBe('other');
    expect(classify404({ kind: 'http', status: 404, title: 'Not Found' })).toBe('other');
    expect(classify404({ kind: 'http', status: 404, title: 'provedor não configurado.' })).toBe(
      'other',
    );
  });
});

describe('listLoginProviders', () => {
  it('pede GET /auth/federated/providers, sem corpo, e devolve os de tipo conhecido com o nome', async () => {
    const fetchMock = stub(
      jsonResponse({
        provedores: [
          { chave: 'corporativo', tipo: 'google' },
          { chave: 'sso', tipo: 'keycloak' },
        ],
      }),
    );

    await expect(listLoginProviders()).resolves.toEqual([
      { chave: 'corporativo', tipo: 'google', nome: 'Google' },
    ]);
    expect(fetchMock.mock.calls).toEqual([['/auth/federated/providers']]);
  });

  it('o tipo desconhecido não está no mapa', () => {
    expect(KNOWN_PROVIDER_TYPES.keycloak).toBeUndefined();
    expect(KNOWN_PROVIDER_TYPES.google).toBe('Google');
  });

  it.each([
    ['500', () => jsonResponse({}, 500)],
    ['um 200 sem provedores', () => jsonResponse({}, 200)],
    ['provedores que não é lista', () => jsonResponse({ provedores: 'google' }, 200)],
    [
      'itens sem chave em texto',
      () => jsonResponse({ provedores: [{ chave: 1, tipo: 'google' }] }, 200),
    ],
    ['um corpo que não é JSON', () => new Response('<html>', { status: 200 })],
  ])('com %s, a lista vazia', async (_nome, resposta) => {
    stub(resposta());
    await expect(listLoginProviders()).resolves.toEqual([]);
  });

  it('com a rede caída, a lista vazia', async () => {
    stub(new TypeError('Failed to fetch'));
    await expect(listLoginProviders()).resolves.toEqual([]);
  });
});

describe('os inícios', () => {
  it('o do login vai ao caminho da chave, com o retorno no corpo', async () => {
    const fetchMock = stub(jsonResponse({ url: 'https://accounts.google.com/x' }));

    await expect(startFederatedLogin('corporativo', '/connect/authorize?a=1')).resolves.toEqual({
      kind: 'ok',
      url: 'https://accounts.google.com/x',
    });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/auth/federated/corporativo/start');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ retorno: '/connect/authorize?a=1' });
  });

  it('o do convite leva o token só no corpo', async () => {
    const fetchMock = stub(jsonResponse({ url: 'https://accounts.google.com/x' }));

    await startFederatedInvitation('corporativo', 'k7Qp2vX9');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/auth/invitation/federated/corporativo/start');
    expect(url).not.toContain('k7Qp2vX9');
    expect(JSON.parse(String(init?.body))).toEqual({ token: 'k7Qp2vX9' });
  });

  it('a recusa traz o status e o title; a rede, network', async () => {
    stub(jsonResponse({ title: START_404_TITLES.providerNotConfigured }, 404));
    await expect(startFederatedLogin('corporativo', null)).resolves.toMatchObject({
      kind: 'http',
      status: 404,
      title: START_404_TITLES.providerNotConfigured,
    });
    stub(new TypeError('Failed to fetch'));
    await expect(startFederatedLogin('corporativo', null)).resolves.toEqual({ kind: 'network' });
  });
});

describe('readRefusalEmail', () => {
  it('o 200 devolve o motivo, o provedor e o e-mail', async () => {
    const fetchMock = stub(
      jsonResponse({ motivo: 'sem-acesso', provedor: 'corporativo', email: 'maria@empresa.com' }),
    );
    await expect(readRefusalEmail()).resolves.toEqual({
      motivo: 'sem-acesso',
      provedor: 'corporativo',
      email: 'maria@empresa.com',
    });
    expect(fetchMock.mock.calls[0][0]).toBe('/auth/federated/refusal');
    expect(fetchMock.mock.calls[0][1]?.method).toBe('POST');
  });

  it.each([
    ['o 404 vazio', () => new Response(null, { status: 404 })],
    ['um 200 sem e-mail', () => jsonResponse({ motivo: 'sem-acesso', provedor: 'corporativo' })],
    [
      'um 200 sem o motivo',
      () => jsonResponse({ provedor: 'corporativo', email: 'maria@empresa.com' }),
    ],
    [
      'um 200 sem o provedor',
      () => jsonResponse({ motivo: 'sem-acesso', email: 'maria@empresa.com' }),
    ],
  ])('%s devolve null', async (_nome, resposta) => {
    stub(resposta());
    await expect(readRefusalEmail()).resolves.toBeNull();
  });

  it('a rede caída devolve null', async () => {
    stub(new TypeError('Failed to fetch'));
    await expect(readRefusalEmail()).resolves.toBeNull();
  });
});
