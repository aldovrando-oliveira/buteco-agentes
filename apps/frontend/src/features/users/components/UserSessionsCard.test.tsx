import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { formatRelative } from '../../../utils/panelDates';
import { PROPRIO, PROPRIO_ID, RAFAEL, fakeUsersApi } from '../test/fakeUsersApi';
import { renderUserDetail, signInAs } from '../test/renderUserDetail';

// As sessões de outro usuário (requisito "Sessões de outro usuário"; design.md da change
// editar-usuario-painel, D10; decisão do dono Q12).

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const REVOKE = 'POST /users/u-rafael/sessions/revoke';

describe('Sessões', () => {
  it('duas sessões', async () => {
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL } });
    renderUserDetail('u-rafael');

    expect(
      await screen.findByText(`2 sessões ativas · último uso ${formatRelative(RAFAEL.sessoes.ultimaAtividade!)}`),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Encerrar faz a pessoa precisar entrar de novo em todos os navegadores. Use se suspeitar de acesso indevido.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Encerrar todas as sessões' })).toBeInTheDocument();
  });

  it('sem sessões ativas, não há botão de encerrar', async () => {
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: { ...RAFAEL, sessoes: { ativas: 0, ultimaAtividade: null } } } });
    renderUserDetail('u-rafael');

    expect(await screen.findByText('Nenhuma sessão ativa')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Encerrar todas as sessões' })).not.toBeInTheDocument();
  });

  it('as sessões do próprio sem a dedução', async () => {
    fakeUsersApi({ [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO } });
    renderUserDetail(PROPRIO_ID);

    expect(await screen.findByText('3 sessões ativas')).toBeInTheDocument();
    expect(screen.queryByText(/incluindo esta/)).not.toBeInTheDocument();
    expect(screen.queryByText(/último uso/)).not.toBeInTheDocument();
    expect(screen.getByText('As suas sessões aparecem em Minha conta, onde você pode encerrar cada uma.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver em Minha conta' })).toHaveAttribute('href', '/account');
    expect(screen.queryByRole('button', { name: 'Encerrar todas as sessões' })).not.toBeInTheDocument();
  });

  it('encerrar com confirmação', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-rafael': [
        { status: 200, body: RAFAEL },
        { status: 200, body: { ...RAFAEL, sessoes: { ativas: 0, ultimaAtividade: null } } },
      ],
      [REVOKE]: { status: 200, body: { encerradas: 2 } },
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Encerrar todas as sessões' }));
    const confirmacao = screen.getByRole('alert');
    expect(confirmacao).toHaveTextContent(
      'Encerrar as 2 sessões de Rafael Duarte? A pessoa precisa entrar de novo em todos os navegadores.',
    );
    expect(api.calls(REVOKE)).toHaveLength(0);
    await user.click(within(confirmacao).getByRole('button', { name: 'Encerrar' }));

    expect(await screen.findByRole('status')).toHaveTextContent('2 sessões encerradas.');
    expect(await screen.findByText('Nenhuma sessão ativa')).toBeInTheDocument();
    expect(api.calls(REVOKE)).toHaveLength(1);
    expect(api.calls(REVOKE)[0].body).toBeUndefined();
    expect(api.calls('GET /users/u-rafael')).toHaveLength(2);
  });

  it('a confirmação de uma sessão só', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: { ...RAFAEL, sessoes: { ativas: 1, ultimaAtividade: RAFAEL.sessoes.ultimaAtividade } } },
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Encerrar todas as sessões' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Encerrar a sessão de Rafael Duarte? A pessoa precisa entrar de novo.');
  });

  it('nenhuma sessão estava aberta', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      [REVOKE]: { status: 200, body: { encerradas: 0 } },
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Encerrar todas as sessões' }));
    await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Encerrar' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Nenhuma sessão estava aberta.');
  });

  it('falha passageira deixa a confirmação aberta, com o aviso', async () => {
    const user = userEvent.setup();
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL }, [REVOKE]: 'rede' });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Encerrar todas as sessões' }));
    const confirmacao = screen.getAllByRole('alert')[0];
    await user.click(within(confirmacao).getByRole('button', { name: 'Encerrar' }));

    expect(await within(confirmacao).findByText(/Não foi possível/)).toBeInTheDocument();
    expect(within(confirmacao).getByRole('button', { name: 'Encerrar' })).toBeInTheDocument();
  });
});
