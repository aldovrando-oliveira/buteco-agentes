import { describe, expect, it } from 'vitest';
import {
  documentNote,
  documentsWithFragmentsCount,
  fragmentCountLabel,
  hasNonTerminalDocument,
  indexedFragmentTotal,
  isNonTerminal,
  nonTerminalCount,
  statusPresentation,
} from './documentIndexing';
import type { KnowledgeDocumentSummary, KnowledgeIndexingStatus } from '../types/knowledgeDocument';

function doc(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: 'd1',
    knowledgeBaseId: 'k1',
    title: 'Documento',
    sourceType: 'markdown',
    contentLengthBytes: 1000,
    indexingStatus: 'Indexed',
    indexedAt: '2026-09-02T03:14:00Z',
    failureReason: null,
    contentRevision: 1,
    fragmentCount: 14,
    indexingAttempts: 1,
    lastAttemptAt: '2026-09-02T03:14:00Z',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-02T00:00:00Z',
    ...overrides,
  };
}

describe('fragmentCountLabel', () => {
  it('exibe a contagem em documento indexado', () => {
    expect(fragmentCountLabel(doc({ indexingStatus: 'Indexed', fragmentCount: 14 }))).toBe(
      '14 fragmentos',
    );
  });

  it('usa o singular com um fragmento só', () => {
    expect(fragmentCountLabel(doc({ fragmentCount: 1 }))).toBe('1 fragmento');
  });

  it('omite a contagem em documento novo em Pending', () => {
    expect(
      fragmentCountLabel(doc({ indexingStatus: 'Pending', indexedAt: null, fragmentCount: 0 })),
    ).toBeNull();
  });

  it('omite a contagem em documento novo em Indexing', () => {
    expect(
      fragmentCountLabel(doc({ indexingStatus: 'Indexing', indexedAt: null, fragmentCount: 0 })),
    ).toBeNull();
  });

  // O caso exato que o protótipo erra: ele escreve '0 fragmentos' no estado que
  // falhou. Aqui a contagem é omitida, porque `indexedAt` é nulo — nunca
  // indexado, então não há contagem nenhuma a afirmar.
  it('omite a contagem em documento que falhou sem nunca ter sido indexado', () => {
    expect(
      fragmentCountLabel(doc({ indexingStatus: 'Failed', indexedAt: null, fragmentCount: 0 })),
    ).toBeNull();
  });

  // ASSERÇÃO NEGATIVA (convenção 13/15), e o ESCOPO dela é a parte que importa.
  //
  // A proibição é de zero com `indexedAt` NULO — o default de uma coluna que
  // ninguém escreveu. Exibi-lo afirmaria que a indexação rodou e não achou nada,
  // que é o defeito do protótipo (`st === 'failed' ? '0 fragmentos' : '—'`).
  //
  // Não é proibição de zero em geral, e a diferença é a mesma que a docstring de
  // KnowledgeBaseIndexingSummaryResponse manda não confundir: zero MEDIDO pode
  // ser exibido, zero por omissão não. Com `indexedAt` preenchido, a indexação
  // rodou e a contagem é medida — e, de todo modo, o par não é alcançável:
  // KnowledgeIndexingService recusa gravar Indexed com zero fragmentos
  // (`fragments.Count == 0` vira FailAsync), e FailAsync preserva a contagem
  // anterior, que é maior que zero. Uma asserção sobre esse par estaria fixando
  // comportamento de um estado que o sistema não produz.
  it('nunca produz a cadeia "0 fragmentos" quando indexedAt é nulo, em nenhum estado', () => {
    const estados: KnowledgeIndexingStatus[] = ['Pending', 'Indexing', 'Indexed', 'Failed'];

    const rotulos = estados.map((indexingStatus) =>
      fragmentCountLabel(doc({ indexingStatus, indexedAt: null, fragmentCount: 0 })),
    );

    expect(rotulos).not.toContain('0 fragmentos');
    expect(rotulos.every((r) => r === null)).toBe(true);
  });

  // As três formas de "já esteve indexado". Os fragmentos antigos continuam
  // vivos no índice nos três casos, então a contagem anterior é verdade.
  it('exibe a contagem anterior em documento que falhou depois de indexado', () => {
    expect(
      fragmentCountLabel(
        doc({ indexingStatus: 'Failed', indexedAt: '2026-09-02T03:14:00Z', fragmentCount: 9 }),
      ),
    ).toBe('9 fragmentos');
  });

  it('exibe a contagem anterior em documento reindexado em Pending', () => {
    expect(
      fragmentCountLabel(
        doc({ indexingStatus: 'Pending', indexedAt: '2026-09-02T03:14:00Z', fragmentCount: 5 }),
      ),
    ).toBe('5 fragmentos');
  });

  it('exibe a contagem anterior em documento reindexando', () => {
    expect(
      fragmentCountLabel(
        doc({ indexingStatus: 'Indexing', indexedAt: '2026-09-02T03:14:00Z', fragmentCount: 11 }),
      ),
    ).toBe('11 fragmentos');
  });
});

