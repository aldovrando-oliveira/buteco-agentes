import { vi } from 'vitest';
import type { AuthRuntime } from '../auth/runtime';
import { jsonResponse, tokenResponse } from './authFakes';

/**
 * `/connect/token` falso com rotação ESTRITA, sem a tolerância do emissor: o refresh
 * resgatado reapresentado responde `invalid_grant` e revoga a cadeia (H3 sem o H2). É
 * o pior caso, e o painel tem de sobreviver a ele sem depender da tolerância.
 *
 * `hold` segura a próxima resposta até o teste soltar: é o pedido "em voo".
 */
export function strictTokenEndpoint(firstRefresh = 1) {
  let generation = firstRefresh;
  const valid = new Set([`refresh-${firstRefresh}`]);
  const redeemed = new Set<string>();
  const holds: Array<Promise<void>> = [];
  const requests: URLSearchParams[] = [];

  const fetch = vi.fn<AuthRuntime['fetch']>(async (input, init) => {
    if (input !== '/connect/token') throw new Error(`rota inesperada: ${input}`);
    const form = new URLSearchParams(String(init?.body));
    requests.push(form);
    const hold = holds.shift();
    if (hold) await hold;

    const presented = form.get('refresh_token') ?? '';
    if (valid.has(presented)) {
      valid.delete(presented);
      redeemed.add(presented);
      generation += 1;
      valid.add(`refresh-${generation}`);
      return tokenResponse(generation);
    }
    if (redeemed.has(presented)) valid.clear();
    return jsonResponse({ error: 'invalid_grant' }, 400);
  });

  return {
    fetch,
    requests,
    hold(promise: Promise<void>) {
      holds.push(promise);
    },
    isValid: (refresh: string) => valid.has(refresh),
  };
}
