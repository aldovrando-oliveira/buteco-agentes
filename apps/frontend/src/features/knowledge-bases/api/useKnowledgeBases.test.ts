import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, type Query } from '@tanstack/react-query';
import {
  useActivateKnowledgeBaseMutation,
  useCreateKnowledgeBaseMutation,
  useDeactivateKnowledgeBaseMutation,
  useKnowledgeBaseIndexingSummaryQuery,
  useKnowledgeBaseQuery,
  useKnowledgeBasesQuery,
  indexingSummaryQueryKey,
  useUpdateKnowledgeBaseMutation,
} from './useKnowledgeBases';
import type {
  KnowledgeBase,
  KnowledgeBaseIndexingSummary,
  KnowledgeBaseSyncState,
} from '../types/knowledgeBase';
import { SYNC_POLL_INTERVAL_MS, SYNC_WAIT_LIMIT_MS, type SyncRequest } from '../utils/syncState';

const knowledgeBase: KnowledgeBase = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação, prazos e faixas de desconto.',
  isActive: true,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  contentMode: 'Manual',
  syncSource: null,
  syncState: null,
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    queryClient,
    Wrapper: ({ children }: PropsWithChildren) =>
      createElement(QueryClientProvider, { client: queryClient }, children),
  };
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('useKnowledgeBasesQuery', () => {
  it('retorna a lista de bases em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([knowledgeBase])));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBasesQuery(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([knowledgeBase]);
  });

  // O par vazio de todo caso "com item" (convenção 5): catálogo sem nenhuma
  // base é sucesso com lista vazia, nunca erro.
  it('retorna lista vazia quando não há nenhuma base cadastrada', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBasesQuery(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  it('expõe erro quando a chamada falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Erro interno' }, 500)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBasesQuery(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('não busca quando desabilitada', () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();

    renderHook(() => useKnowledgeBasesQuery({ enabled: false }), { wrapper: Wrapper });

    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('useKnowledgeBaseQuery', () => {
  it('retorna a base consultada por id', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(knowledgeBase)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBaseQuery(knowledgeBase.id), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(knowledgeBase);
  });

  it('expõe erro quando a base não existe', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse({ title: 'Não encontrado' }, 404)),
    );
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBaseQuery('inexistente'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('mutações do catálogo de bases', () => {
  it('criação escreve o item no cache e invalida a coleção', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(knowledgeBase, 201)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCreateKnowledgeBaseMutation(), { wrapper: Wrapper });
    result.current.mutate({ name: knowledgeBase.name, description: knowledgeBase.description });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['knowledge-bases', knowledgeBase.id])).toEqual(knowledgeBase);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['knowledge-bases'] });
  });

  it('edição escreve o item no cache e invalida a coleção', async () => {
    const renamed = { ...knowledgeBase, name: 'Políticas de Cobrança 2026' };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(renamed)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useUpdateKnowledgeBaseMutation(), { wrapper: Wrapper });
    result.current.mutate({
      id: knowledgeBase.id,
      input: { name: renamed.name, description: renamed.description },
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['knowledge-bases', knowledgeBase.id])).toEqual(renamed);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['knowledge-bases'] });
  });

  it('ativação escreve o item no cache e invalida a coleção', async () => {
    const activated = { ...knowledgeBase, isActive: true };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(activated)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useActivateKnowledgeBaseMutation(), { wrapper: Wrapper });
    result.current.mutate(knowledgeBase.id);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['knowledge-bases', knowledgeBase.id])).toEqual(activated);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['knowledge-bases'] });
  });

  it('desativação escreve o item no cache e invalida a coleção', async () => {
    const deactivated = { ...knowledgeBase, isActive: false };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(deactivated)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDeactivateKnowledgeBaseMutation(), { wrapper: Wrapper });
    result.current.mutate(knowledgeBase.id);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['knowledge-bases', knowledgeBase.id])).toEqual(deactivated);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['knowledge-bases'] });
  });

  it('expõe erro de validação da criação sem escrever no cache', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse(
            { title: 'validação', status: 400, errors: { description: ['obrigatória'] } },
            400,
          ),
        ),
    );
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useCreateKnowledgeBaseMutation(), { wrapper: Wrapper });
    result.current.mutate({ name: 'Cardápio', description: '' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(queryClient.getQueryData(['knowledge-bases', knowledgeBase.id])).toBeUndefined();
  });
});

describe('useKnowledgeBaseIndexingSummaryQuery', () => {
  const summary: KnowledgeBaseIndexingSummary = {
    knowledgeBaseId: knowledgeBase.id,
    documentCount: 5,
    indexedCount: 3,
    failedCount: 1,
  };

  it('retorna o resumo de indexação em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([summary])));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBaseIndexingSummaryQuery(), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([summary]);
  });

  // O par sem item: catálogo sem base nenhuma responde 200 com lista vazia,
  // nunca 404 (convenção 5).
  it('retorna lista vazia quando não existe base cadastrada', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBaseIndexingSummaryQuery(), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([]);
  });

  // AFIRMA O MECANISMO, não confia nele (design.md, D8).
  //
  // A chave do resumo começa com `knowledge-bases` de propósito, para que a
  // invalidação por prefixo das mutações de base o alcance sem uma linha a mais.
  // Isso é fácil de quebrar sem perceber — basta alguém "organizar" a chave como
  // `['indexing-summary']` —, e nenhum outro teste notaria.
  it('é alcançado pela invalidação por prefixo das mutações de base', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([summary])));
    const { queryClient, Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBaseIndexingSummaryQuery(), {
      wrapper: Wrapper,
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // O mesmo prefixo que cacheUpdatedKnowledgeBase usa após criar, editar,
    // ativar e desativar uma base.
    await queryClient.invalidateQueries({ queryKey: ['knowledge-bases'] });

    expect(queryClient.getQueryState(indexingSummaryQueryKey)?.isInvalidated ?? false).toBe(true);
  });
});

