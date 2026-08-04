import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useAgentQuery,
  useAgentsQuery,
  useCreateAgentMutation,
  useReplaceAgentMcpServersMutation,
} from './useAgents';
import { ApiError } from './agentsApi';
import type { Agent } from '../types/agent';

const agent: Agent = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
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

describe('useAgentsQuery', () => {
  it('retorna a lista de agentes em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([agent])));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useAgentsQuery(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([agent]);
  });

  it('expõe erro quando a chamada falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Erro interno' }, 500)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useAgentsQuery(), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('useAgentQuery', () => {
  it('retorna o agente em caso de sucesso', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(agent)));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useAgentQuery(agent.id), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(agent);
  });

  it('expõe erro 404 quando o agente não existe', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })));
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useAgentQuery('inexistente'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as { status?: number })?.status).toBe(404);
  });
});

describe('useCreateAgentMutation', () => {
  it('em sucesso, popula o cache do detalhe e invalida a lista', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(agent, 201)));
    const { Wrapper, queryClient } = createWrapper();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCreateAgentMutation(), { wrapper: Wrapper });

    result.current.mutate({
      name: agent.name,
      instructions: agent.instructions,
      provider: agent.provider!,
      model: agent.model!,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(queryClient.getQueryData(['agents', agent.id])).toEqual(agent);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['agents'] });
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

    const { result } = renderHook(() => useCreateAgentMutation(), { wrapper: Wrapper });

    result.current.mutate({ name: '', instructions: '', provider: '', model: '' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as { status?: number })?.status).toBe(400);
    expect(queryClient.getQueryData(['agents', agent.id])).toBeUndefined();
  });
});

describe('useReplaceAgentMcpServersMutation', () => {
  it('envia o body envelopado ({ mcpServers: [...] }) e, em sucesso, popula o cache do detalhe e invalida a lista', async () => {
    const updated: Agent = {
      ...agent,
      mcpServers: [{ id: 'mcp-1', name: 'Zendesk MCP', allowedTools: ['read'] }],
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(updated));
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper, queryClient } = createWrapper();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useReplaceAgentMcpServersMutation(agent.id), {
      wrapper: Wrapper,
    });

    result.current.mutate([{ mcpServerId: 'mcp-1', allowedTools: ['read'] }]);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/agents/${agent.id}/mcp-servers`),
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ mcpServers: [{ mcpServerId: 'mcp-1', allowedTools: ['read'] }] }),
      }),
    );
    expect(queryClient.getQueryData(['agents', agent.id])).toEqual(updated);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['agents'] });
  });

  it('envia mcpServers: [] quando todos os servidores são desmarcados', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(agent));
    vi.stubGlobal('fetch', fetchMock);
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useReplaceAgentMcpServersMutation(agent.id), {
      wrapper: Wrapper,
    });

    result.current.mutate([]);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(`/agents/${agent.id}/mcp-servers`),
      expect.objectContaining({ body: JSON.stringify({ mcpServers: [] }) }),
    );
  });

  it('em um 502 (falha de handshake), expõe error.problem.title e error.problem.detail identificando o servidor culpado', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(
          {
            title: 'Não foi possível validar as tools do servidor MCP mcp-1.',
            detail: 'Host inalcançável durante o handshake de validação.',
            status: 502,
          },
          502,
        ),
      ),
    );
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useReplaceAgentMcpServersMutation(agent.id), {
      wrapper: Wrapper,
    });

    result.current.mutate([{ mcpServerId: 'mcp-1', allowedTools: ['read'] }]);

    await waitFor(() => expect(result.current.isError).toBe(true));
    const error = result.current.error as ApiError;
    expect(error.status).toBe(502);
    expect(error.problem?.title).toBe('Não foi possível validar as tools do servidor MCP mcp-1.');
    expect(error.problem?.detail).toBe('Host inalcançável durante o handshake de validação.');
  });
});
