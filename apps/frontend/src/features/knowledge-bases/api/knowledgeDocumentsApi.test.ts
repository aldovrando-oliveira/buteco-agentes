import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createKnowledgeDocument,
  deleteKnowledgeDocument,
  getKnowledgeDocument,
  listKnowledgeDocumentEvents,
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

// O corpo é TEXTO JSON escrito à mão com as chaves e os valores de `type` que
// KnowledgeDocumentCatalogTests.cs:653-674 (apps/api) afirma sobre o texto da
// resposta real — e não um objeto do próprio tipo serializado aqui, que passaria
// igual com o tipo errado dos dois lados (convenção 12).
const EVENTS_WIRE = `{"items":[
  {"id":"e-3","documentId":"${documentId}","documentTitle":"Renomeado","type":"Deleted","contentChanged":null,"titleChanged":null,"author":"operator","occurredAt":"2026-10-02T12:00:03.123456+00:00"},
  {"id":"e-2","documentId":"${documentId}","documentTitle":"Renomeado","type":"Updated","contentChanged":false,"titleChanged":true,"author":"operator","occurredAt":"2026-10-02T12:00:02.123456+00:00"},
  {"id":"e-1","documentId":"${documentId}","documentTitle":"Documento","type":"Created","contentChanged":null,"titleChanged":null,"author":"service:connectors","occurredAt":"2026-10-02T12:00:01.123456+00:00"}
],"nextCursor":"AAAA-_bb"}`;

function stubFetchText(text: string) {
  const fetchMock = vi
    .fn()
    .mockResolvedValue(
      new Response(text, { status: 200, headers: { 'Content-Type': 'application/json' } }),
    );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('listKnowledgeDocumentEvents', () => {
  const eventsPath = `/knowledge-bases/${knowledgeBaseId}/document-events`;

  it('a primeira página é pedida sem query string', async () => {
    const fetchMock = stubFetchText(EVENTS_WIRE);

    await listKnowledgeDocumentEvents(knowledgeBaseId, null);

    const { url, init } = callOf(fetchMock);
    expect(url.endsWith(eventsPath)).toBe(true);
    expect(url).not.toContain('?');
    expect(init?.method ?? 'GET').toBe('GET');
  });

  it('a página seguinte leva o cursor codificado', async () => {
    const fetchMock = stubFetchText(EVENTS_WIRE);

    await listKnowledgeDocumentEvents(knowledgeBaseId, 'a+b/c=');

    expect(callOf(fetchMock).url.endsWith(`${eventsPath}?cursor=a%2Bb%2Fc%3D`)).toBe(true);
  });

  it('lê os campos do fio, com type como string', async () => {
    stubFetchText(EVENTS_WIRE);

    const page = await listKnowledgeDocumentEvents(knowledgeBaseId, null);

    expect(page.nextCursor).toBe('AAAA-_bb');
    expect(page.items.map((event) => event.type)).toEqual(['Deleted', 'Updated', 'Created']);
    expect(page.items[1]).toEqual({
      id: 'e-2',
      documentId,
      documentTitle: 'Renomeado',
      type: 'Updated',
      contentChanged: false,
      titleChanged: true,
      author: 'operator',
      occurredAt: '2026-10-02T12:00:02.123456+00:00',
    });
    expect(page.items[2].author).toBe('service:connectors');
  });
});
