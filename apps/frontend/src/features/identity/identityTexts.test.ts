import { describe, expect, it } from 'vitest';
import { formatDuration, keyBadge, nextStepText } from './identityTexts';
import type { SigningKey } from './api/identityApi';

const SP = 'America/Sao_Paulo';

const chave = (estado: string): SigningKey => ({
  kid: 'k',
  estado,
  criadaEm: '2026-10-13T15:00:00Z',
  assinaDesde: '2026-10-20T15:00:00Z',
  assinaAte: '2027-01-18T15:00:00Z',
  saiDaPublicacaoEm: '2026-11-04T15:00:00Z',
});

describe('formatDuration', () => {
  it('unidades exatas', () => {
    expect(formatDuration(86_400)).toBe('1 dia');
    expect(formatDuration(3_600)).toBe('1 h');
    expect(formatDuration(900)).toBe('15 min');
    expect(formatDuration(90)).toBe('90 s');
  });

  it('sem unidade arredondada', () => {
    expect(formatDuration(5_400)).toBe('90 min');
    expect(formatDuration(129_600)).toBe('36 h');
  });

  it('os valores de hoje do emissor', () => {
    expect([900, 1_209_600, 7_776_000, 604_800, 1_296_000, 3_600].map(formatDuration)).toEqual([
      '15 min',
      '14 dias',
      '90 dias',
      '7 dias',
      '15 dias',
      '1 h',
    ]);
  });
});

describe('Próximo passo e selo', () => {
  it('as três frases, com as datas no fuso', () => {
    expect(nextStepText(chave('em-uso'), SP)).toBe('Assina desde 20/10/2026; é substituída em 18/01/2027');
    expect(nextStepText(chave('proxima'), SP)).toBe('Já publicada; passa a assinar em 20/10/2026');
    expect(nextStepText(chave('aposentando'), SP)).toBe('Não assina desde 18/01/2027; sai da publicação em 04/11/2026');
  });

  it('estado desconhecido: o valor cru, neutro, e o travessão', () => {
    expect(keyBadge('revogada')).toEqual({ texto: 'revogada', tom: 'neutral' });
    expect(nextStepText(chave('revogada'), SP)).toBe('—');
  });
});
