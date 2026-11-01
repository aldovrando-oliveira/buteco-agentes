import { afterEach, describe, expect, it } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { ProtectedRoute } from './ProtectedRoute';
import { SESSION_KEY } from '../auth/sessionStore';
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
  render(<RouterProvider router={router} />);
  return router;
}

describe('ProtectedRoute', () => {
  afterEach(() => {
    signOutForTest();
    sessionStorage.clear();
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
});
