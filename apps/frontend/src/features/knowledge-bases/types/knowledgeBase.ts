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
