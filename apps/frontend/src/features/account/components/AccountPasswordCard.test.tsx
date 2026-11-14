import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime, logout } from '../../../auth/session';
import { PASSWORD_CHANGED_KEY } from '../../auth/passwordChangedNotice';
import { ACCOUNT, defaultRoutes, fakeAccountApi, problem } from '../test/fakeAccountApi';
import { renderAccount } from '../test/renderAccount';
import { AccountPasswordCard } from './AccountPasswordCard';

// O requisito "Troca da própria senha" (spec `panel-account-page`; design.md da change
// minha-conta-painel, D2 e D3). O `sessionFetch` e o `request<T>` são os reais; só o
// logout (que navega para fora do jsdom) é trocado por um espião.

vi.mock('../../../auth/session', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../auth/session')>();
  return { ...original, logout: vi.fn(async () => {}) };
});

const NOVA = 'uma-senha-nova-longa';

beforeEach(() => {
  signInForTest();
  sessionStorage.clear();
  vi.mocked(logout).mockReset();
  vi.mocked(logout).mockResolvedValue(undefined);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function abrirEPreencher(user: ReturnType<typeof userEvent.setup>, atual: string, nova: string, repeticao = nova) {
  await user.click(await screen.findByRole('button', { name: 'Trocar senha' }));
  if (atual) await user.type(screen.getByLabelText('Senha atual'), atual);
  if (nova) await user.type(screen.getByLabelText('Nova senha'), nova);
  if (repeticao) await user.type(screen.getByLabelText('Repita a nova senha'), repeticao);
  await user.click(screen.getByRole('button', { name: 'Trocar senha' }));
}

describe('AccountPasswordCard', () => {
  it('começa fechado, com o e-mail e "Trocar senha"', async () => {
    fakeAccountApi(defaultRoutes());
    renderAccount(<AccountPasswordCard />);

    expect(await screen.findByText('ana@exemplo.test')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Trocar senha' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Senha atual')).not.toBeInTheDocument();
  });

  it('o formulário traz os três campos, a ajuda e o aviso de que a pessoa sai de todos os navegadores', async () => {
    const user = userEvent.setup();
    fakeAccountApi(defaultRoutes());
    renderAccount(<AccountPasswordCard />);

    await user.click(await screen.findByRole('button', { name: 'Trocar senha' }));

    expect(screen.getByLabelText('Senha atual')).toHaveAttribute('autocomplete', 'current-password');
    expect(screen.getByLabelText('Nova senha')).toHaveAttribute('autocomplete', 'new-password');
    expect(screen.getByText('De 12 a 128 caracteres.')).toBeInTheDocument();
    expect(screen.getByLabelText('Repita a nova senha')).toBeInTheDocument();
    expect(
      screen.getByText('Ao trocar, você sai de todos os navegadores, inclusive deste, e entra de novo com a nova senha.'),
    ).toBeInTheDocument();
  });

  it('senha nova curta não envia', async () => {
    const user = userEvent.setup();
    const api = fakeAccountApi(defaultRoutes());
    renderAccount(<AccountPasswordCard />);

    await abrirEPreencher(user, 'a-atual', 'curta-11ch');

    expect(screen.getByLabelText('Nova senha')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('A senha precisa ter de 12 a 128 caracteres.')).toBeInTheDocument();
    expect(api.calls('POST /account/password')).toHaveLength(0);
  });

  it('repetição diferente não envia', async () => {
    const user = userEvent.setup();
    const api = fakeAccountApi(defaultRoutes());
    renderAccount(<AccountPasswordCard />);

    await abrirEPreencher(user, 'a-atual', NOVA, `${NOVA}-x`);

    expect(screen.getByText('As senhas não conferem.')).toBeInTheDocument();
    expect(api.calls('POST /account/password')).toHaveLength(0);
  });

  it('a senha atual errada fica no campo, sem deslogar', async () => {
    const user = userEvent.setup();
    // Com o 401 tratado como token vencido, o pedido entraria na renovação, e a tela não
    // receberia a recusa; a repetição e o logout estão fixados no sessionFetch.test.ts.
    const api = fakeAccountApi({ ...defaultRoutes(), 'POST /account/password': problem(401, 'Senha atual inválida.') });
    renderAccount(<AccountPasswordCard />);

    await abrirEPreencher(user, 'errada', NOVA);

    expect(await screen.findByText('Senha atual inválida.')).toBeInTheDocument();
    expect(screen.getByLabelText('Senha atual')).toHaveAttribute('aria-invalid', 'true');
    expect(api.calls('POST /account/password')).toHaveLength(1);
    expect(api.fetch.mock.calls.some(([url]) => String(url).includes('/connect/token'))).toBe(false);
    expect(authRuntime.assign).not.toHaveBeenCalled();
    expect(logout).not.toHaveBeenCalled();
    expect(screen.queryByText('fora de Minha conta')).not.toBeInTheDocument();
  });

  it('o 400 do servidor vai no campo "Nova senha"', async () => {
    const user = userEvent.setup();
    fakeAccountApi({ ...defaultRoutes(), 'POST /account/password': problem(400, 'A senha precisa ter de 12 a 128 caracteres.') });
    renderAccount(<AccountPasswordCard />);

    await abrirEPreencher(user, 'a-atual', NOVA);

    await waitFor(() => expect(screen.getByLabelText('Nova senha')).toHaveAttribute('aria-invalid', 'true'));
  });

  it('depois do 204, grava o aviso e só então chama o logout', async () => {
    const user = userEvent.setup();
    let chaveNoLogout: string | null = 'não chamado';
    vi.mocked(logout).mockImplementation(async () => {
      chaveNoLogout = sessionStorage.getItem(PASSWORD_CHANGED_KEY);
    });
    const api = fakeAccountApi({ ...defaultRoutes(), 'POST /account/password': { status: 204 } });
    renderAccount(<AccountPasswordCard />);

    await abrirEPreencher(user, 'a-atual', NOVA);

    await waitFor(() => expect(logout).toHaveBeenCalledTimes(1));
    expect(chaveNoLogout).not.toBeNull();
    expect(chaveNoLogout).not.toBe('não chamado');
    expect(api.calls('POST /account/password')[0].body).toEqual({ senhaAtual: 'a-atual', novaSenha: NOVA });
  });

  it('"Cancelar" fecha e limpa os campos', async () => {
    const user = userEvent.setup();
    fakeAccountApi(defaultRoutes());
    renderAccount(<AccountPasswordCard />);

    await user.click(await screen.findByRole('button', { name: 'Trocar senha' }));
    await user.type(screen.getByLabelText('Senha atual'), 'algo');
    await user.click(screen.getByRole('button', { name: 'Cancelar' }));
    await user.click(screen.getByRole('button', { name: 'Trocar senha' }));

    expect(screen.getByLabelText('Senha atual')).toHaveValue('');
  });

  it('o bloqueio usa o tempo do servidor e desabilita "Trocar senha"', async () => {
    const user = userEvent.setup();
    fakeAccountApi({
      ...defaultRoutes(),
      'POST /account/password': problem(423, 'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.', {
        'Retry-After': '300',
      }),
    });
    renderAccount(<AccountPasswordCard />);

    await abrirEPreencher(user, 'a-atual', NOVA);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.',
    );
    expect(screen.getByRole('button', { name: 'Trocar senha' })).toBeDisabled();
    expect(screen.getByLabelText('Senha atual')).toHaveValue('a-atual');
  });
});

// A pessoa que entra só pelo Google (spec `panel-account-page`, "Troca da própria senha"; change
// telas-contas-federadas, Q10; quadro `MinhaConta/sem-senha`, versão 81). GUARDA 6.4.
describe('Senha, para quem não tem senha', () => {
  it('A pessoa sem senha', async () => {
    fakeAccountApi({ ...defaultRoutes(), 'GET /account': { status: 200, body: { ...ACCOUNT, formasDeEntrada: ['google'] } } });
    renderAccount(<AccountPasswordCard />);

    expect(
      await screen.findByText('Você entra só pelo Google. Para ter também uma senha, peça a um administrador o link para criar uma.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Trocar senha' })).not.toBeInTheDocument();
    expect(screen.queryByText(/e a sua senha\./)).not.toBeInTheDocument();
  });
});

