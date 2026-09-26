import { describe, expect, it } from 'vitest';
import {
  KNOWN_CAVEAT_CODES,
  caveatLabel,
  caveatsFor,
  unknownCaveats,
} from './caveatLabels';
import { errorsFixture, performanceFixture } from '../test/systemInsightsFixture';
import {
  agentDelegationFixture,
  agentErrorsFixture,
  agentPerformanceFixture,
  agentTokensFixture,
} from '../test/agentInsightsFixture';

const DA_ROTA_SISTEMA = [...performanceFixture().caveats, ...errorsFixture().caveats];

const DA_ROTA_AGENTE = [
  ...agentTokensFixture().caveats,
  ...agentPerformanceFixture().caveats,
  ...agentErrorsFixture().caveats,
  ...agentDelegationFixture().caveats,
];

describe('caveatLabels', () => {
  it('o mapa de texto é FECHADO nos seis códigos das duas rotas', () => {
    expect([...KNOWN_CAVEAT_CODES].sort()).toEqual([
      'delegation-sides-are-not-mirrors',
      'embedding-covers-search-only',
      'point-in-time-only',
      'rejections-missing-from-executions',
      'residual-is-not-only-tools',
      'submitted-at-missing-on-redelivery',
    ]);
  });

  it('rejection-reason-not-collected NÃO está mais no mapa', () => {
    // Ele caiu dos DOIS handlers de `apps/api` com a change
    // `recusa-motivo-coleta` (#51), e os testes de lá afirmam a ausência dele.
    // Um mapa "fechado" que declara um código que a rota não emite deixa de ser
    // fechado em nada.
    expect(KNOWN_CAVEAT_CODES).not.toContain('rejection-reason-not-collected');
    expect(caveatLabel('rejection-reason-not-collected').placement).toBe('unknown');
  });

  it.each(KNOWN_CAVEAT_CODES)('%s tem texto de operador, não o código cru', (code) => {
    const label = caveatLabel(code);

    expect(label.placement).not.toBe('unknown');
    expect(label.text).not.toBe(code);
    expect(label.text.length).toBeGreaterThan(20);
  });

  it('o texto é o MESMO nas duas superfícies', () => {
    // O que o código diz sobre a MEDIÇÃO não muda de tela para tela. Só a
    // posição muda.
    for (const code of KNOWN_CAVEAT_CODES) {
      expect(caveatLabel(code, 'agent').text).toBe(caveatLabel(code, 'system').text);
    }
  });

  it('código desconhecido aparece CRU, e não some, nas duas superfícies', () => {
    for (const surface of ['system', 'agent'] as const) {
      const label = caveatLabel('codigo-que-ainda-nao-existe', surface);
      expect(label.placement).toBe('unknown');
      expect(label.text).toBe('codigo-que-ainda-nao-existe');
    }
  });

  it('o desconhecido não é confundido com nenhum conhecido', () => {
    const textosConhecidos = KNOWN_CAVEAT_CODES.map((c) => caveatLabel(c).text);

    expect(textosConhecidos).not.toContain(caveatLabel('outro-codigo').text);
  });
});

describe('a superfície da PÁGINA DO SISTEMA', () => {
  it('cobre exatamente os códigos que os dois blocos da rota real trazem', () => {
    // A rota entrega 2 em `performance` e 2 em `errors` — conferido no corpo
    // real em 26/09, contra o HEAD. Se um quinto aparecer, ele cai no caminho
    // do desconhecido, visível.
    expect(DA_ROTA_SISTEMA).toHaveLength(4);
    expect(unknownCaveats(DA_ROTA_SISTEMA)).toEqual([]);
  });

  it('cada código aponta para a posição da D12 da change da página', () => {
    expect(caveatLabel('submitted-at-missing-on-redelivery').placement).toBe('task-duration');
    expect(caveatLabel('rejections-missing-from-executions').placement).toBe('rejection-count');
    expect(caveatLabel('point-in-time-only').placement).toBe('non-terminal');
  });

  it('residual-is-not-only-tools é SEM POSIÇÃO aqui', () => {
    // O resíduo (M25) não tem elemento no `Main.dc.html`, e um texto de
    // limitação sem o número que ele limita não tem o que qualificar. A
    // classificação fica escrita para que ninguém a leia como esquecimento.
    const label = caveatLabel('residual-is-not-only-tools', 'system');

    expect(label.placement).toBe('not-on-this-page');
    expect(label.placement).not.toBe('unknown');
    expect(label.text).toContain('residual');
  });

  it('a posição "não desta página" não é devolvida para nenhum lugar de render', () => {
    for (const posicao of ['task-duration', 'rejection-count', 'non-terminal'] as const) {
      expect(caveatsFor(DA_ROTA_SISTEMA, posicao).map((c) => c.code)).not.toContain(
        'residual-is-not-only-tools',
      );
    }
  });

  it('devolve só os códigos daquela posição', () => {
    expect(caveatsFor(DA_ROTA_SISTEMA, 'task-duration').map((c) => c.code)).toEqual([
      'submitted-at-missing-on-redelivery',
    ]);
    expect(caveatsFor(DA_ROTA_SISTEMA, 'non-terminal').map((c) => c.code)).toEqual([
      'point-in-time-only',
    ]);
  });

  it('o desconhecido sai por unknownCaveats, e o conhecido não', () => {
    const comNovo = [...DA_ROTA_SISTEMA, 'codigo-novo'];

    expect(unknownCaveats(comNovo).map((c) => c.code)).toEqual(['codigo-novo']);
  });
});