// ACOMPANHAMENTO DO "SINCRONIZAR AGORA" (frontend-detalhe-base-sincronizada, D5).
// Guarda pareado da convenção 20: a asserção determinística resolve a opção
// `refetchInterval` REAL que o hook passou, contra a query REAL do cache; a
// comportamental, com timers falsos, prova que a requisição se repete (e para).
//
// ATENÇÃO, a armadilha da convenção 20: o react-query rastreia as props LIDAS
// (`notifyOnChangeProps: 'tracked'`). Toda primeira espera abaixo toca `.data`,
// para que a mudança só em `data` provoque re-render.
describe('useKnowledgeBaseQuery — acompanhamento da sincronização', () => {
  const T1 = '2026-10-04T10:00:00Z';
  const T2 = '2026-10-04T10:00:30Z';

  function synced(lastFinishedAt: string | null): KnowledgeBase {
    const syncState: KnowledgeBaseSyncState = {
      lastCompletedAt: lastFinishedAt,
      lastFinishedAt,
      failingSince: null,
      lastError: null,
      ignoredFiles: lastFinishedAt ? [] : null,
    };
    return {
      ...knowledgeBase,
      contentMode: 'Synced',
      syncSource: {
        provider: 'google-drive',
        folderId: 'f',
        folderName: 'F',
        folderUrl: 'https://x',
      },
      syncState,
    };
  }

  function resolveRefetchInterval(queryClient: QueryClient): number | false | undefined {
    const query = queryClient
      .getQueryCache()
      .find({ queryKey: ['knowledge-bases', knowledgeBase.id] });
    const option = query?.observers[0]?.options.refetchInterval;

    return typeof option === 'function'
      ? (option as (q: Query) => number | false | undefined)(query as unknown as Query)
      : option;
  }

  function fetchReturning(get: () => KnowledgeBase) {
    return vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(get())));
  }

  it('sem pedido, a consulta é a de hoje: não se repete', async () => {
    vi.stubGlobal(
      'fetch',
      fetchReturning(() => synced(T1)),
    );
    const { queryClient, Wrapper } = createWrapper();

    const { result } = renderHook(() => useKnowledgeBaseQuery(knowledgeBase.id), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.data?.syncState?.lastFinishedAt).toBe(T1));
    expect(resolveRefetchInterval(queryClient)).toBeFalsy();
  });

  it('determinística: 4 s enquanto lastFinishedAt é a linha de base, false quando muda', async () => {
    let current = synced(T1);
    vi.stubGlobal(
      'fetch',
      fetchReturning(() => current),
    );
    const { queryClient, Wrapper } = createWrapper();
    const request: SyncRequest = { baseline: T1, requestedAt: Date.now() };

    const { result } = renderHook(
      () => useKnowledgeBaseQuery(knowledgeBase.id, { syncRequest: request }),
      { wrapper: Wrapper },
    );

    await waitFor(() => expect(result.current.data?.syncState?.lastFinishedAt).toBe(T1));
    expect(resolveRefetchInterval(queryClient)).toBe(SYNC_POLL_INTERVAL_MS);

    current = synced(T2);
    await result.current.refetch();

    await waitFor(() => expect(result.current.data?.syncState?.lastFinishedAt).toBe(T2));
    expect(resolveRefetchInterval(queryClient)).toBe(false);
  });

  it('comportamental: repete enquanto espera e para quando lastFinishedAt muda', async () => {
    vi.useFakeTimers();
    let current = synced(T1);
    const fetchMock = fetchReturning(() => current);
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();
    const request: SyncRequest = { baseline: T1, requestedAt: Date.now() };

    const { result } = renderHook(
      () => useKnowledgeBaseQuery(knowledgeBase.id, { syncRequest: request }),
      { wrapper: Wrapper },
    );

    await vi.advanceTimersByTimeAsync(0);
    expect(result.current.data?.syncState?.lastFinishedAt).toBe(T1);
    const first = fetchMock.mock.calls.length;

    await vi.advanceTimersByTimeAsync(4500);
    expect(fetchMock.mock.calls.length).toBeGreaterThan(first);

    current = synced(T2);
    await vi.advanceTimersByTimeAsync(4500);
    expect(result.current.data?.syncState?.lastFinishedAt).toBe(T2);
    const afterChange = fetchMock.mock.calls.length;

    await vi.advanceTimersByTimeAsync(20000);
    expect(fetchMock.mock.calls.length).toBe(afterChange);
  });

  it('comportamental: para no limite de 5 minutos sem mudança', async () => {
    vi.useFakeTimers();
    const fetchMock = fetchReturning(() => synced(T1));
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();
    const request: SyncRequest = { baseline: T1, requestedAt: Date.now() };

    const { result } = renderHook(
      () => useKnowledgeBaseQuery(knowledgeBase.id, { syncRequest: request }),
      { wrapper: Wrapper },
    );

    await vi.advanceTimersByTimeAsync(0);
    expect(result.current.data?.syncState?.lastFinishedAt).toBe(T1);

    await vi.advanceTimersByTimeAsync(SYNC_WAIT_LIMIT_MS + 4500);
    const atLimit = fetchMock.mock.calls.length;
    // 5 min a cada 4 s: perto de 75 consultas, e não mais.
    expect(atLimit).toBeGreaterThan(60);
    expect(atLimit).toBeLessThanOrEqual(78);

    await vi.advanceTimersByTimeAsync(60000);
    expect(fetchMock.mock.calls.length).toBe(atLimit);
  });
});
