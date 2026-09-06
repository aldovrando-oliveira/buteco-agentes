import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { McpServerCreatePage } from './McpServerCreatePage';
import { ApiError, createMcpServer } from '../api/mcpServersApi';
import type { McpServer } from '../types/mcpServer';

vi.mock('../api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/mcpServersApi')>();
  return { ...actual, createMcpServer: vi.fn() };
});

const navigateMock = vi.fn();
vi.mock('react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router')>();
  return { ...actual, useNavigate: () => navigateMock };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

const createdMcpServer: McpServer = {
  id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
  name: 'Zendesk MCP',
  description: '',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'None',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

function renderPage() {
  const queryClient = new QueryClient();
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <McpServerCreatePage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByLabelText(/nome/i);
  await user.type(screen.getByLabelText(/nome/i), createdMcpServer.name);
  await user.type(screen.getByLabelText(/url/i), createdMcpServer.url);
  await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));
}

describe('McpServerCreatePage', () => {
  beforeEach(() => {
    vi.mocked(createMcpServer).mockReset();
    navigateMock.mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('em sucesso, navega para o detalhe do servidor criado e notifica sucesso', async () => {
    vi.mocked(createMcpServer).mockResolvedValue(createdMcpServer);
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith(`/mcp-servers/${createdMcpServer.id}`),
    );
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
  });

  it('em erro 400, aplica os erros nos campos certos do formulário e não navega', async () => {
    vi.mocked(createMcpServer).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { name: ['O nome do servidor MCP é obrigatório.'] },
      }),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(await screen.findByText('O nome do servidor MCP é obrigatório.')).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em falha de rede/servidor, notifica erro genérico, não navega e preserva os dados do formulário', async () => {
    vi.mocked(createMcpServer).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/nome/i)).toHaveValue(createdMcpServer.name);
  });

  it('ao clicar em "Cancelar", navega para a listagem de servidores MCP sem enviar requisição', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByLabelText(/nome/i);
    await user.type(screen.getByLabelText(/nome/i), createdMcpServer.name);
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(navigateMock).toHaveBeenCalledWith('/mcp-servers');
    expect(createMcpServer).not.toHaveBeenCalled();
  });

  it('oferece volta para a listagem', async () => {
    renderPage();

    expect(await screen.findByRole('link', { name: 'Servidores MCP' })).toHaveAttribute(
      'href',
      '/mcp-servers',
    );
  });
});
