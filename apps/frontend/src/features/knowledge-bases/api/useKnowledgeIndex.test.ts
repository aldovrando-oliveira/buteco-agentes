import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, type Query } from '@tanstack/react-query';
import { indexDiagnosticsQueryKey, useKnowledgeIndexDiagnosticsQuery } from './useKnowledgeIndex';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

const qwen: KnowledgeIndexProvenance = {
  provider: 'openai',
  model: 'qwen-qwen3-embedding-8b',
  dimensions: 4096,
  fragmentCount: 3,
};

function doc(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: 'd1',
    knowledgeBaseId: 'k1',
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

const indexando = doc({ indexingStatus: 'Indexing', indexedAt: null, fragmentCount: 0 });

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

// Uma Response NOVA por chamada: o corpo só pode ser lido uma vez, e os testes de
// acompanhamento fazem várias.
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
// do cache — não uma reimplementação da condição dentro do teste. É a metade
// determinística do guarda pareado (convenção 20): reprova em 100% das
// execuções, enquanto a comportamental depende de agendamento.
function resolveRefetchInterval(queryClient: QueryClient): number | false | undefined {
  const query = queryClient.getQueryCache().find({ queryKey: indexDiagnosticsQueryKey });
  const option = query?.observers[0]?.options.refetchInterval;

  return typeof option === 'function'
    ? (option as (q: Query) => number | false | undefined)(query as unknown as Query)
    : option;
}

// ATENÇÃO: o react-query usa `notifyOnChangeProps: 'tracked'` e devolve um PROXY
// que registra quais props foram LIDAS. Se a primeira espera tocar só
// `isSuccess`, `data` nunca entra no conjunto rastreado e uma mudança posterior
// só em `data` não provoca re-render — `result.current.data` fica preso no
// primeiro valor. Por isso as esperas abaixo tocam `data` desde a primeira.
afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('useKnowledgeIndexDiagnosticsQuery', () => {
  it('devolve a proveniência quando a aba está ativa', async () => {
    vi.stubGlobal('fetch', fetchAlways([qwen]));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(
      () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [doc()] }),
      { wrapper: Wrapper },
    );

    await waitFor(() => expect(result.current.data).toEqual([qwen]));
  });

  it('devolve lista vazia, sem erro, quando o índice está vazio', async () => {
    vi.stubGlobal('fetch', fetchAlways([]));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(
      () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [] }),
      { wrapper: Wrapper },
    );

    await waitFor(() => expect(result.current.data).toEqual([]));
    expect(result.current.isError).toBe(false);
  });

  // O guarda do `enabled`: com a aba de documentos ativa, NENHUMA requisição sai.
  it('não requisita nada com a aba inativa', async () => {
    const fetchMock = fetchAlways([qwen]);
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();

    const { result } = renderHook(
      () => useKnowledgeIndexDiagnosticsQuery({ enabled: false, documents: [indexando] }),
      { wrapper: Wrapper },
    );

    await waitFor(() => expect(result.current.fetchStatus).toBe('idle'));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(result.current.data).toBeUndefined();
  });

  it('propaga o erro quando a proveniência não pode ser lida', async () => {
    vi.stubGlobal('fetch', fetchAlways({ title: 'erro' }, 500));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(
      () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [doc()] }),
      { wrapper: Wrapper },
    );

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.data).toBeUndefined();
  });

  describe('acompanhamento da única transição que muda a proveniência', () => {
    it('agenda recarga com índice vazio e documento não terminal', async () => {
      vi.stubGlobal('fetch', fetchAlways([]));
      const { Wrapper, queryClient } = createWrapper();

      const { result } = renderHook(
        () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [indexando] }),
        { wrapper: Wrapper },
      );

      await waitFor(() => expect(result.current.data).toEqual([]));
      expect(resolveRefetchInterval(queryClient)).toBe(4000);
    });

    // A metade que evita pagar a varredura de heap para sempre: com a
    // proveniência já gravada, o valor não muda mais.
    it('não agenda recarga com o índice já povoado, mesmo indexando', async () => {
      vi.stubGlobal('fetch', fetchAlways([qwen]));
      const { Wrapper, queryClient } = createWrapper();

      const { result } = renderHook(
        () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [indexando] }),
        { wrapper: Wrapper },
      );

      await waitFor(() => expect(result.current.data).toEqual([qwen]));
      expect(resolveRefetchInterval(queryClient)).toBe(false);
    });

    it('não agenda recarga com índice vazio e todos os documentos terminais', async () => {
      vi.stubGlobal('fetch', fetchAlways([]));
      const { Wrapper, queryClient } = createWrapper();

      const { result } = renderHook(
        () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [doc()] }),
        { wrapper: Wrapper },
      );

      await waitFor(() => expect(result.current.data).toEqual([]));
      expect(resolveRefetchInterval(queryClient)).toBe(false);
    });

    it('deixa de agendar quando a proveniência aparece', async () => {
      let indexado = false;
      const fetchMock = vi
        .fn()
        .mockImplementation(() => Promise.resolve(jsonResponse(indexado ? [qwen] : [])));
      vi.stubGlobal('fetch', fetchMock);
      const { Wrapper, queryClient } = createWrapper();

      const { result } = renderHook(
        () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [indexando] }),
        { wrapper: Wrapper },
      );

      await waitFor(() => expect(result.current.data).toEqual([]));
      expect(resolveRefetchInterval(queryClient)).toBe(4000);

      indexado = true;
      await result.current.refetch();

      await waitFor(() => expect(result.current.data).toEqual([qwen]));
      expect(resolveRefetchInterval(queryClient)).toBe(false);
    });

    // A metade COMPORTAMENTAL do par: prova que a requisição se repete de fato.
    // Sozinha ela depende de agendamento; a determinística acima prova o
    // artefato que a produz.
    it('repete a requisição enquanto a condição vale', async () => {
      vi.useFakeTimers();
      const fetchMock = fetchAlways([]);
      vi.stubGlobal('fetch', fetchMock);
      const { Wrapper } = createWrapper();

      const { result } = renderHook(
        () => useKnowledgeIndexDiagnosticsQuery({ enabled: true, documents: [indexando] }),
        { wrapper: Wrapper },
      );

      await vi.waitFor(() => expect(result.current.data).toEqual([]));
      const depoisDaPrimeira = fetchMock.mock.calls.length;

      await vi.advanceTimersByTimeAsync(4000);

      await vi.waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(depoisDaPrimeira));
    });
  });
});
