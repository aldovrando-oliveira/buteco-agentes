import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { papelDaSessao, usePapel } from './session';
import { SESSION_KEY } from './sessionStore';
import { fakeJwt, session } from '../test/authFakes';

// O papel da sessão (spec `panel-roles`, "O papel da sessão vem do id token, e o desconhecido
// é Leitor"; design.md da change papeis-no-painel, D1).
describe('papelDaSessao', () => {
  it('lê o role do id token', () => {
    expect(papelDaSessao(session(1, 1000, { idToken: fakeJwt({ role: 'operator' }) }, null))).toBe(
      'operador',
    );
  });

  it('Id token sem role é papel desconhecido', () => {
    expect(
      papelDaSessao(session(1, 1000, { idToken: fakeJwt({ name: 'Ana Souza' }) }, null)),
    ).toBeNull();
  });

  it('id token ilegível, ou sem sessão, é papel desconhecido, sem lançar', () => {
    expect(papelDaSessao(session(1, 1000, { idToken: 'isto-nao-e-jwt' }))).toBeNull();
    expect(papelDaSessao(null)).toBeNull();
  });

  it('o registro mudado por outra aba muda o usePapel', () => {
    const { result } = renderHook(() => usePapel());
    expect(result.current).toBe('admin');

    const leitor = session(2, Date.now() + 60_000, {}, 'reader');
    act(() => {
      window.localStorage.setItem(SESSION_KEY, JSON.stringify(leitor));
      window.dispatchEvent(new StorageEvent('storage', { key: SESSION_KEY }));
    });

    expect(result.current).toBe('leitor');
  });
});
