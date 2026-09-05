import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentMcpServerRow } from './AgentMcpServerRow';
import { listMcpServerTools } from '../../mcp-servers/api/mcpServersApi';
import type { McpServer } from '../../mcp-servers/types/mcpServer';

vi.mock('../../mcp-servers/api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../mcp-servers/api/mcpServersApi')>();
  return { ...actual, listMcpServerTools: vi.fn() };
});

const activeServer: McpServer = {
  id: 'srv-1',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'None',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

function renderRow(overrides?: {
  mcpServer?: McpServer;
  selected?: boolean;
  allowedTools?: string[];
  expanded?: boolean;
}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <AgentMcpServerRow
          mcpServer={overrides?.mcpServer ?? activeServer}
          selected={overrides?.selected ?? false}
          allowedTools={overrides?.allowedTools ?? []}
          expanded={overrides?.expanded ?? false}
          onToggleSelected={vi.fn()}
          onToggleExpanded={vi.fn()}
          onToggleTool={vi.fn()}
          onToolsDiscovered={vi.fn()}
        />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('AgentMcpServerRow', () => {
  beforeEach(() => {
    vi.mocked(listMcpServerTools).mockReset();
    vi.mocked(listMcpServerTools).mockResolvedValue({
      success: true,
      tools: [
        { name: 'read', description: 'Lê dados' },
        { name: 'write', description: 'Escreve dados' },
      ],
      failureReason: null,
      message: null,
    });
  });

  it('exibe nome e url do servidor e não busca tools enquanto está recolhido', () => {
    renderRow();

    expect(screen.getByRole('checkbox', { name: /zendesk mcp/i })).toBeInTheDocument();
    expect(screen.getByText(activeServer.url)).toBeInTheDocument();
    expect(listMcpServerTools).not.toHaveBeenCalled();
  });

  it('indica "Não vinculado" quando o servidor não faz parte do vínculo', () => {
    renderRow({ selected: false });

    expect(screen.getByText('Não vinculado')).toBeInTheDocument();
  });

  it('conta as tools marcadas sem total enquanto a descoberta não aconteceu', () => {
    renderRow({ selected: true, allowedTools: ['read'] });

    expect(screen.getByText('1 selecionadas')).toBeInTheDocument();
  });

  it('conta as tools marcadas em relação ao total depois da descoberta', async () => {
    renderRow({ selected: true, allowedTools: ['read'], expanded: true });

    expect(await screen.findByText('1 de 2 selecionadas')).toBeInTheDocument();
  });

  it('exibe o badge de inativo e o aviso de consequência quando o servidor inativo está vinculado', () => {
    renderRow({ mcpServer: { ...activeServer, isActive: false }, selected: true });

    expect(screen.getByText('Inativo')).toBeInTheDocument();
    expect(screen.getByTestId('inactive-bound-callout-srv-1')).toBeInTheDocument();
  });

  it('não exibe o aviso de consequência quando o servidor inativo não está vinculado', () => {
    renderRow({ mcpServer: { ...activeServer, isActive: false }, selected: false });

    expect(screen.getByText('Inativo')).toBeInTheDocument();
    expect(screen.queryByTestId('inactive-bound-callout-srv-1')).not.toBeInTheDocument();
  });

  it('exibe o badge de sem tools apenas quando o servidor está vinculado com nenhuma tool', () => {
    renderRow({ selected: true, allowedTools: [] });

    expect(screen.getByText('Sem tools')).toBeInTheDocument();
  });

  it('desabilita as tools de um servidor que não está vinculado', async () => {
    renderRow({ selected: false, expanded: true });

    expect(await screen.findByRole('checkbox', { name: /^read/ })).toBeDisabled();
  });

  it('habilita as tools de um servidor vinculado', async () => {
    renderRow({ selected: true, expanded: true });

    expect(await screen.findByRole('checkbox', { name: /^read/ })).toBeEnabled();
  });

  it('indica quando o servidor não oferece nenhuma tool', async () => {
    vi.mocked(listMcpServerTools).mockResolvedValue({
      success: true,
      tools: [],
      failureReason: null,
      message: null,
    });

    renderRow({ selected: true, expanded: true });

    expect(await screen.findByText('Este servidor não oferece nenhuma tool.')).toBeInTheDocument();
  });

  it('exibe o motivo da falha e a ação de tentar novamente quando a descoberta falha', async () => {
    vi.mocked(listMcpServerTools).mockResolvedValue({
      success: false,
      tools: null,
      failureReason: 'HostUnreachable',
      message: 'Não foi possível conectar ao host informado.',
    });

    renderRow({ selected: true, expanded: true });

    expect(
      await screen.findByText('Não foi possível conectar ao host informado.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /tentar novamente/i })).toBeInTheDocument();
  });
});
