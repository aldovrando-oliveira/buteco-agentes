// Espelha KnowledgeDocumentEventResponse e KnowledgeDocumentEventPageResponse de
// apps/api (KnowledgeDocuments/Responses/), entregues pela #98
// (historico-documentos-base). Lido no código, não na proposta:
//
// - `type` vai para o fio como STRING (KnowledgeDocumentEventType tem
//   JsonStringEnumConverter), e KnowledgeDocumentCatalogTests.cs:653-674 afirma
//   "Created"/"Updated"/"Deleted" sobre o texto da resposta (convenção 12). A
//   união abaixo é a do contrato de hoje; os rótulos de utils/documentHistory.ts
//   aceitam `string` para o valor que um apps/api mais novo mandar (D3).
// - `contentChanged` e `titleChanged` são nulos em Created e Deleted, e não nulos
//   com pelo menos um verdadeiro em Updated — a CHECK do banco garante (D5 da #98).
// - `documentTitle` é SNAPSHOT: o título depois da escrita; na exclusão, o que o
//   documento tinha. Não há FK para o documento, e o evento sobrevive a ele.
// - `author` é o subject do token, verbatim (`operator`, `service:connectors`);
//   o rótulo é do painel (D2).
export type KnowledgeDocumentEventType = 'Created' | 'Updated' | 'Deleted';

export interface KnowledgeDocumentEvent {
  id: string;
  documentId: string;
  documentTitle: string;
  type: KnowledgeDocumentEventType;
  contentChanged: boolean | null;
  titleChanged: boolean | null;
  author: string;
  occurredAt: string;
}

// `nextCursor` é nulo EXATAMENTE na última página: o apps/api busca 51 linhas
// para devolver 50 e saber se há próxima (D7 da #98). O cursor é opaco.
export interface KnowledgeDocumentEventPage {
  items: KnowledgeDocumentEvent[];
  nextCursor: string | null;
}
