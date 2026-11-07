import { afterEach, describe, expect, it, vi } from 'vitest';
import { completeLink, lookupLink } from './accessLinkApi';
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

const json = { method: 'POST', headers: { 'Content-Type': 'application/json' } };

describe('accessLinkApi', () => {
  it.each([
    ['convite', '/auth/invitation'],
    ['redefinir-senha', '/auth/password-reset'],
  ] as const)('a consulta do %s é POST %s com o token no corpo, sem Authorization', async (purpose, path) => {
    const fetchMock = respond(jsonResponse({ nome: 'Ana', email: 'ana@x.test', expiraEm: '2026-11-06T17:20:00Z' }));

    await lookupLink(purpose, 'tok');

    expect(fetchMock).toHaveBeenCalledWith(path, { ...json, body: JSON.stringify({ token: 'tok' }) });
  });

  it.each([
    ['convite', '/auth/invitation/accept'],
    ['redefinir-senha', '/auth/password-reset/complete'],
  ] as const)('a conclusão do %s é POST %s com token e senha', async (purpose, path) => {
    const fetchMock = respond(new Response(null, { status: 204 }));

    expect(await completeLink(purpose, 'tok', 'senha-de-doze')).toEqual({ kind: 'ok' });
    expect(fetchMock).toHaveBeenCalledWith(path, { ...json, body: JSON.stringify({ token: 'tok', senha: 'senha-de-doze' }) });
  });

  it('a consulta devolve os dados do link', async () => {
    respond(jsonResponse({ nome: 'Marcos Lima', email: 'm@x.test', convidadoPor: 'Aldovrando Oliveira', expiraEm: 'X' }));

    expect(await lookupLink('convite', 'tok')).toEqual({
      kind: 'ok',
      link: { nome: 'Marcos Lima', email: 'm@x.test', convidadoPor: 'Aldovrando Oliveira', expiraEm: 'X' },
    });
  });

  it('404 vira http com o status', async () => {
    respond(jsonResponse({ title: 'Este link não vale mais.' }, 404));
    expect(await lookupLink('convite', 'tok')).toMatchObject({ kind: 'http', status: 404 });
  });

  it('falha de rede vira network', async () => {
    respond(new TypeError('Failed to fetch'));
    expect(await completeLink('convite', 'tok', 's')).toEqual({ kind: 'network' });
  });
});
