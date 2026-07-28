import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentForm } from './AgentForm';

function renderForm(overrides?: { errors?: Record<string, string>; submitting?: boolean }) {
  const onSubmit = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <AgentForm onSubmit={onSubmit} {...overrides} />
    </MantineProvider>,
  );
  return { onSubmit };
}

describe('AgentForm', () => {
  it('chama onSubmit com os valores quando o formulário é válido', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/nome/i), 'Atendente');
    await user.type(screen.getByLabelText(/instruções/i), 'Você é um atendente simpático.');
    await user.click(screen.getByRole('button', { name: /criar agente/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Atendente',
      instructions: 'Você é um atendente simpático.',
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
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('pré-preenche os campos com initialValues quando fornecido', () => {
    render(
      <MantineProvider theme={theme}>
        <AgentForm
          onSubmit={vi.fn()}
          initialValues={{ name: 'Atendente', instructions: 'Você é um atendente simpático.' }}
        />
      </MantineProvider>,
    );

    expect(screen.getByLabelText(/nome/i)).toHaveValue('Atendente');
    expect(screen.getByLabelText(/instruções/i)).toHaveValue('Você é um atendente simpático.');
  });

  it('usa o rótulo do botão informado em submitLabel', () => {
    render(
      <MantineProvider theme={theme}>
        <AgentForm onSubmit={vi.fn()} submitLabel="Salvar alterações" />
      </MantineProvider>,
    );

    expect(screen.getByRole('button', { name: 'Salvar alterações' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /criar agente/i })).not.toBeInTheDocument();
  });
});
