import { describe, expect, it } from 'vitest';
import { agentsAllowingTool, agentsUsingServer, serverUsageSummary } from './agentUsage';
import type { Agent } from '../../agents/types/agent';

function agent(overrides: Partial<Agent>): Agent {
  return {
    id: 'agent-1',
    name: 'Atendente',
    instructions: 'Você é um atendente simpático.',
    isActive: true,
    provider: 'openai',
    model: 'gpt-5.6-sol',
    description: null,
    skills: [],
    createdAt: '2026-07-26T00:00:00Z',
    updatedAt: '2026-07-26T00:00:00Z',
    mcpServers: [],
    delegatesTo: [],
    ...overrides,
  };
}

const withTools = agent({
  id: 'agent-1',
  name: 'Atendente',
  mcpServers: [{ id: 'srv-1', name: 'Zendesk MCP', allowedTools: ['read', 'write'] }],
});

const withoutTools = agent({
  id: 'agent-2',
  name: 'Cobrança',
  mcpServers: [{ id: 'srv-1', name: 'Zendesk MCP', allowedTools: [] }],
});

const otherServer = agent({
  id: 'agent-3',
  name: 'Financeiro',
  mcpServers: [{ id: 'srv-2', name: 'Slack MCP', allowedTools: ['post'] }],
});

describe('agentsUsingServer', () => {
  it('devolve lista vazia quando nenhum agente usa o servidor', () => {
    expect(agentsUsingServer([otherServer], 'srv-1')).toEqual([]);
  });

  it('devolve lista vazia quando não há nenhum agente cadastrado', () => {
    expect(agentsUsingServer([], 'srv-1')).toEqual([]);
  });

  it('devolve o agente com as tools permitidas naquele vínculo', () => {
    const usages = agentsUsingServer([withTools], 'srv-1');

    expect(usages).toHaveLength(1);
    expect(usages[0].agent.name).toBe('Atendente');
    expect(usages[0].allowedTools).toEqual(['read', 'write']);
  });

  it('inclui agente vinculado sem nenhuma tool', () => {
    const usages = agentsUsingServer([withoutTools], 'srv-1');

    expect(usages).toHaveLength(1);
    expect(usages[0].allowedTools).toEqual([]);
  });

  it('reúne vários agentes do mesmo servidor e ignora vínculos com outros', () => {
    const usages = agentsUsingServer([withTools, withoutTools, otherServer], 'srv-1');

    expect(usages.map((usage) => usage.agent.name)).toEqual(['Atendente', 'Cobrança']);
  });
});

describe('serverUsageSummary', () => {
  it('conta zero agentes quando ninguém usa o servidor', () => {
    expect(serverUsageSummary([otherServer], 'srv-1')).toEqual({
      agentCount: 0,
      agentsWithoutToolsCount: 0,
    });
  });

  it('conta os agentes que usam e, entre eles, os que não têm nenhuma tool', () => {
    expect(serverUsageSummary([withTools, withoutTools, otherServer], 'srv-1')).toEqual({
      agentCount: 2,
      agentsWithoutToolsCount: 1,
    });
  });

  it('não conta como sem tools um agente vinculado a outro servidor sem tools', () => {
    const otherWithoutTools = agent({
      id: 'agent-4',
      mcpServers: [{ id: 'srv-2', name: 'Slack MCP', allowedTools: [] }],
    });

    expect(serverUsageSummary([withTools, otherWithoutTools], 'srv-1')).toEqual({
      agentCount: 1,
      agentsWithoutToolsCount: 0,
    });
  });
});

describe('agentsAllowingTool', () => {
  it('conta zero quando nenhum agente permite a tool', () => {
    expect(agentsAllowingTool([withTools, withoutTools], 'srv-1', 'delete')).toBe(0);
  });

  it('conta um agente que permite a tool', () => {
    expect(agentsAllowingTool([withTools, withoutTools], 'srv-1', 'write')).toBe(1);
  });

  it('conta vários agentes que permitem a mesma tool', () => {
    const another = agent({
      id: 'agent-5',
      mcpServers: [{ id: 'srv-1', name: 'Zendesk MCP', allowedTools: ['read'] }],
    });

    expect(agentsAllowingTool([withTools, another, withoutTools], 'srv-1', 'read')).toBe(2);
  });

  it('não conta tool de mesmo nome permitida em outro servidor', () => {
    const sameToolElsewhere = agent({
      id: 'agent-6',
      mcpServers: [{ id: 'srv-2', name: 'Slack MCP', allowedTools: ['read'] }],
    });

    expect(agentsAllowingTool([sameToolElsewhere], 'srv-1', 'read')).toBe(0);
  });
});
