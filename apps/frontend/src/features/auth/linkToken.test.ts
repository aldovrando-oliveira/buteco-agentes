import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { clearLinkToken, LINK_TOKEN_KEYS, takeLinkToken } from './linkToken';

beforeEach(() => {
  sessionStorage.clear();
});

afterEach(() => {
  window.history.replaceState(null, '', '/');
});

describe('takeLinkToken', () => {
  it('lê o fragmento, grava a chave da página e tira o fragmento do endereço', () => {
    window.history.replaceState({ usr: null, key: 'k', idx: 0 }, '', '/convite#k7Qp2vX9');

    expect(takeLinkToken('convite')).toBe('k7Qp2vX9');
    expect(window.location.pathname).toBe('/convite');
    expect(window.location.hash).toBe('');
    expect(sessionStorage.getItem(LINK_TOKEN_KEYS.convite)).toBe('k7Qp2vX9');
    // O `state` do router fica: trocar o endereço não pode apagar a entrada do histórico dele.
    expect(window.history.state).toEqual({ usr: null, key: 'k', idx: 0 });
  });

  it('sem fragmento, devolve o token da chave (a recarga)', () => {
    sessionStorage.setItem(LINK_TOKEN_KEYS['redefinir-senha'], 'Hq8vN3pT');
    window.history.replaceState(null, '', '/redefinir-senha');

    expect(takeLinkToken('redefinir-senha')).toBe('Hq8vN3pT');
  });

  it('é idempotente: a segunda leitura acha a chave', () => {
    window.history.replaceState(null, '', '/convite#abc');

    expect(takeLinkToken('convite')).toBe('abc');
    expect(takeLinkToken('convite')).toBe('abc');
  });

  it('sem fragmento e sem chave, null', () => {
    window.history.replaceState(null, '', '/convite');
    expect(takeLinkToken('convite')).toBeNull();
  });

  it('as chaves são uma por página', () => {
    window.history.replaceState(null, '', '/convite#abc');
    takeLinkToken('convite');

    expect(sessionStorage.getItem(LINK_TOKEN_KEYS['redefinir-senha'])).toBeNull();
    clearLinkToken('convite');
    expect(sessionStorage.getItem(LINK_TOKEN_KEYS.convite)).toBeNull();
  });
});
