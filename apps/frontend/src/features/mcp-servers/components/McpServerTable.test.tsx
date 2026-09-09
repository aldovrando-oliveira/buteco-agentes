import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { McpServerTable } from './McpServerTable';
import type { McpServer } from '../types/mcpServer';
import type { Agent } from '../../agents/types/agent';

const activeServer: McpServer = {
  id: '88888888-8888-8888-8888-888888888888',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'BearerToken',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

const inactiveServer: McpServer = {
  id: '99999999-9999-9999-9999-999999999999',
  name: 'Notion MCP',
  description: 'Servidor MCP do Notion',
  url: 'https://mcp.notion.example/sse',
  authType: 'None',
  isActive: false,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

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
    knowledgeBases: [],
    a2a: null,
    ...overrides,
  };
}

function renderTable(mcpServers: McpServer[], agents?: Agent[]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <McpServerTable mcpServers={mcpServers} agents={agents} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('McpServerTable', () => {
  it('exibe nome (com link), url, tipo de autenticação e estado de cada servidor', () => {
    renderTable([activeServer, inactiveServer]);

    expect(screen.getByRole('link', { name: activeServer.name })).toHaveAttribute(
      'href',
      `/mcp-servers/${activeServer.id}`,
    );
    expect(screen.getByText(activeServer.url)).toBeInTheDocument();
    expect(screen.getByText('Bearer Token')).toBeInTheDocument();
    expect(screen.getByText(inactiveServer.url)).toBeInTheDocument();
    expect(screen.getByText('Nenhuma')).toBeInTheDocument();
  });

  it('exibe indicadores visuais distintos para servidor ativo e inativo', () => {
    renderTable([activeServer, inactiveServer]);

    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByText('Inativo')).toBeInTheDocument();
  });

  it('exibe a quantidade de agentes que usam cada servidor', () => {
    renderTable(
      [activeServer, inactiveServer],
      [
        agent({
          id: 'a1',
          mcpServers: [{ id: activeServer.id, name: 'x', allowedTools: ['read'] }],
        }),
        agent({
          id: 'a2',
          mcpServers: [{ id: activeServer.id, name: 'x', allowedTools: ['write'] }],
        }),
      ],
    );

    expect(screen.getByText('2 agentes')).toBeInTheDocument();
    expect(screen.getByText('Nenhum agente')).toBeInTheDocument();
  });

  it('usa o singular quando apenas um agente usa o servidor', () => {
    renderTable(
      [activeServer],
      [agent({ mcpServers: [{ id: activeServer.id, name: 'x', allowedTools: ['read'] }] })],
    );

    expect(screen.getByText('1 agente')).toBeInTheDocument();
  });

  it('avisa quantos agentes estão vinculados sem nenhuma tool', () => {
    renderTable(
      [activeServer],
      [
        agent({ id: 'a1', mcpServers: [{ id: activeServer.id, name: 'x', allowedTools: [] }] }),
        agent({
          id: 'a2',
          mcpServers: [{ id: activeServer.id, name: 'x', allowedTools: ['read'] }],
        }),
      ],
    );

    expect(screen.getByTestId(`usage-without-tools-${activeServer.id}`)).toHaveTextContent(
      '1 sem tools',
    );
  });

  it('não avisa sobre vínculo sem tools quando todos os agentes têm ao menos uma', () => {
    renderTable(
      [activeServer],
      [agent({ mcpServers: [{ id: activeServer.id, name: 'x', allowedTools: ['read'] }] })],
    );

    expect(screen.queryByTestId(`usage-without-tools-${activeServer.id}`)).not.toBeInTheDocument();
  });

  it('sem o catálogo de agentes, mantém as demais colunas sem afirmar uso', () => {
    renderTable([activeServer]);

    expect(screen.getByRole('link', { name: activeServer.name })).toBeInTheDocument();
    expect(screen.getByText(activeServer.url)).toBeInTheDocument();
    expect(screen.queryByText('Nenhum agente')).not.toBeInTheDocument();
  });
});
