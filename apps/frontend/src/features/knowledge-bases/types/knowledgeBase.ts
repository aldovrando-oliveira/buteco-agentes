// Espelha KnowledgeBaseResponse de apps/api, que tem exatamente estes seis
// campos.
//
// `description` é `string`, e não `string | null`: a API a exige não vazia na
// criação E na edição (ValidateShape em KnowledgeBaseEndpoints), e o campo do
// response não é anulável. Base sem descrição é estado inalcançável, então a
// UI não tem caminho para ele (design.md, D7).
//
// Não é texto decorativo: a partir da etapa de execução é o texto que vira a
// descrição da tool exposta ao modelo, e é por ele que o modelo decide se esta
// base é relevante para a pergunta.
export interface KnowledgeBase {
  id: string;
  name: string;
  description: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateKnowledgeBaseInput {
  name: string;
  description: string;
}

export type UpdateKnowledgeBaseInput = CreateKnowledgeBaseInput;

// Espelha KnowledgeBaseIndexingSummaryResponse de apps/api, servido por
// GET /knowledge-bases/indexing-summary. Quatro campos, todos obrigatórios: o
// recurso devolve uma linha para CADA base do catálogo, inclusive a sem
// documento nenhum e a inativa.
//
// Nomes conferidos no fio, não deduzidos da política camelCase: o teste
// SummaryResponse_UsesCamelCaseFieldNamesOnTheWire inspeciona o TEXTO do JSON da
// resposta real e afirma `knowledgeBaseId`, `documentCount`, `indexedCount` e
// `failedCount`, mais a negativa de que nenhuma variante de caixa aparece.
// `knowledgeBaseId` é o nome que mais pedia esse guarda — termina em sigla, e a
// política camelCase minúscula só a primeira letra.
//
// POR QUE O ZERO DE `documentCount` PODE SER EXIBIDO, e o de `fragmentCount`
// não. Os dois são zero e significam coisas opostas:
//
//   - `documentCount: 0` é uma contagem MEDIDA. A agregação percorreu os
//     documentos daquela base e não encontrou nenhum, e a projeção final do
//     handler é sobre as BASES (não sobre os grupos do GroupBy) exatamente para
//     que a base vazia apareça com zeros em vez de sumir. "Nenhum" na tela é
//     verdade.
//   - `fragmentCount: 0` em documento nunca indexado é o DEFAULT de uma coluna
//     que ninguém escreveu, e por isso `fragmentCountLabel` o omite. Exibi-lo
//     afirmaria que a indexação rodou e não achou nada.
//
// A docstring de KnowledgeBaseIndexingSummaryResponse nomeia os dois e termina
// com "Não confundir os dois zeros". Esta é a frase que impede alguém de
// "consertar" a exibição do zero desta contagem (design.md, D4).
export interface KnowledgeBaseIndexingSummary {
  knowledgeBaseId: string;
  documentCount: number;
  indexedCount: number;
  failedCount: number;
}
