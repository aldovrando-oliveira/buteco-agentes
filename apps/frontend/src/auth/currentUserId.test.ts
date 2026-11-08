import { describe, expect, it } from 'vitest';
import { currentUserId, isSameUser } from './session';
import { fakeJwt, session } from '../test/authFakes';

// O reconhecimento do próprio usuário (requisito "Reconhecimento do próprio usuário"; design.md
// da change editar-usuario-painel, D2).
describe('currentUserId', () => {
  it('lê o sub do id token', () => {
    const s = session(1, 1000, { idToken: fakeJwt({ sub: 'u-ana', name: 'Ana Souza' }) });
    expect(currentUserId(s)).toBe('u-ana');
  });

  it('id token ilegível é null, sem lançar', () => {
    expect(currentUserId(session(1, 1000, { idToken: 'isto-nao-e-jwt' }))).toBeNull();
  });

  it('sem sub, ou sem sessão, é null', () => {
    expect(currentUserId(session(1, 1000, { idToken: fakeJwt({ name: 'Ana Souza' }) }))).toBeNull();
    expect(currentUserId(session(1, 1000, { idToken: fakeJwt({ sub: '' }) }))).toBeNull();
    expect(currentUserId(null)).toBeNull();
  });
});

describe('isSameUser', () => {
  it('id da rota em maiúsculas ainda é o próprio', () => {
    expect(
      isSameUser('3f2a9c1e-0b7d-4e55-9a10-6c2b8d4e71c1', '3F2A9C1E-0B7D-4E55-9A10-6C2B8D4E71C1'),
    ).toBe(true);
  });

  it('outro id não é o próprio, e null nunca é', () => {
    expect(isSameUser('u-ana', 'u-bruno')).toBe(false);
    expect(isSameUser(null, 'u-ana')).toBe(false);
  });
});
