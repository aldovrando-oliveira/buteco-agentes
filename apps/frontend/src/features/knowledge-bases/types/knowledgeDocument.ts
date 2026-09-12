// Espelha KnowledgeDocumentSummaryResponse e KnowledgeDocumentResponse de
// apps/api. Documento pertence à feature `knowledge-bases` por conceito de
// domínio, não por origem da rota (convenção 7).
//
// `indexingStatus` é união de string literal porque o enum vai para o fio como
// STRING, nunca como ordinal: KnowledgeIndexingStatus tem
// [JsonConverter(typeof(JsonStringEnumConverter<...>))] e
// KnowledgeWireFormatTests inspeciona o JSON bruto para afirmar exatamente
// "Pending"/"Indexing"/"Indexed"/"Failed" (convenção 12). Decodificar por índice
// seria o defeito que aquela convenção nasceu para impedir.
export type KnowledgeIndexingStatus = 'Pending' | 'Indexing' | 'Indexed' | 'Failed';

// `contentHash` NÃO EXISTE em resposta nenhuma, e por isso não está aqui.
//
// A coluna existe em KnowledgeDocument e tem um propósito só — Update() não
// devolve o documento para Pending quando o conteúdo extraído é idêntico ao
// gravado —, mas nem KnowledgeDocumentResponse nem
// KnowledgeDocumentSummaryResponse a projetam, e KnowledgeWireFormatTests não a
// menciona. Conferido no fio, não deduzido.
//
// É o campo que alguém vai procurar ao implementar o aviso de atualização. Não
// está aqui porque não dá para buscá-lo: a comparação de conteúdo é local, do
// `extractedText` carregado contra o editado (design.md, D5).
export interface KnowledgeDocumentSummary {
  id: string;
  knowledgeBaseId: string;
  title: string;
  sourceType: string;
  contentLengthBytes: number;
  indexingStatus: KnowledgeIndexingStatus;
  // Nulo enquanto o documento nunca foi indexado com sucesso; preenchido a
  // partir daí e PRESERVADO por atualizações, reindexações e falhas. É este
  // campo, e não o estado, que governa a exibição da contagem de fragmentos —
  // ver utils/documentIndexing.ts.
  indexedAt: string | null;
  failureReason: string | null;
  contentRevision: number;
  fragmentCount: number;
  indexingAttempts: number;
  lastAttemptAt: string | null;
  createdAt: string;
  updatedAt: string;
}

// A listagem não traz `extractedText` de propósito (design.md D14 da etapa 1):
// uma base com 50 documentos de 80 KB devolveria 4 MB de texto que nenhuma tela
// usa. Quem precisa do conteúdo — o modal de atualizar — busca o documento
// inteiro por GET /{id}.
export interface KnowledgeDocument extends KnowledgeDocumentSummary {
  extractedText: string;
}

// `sourceType` é string aberta validada em runtime contra os extratores
// registrados em apps/api, não enum fechado. Hoje só existe `markdown`, e a tela
// o mostra num campo desabilitado para deixar visível que o campo existe e que
// não há escolha.
export interface CreateKnowledgeDocumentInput {
  title: string;
  sourceType: string;
  content: string;
}

export type UpdateKnowledgeDocumentInput = CreateKnowledgeDocumentInput;
