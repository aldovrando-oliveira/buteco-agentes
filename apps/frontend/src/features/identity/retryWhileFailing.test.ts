import { describe, expect, it } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { ApiError } from './api/identityApi';
import { INTERVALO_SEM_RETRY_AFTER_MS, retryWhileFailing } from './retryWhileFailing';

// A metade determinística do guarda pareado (convenção 20): a função resolvida contra a
// consulta REAL do cache, e não contra um objeto montado à mão. A comportamental, com timers
// falsos, está em `pages/IdentityPage.test.tsx`.

async function queryAfter(queryFn: () => Promise<unknown>) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await client.fetchQuery({ queryKey: ['identity'], queryFn }).catch(() => undefined);
  return client.getQueryCache().find({ queryKey: ['identity'] })!;
}

describe('retryWhileFailing', () => {
  it('usa o Retry-After do 503', async () => {
    const query = await queryAfter(() => Promise.reject(new ApiError(503, 'x', undefined, 5)));
    expect(retryWhileFailing(query)).toBe(5_000);
  });

  it('o Retry-After fica entre 1 e 30 s', async () => {
    expect(retryWhileFailing(await queryAfter(() => Promise.reject(new ApiError(503, 'x', undefined, 600))))).toBe(30_000);
    expect(retryWhileFailing(await queryAfter(() => Promise.reject(new ApiError(503, 'x', undefined, 0))))).toBe(1_000);
  });

  it('503 sem Retry-After e falha de rede: 30 s', async () => {
    expect(retryWhileFailing(await queryAfter(() => Promise.reject(new ApiError(503, 'x'))))).toBe(INTERVALO_SEM_RETRY_AFTER_MS);
    expect(retryWhileFailing(await queryAfter(() => Promise.reject(new TypeError('Failed to fetch'))))).toBe(30_000);
  });

  it('sem erro, sem intervalo', async () => {
    const query = await queryAfter(() => Promise.resolve({ ok: true }));
    expect(retryWhileFailing(query)).toBe(false);
  });
});
