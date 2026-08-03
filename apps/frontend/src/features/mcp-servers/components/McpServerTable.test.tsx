import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { McpServerTable } from './McpServerTable';
import type { McpServer } from '../types/mcpServer';

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

function renderTable(mcpServers: McpServer[]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <McpServerTable mcpServers={mcpServers} />
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
});
