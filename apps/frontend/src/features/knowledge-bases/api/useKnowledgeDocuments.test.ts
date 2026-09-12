import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, type Query } from '@tanstack/react-query';
import {
  documentsQueryKey,
  useCreateKnowledgeDocumentMutation,
  useDeleteKnowledgeDocumentMutation,
  useKnowledgeDocumentsQuery,
  useReindexKnowledgeDocumentMutation,
  useUpdateKnowledgeDocumentMutation,
} from './useKnowledgeDocuments';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';

const knowledgeBaseId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const documentId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function doc(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: documentId,
    knowledgeBaseId,
    title: 'Faixas de atraso',
    sourceType: 'markdown',
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
    ...overrides,
  };
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

// Uma Response NOVA por chamada. O corpo de um Response só pode ser lido uma
// vez, então `mockResolvedValue(jsonResponse(...))` devolve o mesmo objeto já
// consumido na segunda chamada — e os testes de polling fazem várias.
function fetchAlways(body: unknown, status = 200) {
  return vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(body, status)));
}

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    queryClient,
    Wrapper: ({ children }: PropsWithChildren) =>
      createElement(QueryClientProvider, { client: queryClient }, children),
  };
}

// Resolve a opção `refetchInterval` REAL que o hook passou, contra a query REAL
// do cache — não uma reimplementação da condição dentro do teste.
//
// É o par determinístico da convenção 15: o teste comportamental com timers
// abaixo prova o efeito, e este prova o artefato que o produz, em 100% das
// execuções.
function resolveRefetchInterval(queryClient: QueryClient): number | false | undefined {
  const query = queryClient.getQueryCache().find({ queryKey: documentsQueryKey(knowledgeBaseId) });
  const option = query?.observers[0]?.options.refetchInterval;

  return typeof option === 'function'
    ? (option as (q: Query) => number | false | undefined)(query as unknown as Query)
    : option;
}

// ATENÇÃO ao escrever teste de hook desta base: o react-query usa
// `notifyOnChangeProps: 'tracked'` por padrão, e o objeto devolvido é um PROXY
// que registra quais props foram LIDAS. O componente de `renderHook` não lê
// nada — quem lê é o teste, por `result.current`.
//
// Consequência prática, medida aqui: se o primeiro `waitFor` tocar só
// `isSuccess`, `data` nunca entra no conjunto rastreado, e uma mudança
// posterior SÓ em `data` não provoca re-render — `result.current.data` fica
// preso no primeiro valor e o `waitFor` seguinte estoura o prazo, com a
// requisição tendo acontecido e devolvido o dado novo. Por isso os testes
// abaixo esperam por `data` desde a primeira espera.
afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('useKnowledgeDocumentsQuery', () => {
  it('retorna a listagem de documentos da base', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([doc()])));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeDocumentsQuery(knowledgeBaseId), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([doc()]);
  });

  it('propaga o erro quando a listagem falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'erro' }, 500)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeDocumentsQuery(knowledgeBaseId), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.data).toBeUndefined();
  });

  it('agenda recarga enquanto houver documento não-terminal', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse([
            doc({ id: 'a', indexingStatus: 'Indexed' }),
            doc({ id: 'b', indexingStatus: 'Indexing', indexedAt: null, fragmentCount: 0 }),
          ]),
        ),
    );
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useKnowledgeDocumentsQuery(knowledgeBaseId), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(resolveRefetchInterval(queryClient)).toBe(4000);
  });

  it('não agenda recarga quando todos os documentos estão terminais', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse([
            doc({ id: 'a', indexingStatus: 'Indexed' }),
            doc({ id: 'b', indexingStatus: 'Failed', failureReason: '429 do provedor.' }),
          ]),
        ),
    );
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useKnowledgeDocumentsQuery(knowledgeBaseId), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(resolveRefetchInterval(queryClient)).toBe(false);
  });

  it('deixa de agendar quando o último não-terminal chega a terminal', async () => {
    let terminal = false;
    const fetchMock = vi
      .fn()
      .mockImplementation(() =>
        Promise.resolve(
          jsonResponse([
            terminal
              ? doc({ indexingStatus: 'Indexed' })
              : doc({ indexingStatus: 'Indexing', indexedAt: null, fragmentCount: 0 }),
          ]),
        ),
      );
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useKnowledgeDocumentsQuery(knowledgeBaseId), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.data).toHaveLength(1));
    expect(resolveRefetchInterval(queryClient)).toBe(4000);

    terminal = true;
    await result.current.refetch();

    await waitFor(() => expect(result.current.data?.[0].indexingStatus).toBe('Indexed'));
    expect(resolveRefetchInterval(queryClient)).toBe(false);
  });

  // Guarda comportamental, pareado com o determinístico acima. Prova o efeito —
  // a requisição realmente se repete —, que a resolução da opção sozinha não
  // prova.
  it('recarrega de fato depois do intervalo, com documento não-terminal', async () => {
    vi.useFakeTimers();
    const fetchMock = fetchAlways([
      doc({ indexingStatus: 'Pending', indexedAt: null, fragmentCount: 0 }),
    ]);
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();

    renderHook(() => useKnowledgeDocumentsQuery(knowledgeBaseId), { wrapper: Wrapper });

    await vi.advanceTimersByTimeAsync(0);
    const depoisDaPrimeira = fetchMock.mock.calls.length;

    await vi.advanceTimersByTimeAsync(4500);

    expect(fetchMock.mock.calls.length).toBeGreaterThan(depoisDaPrimeira);
  });

  it('não recarrega depois do intervalo quando todos estão terminais', async () => {
    vi.useFakeTimers();
    const fetchMock = fetchAlways([doc({ indexingStatus: 'Indexed' })]);
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();

    renderHook(() => useKnowledgeDocumentsQuery(knowledgeBaseId), { wrapper: Wrapper });

    await vi.advanceTimersByTimeAsync(0);
    const depoisDaPrimeira = fetchMock.mock.calls.length;

    await vi.advanceTimersByTimeAsync(12000);

    expect(fetchMock.mock.calls.length).toBe(depoisDaPrimeira);
  });
});

describe('mutações', () => {
  const input = { title: 'Novo', sourceType: 'markdown', content: '# Novo' };

  it('criar invalida a listagem em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(doc())));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCreateKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate(input);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: documentsQueryKey(knowledgeBaseId) });
  });

  it('criar propaga o erro e não invalida', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'erro' }, 400)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCreateKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate(input);

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('atualizar invalida a listagem em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(doc())));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useUpdateKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate({ id: documentId, input });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: documentsQueryKey(knowledgeBaseId) });
  });

  it('atualizar propaga o erro', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'erro' }, 400)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useUpdateKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate({ id: documentId, input });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('excluir invalida a listagem em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDeleteKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate(documentId);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: documentsQueryKey(knowledgeBaseId) });
  });

  it('excluir propaga o erro', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'erro' }, 404)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useDeleteKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate(documentId);

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('reindexar invalida a listagem e devolve o documento no estado novo', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(doc({ indexingStatus: 'Pending' }))),
    );
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useReindexKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate(documentId);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.indexingStatus).toBe('Pending');
    expect(invalidate).toHaveBeenCalledWith({ queryKey: documentsQueryKey(knowledgeBaseId) });
  });

  it('reindexar propaga o erro', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'erro' }, 404)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useReindexKnowledgeDocumentMutation(knowledgeBaseId), {
      wrapper: Wrapper,
    });
    result.current.mutate(documentId);

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
