import { describe, expect, it, vi } from 'vitest';
import { createRenewal } from './renewal';
import { createSessionFetch } from './sessionFetch';
import { createSessionStore } from './sessionStore';
import { fakeRuntime, jsonResponse, session, tokenResponse } from '../test/authFakes';
import type { AuthRuntime } from './runtime';

const API = 'http://localhost:5017';

/**
 * `fetch` falso com o `/connect/token` e uma rota de API: `api` diz, por token, o status
 * que a rota responde.
 */
function setup(api: (token: string | undefined) => number, token: () => Response = () => tokenResponse(2)) {
  const calls: Array<{ url: string; auth?: string }> = [];
  const fetch = vi.fn<AuthRuntime['fetch']>(async (input, init) => {
    if (input === '/connect/token') return token();
    const auth = (init?.headers as Record<string, string> | undefined)?.Authorization;
    calls.push({ url: input, auth });
    return jsonResponse({ ok: true }, api(auth?.replace('Bearer ', '')));
  });
  const tab = fakeRuntime({ fetch, path: '/agents?filtro=x' });
  const store = createSessionStore(tab.runtime.storage, tab.runtime.events);
  const renewal = createRenewal(tab.runtime, store);
  return { ...tab, fetch, calls, store, sessionFetch: createSessionFetch(tab.runtime, renewal) };
}

describe('sessionFetch', () => {
  it('anexa o access token do registro e mantém os outros cabeçalhos', async () => {
    const t = setup(() => 200);
    t.store.write(session(1, t.clock.now + 600_000));

    await t.sessionFetch(`${API}/agents`, { headers: { 'Content-Type': 'application/json' } });

    const init = t.fetch.mock.calls[0][1];
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json', Authorization: 'Bearer access-1' });
  });

  it('sem registro, não anexa Authorization', async () => {
    const t = setup(() => 200);

    await t.sessionFetch(`${API}/agents`);

    expect(t.calls[0].auth).toBeUndefined();
  });

  it('renova antes do pedido quando o token vence em menos de 60 s', async () => {
    const t = setup(() => 200);
    t.store.write(session(1, t.clock.now + 30_000));

    await t.sessionFetch(`${API}/agents`);

    expect(t.calls).toEqual([{ url: `${API}/agents`, auth: 'Bearer access-2' }]);
  });

  it('401 seguido de sucesso não desloga: renova uma vez e repete com o token novo', async () => {
    const t = setup((token) => (token === 'access-1' ? 401 : 200));
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(200);
    expect(t.calls.map((c) => c.auth)).toEqual(['Bearer access-1', 'Bearer access-2']);
    expect(t.store.read()?.accessToken).toBe('access-2');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  it('segundo 401 encerra a sessão e vai ao /entrar com o voltarPara da rota atual', async () => {
    const t = setup(() => 401);
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(401);
    expect(t.calls).toHaveLength(2);
    expect(t.store.read()).toBeNull();
    expect(t.runtime.assign).toHaveBeenCalledWith(
      `/entrar?motivo=sessao-expirada&voltarPara=${encodeURIComponent('/agents?filtro=x')}`,
    );
  });

  it('no apps/connectors, o segundo 401 não encerra: o registro continua e não navega', async () => {
    const t = setup(() => 401);
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/connectors/x`, undefined, { endsSessionOn401: false });

    expect(response.status).toBe(401);
    expect(t.calls).toHaveLength(2);
    expect(t.store.read()?.accessToken).toBe('access-2');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  it('invalid_grant na renovação encerra, também no apps/connectors', async () => {
    const t = setup(
      () => 401,
      () => jsonResponse({ error: 'invalid_grant' }, 400),
    );
    t.store.write(session(1, t.clock.now + 600_000));

    await t.sessionFetch(`${API}/connectors/x`, undefined, { endsSessionOn401: false });

    expect(t.store.read()).toBeNull();
    expect(t.runtime.assign).toHaveBeenCalledWith(expect.stringContaining('/entrar?motivo=sessao-expirada'));
  });

  it('emissor indisponível depois do 401 não encerra: a tela recebe o 401', async () => {
    const t = setup(
      () => 401,
      () => jsonResponse({}, 503, { 'Retry-After': '30' }),
    );
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(401);
    expect(t.calls).toHaveLength(1);
    expect(t.store.read()?.accessToken).toBe('access-1');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  it('503 da rota não renova, não repete e não desloga', async () => {
    const t = setup(() => 503);
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(503);
    expect(t.calls).toHaveLength(1);
    expect(t.store.read()?.accessToken).toBe('access-1');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });
});
