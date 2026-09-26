import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAgentInsightsQuery } from './useAgentInsights';
import { ApiError, getAgentInsights } from './insightsApi';
import { AGENT_ID, TARGET_ID, agentInsightsFixture } from '../test/agentInsightsFixture';
import type { InsightsPeriod } from '../utils/insightsWindow';

vi.mock('./insightsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./insightsApi')>();
  return { ...actual, getAgentInsights: vi.fn() };
});

function wrapper(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

function newClient() {
  // `retry: false` no default do cliente, para que os casos que não são sobre
  // retentativa não esperem por ela. O caso do 404 monta o seu próprio cliente,
  // porque é justamente a política do hook que ele afirma.
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

describe('useAgentInsightsQuery', () => {
  beforeEach(() => {
    vi.mocked(getAgentInsights).mockReset();
    vi.mocked(getAgentInsights).mockResolvedValue(agentInsightsFixture());
  });

  it('faz uma consulta por período', async () => {
    const { result } = renderHook(() => useAgentInsightsQuery(AGENT_ID, '30d'), {
      wrapper: wrapper(newClient()),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getAgentInsights).toHaveBeenCalledTimes(1);
    expect(vi.mocked(getAgentInsights).mock.calls[0][0]).toBe(AGENT_ID);
  });

  it('dois agentes diferentes NÃO compartilham cache', async () => {
    // O eixo que a chave do escopo do sistema não tem. Sem o `id` na chave,
    // abrir a aba de um agente e depois a de outro serviria o agregado do
    // primeiro, com número plausível e sem sintoma.
    const client = newClient();
    const { result, rerender } = renderHook(
      ({ id }: { id: string }) => useAgentInsightsQuery(id, '30d'),
      { wrapper: wrapper(client), initialProps: { id: AGENT_ID } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    rerender({ id: TARGET_ID });

    await waitFor(() => expect(getAgentInsights).toHaveBeenCalledTimes(2));
    expect(vi.mocked(getAgentInsights).mock.calls[1][0]).toBe(TARGET_ID);
  });

  it('renders repetidos sem troca de período NÃO geram consulta nova', async () => {
    const { result, rerender } = renderHook(
      ({ period }: { period: InsightsPeriod }) => useAgentInsightsQuery(AGENT_ID, period),
      { wrapper: wrapper(newClient()), initialProps: { period: '30d' as InsightsPeriod } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    rerender({ period: '30d' });
    rerender({ period: '30d' });
    rerender({ period: '30d' });

    expect(getAgentInsights).toHaveBeenCalledTimes(1);
  });

  it('trocar de período consulta de novo, com a janela do período novo', async () => {
    const { result, rerender } = renderHook(
      ({ period }: { period: InsightsPeriod }) => useAgentInsightsQuery(AGENT_ID, period),
      { wrapper: wrapper(newClient()), initialProps: { period: '30d' as InsightsPeriod } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    rerender({ period: '7d' });
    await waitFor(() => expect(getAgentInsights).toHaveBeenCalledTimes(2));

    const [, from, to] = vi.mocked(getAgentInsights).mock.calls[1];
    const dias = (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000);
    expect(dias).toBe(7);
  });

  it('nova tentativa recalcula a janela no instante da tentativa', async () => {
    // A FALHA AQUI É UM `404`, de propósito. A política de retentativa do hook
    // vence a do cliente de teste, então um erro genérico levaria três
    // tentativas com backoff exponencial antes de o estado de erro aparecer — o
    // caso ficaria lento e passaria a medir o backoff em vez da janela. O `404`
    // falha na primeira, que é exatamente a política que o hook declara.
    vi.mocked(getAgentInsights).mockRejectedValue(new ApiError(404, 'Not Found'));
    const { result } = renderHook(() => useAgentInsightsQuery(AGENT_ID, '7d'), {
      wrapper: wrapper(newClient()),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const primeira = vi.mocked(getAgentInsights).mock.calls[0];
    const tentativasAteFalhar = vi.mocked(getAgentInsights).mock.calls.length;

    vi.mocked(getAgentInsights).mockResolvedValue(agentInsightsFixture());
    await result.current.refetch();
    await waitFor(() =>
      expect(vi.mocked(getAgentInsights).mock.calls.length).toBeGreaterThan(
        tentativasAteFalhar,
      ),
    );

    const segunda = vi.mocked(getAgentInsights).mock.calls[tentativasAteFalhar];
    expect(new Date(segunda[2]).getTime()).toBeGreaterThanOrEqual(
      new Date(primeira[2]).getTime(),
    );
  });

  it('o 404 NÃO é repetido — é resposta, não falha de comunicação', async () => {
    // Cliente SEM `retry: false`, para que a política do hook seja a que decide.
    // Sem a política, o react-query perguntaria três vezes o que já foi
    // respondido, e a aba só mostraria o estado de recusa depois disso.
    const client = new QueryClient();
    vi.mocked(getAgentInsights).mockRejectedValue(new ApiError(404, 'Not Found'));

    const { result } = renderHook(() => useAgentInsightsQuery(AGENT_ID, '30d'), {
      wrapper: wrapper(client),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(getAgentInsights).toHaveBeenCalledTimes(1);
  });
});
