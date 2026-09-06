import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { AppShell } from './AppShell';
import { theme } from '../../theme';

// O mesmo defaultColorScheme da aplicação: sem preferência salva, o esquema
// vem do sistema operacional. O matchMedia de src/test/setup.ts responde
// `matches: false`, então o padrão da suíte é claro; prefersDark() troca isso
// para o caso em que o SO está no escuro.
function renderAppShell() {
  return render(
    <MantineProvider theme={theme} defaultColorScheme="auto">
      <MemoryRouter initialEntries={['/agents']}>
        <AppShell />
      </MemoryRouter>
    </MantineProvider>,
  );
}

function prefersDark() {
  const original = window.matchMedia;

  window.matchMedia = ((query: string) => ({
    ...original(query),
    matches: query.includes('dark'),
  })) as typeof window.matchMedia;

  return () => {
    window.matchMedia = original;
  };
}

describe('AppShell', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('lista os itens de navegação Agentes, Servidores MCP e Canais', () => {
    renderAppShell();

    expect(screen.getByRole('link', { name: 'Agentes' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Canais' })).toBeInTheDocument();
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
    expect(screen.queryByText('Inboxes')).not.toBeInTheDocument();
  });

  it('segue a preferência clara do sistema operacional, sem preferência salva', () => {
    renderAppShell();

    expect(screen.getByRole('button', { name: 'Mudar para tema escuro' })).toBeInTheDocument();
  });

  it('segue a preferência escura do sistema operacional, sem preferência salva', () => {
    const restore = prefersDark();

    try {
      renderAppShell();

      expect(screen.getByRole('button', { name: 'Mudar para tema claro' })).toBeInTheDocument();
    } finally {
      restore();
    }
  });

  it('a escolha manual se sobrepõe à preferência do sistema operacional', async () => {
    const user = userEvent.setup();
    const restore = prefersDark();

    try {
      renderAppShell();

      await user.click(screen.getByRole('button', { name: 'Mudar para tema claro' }));

      expect(screen.getByRole('button', { name: 'Mudar para tema escuro' })).toBeInTheDocument();
    } finally {
      restore();
    }
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
