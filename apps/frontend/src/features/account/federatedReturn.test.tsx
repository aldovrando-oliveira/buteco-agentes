import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import { signInForTest, signOutForTest } from '../../test/authFakes';
import { authRuntime } from '../../auth/session';
import { ACCOUNT, PROVEDORES_GOOGLE, defaultRoutes, fakeAccountApi } from './test/fakeAccountApi';
import { renderAccountPage } from './test/renderAccountPage';

// A volta do conectar em `/account` (spec `panel-federated-accounts`, "A volta do conectar em /account";
// design.md da change telas-contas-federadas, D4; quadro `MinhaConta`, os estados `conectado` e
// `recusa-*`, versão 81). Os casos marcados GUARDA são os da seção 5 do tasks.md.

beforeEach(() => {
  signInForTest();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function voltar(entry: Parameters<typeof renderAccountPage>[0]) {
  const api = fakeAccountApi({
    ...defaultRoutes(),
    'GET /account': { status: 200, body: ACCOUNT },
    'GET /auth/federated/providers': { status: 200, body: PROVEDORES_GOOGLE },
  });
  return { api, ...renderAccountPage(entry) };
}

const cartao = () => screen.findByRole('region', { name: 'Formas de entrada' });

describe('A volta do conectar em /account', () => {
  it('A volta com a conta conectada', async () => {
    const { router } = voltar('/account?conectado=google');

    const aviso = within(await cartao()).getByRole('status');
    expect(aviso).toHaveTextContent(
      'Conta Google conectada. Da próxima vez, você também pode entrar com o Google.',
    );
    await waitFor(() => expect(router.state.location.search).toBe(''));
    expect(router.state.location.pathname).toBe('/account');
  });

  it.each([
    [
      'em-uso',
      'Conta Google já conectada',
      'Esta conta Google já está conectada a outro usuário do Buteco Agentes. Escolha outra conta.',
    ],
    [
      'ja-conectado',
      'Você já tem uma conta Google conectada',
      'Para trocar de conta, desconecte a atual e conecte a outra.',
    ],
    [
      'sessao-expirada',
      'A conexão expirou',
      'A sua conta mudou enquanto você estava no Google: a senha, a verificação ou o acesso. Tente conectar de novo.',
    ],
    [
      'dominio-nao-permitido',
      'Domínio não permitido',
      'Esta conta Google não é do domínio permitido neste painel.',
    ],
  ])('a recusa %s mostra o título e o texto', async (motivo, titulo, texto) => {
    voltar(`/account?recusa=${motivo}&provedor=google`);

    const aviso = within(await cartao()).getByRole('alert');
    expect(aviso).toHaveTextContent(titulo);
    expect(aviso).toHaveTextContent(texto);
  });

  it('as cores das recusas', async () => {
    voltar('/account?recusa=ja-conectado&provedor=google');
    const amarelo = within(await cartao()).getByRole('alert');
    expect(amarelo.getAttribute('style') ?? '').toMatch(/yellow/);
  });

  it('Falha sem provedor', async () => {
    voltar('/account?recusa=falha');

    const aviso = within(await cartao()).getByRole('alert');
    expect(aviso).toHaveTextContent('Não foi possível conectar a conta Google. Tente de novo.');
    expect(within(aviso).queryByRole('heading')).not.toBeInTheDocument();
  });

  // GUARDA 5.5: o motivo fora da tabela sem aviso.
  it('Motivo desconhecido sai como falha', async () => {
    voltar('/account?recusa=outra-coisa&provedor=google');

    const aviso = within(await cartao()).getByRole('alert');
    expect(aviso).toHaveTextContent('Não foi possível conectar a conta Google. Tente de novo.');
  });

  // GUARDAS 5.3 e 5.4: os parâmetros que ficam, e o `state` que se perde na limpeza.
  it('Os parâmetros saem e o state fica', async () => {
    const state = { papelNovo: 'operador' };
    const { router } = voltar({
      pathname: '/account',
      search: '?recusa=em-uso&provedor=google',
      state,
    });

    await cartao();
    await waitFor(() => expect(router.state.location.search).toBe(''));
    expect(router.state.location.pathname).toBe('/account');
    expect(router.state.location.state).toEqual(state);
    expect(router.state.historyAction).toBe('REPLACE');
  });

  it('Recarregar não repete o aviso', async () => {
    const primeira = voltar('/account?conectado=google');
    await cartao();
    await waitFor(() => expect(primeira.router.state.location.search).toBe(''));
    primeira.unmount();

    voltar(primeira.router.state.location.pathname);

    const card = await cartao();
    expect(within(card).queryByRole('status')).not.toBeInTheDocument();
    expect(within(card).queryByText(/Conta Google conectada/)).not.toBeInTheDocument();
  });

  it('Sem a rota do aviso', async () => {
    const { api } = voltar('/account?recusa=em-uso&provedor=google');

    await cartao();
    expect(api.calls('POST /auth/federated/refusal')).toHaveLength(0);
  });
});
