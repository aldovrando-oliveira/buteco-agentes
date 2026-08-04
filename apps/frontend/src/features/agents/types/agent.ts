export interface AgentMcpServerSummary {
  id: string;
  name: string;
  allowedTools: string[];
}

export interface Agent {
  id: string;
  name: string;
  instructions: string;
  isActive: boolean;
  provider: string | null;
  model: string | null;
  createdAt: string;
  updatedAt: string;
  mcpServers: AgentMcpServerSummary[];
}

export interface AgentMcpServerBinding {
  mcpServerId: string;
  allowedTools: string[];
}

export interface CreateAgentInput {
  name: string;
  instructions: string;
  provider: string;
  model: string;
}

export type UpdateAgentInput = CreateAgentInput;

export interface ProviderCatalogEntry {
  id: string;
  models: string[];
}
