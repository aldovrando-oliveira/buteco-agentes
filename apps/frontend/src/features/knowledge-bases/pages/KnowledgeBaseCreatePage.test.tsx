import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseCreatePage } from './KnowledgeBaseCreatePage';
import { ApiError, createKnowledgeBase } from '../api/knowledgeBasesApi';
import type { KnowledgeBase } from '../types/knowledgeBase';

vi.mock('../api/knowledgeBasesApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeBasesApi')>();
  return { ...actual, createKnowledgeBase: vi.fn() };
});

const created: KnowledgeBase = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação, prazos e faixas de desconto para atendimento humano.',
  isActive: true,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

// Data router de verdade, e não MemoryRouter: é o modo da aplicação desde
// frontend-roteamento-data-router, e é o que permite afirmar em qual rota a
// navegação parou.
function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/knowledge-bases/new', element: <KnowledgeBaseCreatePage /> },
      { path: '/knowledge-bases', element: <div>Catálogo de bases</div> },
      { path: '/knowledge-bases/:id', element: <div>Detalhe da base</div> },
    ],
    { initialEntries: ['/knowledge-bases/new'] },
  );
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <Notifications />
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );
  return router;
}

async function fillValidForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/Nome/), created.name);
  await user.type(screen.getByLabelText(/Descrição/), created.description);
}

describe('KnowledgeBaseCreatePage', () => {
  beforeEach(() => {
    vi.mocked(createKnowledgeBase).mockReset();
  });

  it('cria a base e leva ao detalhe dela', async () => {
    const user = userEvent.setup();
    vi.mocked(createKnowledgeBase).mockResolvedValue(created);

    const router = renderPage();
    await fillValidForm(user);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(await screen.findByText('Detalhe da base')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${created.id}`);
    expect(createKnowledgeBase).toHaveBeenCalledWith({
      name: created.name,
      description: created.description,
    });
  });

  it('não envia requisição quando o cliente barra a validação', async () => {
    const user = userEvent.setup();

    renderPage();
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(
      await screen.findByText('O nome da base de conhecimento é obrigatório.'),
    ).toBeInTheDocument();
    expect(createKnowledgeBase).not.toHaveBeenCalled();
  });

  it('mapeia 400 da API para erro no campo correspondente', async () => {
    const user = userEvent.setup();
    vi.mocked(createKnowledgeBase).mockRejectedValue(
      new ApiError(400, 'validação', {
        status: 400,
        errors: { description: ['A descrição da base de conhecimento é obrigatória.'] },
      }),
    );

    renderPage();
    await fillValidForm(user);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(
      await screen.findByText('A descrição da base de conhecimento é obrigatória.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Descrição/)).toHaveAttribute('aria-invalid', 'true');
  });

  it('mantém a tela e o texto digitado quando a criação falha', async () => {
    const user = userEvent.setup();
    vi.mocked(createKnowledgeBase).mockRejectedValue(new Error('falha de rede'));

    const router = renderPage();
    await fillValidForm(user);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    await screen.findByText('Erro ao cadastrar base');
    expect(router.state.location.pathname).toBe('/knowledge-bases/new');
    expect(screen.getByLabelText(/Nome/)).toHaveValue(created.name);
  });

  it('Cancelar volta para o catálogo', async () => {
    const user = userEvent.setup();

    const router = renderPage();
    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(router.state.location.pathname).toBe('/knowledge-bases');
  });

  // Sem guarda de navegação: nenhum formulário do painel usa uma (design.md,
  // D11). Sair com alteração pendente navega, sem diálogo de bloqueio.
  it('sair com alteração pendente não é bloqueado', async () => {
    const user = userEvent.setup();

    const router = renderPage();
    await user.type(screen.getByLabelText(/Nome/), 'rascunho não salvo');
    await user.click(screen.getByRole('link', { name: /bases de conhecimento/i }));

    expect(await screen.findByText('Catálogo de bases')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/knowledge-bases');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
