// Todo escritor do registro sob o lock da renovação (design.md da change
// painel-entra-pelo-emissor, D3, revisão do dono de 01/11/2026).
import { describe, expect, it, vi } from 'vitest';
import { createLogout } from './logout';
import { createRenewal } from './renewal';
import { createSessionStore } from './sessionStore';
import { FakeLockManager, deferred } from '../test/fakeLocks';
import { MemoryStorage, fakeRuntime, session } from '../test/authFakes';
import { strictTokenEndpoint } from '../test/strictTokenEndpoint';
import type { AuthRuntime } from './runtime';

function twoTabs(fetch: AuthRuntime['fetch']) {
  const storage = new MemoryStorage();
  const locks = new FakeLockManager();
  const clock = { now: Date.parse('2026-11-01T12:00:00Z') };
  const tab = () => {
    const t = fakeRuntime({ storage, locks, clock, fetch });
    const store = createSessionStore(t.runtime.storage, t.runtime.events);
    return {
      ...t,
      store,
      renewal: createRenewal(t.runtime, store),
      logout: createLogout(t.runtime, store),
    };
  };
  return { a: tab(), b: tab(), clock };
}

describe('escritores do registro sob o lock', () => {
  it('logout durante uma renovação termina sem sessão, com o hint do id token novo', async () => {
    const endpoint = strictTokenEndpoint();
    const { a, b, clock } = twoTabs(endpoint.fetch);
    a.store.write(session(1, clock.now + 30_000));
    const inFlight = deferred<void>();
    endpoint.hold(inFlight.promise);

    // A renova, com o pedido ao /connect/token em voo; B clica em "Sair".
    const renewing = a.renewal.renew();
    await vi.waitFor(() => expect(endpoint.requests).toHaveLength(1));
    const leaving = b.logout();
    // A resposta 200 de A chega depois do clique de B.
    inFlight.resolve();
    await Promise.all([renewing, leaving]);

    expect(a.store.read()).toBeNull();
    const hint = new URL(b.runtime.assign.mock.calls[0][0], 'https://x').searchParams.get('id_token_hint');
    // O hint é o id token da geração 2, recebido por A.
    expect(JSON.parse(atob(hint!.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))).toMatchObject({ n: 2 });
  });

  it('fim de sessão não apaga a sessão nova de outra aba', async () => {
    const { a, clock } = twoTabs(vi.fn());
    // A recebeu o segundo 401 com t1; o registro já tem t2, de um login em outra aba.
    a.store.write(session(2, clock.now + 600_000));

    await a.renewal.endSessionIfCurrent('access-1');

    expect(a.store.read()?.accessToken).toBe('access-2');
  });

  it('fim de sessão apaga quando o registro ainda é o que falhou', async () => {
    const { a, clock } = twoTabs(vi.fn());
    a.store.write(session(1, clock.now + 600_000));

    await a.renewal.endSessionIfCurrent('access-1');

    expect(a.store.read()).toBeNull();
  });

  it('a gravação de um login novo espera a renovação em voo', async () => {
    const endpoint = strictTokenEndpoint();
    const { a, b, clock } = twoTabs(endpoint.fetch);
    a.store.write(session(1, clock.now + 30_000));
    const inFlight = deferred<void>();
    endpoint.hold(inFlight.promise);

    const renewing = a.renewal.renew();
    await vi.waitFor(() => expect(endpoint.requests).toHaveLength(1));
    const storing = b.renewal.storeNewSession(session(9, clock.now + 900_000));
    inFlight.resolve();
    await Promise.all([renewing, storing]);

    // O login novo grava por último: a resposta atrasada da renovação não o sobrescreve.
    expect(a.store.read()?.accessToken).toBe('access-9');
  });
});
