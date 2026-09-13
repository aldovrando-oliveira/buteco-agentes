import { describe, expect, it } from 'vitest';
import {
  STATUS_ALL,
  STATUS_FAILED,
  STATUS_INACTIVE,
  documentCountLabel,
  effectiveStatusFilter,
  hasFailure,
  indexingParts,
  summaryById,
  summaryFor,
} from './indexingSummary';
import type { KnowledgeBaseIndexingSummary } from '../types/knowledgeBase';

const cobranca = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const produtos = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function summary(
  overrides: Partial<KnowledgeBaseIndexingSummary> = {},
): KnowledgeBaseIndexingSummary {
  return {
    knowledgeBaseId: cobranca,
    documentCount: 5,
    indexedCount: 3,
    failedCount: 1,
    ...overrides,
  };
}

function labels(item: KnowledgeBaseIndexingSummary | undefined): string[] {
  return (indexingParts(item) ?? []).map((part) => part.label);
}

describe('summaryById', () => {
  it('indexa os itens por identificador de base', () => {
    const byId = summaryById([summary(), summary({ knowledgeBaseId: produtos })]);

    expect(summaryFor(byId, produtos)?.knowledgeBaseId).toBe(produtos);
  });

  // O par sem item: resumo indisponível é `undefined`, e não um mapa vazio. A
  // diferença decide se a tela diz "não sei" ou "sei que não tem".
  it('devolve indefinido quando não há resumo', () => {
    expect(summaryById(undefined)).toBeUndefined();
    expect(summaryFor(undefined, cobranca)).toBeUndefined();
  });

  it('devolve indefinido para base que o resumo não trouxe', () => {
    const byId = summaryById([summary({ knowledgeBaseId: produtos })]);

    expect(summaryFor(byId, cobranca)).toBeUndefined();
  });
});

describe('documentCountLabel', () => {
  it('conta no plural', () => {
    expect(documentCountLabel(summary({ documentCount: 4 }))).toBe('4 documentos');
  });

  it('conta no singular', () => {
    expect(documentCountLabel(summary({ documentCount: 1 }))).toBe('1 documento');
  });

  // Zero MEDIDO é exibível: a agregação percorreu os documentos e não achou
  // nenhum. É o oposto do zero de `fragmentCount` em documento nunca indexado.
  it('exibe Nenhum quando a contagem medida é zero', () => {
    expect(documentCountLabel(summary({ documentCount: 0, indexedCount: 0, failedCount: 0 }))).toBe(
      'Nenhum',
    );
  });

  // Asserção negativa: sem item NÃO é zero. Quem renderiza traduz `null` em `—`.
  it('devolve null quando não há item, e nunca Nenhum', () => {
    expect(documentCountLabel(undefined)).toBeNull();
    expect(documentCountLabel(undefined)).not.toBe('Nenhum');
  });
});

describe('indexingParts', () => {
  it('soma indexados, em andamento e falhas', () => {
    expect(labels(summary({ documentCount: 5, indexedCount: 3, failedCount: 1 }))).toEqual([
      '3 indexados',
      '1 em andamento',
      '1 falhou',
    ]);
  });

  it('omite as parcelas zeradas', () => {
    expect(labels(summary({ documentCount: 2, indexedCount: 2, failedCount: 0 }))).toEqual([
      '2 indexados',
    ]);
  });

  it('pluraliza a parcela de falha', () => {
    expect(labels(summary({ documentCount: 3, indexedCount: 0, failedCount: 3 }))).toEqual([
      '3 falharam',
    ]);
  });

  // Base sem documento: lista VAZIA, não `null`. Sei que não há o que indexar, e
  // a coluna vizinha já diz `Nenhum` — repetir aqui seria a redundância que o
  // protótipo tem em três células da mesma linha.
  it('devolve lista vazia quando a base não tem documento', () => {
    expect(indexingParts(summary({ documentCount: 0, indexedCount: 0, failedCount: 0 }))).toEqual(
      [],
    );
  });

  // O par sem item, e a distinção que ele protege: vazio ≠ desconhecido.
  it('devolve null quando não há item', () => {
    expect(indexingParts(undefined)).toBeNull();
  });

  it('nunca produz parcela negativa', () => {
    expect(labels(summary({ documentCount: 1, indexedCount: 2, failedCount: 3 }))).toEqual([
      '2 indexados',
      '3 falharam',
    ]);
  });

  // A REGRA DE D5, NA FORMA NEGATIVA.
  //
  // Este é o guarda de DENTRO. Ele protege a regra da subtração e o rótulo
  // escolhido, e NÃO protege a tela: quem monta as duas parcelas direto na célula
  // sem tocar esta função passa por ele verde. O guarda que reprova contra essa
  // reintrodução é o de DOM, em KnowledgeBaseTable.test.tsx (design.md, D5).
  it('nunca emprega pendente nem indexando, em nenhum arranjo de contagens', () => {
    const arranjos = [
      summary({ documentCount: 5, indexedCount: 1, failedCount: 1 }),
      summary({ documentCount: 3, indexedCount: 0, failedCount: 0 }),
      summary({ documentCount: 9, indexedCount: 4, failedCount: 2 }),
      summary({ documentCount: 1, indexedCount: 0, failedCount: 0 }),
    ];

    for (const arranjo of arranjos) {
      const texto = labels(arranjo).join(' · ');
      expect(texto).not.toMatch(/pendente/i);
      expect(texto).not.toMatch(/indexando/i);
    }
  });

  it('reúne o complemento não terminal numa única parcela', () => {
    // 4 não terminais poderiam ser 1 pendente + 3 indexando; o dado não sabe, e
    // a tela não inventa.
    expect(labels(summary({ documentCount: 6, indexedCount: 1, failedCount: 1 }))).toEqual([
      '1 indexado',
      '4 em andamento',
      '1 falhou',
    ]);
  });

  it('marca como falha apenas a parcela de falha', () => {
    const parts = indexingParts(summary({ documentCount: 5, indexedCount: 3, failedCount: 1 }));

    expect(parts?.filter((part) => part.isFailure).map((part) => part.label)).toEqual(['1 falhou']);
  });
});

describe('hasFailure', () => {
  it('é verdadeiro quando há documento em falha', () => {
    expect(hasFailure(summary({ failedCount: 2 }))).toBe(true);
  });

  it('é falso quando não há falha', () => {
    expect(hasFailure(summary({ failedCount: 0 }))).toBe(false);
  });

  it('é falso quando não há item', () => {
    expect(hasFailure(undefined)).toBe(false);
  });
});

describe('effectiveStatusFilter', () => {
  it('preserva o filtro escolhido quando o resumo está disponível', () => {
    expect(effectiveStatusFilter(STATUS_FAILED, true)).toBe(STATUS_FAILED);
  });

  // Sem resumo, filtrar por falha esvaziaria a listagem — e listagem vazia
  // afirmaria que nenhuma base tem falha, que é o que a consulta sem resposta não
  // permite dizer.
  it('cai para todas quando o filtro é falha e o resumo não está disponível', () => {
    expect(effectiveStatusFilter(STATUS_FAILED, false)).toBe(STATUS_ALL);
  });

  it('não mexe nos demais filtros quando o resumo não está disponível', () => {
    expect(effectiveStatusFilter(STATUS_INACTIVE, false)).toBe(STATUS_INACTIVE);
  });
});
