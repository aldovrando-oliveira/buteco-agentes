import { vi } from 'vitest';
import type { AuthRuntime } from '../auth/runtime';
import type { Session } from '../auth/sessionStore';
import { FakeLockManager } from './fakeLocks';

/** Storage em memória, separado do `localStorage` global do setup. */
export class MemoryStorage implements Storage {
  private store = new Map<string, string>();
  get length() {
    return this.store.size;
  }
  clear() {
    this.store.clear();
  }
  getItem(key: string) {
    return this.store.has(key) ? this.store.get(key)! : null;
  }
  key(index: number) {
    return Array.from(this.store.keys())[index] ?? null;
  }
  removeItem(key: string) {
    this.store.delete(key);
  }
  setItem(key: string, value: string) {
    this.store.set(key, String(value));
  }
}

/** JWT sem assinatura válida, com o payload dado: basta para o painel, que só decodifica. */
export function fakeJwt(payload: Record<string, unknown>): string {
  const encode = (value: unknown) =>
    btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(value))))
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
  return `${encode({ alg: 'none', typ: 'JWT' })}.${encode(payload)}.assinatura`;
}

export function jsonResponse(body: unknown, status = 200, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  });
}

export function tokenResponse(n: number, expiresIn = 900) {
  return jsonResponse({
    access_token: `access-${n}`,
    refresh_token: `refresh-${n}`,
    id_token: fakeJwt({ name: 'Ana Souza', email: 'ana@exemplo.test', n }),
    expires_in: expiresIn,
    token_type: 'Bearer',
  });
}

export function session(n: number, accessExpiresAt: number, overrides: Partial<Session> = {}): Session {
  return {
    accessToken: `access-${n}`,
    accessExpiresAt,
    refreshToken: `refresh-${n}`,
    idToken: fakeJwt({ name: 'Ana Souza', email: 'ana@exemplo.test', n }),
    nome: 'Ana Souza',
    email: 'ana@exemplo.test',
    ...overrides,
  };
}

/**
 * Uma "aba" de teste: storage, lock e relógio podem ser compartilhados com outra aba,
 * e o resto é dela. O relógio é manual (`clock.now`), e o `sleep` avança esse relógio
 * em vez de esperar.
 */
export function fakeRuntime(shared: {
  storage?: Storage;
  locks?: FakeLockManager;
  clock?: { now: number };
  fetch?: AuthRuntime['fetch'];
  tabStorage?: Storage;
  path?: string;
} = {}) {
  const clock = shared.clock ?? { now: Date.parse('2026-11-01T12:00:00Z') };
  const storage = shared.storage ?? new MemoryStorage();
  const tabStorage = shared.tabStorage ?? new MemoryStorage();
  const locks = shared.locks ?? new FakeLockManager();
  const events = new EventTarget();
  const sleeps: number[] = [];
  const runtime = {
    storage: () => storage,
    tabStorage: () => tabStorage,
    locks: () => locks,
    fetch: shared.fetch ?? vi.fn<AuthRuntime['fetch']>(),
    now: () => clock.now,
    sleep: vi.fn(async (ms: number) => {
      sleeps.push(ms);
      clock.now += ms;
    }),
    timeoutSignal: () => new AbortController().signal,
    origin: () => 'https://painel.exemplo.test',
    currentPath: () => shared.path ?? '/agents',
    assign: vi.fn<(url: string) => void>(),
    events: () => events as unknown as Window,
  } satisfies AuthRuntime;
  return { runtime, clock, storage, tabStorage, locks, events, sleeps };
}

/**
 * Para os testes de tela e das cópias do `request<T>`: grava no `localStorage` global
 * o registro de uma sessão válida por 15 min (o lugar onde o painel a procura), ou o
 * apaga. Substitui o `setToken`/`clearToken` do `auth/token.ts`, que saiu.
 */
export function signInForTest(accessToken = 'token-de-teste'): Session {
  const s = session(1, Date.now() + 15 * 60_000, { accessToken });
  window.localStorage.setItem('buteco.sessao', JSON.stringify(s));
  return s;
}

export function signOutForTest(): void {
  window.localStorage.removeItem('buteco.sessao');
}
