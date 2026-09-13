import { afterEach, describe, expect, it, vi } from 'vitest';
import { listKnowledgeIndexDiagnostics } from './knowledgeIndexApi';
import { ApiError } from './knowledgeBasesApi';
import { clearToken } from '../../../auth/token';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

// Os corpos abaixo são os CORPOS REAIS da rota, capturados à mão com apps/api de
// pé contra um pgvector real e guardados na seção "Corpos reais da rota" do
// design.md de knowledge-index-diagnostics. Não são fixture inventado aqui — é a
// diferença que a convenção 11 registra entre provar acordo e provar que dois
// lados do teste concordam entre si.
const umaCombinacao: KnowledgeIndexProvenance[] = [
  { provider: 'openai', model: 'qwen-qwen3-embedding-8b', dimensions: 4096, fragmentCount: 3 },
];

// Note a ordem: `nomic-` antes de `qwen-`, crescente por modelo, produzida pela
// COLLATION DO BANCO. A tela reproduz; não reordena.
const duasCombinacoes: KnowledgeIndexProvenance[] = [
  { provider: 'openai', model: 'nomic-embed-text-v1.5', dimensions: 4096, fragmentCount: 1 },
  { provider: 'openai', model: 'qwen-qwen3-embedding-8b', dimensions: 4096, fragmentCount: 3 },
];

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(body: unknown, status = 200) {
  const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body, status));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
  clearToken();
});

describe('listKnowledgeIndexDiagnostics', () => {
  it('requisita a rota global, sem identificador de base', async () => {
    const fetchMock = stubFetch(umaCombinacao);

    await listKnowledgeIndexDiagnostics();

    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://localhost:5017/knowledge-index/diagnostics');
  });

  it('devolve a combinação única com a contagem de fragmentos', async () => {
    stubFetch(umaCombinacao);

    await expect(listKnowledgeIndexDiagnostics()).resolves.toEqual(umaCombinacao);
  });

  // O par vazio da convenção 5 — e aqui ele não é formalidade: lista vazia é o
  // estado "índice vazio", que a tela renderiza de forma própria. Vazio chega
  // como sucesso, nunca como erro.
  it('devolve lista vazia sem erro quando o índice está vazio', async () => {
    stubFetch([]);

    await expect(listKnowledgeIndexDiagnostics()).resolves.toEqual([]);
  });

  // Mais de uma combinação é resposta VÁLIDA: HTTP 200 com dois itens. É o estado
  // em que apps/workers recusa o boot e apps/api continua de pé — ou seja, o
  // estado em que o operador abre esta tela.
  it('devolve as duas combinações, na ordem em que a API as enviou', async () => {
    stubFetch(duasCombinacoes);

    const result = await listKnowledgeIndexDiagnostics();

    expect(result).toEqual(duasCombinacoes);
    expect(result.map((item) => item.model)).toEqual([
      'nomic-embed-text-v1.5',
      'qwen-qwen3-embedding-8b',
    ]);
  });

  it('propaga ApiError quando a rota responde erro', async () => {
    stubFetch({ title: 'Erro interno' }, 500);

    await expect(listKnowledgeIndexDiagnostics()).rejects.toBeInstanceOf(ApiError);
  });

  // A rota é autenticada por omissão, pela FallbackPolicy de apps/api. Sem token
  // ela responde 401, e o caminho de `request<T>` limpa o token e navega para o
  // login — o mesmo de qualquer outra rota da feature.
  it('em 401 segue o caminho de request<T>', async () => {
    stubFetch({ title: 'unauthorized' }, 401);
    const originalLocation = window.location;
    Object.defineProperty(window, 'location', {
      value: { ...originalLocation, href: 'http://localhost/knowledge-bases/k1' },
      writable: true,
      configurable: true,
    });

    await expect(listKnowledgeIndexDiagnostics()).rejects.toBeInstanceOf(ApiError);
    expect(window.location.href).toBe('/login');

    Object.defineProperty(window, 'location', {
      value: originalLocation,
      writable: true,
      configurable: true,
    });
  });
});
