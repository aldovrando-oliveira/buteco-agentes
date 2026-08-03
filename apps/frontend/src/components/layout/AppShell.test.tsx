import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { AppShell } from './AppShell';
import { theme } from '../../theme';

function renderAppShell() {
  return render(
    <MantineProvider theme={theme} defaultColorScheme="light">
      <MemoryRouter initialEntries={['/agents']}>
        <AppShell />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('AppShell', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('lista os itens de navegação Agentes e Servidores MCP', () => {
    renderAppShell();

    expect(screen.getByRole('link', { name: 'Agentes' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toBeInTheDocument();
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
    expect(screen.queryByText('Inboxes')).not.toBeInTheDocument();
  });

  it('abre no tema claro por padrão, sem nenhuma preferência salva', () => {
    renderAppShell();

    expect(screen.getByRole('button', { name: 'Mudar para tema escuro' })).toBeInTheDocument();
  });

  it('persiste a alternância de tema entre sessões', async () => {
    const user = userEvent.setup();
    const { unmount } = renderAppShell();

    await user.click(screen.getByRole('button', { name: 'Mudar para tema escuro' }));
    expect(screen.getByRole('button', { name: 'Mudar para tema claro' })).toBeInTheDocument();

    unmount();
    renderAppShell();

    expect(screen.getByRole('button', { name: 'Mudar para tema claro' })).toBeInTheDocument();
  });
});
