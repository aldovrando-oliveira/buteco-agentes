import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { AgentTable } from './AgentTable';
import type { Agent } from '../types/agent';

const activeAgent: Agent = {
  id: '44444444-4444-4444-4444-444444444444',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
};

const inactiveAgent: Agent = {
  id: '55555555-5555-5555-5555-555555555555',
  name: 'Vendedor',
  instructions: 'Você é um vendedor objetivo.',
  isActive: false,
  provider: 'anthropic',
  model: 'claude-opus-5',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
};

function renderTable(agents: Agent[]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <AgentTable agents={agents} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('AgentTable', () => {
  it('exibe o indicador de "Ativo" para um agente ativo', () => {
    renderTable([activeAgent]);

    expect(screen.getByText('Ativo')).toBeInTheDocument();
  });

  it('exibe indicadores visuais distintos para agente ativo e agente inativo', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByText('Inativo')).toBeInTheDocument();
    expect(screen.queryByText('Ativo')).not.toBe(screen.queryByText('Inativo'));
  });

  it('exibe provedor e modelo de cada agente em uma única coluna', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.getByText('openai / gpt-5.6-sol')).toBeInTheDocument();
    expect(screen.getByText('anthropic / claude-opus-5')).toBeInTheDocument();
  });

  it('identifica o modelo não configurado na coluna de provedor', () => {
    renderTable([{ ...activeAgent, provider: null, model: null }]);

    expect(screen.getByText('— / não configurado')).toBeInTheDocument();
  });

  it('exibe o indicador de "precisa de reconfiguração" apenas para o agente sem provider/model', () => {
    renderTable([activeAgent, { ...inactiveAgent, provider: null, model: null }]);

    expect(screen.getAllByText('Precisa de reconfiguração')).toHaveLength(1);
  });

  it('não exibe o indicador de "precisa de reconfiguração" quando todos os agentes têm provider/model', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.queryByText('Precisa de reconfiguração')).not.toBeInTheDocument();
  });

  it('exibe a descrição do agente junto do nome quando ela existe', () => {
    renderTable([{ ...activeAgent, description: 'Atende o financeiro' }]);

    expect(screen.getByText('Atende o financeiro')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: activeAgent.name })).toBeInTheDocument();
  });

  it('não exibe descrição quando o agente não tem uma', () => {
    renderTable([activeAgent]);

    expect(screen.getByRole('link', { name: activeAgent.name })).toBeInTheDocument();
    expect(screen.queryByText('Atende o financeiro')).not.toBeInTheDocument();
  });

  it('resume servidores e tools do agente na coluna de ferramentas', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [
          { id: 's1', name: 'Zendesk MCP', allowedTools: ['read', 'write'] },
          { id: 's2', name: 'Slack MCP', allowedTools: ['post'] },
        ],
      },
    ]);

    expect(screen.getByText('2 servidores · 3 tools')).toBeInTheDocument();
  });

  it('usa o singular com um servidor e uma tool', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [{ id: 's1', name: 'Zendesk MCP', allowedTools: ['read'] }],
      },
    ]);

    expect(screen.getByText('1 servidor · 1 tool')).toBeInTheDocument();
  });

  it('indica quando o agente não tem nenhum servidor vinculado', () => {
    renderTable([activeAgent]);

    expect(screen.getByText('Nenhum servidor')).toBeInTheDocument();
  });

  it('avisa quantos servidores estão vinculados sem nenhuma tool', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [
          { id: 's1', name: 'Zendesk MCP', allowedTools: [] },
          { id: 's2', name: 'Slack MCP', allowedTools: [] },
          { id: 's3', name: 'Github MCP', allowedTools: ['read'] },
        ],
      },
    ]);

    expect(screen.getByTestId(`agent-servers-without-tools-${activeAgent.id}`)).toHaveTextContent(
      '2 servidores sem tools',
    );
  });

  it('não avisa sobre servidores sem tools quando todos têm ao menos uma', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [{ id: 's1', name: 'Zendesk MCP', allowedTools: ['read'] }],
      },
    ]);

    expect(
      screen.queryByTestId(`agent-servers-without-tools-${activeAgent.id}`),
    ).not.toBeInTheDocument();
  });

  it('lista os agentes-alvo de delegação', () => {
    renderTable([
      {
        ...activeAgent,
        delegatesTo: [
          { id: 'd1', name: 'Cobrança' },
          { id: 'd2', name: 'Financeiro' },
        ],
      },
    ]);

    expect(screen.getByText('Cobrança, Financeiro')).toBeInTheDocument();
  });

  it('indica ausência de delegação quando o agente não delega para ninguém', () => {
    renderTable([activeAgent]);

    expect(screen.getByText('—')).toBeInTheDocument();
  });
});
