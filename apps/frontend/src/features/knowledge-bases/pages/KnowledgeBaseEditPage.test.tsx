import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseEditPage } from './KnowledgeBaseEditPage';
import { ApiError, getKnowledgeBase, updateKnowledgeBase } from '../api/knowledgeBasesApi';
import type { KnowledgeBase } from '../types/knowledgeBase';

vi.mock('../api/knowledgeBasesApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeBasesApi')>();
  return { ...actual, getKnowledgeBase: vi.fn(), updateKnowledgeBase: vi.fn() };
});

const knowledgeBase: KnowledgeBase = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação, prazos e faixas de desconto para atendimento humano.',
  isActive: true,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-02T00:00:00Z',
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/knowledge-bases/:id/edit', element: <KnowledgeBaseEditPage /> },
      { path: '/knowledge-bases/:id', element: <div>Detalhe da base</div> },
    ],
    { initialEntries: [`/knowledge-bases/${knowledgeBase.id}/edit`] },
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

describe('KnowledgeBaseEditPage', () => {
  beforeEach(() => {
    vi.mocked(getKnowledgeBase).mockReset();
    vi.mocked(updateKnowledgeBase).mockReset();
  });

  it('exibe indicador de carregamento enquanto a base não chega', () => {
    vi.mocked(getKnowledgeBase).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(screen.getByText('Carregando base de conhecimento...')).toBeInTheDocument();
  });

  it('carrega os valores atuais nos campos', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByLabelText(/Nome/)).toHaveValue(knowledgeBase.name);
    expect(screen.getByLabelText(/Descrição/)).toHaveValue(knowledgeBase.description);
  });

  it('salva as alterações e volta ao detalhe', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(updateKnowledgeBase).mockResolvedValue({
      ...knowledgeBase,
      name: 'Políticas de Cobrança 2026',
    });

    const router = renderPage();
    const nameField = await screen.findByLabelText(/Nome/);
    await user.clear(nameField);
    await user.type(nameField, 'Políticas de Cobrança 2026');
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }));

    expect(await screen.findByText('Detalhe da base')).toBeInTheDocument();
    expect(updateKnowledgeBase).toHaveBeenCalledWith(knowledgeBase.id, {
      name: 'Políticas de Cobrança 2026',
      description: knowledgeBase.description,
    });
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${knowledgeBase.id}`);
  });

  it('barra descrição apagada no cliente, sem enviar', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();
    await user.clear(await screen.findByLabelText(/Descrição/));
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }));

    expect(await screen.findByText(/A descrição da base de conhecimento é obrigatória/)).toBeInTheDocument();
    expect(updateKnowledgeBase).not.toHaveBeenCalled();
  });

  it('mapeia 400 da API para erro no campo correspondente', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(updateKnowledgeBase).mockRejectedValue(
      new ApiError(400, 'validação', {
        status: 400,
        errors: { name: ['O nome da base de conhecimento é obrigatório.'] },
      }),
    );

    renderPage();
    await screen.findByLabelText(/Nome/);
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }));

    expect(
      await screen.findByText('O nome da base de conhecimento é obrigatório.'),
    ).toBeInTheDocument();
  });

  it('informa base não encontrada', async () => {
    vi.mocked(getKnowledgeBase).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage();

    expect(await screen.findByText('Base de conhecimento não encontrada.')).toBeInTheDocument();
  });

  it('oferece volta para a base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByRole('link', { name: /Voltar à base/ })).toHaveAttribute(
      'href',
      `/knowledge-bases/${knowledgeBase.id}`,
    );
  });

  it('sair com alteração pendente não é bloqueado', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    const router = renderPage();
    await user.type(await screen.findByLabelText(/Nome/), ' rascunho');
    await user.click(screen.getByRole('link', { name: /Voltar à base/ }));

    expect(await screen.findByText('Detalhe da base')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/knowledge-bases/${knowledgeBase.id}`);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
