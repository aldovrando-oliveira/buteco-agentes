import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { ChannelForm } from './ChannelForm';
import type { Agent } from '../../agents/types/agent';

const activeAgent: Agent = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
  knowledgeBases: [],
  a2a: null,
};

const inactiveAgent: Agent = {
  ...activeAgent,
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Vendedor',
  isActive: false,
};

const defaultAgents = [activeAgent, inactiveAgent];

function renderForm(overrides?: {
  errors?: Record<string, string>;
  credentialErrors?: string[];
  submitting?: boolean;
  initialValues?: Parameters<typeof ChannelForm>[0]['initialValues'];
  submitLabel?: string;
  mode?: 'create' | 'edit';
  agents?: Agent[];
}) {
  const onSubmit = vi.fn();
  const onCancel = vi.fn();
  const { agents, ...rest } = overrides ?? {};
  render(
    <MantineProvider theme={theme}>
      <ChannelForm
        onSubmit={onSubmit}
        onCancel={onCancel}
        agents={agents ?? defaultAgents}
        {...rest}
      />
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

describe('ChannelForm', () => {
  it('cadastro de canal WAHA com sucesso envia credential serializado em JSON', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByRole('textbox', { name: 'Nome' }), 'Canal WAHA');
    await selectOption(user, /agente responsável/i, activeAgent.name);
    await user.type(screen.getByLabelText(/url do serviço/i), 'http://localhost:3000');
    await user.type(screen.getByLabelText(/nome da sessão/i), 'default');
    await user.type(screen.getByLabelText(/token de autenticação/i), 'token-abc');
    await user.click(screen.getByRole('button', { name: /cadastrar canal/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Canal WAHA',
      agentId: activeAgent.id,
      channelType: 'waha',
      credential: JSON.stringify({
        ServiceUrl: 'http://localhost:3000',
        SessionName: 'default',
        AuthToken: 'token-abc',
      }),
    });
  });

  it('cadastro de canal Telegram com sucesso envia credential serializado em JSON', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByRole('textbox', { name: 'Nome' }), 'Canal Telegram');
    await selectOption(user, /agente responsável/i, activeAgent.name);
    await selectOption(user, /tipo de canal/i, 'Telegram');
    await user.type(screen.getByLabelText(/token do bot/i), 'bot-token-123');
    await user.click(screen.getByRole('button', { name: /cadastrar canal/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Canal Telegram',
      agentId: activeAgent.id,
      channelType: 'telegram',
      credential: JSON.stringify({ BotToken: 'bot-token-123' }),
    });
  });

  it('troca de tipo de canal troca o sub-formulário de credencial exibido', async () => {
    const user = userEvent.setup();
    renderForm();

    expect(screen.getByLabelText(/url do serviço/i)).toBeInTheDocument();

    await selectOption(user, /tipo de canal/i, 'Telegram');

    expect(screen.queryByLabelText(/url do serviço/i)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/token do bot/i)).toBeInTheDocument();
  });

  it('campo de agente responsável inclui agentes inativos com rótulo distinto', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.click(getSelect(/agente responsável/i));

    expect(screen.getByRole('option', { name: activeAgent.name })).toBeInTheDocument();
    expect(
      screen.getByRole('option', { name: `${inactiveAgent.name} (inativo)` }),
    ).toBeInTheDocument();
  });

  it('erro 400 de credencial com múltiplas mensagens exibe todas, não só a primeira', () => {
    renderForm({
      credentialErrors: [
        'serviceUrl deve ser uma URL absoluta (ex. http://localhost:3000).',
        'sessionName é obrigatório.',
      ],
    });

    expect(
      screen.getByText('serviceUrl deve ser uma URL absoluta (ex. http://localhost:3000).'),
    ).toBeInTheDocument();
    expect(screen.getByText('sessionName é obrigatório.')).toBeInTheDocument();
  });

  it('em modo edição, tipo de canal aparece como texto fixo, não como Select editável', async () => {
    renderForm({
      mode: 'edit',
      initialValues: { name: 'Canal WAHA', agentId: activeAgent.id, channelType: 'waha' },
    });

    expect(screen.queryByRole('combobox', { name: /tipo de canal/i })).not.toBeInTheDocument();
    const channelTypeField = screen.getByLabelText(/tipo de canal/i);
    expect(channelTypeField).toHaveValue('WAHA');
    expect(channelTypeField).toBeDisabled();
  });

  it('em modo edição, preenchimento parcial da credencial bloqueia o submit', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm({
      mode: 'edit',
      submitLabel: 'Salvar alterações',
      initialValues: { name: 'Canal WAHA', agentId: activeAgent.id, channelType: 'waha' },
    });

    await user.type(screen.getByLabelText(/token de autenticação/i), 'novo-token');
    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(await screen.findByText('A URL do serviço é obrigatória.')).toBeInTheDocument();
    expect(screen.getByText('O nome da sessão é obrigatório.')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('em modo edição, deixar toda a credencial em branco preserva a persistida (credential undefined)', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm({
      mode: 'edit',
      submitLabel: 'Salvar alterações',
      initialValues: { name: 'Canal WAHA', agentId: activeAgent.id, channelType: 'waha' },
    });

    await user.click(screen.getByRole('button', { name: /salvar alterações/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Canal WAHA',
      agentId: activeAgent.id,
      channelType: 'waha',
      credential: undefined,
    });
  });

  it('exibe erro de validação e não chama onSubmit quando nome e agente estão vazios', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.click(screen.getByRole('button', { name: /cadastrar canal/i }));

    expect(await screen.findByText('O nome do canal é obrigatório.')).toBeInTheDocument();
    expect(screen.getByText('O agente responsável pelo canal é obrigatório.')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('chama onCancel e não chama onSubmit quando o usuário clica em Cancelar', async () => {
    const user = userEvent.setup();
    const { onSubmit, onCancel } = renderForm();

    await user.type(screen.getByRole('textbox', { name: 'Nome' }), 'Canal WAHA');
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
