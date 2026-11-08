import { afterEach, describe, expect, it, vi } from 'vitest';
import { confirmEnrollment, createSession, startEnrollment, submitSecondFactor } from './sessionApi';
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

  it('204 não é sessão: o login não responde mais 204, e cai no aviso genérico', async () => {
    respond(new Response(null, { status: 204 }));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'http', status: 204, title: undefined, retryAfter: undefined });
  });

  it('200 com segundoPasso "totp" abre o passo do código', async () => {
    respond(jsonResponse({ segundoPasso: 'totp' }, 200));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'segundo-passo', passo: 'totp' });
  });

  it('200 com segundoPasso "cadastro" abre o cadastro', async () => {
    respond(jsonResponse({ segundoPasso: 'cadastro' }, 200));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'segundo-passo', passo: 'cadastro' });
  });

  it('200 com um segundo passo desconhecido não abre etapa nenhuma', async () => {
    respond(jsonResponse({ segundoPasso: 'webauthn' }, 200));
    expect(await createSession({ email: 'a', senha: 'b' })).toEqual({ kind: 'http', status: 200 });
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

describe('startEnrollment', () => {
  it('manda um JSON vazio ao /auth/session/enrollment/setup, sem token, e devolve o segredo e o URI', async () => {
    const fetchMock = respond(jsonResponse({ segredo: 'JBSWY3DPEHPK3PXP', otpauthUri: 'otpauth://totp/x' }, 200));

    expect(await startEnrollment()).toEqual({ kind: 'ok', segredo: 'JBSWY3DPEHPK3PXP', otpauthUri: 'otpauth://totp/x' });
    expect(fetchMock).toHaveBeenCalledWith('/auth/session/enrollment/setup', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{}',
    });
  });

  it('200 sem o segredo não é tomado por QR', async () => {
    respond(jsonResponse({}, 200));
    expect(await startEnrollment()).toEqual({ kind: 'http', status: 200 });
  });

  it('401 devolve o title', async () => {
    respond(jsonResponse({ title: 'A entrada expirou. Informe e-mail e senha de novo.' }, 401));
    expect(await startEnrollment()).toEqual({
      kind: 'http',
      status: 401,
      title: 'A entrada expirou. Informe e-mail e senha de novo.',
      retryAfter: undefined,
    });
  });

  it('falha de rede vira network', async () => {
    respond(new TypeError('Failed to fetch'));
    expect(await startEnrollment()).toEqual({ kind: 'network' });
  });
});

describe('confirmEnrollment', () => {
  it('manda o código ao /auth/session/enrollment/confirm e devolve os códigos de recuperação', async () => {
    const fetchMock = respond(jsonResponse({ codigosRecuperacao: ['A', 'B'] }, 200));

    expect(await confirmEnrollment('123456')).toEqual({ kind: 'ok', codigosRecuperacao: ['A', 'B'] });
    expect(fetchMock).toHaveBeenCalledWith('/auth/session/enrollment/confirm', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ codigo: '123456' }),
    });
  });

  it('200 sem os códigos não é tomado por cadastro concluído', async () => {
    respond(jsonResponse({ codigosRecuperacao: [1] }, 200));
    expect(await confirmEnrollment('1')).toEqual({ kind: 'http', status: 200 });
  });

  it('423 devolve o Retry-After', async () => {
    respond(jsonResponse({ title: 'Acesso pausado.' }, 423, { 'Retry-After': '300' }));
    expect(await confirmEnrollment('1')).toEqual({ kind: 'http', status: 423, title: 'Acesso pausado.', retryAfter: 300 });
  });

  it('falha de rede vira network', async () => {
    respond(new TypeError('Failed to fetch'));
    expect(await confirmEnrollment('1')).toEqual({ kind: 'network' });
  });
});
