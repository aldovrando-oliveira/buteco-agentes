import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { PROPRIO_ID, RAFAEL, RESET_LINK, fakeUsersApi, problem, type FakeReply } from '../test/fakeUsersApi';
import { renderUserDetail, signInAs } from '../test/renderUserDetail';

// Formas de entrada e "Redefinir senha" com a senha de quem chama (requisitos "Formas de
// entrada" e "Redefinir senha com a senha de quem chama"; design.md da change
// editar-usuario-painel, D6).

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const RESET = 'POST /users/u-rafael/password-reset';
const formulario = () => screen.getByRole('form', { name: 'Gerar link para Rafael Duarte criar uma nova senha' });

async function abrirFormulario(reset: FakeReply) {
  const user = userEvent.setup();
  const api = fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL }, [RESET]: reset });
  renderUserDetail('u-rafael');
  await user.click(await screen.findByRole('button', { name: 'Redefinir senha' }));
  return { user, api };
}

describe('Formas de entrada', () => {
  it('a linha Senha de outro usuário', async () => {
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL } });
    renderUserDetail('u-rafael');

    const tr = (await screen.findByText('Senha')).closest('tr') as HTMLElement;
    expect(within(tr).getByText('rafael.duarte@empresa.com')).toBeInTheDocument();
    expect(within(tr).getByRole('button', { name: 'Redefinir senha' })).toBeInTheDocument();
    expect(screen.getByText('Por enquanto, a senha é a única forma de entrada.')).toBeInTheDocument();
    expect(screen.queryByText('Conectada em')).not.toBeInTheDocument();
  });
});

describe('Redefinir senha', () => {
  it('o formulário', async () => {
    await abrirFormulario({ status: 200, body: { link: RESET_LINK, expiraEm: '2026-11-08T21:30:00Z' } });

    const form = formulario();
    expect(form).toHaveTextContent(
      'A senha atual continua valendo até a nova ser criada. Quando isso acontecer, a pessoa sai de todos os navegadores.',
    );
    expect(within(form).getByLabelText('Sua senha')).toHaveAttribute('autocomplete', 'current-password');
    expect(within(form).getByText('Para gerar o link, confirme que é você.')).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Gerar link' })).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Cancelar' })).toBeInTheDocument();
  });

  it('senha vazia não chama a rota', async () => {
    const { user, api } = await abrirFormulario({ status: 200, body: { link: RESET_LINK, expiraEm: '2026-11-08T21:30:00Z' } });

    await user.click(within(formulario()).getByRole('button', { name: 'Gerar link' }));

    expect(within(formulario()).getByText('Informe a sua senha.')).toBeInTheDocument();
    expect(api.calls(RESET)).toHaveLength(0);
  });

  it('senha errada na redefinição fica no campo, sem deslogar', async () => {
    const { user, api } = await abrirFormulario(problem(401, 'Senha inválida.'));

    await user.type(within(formulario()).getByLabelText('Sua senha'), 'senha-errada');
    await user.click(within(formulario()).getByRole('button', { name: 'Gerar link' }));

    const campo = within(formulario()).getByLabelText('Sua senha');
    expect(await within(formulario()).findByText('Senha inválida.')).toBeInTheDocument();
    expect(campo).toHaveAttribute('aria-invalid', 'true');
    expect(campo).toHaveValue('');
    // Nem renovação nem logout: nenhum pedido ao emissor, a sessão no registro, um pedido só.
    expect(api.requests.filter((r) => r.path.startsWith('/connect'))).toEqual([]);
    expect(authRuntime.assign).not.toHaveBeenCalled();
    expect(window.localStorage.getItem('buteco.sessao')).not.toBeNull();
    expect(api.calls(RESET)).toHaveLength(1);
    expect(api.calls(RESET)[0].body).toEqual({ senha: 'senha-errada' });
  });

  it('conta pausada', async () => {
    const { user } = await abrirFormulario(problem(423, 'Conta pausada.', { 'Retry-After': '120' }));

    await user.type(within(formulario()).getByLabelText('Sua senha'), 'qualquer');
    await user.click(within(formulario()).getByRole('button', { name: 'Gerar link' }));

    expect(await within(formulario()).findByRole('alert')).toHaveTextContent(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 2 minutos.',
    );
    expect(within(formulario()).getByRole('button', { name: 'Gerar link' })).toBeDisabled();
  });

  it('desativado no meio', async () => {
    const { user, api } = await abrirFormulario(problem(409, 'Usuário desativado.'));

    await user.type(within(formulario()).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(formulario()).getByRole('button', { name: 'Gerar link' }));

    expect(await within(formulario()).findByText('Usuário desativado.')).toBeInTheDocument();
    expect(api.calls('GET /users/u-rafael')).toHaveLength(2);
  });

  it('403 a si mesmo no aviso do formulário', async () => {
    const { user } = await abrirFormulario(problem(403, 'Use Minha conta para trocar a própria senha.'));

    await user.type(within(formulario()).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(formulario()).getByRole('button', { name: 'Gerar link' }));

    expect(await within(formulario()).findByText('Use Minha conta para trocar a própria senha.')).toBeInTheDocument();
  });

  it('Cancelar fecha e limpa a senha', async () => {
    const { user } = await abrirFormulario({ status: 200, body: { link: RESET_LINK, expiraEm: '2026-11-08T21:30:00Z' } });

    await user.type(within(formulario()).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(formulario()).getByRole('button', { name: 'Cancelar' }));
    expect(screen.queryByRole('form', { name: /Gerar link para/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Redefinir senha' }));
    expect(within(formulario()).getByLabelText('Sua senha')).toHaveValue('');
  });
});
