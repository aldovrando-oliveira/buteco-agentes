import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';

// Uma linha da aba Conhecimento: o cruzamento entre um id vinculado e o
// registro da base no catálogo.
//
// A descrição e o estado NÃO vêm do vínculo: AgentResponse.knowledgeBases é
// KnowledgeBaseSummaryResponse, que é id + name e nada mais — decisão registrada
// no próprio record da API, que manda a tela de vínculo tirar o estado do
// catálogo (GET /knowledge-bases, que ela já busca para montar o modal).
// Ver design.md, D2.
export interface KnowledgeBaseRow {
  id: string;
  name: string;
  description: string;
  isActive: boolean;
}

// Ordena por nome com desempate por id — exatamente o critério de
// AgentKnowledgeBaseLookup e das agregações em lote de ListAgentsQueryHandler.
//
// Não é preciosismo: nome de base não é único por requisito, e reproduzir a
// ordem do servidor é o que faz a lista NÃO se remontar quando o PUT volta.
// Uma base recém-escolhida ocupa, no rascunho, a mesma posição que vai ocupar
// depois de gravada (design.md, D4).
function byNameThenId(left: KnowledgeBaseRow, right: KnowledgeBaseRow): number {
  const byName = left.name.localeCompare(right.name);
  return byName !== 0 ? byName : left.id.localeCompare(right.id);
}

/**
 * Monta as linhas das bases vinculadas a partir dos ids do rascunho e do
 * catálogo completo.
 *
 * Id sem correspondência no catálogo é descartado em silêncio, sem linha e sem
 * erro: KnowledgeBase não tem rota de exclusão e o catálogo devolve inativas,
 * então base vinculada ausente do catálogo é estado inalcançável — não vale
 * construir tela de erro para ele.
 */
export function knowledgeBaseRows(
  knowledgeBaseIds: string[],
  catalog: KnowledgeBase[],
): KnowledgeBaseRow[] {
  const byId = new Map(catalog.map((knowledgeBase) => [knowledgeBase.id, knowledgeBase]));

  return knowledgeBaseIds
    .map((id) => byId.get(id))
    .filter((knowledgeBase): knowledgeBase is KnowledgeBase => knowledgeBase !== undefined)
    .map(({ id, name, description, isActive }) => ({ id, name, description, isActive }))
    .sort(byNameThenId);
}
