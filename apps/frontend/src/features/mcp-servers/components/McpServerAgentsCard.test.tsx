import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { McpServerAgentsCard } from './McpServerAgentsCard';
import type { Agent } from '../../agents/types/agent';

const MCP_SERVER_ID = 'srv-1';

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
    a2a: null,
    ...overrides,
  };
}

function renderCard(agents?: Agent[]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <McpServerAgentsCard mcpServerId={MCP_SERVER_ID} agents={agents} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('McpServerAgentsCard', () => {
  it('lista cada agente que usa o servidor, com link, tools permitidas e estado', () => {
    renderCard([
      agent({
        id: 'a1',
        name: 'Atendente',
        mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: ['read', 'write'] }],
      }),
      agent({
        id: 'a2',
        name: 'Cobrança',
        isActive: false,
        mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: ['read'] }],
      }),
    ]);

    // A contagem saiu de dentro do rótulo e virou informação ao lado dele: o
    // rótulo nomeia a seção, a contagem informa o estado dela.
    expect(screen.getByText('Agentes que usam este servidor')).toBeInTheDocument();
    expect(screen.getByText('2 agentes usam este servidor MCP')).toBeInTheDocument();

    const first = within(screen.getByTestId('server-agent-row-a1'));
    expect(first.getByRole('link', { name: 'Atendente' })).toHaveAttribute('href', '/agents/a1');
    expect(first.getByText('read')).toBeInTheDocument();
    expect(first.getByText('write')).toBeInTheDocument();
    expect(first.getByText('Ativo')).toBeInTheDocument();

    const second = within(screen.getByTestId('server-agent-row-a2'));
    expect(second.getByText('Inativo')).toBeInTheDocument();
  });

  it('sinaliza o agente vinculado sem nenhuma tool', () => {
    renderCard([
      agent({ id: 'a1', mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: [] }] }),
    ]);

    const row = within(screen.getByTestId('server-agent-row-a1'));
    expect(row.getByText('Vinculado sem tools')).toBeInTheDocument();
  });

  it('usa o singular quando apenas um agente usa o servidor', () => {
    renderCard([agent({ mcpServers: [{ id: MCP_SERVER_ID, name: 'x', allowedTools: ['read'] }] })]);

    expect(screen.getByText('1 agente usa este servidor MCP')).toBeInTheDocument();
  });

  it('informa que desativar não afeta ninguém quando nenhum agente usa o servidor', () => {
    renderCard([agent({ mcpServers: [{ id: 'outro', name: 'y', allowedTools: ['read'] }] })]);

    expect(
      screen.getByText(
        'Nenhum agente usa este servidor. Desativá-lo não afeta nenhum agente agora.',
      ),
    ).toBeInTheDocument();
  });

  it('indica quando não foi possível carregar a informação de uso', () => {
    renderCard(undefined);

    expect(
      screen.getByText('Não foi possível carregar a informação de uso deste servidor.'),
    ).toBeInTheDocument();
  });
});
