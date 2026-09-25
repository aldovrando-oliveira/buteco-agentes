import { describe, expect, it } from 'vitest';
import {
  EM_DASH,
  formatDurationMs,
  formatRatio,
  formatTokens,
  ratio,
  readMetric,
  sumKnown,
} from './metricState';

// OS GUARDAS DESTE ARQUIVO SÃO NEGATIVOS (convenção 13).
//
// "Não há `0` onde a origem é nula" é o que precisa ser verdade, e é diferente
// de "renderiza vazio": o segundo passaria se alguém trocasse o vazio por um
// travessão, por um "N/D" ou por qualquer outra coisa — inclusive por algo
// errado. O primeiro só passa enquanto o zero estiver ausente.

describe('readMetric — os quatro estados', () => {
  it('número vira valor formatado', () => {
    expect(readMetric(476, 'ok')).toEqual({ state: 'value', text: '476' });
  });

  it('zero medido vira o estado de zero, e NUNCA o de vazio', () => {
    const leitura = readMetric(0, 'ok');

    expect(leitura.state).toBe('zero');
    expect(leitura.state).not.toBe('empty');
    expect(leitura.text).toBe('0');
  });

  it('nulo NÃO produz "0"', () => {
    const leitura = readMetric(null, 'ok');

    expect(leitura.text).not.toBe('0');
    expect(leitura.text).not.toContain('0');
    expect(leitura.state).toBe('empty');
  });

  it('indefinido NÃO produz "0"', () => {
    const leitura = readMetric(undefined, 'ok');

    expect(leitura.text).not.toBe('0');
    expect(leitura.text).not.toContain('0');
    expect(leitura.state).toBe('empty');
  });

  it('consulta sem resposta vira travessão, e NÃO zero', () => {
    const leitura = readMetric(null, 'failed');

    expect(leitura.state).toBe('unknown');
    expect(leitura.text).toBe(EM_DASH);
    expect(leitura.text).not.toContain('0');
  });

  it('consulta sem resposta vence o valor residual', () => {
    // Um número que sobrou de uma resposta anterior não pode ser exibido como
    // se a consulta atual o tivesse trazido.
    expect(readMetric(476, 'failed').state).toBe('unknown');
    expect(readMetric(0, 'failed').state).toBe('unknown');
  });

  it('NaN e Infinity não passam por número', () => {
    // Nenhum dos dois deve escapar como valor: são sintoma de divisão feita
    // fora deste módulo, e exibi-los afirmaria um número que ninguém mediu.
    expect(readMetric(Number.NaN, 'ok').state).toBe('empty');
    expect(readMetric(Number.POSITIVE_INFINITY, 'ok').state).toBe('empty');
  });

  it('o formatador nunca recebe nulo', () => {
    const chamadas: unknown[] = [];
    const espiao = (n: number) => {
      chamadas.push(n);
      return String(n);
    };

    readMetric(null, 'ok', espiao);
    readMetric(undefined, 'ok', espiao);
    readMetric(null, 'failed', espiao);
    readMetric(0, 'ok', espiao);

    // Nem no nulo, nem no indefinido, nem no zero — só no valor de verdade.
    expect(chamadas).toEqual([]);
    readMetric(7, 'ok', espiao);
    expect(chamadas).toEqual([7]);
  });
});

describe('sumKnown — soma que preserva o nulo', () => {
  it('todas as parcelas nulas NÃO produzem 0', () => {
    const total = sumKnown([null, null, undefined]);

    expect(total).toBeNull();
    expect(total).not.toBe(0);
    expect(readMetric(total, 'ok').text).not.toContain('0');
  });

  it('soma só as parcelas conhecidas', () => {
    expect(sumKnown([9_600_000, null, 2_800_000])).toBe(12_400_000);
  });

  it('parcelas que são zero medido somam zero medido', () => {
    // Diferente do caso acima: aqui alguém CONTOU, e contou zero.
    const total = sumKnown([0, 0]);

    expect(total).toBe(0);
    expect(readMetric(total, 'ok').state).toBe('zero');
  });
});

describe('ratio — percentual com denominador guardado', () => {
  it('denominador zero NÃO produz "0%" nem NaN', () => {
    const r = ratio(0, 0);

    expect(r).toBeNull();
    expect(r).not.toBe(0);
    expect(Number.isNaN(r as number)).toBe(false);
    expect(readMetric(r, 'ok').text).not.toContain('0');
  });

  it('numerador zero sobre denominador medido é zero medido', () => {
    expect(ratio(0, 476)).toBe(0);
  });

  it('denominador nulo não vira percentual', () => {
    expect(ratio(12, null)).toBeNull();
  });

  it('a razão é a fração, não o valor já multiplicado', () => {
    expect(ratio(12, 400)).toBe(0.03);
    expect(formatRatio(0.03)).toBe('3%');
  });
});

describe('formatação em pt-BR', () => {
  it('abrevia tokens como o protótipo', () => {
    expect(formatTokens(12_400_000)).toBe('12,4 M');
    expect(formatTokens(341_200)).toBe('341,2 mil');
    expect(formatTokens(563)).toBe('563');
  });

  it('duração sai em segundos', () => {
    expect(formatDurationMs(12_400)).toBe('12,4 s');
  });

  it('zero formatado continua sendo zero', () => {
    // O zero medido não é abreviado nem enfeitado: é `0`, e é por isso que ele
    // é distinguível do vazio na tela.
    expect(readMetric(0, 'ok', formatTokens).text).toBe('0');
  });
});
