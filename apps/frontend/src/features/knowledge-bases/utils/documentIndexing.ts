import type { KnowledgeDocumentSummary, KnowledgeIndexingStatus } from '../types/knowledgeDocument';

// A CONTAGEM DE FRAGMENTOS É GOVERNADA POR `indexedAt`, NUNCA PELO ESTADO.
//
// Esta é a regra que alguém vai "consertar" ao ver uma célula vazia numa tabela,
// e o protótipo do handoff já a errou — ele faz
// `st === 'failed' ? '0 fragmentos' : '—'`, ou seja zera no estado que falhou.
// Zerar afirmaria que a indexação rodou e não achou nada. A causa está no
// backend, e é por isso que ela mora aqui, comentada, e não espalhada na tabela:
//
//   - KnowledgeIndexingService.FailAsync grava SÓ IndexingStatus e
//     FailureReason. Não toca IndexedAt nem FragmentCount, e a exclusão dos
//     fragmentos antigos acontece apenas dentro de CommitAsync, DEPOIS de a
//     atualização condicional ter afetado uma linha. Ou seja: documento que já
//     esteve indexado e falhou depois continua com os fragmentos ANTIGOS VIVOS
//     no índice, respondendo às consultas do agente (garantia 3 de D9 da
//     etapa 1).
//   - KnowledgeDocument.RequestReindex() e Update() também preservam os dois
//     campos, pelo mesmo motivo.
//
// Logo, `Pending`, `Indexing` e `Failed` podem TODOS ter contagem legítima a
// exibir — a anterior —, e o que distingue "nunca indexado" de "há conteúdo
// indexado respondendo agora" é `indexedAt`, exatamente como o comentário de
// KnowledgeDocument.FragmentCount declara como contrato desde a etapa 1.
//
// Devolve `null` quando não há contagem a exibir. Quem renderiza deixa a célula
// vazia — nunca escreve zero, nunca escreve travessão no lugar de um número que
// existiria.
export function fragmentCountLabel(document: KnowledgeDocumentSummary): string | null {
  if (document.indexedAt === null) {
    return null;
  }

  return document.fragmentCount === 1 ? '1 fragmento' : `${document.fragmentCount} fragmentos`;
}

// A MESMA REGRA, APLICADA À SOMA DA BASE INTEIRA — e o protótipo erra aqui de
// novo, por dois caminhos.
//
// A aba de diagnóstico do índice exibe "fragmentos desta base" e "documentos com
// fragmentos no índice". O protótipo calcula o primeiro com
// `d.reduce((n, x) => n + (x.status === 'indexed' ? x.chunks : 0), 0)`, o que
// SUBCONTA o índice real pelo mecanismo já descrito acima: documento que indexou
// e falhou ao reindexar continua com os fragmentos antigos vivos, respondendo às
// consultas do agente, e some da soma. A tela informaria menos fragmentos do que
// o índice tem.
//
// O predicado certo é `indexedAt !== null`, QUALQUER QUE SEJA O ESTADO, e é o que
// a spec viva de knowledge-document-indexing já diz sobre a contagem de
// fragmentos.
//
// SEGUNDO CAMINHO, registrado porque quem consertar só o `reduce` não o fecha: no
// mock do protótipo, `reindex` e a atualização de conteúdo gravam `chunks: 0` na
// hora, enquanto o backend PRESERVA a contagem. No painel real esse caminho não
// existe — a contagem vem da resposta da API —, e é por isso que nenhuma
// atualização otimista daqui pode espelhar o comportamento do mock.
//
// O rótulo da contagem de documentos NÃO reusa a palavra `Indexado`, que é o
// badge de estado da tabela vizinha: com este predicado, um documento exibido
// como `Falhou` conta aqui, e duas telas usando a mesma palavra com predicados
// diferentes é a confusão que essa regra já custou uma vez.
function hasFragmentsInIndex(document: KnowledgeDocumentSummary): boolean {
  return document.indexedAt !== null;
}

export function indexedFragmentTotal(documents: KnowledgeDocumentSummary[] | undefined): number {
  return (documents ?? [])
    .filter(hasFragmentsInIndex)
    .reduce((total, document) => total + document.fragmentCount, 0);
}

export function documentsWithFragmentsCount(
  documents: KnowledgeDocumentSummary[] | undefined,
): number {
  return (documents ?? []).filter(hasFragmentsInIndex).length;
}

// Estados terminais são aqueles em que o pipeline parou. `Pending` e `Indexing`
// são os dois estados não-terminais do ciclo — a reindexação passa pelos dois
// com fragmentos antigos vivos, e é por isso que não existe valor de enum
// separado para ela.
const NON_TERMINAL: readonly KnowledgeIndexingStatus[] = ['Pending', 'Indexing'];

export function isNonTerminal(document: KnowledgeDocumentSummary): boolean {
  return NON_TERMINAL.includes(document.indexingStatus);
}

// A condição do polling mora aqui, e não numa expressão embutida no hook, por
// dois motivos: ela é testável sem timer, e a faixa de resumo da tela usa a
// mesma pergunta (design.md, D3).
export function hasNonTerminalDocument(documents: KnowledgeDocumentSummary[] | undefined): boolean {
  return (documents ?? []).some(isNonTerminal);
}

export function nonTerminalCount(documents: KnowledgeDocumentSummary[] | undefined): number {
  return (documents ?? []).filter(isNonTerminal).length;
}

// Rótulo e tom por estado. O tom é nome de cor semântica do Mantine, que resolve
// por esquema sozinha — nenhum tom fixo da escala neutra, nenhuma variável nova
// (design.md, D9).
//
// Valor desconhecido cai no indicador NEUTRO, e nunca reaproveita o de sucesso
// (convenção 13): um enum que cresça do outro lado não pode virar "Indexado" por
// omissão.
const STATUS_PRESENTATION: Record<KnowledgeIndexingStatus, { label: string; color: string }> = {
  Pending: { label: 'Pendente', color: 'gray' },
  Indexing: { label: 'Indexando', color: 'blue' },
  Indexed: { label: 'Indexado', color: 'green' },
  Failed: { label: 'Falhou', color: 'red' },
};

export function statusPresentation(status: KnowledgeIndexingStatus): {
  label: string;
  color: string;
} {
  return STATUS_PRESENTATION[status] ?? { label: 'Desconhecido', color: 'gray' };
}

// Nota do documento sob o título. `Pending` e `Indexing` dizem o que está
// acontecendo; os terminais dizem quando o documento foi atualizado.
export function documentNote(document: KnowledgeDocumentSummary, updatedAtLabel: string): string {
  if (document.indexingStatus === 'Pending') {
    return 'na fila de indexação';
  }

  if (document.indexingStatus === 'Indexing') {
    return 'gerando embeddings';
  }

  return `atualizado em ${updatedAtLabel}`;
}
