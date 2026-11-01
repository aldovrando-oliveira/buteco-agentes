import { describe, expect, it, vi } from 'vitest';
import { RETRY_WINDOW_MS, createRenewal } from './renewal';
import { createSessionStore } from './sessionStore';
import { FakeLockManager, deferred } from '../test/fakeLocks';
import { MemoryStorage, fakeRuntime, jsonResponse, session, tokenResponse } from '../test/authFakes';
import { strictTokenEndpoint } from '../test/strictTokenEndpoint';
import type { AuthRuntime } from './runtime';

/** Duas "abas" sobre o mesmo storage, o mesmo lock e o mesmo relógio. */
function twoTabs(fetch: AuthRuntime['fetch']) {
  const storage = new MemoryStorage();
  const locks = new FakeLockManager();
  const clock = { now: Date.parse('2026-11-01T12:00:00Z') };
  const tab = () => {
    const t = fakeRuntime({ storage, locks, clock, fetch });
    const store = createSessionStore(t.runtime.storage, t.runtime.events);
    return { ...t, store, renewal: createRenewal(t.runtime, store) };
  };
  return { a: tab(), b: tab(), clock, storage };
}

function oneTab(fetch: AuthRuntime['fetch']) {
  return twoTabs(fetch).a;
}

describe('renovação', () => {
  it('duas renovações simultâneas não derrubam a sessão', async () => {
    const endpoint = strictTokenEndpoint();
    const { a, b, clock } = twoTabs(endpoint.fetch);
    // Vence em 30 s: abaixo dos 60 s, as duas abas renovam antes do pedido.
    a.store.write(session(1, clock.now + 30_000));

    const [sa, sb] = await Promise.all([a.renewal.ensureFresh(), b.renewal.ensureFresh()]);

    // Primeiro a sessão: sem coordenação, a segunda aba apresenta o refresh já
    // resgatado, o emissor estrito revoga a cadeia, e é aqui que o caso reprova.
    expect(a.store.read()?.refreshToken).toBe('refresh-2');
    expect(endpoint.isValid('refresh-2')).toBe(true);
    expect(sa?.accessToken).toBe('access-2');
    expect(sb?.accessToken).toBe('access-2');
    expect(endpoint.requests).toHaveLength(1);
  });

  it('reaproveita a renovação de outra aba: quem esperava o lock não pede de novo', async () => {
    const endpoint = strictTokenEndpoint();
    const { a, b, clock } = twoTabs(endpoint.fetch);
    a.store.write(session(1, clock.now + 30_000));
    const inFlight = deferred<void>();
    endpoint.hold(inFlight.promise);

    // A entra no lock e fica com o pedido em voo; B, com o token antigo (que levou
    // 401), pede a renovação nesse meio tempo.
    const renewA = a.renewal.renew();
    await vi.waitFor(() => expect(endpoint.requests).toHaveLength(1));
    const renewB = b.renewal.renew('access-1');
    inFlight.resolve();

    const [ra, rb] = await Promise.all([renewA, renewB]);
    expect(ra).toMatchObject({ kind: 'ok', session: { accessToken: 'access-2' } });
    expect(rb).toMatchObject({ kind: 'ok', session: { accessToken: 'access-2' } });
    expect(endpoint.requests).toHaveLength(1);
  });

  it('com tempo de sobra, não renova', async () => {
    const fetch = vi.fn<AuthRuntime['fetch']>();
    const tab = oneTab(fetch);
    tab.store.write(session(1, tab.clock.now + 61_000));

    expect((await tab.renewal.ensureFresh())?.accessToken).toBe('access-1');
    expect(fetch).not.toHaveBeenCalled();
  });

  it('o pedido leva grant_type, o refresh do registro e o client_id', async () => {
    const endpoint = strictTokenEndpoint();
    const tab = oneTab(endpoint.fetch);
    tab.store.write(session(1, tab.clock.now));

    await tab.renewal.renew();

    expect(Object.fromEntries(endpoint.requests[0])).toEqual({
      grant_type: 'refresh_token',
      refresh_token: 'refresh-1',
      client_id: 'buteco-painel',
    });
  });

  it('503 na renovação não apaga a sessão, e nenhuma tentativa começa depois de 25 s', async () => {
    let calls = 0;
    const starts: number[] = [];
    const fetch = vi.fn<AuthRuntime['fetch']>(async () => {
      starts.push(tab.clock.now - t0);
      calls += 1;
      // Válvula do teste: sem o prazo, a renovação repetiria para sempre; na 11ª
      // tentativa ela recebe 200, e o caso reprova pelo resultado, sem travar.
      return calls > 10
        ? tokenResponse(2)
        : jsonResponse({ title: 'iniciando' }, 503, { 'Retry-After': '5' });
    });
    const tab = oneTab(fetch);
    const t0 = tab.clock.now;
    tab.store.write(session(1, t0));

    const outcome = await tab.renewal.renew();

    expect(outcome).toEqual({ kind: 'unavailable' });
    expect(starts).toEqual([0, 5_000, 10_000, 15_000, 20_000]);
    expect(Math.max(...starts)).toBeLessThan(RETRY_WINDOW_MS);
    expect(tab.store.read()?.refreshToken).toBe('refresh-1');
    expect(tab.runtime.assign).not.toHaveBeenCalled();
  });

  it('falha de rede repete depois de 1 s, e a resposta seguinte vale', async () => {
    const fetch = vi
      .fn<AuthRuntime['fetch']>()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(tokenResponse(2));
    const tab = oneTab(fetch);
    tab.store.write(session(1, tab.clock.now));

    const outcome = await tab.renewal.renew();

    expect(outcome).toMatchObject({ kind: 'ok', session: { accessToken: 'access-2' } });
    expect(tab.sleeps).toEqual([1_000]);
  });

  it('invalid_grant apaga o registro e encerra', async () => {
    const fetch = vi.fn<AuthRuntime['fetch']>(async () => jsonResponse({ error: 'invalid_grant' }, 400));
    const tab = oneTab(fetch);
    tab.store.write(session(1, tab.clock.now));

    expect(await tab.renewal.renew()).toEqual({ kind: 'ended' });
    expect(tab.store.read()).toBeNull();
  });

  it('outro 400 não encerra: o registro continua', async () => {
    const fetch = vi.fn<AuthRuntime['fetch']>(async () => jsonResponse({ error: 'invalid_client' }, 400));
    const tab = oneTab(fetch);
    tab.store.write(session(1, tab.clock.now));

    expect(await tab.renewal.renew()).toEqual({ kind: 'unavailable' });
    expect(tab.store.read()?.accessToken).toBe('access-1');
  });

  it('sem registro, a sessão acabou e nada é pedido', async () => {
    const fetch = vi.fn<AuthRuntime['fetch']>();
    const tab = oneTab(fetch);

    expect(await tab.renewal.renew()).toEqual({ kind: 'ended' });
    expect(await tab.renewal.ensureFresh()).toBeNull();
    expect(fetch).not.toHaveBeenCalled();
  });
});
