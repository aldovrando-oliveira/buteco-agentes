import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { LoginPage } from './LoginPage';
import { ApiError, login } from '../api/authApi';
import { clearToken, getToken } from '../../../auth/token';

vi.mock('../api/authApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/authApi')>();
  return { ...actual, login: vi.fn() };
});

const navigateMock = vi.fn();
vi.mock('react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router')>();
  return { ...actual, useNavigate: () => navigateMock };
});

function renderPage() {
  const queryClient = new QueryClient();
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <LoginPage />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

async function fillAndSubmit(
  user: ReturnType<typeof userEvent.setup>,
  username = 'operator',
  password = 'senha',
) {
  await user.type(screen.getByLabelText(/usuário/i), username);
  await user.type(screen.getByLabelText(/senha/i), password);
  await user.click(screen.getByRole('button', { name: /entrar/i }));
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.mocked(login).mockReset();
    navigateMock.mockReset();
    clearToken();
  });

  afterEach(() => {
    clearToken();
  });

  it('encabeça a tela com a marca, nomeando o produto uma única vez', () => {
    const { container } = renderPage();

    // O lockup desenha o nome, mas é decorativo: quem carrega o nome acessível
    // é o <h2>, por texto visualmente oculto. Se o SVG também o expusesse, o
    // leitor de tela anunciaria "Buteco Agentes" duas vezes (D9, D11).
    expect(screen.getByRole('heading', { level: 2, name: 'Buteco Agentes' })).toBeInTheDocument();

    // Contando só o que a tecnologia assistiva alcança: o <text> do lockup
    // também escreve o nome no DOM, mas está sob aria-hidden e não é anunciado.
    const anunciado = screen.getAllByText('Buteco Agentes', {
      ignore: '[aria-hidden="true"], [aria-hidden="true"] *',
    });
    expect(anunciado).toHaveLength(1);

    const svg = container.querySelector('svg');
    expect(svg?.getAttribute('aria-hidden')).toBe('true');
    // O nome desenhado é o do lockup, não uma peça sem assinatura.
    expect(svg?.querySelector('text')?.textContent).toBe('Buteco Agentes');
  });

  it('em sucesso, armazena o token e redireciona para a área autenticada', async () => {
    vi.mocked(login).mockResolvedValue({
      token: 'token-emitido',
      expiresAt: '2026-08-22T12:30:00Z',
    });
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/', { replace: true }));
    expect(getToken()).toBe('token-emitido');
  });

  it('com credencial inválida, exibe erro genérico, não redireciona e não armazena token', async () => {
    vi.mocked(login).mockRejectedValue(new ApiError(401, 'Usuário ou senha inválidos.'));
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(await screen.findByText('Usuário ou senha inválidos.')).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
    expect(getToken()).toBeNull();
  });

  it('exige usuário e senha antes de submeter', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /entrar/i }));

    expect(await screen.findByText('O usuário é obrigatório.')).toBeInTheDocument();
    expect(screen.getByText('A senha é obrigatória.')).toBeInTheDocument();
    expect(login).not.toHaveBeenCalled();
  });
});
