import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { McpServerForm } from './McpServerForm';
import {
  ApiError,
  testSavedMcpServerConnection,
  testUnsavedMcpServerConnection,
} from '../api/mcpServersApi';

vi.mock('../api/mcpServersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/mcpServersApi')>();
  return {
    ...actual,
    testUnsavedMcpServerConnection: vi.fn(),
    testSavedMcpServerConnection: vi.fn(),
  };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

function renderForm(overrides?: {
  errors?: Record<string, string>;
  submitting?: boolean;
  initialValues?: Parameters<typeof McpServerForm>[0]['initialValues'];
  submitLabel?: string;
  mode?: 'create' | 'edit';
  mcpServerId?: string;
}) {
  const onSubmit = vi.fn();
  const onCancel = vi.fn();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <McpServerForm onSubmit={onSubmit} onCancel={onCancel} {...overrides} />
      </QueryClientProvider>
    </MantineProvider>,
  );
  return { onSubmit, onCancel };
}

function getSelect(label: RegExp) {
  return screen.getByRole('combobox', { name: label });
}

async function selectOption(
  user: ReturnType<typeof userEvent.setup>,
  label: RegExp,
  optionName: string,
) {
  await user.click(getSelect(label));
  await user.click(await screen.findByRole('option', { name: optionName }));
}

