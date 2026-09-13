import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
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
import type { KnowledgeBase, KnowledgeBaseIndexingSummary } from '../types/knowledgeBase';

const knowledgeBase: KnowledgeBase = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
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
