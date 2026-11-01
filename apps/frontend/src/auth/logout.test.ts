import { describe, expect, it } from 'vitest';
import { createLogout } from './logout';
import { createSessionStore } from './sessionStore';
import { fakeRuntime, session } from '../test/authFakes';

function setup() {
  const tab = fakeRuntime();
  const store = createSessionStore(tab.runtime.storage, tab.runtime.events);
  return { ...tab, store, logout: createLogout(tab.runtime, store) };
}

describe('logout', () => {
  it('no instante da navegação o registro já não existe', async () => {
    const tab = setup();
    tab.store.write(session(1, tab.clock.now + 600_000));
    let recordAtNavigation: unknown = 'não navegou';
    tab.runtime.assign.mockImplementation(() => {
      recordAtNavigation = tab.store.read();
    });

    await tab.logout();

    expect(recordAtNavigation).toBeNull();
  });

  it('navega ao /connect/logout com o id_token_hint e o post_logout_redirect_uri da origem', async () => {
    const tab = setup();
    const s = session(1, tab.clock.now + 600_000);
    tab.store.write(s);

    await tab.logout();

    const url = new URL(tab.runtime.assign.mock.calls[0][0], 'https://painel.exemplo.test');
    expect(url.pathname).toBe('/connect/logout');
    expect(url.searchParams.get('id_token_hint')).toBe(s.idToken);
    expect(url.searchParams.get('post_logout_redirect_uri')).toBe('https://painel.exemplo.test/entrar');
  });

  it('sem id token, navega sem o hint', async () => {
    const tab = setup();

    await tab.logout();

    const url = new URL(tab.runtime.assign.mock.calls[0][0], 'https://painel.exemplo.test');
    expect(url.searchParams.has('id_token_hint')).toBe(false);
    expect(url.searchParams.get('post_logout_redirect_uri')).toBe('https://painel.exemplo.test/entrar');
  });
});
