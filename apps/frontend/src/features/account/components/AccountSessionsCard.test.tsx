import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime, logout } from '../../../auth/session';
import { SESSIONS, defaultRoutes, fakeAccountApi, problem, type FakeReply } from '../test/fakeAccountApi';
import { renderAccount } from '../test/renderAccount';
import { AccountSessionsCard } from './AccountSessionsCard';

// O requisito "Sessões da própria conta" (spec `panel-account-page`; design.md da change
// minha-conta-painel, D6; decisões do dono Q4, Q9 e Q10).

vi.mock('../../../auth/session', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../auth/session')>();
  return { ...original, logout: vi.fn(async () => {}) };
});

beforeEach(() => {
  signInForTest();
  vi.mocked(logout).mockReset();
  vi.mocked(logout).mockResolvedValue(undefined);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function setup(routes: Record<string, FakeReply | FakeReply[]> = {}) {
  const api = fakeAccountApi({ ...defaultRoutes(), ...routes });
  renderAccount(<AccountSessionsCard />);
  return { api, user: userEvent.setup() };
}

const linhas = async () => (await screen.findAllByRole('row')).slice(1);

describe('AccountSessionsCard', () => {
  it('a tabela, com a sessão atual marcada pelo campo, e não pela posição', async () => {
    const atualNoMeio = [
      { ...SESSIONS[1], atual: false },
      { ...SESSIONS[0], atual: true },
      { ...SESSIONS[2], atual: false },
    ];
    setup({ 'GET /account/sessions': { status: 200, body: atualNoMeio } });

    const [l1, l2, l3] = await linhas();
    expect(screen.getAllByRole('columnheader').map((c) => c.textContent)).toEqual(['Sessão', 'Começou em', 'Último uso', 'Ações']);
    expect(within(l1).getByText('Sessão 1')).toBeInTheDocument();
    expect(within(l1).queryByText('Esta sessão')).not.toBeInTheDocument();
    expect(within(l1).getByRole('button', { name: 'Encerrar a Sessão 1' })).toBeInTheDocument();
    expect(within(l2).getByText('Sessão 2')).toBeInTheDocument();
    expect(within(l2).getByText('Esta sessão')).toBeInTheDocument();
    expect(within(l2).getByText('agora')).toBeInTheDocument();
    expect(within(l2).getByRole('button', { name: 'Encerrar e sair' })).toBeInTheDocument();
    expect(within(l3).getByText('Sessão 3')).toBeInTheDocument();
    expect(
      screen.getByText(/Cada sessão é um navegador em que você entrou\. .* com precisão de 15 minutos\. Encerrar esta sessão leva você ao login\./),
    ).toBeInTheDocument();
  });

  it('encerrar uma pede confirmação, e nenhum pedido sai antes dela', async () => {
    const t = setup({ 'POST /account/sessions/s-3/revoke': { status: 204 } });
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar a Sessão 3' }));

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Encerrar a Sessão 3? Quem estiver usando esse navegador precisa entrar de novo.',
    );
    expect(t.api.calls('POST /account/sessions/s-3/revoke')).toHaveLength(0);

    await t.user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Encerrar' }));

    await waitFor(() => expect(t.api.calls('POST /account/sessions/s-3/revoke')).toHaveLength(1));
    await waitFor(() => expect(t.api.calls('GET /account/sessions')).toHaveLength(2));
  });

  it('"Cancelar" na confirmação não encerra', async () => {
    const t = setup();
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar a Sessão 2' }));
    await t.user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Cancelar' }));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(t.api.requests.filter((r) => r.method === 'POST')).toHaveLength(0);
  });

  it('sessão já encerrada (404): relê a lista, sem aviso de erro', async () => {
    const t = setup({ 'POST /account/sessions/s-2/revoke': problem(404, 'Sessão não encontrada.') });
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar a Sessão 2' }));
    await t.user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Encerrar' }));

    await waitFor(() => expect(t.api.calls('GET /account/sessions')).toHaveLength(2));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('encerrar as outras: a confirmação com o número, e "2 sessões encerradas."', async () => {
    const t = setup({
      'GET /account/sessions': [{ status: 200, body: SESSIONS }, { status: 200, body: [SESSIONS[0]] }],
      'POST /account/sessions/revoke-others': { status: 200, body: { encerradas: 2 } },
    });
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar as outras' }));
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Encerrar as outras 2 sessões? Quem estiver usando esses navegadores precisa entrar de novo.',
    );
    await t.user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Encerrar' }));

    expect(await screen.findByRole('status')).toHaveTextContent('2 sessões encerradas.');
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Encerrar as outras' })).not.toBeInTheDocument());
  });

  it.each([
    [1, '1 sessão encerrada.'],
    [0, 'Nenhuma outra sessão estava aberta.'],
  ])('com %i encerrada(s): "%s"', async (encerradas, texto) => {
    const t = setup({ 'POST /account/sessions/revoke-others': { status: 200, body: { encerradas } } });
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar as outras' }));
    await t.user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Encerrar' }));

    expect(await screen.findByRole('status')).toHaveTextContent(texto);
  });

  // Achado na conferência no navegador (11.4): com uma outra sessão, o plural do quadro
  // dizia "as outras 1 sessões".
  it('com uma outra sessão só, a confirmação vai no singular', async () => {
    const t = setup({ 'GET /account/sessions': { status: 200, body: [SESSIONS[0], SESSIONS[1]] } });
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar as outras' }));

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Encerrar a outra sessão? Quem estiver usando esse navegador precisa entrar de novo.',
    );
  });

  it('sem outras sessões, "Encerrar as outras" não aparece', async () => {
    setup({ 'GET /account/sessions': { status: 200, body: [SESSIONS[0]] } });
    await linhas();

    expect(screen.queryByRole('button', { name: 'Encerrar as outras' })).not.toBeInTheDocument();
  });

  it('"Encerrar e sair" vai ao logout do emissor, sem confirmação e sem a rota de revogação', async () => {
    const t = setup();
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar e sair' }));

    expect(logout).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(t.api.requests.filter((r) => r.method === 'POST')).toHaveLength(0);
  });

  it('falha passageira ao encerrar: o aviso no cartão, sem deslogar', async () => {
    const t = setup({ 'POST /account/sessions/revoke-others': 'rede' });
    await linhas();

    await t.user.click(screen.getByRole('button', { name: 'Encerrar as outras' }));
    await t.user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Encerrar' }));

    expect(await screen.findByText('Não foi possível falar com o servidor.')).toBeInTheDocument();
    expect(logout).not.toHaveBeenCalled();
  });
});
