import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PORTAL, detalhe, fakeApplicationsApi, problem } from '../test/fixtures';
import { renderApplications } from '../test/renderApplications';

// Spec `panel-external-applications`, "Editar nome e descrição" (quadro `AplicacaoEditar`, versão 66).

const DETALHE = 'GET /applications/app-portal';
const SALVAR = 'PUT /applications/app-portal';

function rotas(extra: Record<string, unknown> = {}) {
  return {
    [DETALHE]: { status: 200, body: detalhe() },
    'GET /applications/app-portal/agents': { status: 200, body: [] },
    ...(extra as Record<string, never>),
  };
}

describe('ApplicationEditPage', () => {
  it('o formulário preenchido do detalhe, sem senha nem credencial', async () => {
    fakeApplicationsApi(rotas());
    renderApplications('/applications/app-portal/edit');

    expect(await screen.findByRole('heading', { name: 'Editar aplicação' })).toBeInTheDocument();
    expect(screen.getByLabelText('Nome')).toHaveValue('Portal do Parceiro');
    expect(screen.getByLabelText('Descrição')).toHaveValue('Autoatendimento de revendas');
    expect(screen.getByText('O client ID e os segredos não mudam ao editar.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Sua senha')).not.toBeInTheDocument();
    expect(screen.queryByText('Credencial')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Portal do Parceiro' })).toHaveAttribute(
      'href',
      '/applications/app-portal',
    );
  });

  it('Nome repetido ao editar', async () => {
    fakeApplicationsApi(
      rotas({ [SALVAR]: problem(409, 'Já existe uma aplicação com este nome.') }),
    );
    const user = userEvent.setup();
    const { router } = renderApplications('/applications/app-portal/edit');

    const nome = await screen.findByLabelText('Nome');
    await user.clear(nome);
    await user.type(nome, 'Chatbot do site');
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    expect(await screen.findByText('Já existe uma aplicação com este nome.')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/applications/app-portal/edit');
  });

  it('nome vazio não envia', async () => {
    const api = fakeApplicationsApi(rotas());
    const user = userEvent.setup();
    renderApplications('/applications/app-portal/edit');

    await user.clear(await screen.findByLabelText('Nome'));
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    expect(await screen.findByText('Informe o nome.')).toBeInTheDocument();
    expect(api.calls(SALVAR)).toHaveLength(0);
  });

  it('Salvar volta ao detalhe', async () => {
    const renomeada = detalhe({ ...PORTAL, nome: 'Portal de Revendas', descricao: null });
    const api = fakeApplicationsApi({
      ...rotas({ [SALVAR]: { status: 200, body: renomeada } }),
      [DETALHE]: [
        { status: 200, body: detalhe() },
        { status: 200, body: renomeada },
      ],
    });
    const user = userEvent.setup();
    const { router } = renderApplications('/applications/app-portal/edit');

    const nome = await screen.findByLabelText('Nome');
    await user.clear(nome);
    await user.type(nome, 'Portal de Revendas');
    await user.clear(screen.getByLabelText('Descrição'));
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/applications/app-portal'));
    expect(await screen.findByRole('heading', { name: 'Portal de Revendas' })).toBeInTheDocument();
    expect(api.calls(SALVAR)[0].body).toEqual({ nome: 'Portal de Revendas', descricao: null });
  });

  it('salvando mostra "Salvando…"', async () => {
    fakeApplicationsApi(rotas({ [SALVAR]: () => new Promise(() => {}) }));
    const user = userEvent.setup();
    renderApplications('/applications/app-portal/edit');

    await screen.findByLabelText('Nome');
    await user.click(screen.getByRole('button', { name: 'Salvar' }));

    expect(await screen.findByRole('button', { name: 'Salvando…' })).toBeDisabled();
  });
});
