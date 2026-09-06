import { describe, expect, it } from 'vitest';
import { matchesSearch, normalizeForSearch } from './searchText';

describe('normalizeForSearch', () => {
  it('passa o texto para minúsculas', () => {
    expect(normalizeForSearch('Atendente')).toBe('atendente');
  });

  it('remove acentuação das vogais', () => {
    expect(normalizeForSearch('Análise Técnica')).toBe('analise tecnica');
  });

  it('remove a cedilha', () => {
    expect(normalizeForSearch('Cobrança')).toBe('cobranca');
  });

  it('mantém texto vazio como vazio', () => {
    expect(normalizeForSearch('')).toBe('');
  });
});

describe('matchesSearch', () => {
  it('encontra com caixa diferente da cadastrada', () => {
    expect(matchesSearch('ATENDENTE', 'Atendente Financeiro')).toBe(true);
  });

  it('encontra quando o termo vem sem acentuação e o texto tem', () => {
    expect(matchesSearch('cobranca', 'Cobrança Ativa')).toBe(true);
  });

  it('encontra quando o termo vem com acentuação e o texto não tem', () => {
    expect(matchesSearch('cobrança', 'Cobranca Ativa')).toBe(true);
  });

  it('procura em mais de um campo', () => {
    expect(matchesSearch('boleto', 'Atendente', 'Emite segunda via de boleto')).toBe(true);
  });

  it('não encontra quando nenhum campo contém o termo', () => {
    expect(matchesSearch('vendas', 'Atendente', 'Emite boleto')).toBe(false);
  });

  it('termo em branco casa com qualquer item', () => {
    expect(matchesSearch('', 'Atendente')).toBe(true);
    expect(matchesSearch('   ', 'Atendente')).toBe(true);
  });

  it('ignora campos nulos e indefinidos', () => {
    expect(matchesSearch('atendente', null, undefined, 'Atendente')).toBe(true);
    expect(matchesSearch('atendente', null, undefined)).toBe(false);
  });
});
