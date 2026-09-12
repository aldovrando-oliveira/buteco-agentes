import { request } from './knowledgeBasesApi';
import type {
  CreateKnowledgeDocumentInput,
  KnowledgeDocument,
  KnowledgeDocumentSummary,
  UpdateKnowledgeDocumentInput,
} from '../types/knowledgeDocument';

// `request<T>`/`ApiError` vêm do módulo de bases, da MESMA feature — não de um
// cliente HTTP compartilhado entre features (convenção 7, D12 da 5a-1). As rotas
// de documento são aninhadas em /knowledge-bases/{id}/documents e pertencem a
// esta feature por conceito de domínio.

const documentsPath = (knowledgeBaseId: string) => `/knowledge-bases/${knowledgeBaseId}/documents`;

export function listKnowledgeDocuments(
  knowledgeBaseId: string,
): Promise<KnowledgeDocumentSummary[]> {
  return request<KnowledgeDocumentSummary[]>(documentsPath(knowledgeBaseId));
}

// Único lugar que traz `extractedText`. O modal de atualizar depende dele para
// carregar o conteúdo atual e para comparar contra o editado (design.md, D5).
export function getKnowledgeDocument(
  knowledgeBaseId: string,
  id: string,
): Promise<KnowledgeDocument> {
  return request<KnowledgeDocument>(`${documentsPath(knowledgeBaseId)}/${id}`);
}

// Corpo JSON com o conteúdo como string, e NÃO multipart/form-data: markdown e
// texto são texto, o cliente os lê e envia aqui dentro. É a D3 da etapa 1
// ("Zero multipart, com gatilho registrado"); multipart obrigaria a mexer no
// Content-Type fixo deste request<T>, mais IFormFile, mais validação de tipo
// binário, mais limite separado. O gatilho para ele nascer é o primeiro tipo de
// origem binário (PDF), junto com o extrator que precisa dos bytes.
export function createKnowledgeDocument(
  knowledgeBaseId: string,
  input: CreateKnowledgeDocumentInput,
): Promise<KnowledgeDocument> {
  return request<KnowledgeDocument>(documentsPath(knowledgeBaseId), {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export function updateKnowledgeDocument(
  knowledgeBaseId: string,
  id: string,
  input: UpdateKnowledgeDocumentInput,
): Promise<KnowledgeDocument> {
  return request<KnowledgeDocument>(`${documentsPath(knowledgeBaseId)}/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

// 204 sem corpo; o request<T> devolve undefined nesse caso.
export function deleteKnowledgeDocument(knowledgeBaseId: string, id: string): Promise<void> {
  return request<void>(`${documentsPath(knowledgeBaseId)}/${id}`, { method: 'DELETE' });
}

// Responde 200 com o documento já no estado novo, e não 202: a mudança de estado
// que a resposta descreve — Pending, contadores limpos — aconteceu de forma
// síncrona e completa. A tela re-renderiza a linha a partir dela, sem uma
// segunda leitura.
export function reindexKnowledgeDocument(
  knowledgeBaseId: string,
  id: string,
): Promise<KnowledgeDocument> {
  return request<KnowledgeDocument>(`${documentsPath(knowledgeBaseId)}/${id}/reindex`, {
    method: 'POST',
  });
}
