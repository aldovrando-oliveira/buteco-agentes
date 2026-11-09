import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { createAppQueryClient } from '../../../app/queryClient';
import { BRUNO, JOANA_DETALHE, PROPRIO, PROPRIO_ID, RAFAEL, fakeUsersApi, problem } from '../test/fakeUsersApi';
import { renderUserDetail, signInAs } from '../test/renderUserDetail';
import { detailSubtitle } from '../userDetailTexts';

// Editar usuário: a carga, o cabeçalho, o próprio, desativar e reativar (requisitos de
// `panel-user-detail-page`; design.md da change editar-usuario-painel, D2, D3 e D8).

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

/** O tom do selo: o `data-tom` fica na raiz do `Badge`, e o texto num filho. */
const tom = (el: HTMLElement) => el.closest('[data-tom]')?.getAttribute('data-tom');

const titulo = (nome: string) => screen.findByRole('heading', { name: nome, level: 2 });

describe('UserDetailPage: a carga', () => {
  it('carregando', () => {
    fakeUsersApi({ 'GET /users/u-rafael': () => new Promise(() => {}) });
    renderUserDetail('u-rafael');

    expect(screen.getByRole('status')).toHaveTextContent('Carregando o usuário…');
  });

  it('id inexistente mostra Usuário não encontrado', async () => {
    fakeUsersApi({ 'GET /users/u-x': problem(404, 'Usuário não encontrado.') });
    renderUserDetail('u-x');

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('Usuário não encontrado');
    expect(alerta).toHaveTextContent('O link pode estar errado, ou o convite foi cancelado.');
    expect(within(alerta).getByRole('link', { name: 'Voltar para Usuários' })).toHaveAttribute('href', '/users');
  });

  it('id inexistente faz um GET só', async () => {
    // Com o QueryClient do painel, que repete todo erro de consulta três vezes: o `retry` do
    // detalhe recusa o `404`, e o aviso aparece sem esperar as repetições.
    const api = fakeUsersApi({ 'GET /users/u-x': problem(404, 'Usuário não encontrado.') });
    renderUserDetail('u-x', createAppQueryClient());

    expect(await screen.findByText('Usuário não encontrado', {}, { timeout: 500 })).toBeInTheDocument();
    expect(api.calls('GET /users/u-x')).toHaveLength(1);
  });

  it('erro de carga com Tentar de novo', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({ 'GET /users/u-rafael': ['rede', { status: 200, body: RAFAEL }] });
    renderUserDetail('u-rafael');

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('Não foi possível carregar o usuário agora.');
    await user.click(within(alerta).getByRole('button', { name: 'Tentar de novo' }));

    expect(await titulo('Rafael Duarte')).toBeInTheDocument();
    expect(api.calls('GET /users/u-rafael')).toHaveLength(2);
  });

  it('o detalhe de um convidado volta para /users', async () => {
    fakeUsersApi({ 'GET /users/u-joana': { status: 200, body: JOANA_DETALHE } });
    const { router } = renderUserDetail('u-joana');

    expect(await screen.findByText('lista de Usuários')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/users');
    // Com `replace`: a entrada do detalhe some do histórico.
    expect(router.state.historyAction).toBe('REPLACE');
    expect(screen.queryByText('Joana Prado')).not.toBeInTheDocument();
  });
});

describe('UserDetailPage: o cabeçalho', () => {
  it('outro usuário ativo', async () => {
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL } });
    renderUserDetail('u-rafael');

    expect(await titulo('Rafael Duarte')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Usuários/ })).toHaveAttribute('href', '/users');
    expect(tom(screen.getByText('Ativo'))).toBe('green');
    expect(screen.getByRole('button', { name: 'Desativar' })).toBeInTheDocument();
    expect(screen.queryByText('Você')).not.toBeInTheDocument();
    expect(screen.getByText(detailSubtitle(RAFAEL))).toBeInTheDocument();
  });

  it('desativado', async () => {
    fakeUsersApi({ 'GET /users/u-bruno': { status: 200, body: BRUNO } });
    renderUserDetail('u-bruno');

    expect(await titulo('Bruno Teixeira')).toBeInTheDocument();
    expect(tom(screen.getByText('Desativado'))).toBe('gray');
    expect(screen.getByRole('button', { name: 'Reativar' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Desativar' })).not.toBeInTheDocument();
    expect(screen.getByText(/desativado em 20\/09\/2026$/)).toBeInTheDocument();
    expect(screen.getByText('Reative para gerar um link')).toBeInTheDocument();
  });

  it('estado desconhecido sai neutro, sem Desativar nem Reativar', async () => {
    fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: { ...RAFAEL, estado: 'suspenso' } } });
    renderUserDetail('u-rafael');

    expect(tom(await screen.findByText('suspenso'))).toBe('neutral');
    expect(screen.queryByRole('button', { name: 'Desativar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reativar' })).not.toBeInTheDocument();
  });
});