describe('McpServerForm', () => {
  beforeEach(() => {
    vi.mocked(testUnsavedMcpServerConnection).mockReset();
    vi.mocked(testSavedMcpServerConnection).mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('chama onSubmit com os valores quando o formulário é válido e AuthType é None', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/nome/i), 'Zendesk MCP');
    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Zendesk MCP',
      description: '',
      url: 'https://mcp.zendesk.example/sse',
      authType: 'None',
      credential: undefined,
    });
  });

  it('chama onSubmit incluindo a credencial quando AuthType é BearerToken', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/nome/i), 'Zendesk MCP');
    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await selectOption(user, /tipo de autenticação/i, 'Bearer Token');
    await user.type(screen.getByLabelText(/credencial/i), 'token-secreto');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Zendesk MCP',
      description: '',
      url: 'https://mcp.zendesk.example/sse',
      authType: 'BearerToken',
      credential: 'token-secreto',
    });
  });

  it('exibe erro de validação e não chama onSubmit quando nome e url estão vazios', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(await screen.findByText('O nome do servidor MCP é obrigatório.')).toBeInTheDocument();
    expect(screen.getByText('A URL do servidor MCP é obrigatória.')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('campo Credencial não é exibido quando AuthType é None', () => {
    renderForm();

    expect(screen.queryByLabelText(/credencial/i)).not.toBeInTheDocument();
  });

  it('campo Credencial aparece e é obrigatório no cadastro quando AuthType é BearerToken', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/nome/i), 'Zendesk MCP');
    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await selectOption(user, /tipo de autenticação/i, 'Bearer Token');

    expect(screen.getByLabelText(/credencial/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(
      await screen.findByText('A credencial é obrigatória para o tipo de autenticação informado.'),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('em modo de edição, campo Credencial não é obrigatório e placeholder indica que vazio mantém a credencial atual', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm({
      mode: 'edit',
      submitLabel: 'Salvar alterações',
      initialValues: {
        name: 'Zendesk MCP',
        description: '',
        url: 'https://mcp.zendesk.example/sse',
        authType: 'BearerToken',
      },
    });

    const credentialInput = screen.getByLabelText(/credencial/i);
    expect(credentialInput).toHaveAttribute(
      'placeholder',
      'Deixe em branco para manter a credencial atual',
    );

    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Zendesk MCP',
      description: '',
      url: 'https://mcp.zendesk.example/sse',
      authType: 'BearerToken',
      credential: undefined,
    });
  });

  it('chama onCancel e não chama onSubmit quando o usuário clica em Cancelar', async () => {
    const user = userEvent.setup();
    const { onSubmit, onCancel } = renderForm();

    await user.type(screen.getByLabelText(/nome/i), 'Zendesk MCP');
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('teste de conexão bem-sucedido exibe indicação inline de sucesso', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue({
      success: true,
      failureReason: null,
      message: null,
    });
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Conexão bem-sucedida')).toBeInTheDocument();
  });

  it('teste de conexão com falha exibe o motivo retornado pela API', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue({
      success: false,
      failureReason: 'HostUnreachable',
      message: 'Não foi possível conectar ao host informado.',
    });
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Falha na conexão')).toBeInTheDocument();
    expect(screen.getByText('Não foi possível conectar ao host informado.')).toBeInTheDocument();
  });

  it('erro de validação (400) no teste de conexão é normalizado para o mesmo alerta de falha', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { url: ['A URL do servidor MCP é obrigatória.'] },
      }),
    );
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Falha na conexão')).toBeInTheDocument();
    expect(screen.getByText('A URL do servidor MCP é obrigatória.')).toBeInTheDocument();
  });

  it('alterar a URL depois de um teste limpa o resultado exibido', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue({
      success: true,
      failureReason: null,
      message: null,
    });
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));
    expect(await screen.findByText('Conexão bem-sucedida')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/url/i), '/novo-caminho');

    expect(screen.queryByText('Conexão bem-sucedida')).not.toBeInTheDocument();
  });

  it('o resultado do teste de conexão nunca é exibido como notificação (toast) nem como diálogo modal', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue({
      success: false,
      failureReason: 'CredentialRejected',
      message: 'A credencial informada foi rejeitada pelo servidor MCP.',
    });
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Falha na conexão')).toBeInTheDocument();
    expect(notifications.show).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('testar com a Url vazia é barrado antes de qualquer requisição', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Informe a Url antes de testar.')).toBeInTheDocument();
    expect(testUnsavedMcpServerConnection).not.toHaveBeenCalled();
    expect(testSavedMcpServerConnection).not.toHaveBeenCalled();
  });

  it('na edição sem credencial digitada, testa o servidor salvo e avisa que usa a credencial guardada', async () => {
    vi.mocked(testSavedMcpServerConnection).mockResolvedValue({
      success: true,
      failureReason: null,
      message: null,
    });
    const user = userEvent.setup();
    renderForm({
      mode: 'edit',
      mcpServerId: 'srv-1',
      initialValues: {
        name: 'Zendesk MCP',
        description: 'Servidor MCP do Zendesk',
        url: 'https://mcp.zendesk.example/sse',
        authType: 'BearerToken',
      },
    });

    expect(screen.getByTestId('saved-credential-test-note')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Conexão bem-sucedida')).toBeInTheDocument();
    expect(testSavedMcpServerConnection).toHaveBeenCalledWith('srv-1');
    expect(testUnsavedMcpServerConnection).not.toHaveBeenCalled();
  });

  it('na edição com credencial digitada, testa a configuração informada', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue({
      success: true,
      failureReason: null,
      message: null,
    });
    const user = userEvent.setup();
    renderForm({
      mode: 'edit',
      mcpServerId: 'srv-1',
      initialValues: {
        name: 'Zendesk MCP',
        description: 'Servidor MCP do Zendesk',
        url: 'https://mcp.zendesk.example/sse',
        authType: 'BearerToken',
      },
    });

    await user.type(screen.getByLabelText(/credencial/i), 'novo-token');
    expect(screen.queryByTestId('saved-credential-test-note')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    await screen.findByText('Conexão bem-sucedida');
    expect(testUnsavedMcpServerConnection).toHaveBeenCalledWith({
      url: 'https://mcp.zendesk.example/sse',
      authType: 'BearerToken',
      credential: 'novo-token',
    });
    expect(testSavedMcpServerConnection).not.toHaveBeenCalled();
  });

  it('no cadastro, testa a configuração digitada mesmo com credencial em branco', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue({
      success: true,
      failureReason: null,
      message: null,
    });
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/url/i), 'https://mcp.zendesk.example/sse');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    await screen.findByText('Conexão bem-sucedida');
    expect(testUnsavedMcpServerConnection).toHaveBeenCalled();
    expect(testSavedMcpServerConnection).not.toHaveBeenCalled();
    expect(screen.queryByTestId('saved-credential-test-note')).not.toBeInTheDocument();
  });

  it('exibe a dica de credencial adequada ao contexto', () => {
    renderForm();
    expect(screen.queryByText(/Enviada cifrada/)).not.toBeInTheDocument();

    renderForm({
      mode: 'edit',
      mcpServerId: 'srv-1',
      initialValues: {
        name: 'Zendesk MCP',
        description: '',
        url: 'https://mcp.zendesk.example/sse',
        authType: 'BearerToken',
      },
    });
    expect(screen.getByText('Deixe em branco para manter a credencial atual.')).toBeInTheDocument();
  });
});
