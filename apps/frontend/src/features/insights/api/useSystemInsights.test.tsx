import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useSystemInsightsQuery } from './useSystemInsights';
import { getSystemInsights } from './insightsApi';
import { systemInsightsFixture } from '../test/systemInsightsFixture';
import type { InsightsPeriod } from '../utils/insightsWindow';

vi.mock('./insightsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./insightsApi')>();
  return { ...actual, getSystemInsights: vi.fn() };
});

function wrapper(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

function newClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

describe('useSystemInsightsQuery', () => {
  beforeEach(() => {
    vi.mocked(getSystemInsights).mockReset();
    vi.mocked(getSystemInsights).mockResolvedValue(systemInsightsFixture());
  });

  it('faz uma consulta por período', async () => {
    const { result } = renderHook(() => useSystemInsightsQuery('30d'), {
      wrapper: wrapper(newClient()),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getSystemInsights).toHaveBeenCalledTimes(1);
  });

  it('renders repetidos sem troca de período NÃO geram consulta nova', async () => {
    // É o modo de falha que a chave fixa existe para impedir: com a janela na
    // chave, cada render traria um instante novo, e a página ficaria presa em
    // carregamento para sempre.
    const { result, rerender } = renderHook(
      ({ period }: { period: InsightsPeriod }) => useSystemInsightsQuery(period),
      { wrapper: wrapper(newClient()), initialProps: { period: '30d' as InsightsPeriod } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    rerender({ period: '30d' });
    rerender({ period: '30d' });
    rerender({ period: '30d' });

    expect(getSystemInsights).toHaveBeenCalledTimes(1);
  });

  it('trocar de período consulta de novo', async () => {
    const { result, rerender } = renderHook(
      ({ period }: { period: InsightsPeriod }) => useSystemInsightsQuery(period),
      { wrapper: wrapper(newClient()), initialProps: { period: '30d' as InsightsPeriod } },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    rerender({ period: '7d' });
    await waitFor(() => expect(getSystemInsights).toHaveBeenCalledTimes(2));

    // A janela do segundo chamado é a de 7 dias, não a de 30.
    const [from, to] = vi.mocked(getSystemInsights).mock.calls[1];
    const dias = (new Date(to).getTime() - new Date(from).getTime()) / (24 * 60 * 60 * 1000);
    expect(dias).toBe(7);
  });

  it('nova tentativa recalcula a janela no instante da tentativa', async () => {
    vi.mocked(getSystemInsights).mockRejectedValueOnce(new Error('falhou'));
    const { result } = renderHook(() => useSystemInsightsQuery('7d'), {
      wrapper: wrapper(newClient()),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const primeiraJanela = vi.mocked(getSystemInsights).mock.calls[0];

    vi.mocked(getSystemInsights).mockResolvedValue(systemInsightsFixture());
    await result.current.refetch();
    await waitFor(() => expect(getSystemInsights).toHaveBeenCalledTimes(2));

    const segundaJanela = vi.mocked(getSystemInsights).mock.calls[1];
    // O `to` da segunda é >= o da primeira: a janela nasceu no `queryFn`, no
    // instante da nova tentativa, e não foi congelada no primeiro render.
    expect(new Date(segundaJanela[1]).getTime()).toBeGreaterThanOrEqual(
      new Date(primeiraJanela[1]).getTime(),
    );
  });
});
