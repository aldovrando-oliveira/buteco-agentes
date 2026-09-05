import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { McpServerConfigCard } from './McpServerConfigCard';
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
      <McpServerConfigCard mcpServer={server} />
    </MantineProvider>,
  );
}

describe('McpServerConfigCard', () => {
  it('exibe url, tipo de autenticação e as datas de criação e atualização', () => {
    renderCard(mcpServer);

    expect(screen.getByText(mcpServer.url)).toBeInTheDocument();
    expect(screen.getByText('Bearer Token')).toBeInTheDocument();
    expect(screen.getByText('Criado em')).toBeInTheDocument();
    expect(screen.getByText('Atualizado em')).toBeInTheDocument();
  });

  it('exibe a linha de credencial cifrada, sem nenhum valor, quando a autenticação exige credencial', () => {
    renderCard(mcpServer);

    const credential = screen.getByTestId('credential-row');
    expect(credential).toHaveTextContent('cifrada');
    expect(credential).toHaveTextContent('nunca devolvida pela API');
    expect(credential.textContent).toContain('••••');
  });

  it('não exibe linha de credencial quando o tipo de autenticação é None', () => {
    renderCard({ ...mcpServer, authType: 'None' });

    expect(screen.queryByTestId('credential-row')).not.toBeInTheDocument();
    expect(screen.getByText('Nenhuma')).toBeInTheDocument();
  });
});
