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

// Endereços públicos A2A do agente, montados pelo servidor. Nulos quando a URL
// pública não está configurada lá — a ausência é caso legítimo, não erro, e o
// painel a exibe como configuração faltando.
//
// Não existe campo dizendo se A2A está habilitado: todo agente tem os dois
// endereços, sempre, e o que varia é aceitar trabalho, que é o isActive
// (design.md da change agente-enderecos-a2a, D3).
export interface AgentA2AAddresses {
  url: string;
  agentCardUrl: string;
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
  // Obrigatório, e não opcional como `a2a`: o campo está em AgentResponse desde
  // a change knowledge-base-vinculo-agente, que já foi implantada — não há
  // janela de migração a cobrir aqui. É `id + name` porque
  // KnowledgeBaseSummaryResponse é só isso, deliberadamente: o estado da base
  // sai do catálogo, não do vínculo.
  knowledgeBases: AgentSummaryReference[];
  // Opcional, e não só anulável: uma API que ainda não subiu com esta mudança
  // omite o campo, e aí ele chega como undefined, não null. É a janela de
  // migração — os dois lados implantam separado (design.md da change
  // agente-enderecos-a2a, Migration Plan).
  a2a?: AgentA2AAddresses | null;
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
