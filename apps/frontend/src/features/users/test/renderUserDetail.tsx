import { render } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Link, RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { fakeJwt, session } from '../../../test/authFakes';
import { UserDetailPage } from '../pages/UserDetailPage';
import { PROPRIO_ID } from './fakeUsersApi';

/**
 * Entra com uma sessão cujo id token tem o `sub` dado. O nome e o e-mail da sessão são os de
 * `RAFAEL`, de propósito: o reconhecimento do próprio é pelo `sub` (D2).
 */
export function signInAs(sub: string | null = PROPRIO_ID) {
  const claims: Record<string, unknown> = {
    name: 'Rafael Duarte',
    email: 'rafael.duarte@empresa.com',
  };
  if (sub !== null) claims.sub = sub;
  const s = session(1, Date.now() + 15 * 60_000, {
    accessToken: 'token-de-teste',
    idToken: fakeJwt(claims),
    nome: 'Rafael Duarte',
    email: 'rafael.duarte@empresa.com',
  });
  window.localStorage.setItem('buteco.sessao', JSON.stringify(s));
  return s;
}

/**
 * `/users/:id` num data router de memória, com as rotas vizinhas para conferir a navegação,
 * e o QueryClient de teste. `retry` padrão desligado, como nos outros testes de tela: a
 * consulta do detalhe declara o dela, que é o que o caso do `404` exercita.
 */
export function renderUserDetail(
  id: string,
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
  const router = createMemoryRouter(
    [
      {
        path: '/users/:id',
        element: (
          <>
            <Link to="/inventory">sair</Link>
            <UserDetailPage />
          </>
        ),
      },
      { path: '/users', element: <p>lista de Usuários</p> },
      { path: '/account', element: <p>Minha conta</p> },
      { path: '/inventory', element: <p>fora de Usuários</p> },
    ],
    { initialEntries: ['/inventory', `/users/${id}`], initialIndex: 1 },
  );
  const result = render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
  return { ...result, queryClient, router };
}
