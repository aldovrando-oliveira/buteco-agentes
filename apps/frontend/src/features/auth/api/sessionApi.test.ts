import { afterEach, describe, expect, it, vi } from 'vitest';
import { createSession, submitSecondFactor } from './sessionApi';
import { jsonResponse } from '../../../test/authFakes';

function respond(response: Response | Error) {
  const fetchMock = vi.fn<(input: string, init?: RequestInit) => Promise<Response>>(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('createSession', () => {
  it('manda e-mail e senha em JSON ao /auth/session', async () => {
    const fetchMock = respond(new Response(null, { status: 204 }));

    await createSession({ email: 'ana@exemplo.test', senha: 'senha-certa' });

    expect(fetchMock).toHaveBeenCalledWith('/auth/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'ana@exemplo.test', senha: 'senha-certa' }),
    });
  });

  it('204 é sessão aberta', async () => {
    respond(new Response(null, { status: 204 }));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'ok' });
  });

  it('200 com segundoPasso "totp" abre o passo do código', async () => {
    respond(jsonResponse({ segundoPasso: 'totp' }, 200));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'segundo-passo' });
  });

  it('200 sem segundoPasso não é tomado por segundo passo', async () => {
    respond(jsonResponse({}, 200));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'http', status: 200 });
  });

  it('outro status devolve o title e o Retry-After', async () => {
    respond(jsonResponse({ title: 'Acesso pausado.' }, 423, { 'Retry-After': '600' }));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({
      kind: 'http',
      status: 423,
      title: 'Acesso pausado.',
      retryAfter: 600,
    });
  });

  it('falha de rede vira network', async () => {
    respond(new TypeError('Failed to fetch'));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'network' });
  });
});

describe('submitSecondFactor', () => {
  it('manda o código em JSON ao /auth/session/second-factor', async () => {
    const fetchMock = respond(new Response(null, { status: 204 }));

    expect(await submitSecondFactor('123456')).toEqual({ kind: 'ok' });
    expect(fetchMock).toHaveBeenCalledWith('/auth/session/second-factor', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ codigo: '123456' }),
    });
  });

  it('401 devolve o title', async () => {
    respond(jsonResponse({ title: 'Código inválido.' }, 401));
    expect(await submitSecondFactor('1')).toEqual({ kind: 'http', status: 401, title: 'Código inválido.', retryAfter: undefined });
  });

  it('falha de rede vira network', async () => {
    respond(new TypeError('Failed to fetch'));
    expect(await submitSecondFactor('1')).toEqual({ kind: 'network' });
  });
});
