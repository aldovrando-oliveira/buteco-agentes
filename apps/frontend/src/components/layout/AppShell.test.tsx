import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { AppShell } from './AppShell';
import { theme } from '../../theme';
import { authRuntime } from '../../auth/session';
import { SESSION_LOCK } from '../../auth/renewal';
import { SESSION_KEY } from '../../auth/sessionStore';
import { deferred } from '../../test/fakeLocks';
import { signInForTest } from '../../test/authFakes';

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

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('lista os itens de navegação Inventário, Agentes, Servidores MCP, Conhecimento e Canais', () => {
    renderAppShell();

    // A negativa de 'Dashboard' INVERTEU, e o motivo está no design.md: a
    // decisão de 2026-07-27 removeu o item porque não existia página, e escreveu
    // a condição de reabertura — "reintroduzi-los quando a página existir de
    // fato". A página existe; o item volta, com o nome que a tela tem.
    //
    // 'Inboxes' continua ausente: aquele item segue sem página.
    expect(screen.getByRole('link', { name: 'Inventário' })).toHaveAttribute('href', '/inventory');
    expect(screen.getByRole('link', { name: 'Agentes' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Conhecimento' })).toHaveAttribute(
      'href',
      '/knowledge-bases',
    );
    expect(screen.getByRole('link', { name: 'Canais' })).toBeInTheDocument();
    // Insights entra pela mesma régua que reabriu o Inventário: o item existe
    // porque a página existe.
    expect(screen.getByRole('link', { name: 'Insights' })).toHaveAttribute('href', '/insights');
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
    for (const label of [
      'Inventário',
      'Agentes',
      'Servidores MCP',
      'Conhecimento',
      'Canais',
      'Insights',
    ]) {
      const link = screen.getByRole('link', { name: label });
      const icon = link.querySelector('svg');

      expect(icon).not.toBeNull();
      expect(icon).toHaveAttribute('aria-hidden', 'true');
    }

    expect(container.querySelectorAll('a svg')).toHaveLength(6);
  });

  it.each([
    ['/inventory', 'Inventário'],
    ['/agents/abc-123', 'Agentes'],
    ['/agents/new', 'Agentes'],
    ['/mcp-servers/abc-123/edit', 'Servidores MCP'],
    ['/knowledge-bases', 'Conhecimento'],
    ['/knowledge-bases/abc-123', 'Conhecimento'],
    ['/knowledge-bases/new', 'Conhecimento'],
    ['/knowledge-bases/abc-123/edit', 'Conhecimento'],
    ['/channels/abc-123', 'Canais'],
    ['/insights', 'Insights'],
  ])('mantém o item ativo em %s', (rota, ativo) => {
    renderAppShell(rota);

    // Rota profunda de detalhe, criação e edição continua marcando o grupo —
    // não só a raiz dele.
    expect(screen.getByRole('link', { name: ativo })).toHaveAttribute('data-active', 'true');

    for (const outro of [
      'Inventário',
      'Agentes',
      'Servidores MCP',
      'Conhecimento',
      'Canais',
      'Insights',
    ].filter((l) => l !== ativo)) {
      expect(screen.getByRole('link', { name: outro })).not.toHaveAttribute('data-active');
    }
  });

  it('exibe a identificação do produto uma única vez', () => {
    renderAppShell();

    expect(screen.getAllByText('Buteco Agentes')).toHaveLength(1);
  });

  // O rodapé do protótipo de autenticação (quadro "Editar usuário"): avatar com as
  // iniciais, o nome e o e-mail do id token em duas linhas, e os botões de tema e de
  // Sair, nesta ordem.
  function rodape(container: HTMLElement) {
    const sair = screen.getByRole('button', { name: /^(Sair|Saindo…)$/ });
    const secao = sair.closest('.mantine-AppShell-section') as HTMLElement;
    expect(secao).not.toBeNull();
    return { secao, avatar: secao.querySelector('.mantine-Avatar-root'), container };
  }

  it('nome com duas palavras: avatar com as duas iniciais, o nome e, abaixo, o e-mail', () => {
    signInForTest();
    const { container } = renderAppShell();
    const { secao, avatar } = rodape(container);

    expect(avatar).toHaveTextContent(/^AS$/);
    expect(avatar).toHaveAttribute('aria-hidden', 'true');
    const textos = [...secao.querySelectorAll('p')].map((p) => p.textContent);
    expect(textos).toEqual(['Ana Souza', 'ana@exemplo.test']);
    const botoes = [...secao.querySelectorAll('button')].map((b) => b.getAttribute('aria-label'));
    expect(botoes).toEqual(['Mudar para tema escuro', 'Sair']);
    expect(screen.getByRole('button', { name: 'Sair' })).toHaveAttribute('title', 'Sair');
  });

  it('nome de uma palavra: avatar com uma inicial', () => {
    const s = signInForTest();
    localStorage.setItem(SESSION_KEY, JSON.stringify({ ...s, nome: 'admin', email: 'admin@exemplo.test' }));
    const { container } = renderAppShell();
    const { secao, avatar } = rodape(container);

    expect(avatar).toHaveTextContent(/^A$/);
    expect([...secao.querySelectorAll('p')].map((p) => p.textContent)).toEqual(['admin', 'admin@exemplo.test']);
  });

  it('sem nome no id token, o e-mail vai na primeira linha e a segunda fica vazia', () => {
    const s = signInForTest();
    localStorage.setItem(SESSION_KEY, JSON.stringify({ ...s, nome: null, email: 'bia@exemplo.test' }));
    const { container } = renderAppShell();
    const { secao, avatar } = rodape(container);

    expect(avatar).toHaveTextContent(/^B$/);
    expect([...secao.querySelectorAll('p')].map((p) => p.textContent)).toEqual(['bia@exemplo.test']);
    expect(screen.getByRole('button', { name: 'Sair' })).toBeInTheDocument();
  });

  it('Sair mostra "Saindo…" enquanto espera o lock, e depois vai ao logout do emissor sem o registro', async () => {
    const s = signInForTest();
    const assign = vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
    const renovacao = deferred<void>();
    // Uma renovação "em voo" em outra aba segura o lock.
    const held = navigator.locks.request(SESSION_LOCK, () => renovacao.promise);
    const user = userEvent.setup();
    renderAppShell();

    await user.click(screen.getByRole('button', { name: 'Sair' }));

    const saindo = screen.getByRole('button', { name: 'Saindo…' });
    expect(saindo).toBeDisabled();
    // O indicador de carregamento no lugar do ícone: o Mantine esconde o ícone com
    // `data-loading` e monta o loader por uma transição.
    expect(saindo).toHaveAttribute('data-loading', 'true');
    await waitFor(() => expect(saindo.querySelector('.mantine-ActionIcon-loader')).not.toBeNull());
    expect(assign).not.toHaveBeenCalled();
    renovacao.resolve();
    await held;
    await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
    expect(localStorage.getItem(SESSION_KEY)).toBeNull();
    const url = new URL(assign.mock.calls[0][0], 'http://localhost');
    expect(url.pathname).toBe('/connect/logout');
    expect(url.searchParams.get('id_token_hint')).toBe(s.idToken);
  });

  it('não renderiza barra superior nem controle de gaveta', () => {
    renderAppShell();

    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /toggle navigation/i })).not.toBeInTheDocument();
  });
});
