// A sessão do painel ligada ao navegador: uma instância de cada peça do módulo, para
// as telas e as cópias do `request<T>` (design.md da change painel-entra-pelo-emissor,
// D13). As peças recebem o runtime por parâmetro, e os testes do módulo montam as
// suas; os testes das telas trocam o `fetch` global e o `authRuntime.assign`.
import { useMemo, useSyncExternalStore } from 'react';
import { createLogout } from './logout';
import { createRenewal } from './renewal';
import { browserRuntime } from './runtime';
import { createSessionFetch } from './sessionFetch';
import { createSessionStore, parseSession } from './sessionStore';
import type { Session } from './sessionStore';

export const authRuntime = browserRuntime();
export const sessionStore = createSessionStore(authRuntime.storage, authRuntime.events);
export const renewal = createRenewal(authRuntime, sessionStore);
export const sessionFetch = createSessionFetch(authRuntime, renewal);
export const logout = createLogout(authRuntime, sessionStore);

/** A sessão atual, relida quando o registro muda nesta aba ou em outra. */
export function useSession(): Session | null {
  const raw = useSyncExternalStore(sessionStore.subscribe, sessionStore.raw);
  return useMemo(() => parseSession(raw), [raw]);
}
