import { afterEach, describe, expect, it, vi } from 'vitest';
import { replaceAgentKnowledgeBases, request } from './agentsApi';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { sessionFetchContract } from '../../../test/sessionContract';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
  signOutForTest();
});

describe('request', () => {
  it('anexa o header Authorization quando há token armazenado', async () => {
    signInForTest('token-valido');
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal('fetch', fetchMock);

    await request('/agents');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer token-valido');
  });

  it('não anexa Authorization quando não há token armazenado', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal('fetch', fetchMock);

    await request('/agents');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  describe('sessão pelo sessionFetch', () => {
    sessionFetchContract(() => request('/agents'));
  });
});

describe('replaceAgentKnowledgeBases', () => {
  it('envia PUT para a rota do agente com o conjunto completo em knowledgeBaseIds', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 'agente-1' }));
    vi.stubGlobal('fetch', fetchMock);

    await replaceAgentKnowledgeBases('agente-1', ['base-a', 'base-b']);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toContain('/agents/agente-1/knowledge-bases');
    expect(init.method).toBe('PUT');
    expect(JSON.parse(init.body as string)).toEqual({
      knowledgeBaseIds: ['base-a', 'base-b'],
    });
  });

  // O conjunto vazio é como se removem todos os vínculos, e precisa ir como
  // lista vazia explícita: campo ausente faz o servidor responder 400
  // ("envie uma lista vazia para remover todos os vínculos").
  it('envia lista vazia explícita ao remover todos os vínculos, nunca o campo ausente', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 'agente-1' }));
    vi.stubGlobal('fetch', fetchMock);

    await replaceAgentKnowledgeBases('agente-1', []);

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const body = JSON.parse(init.body as string) as Record<string, unknown>;
    expect(body).toHaveProperty('knowledgeBaseIds');
    expect(body.knowledgeBaseIds).toEqual([]);
  });
});
