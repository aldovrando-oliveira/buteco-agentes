import { describe, expect, it } from 'vitest';
import {
  KNOWN_CAVEAT_CODES,
  caveatLabel,
  caveatsFor,
  unknownCaveats,
} from './caveatLabels';
import { errorsFixture, performanceFixture } from '../test/systemInsightsFixture';

describe('caveatLabels', () => {
  it('o mapa é FECHADO nos cinco códigos que a rota serve', () => {
    expect(KNOWN_CAVEAT_CODES).toEqual([
      'submitted-at-missing-on-redelivery',
      'residual-is-not-only-tools',
      'rejections-missing-from-executions',
      'rejection-reason-not-collected',
      'point-in-time-only',
    ]);
  });

  it('cobre exatamente os códigos que os dois blocos da rota real trazem', () => {
    // A rota entrega 2 em `performance` e 3 em `errors` — conferido no corpo
    // real. Se um sexto aparecer, ele cai no caminho do desconhecido, visível.
    const daRota = [...performanceFixture().caveats, ...errorsFixture().caveats];

    expect(daRota).toHaveLength(5);
    expect([...daRota].sort()).toEqual([...KNOWN_CAVEAT_CODES].sort());
  });

  it.each(KNOWN_CAVEAT_CODES)('%s tem texto de operador, não o código cru', (code) => {
    const label = caveatLabel(code);

    expect(label.placement).not.toBe('unknown');
    expect(label.text).not.toBe(code);
    expect(label.text.length).toBeGreaterThan(20);
  });

  it('cada código conhecido aponta para a posição da D12', () => {
    expect(caveatLabel('submitted-at-missing-on-redelivery').placement).toBe('task-duration');
    expect(caveatLabel('rejections-missing-from-executions').placement).toBe('rejection-count');
    // SEM POSIÇÃO: ele qualifica o MOTIVO da recusa, que saiu da tela quando as
    // lacunas em moldura foram removidas. A regra é da própria spec — código
    // cujo número não está na página não é renderizado.
    expect(caveatLabel('rejection-reason-not-collected').placement).toBe('not-on-this-page');
    expect(caveatLabel('point-in-time-only').placement).toBe('non-terminal');
  });

  it('DOIS códigos são classificados como SEM POSIÇÃO nesta página', () => {
    // Nenhum dos dois é descartado em silêncio: os dois têm texto e
    // classificação por escrito, e o dia em que o número que cada um qualifica
    // entrar na tela, estes casos falham e apontam o lugar.
    const semPosicao = KNOWN_CAVEAT_CODES.filter(
      (c) => caveatLabel(c).placement === 'not-on-this-page',
    );

    expect(semPosicao).toEqual([
      'residual-is-not-only-tools',
      'rejection-reason-not-collected',
    ]);
  });

  it('residual-is-not-only-tools é classificado como SEM POSIÇÃO nesta página', () => {
    // Não é omissão silenciosa: o resíduo (M25) não tem elemento no
    // `Main.dc.html`, e um texto de limitação sem o número que ele limita não
    // tem o que qualificar. A classificação fica escrita para que ninguém a
    // leia como esquecimento — e para que, no dia em que o resíduo ganhar card,
    // este caso falhe e aponte o lugar.
    const label = caveatLabel('residual-is-not-only-tools');

    expect(label.placement).toBe('not-on-this-page');
    expect(label.placement).not.toBe('unknown');
    // E ele tem texto: a classificação é sobre onde renderizar, não sobre
    // conhecer o código.
    expect(label.text).toContain('residual');
  });

  it('código desconhecido aparece CRU, e não some', () => {
    const label = caveatLabel('codigo-que-ainda-nao-existe');

    expect(label.placement).toBe('unknown');
    expect(label.text).toBe('codigo-que-ainda-nao-existe');
  });

  it('o desconhecido não é confundido com nenhum conhecido', () => {
    const textosConhecidos = KNOWN_CAVEAT_CODES.map((c) => caveatLabel(c).text);

    expect(textosConhecidos).not.toContain(caveatLabel('outro-codigo').text);
  });
});

describe('caveatsFor e unknownCaveats', () => {
  const daRota = [...performanceFixture().caveats, ...errorsFixture().caveats];

  it('devolve só os códigos daquela posição', () => {
    expect(caveatsFor(daRota, 'task-duration').map((c) => c.code)).toEqual([
      'submitted-at-missing-on-redelivery',
    ]);
    expect(caveatsFor(daRota, 'non-terminal').map((c) => c.code)).toEqual([
      'point-in-time-only',
    ]);
  });

  it('a posição "não desta página" não é devolvida para nenhum lugar de render', () => {
    // O guarda negativo: nenhuma das quatro posições reais recebe o resíduo.
    for (const posicao of ['task-duration', 'rejection-count', 'non-terminal'] as const) {
      expect(caveatsFor(daRota, posicao).map((c) => c.code)).not.toContain(
        'residual-is-not-only-tools',
      );
    }
  });

  it('o desconhecido sai por unknownCaveats, e o conhecido não', () => {
    const comNovo = [...daRota, 'codigo-novo'];

    expect(unknownCaveats(comNovo).map((c) => c.code)).toEqual(['codigo-novo']);
    expect(unknownCaveats(daRota)).toEqual([]);
  });

  it('as posições de render cobrem tudo menos os dois sem posição', () => {
    const renderizados = (
      ['task-duration', 'rejection-count', 'non-terminal'] as const
    ).flatMap((p) => caveatsFor(daRota, p).map((c) => c.code));

    expect(renderizados).toHaveLength(3);
    expect([...renderizados].sort()).toEqual(
      KNOWN_CAVEAT_CODES.filter(
        (c) => caveatLabel(c).placement !== 'not-on-this-page',
      ).sort(),
    );
  });
});
