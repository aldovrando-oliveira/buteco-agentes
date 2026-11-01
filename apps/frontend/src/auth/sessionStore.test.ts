import { describe, expect, it, vi } from 'vitest';
import { SESSION_KEY, createSessionStore, sessionFromTokenResponse } from './sessionStore';
import { MemoryStorage, fakeJwt, session } from '../test/authFakes';

function setup() {
  const storage = new MemoryStorage();
  const events = new EventTarget();
  const store = createSessionStore(
    () => storage,
    () => events as unknown as Window,
  );
  return { storage, events, store };
}

describe('sessionStore', () => {
  it('grava e lê o registro em buteco.sessao', () => {
    const { storage, store } = setup();
    const s = session(1, 1000);

    store.write(s);

    expect(JSON.parse(storage.getItem(SESSION_KEY)!)).toEqual(s);
    expect(store.read()).toEqual(s);
  });

  it('registro corrompido é ausência, sem lançar', () => {
    const { storage, store } = setup();
    storage.setItem(SESSION_KEY, '{isto não é json');

    expect(store.read()).toBeNull();
  });

  it('registro com a forma errada é ausência', () => {
    const { storage, store } = setup();
    storage.setItem(SESSION_KEY, JSON.stringify({ accessToken: 'a' }));

    expect(store.read()).toBeNull();
  });

  it('avisa quando o registro é apagado por outra aba', () => {
    const { storage, events, store } = setup();
    store.write(session(1, 1000));
    const listener = vi.fn();
    store.subscribe(listener);

    storage.removeItem(SESSION_KEY);
    events.dispatchEvent(new StorageEvent('storage', { key: SESSION_KEY, newValue: null }));

    expect(listener).toHaveBeenCalledTimes(1);
    expect(store.read()).toBeNull();
  });

  it('não avisa por chave alheia de outra aba', () => {
    const { events, store } = setup();
    const listener = vi.fn();
    store.subscribe(listener);

    events.dispatchEvent(new StorageEvent('storage', { key: 'mantine-color-scheme-value' }));

    expect(listener).not.toHaveBeenCalled();
  });

  it('avisa nesta aba quando ela mesma grava ou apaga, e para de avisar depois do unsubscribe', () => {
    const { store } = setup();
    const listener = vi.fn();
    const unsubscribe = store.subscribe(listener);

    store.write(session(1, 1000));
    store.remove();
    unsubscribe();
    store.write(session(2, 1000));

    expect(listener).toHaveBeenCalledTimes(2);
  });

  it('o registro da resposta do token leva nome e e-mail do id token, com acento', () => {
    const now = Date.parse('2026-11-01T12:00:00Z');
    const s = sessionFromTokenResponse(
      {
        access_token: 'a',
        refresh_token: 'r',
        id_token: fakeJwt({ name: 'João Conceição', email: 'joao@exemplo.test' }),
        expires_in: 900,
      },
      now,
    );

    expect(s).toMatchObject({
      accessToken: 'a',
      refreshToken: 'r',
      accessExpiresAt: now + 900_000,
      nome: 'João Conceição',
      email: 'joao@exemplo.test',
    });
  });

  it('sem id token nem refresh novos, mantém os anteriores', () => {
    const previous = session(1, 0);
    const s = sessionFromTokenResponse({ access_token: 'a2', expires_in: 60 }, 0, previous);

    expect(s.idToken).toBe(previous.idToken);
    expect(s.refreshToken).toBe(previous.refreshToken);
    expect(s.nome).toBe('Ana Souza');
  });
});
