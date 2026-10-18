import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
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

// O tipo de autenticação é escolhido em cartões com radio (design.md da change
// frontend-mcp-autenticacao-header-customizado, D1/D8), não mais num Select. O
// nome acessível do cartão começa pelo título e segue com a descrição.
function authTypeRadio(title: string) {
  return screen.getByRole('radio', { name: new RegExp(`^${title}`) });
}

async function selectAuthType(user: ReturnType<typeof userEvent.setup>, title: string) {
  await user.click(authTypeRadio(title));
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
    await selectAuthType(user, 'Bearer Token');
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
    await selectAuthType(user, 'Bearer Token');

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

  // Asserção editada por decisão (design.md, D5, aprovada pelo dono em
  // 18/10/2026): o 400 do teste não é mais normalizado para o alerta "Falha na
  // conexão", que afirmava uma tentativa de conexão que não aconteceu; vai para
  // o campo, com um aviso inline.
  it('erro de validação (400) no teste de conexão vai para o campo, sem o alerta de falha de conexão', async () => {
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

    expect(await screen.findByTestId('connection-test-invalid')).toBeInTheDocument();
    expect(screen.getByText('A URL do servidor MCP é obrigatória.')).toBeInTheDocument();
    expect(screen.queryByText('Falha na conexão')).not.toBeInTheDocument();
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

// Casos da change frontend-mcp-autenticacao-header-customizado (#168), um `it()`
// por estado observável dos cenários da delta de mcp-server-catalog-ui.
describe('McpServerForm — token em header customizado e troca de tipo', () => {
  beforeEach(() => {
    vi.mocked(testUnsavedMcpServerConnection).mockReset();
    vi.mocked(testSavedMcpServerConnection).mockReset();
  });

  const ok = { success: true, failureReason: null, message: null };

  function renderEdit(
    savedAuthType: 'None' | 'BearerToken' | 'CustomHeader',
    errors?: Record<string, string>,
  ) {
    return renderForm({
      mode: 'edit',
      mcpServerId: 'srv-1',
      submitLabel: 'Salvar alterações',
      errors,
      initialValues: {
        name: 'Ferramentas internas',
        description: '',
        url: 'https://mcp.exemplo.internal/mcp',
        authType: savedAuthType,
        authHeaderName: savedAuthType === 'CustomHeader' ? 'X-API-TOKEN' : '',
      },
    });
  }

  // Rótulo do campo com o asterisco de obrigatório, que o Mantine põe num span
  // dentro do <label>.
  function fieldLabel(text: string) {
    return Array.from(document.querySelectorAll('label')).find((label) =>
      label.textContent?.startsWith(text),
    );
  }

  async function fillCreate(user: ReturnType<typeof userEvent.setup>) {
    await user.type(screen.getByRole('textbox', { name: 'Nome' }), 'Ferramentas internas');
    await user.type(screen.getByLabelText(/url/i), 'https://mcp.exemplo.internal/mcp');
  }

  it('oferece os três tipos como cartões com radio, sem lista suspensa', () => {
    renderForm();

    expect(authTypeRadio('Nenhuma')).toHaveAttribute('aria-checked', 'true');
    expect(authTypeRadio('Bearer Token')).toBeInTheDocument();
    expect(authTypeRadio('Token em header customizado')).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('com Nenhuma, não mostra credencial nem nome do header', () => {
    renderForm();

    expect(screen.queryByLabelText(/credencial/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/token/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/nome do header/i)).not.toBeInTheDocument();
  });

  it('com Bearer Token, mostra "Credencial" e não o nome do header', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectAuthType(user, 'Bearer Token');

    expect(screen.getByLabelText(/credencial/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/nome do header/i)).not.toBeInTheDocument();
  });

  it('com token em header customizado, mostra "Nome do header" e "Token" no lugar de "Credencial"', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectAuthType(user, 'Token em header customizado');

    expect(screen.getByLabelText(/nome do header/i)).toHaveAttribute('placeholder', 'X-API-TOKEN');
    expect(screen.getByLabelText(/^token/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/credencial/i)).not.toBeInTheDocument();
    expect(fieldLabel('Nome do header')).toHaveTextContent('*');
    expect(fieldLabel('Token')).toHaveTextContent('*');
  });

  it('envia o nome do header e o token com token em header customizado', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Ferramentas internas',
      description: '',
      url: 'https://mcp.exemplo.internal/mcp',
      authType: 'CustomHeader',
      authHeaderName: 'X-API-TOKEN',
      credential: 'segredo-123',
    });
  });

  it('nome do header digitado e abandonado não é enviado com outro tipo', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await selectAuthType(user, 'Bearer Token');
    await user.type(screen.getByLabelText(/credencial/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit.mock.calls[0][0]).not.toHaveProperty('authHeaderName');
  });

  it('o nome do header digitado volta preenchido ao selecionar o tipo de novo', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await selectAuthType(user, 'Nenhuma');
    await selectAuthType(user, 'Token em header customizado');

    expect(screen.getByLabelText(/nome do header/i)).toHaveValue('X-API-TOKEN');
  });

  it('nome do header vazio é barrado no cliente', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(
      await screen.findByText('Informe o nome do header em que o serviço espera o token.'),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('nome do header só com espaços é barrado no cliente', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), '   ');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(
      await screen.findByText('Informe o nome do header em que o serviço espera o token.'),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('token vazio no cadastro é barrado no cliente', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(
      await screen.findByText('A credencial é obrigatória para o tipo de autenticação informado.'),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('na edição com o tipo trocado, a credencial nova é obrigatória no cliente', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderEdit('BearerToken');

    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(
      await screen.findByText('Informe a credencial nova para o tipo Token em header customizado.'),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('nome do header com formato que a API recusa é enviado: o formato não é validado no cliente', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'Content Type');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /cadastrar servidor/i }));

    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ authType: 'CustomHeader', authHeaderName: 'Content Type' }),
    );
  });

  it('erros da página em authHeaderName e credential aparecem nos campos', () => {
    renderEdit('CustomHeader', {
      authHeaderName:
        'Headers com prefixo Mcp- são do protocolo MCP e não podem levar a credencial.',
      credential: 'A credencial não pode começar nem terminar com espaço.',
    });

    expect(
      screen.getByText(
        'Headers com prefixo Mcp- são do protocolo MCP e não podem levar a credencial.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText('A credencial não pode começar nem terminar com espaço.'),
    ).toBeInTheDocument();
  });

  it('a prévia acompanha o nome do header digitado, e sem nome mostra o marcador', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectAuthType(user, 'Token em header customizado');
    const preview = screen.getByTestId('credential-preview');
    expect(
      within(preview).getByText('Como a credencial sai em cada requisição ao servidor'),
    ).toBeInTheDocument();
    expect(within(preview).getByText('<nome do header>: ••••••••')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/nome do header/i), 'X-A');
    expect(within(preview).getByText('X-A: ••••••••')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/nome do header/i), 'PI-TOKEN');
    expect(within(preview).getByText('X-API-TOKEN: ••••••••')).toBeInTheDocument();
    expect(
      within(preview).getByText(
        'O valor vai como foi digitado, sem prefixo. Authorization não é enviado.',
      ),
    ).toBeInTheDocument();
  });

  it('o valor do token nunca aparece na prévia', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');

    expect(screen.getByTestId('credential-preview')).not.toHaveTextContent('segredo-123');
  });

  it('sem prévia com Nenhuma', () => {
    renderForm();

    expect(screen.queryByTestId('credential-preview')).not.toBeInTheDocument();
  });

  it('sem prévia com Bearer Token', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectAuthType(user, 'Bearer Token');

    expect(screen.queryByTestId('credential-preview')).not.toBeInTheDocument();
  });

  it('troca entre dois tipos com credencial avisa que a salva não é reaproveitada e a torna obrigatória', async () => {
    const user = userEvent.setup();
    renderEdit('BearerToken');

    expect(screen.queryByTestId('type-change-warning')).not.toBeInTheDocument();
    expect(fieldLabel('Credencial')).not.toHaveTextContent('*');

    await selectAuthType(user, 'Token em header customizado');

    expect(screen.getByTestId('type-change-warning')).toHaveTextContent(
      'O tipo mudou de Bearer Token para Token em header customizado. Informe a credencial nova: a credencial salva era do tipo anterior e não é reaproveitada.',
    );
    expect(fieldLabel('Token')).toHaveTextContent('*');
    expect(screen.getByLabelText(/^token/i)).toHaveAttribute('placeholder', 'Credencial de acesso');
  });

  it('troca para Nenhuma avisa que a credencial atual será apagada', async () => {
    const user = userEvent.setup();
    renderEdit('CustomHeader');

    await selectAuthType(user, 'Nenhuma');

    expect(screen.getByTestId('credential-removal-warning')).toHaveTextContent(
      'Ao salvar, a credencial atual (Token em header customizado) é apagada. Ela não fica guardada sem uso.',
    );
    expect(screen.queryByLabelText(/^token/i)).not.toBeInTheDocument();
  });

  it('saída de Nenhuma não avisa sobre credencial anterior, mas a exige', async () => {
    const user = userEvent.setup();
    renderEdit('None');

    await selectAuthType(user, 'Bearer Token');

    expect(screen.queryByTestId('type-change-warning')).not.toBeInTheDocument();
    expect(screen.queryByTestId('credential-removal-warning')).not.toBeInTheDocument();
    expect(fieldLabel('Credencial')).toHaveTextContent('*');
  });

  it('o teste de token em header customizado envia o nome do header', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue(ok);
    const user = userEvent.setup();
    renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    await screen.findByText('Conexão bem-sucedida');
    expect(testUnsavedMcpServerConnection).toHaveBeenCalledWith({
      url: 'https://mcp.exemplo.internal/mcp',
      authType: 'CustomHeader',
      authHeaderName: 'X-API-TOKEN',
      credential: 'segredo-123',
    });
  });

  it('o teste sem o nome do header é barrado antes da requisição', async () => {
    const user = userEvent.setup();
    renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(
      await screen.findByText('Informe o nome do header antes de testar.'),
    ).toBeInTheDocument();
    expect(testUnsavedMcpServerConnection).not.toHaveBeenCalled();
  });

  it('o teste sem o token é barrado antes da requisição', async () => {
    const user = userEvent.setup();
    renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Informe o token antes de testar.')).toBeInTheDocument();
    expect(testUnsavedMcpServerConnection).not.toHaveBeenCalled();
  });

  it('o 400 do teste vai para os campos do header e do token, com aviso e sem "Falha na conexão"', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: {
          authHeaderName: [
            'O header Content-Type não pode ser usado em uma requisição (é um header de conteúdo ou de controle do .NET).',
          ],
          credential: ['A credencial não pode começar nem terminar com espaço.'],
        },
      }),
    );
    const user = userEvent.setup();
    renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'Content-Type');
    await user.type(screen.getByLabelText(/^token/i), ' segredo');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByTestId('connection-test-invalid')).toHaveTextContent(
      'Corrija os campos destacados antes de testar. Nenhuma conexão foi tentada.',
    );
    expect(
      screen.getByText(
        'O header Content-Type não pode ser usado em uma requisição (é um header de conteúdo ou de controle do .NET).',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText('A credencial não pode começar nem terminar com espaço.'),
    ).toBeInTheDocument();
  });

  it('o 400 do teste não aparece como resultado de conexão', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: {
          authHeaderName: [
            'Headers com prefixo Mcp- são do protocolo MCP e não podem levar a credencial.',
          ],
        },
      }),
    );
    const user = userEvent.setup();
    renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'Mcp-Token');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    await screen.findByTestId('connection-test-invalid');
    expect(screen.queryByText('Falha na conexão')).not.toBeInTheDocument();
    expect(screen.queryByTestId('connection-test-result')).not.toBeInTheDocument();
    expect(screen.queryByTestId('connection-test-failure-reason')).not.toBeInTheDocument();
  });

  it('chave do 400 do teste sem campo na tela aparece no aviso', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { outroCampo: ['Mensagem de um campo que a tela não tem.'] },
      }),
    );
    const user = userEvent.setup();
    renderForm();

    await fillCreate(user);
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByTestId('connection-test-invalid')).toHaveTextContent(
      'Mensagem de um campo que a tela não tem.',
    );
  });

  it('a nota do teste com a configuração salva cita o nome do header com token em header customizado', () => {
    renderEdit('CustomHeader');

    expect(screen.getByTestId('saved-credential-test-note')).toHaveTextContent(
      'Sem digitar a credencial, o teste usa a configuração salva deste servidor, inclusive o nome do header: uma alteração no nome só é testada depois de salvar.',
    );
  });

  it('com Nenhuma na edição, testa a configuração digitada', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue(ok);
    const user = userEvent.setup();
    renderEdit('None');

    await user.clear(screen.getByLabelText(/url/i));
    await user.type(screen.getByLabelText(/url/i), 'https://mcp.exemplo.internal/novo');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    await screen.findByText('Conexão bem-sucedida');
    expect(testUnsavedMcpServerConnection).toHaveBeenCalledWith({
      url: 'https://mcp.exemplo.internal/novo',
      authType: 'None',
      credential: undefined,
    });
    expect(testSavedMcpServerConnection).not.toHaveBeenCalled();
  });

  it('com Nenhuma na edição, não mostra a nota de configuração salva', () => {
    renderEdit('None');

    expect(screen.queryByTestId('saved-credential-test-note')).not.toBeInTheDocument();
  });

  // Precedência entre o erro da página e o do formulário (design.md, D5).
  it('editar o campo esconde o erro do envio anterior', async () => {
    const user = userEvent.setup();
    renderEdit('CustomHeader', { authHeaderName: 'Mensagem do envio anterior.' });

    expect(screen.getByText('Mensagem do envio anterior.')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/nome do header/i), '-NOVO');

    expect(screen.queryByText('Mensagem do envio anterior.')).not.toBeInTheDocument();
  });

  it('depois de editar o campo, o 400 do teste substitui o erro do envio anterior', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockRejectedValue(
      new ApiError(400, 'Validação falhou', {
        title: 'Validação falhou',
        status: 400,
        errors: { authHeaderName: ['Mensagem do teste.'] },
      }),
    );
    const user = userEvent.setup();
    renderEdit('CustomHeader', { authHeaderName: 'Mensagem do envio anterior.' });

    await user.type(screen.getByLabelText(/nome do header/i), '-NOVO');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));

    expect(await screen.findByText('Mensagem do teste.')).toBeInTheDocument();
    expect(screen.queryByText('Mensagem do envio anterior.')).not.toBeInTheDocument();
  });

  // Guarda G1 (design.md, V4): sem o nome do header na comparação, o sucesso
  // continuaria na tela para um header que nunca foi testado.
  it('[guarda G1] editar o nome do header descarta o resultado do teste', async () => {
    vi.mocked(testUnsavedMcpServerConnection).mockResolvedValue(ok);
    const user = userEvent.setup();
    renderForm();

    await fillCreate(user);
    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');
    await user.type(screen.getByLabelText(/^token/i), 'segredo-123');
    await user.click(screen.getByRole('button', { name: /testar conexão/i }));
    expect(await screen.findByText('Conexão bem-sucedida')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/nome do header/i), 'S');

    expect(screen.queryByText('Conexão bem-sucedida')).not.toBeInTheDocument();
  });

  // Guarda G2 (design.md, D4): a regra anterior testava o servidor salvo, com o
  // tipo ANTIGO, quando a credencial ficava em branco.
  it('[guarda G2] trocar o tipo com a credencial em branco desabilita o teste, com nota', async () => {
    const user = userEvent.setup();
    renderEdit('BearerToken');

    await selectAuthType(user, 'Token em header customizado');
    await user.type(screen.getByLabelText(/nome do header/i), 'X-API-TOKEN');

    const testButton = screen.getByRole('button', { name: /testar conexão/i });
    expect(testButton).toBeDisabled();
    expect(screen.getByTestId('blocked-test-note')).toHaveTextContent(
      'Para testar o tipo novo, informe a credencial nova.',
    );
    expect(screen.queryByTestId('saved-credential-test-note')).not.toBeInTheDocument();

    fireEvent.click(testButton);

    expect(testSavedMcpServerConnection).not.toHaveBeenCalled();
    expect(testUnsavedMcpServerConnection).not.toHaveBeenCalled();
  });
});
