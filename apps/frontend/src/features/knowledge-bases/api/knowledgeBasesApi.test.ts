import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  activateKnowledgeBase,
  createKnowledgeBase,
  deactivateKnowledgeBase,
  getKnowledgeBase,
  listKnowledgeBaseIndexingSummary,
  listKnowledgeBases,
  request,
  updateKnowledgeBase,
} from './knowledgeBasesApi';
import { clearToken, getToken, setToken } from '../../../auth/token';
import type { KnowledgeBase, KnowledgeBaseIndexingSummary } from '../types/knowledgeBase';

const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

const knowledgeBase: KnowledgeBase = {
  id,
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação, prazos e faixas de desconto.',
  isActive: true,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(body: unknown = knowledgeBase, status = 200) {
  const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body, status));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
  clearToken();
});

describe('request', () => {
  it('anexa o header Authorization quando há token armazenado', async () => {
    setToken('token-valido');
    const fetchMock = stubFetch();

    await request('/knowledge-bases');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer token-valido');
  });

  it('não anexa Authorization quando não há token armazenado', async () => {
    const fetchMock = stubFetch();

    await request('/knowledge-bases');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it('em 401, limpa o token armazenado e redireciona para /login', async () => {
    setToken('token-expirado');
    stubFetch({ title: 'unauthorized' }, 401);
    const originalLocation = window.location;
    Object.defineProperty(window, 'location', {
      value: { ...originalLocation, href: 'http://localhost/knowledge-bases' },
      writable: true,
      configurable: true,
    });

    await expect(request('/knowledge-bases')).rejects.toThrow();

    expect(getToken()).toBeNull();
    expect(window.location.href).toBe('/login');

    Object.defineProperty(window, 'location', {
      value: originalLocation,
      writable: true,
      configurable: true,
    });
  });

  it('carrega status e problem no ApiError', async () => {
    stubFetch({ title: 'Não encontrado', status: 404 }, 404);

    const error = await request('/knowledge-bases/x').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
    expect((error as ApiError).message).toBe('Não encontrado');
  });

  // O formulário depende deste formato para pintar o erro NO CAMPO em vez de
  // como erro genérico da tela (spec: "Erro de validação da API vira erro por
  // campo").
  it('preserva ValidationProblemDetails.errors em 400', async () => {
    stubFetch(
      {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { description: ['A descrição da base de conhecimento é obrigatória.'] },
      },
      400,
    );

    const error = (await request('/knowledge-bases', { method: 'POST' }).catch(
      (caught: unknown) => caught,
    )) as ApiError;

    expect(error.status).toBe(400);
    expect(error.problem?.errors?.description).toEqual([
      'A descrição da base de conhecimento é obrigatória.',
    ]);
  });
});

describe('rotas do catálogo de bases', () => {
  it('monta GET /knowledge-bases', async () => {
    const fetchMock = stubFetch([knowledgeBase]);

    await expect(listKnowledgeBases()).resolves.toEqual([knowledgeBase]);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(/\/knowledge-bases$/);
    expect(init.method).toBeUndefined();
  });

  it('monta GET /knowledge-bases/{id}', async () => {
    const fetchMock = stubFetch();

    await expect(getKnowledgeBase(id)).resolves.toEqual(knowledgeBase);

    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(new RegExp(`/knowledge-bases/${id}$`));
  });

  it('monta POST /knowledge-bases com nome e descrição no corpo', async () => {
    const fetchMock = stubFetch(knowledgeBase, 201);

    await createKnowledgeBase({ name: 'Cardápio', description: 'Pratos e preços.' });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(/\/knowledge-bases$/);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      name: 'Cardápio',
      description: 'Pratos e preços.',
    });
  });

  it('monta PUT /knowledge-bases/{id} com nome e descrição no corpo', async () => {
    const fetchMock = stubFetch();

    await updateKnowledgeBase(id, { name: 'Cardápio', description: 'Pratos e preços.' });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(new RegExp(`/knowledge-bases/${id}$`));
    expect(init.method).toBe('PUT');
    expect(JSON.parse(init.body as string)).toEqual({
      name: 'Cardápio',
      description: 'Pratos e preços.',
    });
  });

  it('monta POST /knowledge-bases/{id}/activate sem corpo', async () => {
    const fetchMock = stubFetch();

    await activateKnowledgeBase(id);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(new RegExp(`/knowledge-bases/${id}/activate$`));
    expect(init.method).toBe('POST');
    expect(init.body).toBeUndefined();
  });

  it('monta POST /knowledge-bases/{id}/deactivate sem corpo', async () => {
    const fetchMock = stubFetch();

    await deactivateKnowledgeBase(id);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(new RegExp(`/knowledge-bases/${id}/deactivate$`));
    expect(init.method).toBe('POST');
    expect(init.body).toBeUndefined();
  });
});

describe('resumo de indexação por base', () => {
  const summary: KnowledgeBaseIndexingSummary = {
    knowledgeBaseId: id,
    documentCount: 5,
    indexedCount: 3,
    failedCount: 1,
  };

  it('monta GET /knowledge-bases/indexing-summary e devolve os itens', async () => {
    const fetchMock = stubFetch([summary]);

    await expect(listKnowledgeBaseIndexingSummary()).resolves.toEqual([summary]);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit | undefined];
    expect(url).toMatch(/\/knowledge-bases\/indexing-summary$/);
    expect(init?.method).toBeUndefined();
  });

  // O par vazio da convenção 5. Não é simetria de fachada: a API responde 200
  // com lista vazia quando não existe base nenhuma, nunca 404, e é esse o caminho
  // que o catálogo vazio percorre.
  it('devolve lista vazia sem erro quando não existe base cadastrada', async () => {
    stubFetch([]);

    await expect(listKnowledgeBaseIndexingSummary()).resolves.toEqual([]);
  });
});
