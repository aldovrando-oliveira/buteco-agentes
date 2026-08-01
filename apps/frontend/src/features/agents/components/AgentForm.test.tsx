import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentForm } from './AgentForm';
import type { ProviderCatalogEntry } from '../types/agent';

const defaultProviders: ProviderCatalogEntry[] = [
  { id: 'openai', models: ['gpt-5.6-sol', 'gpt-5.6-terra'] },
  { id: 'anthropic', models: ['claude-opus-5', 'claude-sonnet-5'] },
];

function renderForm(overrides?: {
  errors?: Record<string, string>;
  submitting?: boolean;
  providers?: ProviderCatalogEntry[];
}) {
  const onSubmit = vi.fn();
  const onCancel = vi.fn();
  const { providers, ...rest } = overrides ?? {};
  render(
    <MantineProvider theme={theme}>
      <AgentForm
        onSubmit={onSubmit}
        onCancel={onCancel}
        providers={providers ?? defaultProviders}
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

describe('AgentForm', () => {
  it('chama onSubmit com os valores quando o formulário é válido', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/nome/i), 'Atendente');
    await user.type(screen.getByLabelText(/instruções/i), 'Você é um atendente simpático.');
    await selectOption(user, /provedor/i, 'openai');
    await selectOption(user, /modelo/i, 'gpt-5.6-sol');
    await user.click(screen.getByRole('button', { name: /criar agente/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Atendente',
      instructions: 'Você é um atendente simpático.',
      provider: 'openai',
      model: 'gpt-5.6-sol',
    });
  });

  it('exibe erro de validação e não chama onSubmit quando os campos estão vazios', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.click(screen.getByRole('button', { name: /criar agente/i }));

    expect(await screen.findByText('O nome do agente é obrigatório.')).toBeInTheDocument();
    expect(
      screen.getByText('As instruções (system prompt) do agente são obrigatórias.'),
    ).toBeInTheDocument();
    expect(screen.getByText('O provedor de LLM do agente é obrigatório.')).toBeInTheDocument();
    expect(screen.getByText('O modelo do agente é obrigatório.')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('chama onCancel e não chama onSubmit quando o usuário clica em Cancelar', async () => {
    const user = userEvent.setup();
    const { onSubmit, onCancel } = renderForm();

    await user.type(screen.getByLabelText(/nome/i), 'Atendente');
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('pré-preenche os campos com initialValues quando fornecido', () => {
    render(
      <MantineProvider theme={theme}>
        <AgentForm
          onSubmit={vi.fn()}
          onCancel={vi.fn()}
          providers={defaultProviders}
          initialValues={{
            name: 'Atendente',
            instructions: 'Você é um atendente simpático.',
            provider: 'openai',
            model: 'gpt-5.6-sol',
          }}
        />
      </MantineProvider>,
    );

    expect(screen.getByLabelText(/nome/i)).toHaveValue('Atendente');
    expect(screen.getByLabelText(/instruções/i)).toHaveValue('Você é um atendente simpático.');
    expect(getSelect(/provedor/i)).toHaveValue('openai');
    expect(getSelect(/modelo/i)).toHaveValue('gpt-5.6-sol');
  });

  it('usa o rótulo do botão informado em submitLabel', () => {
    render(
      <MantineProvider theme={theme}>
        <AgentForm
          onSubmit={vi.fn()}
          onCancel={vi.fn()}
          providers={defaultProviders}
          submitLabel="Salvar alterações"
        />
      </MantineProvider>,
    );

    expect(screen.getByRole('button', { name: 'Salvar alterações' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /criar agente/i })).not.toBeInTheDocument();
  });

  it('Select de Modelo filtra pelas opções do Provedor escolhido', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectOption(user, /provedor/i, 'anthropic');
    await user.click(getSelect(/modelo/i));

    expect(screen.getByRole('option', { name: 'claude-opus-5' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'gpt-5.6-sol' })).not.toBeInTheDocument();
  });

  it('trocar o Provedor depois de selecionar um Modelo reseta o Modelo', async () => {
    const user = userEvent.setup();
    renderForm();

    await selectOption(user, /provedor/i, 'openai');
    await selectOption(user, /modelo/i, 'gpt-5.6-sol');
    expect(getSelect(/modelo/i)).toHaveValue('gpt-5.6-sol');

    await selectOption(user, /provedor/i, 'anthropic');

    expect(getSelect(/modelo/i)).toHaveValue('');
  });

  it('não reseta o Modelo no mount inicial em modo edição, mesmo com um par provider/model fora das opções disponíveis', () => {
    render(
      <MantineProvider theme={theme}>
        <AgentForm
          onSubmit={vi.fn()}
          onCancel={vi.fn()}
          providers={defaultProviders}
          initialValues={{
            name: 'Atendente',
            instructions: 'Você é um atendente simpático.',
            provider: 'gemini',
            model: 'gemini-3.6-flash',
          }}
        />
      </MantineProvider>,
    );

    expect(getSelect(/provedor/i)).toHaveValue('gemini (indisponível)');
    expect(getSelect(/modelo/i)).toHaveValue('gemini-3.6-flash (indisponível)');
  });

  it('exibe o provider e o model do agente como opção informativa e não re-selecionável quando não constam nas opções disponíveis', async () => {
    const user = userEvent.setup();
    render(
      <MantineProvider theme={theme}>
        <AgentForm
          onSubmit={vi.fn()}
          onCancel={vi.fn()}
          providers={defaultProviders}
          initialValues={{
            name: 'Atendente',
            instructions: 'Você é um atendente simpático.',
            provider: 'gemini',
            model: 'gemini-3.6-flash',
          }}
        />
      </MantineProvider>,
    );

    await user.click(getSelect(/provedor/i));
    const staleProviderOption = screen.getByRole('option', { name: 'gemini (indisponível)' });
    expect(staleProviderOption).toHaveAttribute('data-combobox-disabled', 'true');

    await user.click(getSelect(/modelo/i));
    const staleModelOption = screen.getByRole('option', {
      name: 'gemini-3.6-flash (indisponível)',
    });
    expect(staleModelOption).toHaveAttribute('data-combobox-disabled', 'true');
  });
});
