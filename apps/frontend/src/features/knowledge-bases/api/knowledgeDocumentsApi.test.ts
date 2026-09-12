import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createKnowledgeDocument,
  deleteKnowledgeDocument,
  getKnowledgeDocument,
  listKnowledgeDocuments,
  reindexKnowledgeDocument,
  updateKnowledgeDocument,
} from './knowledgeDocumentsApi';
import { ApiError } from './knowledgeBasesApi';
import { clearToken } from '../../../auth/token';
import type { KnowledgeDocument } from '../types/knowledgeDocument';

const knowledgeBaseId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const documentId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const base = `/knowledge-bases/${knowledgeBaseId}/documents`;

const document: KnowledgeDocument = {
  id: documentId,
  knowledgeBaseId,
  title: 'Faixas de atraso e descontos',
  sourceType: 'markdown',
  extractedText: '# Faixas de atraso\n- 1 a 30 dias: sem desconto.',
  contentLengthBytes: 8420,
  indexingStatus: 'Indexed',
  indexedAt: '2026-09-02T03:14:00Z',
  failureReason: null,
  contentRevision: 1,
  fragmentCount: 14,
  indexingAttempts: 1,
  lastAttemptAt: '2026-09-02T03:14:00Z',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-02T00:00:00Z',
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(body: unknown = document, status = 200) {
  const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body, status));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function callOf(fetchMock: ReturnType<typeof vi.fn>) {
  const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit | undefined];
  return { url, init };
}

afterEach(() => {
  vi.unstubAllGlobals();
  clearToken();
});

describe('listKnowledgeDocuments', () => {
  it('chama a rota aninhada da base', async () => {
    const fetchMock = stubFetch([document]);

    const result = await listKnowledgeDocuments(knowledgeBaseId);

    expect(callOf(fetchMock).url).toContain(base);
    expect(result).toEqual([document]);
  });

  it('devolve lista vazia quando a base não tem documentos', async () => {
    stubFetch([]);

    await expect(listKnowledgeDocuments(knowledgeBaseId)).resolves.toEqual([]);
  });
});

describe('getKnowledgeDocument', () => {
  it('busca o documento inteiro, com extractedText', async () => {
    const fetchMock = stubFetch();

    const result = await getKnowledgeDocument(knowledgeBaseId, documentId);

    expect(callOf(fetchMock).url).toContain(`${base}/${documentId}`);
    expect(result.extractedText).toBe(document.extractedText);
  });
});

describe('createKnowledgeDocument', () => {
  it('envia POST com o conteúdo como string no corpo JSON', async () => {
    const fetchMock = stubFetch();

    await createKnowledgeDocument(knowledgeBaseId, {
      title: 'Prazos de ativação',
      sourceType: 'markdown',
      content: '# Ativação\nSudeste: 2 dias úteis.',
    });

    const { url, init } = callOf(fetchMock);
    expect(url).toContain(base);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(init?.body as string)).toEqual({
      title: 'Prazos de ativação',
      sourceType: 'markdown',
      content: '# Ativação\nSudeste: 2 dias úteis.',
    });
  });

  // O corpo é JSON, e não multipart/form-data — D3 da etapa 1. Esta asserção é o
  // que impede alguém de "resolver" o upload trocando o Content-Type do
  // request<T> da feature inteira, que é o que o handoff pedia.
  it('não usa multipart: o Content-Type continua application/json', async () => {
    const fetchMock = stubFetch();

    await createKnowledgeDocument(knowledgeBaseId, {
      title: 'Prazos de ativação',
      sourceType: 'markdown',
      content: 'texto',
    });

    const { init } = callOf(fetchMock);
    const headers = init?.headers as Record<string, string>;
    expect(headers['Content-Type']).toBe('application/json');
    expect(init?.body).toBeTypeOf('string');
  });
});

describe('updateKnowledgeDocument', () => {
  it('envia PUT no documento com o corpo completo', async () => {
    const fetchMock = stubFetch();

    await updateKnowledgeDocument(knowledgeBaseId, documentId, {
      title: 'Faixas de atraso e descontos',
      sourceType: 'markdown',
      content: '# Novo conteúdo',
    });

    const { url, init } = callOf(fetchMock);
    expect(url).toContain(`${base}/${documentId}`);
    expect(init?.method).toBe('PUT');
    expect(JSON.parse(init?.body as string).content).toBe('# Novo conteúdo');
  });
});

describe('deleteKnowledgeDocument', () => {
  it('envia DELETE e resolve sem corpo no 204', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(deleteKnowledgeDocument(knowledgeBaseId, documentId)).resolves.toBeUndefined();

    const { url, init } = callOf(fetchMock);
    expect(url).toContain(`${base}/${documentId}`);
    expect(init?.method).toBe('DELETE');
  });
});

describe('reindexKnowledgeDocument', () => {
  it('envia POST na rota de reindexação e devolve o documento já no estado novo', async () => {
    const reindexed: KnowledgeDocument = {
      ...document,
      indexingStatus: 'Pending',
      failureReason: null,
      indexingAttempts: 0,
      lastAttemptAt: null,
    };
    const fetchMock = stubFetch(reindexed);

    const result = await reindexKnowledgeDocument(knowledgeBaseId, documentId);

    const { url, init } = callOf(fetchMock);
    expect(url).toContain(`${base}/${documentId}/reindex`);
    expect(init?.method).toBe('POST');
    expect(result.indexingStatus).toBe('Pending');
    // IndexedAt e FragmentCount são PRESERVADOS por RequestReindex(): o conteúdo
    // anterior continua respondendo até a nova indexação terminar.
    expect(result.indexedAt).toBe(document.indexedAt);
    expect(result.fragmentCount).toBe(14);
  });
});

describe('erros', () => {
  it('resposta de erro vira ApiError com o status', async () => {
    stubFetch({ title: 'Documento não encontrado.' }, 404);

    await expect(getKnowledgeDocument(knowledgeBaseId, documentId)).rejects.toBeInstanceOf(
      ApiError,
    );
  });

  it('ValidationProblem preserva os erros por campo', async () => {
    stubFetch(
      {
        title: 'Erro de validação',
        status: 400,
        errors: { content: ['O conteúdo do documento é obrigatório e não pode ser vazio.'] },
      },
      400,
    );

    await expect(
      createKnowledgeDocument(knowledgeBaseId, { title: 't', sourceType: 'markdown', content: '' }),
    ).rejects.toMatchObject({
      status: 400,
      problem: { errors: { content: expect.any(Array) } },
    });
  });
});
