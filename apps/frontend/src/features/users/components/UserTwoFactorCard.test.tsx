import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { BRUNO, PROPRIO_ID, RAFAEL, fakeUsersApi, problem } from '../test/fakeUsersApi';
import { renderUserDetail, signInAs } from '../test/renderUserDetail';

// A verificação em 2 etapas de outro usuário (requisito "Verificação em 2 etapas de outro
// usuário"; design.md da change editar-usuario-painel, D9).

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const RESET = 'POST /users/u-rafael/two-factor/reset';
const M3_VERIFICACAO =
  'Obrigatória para todos os papéis, inclusive para quem entra pelo Google: o Buteco pede o próprio código.';
const botao = () => screen.getByRole('button', { name: 'Redefinir verificação' });

describe('Verificação em 2 etapas', () => {
  it('verificação ativa', async () => {
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL } });
    renderUserDetail('u-rafael');

    expect(await screen.findByText('Ativa')).toBeInTheDocument();
    // Um texto só da M3 para os dois estados (lista do topo da change telas-contas-federadas, D5).
    expect(screen.getByText(M3_VERIFICACAO)).toBeInTheDocument();
    expect(botao()).toBeEnabled();
    expect(screen.queryByText(/desde/)).not.toBeInTheDocument();
  });

  it('com a verificação desligada, o botão fica desabilitado', async () => {
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: { ...RAFAEL, duasEtapas: false } } });
    renderUserDetail('u-rafael');

    expect(await screen.findByText('Não configurada')).toBeInTheDocument();
    expect(screen.getByText(M3_VERIFICACAO)).toBeInTheDocument();
    expect(botao()).toBeDisabled();
  });

  it('o desativado com a verificação ativa tem o botão habilitado', async () => {
    fakeUsersApi({ 'GET /users/u-bruno': { status: 200, body: { ...BRUNO, duasEtapas: true } } });
    renderUserDetail('u-bruno');

    expect(await screen.findByText('Ativa')).toBeInTheDocument();
    expect(botao()).toBeEnabled();
  });

  it('depois de redefinir a verificação, as sessões vão a Nenhuma sessão ativa', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-rafael': [
        { status: 200, body: RAFAEL },
        { status: 200, body: { ...RAFAEL, duasEtapas: false, sessoes: { ativas: 0, ultimaAtividade: null } } },
      ],
      [RESET]: { status: 204 },
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Redefinir verificação' }));
    const confirmacao = screen.getByRole('alert');
    expect(confirmacao).toHaveTextContent(
      'Redefinir a verificação de Rafael Duarte? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo.',
    );
    await user.click(within(confirmacao).getByRole('button', { name: 'Redefinir' }));

    expect(await screen.findByText('Não configurada')).toBeInTheDocument();
    expect(screen.getByText('Nenhuma sessão ativa')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Encerrar todas as sessões' })).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(api.calls(RESET)).toHaveLength(1);
  });

  it('já redefinida em outra aba', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-rafael': [{ status: 200, body: RAFAEL }, { status: 200, body: { ...RAFAEL, duasEtapas: false } }],
      [RESET]: problem(409, 'A verificação em 2 etapas deste usuário não está configurada.'),
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Redefinir verificação' }));
    await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Redefinir' }));

    expect(await screen.findByText('Não configurada')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(api.calls('GET /users/u-rafael')).toHaveLength(2);
  });

  it('403 no aviso da confirmação', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      [RESET]: problem(403, 'Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor.'),
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Redefinir verificação' }));
    const confirmacao = screen.getAllByRole('alert')[0];
    await user.click(within(confirmacao).getByRole('button', { name: 'Redefinir' }));

    expect(await within(confirmacao).findByText('Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor.')).toBeInTheDocument();
  });
});
