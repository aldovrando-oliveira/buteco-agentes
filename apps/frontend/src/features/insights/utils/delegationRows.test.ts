import { describe, expect, it } from 'vitest';
import { delegationSides, type DelegationCatalogAgent } from './delegationRows';
import {
  AGENT_ID,
  SOURCE_ID,
  TARGET_ID,
  agentDelegationFixture,
  divergentDelegationFixture,
  noDelegationFixture,
} from '../test/agentInsightsFixture';

const OUTRO_ID = '11111111-1111-1111-1111-111111111111';

function catalogo(
  // `Record`, e não `Partial<Record<…>>`: com o `Partial`, `Object.values`
  // devolve `(DelegationCatalogAgent | undefined)[]` e o build reprova. O
  // `--noEmit` avulso não pega, porque o `tsconfig.json` da raiz é só
  // referências — quem checa é `npm run build`.
  overrides: Record<string, DelegationCatalogAgent> = {},
): DelegationCatalogAgent[] {
  const base: Record<string, DelegationCatalogAgent> = {
    [AGENT_ID]: { id: AGENT_ID, name: 'Atendente', delegatesTo: [] },
    [TARGET_ID]: { id: TARGET_ID, name: 'Cobrança', delegatesTo: [] },
    [SOURCE_ID]: { id: SOURCE_ID, name: 'Triagem', delegatesTo: [] },
    [OUTRO_ID]: { id: OUTRO_ID, name: 'Financeiro', delegatesTo: [] },
  };
  return Object.values({ ...base, ...overrides });
}

