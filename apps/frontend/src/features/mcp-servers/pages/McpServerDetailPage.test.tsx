import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { McpServerDetailPage } from './McpServerDetailPage';
import {
  ApiError,
  activateMcpServer,
  deactivateMcpServer,
  getMcpServer,
  testSavedMcpServerConnection,
} from '../api/mcpServersApi';
import type { McpServer } from '../types/mcpServer';

vi.mock('../api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/mcpServersApi')>();
  return {
    ...actual,
    getMcpServer: vi.fn(),
    activateMcpServer: vi.fn(),
    deactivateMcpServer: vi.fn(),
    testSavedMcpServerConnection: vi.fn(),
  };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

const activeMcpServer: McpServer = {
  id: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'BearerToken',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

const inactiveMcpServer: McpServer = { ...activeMcpServer, isActive: false };

function renderPage(id: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/mcp-servers/${id}`]}>
          <Routes>
            <Route path="/mcp-servers/:id" element={<McpServerDetailPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('McpServerDetailPage', () => {
  beforeEach(() => {
    vi.mocked(getMcpServer).mockReset();
    vi.mocked(activateMcpServer).mockReset();
    vi.mocked(deactivateMcpServer).mockReset();
    vi.mocked(testSavedMcpServerConnection).mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe nome, url, autenticação, estado, link de edição e ação de desativar de um servidor ativo', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(activeMcpServer);

    renderPage(activeMcpServer.id);

    expect(await screen.findByRole('heading', { name: activeMcpServer.name })).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /editar/i })).toHaveAttribute(
      'href',
      `/mcp-servers/${activeMcpServer.id}/edit`,
    );
    expect(screen.getByRole('button', { name: /desativar/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^ativar$/i })).not.toBeInTheDocument();
  });

  it('exibe estado de "não encontrado" quando o servidor MCP não existe', async () => {
    vi.mocked(getMcpServer).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage('inexistente');

    expect(await screen.findByText('Servidor MCP não encontrado.')).toBeInTheDocument();
  });

  it('exibe a ação "Ativar" (e não "Desativar") para um servidor inativo', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(inactiveMcpServer);

    renderPage(inactiveMcpServer.id);

    expect(await screen.findByText('Inativo')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^ativar$/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /desativar/i })).not.toBeInTheDocument();
  });

  it('ao ativar, envia a requisição imediatamente sem confirmação e atualiza o indicador', async () => {
    vi.mocked(getMcpServer)
      .mockResolvedValueOnce(inactiveMcpServer)
      .mockResolvedValue(activeMcpServer);
    vi.mocked(activateMcpServer).mockResolvedValue(activeMcpServer);
    const user = userEvent.setup();

    renderPage(inactiveMcpServer.id);

    await user.click(await screen.findByRole('button', { name: /^ativar$/i }));

    expect(activateMcpServer).toHaveBeenCalledWith(inactiveMcpServer.id);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(await screen.findByText('Ativo')).toBeInTheDocument();
  });

  it('ao clicar em "Desativar", abre o modal de confirmação sem enviar a requisição', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(activeMcpServer);
    const user = userEvent.setup();

    renderPage(activeMcpServer.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(deactivateMcpServer).not.toHaveBeenCalled();
  });

  it('confirmar a desativação envia a requisição, notifica sucesso e atualiza o indicador para inativo', async () => {
    vi.mocked(getMcpServer)
      .mockResolvedValueOnce(activeMcpServer)
      .mockResolvedValue(inactiveMcpServer);
    vi.mocked(deactivateMcpServer).mockResolvedValue(inactiveMcpServer);
    const user = userEvent.setup();

    renderPage(activeMcpServer.id);

    await user.click(await screen.findByRole('button', { name: /desativar/i }));
    await screen.findByRole('dialog');
    await user.click(screen.getByRole('button', { name: /confirmar desativação/i }));

    expect(deactivateMcpServer).toHaveBeenCalledWith(activeMcpServer.id);
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(await screen.findByText('Inativo')).toBeInTheDocument();
  });

  it('teste de conexão salvo bem-sucedido exibe indicação inline de sucesso, sem toast nem modal', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(activeMcpServer);
    vi.mocked(testSavedMcpServerConnection).mockResolvedValue({
      success: true,
      failureReason: null,
      message: null,
    });
    const user = userEvent.setup();

    renderPage(activeMcpServer.id);

    await user.click(await screen.findByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Conexão bem-sucedida')).toBeInTheDocument();
    expect(testSavedMcpServerConnection).toHaveBeenCalledWith(activeMcpServer.id);
    expect(notifications.show).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('teste de conexão salvo com falha exibe o motivo retornado, sem toast nem modal', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(activeMcpServer);
    vi.mocked(testSavedMcpServerConnection).mockResolvedValue({
      success: false,
      failureReason: 'CredentialDecryptionFailed',
      message: 'Não foi possível decifrar a credencial persistida.',
    });
    const user = userEvent.setup();

    renderPage(activeMcpServer.id);

    await user.click(await screen.findByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Falha na conexão')).toBeInTheDocument();
    expect(
      screen.getByText('Não foi possível decifrar a credencial persistida.'),
    ).toBeInTheDocument();
    expect(notifications.show).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
