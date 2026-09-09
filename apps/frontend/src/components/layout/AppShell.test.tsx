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
function renderAppShell(initialEntry = '/agents') {
  return render(
    <MantineProvider theme={theme} defaultColorScheme="auto">
      <MemoryRouter initialEntries={[initialEntry]}>
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

  it('lista os itens de navegação Agentes, Servidores MCP, Conhecimento e Canais', () => {
    renderAppShell();

    expect(screen.getByRole('link', { name: 'Agentes' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Conhecimento' })).toHaveAttribute(
      'href',
      '/knowledge-bases',
    );
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

  it('dá um ícone decorativo a cada item, sem roubar o nome acessível', () => {
    const { container } = renderAppShell();

    // O rótulo textual continua sendo o nome acessível; o ícone é decoração.
    for (const label of ['Agentes', 'Servidores MCP', 'Conhecimento', 'Canais']) {
      const link = screen.getByRole('link', { name: label });
      const icon = link.querySelector('svg');

      expect(icon).not.toBeNull();
      expect(icon).toHaveAttribute('aria-hidden', 'true');
    }

    expect(container.querySelectorAll('a svg')).toHaveLength(4);
  });

  it.each([
    ['/agents/abc-123', 'Agentes'],
    ['/agents/new', 'Agentes'],
    ['/mcp-servers/abc-123/edit', 'Servidores MCP'],
    ['/knowledge-bases', 'Conhecimento'],
    ['/knowledge-bases/abc-123', 'Conhecimento'],
    ['/knowledge-bases/new', 'Conhecimento'],
    ['/knowledge-bases/abc-123/edit', 'Conhecimento'],
    ['/channels/abc-123', 'Canais'],
  ])('mantém o item ativo em %s', (rota, ativo) => {
    renderAppShell(rota);

    // Rota profunda de detalhe, criação e edição continua marcando o grupo —
    // não só a raiz dele.
    expect(screen.getByRole('link', { name: ativo })).toHaveAttribute('data-active', 'true');

    for (const outro of ['Agentes', 'Servidores MCP', 'Conhecimento', 'Canais'].filter(
      (l) => l !== ativo,
    )) {
      expect(screen.getByRole('link', { name: outro })).not.toHaveAttribute('data-active');
    }
  });

  it('exibe a identificação do produto uma única vez', () => {
    renderAppShell();

    expect(screen.getAllByText('Buteco Agentes')).toHaveLength(1);
  });

  it('não afirma a identidade do operador no rodapé', () => {
    const { container } = renderAppShell();

    // O login devolve só credencial e validade: não há nome nem e-mail a
    // exibir, e inventá-los seria afirmar o que o backend não confirma.
    expect(container.querySelector('img')).toBeNull();
    expect(screen.queryByText(/@/)).not.toBeInTheDocument();
  });

  it('não renderiza barra superior nem controle de gaveta', () => {
    renderAppShell();

    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /toggle navigation/i })).not.toBeInTheDocument();
  });
});
