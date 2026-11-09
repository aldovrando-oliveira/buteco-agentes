import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../theme';
import { appRoutes } from './routes';
import { signInAsRole } from '../test/authFakes';

// A guarda de rota por papel (spec `panel-roles`, "Guarda de rota por papel"). A tabela das
// rotas e dos papéis que entram é escrita aqui, à parte do `routes.tsx`: lê-la do código faria
// o teste circular. Toda chamada de rede é contada: a rota negada não monta a página, e por
// isso não chama nada além do que a casca já chamaria (a casca não chama nada).

type PapelDeTeste = 'admin' | 'operador' | 'leitor';

const TABELA: Array<{ rota: string; entram: PapelDeTeste[] }> = [
  { rota: '/users', entram: ['admin'] },
  { rota: '/users/u-rafael', entram: ['admin'] },
  { rota: '/identity', entram: ['admin'] },
  { rota: '/agents/new', entram: ['admin', 'operador'] },
  { rota: '/agents/a1/edit', entram: ['admin', 'operador'] },
  { rota: '/mcp-servers/new', entram: ['admin', 'operador'] },
  { rota: '/mcp-servers/m1/edit', entram: ['admin', 'operador'] },
  { rota: '/knowledge-bases/new', entram: ['admin', 'operador'] },
  { rota: '/knowledge-bases/k1/edit', entram: ['admin', 'operador'] },
  { rota: '/channels/new', entram: ['admin', 'operador'] },
  { rota: '/channels/c1/edit', entram: ['admin', 'operador'] },
  { rota: '/channels/c1/sessions/s1', entram: ['admin', 'operador'] },
];

const PAPEIS: PapelDeTeste[] = ['admin', 'operador', 'leitor'];
const CASOS = TABELA.flatMap(({ rota, entram }) =>
  PAPEIS.map((papel) => ({ rota, papel, entra: entram.includes(papel) })),
);

let urls: string[] = [];

beforeEach(() => {
  urls = [];
  // Nenhuma resposta chega: a página que monta fica em "Carregando…", e o que se afirma é se
  // ela montou (chamou) ou não.
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      urls.push(String(input));
      return new Promise<Response>(() => {});
    }),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function renderAt(path: string) {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
      >
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('guarda de rota por papel', () => {
  it('a tabela não é vazia, e cada rota tem os três papéis', () => {
    expect(CASOS.length).toBe(TABELA.length * 3);
    expect(TABELA.length).toBeGreaterThan(0);
  });

  it.each(CASOS)('$rota com $papel: entra=$entra', async ({ rota, papel, entra }) => {
    signInAsRole(papel);
    renderAt(rota);

    if (entra) {
      // A página montou: a área principal tem conteúdo (o formulário, ou o "Carregando…" de
      // quem chama dados), e nenhum acesso negado. Nem toda página chama na carga: o cadastro
      // de MCP e o de base manual não chamam.
      await vi.waitFor(() =>
        expect(document.querySelector('main')?.textContent?.trim()).toBeTruthy(),
      );
      expect(screen.queryByText('Você não tem acesso a esta página.')).not.toBeInTheDocument();
    } else {
      expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
      expect(urls).toEqual([]);
    }
  });

  it('Operador em /users', async () => {
    signInAsRole('operador');
    renderAt('/users');

    expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
    expect(urls.filter((u) => u.endsWith('/users'))).toEqual([]);
  });

  it('Leitor numa sessão de canal', async () => {
    signInAsRole('leitor');
    renderAt('/channels/c1/sessions/s1');

    expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
    expect(urls.filter((u) => u.includes('/sessions'))).toEqual([]);
  });

  it('Admin em Identidade', async () => {
    signInAsRole('admin');
    renderAt('/identity');

    await vi.waitFor(() => expect(urls.some((u) => u.endsWith('/identity'))).toBe(true));
    expect(screen.queryByText('Você não tem acesso a esta página.')).not.toBeInTheDocument();
  });

  it('papel desconhecido vale Leitor', async () => {
    window.localStorage.setItem(
      'buteco.sessao',
      JSON.stringify({
        ...JSON.parse(window.localStorage.getItem('buteco.sessao')!),
        idToken: 'a.e30.b',
      }),
    );
    renderAt('/agents/new');

    expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
  });
});
