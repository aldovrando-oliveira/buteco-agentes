import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../theme';
import { UnsavedChangesBar } from './UnsavedChangesBar';

function renderBar(overrides?: Partial<Parameters<typeof UnsavedChangesBar>[0]>) {
  const onDiscard = vi.fn();
  const onSave = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <UnsavedChangesBar
        message="Alterações não salvas neste vínculo"
        saveLabel="Salvar vínculo"
        onDiscard={onDiscard}
        onSave={onSave}
        {...overrides}
      />
    </MantineProvider>,
  );
  return { onDiscard, onSave };
}

describe('UnsavedChangesBar', () => {
  it('exibe a mensagem e as duas ações', () => {
    renderBar();

    expect(screen.getByText('Alterações não salvas neste vínculo')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descartar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Salvar vínculo' })).toBeInTheDocument();
  });

  it('aciona os callbacks de descartar e salvar', async () => {
    const user = userEvent.setup();
    const { onDiscard, onSave } = renderBar();

    await user.click(screen.getByRole('button', { name: 'Descartar' }));
    await user.click(screen.getByRole('button', { name: 'Salvar vínculo' }));

    expect(onDiscard).toHaveBeenCalledTimes(1);
    expect(onSave).toHaveBeenCalledTimes(1);
  });

  it('durante o salvamento troca a mensagem, sem exibir contagem, e desabilita descartar', () => {
    renderBar({ saving: true, savingMessage: 'Validando as tools nos servidores MCP…' });

    expect(screen.getByText('Validando as tools nos servidores MCP…')).toBeInTheDocument();
    expect(screen.queryByText('Alterações não salvas neste vínculo')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descartar' })).toBeDisabled();
  });

  it('durante o salvamento a ação de salvar fica inerte', async () => {
    const user = userEvent.setup();
    const { onSave } = renderBar({ saving: true });

    await user.click(screen.getByRole('button', { name: 'Salvar vínculo' }));

    expect(onSave).not.toHaveBeenCalled();
  });
});
