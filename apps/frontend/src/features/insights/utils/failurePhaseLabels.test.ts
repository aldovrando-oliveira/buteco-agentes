import { describe, expect, it } from 'vitest';
import {
  KNOWN_EXECUTION_PHASES,
  KNOWN_INDEXING_OUTCOMES,
  KNOWN_INDEXING_PHASES,
  executionPhaseLabel,
  indexingFailureLabel,
  indexingOutcomeLabel,
  indexingPhaseLabel,
} from './failurePhaseLabels';

describe('executionPhaseLabel', () => {
  it('cobre as SETE fases de ExecutionMetricsValues.FailurePhase', () => {
    // O número é contrato com `apps/workers`: se uma fase entrar lá e não aqui,
    // este caso não pega — mas o caso do desconhecido abaixo garante que ela
    // apareça na tela em vez de sumir.
    expect(KNOWN_EXECUTION_PHASES).toEqual([
      'DelegationDepthExceeded',
      'ContextLock',
      'ChatClientResolution',
      'ToolResolution',
      'SessionLoad',
      'AgentRun',
      'Persistence',
    ]);
  });

  it.each(KNOWN_EXECUTION_PHASES)('%s tem rótulo de operador em português', (phase) => {
    const label = executionPhaseLabel(phase);

    expect(label.unknown).toBe(false);
    expect(label.text).not.toBe(phase);
    expect(label.text.length).toBeGreaterThan(0);
  });

  it('fase desconhecida devolve o VALOR CRU, não o rótulo de outra fase', () => {
    const label = executionPhaseLabel('FaseQueAindaNaoExiste');

    expect(label.unknown).toBe(true);
    expect(label.text).toBe('FaseQueAindaNaoExiste');

    // O guarda negativo: o valor desconhecido não pode ter caído em nenhum dos
    // sete rótulos conhecidos. Afirmar uma causa errada é pior que não afirmar
    // nenhuma.
    const rotulosConhecidos = KNOWN_EXECUTION_PHASES.map((p) => executionPhaseLabel(p).text);
    expect(rotulosConhecidos).not.toContain(label.text);
  });

  it('fase desconhecida não vira string vazia nem some', () => {
    expect(executionPhaseLabel('').text).toBe('');
    expect(executionPhaseLabel('X').text).toBe('X');
    expect(executionPhaseLabel('X').unknown).toBe(true);
  });
});

describe('rótulos de indexação', () => {
  it('cobre os quatro resultados e as seis fases', () => {
    expect(KNOWN_INDEXING_OUTCOMES).toEqual([
      'Indexed',
      'RetryScheduled',
      'Failed',
      'Discarded',
    ]);
    expect(KNOWN_INDEXING_PHASES).toHaveLength(6);
  });

  it.each(KNOWN_INDEXING_OUTCOMES)('o resultado %s tem rótulo', (outcome) => {
    expect(indexingOutcomeLabel(outcome).unknown).toBe(false);
  });

  it.each(KNOWN_INDEXING_PHASES)('a fase %s tem rótulo', (phase) => {
    expect(indexingPhaseLabel(phase).unknown).toBe(false);
  });

  it('a linha junta resultado e fase quando a fase existe', () => {
    const label = indexingFailureLabel('Failed', 'EmbeddingGateway');

    expect(label.unknown).toBe(false);
    expect(label.text).toBe('Falhou · Chamada ao gateway de embedding');
  });

  it('fase nula devolve só o resultado', () => {
    // `Discarded` não tem fase — e a linha não pode virar "Descartado · null".
    const label = indexingFailureLabel('Discarded', null);

    expect(label.text).toBe('Descartado');
    expect(label.text).not.toContain('null');
  });

  it('qualquer metade desconhecida marca a linha inteira como desconhecida', () => {
    expect(indexingFailureLabel('Failed', 'FaseNova').unknown).toBe(true);
    expect(indexingFailureLabel('ResultadoNovo', 'Chunking').unknown).toBe(true);
  });
});
