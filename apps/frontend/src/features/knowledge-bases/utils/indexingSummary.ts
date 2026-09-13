import type { KnowledgeBaseIndexingSummary } from '../types/knowledgeBase';

// AS DUAS COLUNAS TÊM TRÊS ESTADOS, E O TRAVESSÃO NUNCA SIGNIFICA ZERO.
//
// Esta é a gramática que a tabela e a página compartilham, e é a que alguém vai
// "consertar" ao ver uma célula vazia numa listagem:
//
//   valor   — há linha de resumo para esta base: mostra a contagem.
//   vazio   — há linha de resumo, e não há o que dizer. É o caso da base sem
//             documento na coluna `Indexação`: não há nada para indexar, e a
//             coluna vizinha já diz `Nenhum`.
//   `—`     — NÃO há linha de resumo. A consulta não respondeu, ou o resumo não
//             trouxe esta base.
//
// O terceiro estado é o que importa. A API garante uma linha para CADA base do
// catálogo, mas são DUAS requisições independentes: uma base criada entre elas
// aparece no catálogo e não no resumo. Zerar ali afirmaria uma contagem que
// ninguém fez — é o mesmo defeito de `0 fragmentos` na tela vizinha, e a
// convenção 13 o proíbe.
//
// A mesma distinção já existe na tela: `ConsultedByCell` separa `—`
// (indisponível) de `Nenhum agente` (zero conhecido), e `fragmentCountLabel`
// devolve `null` para "deixe a célula vazia — nunca escreve zero, nunca escreve
// travessão no lugar de um número que existiria".
//
// Devolver `null` daqui significa SEMPRE "não sei"; quem renderiza traduz em `—`.

// Cruzamento POR IDENTIFICADOR, nunca por posição (design.md, D6).
//
// O resumo vem ordenado pelo mesmo critério do catálogo, com o mesmo desempate —
// e o código não pode depender disso. São duas respostas de duas requisições
// independentes, e "as duas ordens coincidem" é uma propriedade verdadeira hoje
// que nenhuma das duas specs promete ao consumidor; o comentário do próprio
// handler em apps/api diz "o consumidor casa por id, nunca por posição".
//
// Não existe caminho posicional neste módulo, e é de propósito: o `Map` é a
// única porta de entrada do resumo na tela.
export function summaryById(
  summary: KnowledgeBaseIndexingSummary[] | undefined,
): Map<string, KnowledgeBaseIndexingSummary> | undefined {
  if (!summary) {
    return undefined;
  }

  return new Map(summary.map((item) => [item.knowledgeBaseId, item]));
}

export function summaryFor(
  byId: Map<string, KnowledgeBaseIndexingSummary> | undefined,
  knowledgeBaseId: string,
): KnowledgeBaseIndexingSummary | undefined {
  return byId?.get(knowledgeBaseId);
}

// `null` é dado desconhecido; `'Nenhum'` é contagem medida que deu zero. Os dois
// são textos diferentes porque são fatos diferentes — ver o comentário de
// KnowledgeBaseIndexingSummary sobre os dois zeros.
export function documentCountLabel(item: KnowledgeBaseIndexingSummary | undefined): string | null {
  if (!item) {
    return null;
  }

  if (item.documentCount === 0) {
    return 'Nenhum';
  }

  return item.documentCount === 1 ? '1 documento' : `${item.documentCount} documentos`;
}

export interface IndexingPart {
  readonly label: string;
  // Só a parcela de falha carrega tom de alerta. Documento em andamento é o
  // funcionamento normal do pipeline, e pintá-lo de aviso — como o protótipo faz,
  // com a célula inteira em `--wa` — afirmaria que a base precisa de atenção num
  // estado que se resolve sozinho (design.md, D10).
  readonly isFailure: boolean;
}

// PENDENTE E INDEXANDO VIRAM UMA PARCELA SÓ, `em andamento`.
//
// O recurso devolve TRÊS contagens — documentCount, indexedCount, failedCount —
// e não distingue `Pending` de `Indexing`. Não é lacuna: a spec viva de
// knowledge-document-indexing diz que "o consumidor que precisa distingui-los é o
// detalhe da base, que já recebe o estado por documento na listagem de
// documentos. O total não terminal permanece exato por subtração."
//
// Escrever `pendente` ou `indexando` aqui afirmaria uma separação que o dado não
// carrega. O protótipo escreve as duas (`1 indexado · 1 indexando · 1 pendente`),
// e é por isso que a asserção que protege esta regra é NEGATIVA — e mora no DOM,
// em KnowledgeBaseTable.test.tsx, não só aqui: o modo de falha real é alguém
// montar as duas parcelas direto na célula, sem tocar esta função (convenção 15,
// guarda no componente certo).
//
// Devolve `null` quando não há item (não sei) e lista VAZIA quando a base não tem
// documento (sei, e não há o que dizer).
export function indexingParts(
  item: KnowledgeBaseIndexingSummary | undefined,
): IndexingPart[] | null {
  if (!item) {
    return null;
  }

  if (item.documentCount === 0) {
    return [];
  }

  // Math.max defensivo: as três contagens saem de um único GROUP BY sobre as
  // mesmas linhas, então o complemento não tem como ficar negativo hoje. A
  // proteção existe para que um dia em que isso deixe de valer produza uma
  // parcela omitida, e nunca `-1 em andamento` na tela do operador.
  const inProgress = Math.max(0, item.documentCount - item.indexedCount - item.failedCount);

  const parts: IndexingPart[] = [];

  if (item.indexedCount > 0) {
    parts.push({
      label: item.indexedCount === 1 ? '1 indexado' : `${item.indexedCount} indexados`,
      isFailure: false,
    });
  }

  if (inProgress > 0) {
    parts.push({ label: `${inProgress} em andamento`, isFailure: false });
  }

  if (item.failedCount > 0) {
    parts.push({
      label: item.failedCount === 1 ? '1 falhou' : `${item.failedCount} falharam`,
      isFailure: true,
    });
  }

  return parts;
}

// Sem item devolve `false`, e isso é deliberado: o predicado é usado pelo filtro,
// e a ausência do resumo não é evidência de falha. Quem trata a indisponibilidade
// é `effectiveStatusFilter`, que impede a listagem de esvaziar.
export function hasFailure(item: KnowledgeBaseIndexingSummary | undefined): boolean {
  return item !== undefined && item.failedCount > 0;
}

export const STATUS_ALL = 'todas';
export const STATUS_ACTIVE = 'ativas';
export const STATUS_INACTIVE = 'inativas';
export const STATUS_FAILED = 'com-falha';

export type StatusFilter =
  typeof STATUS_ALL | typeof STATUS_ACTIVE | typeof STATUS_INACTIVE | typeof STATUS_FAILED;

// O filtro de falha só significa alguma coisa com o resumo em mãos. Sem ele, a
// opção fica DESABILITADA no controle — e esta função cobre o caso de borda que o
// `disabled` sozinho não cobre: o operador seleciona `Com falha`, o resumo é
// invalidado, e a nova consulta falha.
//
// Nesse caminho o filtro resolve para `todas`. Filtrar afirmaria que nenhuma base
// tem falha, que é exatamente o que uma consulta sem resposta não permite dizer —
// e oferecer a opção vazia ou inerte é o que a etapa 5a-1 já recusou em D9.
export function effectiveStatusFilter(
  selected: StatusFilter,
  summaryAvailable: boolean,
): StatusFilter {
  if (selected === STATUS_FAILED && !summaryAvailable) {
    return STATUS_ALL;
  }

  return selected;
}
