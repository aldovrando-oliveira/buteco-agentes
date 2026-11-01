import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MutationObserver, QueryObserver } from '@tanstack/react-query';
import { createAppQueryClient } from './queryClient';
import { queryRetryDelay } from '../auth/transient';

class FakeApiError extends Error {
  readonly status: number;
  readonly retryAfter?: number;

  constructor(status: number, retryAfter?: number) {
    super(`Erro ${status}`);
    this.status = status;
    this.retryAfter = retryAfter;
  }
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('queryClient', () => {
  it('consulta com 503 repete esperando o Retry-After, e mostra o dado', async () => {
    const client = createAppQueryClient();
    const queryFn = vi
      .fn()
      .mockRejectedValueOnce(new FakeApiError(503, 2))
      .mockResolvedValueOnce(['agente']);
    const observer = new QueryObserver(client, { queryKey: ['x'], queryFn });
    const unsubscribe = observer.subscribe(() => {});

    await vi.advanceTimersByTimeAsync(0);
    expect(queryFn).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1_999);
    expect(queryFn).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(queryFn).toHaveBeenCalledTimes(2);
    expect(observer.getCurrentResult().data).toEqual(['agente']);
    unsubscribe();
  });

  it('consulta com 503 desiste depois de 3 novas tentativas', async () => {
    const client = createAppQueryClient();
    const queryFn = vi.fn().mockRejectedValue(new FakeApiError(503, 1));
    const observer = new QueryObserver(client, { queryKey: ['y'], queryFn });
    const unsubscribe = observer.subscribe(() => {});

    await vi.advanceTimersByTimeAsync(10_000);

    expect(queryFn).toHaveBeenCalledTimes(4);
    expect(observer.getCurrentResult().status).toBe('error');
    unsubscribe();
  });

  it('mutação com 503 não repete', async () => {
    const client = createAppQueryClient();
    const mutationFn = vi.fn().mockRejectedValue(new FakeApiError(503, 1));
    const observer = new MutationObserver(client, { mutationFn });

    const result = observer.mutate(undefined).catch((error: unknown) => error);
    await vi.advanceTimersByTimeAsync(60_000);

    expect(mutationFn).toHaveBeenCalledTimes(1);
    expect(await result).toBeInstanceOf(FakeApiError);
  });

  it('consulta com retry próprio continua com ele', async () => {
    const client = createAppQueryClient();
    const queryFn = vi.fn().mockRejectedValue(new FakeApiError(503, 1));
    const observer = new QueryObserver(client, { queryKey: ['z'], queryFn, retry: false });
    const unsubscribe = observer.subscribe(() => {});

    await vi.advanceTimersByTimeAsync(10_000);

    expect(queryFn).toHaveBeenCalledTimes(1);
    unsubscribe();
  });
});

describe('espera entre tentativas', () => {
  it.each([
    [2, 2_000],
    [120, 30_000],
    [0, 1_000],
    [undefined, 1_000],
  ])('Retry-After %s no 503 espera %i ms', (retryAfter, ms) => {
    expect(queryRetryDelay(0, new FakeApiError(503, retryAfter))).toBe(ms);
  });

  it('os outros erros mantêm a espera exponencial do padrão', () => {
    expect([0, 1, 2, 5].map((n) => queryRetryDelay(n, new FakeApiError(500)))).toEqual([
      1_000, 2_000, 4_000, 30_000,
    ]);
  });
});