describe('a superfície da ABA DO AGENTE', () => {
  it('cobre exatamente os seis códigos que os quatro blocos da rota trazem', () => {
    // 1 em `tokens`, 2 em `performance`, 2 em `errors`, 1 em `delegation` —
    // conferido no corpo real em 26/09. Dois blocos a mais que na página do
    // sistema.
    expect(DA_ROTA_AGENTE).toHaveLength(6);
    expect(unknownCaveats(DA_ROTA_AGENTE, 'agent')).toEqual([]);
  });

  it('cada código aponta para a posição da D11', () => {
    expect(caveatLabel('submitted-at-missing-on-redelivery', 'agent').placement).toBe(
      'task-duration',
    );
    expect(caveatLabel('residual-is-not-only-tools', 'agent').placement).toBe('residual');
    expect(caveatLabel('rejections-missing-from-executions', 'agent').placement).toBe(
      'rejection-count',
    );
    expect(caveatLabel('delegation-sides-are-not-mirrors', 'agent').placement).toBe(
      'delegation-sides',
    );
    expect(caveatLabel('point-in-time-only', 'agent').placement).toBe('not-on-this-page');
    expect(caveatLabel('embedding-covers-search-only', 'agent').placement).toBe(
      'not-on-this-page',
    );
  });

  it('DOIS códigos são SEM POSIÇÃO nesta aba, e estão classificados', () => {
    const semPosicao = KNOWN_CAVEAT_CODES.filter(
      (c) => caveatLabel(c, 'agent').placement === 'not-on-this-page',
    );

    // Nenhum dos dois é descartado em silêncio. O dia em que o número que cada
    // um qualifica entrar na aba, este caso falha e aponta o lugar.
    expect([...semPosicao].sort()).toEqual([
      'embedding-covers-search-only',
      'point-in-time-only',
    ]);
  });

  it('O MESMO CÓDIGO TEM POSIÇÃO DIFERENTE NAS DUAS SUPERFÍCIES', () => {
    // É este caso que justifica a posição ser por superfície em vez de um mapa
    // só. Os dois sentidos, porque um mapa único mentiria nos dois:
    //
    //   - o resíduo NÃO é desenhado na página do sistema e É na aba;
    //   - o instantâneo É desenhado na página do sistema e NÃO é na aba.
    expect(caveatLabel('residual-is-not-only-tools', 'system').placement).toBe(
      'not-on-this-page',
    );
    expect(caveatLabel('residual-is-not-only-tools', 'agent').placement).toBe('residual');

    expect(caveatLabel('point-in-time-only', 'system').placement).toBe('non-terminal');
    expect(caveatLabel('point-in-time-only', 'agent').placement).toBe('not-on-this-page');
  });

  it('as posições de render da aba cobrem tudo menos os dois sem posição', () => {
    const renderizados = (
      ['task-duration', 'rejection-count', 'residual', 'delegation-sides'] as const
    ).flatMap((p) => caveatsFor(DA_ROTA_AGENTE, p, 'agent').map((c) => c.code));

    expect(renderizados).toHaveLength(4);
    expect([...renderizados].sort()).toEqual(
      DA_ROTA_AGENTE.filter(
        (c) => caveatLabel(c, 'agent').placement !== 'not-on-this-page',
      ).sort(),
    );
  });

  it('a superfície errada devolve a posição errada — o padrão não vale na aba', () => {
    // Guarda de acoplamento: chamar sem a superfície dentro de um componente da
    // aba daria a posição da página do sistema, e o resíduo sumiria em silêncio.
    expect(caveatsFor(DA_ROTA_AGENTE, 'residual')).toEqual([]);
    expect(caveatsFor(DA_ROTA_AGENTE, 'residual', 'agent')).toHaveLength(1);
  });
});