describe('isNonTerminal / hasNonTerminalDocument / nonTerminalCount', () => {
  it('Pending e Indexing são não-terminais; Indexed e Failed são terminais', () => {
    expect(isNonTerminal(doc({ indexingStatus: 'Pending' }))).toBe(true);
    expect(isNonTerminal(doc({ indexingStatus: 'Indexing' }))).toBe(true);
    expect(isNonTerminal(doc({ indexingStatus: 'Indexed' }))).toBe(false);
    expect(isNonTerminal(doc({ indexingStatus: 'Failed' }))).toBe(false);
  });

  it('detecta documento não-terminal na lista', () => {
    expect(
      hasNonTerminalDocument([
        doc({ id: 'a', indexingStatus: 'Indexed' }),
        doc({ id: 'b', indexingStatus: 'Pending', indexedAt: null }),
      ]),
    ).toBe(true);
  });

  it('lista só com terminais não tem documento não-terminal', () => {
    expect(
      hasNonTerminalDocument([
        doc({ id: 'a', indexingStatus: 'Indexed' }),
        doc({ id: 'b', indexingStatus: 'Failed' }),
      ]),
    ).toBe(false);
  });

  it('lista vazia e indefinida não têm documento não-terminal', () => {
    expect(hasNonTerminalDocument([])).toBe(false);
    expect(hasNonTerminalDocument(undefined)).toBe(false);
  });

  it('conta os não-terminais', () => {
    expect(
      nonTerminalCount([
        doc({ id: 'a', indexingStatus: 'Indexed' }),
        doc({ id: 'b', indexingStatus: 'Pending', indexedAt: null }),
        doc({ id: 'c', indexingStatus: 'Indexing', indexedAt: null }),
      ]),
    ).toBe(2);
  });
});

describe('statusPresentation', () => {
  it('os quatro estados têm rótulos distintos entre si', () => {
    const rotulos = (['Pending', 'Indexing', 'Indexed', 'Failed'] as KnowledgeIndexingStatus[]).map(
      (s) => statusPresentation(s).label,
    );

    expect(rotulos).toEqual(['Pendente', 'Indexando', 'Indexado', 'Falhou']);
    expect(new Set(rotulos).size).toBe(4);
  });

  // Convenção 13: valor desconhecido renderiza indicador neutro, jamais
  // reaproveita o de sucesso.
  it('valor desconhecido cai no indicador neutro, nunca no de sucesso', () => {
    const desconhecido = statusPresentation('Archived' as KnowledgeIndexingStatus);

    expect(desconhecido.label).toBe('Desconhecido');
    expect(desconhecido.color).not.toBe(statusPresentation('Indexed').color);
  });
});

