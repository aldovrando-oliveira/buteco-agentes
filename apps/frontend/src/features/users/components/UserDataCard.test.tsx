import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { ACCOUNT_QUERY_KEY } from '../../account/queryKeys';
import { USERS_QUERY_KEY } from '../queryKeys';
import { PROPRIO, PROPRIO_ID, RAFAEL, fakeUsersApi, problem } from '../test/fakeUsersApi';
import { renderUserDetail, signInAs } from '../test/renderUserDetail';

// O cartão Dados de Editar usuário (requisito "Cartão Dados de Editar usuário"; design.md da
// change editar-usuario-painel, D5; decisão do dono Q13).

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const salvar = () => screen.getByRole('button', { name: 'Salvar alterações' });

async function abrir(routes: Parameters<typeof fakeUsersApi>[0] = {}, id = 'u-rafael') {
  const user = userEvent.setup();
  const api = fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL }, ...routes });
  const rendered = renderUserDetail(id);
  await screen.findByRole('heading', { level: 2 });
  return { user, api, ...rendered };
}

describe('Cartão Dados', () => {
  it('Salvar desabilitado sem mudança', async () => {
    await abrir();

    expect(screen.getByLabelText('Nome')).toHaveValue('Rafael Duarte');
    expect(screen.getByLabelText('E-mail')).toHaveValue('rafael.duarte@empresa.com');
    expect(screen.getByText('É o e-mail de entrada no painel.')).toBeInTheDocument();
    expect(salvar()).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Descartar' })).not.toBeInTheDocument();
  });

  it('voltar ao valor lido desabilita Salvar', async () => {
    const { user } = await abrir();
    const nome = screen.getByLabelText('Nome');

    await user.type(nome, 'x');
    expect(salvar()).toBeEnabled();
    await user.type(nome, '{Backspace}');

    expect(nome).toHaveValue('Rafael Duarte');
    expect(salvar()).toBeDisabled();
  });

  it('salvar com sucesso', async () => {
    const novo = { ...RAFAEL, nome: 'Rafael D. Souza' };
    const { user, api, queryClient } = await abrir({ 'PUT /users/u-rafael': { status: 200, body: novo } });
    const invalidar = vi.spyOn(queryClient, 'invalidateQueries');

    await user.clear(screen.getByLabelText('Nome'));
    await user.type(screen.getByLabelText('Nome'), 'Rafael D. Souza');
    await user.click(salvar());

    expect(await screen.findByText('Alterações salvas.')).toBeInTheDocument();
    expect(api.calls('PUT /users/u-rafael')[0].body).toEqual({ nome: 'Rafael D. Souza', email: 'rafael.duarte@empresa.com' });
    expect(screen.getByRole('heading', { name: 'Rafael D. Souza', level: 2 })).toBeInTheDocument();
    expect(salvar()).toBeDisabled();
    expect(invalidar).toHaveBeenCalledWith({ queryKey: USERS_QUERY_KEY, exact: true });
    expect(invalidar).not.toHaveBeenCalledWith({ queryKey: ACCOUNT_QUERY_KEY });
  });

  it('o próprio salva e Minha conta é relida', async () => {
    const { user, queryClient, api } = await abrir(
      {
        [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO },
        [`PUT /users/${PROPRIO_ID}`]: { status: 200, body: { ...PROPRIO, nome: 'Aldo Oliveira' } },
      },
      PROPRIO_ID,
    );
    const invalidar = vi.spyOn(queryClient, 'invalidateQueries');

    await user.clear(screen.getByLabelText('Nome'));
    await user.type(screen.getByLabelText('Nome'), 'Aldo Oliveira');
    await user.click(salvar());

    expect(await screen.findByText('Alterações salvas.')).toBeInTheDocument();
    expect(invalidar).toHaveBeenCalledWith({ queryKey: ACCOUNT_QUERY_KEY });
    // Sem renovação forçada (Q13): nenhum pedido ao emissor.
    expect(api.requests.filter((r) => r.path.startsWith('/connect'))).toEqual([]);
  });

  it('e-mail em uso', async () => {
    const { user } = await abrir({ 'PUT /users/u-rafael': problem(409, 'Já existe um usuário com este e-mail.') });

    await user.clear(screen.getByLabelText('E-mail'));
    await user.type(screen.getByLabelText('E-mail'), 'ana@exemplo.test');
    await user.click(salvar());

    expect(await screen.findByText('Já existe um usuário com este e-mail.')).toBeInTheDocument();
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('aria-invalid', 'true');
  });

  it('erros de validação do servidor', async () => {
    const { user } = await abrir({
      'PUT /users/u-rafael': {
        status: 400,
        body: { title: 'One or more validation errors occurred.', errors: { nome: ['O nome precisa ter de 1 a 200 caracteres.'] } },
      },
    });

    await user.clear(screen.getByLabelText('Nome'));
    await user.type(screen.getByLabelText('Nome'), ' ');
    await user.click(salvar());

    expect(await screen.findByText('O nome precisa ter de 1 a 200 caracteres.')).toBeInTheDocument();
  });

  it('falha passageira no aviso do cartão', async () => {
    const { user } = await abrir({ 'PUT /users/u-rafael': 'rede' });

    await user.type(screen.getByLabelText('Nome'), 'x');
    await user.click(salvar());

    const cartao = screen.getByLabelText('Nome').closest('form') as HTMLElement;
    expect(await within(cartao).findByRole('alert')).toBeInTheDocument();
  });

  it('Descartar', async () => {
    const { user } = await abrir();

    await user.type(screen.getByLabelText('E-mail'), 'x');
    await user.click(screen.getByRole('button', { name: 'Descartar' }));

    expect(screen.getByLabelText('E-mail')).toHaveValue('rafael.duarte@empresa.com');
    expect(salvar()).toBeDisabled();
  });

  it('o rascunho de Dados sobrevive a uma ação', async () => {
    const { user } = await abrir();

    await user.type(screen.getByLabelText('Nome'), ' Filho');
    await user.click(screen.getByRole('button', { name: 'Redefinir senha' }));
    await user.click(within(screen.getByRole('form', { name: /Gerar link para/ })).getByRole('button', { name: 'Cancelar' }));

    expect(screen.getByLabelText('Nome')).toHaveValue('Rafael Duarte Filho');
    expect(salvar()).toBeEnabled();
  });

  it('404 no salvar leva a Usuário não encontrado', async () => {
    const { user } = await abrir({
      'GET /users/u-rafael': [{ status: 200, body: RAFAEL }, problem(404, 'Usuário não encontrado.')],
      'PUT /users/u-rafael': problem(404, 'Usuário não encontrado.'),
    });

    await user.type(screen.getByLabelText('Nome'), 'x');
    await user.click(salvar());

    await waitFor(() => expect(screen.getByText('Usuário não encontrado')).toBeInTheDocument());
  });
});
