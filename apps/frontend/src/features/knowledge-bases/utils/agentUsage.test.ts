import { describe, expect, it } from 'vitest';
import { agentsConsultingBase } from './agentUsage';
import type { Agent } from '../../agents/types/agent';

const baseId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const outraBase = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function agent(overrides: Partial<Agent>): Agent {
  return {
    id: 'agent-1',
    name: 'Atendimento Financeiro',
    instructions: 'Você é um atendente.',
    isActive: true,
    provider: 'openai',
    model: 'gpt-5.6-sol',
    description: null,
    skills: [],
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    mcpServers: [],
    delegatesTo: [],
    knowledgeBases: [],
    a2a: null,
    ...overrides,
  };
}

describe('agentsConsultingBase', () => {
  it('devolve os agentes vinculados à base', () => {
    const vinculado = agent({ id: 'a1', knowledgeBases: [{ id: baseId, name: 'Cobrança' }] });
    const outro = agent({ id: 'a2', knowledgeBases: [{ id: outraBase, name: 'Cardápio' }] });

    expect(agentsConsultingBase([vinculado, outro], baseId)).toEqual([vinculado]);
  });

  it('inclui agentes inativos', () => {
    const inativo = agent({
      id: 'a1',
      isActive: false,
      knowledgeBases: [{ id: baseId, name: 'Cobrança' }],
    });

    expect(agentsConsultingBase([inativo], baseId)).toEqual([inativo]);
  });

  it('devolve vazio quando nenhum agente consulta a base', () => {
    expect(agentsConsultingBase([agent({ id: 'a1' })], baseId)).toEqual([]);
  });

  it('devolve vazio quando não há agente nenhum', () => {
    expect(agentsConsultingBase([], baseId)).toEqual([]);
  });

  it('encontra a base entre várias vinculadas ao mesmo agente', () => {
    const multi = agent({
      id: 'a1',
      knowledgeBases: [
        { id: outraBase, name: 'Cardápio' },
        { id: baseId, name: 'Cobrança' },
      ],
    });

    expect(agentsConsultingBase([multi], baseId)).toEqual([multi]);
  });
});
