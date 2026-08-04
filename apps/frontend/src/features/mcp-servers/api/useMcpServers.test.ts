import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useActivateMcpServerMutation,
  useCreateMcpServerMutation,
  useDeactivateMcpServerMutation,
  useMcpServerQuery,
  useMcpServersQuery,
  useMcpServerToolsQuery,
  useTestSavedMcpServerConnectionMutation,
  useTestUnsavedMcpServerConnectionMutation,
  useUpdateMcpServerMutation,
} from './useMcpServers';
import type { McpServer } from '../types/mcpServer';

const mcpServer: McpServer = {
  id: '77777777-7777-7777-7777-777777777777',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'BearerToken',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
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

describe('useMcpServersQuery', () => {
  it('retorna a lista de servidores MCP em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([mcpServer])));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useMcpServersQuery(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([mcpServer]);
  });

  it('expõe erro quando a chamada falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Erro interno' }, 500)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useMcpServersQuery(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('useMcpServerQuery', () => {
  it('retorna o servidor MCP em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(mcpServer)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useMcpServerQuery(mcpServer.id), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mcpServer);
  });

  it('expõe erro 404 quando o servidor MCP não existe', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useMcpServerQuery('inexistente'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as { status?: number })?.status).toBe(404);
  });
});

describe('useCreateMcpServerMutation', () => {
  it('em sucesso, popula o cache do detalhe e invalida a lista', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(mcpServer, 201)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCreateMcpServerMutation(), { wrapper: Wrapper });

    result.current.mutate({
      name: mcpServer.name,
      description: mcpServer.description,
      url: mcpServer.url,
      authType: mcpServer.authType,
      credential: 'token-secreto',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['mcp-servers', mcpServer.id])).toEqual(mcpServer);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['mcp-servers'] });
  });

  it('em erro, expõe o ApiError sem popular nenhum cache', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse(
            { title: 'Validação falhou', status: 400, errors: { name: ['obrigatório'] } },
            400,
          ),
        ),
    );
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useCreateMcpServerMutation(), { wrapper: Wrapper });

    result.current.mutate({ name: '', description: '', url: '', authType: 'None' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as { status?: number })?.status).toBe(400);
    expect(queryClient.getQueryData(['mcp-servers', mcpServer.id])).toBeUndefined();
  });
});

describe('useUpdateMcpServerMutation', () => {
  it('em sucesso, popula o cache do detalhe e invalida a lista', async () => {
    const updated = { ...mcpServer, name: 'Zendesk MCP (renomeado)' };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(updated)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useUpdateMcpServerMutation(), { wrapper: Wrapper });

    result.current.mutate({
      id: mcpServer.id,
      input: {
        name: updated.name,
        description: updated.description,
        url: updated.url,
        authType: updated.authType,
      },
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['mcp-servers', mcpServer.id])).toEqual(updated);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['mcp-servers'] });
  });
});

describe('useActivateMcpServerMutation', () => {
  it('em sucesso, popula o cache do detalhe com o servidor ativado', async () => {
    const activated = { ...mcpServer, isActive: true };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(activated)));
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useActivateMcpServerMutation(), { wrapper: Wrapper });

    result.current.mutate(mcpServer.id);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['mcp-servers', mcpServer.id])).toEqual(activated);
  });
});

describe('useDeactivateMcpServerMutation', () => {
  it('em sucesso, popula o cache do detalhe com o servidor desativado', async () => {
    const deactivated = { ...mcpServer, isActive: false };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(deactivated)));
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useDeactivateMcpServerMutation(), { wrapper: Wrapper });

    result.current.mutate(mcpServer.id);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['mcp-servers', mcpServer.id])).toEqual(deactivated);
  });
});

describe('useTestUnsavedMcpServerConnectionMutation', () => {
  it('em sucesso, retorna o resultado do teste sem tocar em nenhum cache', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(jsonResponse({ success: true, failureReason: null, message: null })),
    );
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useTestUnsavedMcpServerConnectionMutation(), {
      wrapper: Wrapper,
    });

    result.current.mutate({ url: mcpServer.url, authType: 'None' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ success: true, failureReason: null, message: null });
    expect(queryClient.getQueryData(['mcp-servers'])).toBeUndefined();
  });

  it('em falha de conectividade, retorna o resultado indicando falha com o motivo', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({
          success: false,
          failureReason: 'HostUnreachable',
          message: 'Não foi possível conectar ao host informado.',
        }),
      ),
    );
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useTestUnsavedMcpServerConnectionMutation(), {
      wrapper: Wrapper,
    });

    result.current.mutate({ url: mcpServer.url, authType: 'None' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.success).toBe(false);
    expect(result.current.data?.message).toBe('Não foi possível conectar ao host informado.');
  });
});

describe('useTestSavedMcpServerConnectionMutation', () => {
  it('em sucesso, retorna o resultado do teste sem tocar em nenhum cache', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(jsonResponse({ success: true, failureReason: null, message: null })),
    );
    const { Wrapper, queryClient } = createWrapper();

    const { result } = renderHook(() => useTestSavedMcpServerConnectionMutation(), {
      wrapper: Wrapper,
    });

    result.current.mutate(mcpServer.id);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ success: true, failureReason: null, message: null });
    expect(queryClient.getQueryData(['mcp-servers', mcpServer.id])).toBeUndefined();
  });

  it('expõe erro 404 quando o servidor MCP não existe', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useTestSavedMcpServerConnectionMutation(), {
      wrapper: Wrapper,
    });

    result.current.mutate('inexistente');

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as { status?: number })?.status).toBe(404);
  });
});

describe('useMcpServerToolsQuery', () => {
  it('não dispara a chamada quando enabled é false', () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        success: true,
        tools: [{ name: 'read', description: 'Lê dados' }],
        failureReason: null,
        message: null,
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useMcpServerToolsQuery(mcpServer.id, { enabled: false }), {
      wrapper: Wrapper,
    });

    expect(result.current.fetchStatus).toBe('idle');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('dispara a chamada e retorna as tools quando enabled é true', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({
          success: true,
          tools: [{ name: 'read', description: 'Lê dados' }],
          failureReason: null,
          message: null,
        }),
      ),
    );
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useMcpServerToolsQuery(mcpServer.id, { enabled: true }), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({
      success: true,
      tools: [{ name: 'read', description: 'Lê dados' }],
      failureReason: null,
      message: null,
    });
  });

  it('expõe success: false como dado normal, não como isError, quando a descoberta falha', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({
          success: false,
          tools: null,
          failureReason: 'HostUnreachable',
          message: 'Não foi possível conectar ao host informado.',
        }),
      ),
    );
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useMcpServerToolsQuery(mcpServer.id, { enabled: true }), {
      wrapper: Wrapper,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.isError).toBe(false);
    expect(result.current.data?.success).toBe(false);
    expect(result.current.data?.message).toBe('Não foi possível conectar ao host informado.');
  });
});