describe('delegationSides', () => {
  describe('cadastro e uso são fatos diferentes', () => {
    it('vínculo cadastrado E ocioso vira linha com 0, e NÃO ausência de vínculo', () => {
      // O caso que o dado de dev já produz hoje: `Triagem` tem vínculo
      // cadastrado para `Gestor de Reservas` e a janela não tem nenhuma
      // delegação. Conferido contra a rota real em 26/09.
      const sides = delegationSides(
        noDelegationFixture(),
        AGENT_ID,
        [{ id: TARGET_ID, name: 'Cobrança' }],
        catalogo(),
      );

      expect(sides.delegatesTo).toHaveLength(1);
      expect(sides.delegatesTo[0]).toMatchObject({
        agentId: TARGET_ID,
        name: 'Cobrança',
        total: 0,
        registered: true,
      });
      expect(sides.delegatesTo[0].outcomes).toEqual([]);
      // A asserção NEGATIVA: com cadastro, a lista NÃO fica vazia — e lista
      // vazia é o que a tela desenha como tracejado.
      expect(sides.delegatesTo.length).not.toBe(0);
    });

    it('ausência de cadastro NÃO produz nenhum 0 — a lista fica vazia', () => {
      const sides = delegationSides(noDelegationFixture(), AGENT_ID, [], catalogo());

      // Lista vazia é o estado do tracejado. Emitir uma linha com `0` aqui
      // afirmaria contagem sobre uma relação que não existe.
      expect(sides.delegatesTo).toEqual([]);
      expect(sides.triggeredBy).toEqual([]);
      expect(sides.delegatesToMax).toBe(0);
      expect(sides.triggeredByMax).toBe(0);
    });

    it('ocorrência medida sem cadastro atual NÃO é omitida', () => {
      // Vínculo removido depois de ter sido usado. Omitir a linha apagaria
      // medição que aconteceu.
      const sides = delegationSides(
        agentDelegationFixture({
          delegatesTo: [{ targetAgentId: TARGET_ID, outcome: 'Completed', count: 9 }],
        }),
        AGENT_ID,
        [],
        catalogo(),
      );

      expect(sides.delegatesTo).toHaveLength(1);
      expect(sides.delegatesTo[0]).toMatchObject({
        agentId: TARGET_ID,
        total: 9,
        registered: false,
      });
    });

    it('o cadastro de ENTRADA é derivado do catálogo, sem rota nova', () => {
      // Ninguém executou nada por delegação, mas `Triagem` declara este agente
      // como destino: linha com `0`, não tracejado.
      const sides = delegationSides(
        noDelegationFixture(),
        AGENT_ID,
        [],
        catalogo({
          [SOURCE_ID]: {
            id: SOURCE_ID,
            name: 'Triagem',
            delegatesTo: [{ id: AGENT_ID, name: 'Atendente' }],
          },
        }),
      );

      expect(sides.triggeredBy).toEqual([
        { agentId: SOURCE_ID, name: 'Triagem', executedCount: 0, registered: true },
      ]);
    });

    it('o próprio agente não entra no lado de entrada por autodelegação no catálogo', () => {
      const sides = delegationSides(
        noDelegationFixture(),
        AGENT_ID,
        [],
        catalogo({
          [AGENT_ID]: {
            id: AGENT_ID,
            name: 'Atendente',
            delegatesTo: [{ id: AGENT_ID, name: 'Atendente' }],
          },
        }),
      );

      expect(sides.triggeredBy).toEqual([]);
    });
  });

  describe('a assimetria', () => {
    it('os dois lados divergem, e nenhum é corrigido pelo outro', () => {
      // 30 tentativas (24 concluídas + 6 nunca iniciadas) contra 24 execuções
      // do outro lado. `NotStarted` não cria task alguma no destino.
      const sides = delegationSides(
        divergentDelegationFixture(),
        AGENT_ID,
        [{ id: TARGET_ID, name: 'Cobrança' }],
        catalogo(),
      );

      expect(sides.delegatesTo[0].total).toBe(30);
      expect(sides.triggeredBy[0].executedCount).toBe(24);
      // A asserção AFIRMA a divergência. Uma que afirmasse igualdade reprovaria
      // o comportamento correto — é a forma que a convenção 15 pede aqui.
      expect(sides.delegatesTo[0].total).not.toBe(sides.triggeredBy[0].executedCount);
    });

    it('não existe campo que some os dois lados', () => {
      const sides = delegationSides(
        divergentDelegationFixture(),
        AGENT_ID,
        [],
        catalogo(),
      );

      // O guarda de FORMA: um total agregado apareceria na tela no dia em que
      // alguém achasse que "fica mais completo", e ele é a soma que a tela não
      // pode fazer.
      const valores = Object.values(sides as unknown as Record<string, unknown>);
      expect(valores).not.toContain(54);
      expect(Object.keys(sides).sort()).toEqual([
        'delegatesTo',
        'delegatesToMax',
        'triggeredBy',
        'triggeredByMax',
      ]);
    });

    it('agrupa os resultados por destino e discrimina cada um', () => {
      const sides = delegationSides(
        divergentDelegationFixture(),
        AGENT_ID,
        [],
        catalogo(),
      );

      expect(sides.delegatesTo).toHaveLength(1);
      expect(sides.delegatesTo[0].outcomes).toEqual([
        { outcome: 'Completed', count: 24 },
        { outcome: 'NotStarted', count: 6 },
      ]);
    });
  });

  describe('o catálogo', () => {
    it('sem catálogo, as linhas continuam todas — só o nome fica nulo', () => {
      const sides = delegationSides(
        divergentDelegationFixture(),
        AGENT_ID,
        [],
        undefined,
      );

      expect(sides.delegatesTo).toHaveLength(1);
      expect(sides.delegatesTo[0].name).toBeNull();
      expect(sides.delegatesTo[0].total).toBe(30);
      expect(sides.triggeredBy).toHaveLength(1);
      expect(sides.triggeredBy[0].name).toBeNull();
    });

    it('sem catálogo, o cadastro de saída ainda dá o nome', () => {
      // `agent.delegatesTo` do detalhe já traz id + nome, e ele chega antes do
      // catálogo em qualquer cenário.
      const sides = delegationSides(
        noDelegationFixture(),
        AGENT_ID,
        [{ id: TARGET_ID, name: 'Cobrança' }],
        undefined,
      );

      expect(sides.delegatesTo[0].name).toBe('Cobrança');
    });

    it('agente medido que o catálogo não conhece fica com nome nulo, não some', () => {
      const sides = delegationSides(
        agentDelegationFixture({
          triggeredBy: [{ sourceAgentId: 'ffffffff-ffff-ffff-ffff-ffffffffffff', executedCount: 4 }],
        }),
        AGENT_ID,
        [],
        catalogo(),
      );

      expect(sides.triggeredBy).toHaveLength(1);
      expect(sides.triggeredBy[0].name).toBeNull();
      expect(sides.triggeredBy[0].executedCount).toBe(4);
    });
  });

  describe('ordem', () => {
    it('contagem maior primeiro, e o zero por último', () => {
      const sides = delegationSides(
        agentDelegationFixture({
          delegatesTo: [
            { targetAgentId: OUTRO_ID, outcome: 'Completed', count: 14 },
            { targetAgentId: TARGET_ID, outcome: 'Completed', count: 27 },
          ],
        }),
        AGENT_ID,
        [
          { id: TARGET_ID, name: 'Cobrança' },
          { id: OUTRO_ID, name: 'Financeiro' },
          { id: SOURCE_ID, name: 'Triagem' },
        ],
        catalogo(),
      );

      expect(sides.delegatesTo.map((r) => [r.name, r.total])).toEqual([
        ['Cobrança', 27],
        ['Financeiro', 14],
        ['Triagem', 0],
      ]);
      expect(sides.delegatesToMax).toBe(27);
    });

    it('empate desempata por nome, e a ordem é estável entre chamadas', () => {
      const delegation = agentDelegationFixture({
        delegatesTo: [
          { targetAgentId: OUTRO_ID, outcome: 'Completed', count: 5 },
          { targetAgentId: TARGET_ID, outcome: 'Completed', count: 5 },
        ],
      });
      const primeira = delegationSides(delegation, AGENT_ID, [], catalogo());
      const segunda = delegationSides(delegation, AGENT_ID, [], catalogo());

      expect(primeira.delegatesTo.map((r) => r.name)).toEqual(['Cobrança', 'Financeiro']);
      expect(segunda.delegatesTo.map((r) => r.name)).toEqual(
        primeira.delegatesTo.map((r) => r.name),
      );
    });
  });
});
