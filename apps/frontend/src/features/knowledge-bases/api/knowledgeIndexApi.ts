import { request } from './knowledgeBasesApi';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

// MÓDULO PRÓPRIO, E NÃO UMA FUNÇÃO A MAIS EM `knowledgeBasesApi`.
//
// `request<T>` vem do módulo de bases, da MESMA feature — não de um cliente HTTP
// compartilhado entre features (convenção 7), exatamente como
// `knowledgeDocumentsApi` faz. O que muda aqui é o RECURSO:
// `/knowledge-index/diagnostics` não é rota de base, e a feature se organiza por
// conceito de domínio, não por origem do dado.
//
// E há um motivo medido para o módulo ser separado, além da forma: seis arquivos
// mockam `knowledgeBasesApi` PARCIALMENTE (`vi.mock` com `importOriginal`), dois
// deles em outra feature. Toda função nova daquele módulo que alguma rota da
// árvore chame precisa entrar no override de cada um — a que faltar escapa para a
// rede, e quem reprova é o teste SEGUINTE, renderizando a tela de login. Já
// aconteceu quando `listKnowledgeBaseIndexingSummary` nasceu. Módulo novo deixa
// os seis intocados (design.md, D1).
export function listKnowledgeIndexDiagnostics(): Promise<KnowledgeIndexProvenance[]> {
  return request<KnowledgeIndexProvenance[]>('/knowledge-index/diagnostics');
}
