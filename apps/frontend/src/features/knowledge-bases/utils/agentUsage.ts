import type { Agent } from '../../agents/types/agent';

// A API não tem consulta inversa: não existe GET /knowledge-bases/{id}/agents, e
// AgentKnowledgeBindingEndpoints registra que isso é decisão, não omissão — a
// visão "quem consulta esta base" é derivada no cliente a partir de GET /agents,
// como McpServerAgentsCard já faz para servidores MCP.
//
// Custa UMA requisição, não N: AgentResponse.KnowledgeBases já vem populado em
// cada agente da listagem (design.md, D2).
export function agentsConsultingBase(agents: Agent[], knowledgeBaseId: string): Agent[] {
  return agents.filter((agent) =>
    agent.knowledgeBases.some((knowledgeBase) => knowledgeBase.id === knowledgeBaseId),
  );
}
