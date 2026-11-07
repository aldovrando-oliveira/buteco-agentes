import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  PASSWORD_CHANGED_KEY,
  clearPasswordChanged,
  hasPasswordChanged,
  markPasswordChanged,
} from './passwordChangedNotice';

afterEach(() => {
  vi.restoreAllMocks();
  sessionStorage.clear();
});

describe('passwordChangedNotice', () => {
  it('grava a chave na aba, e só nela', () => {
    markPasswordChanged();

    expect(sessionStorage.getItem(PASSWORD_CHANGED_KEY)).not.toBeNull();
    expect(localStorage.getItem(PASSWORD_CHANGED_KEY)).toBeNull();
    expect(hasPasswordChanged()).toBe(true);
  });

  it('apagar tira a chave', () => {
    markPasswordChanged();
    clearPasswordChanged();

    expect(hasPasswordChanged()).toBe(false);
  });

  it('sem sessionStorage, nada lança e não há aviso', () => {
    // Um navegador que recusa o storage lança já no acesso ao `sessionStorage`.
    vi.spyOn(window, 'sessionStorage', 'get').mockImplementation(() => {
      throw new DOMException('negado', 'SecurityError');
    });

    expect(() => markPasswordChanged()).not.toThrow();
    expect(hasPasswordChanged()).toBe(false);
  });
});