describe('documentNote', () => {
  it('Pending diz que está na fila', () => {
    expect(documentNote(doc({ indexingStatus: 'Pending' }), '06/09/2026')).toBe(
      'na fila de indexação',
    );
  });

  it('Indexing diz que está gerando embeddings', () => {
    expect(documentNote(doc({ indexingStatus: 'Indexing' }), '06/09/2026')).toBe(
      'gerando embeddings',
    );
  });

  it('estados terminais dizem quando foi atualizado', () => {
    expect(documentNote(doc({ indexingStatus: 'Indexed' }), '06/09/2026')).toBe(
      'atualizado em 06/09/2026',
    );
    expect(documentNote(doc({ indexingStatus: 'Failed' }), '01/09/2026')).toBe(
      'atualizado em 01/09/2026',
    );
  });
});

// AS DUAS SOMAS DA ABA DE DIAGNÓSTICO, governadas por `indexedAt` e nunca pelo
// estado. O caso que decide está no meio: documento que INDEXOU E FALHOU DEPOIS.
//
// Ele é o cenário que a semente do protótipo não consegue produzir — nenhum
// documento dela chega a esse estado —, e por isso o defeito da soma só apareceu
// lendo o código do mock contra a spec viva, nunca percorrendo a tela.
describe('indexedFragmentTotal', () => {
  it('soma os fragmentos dos documentos com indexedAt preenchido', () => {
    expect(
      indexedFragmentTotal([
        doc({ id: 'a', fragmentCount: 14 }),
        doc({ id: 'b', fragmentCount: 7 }),
      ]),
    ).toBe(21);
  });

  // O caso que reprova quando alguém filtra por `status === 'Indexed'`.
  it('conta o documento que indexou e falhou ao reindexar', () => {
    expect(
      indexedFragmentTotal([
        doc({ id: 'a', indexingStatus: 'Indexed', fragmentCount: 14 }),
        doc({
          id: 'b',
          indexingStatus: 'Failed',
          indexedAt: '2026-09-02T03:14:00Z',
          failureReason: 'O provedor devolveu 429.',
          fragmentCount: 9,
        }),
      ]),
    ).toBe(23);
  });

  // Reindexação em curso: os fragmentos antigos continuam vivos até o commit.
  it('conta o documento em reindexação, que ainda responde com os fragmentos antigos', () => {
    expect(
      indexedFragmentTotal([
        doc({ id: 'a', indexingStatus: 'Pending', fragmentCount: 5 }),
        doc({ id: 'b', indexingStatus: 'Indexing', fragmentCount: 6 }),
      ]),
    ).toBe(11);
  });

  it('ignora o documento nunca indexado', () => {
    expect(
      indexedFragmentTotal([
        doc({ id: 'a', fragmentCount: 14 }),
        doc({ id: 'b', indexingStatus: 'Pending', indexedAt: null, fragmentCount: 0 }),
        doc({ id: 'c', indexingStatus: 'Failed', indexedAt: null, fragmentCount: 0 }),
      ]),
    ).toBe(14);
  });

  it('devolve zero sem documento, e zero com a listagem ainda indefinida', () => {
    expect(indexedFragmentTotal([])).toBe(0);
    expect(indexedFragmentTotal(undefined)).toBe(0);
  });
});

describe('documentsWithFragmentsCount', () => {
  it('conta os documentos com indexedAt preenchido, qualquer que seja o estado', () => {
    expect(
      documentsWithFragmentsCount([
        doc({ id: 'a', indexingStatus: 'Indexed' }),
        doc({ id: 'b', indexingStatus: 'Failed', indexedAt: '2026-09-02T03:14:00Z' }),
        doc({ id: 'c', indexingStatus: 'Failed', indexedAt: null, fragmentCount: 0 }),
      ]),
    ).toBe(2);
  });

  it('devolve zero sem documento, e zero com a listagem ainda indefinida', () => {
    expect(documentsWithFragmentsCount([])).toBe(0);
    expect(documentsWithFragmentsCount(undefined)).toBe(0);
  });
});
