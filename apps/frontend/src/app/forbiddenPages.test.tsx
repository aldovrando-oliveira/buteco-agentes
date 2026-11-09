import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { theme } from '../theme';
import { appRoutes } from './routes';
import { queryRetry } from '../auth/transient';

// Spec `panel-roles`, "O 403 na carga e na ação" (change papeis-no-painel, tarefas 3.3 e 3.4).
// A sessão é a de Admin do setup, e o servidor recusa tudo com `403` sem corpo: é o caso do
// papel que mudou no meio da sessão, em que a guarda pelo token deixa a página montar e o
// servidor recusa.

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(null, { status: 403 })),
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
        client={new QueryClient({ defaultOptions: { queries: { retry: queryRetry } } })}
      >
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

describe('O 403 na carga da página', () => {
  it.each([
    ['/agents', 'Não foi possível carregar os agentes.'],
    ['/agents/a1', 'Não foi possível carregar o agente.'],
    ['/agents/a1/edit', 'Não foi possível carregar o agente.'],
    ['/mcp-servers', 'Não foi possível carregar os servidores MCP.'],
    ['/mcp-servers/m1', 'Não foi possível carregar o servidor MCP.'],
    ['/mcp-servers/m1/edit', 'Não foi possível carregar o servidor MCP.'],
    ['/knowledge-bases', 'Não foi possível carregar as bases de conhecimento.'],
    ['/knowledge-bases/k1', 'Não foi possível carregar a base de conhecimento.'],
    ['/knowledge-bases/k1/edit', 'Não foi possível carregar a base de conhecimento.'],
    ['/channels', 'Não foi possível carregar os canais.'],
    ['/channels/c1', 'Não foi possível carregar o canal.'],
    ['/channels/c1/edit', 'Não foi possível carregar o canal.'],
    ['/users', 'Não foi possível carregar os usuários agora.'],
    ['/users/u1', 'Não foi possível carregar o usuário agora.'],
    ['/identity', 'Não foi possível carregar os dados do emissor agora.'],
  ])('%s mostra o acesso negado, e não o erro de carga', async (rota, erroDeCarga) => {
    renderAt(rota);

    expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
    expect(screen.queryByText(erroDeCarga, { exact: false })).not.toBeInTheDocument();
  });
});

// Guarda do mecanismo: toda notificação vermelha das telas de domínio passa pelo
// `notifyFailure`, que é onde o `403` vira o aviso de ação recusada. Uma notificação vermelha
// escrita direto escaparia da regra sem nenhum caso de tela reprovar.
describe('O 403 na ação', () => {
  function arquivos(dir: string): string[] {
    return readdirSync(dir).flatMap((nome) => {
      const caminho = join(dir, nome);
      if (statSync(caminho).isDirectory()) return arquivos(caminho);
      return /\.tsx?$/.test(nome) && !/\.test\./.test(nome) ? [caminho] : [];
    });
  }

  it('nenhuma notificação vermelha escapa do notifyFailure', () => {
    const raiz = join(__dirname, '..', 'features');
    const lidos = arquivos(raiz);
    const fora = lidos.filter((f) =>
      /notifications\.show\(\{\s*color: 'red'/.test(readFileSync(f, 'utf8')),
    );

    expect(lidos.length).toBeGreaterThan(50);
    expect(fora).toEqual([]);
  });
});
