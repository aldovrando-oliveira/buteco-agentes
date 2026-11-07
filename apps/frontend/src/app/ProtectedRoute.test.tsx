import { afterEach, describe, expect, it } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { MantineProvider } from '@mantine/core';
import { theme } from '../theme';
import { ProtectedRoute } from './ProtectedRoute';
import { SESSION_KEY } from '../auth/sessionStore';
import { leaving, sessionStore } from '../auth/session';
import { signInForTest, signOutForTest } from '../test/authFakes';

function renderAt(path = '/agents?filtro=x') {
  const router = createMemoryRouter(
    [
      { path: '/entrar', element: <div>Tela do entrar</div> },
      {
        element: <ProtectedRoute />,
        children: [{ path: '/agents', element: <div>Conteúdo protegido</div> }],
      },
    ],
    { initialEntries: [path] },
  );
  render(
    <MantineProvider theme={theme}>
      <RouterProvider router={router} />
    </MantineProvider>,
  );
  return router;
}

describe('ProtectedRoute', () => {
  afterEach(() => {
    signOutForTest();
    sessionStorage.clear();
    leaving.reset();
  });

  it('sem o registro, vai ao /entrar com a rota pedida como voltarPara, sem renderizar o conteúdo', () => {
    const router = renderAt();

    expect(screen.getByText('Tela do entrar')).toBeInTheDocument();
    expect(screen.queryByText('Conteúdo protegido')).not.toBeInTheDocument();
    expect(new URLSearchParams(router.state.location.search).get('voltarPara')).toBe('/agents?filtro=x');
  });

  it('outra aba usa a mesma sessão: com o registro no localStorage, renderiza a rota', () => {
    signInForTest();

    renderAt();

    expect(screen.getByText('Conteúdo protegido')).toBeInTheDocument();
  });

  it('registro corrompido é ausência: vai ao /entrar, sem erro na tela', () => {
    localStorage.setItem(SESSION_KEY, '{nada');

    renderAt();

    expect(screen.getByText('Tela do entrar')).toBeInTheDocument();
  });

  it('quando outra aba apaga o registro, esta vai ao /entrar', () => {
    signInForTest();
    renderAt();
    expect(screen.getByText('Conteúdo protegido')).toBeInTheDocument();

    act(() => {
      localStorage.removeItem(SESSION_KEY);
      window.dispatchEvent(new StorageEvent('storage', { key: SESSION_KEY, newValue: null }));
    });

    expect(screen.getByText('Tela do entrar')).toBeInTheDocument();
  });

  // O logout desta aba (design.md da change minha-conta-painel, D3; opção 3 do dono): o
  // registro sai antes de a página ir ao /connect/logout, e a rota não vai ao /entrar interno.
  it('com o logout desta aba em andamento, mostra "Saindo…" e não vai ao /entrar', () => {
    signInForTest();
    const router = renderAt();

    act(() => {
      leaving.mark();
      sessionStore.remove();
    });

    expect(screen.getByRole('status')).toHaveTextContent('Saindo…');
    expect(screen.queryByText('Tela do entrar')).not.toBeInTheDocument();
    expect(screen.queryByText('Conteúdo protegido')).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/agents');
  });
});
