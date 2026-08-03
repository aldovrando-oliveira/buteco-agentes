import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { McpServerDetailCard } from './McpServerDetailCard';
import type { McpServer } from '../types/mcpServer';

const mcpServer: McpServer = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'BearerToken',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

function renderCard(server: McpServer) {
  return render(
    <MantineProvider theme={theme}>
      <McpServerDetailCard mcpServer={server} />
    </MantineProvider>,
  );
}

describe('McpServerDetailCard', () => {
  it('exibe nome, descrição, url, tipo de autenticação, estado e datas — sem nenhum campo de credencial', () => {
    renderCard(mcpServer);

    expect(screen.getByRole('heading', { name: mcpServer.name })).toBeInTheDocument();
    expect(screen.getByText(mcpServer.description)).toBeInTheDocument();
    expect(screen.getByText(/https:\/\/mcp\.zendesk\.example\/sse/)).toBeInTheDocument();
    expect(screen.getByText(/Bearer Token/)).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.queryByText(/credencial/i)).not.toBeInTheDocument();
  });

  it('exibe o servidor inativo com o indicador correspondente', () => {
    renderCard({ ...mcpServer, isActive: false });

    expect(screen.getByText('Inativo')).toBeInTheDocument();
  });
});
