import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useConnectorFoldersQuery,
  useConnectorProvidersQuery,
  useRequestKnowledgeBaseSyncMutation,
} from './useConnectors';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

// QueryClient SEM `retry: false` nos defaults, como o do app (app/queryClient.ts):
// é o hook que precisa desligar as novas tentativas, não o teste.
function createWrapper() {
  const queryClient = new QueryClient();
  return ({ children }: PropsWithChildren) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe('useConnectorProvidersQuery', () => {
  it('com enabled false, não faz nenhuma requisição', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal('fetch', fetchMock);

    const { result } = renderHook(() => useConnectorProvidersQuery({ enabled: false }), {
      wrapper: createWrapper(),
    });

    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(result.current.fetchStatus).toBe('idle');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('devolve os provedores', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const providers = [{ key: 'google-drive', accountEmail: 'a@b.iam.gserviceaccount.com' }];
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(providers)));

    const { result } = renderHook(() => useConnectorProvidersQuery({ enabled: true }), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(providers);
  });

  it('não repete a requisição depois de um erro', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ code: 'provider-unavailable' }, 503));
    vi.stubGlobal('fetch', fetchMock);

    const { result } = renderHook(() => useConnectorProvidersQuery({ enabled: true }), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true), { timeout: 2000 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

describe('useConnectorFoldersQuery', () => {
  it('consulta o nível de cima sem parentId e uma pasta com parentId', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([])));
    vi.stubGlobal('fetch', fetchMock);
    const wrapper = createWrapper();

    const top = renderHook(
      () => useConnectorFoldersQuery('google-drive', null, { enabled: true }),
      {
        wrapper,
      },
    );
    await waitFor(() => expect(top.result.current.isSuccess).toBe(true));
    const inner = renderHook(
      () => useConnectorFoldersQuery('google-drive', 'pasta-1', { enabled: true }),
      { wrapper },
    );
    await waitFor(() => expect(inner.result.current.isSuccess).toBe(true));

    expect(fetchMock.mock.calls.map((call) => call[0])).toEqual([
      'http://localhost:5037/connectors/providers/google-drive/folders',
      'http://localhost:5037/connectors/providers/google-drive/folders?parentId=pasta-1',
    ]);
  });

  it('sem provedor ou com enabled false, não faz nenhuma requisição', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal('fetch', fetchMock);
    const wrapper = createWrapper();

    renderHook(() => useConnectorFoldersQuery(null, null, { enabled: true }), { wrapper });
    renderHook(() => useConnectorFoldersQuery('google-drive', null, { enabled: false }), {
      wrapper,
    });

    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('useRequestKnowledgeBaseSyncMutation', () => {
  it('pede a sincronização da base e não repete depois de um erro', async () => {
    vi.stubEnv('VITE_CONNECTORS_BASE_URL', 'http://localhost:5037');
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ status: 503, code: 'sync-api-unavailable' }, 503));
    vi.stubGlobal('fetch', fetchMock);

    const { result } = renderHook(() => useRequestKnowledgeBaseSyncMutation(), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await result.current.mutateAsync('b-1').catch(() => undefined);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe(
      'http://localhost:5037/connectors/knowledge-bases/b-1/sync',
    );
    expect((fetchMock.mock.calls[0][1] as RequestInit).method).toBe('POST');
  });
});
