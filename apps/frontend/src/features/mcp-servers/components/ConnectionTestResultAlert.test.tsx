import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { ConnectionTestResultAlert } from './ConnectionTestResultAlert';

function renderAlert(result: { success: boolean; message: string | null } | undefined) {
  return render(
    <MantineProvider theme={theme}>
      <ConnectionTestResultAlert result={result} />
    </MantineProvider>,
  );
}

describe('ConnectionTestResultAlert', () => {
  it('não renderiza nada quando não há resultado', () => {
    renderAlert(undefined);

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('exibe uma indicação de sucesso quando o teste é bem-sucedido', () => {
    renderAlert({ success: true, message: null });

    expect(screen.getByText('Conexão bem-sucedida')).toBeInTheDocument();
  });

  it('exibe o motivo da falha quando o teste falha', () => {
    renderAlert({ success: false, message: 'Não foi possível conectar ao host informado.' });

    expect(screen.getByText('Falha na conexão')).toBeInTheDocument();
    expect(screen.getByText('Não foi possível conectar ao host informado.')).toBeInTheDocument();
  });
});
