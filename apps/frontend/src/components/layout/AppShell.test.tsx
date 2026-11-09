import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { AppShell } from './AppShell';
import { theme } from '../../theme';
import { authRuntime, sessionFetch, sessionStore } from '../../auth/session';
import { SESSION_LOCK } from '../../auth/renewal';
import { SESSION_KEY } from '../../auth/sessionStore';
import { deferred } from '../../test/fakeLocks';
import { fakeJwt, session, signInAsRole, signInForTest, tokenResponse } from '../../test/authFakes';
import { act } from '@testing-library/react';

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
    // O clear apaga também a sessão de Admin do setup (D11 da papeis-no-painel): sem ela, o
    // papel é desconhecido e a seção Acesso some. Os casos de outro papel gravam o seu.
    signInForTest();
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
      'Usuários',
      'Identidade',
    ]) {
      const link = screen.getByRole('link', { name: label });
      const icon = link.querySelector('svg');

      expect(icon).not.toBeNull();
      expect(icon).toHaveAttribute('aria-hidden', 'true');
    }

    // 8 desde a seção Acesso (Usuários e Identidade; change usuarios-convites-identidade-painel).
    expect(container.querySelectorAll('a svg')).toHaveLength(8);
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

  // O rodapé do protótipo de autenticação (quadros da entrega `m2`): avatar com as
  // iniciais, o nome e o papel do id token em duas linhas, e os botões de tema e de
  // Sair, nesta ordem. A segunda linha passou do e-mail ao papel com a #185 (change
  // papeis-no-painel; caso que muda de asserção, listado no tasks.md).
  function rodape(container: HTMLElement) {
    const sair = screen.getByRole('button', { name: /^(Sair|Saindo…)$/ });
    const secao = sair.closest('.mantine-AppShell-section') as HTMLElement;
    expect(secao).not.toBeNull();
    return { secao, avatar: secao.querySelector('.mantine-Avatar-root'), container };
  }

  it('nome com duas palavras: avatar com as duas iniciais, o nome e, abaixo, o papel', () => {
    signInForTest();
    const { container } = renderAppShell();
    const { secao, avatar } = rodape(container);

    expect(avatar).toHaveTextContent(/^AS$/);
    expect(avatar).toHaveAttribute('aria-hidden', 'true');
    const textos = [...secao.querySelectorAll('p')].map((p) => p.textContent);
    expect(textos).toEqual(['Ana Souza', 'Admin']);
    const botoes = [...secao.querySelectorAll('button')].map((b) => b.getAttribute('aria-label'));
    expect(botoes).toEqual(['Mudar para tema escuro', 'Sair']);
    expect(screen.getByRole('button', { name: 'Sair' })).toHaveAttribute('title', 'Sair');
  });

  it('nome de uma palavra: avatar com uma inicial', () => {
    const s = signInForTest();
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ ...s, nome: 'admin', email: 'admin@exemplo.test' }),
    );
    const { container } = renderAppShell();
    const { secao, avatar } = rodape(container);

    expect(avatar).toHaveTextContent(/^A$/);
    expect([...secao.querySelectorAll('p')].map((p) => p.textContent)).toEqual(['admin', 'Admin']);
  });

  it('sem nome no id token, o e-mail vai na primeira linha e o papel na segunda', () => {
    const s = signInForTest();
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ ...s, nome: null, email: 'bia@exemplo.test' }),
    );
    const { container } = renderAppShell();
    const { secao, avatar } = rodape(container);

    expect(avatar).toHaveTextContent(/^B$/);
    expect([...secao.querySelectorAll('p')].map((p) => p.textContent)).toEqual([
      'bia@exemplo.test',
      'Admin',
    ]);
    expect(screen.getByRole('button', { name: 'Sair' })).toBeInTheDocument();
  });

  it('O papel na segunda linha', () => {
    const s = signInAsRole('leitor')!;
    localStorage.setItem(SESSION_KEY, JSON.stringify({ ...s, nome: 'Rafael Duarte' }));
    const { container } = renderAppShell();
    const { secao } = rodape(container);

    expect([...secao.querySelectorAll('p')].map((p) => p.textContent)).toEqual([
      'Rafael Duarte',
      'Leitor',
    ]);
    expect(secao).not.toHaveTextContent('ana@exemplo.test');
  });

  it('Nome de uma palavra, com o papel Operador', () => {
    const s = signInAsRole('operador')!;
    localStorage.setItem(SESSION_KEY, JSON.stringify({ ...s, nome: 'admin' }));
    const { container } = renderAppShell();
    const { secao } = rodape(container);

    expect([...secao.querySelectorAll('p')].map((p) => p.textContent)).toEqual([
      'admin',
      'Operador',
    ]);
  });

  it('Papel desconhecido', () => {
    const s = signInForTest();
    localStorage.setItem(
      SESSION_KEY,
      JSON.stringify({ ...s, idToken: fakeJwt({ name: 'Ana Souza' }) }),
    );
    const { container } = renderAppShell();
    const { secao } = rodape(container);

    expect([...secao.querySelectorAll('p')].map((p) => p.textContent)).toEqual(['Ana Souza']);
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

  // O bloco do nome leva a Minha conta (spec `frontend-app-shell`; design.md da change
  // minha-conta-painel, D1), sem item de menu.
  describe('seção Acesso', () => {
    it('depois de Insights vêm o rótulo Acesso e os links Usuários e Identidade', () => {
      renderAppShell();

      const nav = screen.getByRole('navigation');
      const textos = Array.from(nav.querySelectorAll('a, p')).map((el) => el.textContent?.trim());
      const insights = textos.indexOf('Insights');
      expect(textos.slice(insights, insights + 4)).toEqual([
        'Insights',
        'Acesso',
        'Usuários',
        'Identidade',
      ]);
      expect(screen.getByRole('link', { name: 'Usuários' })).toHaveAttribute('href', '/users');
      expect(screen.getByRole('link', { name: 'Identidade' })).toHaveAttribute('href', '/identity');
    });

    it('o rótulo Acesso não é link', () => {
      renderAppShell();

      expect(screen.getByText('Acesso').closest('a')).toBeNull();
      expect(screen.queryByRole('link', { name: 'Acesso' })).not.toBeInTheDocument();
    });

    it('em /users, o item Usuários fica ativo, e Identidade não', () => {
      renderAppShell('/users');

      expect(screen.getByRole('link', { name: 'Usuários' })).toHaveAttribute('data-active', 'true');
      expect(screen.getByRole('link', { name: 'Identidade' })).not.toHaveAttribute('data-active');
    });

    it('o menu não tem Aplicações externas', () => {
      renderAppShell();

      expect(screen.queryByText('Aplicações externas')).not.toBeInTheDocument();
    });

    // Spec `frontend-app-shell`, "Seção Acesso na navegação", e `panel-roles` (change
    // papeis-no-painel). O papel é gravado explicitamente: o padrão do setup é o Admin.
    it.each(['operador', 'leitor'] as const)('Operador e Leitor sem a seção: %s', (papel) => {
      signInAsRole(papel);
      renderAppShell();

      expect(screen.queryByText('Acesso')).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Usuários' })).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Identidade' })).not.toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Inventário' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Insights' })).toBeInTheDocument();
    });

    it('Id token sem role', () => {
      const s = signInForTest('token-de-teste', null);
      localStorage.setItem(SESSION_KEY, JSON.stringify(s));
      renderAppShell();

      expect(screen.queryByText('Acesso')).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Usuários' })).not.toBeInTheDocument();
    });

    // Spec `panel-issuer-session`, "A renovação traz o papel novo", pelo caminho inteiro: o
    // `sessionFetch` real recebe o 403, renova pelo emissor, e a casca se redesenha.
    it('O 403 renova e a casca passa a Operador sem recarregar', async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn(async (input: string) =>
          input === '/connect/token'
            ? tokenResponse(2, 900, 'operator')
            : new Response(null, { status: 403 }),
        ),
      );
      renderAppShell('/agents');
      expect(screen.getByRole('link', { name: 'Usuários' })).toBeInTheDocument();

      await act(async () => {
        await sessionFetch('http://localhost:5017/users');
      });

      expect(screen.queryByRole('link', { name: 'Usuários' })).not.toBeInTheDocument();
      expect(screen.queryByText('Acesso')).not.toBeInTheDocument();
      expect(screen.getByText('Operador')).toBeInTheDocument();
      vi.unstubAllGlobals();
    });

    it('A renovação traz o papel novo: a seção some sem recarregar', () => {
      renderAppShell('/agents');
      expect(screen.getByRole('link', { name: 'Usuários' })).toBeInTheDocument();

      // A renovação grava o registro com o id token novo, como o `renew` do `sessionFetch`
      // depois de um 403 (spec `panel-issuer-session`, "A renovação traz o papel novo").
      act(() => sessionStore.write(session(2, Date.now() + 15 * 60_000, {}, 'operator')));

      expect(screen.queryByRole('link', { name: 'Usuários' })).not.toBeInTheDocument();
      expect(screen.queryByText('Acesso')).not.toBeInTheDocument();
      expect(screen.getByText('Operador')).toBeInTheDocument();
    });
  });

  describe('link para Minha conta', () => {
    function Onde() {
      return <p data-testid="onde">{useLocation().pathname}</p>;
    }

    function renderComRotas(initialEntry: string) {
      return render(
        <MantineProvider theme={theme} defaultColorScheme="auto">
          <MemoryRouter initialEntries={[initialEntry]}>
            <Routes>
              <Route element={<AppShell />}>
                <Route path="*" element={<Onde />} />
              </Route>
            </Routes>
          </MemoryRouter>
        </MantineProvider>,
      );
    }

    it('o nome no rodapé leva a Minha conta, sem marcar a página fora dela', async () => {
      const user = userEvent.setup();
      signInForTest();
      renderComRotas('/agents');

      const link = screen.getByRole('link', { name: /Ana Souza/ });
      expect(link).toHaveAttribute('href', '/account');
      expect(link).toHaveAttribute('title', 'Minha conta');
      expect(link).not.toHaveAttribute('aria-current');

      await user.click(link);

      expect(screen.getByTestId('onde')).toHaveTextContent('/account');
    });

    it('em Minha conta, o link marca a página atual, e o menu não tem item para ela', () => {
      signInForTest();
      renderComRotas('/account');

      expect(screen.getByRole('link', { name: /Ana Souza/ })).toHaveAttribute(
        'aria-current',
        'page',
      );
      // O único link para /account é o do rodapé: nenhum item de menu leva a Minha conta.
      const paraMinhaConta = screen
        .getAllByRole('link')
        .filter((a) => a.getAttribute('href') === '/account');
      expect(paraMinhaConta).toHaveLength(1);
      expect(paraMinhaConta[0]).toHaveAttribute('title', 'Minha conta');
    });

    it('o avatar e os botões ficam fora do link', async () => {
      const user = userEvent.setup();
      signInForTest();
      vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
      renderComRotas('/agents');

      const link = screen.getByRole('link', { name: /Ana Souza/ });
      expect(link.querySelector('[aria-hidden="true"]')).toBeNull();
      expect(link).not.toContainElement(screen.getByRole('button', { name: 'Sair' }));
      expect(link).not.toContainElement(screen.getByRole('button', { name: /Mudar para tema/ }));

      await user.click(screen.getByRole('button', { name: /Mudar para tema/ }));

      expect(screen.getByTestId('onde')).toHaveTextContent('/agents');
    });
  });

  it('não renderiza barra superior nem controle de gaveta', () => {
    renderAppShell();

    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /toggle navigation/i })).not.toBeInTheDocument();
  });
});