describe('UserDetailPage: o próprio usuário', () => {
  it('o próprio é o do sub, mesmo com o nome e o e-mail iguais aos de outro', async () => {
    // A sessão de teste tem o nome e o e-mail de RAFAEL, e o `sub` do PROPRIO.
    fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO },
    });
    const { unmount } = renderUserDetail('u-rafael');
    expect(await titulo('Rafael Duarte')).toBeInTheDocument();
    expect(screen.queryByText('Você')).not.toBeInTheDocument();
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
    unmount();

    renderUserDetail(PROPRIO_ID);
    expect(await titulo('Aldovrando Oliveira')).toBeInTheDocument();
    expect(screen.getByText('Você')).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent(
      'Este é você. A sua senha, a sua verificação em 2 etapas e as suas sessões ficam em Minha conta.',
    );
    expect(within(screen.getByRole('note')).getByRole('link', { name: 'Minha conta' })).toHaveAttribute('href', '/account');
  });

  it('id da rota em maiúsculas ainda é o próprio', async () => {
    fakeUsersApi({ 'GET /users/U-ALDO': { status: 200, body: { ...PROPRIO, id: 'U-ALDO' } } });
    renderUserDetail('U-ALDO');

    expect(await titulo('Aldovrando Oliveira')).toBeInTheDocument();
    expect(screen.getByText('Você')).toBeInTheDocument();
  });

  it('sem sub legível, trata como outro, sem erro', async () => {
    signInAs(null);
    fakeUsersApi({ [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO } });
    renderUserDetail(PROPRIO_ID);

    expect(await titulo('Aldovrando Oliveira')).toBeInTheDocument();
    expect(screen.queryByText('Você')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Desativar' })).toBeInTheDocument();
  });

  it('o próprio não tem Desativar', async () => {
    fakeUsersApi({ [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO } });
    renderUserDetail(PROPRIO_ID);

    expect(await titulo('Aldovrando Oliveira')).toBeInTheDocument();
    for (const nome of ['Desativar', 'Redefinir senha', 'Redefinir verificação', 'Encerrar todas as sessões']) {
      expect(screen.queryByRole('button', { name: nome })).not.toBeInTheDocument();
    }
    expect(screen.getByRole('link', { name: 'Trocar em Minha conta' })).toHaveAttribute('href', '/account');
    expect(screen.getByRole('link', { name: 'Ver em Minha conta' })).toHaveAttribute('href', '/account');
    // Só um Admin redefine (change papeis-no-painel; caso que muda de asserção, listado no tasks.md).
    expect(screen.getByText('Para trocar de celular, peça a outro Admin que redefina a sua verificação.')).toBeInTheDocument();
    // Os dados continuam editáveis.
    expect(screen.getByLabelText('Nome')).toBeEnabled();
  });

  it('a troca de conta em outra aba refaz o reconhecimento', async () => {
    fakeUsersApi({ [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO } });
    renderUserDetail(PROPRIO_ID);
    expect(await screen.findByText('Você')).toBeInTheDocument();

    // Outro login regrava o registro; o evento `storage` chega às outras abas.
    const outra = signInAs('u-outra-pessoa');
    window.dispatchEvent(new StorageEvent('storage', { key: 'buteco.sessao', newValue: JSON.stringify(outra) }));

    await waitFor(() => expect(screen.queryByText('Você')).not.toBeInTheDocument());
  });
});

describe('UserDetailPage: desativar e reativar', () => {
  it('desativar com confirmação', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      'POST /users/u-rafael/deactivate': {
        status: 200,
        body: { ...RAFAEL, estado: 'desativado', desativadoEm: '2026-11-07T18:00:00Z', sessoes: { ativas: 0, ultimaAtividade: null } },
      },
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const confirmacao = screen.getByRole('alert');
    expect(confirmacao).toHaveTextContent(
      'Desativar Rafael Duarte? A pessoa sai de todos os navegadores na hora e não consegue mais entrar. Você pode reativar depois, e ela vai precisar entrar de novo.',
    );
    expect(api.calls('POST /users/u-rafael/deactivate')).toHaveLength(0);
    await user.click(within(confirmacao).getByRole('button', { name: 'Desativar' }));

    expect(await screen.findByText('Desativado')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reativar' })).toBeInTheDocument();
    expect(screen.getByText('Reative para gerar um link')).toBeInTheDocument();
    expect(screen.getByText('Nenhuma sessão ativa')).toBeInTheDocument();
    expect(api.calls('POST /users/u-rafael/deactivate')).toHaveLength(1);
  });

  it('cancelar a desativação', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      'POST /users/u-rafael/deactivate': { status: 200, body: RAFAEL },
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Cancelar' }));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(api.calls('POST /users/u-rafael/deactivate')).toHaveLength(0);
  });

  it('403 do servidor no aviso da confirmação', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      'POST /users/u-rafael/deactivate': problem(403, 'Você não pode desativar o próprio usuário.'),
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const confirmacao = screen.getAllByRole('alert')[0];
    await user.click(within(confirmacao).getByRole('button', { name: 'Desativar' }));

    expect(await within(confirmacao).findByText('Você não pode desativar o próprio usuário.')).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
  });

  it('404 numa ação leva a Usuário não encontrado', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users/u-rafael': [{ status: 200, body: RAFAEL }, problem(404, 'Usuário não encontrado.')],
      'POST /users/u-rafael/deactivate': problem(404, 'Usuário não encontrado.'),
    });
    renderUserDetail('u-rafael');

    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Desativar' }));

    expect(await screen.findByText('Usuário não encontrado')).toBeInTheDocument();
  });

  it('reativar sem confirmação', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-bruno': { status: 200, body: BRUNO },
      'POST /users/u-bruno/activate': { status: 200, body: { ...BRUNO, estado: 'ativo', desativadoEm: null } },
    });
    renderUserDetail('u-bruno');

    await user.click(await screen.findByRole('button', { name: 'Reativar' }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Reativação concluída. Bruno Teixeira pode entrar de novo com a senha que já tinha.',
    );
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Desativar' })).toBeInTheDocument();
    expect(api.calls('POST /users/u-bruno/activate')).toHaveLength(1);
  });
});
