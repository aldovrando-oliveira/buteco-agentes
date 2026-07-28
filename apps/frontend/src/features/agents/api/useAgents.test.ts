import { afterEach, describe, expect, it, vi } from 'vitest';
import { createElement, type PropsWithChildren } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAgentQuery, useAgentsQuery, useCreateAgentMutation } from './useAgents';
import type { Agent } from '../types/agent';

const agent: Agent = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
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

    result.current.mutate({ name: agent.name, instructions: agent.instructions });

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

    result.current.mutate({ name: '', instructions: '' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error as { status?: number })?.status).toBe(400);
    expect(queryClient.getQueryData(['agents', agent.id])).toBeUndefined();
  });
});
