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
  authHeaderName: null,
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
};

// A mensagem pertence ao campo quando o campo aponta para ela em
// `aria-describedby`, e o campo se declara inválido em `aria-invalid`. O
// PasswordInput do Mantine 9.4.2 não põe `aria-invalid` sozinho
// (PasswordInput.mjs:159 e :165); o McpServerForm passa o atributo explícito, e
// este helper o afirma nos dois campos.
function expectErrorOnField(field: HTMLElement, message: string) {
  const errorElement = screen.getByText(message);
  // Sem id, o `toContain('')` abaixo passaria sempre.
  expect(errorElement.id).not.toBe('');
  expect((field.getAttribute('aria-describedby') ?? '').split(' ')).toContain(errorElement.id);
  expect(field).toHaveAttribute('aria-invalid', 'true');
}

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
    await user.click(screen.getByRole('radio', { name: /^Nenhuma/ }));

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

  // Reescrito (design.md, D5, "Corrigido na implementação", decisão do dono em
  // 18/10/2026). Antes: a troca de None para Bearer Token com a credencial em
  // branco saía no PUT e o 400 da API aparecia no campo. Agora a credencial nova
  // é obrigatória no cliente, e o PUT não sai.
  it('trocar None por Bearer Token com a credencial em branco é barrado no cliente, sem PUT e sem navegar', async () => {
    vi.mocked(getMcpServer).mockResolvedValue({ ...mcpServer, authType: 'None' });
    const user = userEvent.setup();

    renderPage(mcpServer.id);

    await screen.findByLabelText(/nome/i);
    await user.click(screen.getByRole('radio', { name: /^Bearer Token/ }));
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(
      await screen.findByText('Informe a credencial nova para o tipo Bearer Token.'),
    ).toBeInTheDocument();
    expect(updateMcpServer).not.toHaveBeenCalled();
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

  describe('token em header customizado', () => {
    const customHeaderServer: McpServer = {
      ...mcpServer,
      authType: 'CustomHeader',
      authHeaderName: 'X-API-TOKEN',
    };

    it('pré-preenche o nome do header e deixa o token vazio', async () => {
      vi.mocked(getMcpServer).mockResolvedValue(customHeaderServer);

      renderPage(customHeaderServer.id);

      expect(await screen.findByLabelText(/nome do header/i)).toHaveValue('X-API-TOKEN');
      expect(screen.getByRole('radio', { name: /^Token em header customizado/ })).toHaveAttribute(
        'aria-checked',
        'true',
      );
      expect(screen.getByLabelText(/^token/i)).toHaveValue('');
      expect(
        screen.getByText('Deixe em branco para manter a credencial atual.'),
      ).toBeInTheDocument();
    });

    it('editar só o nome do header envia o nome novo, sem credencial', async () => {
      vi.mocked(getMcpServer).mockResolvedValue(customHeaderServer);
      vi.mocked(updateMcpServer).mockResolvedValue({
        ...customHeaderServer,
        authHeaderName: 'X-API-KEY',
      });
      const user = userEvent.setup();

      renderPage(customHeaderServer.id);

      const headerInput = await screen.findByLabelText(/nome do header/i);
      await user.clear(headerInput);
      await user.type(headerInput, 'X-API-KEY');
      await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

      await waitFor(() =>
        expect(updateMcpServer).toHaveBeenCalledWith(customHeaderServer.id, {
          name: customHeaderServer.name,
          description: customHeaderServer.description,
          url: customHeaderServer.url,
          authType: 'CustomHeader',
          authHeaderName: 'X-API-KEY',
          credential: undefined,
        }),
      );
      expect(screen.queryByTestId('type-change-warning')).not.toBeInTheDocument();
    });

    // Caminho real da página: PUT rejeitado → fieldErrorsFrom → campo. Token com
    // espaço no início passa pela obrigatoriedade do cliente; o formato é da API
    // (McpAuthHeaderRules.ValidateTokenValue).
    it('400 da API em credential aparece no campo "Token" e não navega', async () => {
      vi.mocked(getMcpServer).mockResolvedValue(customHeaderServer);
      vi.mocked(updateMcpServer).mockRejectedValue(
        new ApiError(400, 'Validação falhou', {
          title: 'Validação falhou',
          status: 400,
          errors: { credential: ['A credencial não pode começar nem terminar com espaço.'] },
        }),
      );
      const user = userEvent.setup();

      renderPage(customHeaderServer.id);

      await user.type(await screen.findByLabelText(/^token/i), ' segredo-123');
      await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

      await waitFor(() =>
        expect(updateMcpServer).toHaveBeenCalledWith(
          customHeaderServer.id,
          expect.objectContaining({ credential: ' segredo-123' }),
        ),
      );
      await screen.findByText('A credencial não pode começar nem terminar com espaço.');
      expectErrorOnField(
        screen.getByLabelText(/^token/i),
        'A credencial não pode começar nem terminar com espaço.',
      );
      expect(navigateMock).not.toHaveBeenCalled();
    });

    // O fluxo do quadro "Erros" do protótipo: nome que a API recusa, token
    // preenchido, o PUT sai e o 400 volta para o campo do nome.
    it('400 da API em authHeaderName aparece no campo "Nome do header" e não navega', async () => {
      vi.mocked(getMcpServer).mockResolvedValue(mcpServer);
      vi.mocked(updateMcpServer).mockRejectedValue(
        new ApiError(400, 'Validação falhou', {
          title: 'Validação falhou',
          status: 400,
          errors: {
            authHeaderName: [
              'O header Content-Type não pode ser usado em uma requisição (é um header de conteúdo ou de controle do .NET).',
            ],
          },
        }),
      );
      const user = userEvent.setup();

      renderPage(mcpServer.id);

      await screen.findByLabelText(/nome/i);
      await user.click(screen.getByRole('radio', { name: /^Token em header customizado/ }));
      await user.type(screen.getByLabelText(/nome do header/i), 'Content-Type');
      await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
      await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

      await waitFor(() =>
        expect(updateMcpServer).toHaveBeenCalledWith(
          mcpServer.id,
          expect.objectContaining({ authType: 'CustomHeader', authHeaderName: 'Content-Type' }),
        ),
      );
      await screen.findByText(
        'O header Content-Type não pode ser usado em uma requisição (é um header de conteúdo ou de controle do .NET).',
      );
      expectErrorOnField(
        screen.getByLabelText(/nome do header/i),
        'O header Content-Type não pode ser usado em uma requisição (é um header de conteúdo ou de controle do .NET).',
      );
      expect(navigateMock).not.toHaveBeenCalled();
    });
  });
});
