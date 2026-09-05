import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../theme';
import { UnsavedChangesModal } from './UnsavedChangesModal';

function renderModal(opened: boolean) {
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <UnsavedChangesModal
        opened={opened}
        message="As alterações deste vínculo serão descartadas."
        onConfirm={onConfirm}
        onCancel={onCancel}
      />
    </MantineProvider>,
  );
  return { onConfirm, onCancel };
}

describe('UnsavedChangesModal', () => {
  it('fechado, não exibe nada', () => {
    renderModal(false);

    expect(
      screen.queryByText('As alterações deste vínculo serão descartadas.'),
    ).not.toBeInTheDocument();
  });

  it('aberto, exibe a mensagem e as duas ações', () => {
    renderModal(true);

    expect(screen.getByText('As alterações deste vínculo serão descartadas.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continuar editando' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sair e descartar' })).toBeInTheDocument();
  });

  it('aciona confirmar e cancelar', async () => {
    const user = userEvent.setup();
    const { onConfirm, onCancel } = renderModal(true);

    await user.click(screen.getByRole('button', { name: 'Sair e descartar' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('button', { name: 'Continuar editando' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
