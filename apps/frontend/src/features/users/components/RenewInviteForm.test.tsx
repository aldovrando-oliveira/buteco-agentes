import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime, sessionStore } from '../../../auth/session';
import { CONVIDADA, fakeUsersApi, problem, type FakeReply } from '../test/fakeUsersApi';
import { renderUsers } from '../test/renderUsers';

// "Gerar novo link" (requisito "Gerar novo link de convite"; decisão do dono Q7).

const NOVO = 'https://buteco.exemplo.test/convite#Rm4Tq8wZx2Lp9KvN3sYc7Hd5Jf1Ge6Ua0Bi';
const RENEW = 'POST /users/u-joana/invitation/renew';

beforeEach(() => {
  signInForTest();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function renovar(reply: FakeReply, senha = 'minha-senha-certa', depois: FakeReply = { status: 200, body: [CONVIDADA] }) {
  const user = userEvent.setup();
  const api = fakeUsersApi({ 'GET /users': [{ status: 200, body: [CONVIDADA] }, depois], [RENEW]: reply });
  renderUsers();
  await user.click(await screen.findByRole('button', { name: 'Gerar novo link para Joana Prado' }));
  const form = screen.getByRole('form', { name: 'Gerar novo link para Joana Prado' });
  if (senha) await user.type(within(form).getByLabelText('Sua senha'), senha);
  await user.click(within(form).getByRole('button', { name: 'Gerar novo link' }));
  return { user, api, form };
}

describe('Gerar novo link', () => {
  it('o formulário tem o texto do quadro e a ajuda da senha', async () => {
    const user = userEvent.setup();
    fakeUsersApi({ 'GET /users': { status: 200, body: [CONVIDADA] } });
    renderUsers();

    await user.click(await screen.findByRole('button', { name: 'Gerar novo link para Joana Prado' }));

    const form = screen.getByRole('form', { name: 'Gerar novo link para Joana Prado' });
    expect(within(form).getByText('O link anterior deixa de funcionar na hora, e o novo vale por 72 horas.')).toBeInTheDocument();
    expect(within(form).getByText('Para gerar um link, confirme que é você.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Convidar usuário' })).toBeDisabled();
  });

  it('novo link gerado', async () => {
    const { api } = await renovar({ status: 200, body: { link: NOVO, expiraEm: '2026-11-10T12:40:00Z' } });

    expect(await screen.findByRole('heading', { name: 'Novo link gerado para Joana Prado' })).toBeInTheDocument();
    expect(screen.getByText('joana@exemplo.test', { selector: '[role="status"] p' })).toBeInTheDocument();
    expect(screen.getByText(NOVO)).toBeInTheDocument();
    expect(api.calls(RENEW)[0].body).toEqual({ senha: 'minha-senha-certa' });
  });

  it('convite aceito no meio', async () => {
    const { api } = await renovar(problem(409, 'Este usuário não tem convite pendente.'), 'minha-senha-certa', {
      status: 200,
      body: [{ ...CONVIDADA, estado: 'ativo', convite: null }],
    });

    expect(
      await screen.findByText('O convite de Joana Prado já foi aceito, então não há link a gerar. A lista foi atualizada.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Gerar novo link para Joana Prado' })).not.toBeInTheDocument();
    await waitFor(() => expect(api.calls('GET /users')).toHaveLength(2));
  });

  it('convite cancelado no meio', async () => {
    const { api } = await renovar(problem(404, 'Usuário não encontrado.'), 'minha-senha-certa', { status: 200, body: [] });

    await waitFor(() => expect(api.calls('GET /users')).toHaveLength(2));
    expect(screen.queryByRole('form', { name: 'Gerar novo link para Joana Prado' })).not.toBeInTheDocument();
    expect(screen.queryByText(/já foi aceito/)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('senha vazia não é enviada', async () => {
    const { api, form } = await renovar({ status: 200, body: {} }, '');

    expect(within(form).getByText('Informe a sua senha.')).toBeInTheDocument();
    expect(api.calls(RENEW)).toHaveLength(0);
  });

  it('senha errada fica no campo, sem deslogar', async () => {
    const { api, form } = await renovar(problem(401, 'Senha inválida.'), 'senha-errada');

    expect(await within(form).findByText('Senha inválida.')).toBeInTheDocument();
    expect(within(form).getByLabelText('Sua senha')).toHaveAttribute('aria-invalid', 'true');
    expect(api.calls(RENEW)).toHaveLength(1);
    expect(sessionStore.read()).not.toBeNull();
    expect(authRuntime.assign).not.toHaveBeenCalled();
  });

  it('conta pausada', async () => {
    const { form } = await renovar(
      problem(423, 'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.', { 'Retry-After': '300' }),
    );

    expect(await within(form).findByRole('alert')).toHaveTextContent(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.',
    );
    expect(within(form).getByRole('button', { name: 'Gerar novo link' })).toBeDisabled();
  });
});
