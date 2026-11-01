import { describe, expect, it } from 'vitest';
import { userInitials } from './userInitials';

describe('userInitials', () => {
  it.each([
    ['Ana Souza', 'AS'],
    ['Aldovrando O.', 'AO'],
    ['Maria da Silva Pereira', 'MP'],
    ['  ana   souza  ', 'AS'],
    ['admin', 'A'],
  ])('%s → %s', (nome, esperado) => {
    expect(userInitials(nome, 'x@exemplo.test')).toBe(esperado);
  });

  it('sem nome, a primeira letra do e-mail', () => {
    expect(userInitials(null, 'bia@exemplo.test')).toBe('B');
    expect(userInitials('   ', 'bia@exemplo.test')).toBe('B');
  });
});
