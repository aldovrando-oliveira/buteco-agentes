import type { Agent } from '../../agents/types/agent';

export interface AgentServerUsage {
  agent: Agent;
  allowedTools: string[];
}

// A API não tem consulta inversa: não existe GET /mcp-servers/{id}/agents.
// Tudo aqui é derivado de GET /agents, percorrendo os vínculos de cada
// agente. Módulo puro de propósito — três telas consomem estas mesmas
// regras (a coluna da listagem, a visão inversa do detalhe e o diálogo de
// desativação), e é este o ponto único a trocar quando o backend ganhar o
// endpoint inverso (Decision 1 do design.md da change
// frontend-mcp-servidor-uso-e-diagnostico).

export function agentsUsingServer(agents: Agent[], mcpServerId: string): AgentServerUsage[] {
  return agents.flatMap((agent) => {
    const binding = agent.mcpServers.find((mcpServer) => mcpServer.id === mcpServerId);
    return binding ? [{ agent, allowedTools: binding.allowedTools }] : [];
  });
}

export interface ServerUsageSummary {
  agentCount: number;
  agentsWithoutToolsCount: number;
}

export function serverUsageSummary(agents: Agent[], mcpServerId: string): ServerUsageSummary {
  const usages = agentsUsingServer(agents, mcpServerId);
  return {
    agentCount: usages.length,
    agentsWithoutToolsCount: usages.filter((usage) => usage.allowedTools.length === 0).length,
  };
}

export function agentsAllowingTool(
  agents: Agent[],
  mcpServerId: string,
  toolName: string,
): number {
  return agentsUsingServer(agents, mcpServerId).filter((usage) =>
    usage.allowedTools.includes(toolName),
  ).length;
}
