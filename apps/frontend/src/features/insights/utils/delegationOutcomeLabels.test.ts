import { describe, expect, it } from 'vitest';
import {
  KNOWN_DELEGATION_OUTCOMES,
  delegationOutcomeLabel,
} from './delegationOutcomeLabels';

describe('delegationOutcomeLabel', () => {
  it('o vocabulário é o fechado dos quatro de ExecutionMetricsValues', () => {
    expect([...KNOWN_DELEGATION_OUTCOMES].sort()).toEqual([
      'Completed',
      'Expired',
      'NotStarted',
      'TargetUnsuccessful',
    ]);
  });

  it.each(KNOWN_DELEGATION_OUTCOMES)('traduz %s', (outcome) => {
    const label = delegationOutcomeLabel(outcome);
    expect(label.unknown).toBe(false);
    expect(label.text).not.toBe(outcome);
    expect(label.text.length).toBeGreaterThan(0);
  });

  it('nomeia os dois resultados que explicam a divergência', () => {
    // Não é teste de prosa: são estes dois que a tela precisa deixar legíveis
    // ao lado do número, porque são a primeira das duas causas da assimetria.
    expect(delegationOutcomeLabel('NotStarted').text).toBe('Não iniciada');
    expect(delegationOutcomeLabel('Expired').text).toBe('Expirou');
  });

  it('o desconhecido devolve o valor cru, e NÃO o rótulo de outro resultado', () => {
    const label = delegationOutcomeLabel('Rescheduled');

    expect(label.unknown).toBe(true);
    expect(label.text).toBe('Rescheduled');
    // A asserção negativa é o que importa: cair no rótulo de outro resultado
    // faria a tela afirmar uma causa errada, que é pior que não afirmar
    // nenhuma.
    for (const conhecido of KNOWN_DELEGATION_OUTCOMES) {
      expect(label.text).not.toBe(delegationOutcomeLabel(conhecido).text);
    }
  });

  it('o desconhecido não é string vazia nem traço', () => {
    // Silêncio disfarçado é o modo de falha vizinho: um rótulo vazio some da
    // tela do mesmo jeito que omitir a linha.
    const label = delegationOutcomeLabel('');
    expect(label.unknown).toBe(true);
    expect(label.text).toBe('');
  });
});
