export interface AgentMcpServerSummary {
  id: string;
  name: string;
  allowedTools: string[];
}

export interface AgentSummaryReference {
  id: string;
  name: string;
}

// Espelha SkillResponse/SkillRequest da API. A description não é
// decorativa: alimenta o AgentSkill.Description do Agent Card A2A —
// por isso o frontend carrega os dois campos, nunca só o nome
// (Decision 1 do design.md da change frontend-agente-description-skills).
export interface AgentSkill {
  name: string;
  description: string | null;
}

export interface Agent {
  id: string;
  name: string;
  instructions: string;
  isActive: boolean;
  provider: string | null;
  model: string | null;
  description: string | null;
  skills: AgentSkill[];
  createdAt: string;
  updatedAt: string;
  mcpServers: AgentMcpServerSummary[];
  delegatesTo: AgentSummaryReference[];
}

export interface AgentMcpServerBinding {
  mcpServerId: string;
  allowedTools: string[];
}

// description e skills são obrigatórios no tipo (não opcionais) de
// propósito: PUT /agents/{id} é substituição do recurso inteiro, e o
// servidor trata campo ausente como "limpar". Omitir os dois foi o que
// fazia a edição pelo painel apagar description/skills silenciosamente
// (Decision 2 do design.md).
export interface CreateAgentInput {
  name: string;
  instructions: string;
  provider: string;
  model: string;
  description: string | null;
  skills: AgentSkill[];
}

export type UpdateAgentInput = CreateAgentInput;

export interface ProviderCatalogEntry {
  id: string;
  models: string[];
}
