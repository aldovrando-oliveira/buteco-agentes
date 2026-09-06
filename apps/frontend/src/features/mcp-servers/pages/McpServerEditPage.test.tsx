import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { McpServerEditPage } from './McpServerEditPage';
import { ApiError, getMcpServer, updateMcpServer } from '../api/mcpServersApi';
import type { McpServer } from '../types/mcpServer';

vi.mock('../api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/mcpServersApi')>();
  return { ...actual, getMcpServer: vi.fn(), updateMcpServer: vi.fn() };
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

const mcpServer: McpServer = {
  id: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
  name: 'Zendesk MCP',
  description: 'Servidor MCP do Zendesk',
  url: 'https://mcp.zendesk.example/sse',
  authType: 'BearerToken',
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

function renderPage(id: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/mcp-servers/${id}/edit`]}>
          <Routes>
            <Route path="/mcp-servers/:id/edit" element={<McpServerEditPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('McpServerEditPage', () => {
  beforeEach(() => {
    vi.mocked(getMcpServer).mockReset();
    vi.mocked(updateMcpServer).mockReset();
    navigateMock.mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('exibe o formulário pré-preenchido, sem preencher a credencial, e mantém a credencial atual quando o campo fica em branco', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(mcpServer);
    const updated = { ...mcpServer, name: 'Zendesk MCP (renomeado)' };
    vi.mocked(updateMcpServer).mockResolvedValue(updated);
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    expect(await screen.findByLabelText(/nome/i)).toHaveValue(mcpServer.name);
    expect(screen.getByLabelText(/url/i)).toHaveValue(mcpServer.url);
    expect(screen.getByLabelText(/credencial/i)).toHaveValue('');

    await user.clear(screen.getByLabelText(/nome/i));
    await user.type(screen.getByLabelText(/nome/i), updated.name);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(updateMcpServer).toHaveBeenCalledWith(mcpServer.id, {
        name: updated.name,
        description: mcpServer.description,
        url: mcpServer.url,
        authType: mcpServer.authType,
        credential: undefined,
      }),
    );
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith(`/mcp-servers/${mcpServer.id}`));
    expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' }));
  });

  it('em edição trocando a credencial, envia a nova credencial informada', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(mcpServer);
    vi.mocked(updateMcpServer).mockResolvedValue(mcpServer);
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    await screen.findByLabelText(/nome/i);
    await user.type(screen.getByLabelText(/credencial/i), 'novo-token');
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(updateMcpServer).toHaveBeenCalledWith(mcpServer.id, {
        name: mcpServer.name,
        description: mcpServer.description,
        url: mcpServer.url,
        authType: mcpServer.authType,
        credential: 'novo-token',
      }),
    );
  });

  it('trocar o tipo de autenticação para None oculta o campo de credencial e não o envia', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(mcpServer);
    vi.mocked(updateMcpServer).mockResolvedValue({ ...mcpServer, authType: 'None' });
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('combobox', { name: /tipo de autenticação/i }));
    await user.click(await screen.findByRole('option', { name: 'Nenhuma' }));

    expect(screen.queryByLabelText(/credencial/i)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(updateMcpServer).toHaveBeenCalledWith(mcpServer.id, {
        name: mcpServer.name,
        description: mcpServer.description,
        url: mcpServer.url,
        authType: 'None',
        credential: undefined,
      }),
    );
  });

  it('em erro 400 por credencial nunca persistida ao mudar AuthType para BearerToken sem informar credencial, aplica o erro no campo Credencial', async () => {
    vi.mocked(getMcpServer).mockResolvedValue({ ...mcpServer, authType: 'None' });
    vi.mocked(updateMcpServer).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: {
          credential: ['A credencial é obrigatória para o tipo de autenticação informado.'],
        },
      }),
    );
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('combobox', { name: /tipo de autenticação/i }));
    await user.click(await screen.findByRole('option', { name: 'Bearer Token' }));
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(
      await screen.findByText('A credencial é obrigatória para o tipo de autenticação informado.'),
    ).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em erro 400, aplica os erros nos campos certos do formulário e não navega', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(mcpServer);
    vi.mocked(updateMcpServer).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { name: ['O nome do servidor MCP é obrigatório.'] },
      }),
    );
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(await screen.findByText('O nome do servidor MCP é obrigatório.')).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('em falha de rede/servidor, notifica erro genérico, não navega e preserva os dados editados', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(mcpServer);
    vi.mocked(updateMcpServer).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/nome/i)).toHaveValue(mcpServer.name);
  });

  it('ao clicar em "Cancelar", navega para o detalhe do servidor em edição sem enviar requisição', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(mcpServer);
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(navigateMock).toHaveBeenCalledWith(`/mcp-servers/${mcpServer.id}`);
    expect(updateMcpServer).not.toHaveBeenCalled();
  });

  it('volta para o registro que está sendo editado, e não para a listagem', async () => {
    vi.mocked(getMcpServer).mockResolvedValue(mcpServer);

    renderPage(mcpServer.id);

    // De "editar X" quer-se voltar para X, que é de onde se veio — não para a
    // lista inteira.
    expect(await screen.findByRole('link', { name: 'Voltar ao servidor' })).toHaveAttribute(
      'href',
      `/mcp-servers/${mcpServer.id}`,
    );
  });
});
