import { afterEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { ProtectedRoute } from './ProtectedRoute';
import { clearToken, setToken } from '../auth/token';

function renderWithProtectedRoute() {
  return render(
    <MemoryRouter initialEntries={['/agents']}>
      <Routes>
        <Route path="/login" element={<div>Tela de login</div>} />
        <Route element={<ProtectedRoute />}>
          <Route path="/agents" element={<div>Conteúdo protegido</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('ProtectedRoute', () => {
  afterEach(() => {
    clearToken();
  });

  it('sem token armazenado, redireciona para /login sem renderizar o conteúdo', () => {
    renderWithProtectedRoute();

    expect(screen.getByText('Tela de login')).toBeInTheDocument();
    expect(screen.queryByText('Conteúdo protegido')).not.toBeInTheDocument();
  });

  it('com token armazenado, renderiza o conteúdo da rota', () => {
    setToken('token-valido');

    renderWithProtectedRoute();

    expect(screen.getByText('Conteúdo protegido')).toBeInTheDocument();
    expect(screen.queryByText('Tela de login')).not.toBeInTheDocument();
  });
});
