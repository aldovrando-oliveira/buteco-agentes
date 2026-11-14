import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import {
  ACCOUNT,
  ACCOUNT_SO_GOOGLE,
  GOOGLE_CONECTADA,
  GOOGLE_FORA,
  PROVEDORES_GOOGLE,
  defaultRoutes,
  fakeAccountApi,
  problem,
  type FakeReply,
} from '../test/fakeAccountApi';
import { renderAccountPage } from '../test/renderAccountPage';

// O cartão "Formas de entrada" de Minha conta (spec `panel-federated-accounts`: "Formas de entrada em
// Minha conta", "Conectar a conta do provedor" e "Desconectar a própria conta do provedor"; design.md
// da change telas-contas-federadas, D3; quadro `MinhaConta`, `entrega=m3`, versão 81). A página
// inteira, com o `sessionFetch` e o `request<T>` reais por cima do `fetch` falso.

const CONECTAR = 'POST /account/federated/google/connect';
const DESCONECTAR = 'DELETE /account/federated/google';
const COM_GOOGLE = {
  ...ACCOUNT,
  formasDeEntrada: ['senha', 'google'],
  contasConectadas: [GOOGLE_CONECTADA],
};

beforeEach(() => {
  signInForTest();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function abrir(
  conta: unknown,
  routes: Record<string, FakeReply | FakeReply[]> = {},
  entry: Parameters<typeof renderAccountPage>[0] = '/account',
) {
  const api = fakeAccountApi({
    ...defaultRoutes(),
    'GET /account': Array.isArray(conta)
      ? conta.map((c) => ({ status: 200, body: c }))
      : { status: 200, body: conta },
    'GET /auth/federated/providers': { status: 200, body: PROVEDORES_GOOGLE },
    ...routes,
  });
  const view = renderAccountPage(entry);
  return { api, ...view, user: userEvent.setup() };
}

const cartao = () => screen.findByRole('region', { name: 'Formas de entrada' });
const linha = (card: HTMLElement, forma: string) => {
  const celula = within(card)
    .getAllByRole('cell')
    .find((c) => c.textContent === forma);
  return celula?.closest('tr') as HTMLElement;
};

describe('Formas de entrada em Minha conta', () => {
  it('A senha e o Google conectado', async () => {
    abrir(COM_GOOGLE);

    const card = await cartao();
    await within(card).findByText('ana@empresa.com');
    const senha = linha(card, 'Senha');
    expect(within(senha).getByText(ACCOUNT.email)).toBeInTheDocument();
    expect(within(senha).queryByRole('button')).not.toBeInTheDocument();
    const google = linha(card, 'Google');
    expect(within(google).getByText('ana@empresa.com')).toBeInTheDocument();
    expect(within(google).getByText('14/08/2026')).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Desconectar' })).toBeEnabled();
    expect(within(card).queryByText('Não conectada')).not.toBeInTheDocument();
    expect(
      within(card).getByText(
        'Cada forma de entrada leva ao mesmo usuário do Buteco. A conta Google só entra se estiver conectada aqui ou se você aceitou o convite com ela. A última forma de entrada não pode ser removida.',
      ),
    ).toBeInTheDocument();
  });

  it('O provedor sem conta', async () => {
    abrir(ACCOUNT);

    const card = await cartao();
    const google = await within(card).findByText('Não conectada');
    expect(
      within(google.closest('tr') as HTMLElement).getByRole('button', { name: 'Conectar Google' }),
    ).toBeInTheDocument();
  });

  it('Sem provedor e sem conta, o cartão não aparece', async () => {
    const { api } = abrir(ACCOUNT, {
      'GET /auth/federated/providers': { status: 200, body: { provedores: [] } },
    });

    await screen.findByRole('region', { name: 'Senha' });
    await waitFor(() => expect(api.calls('GET /auth/federated/providers')).toHaveLength(1));
    expect(screen.queryByRole('region', { name: 'Formas de entrada' })).not.toBeInTheDocument();
  });

  // GUARDA 3.5: a recusa sem conta e com a lista falhando levaria o aviso junto com o cartão.
  it('A recusa sem conta e sem lista mantém o cartão', async () => {
    abrir(
      ACCOUNT,
      { 'GET /auth/federated/providers': 'rede' },
      '/account?recusa=em-uso&provedor=google',
    );

    const card = await cartao();
    expect(within(card).getByText('Conta Google já conectada')).toBeInTheDocument();
    expect(within(card).getAllByRole('row')).toHaveLength(2);
    expect(linha(card, 'Senha')).toBeDefined();
  });

  // GUARDA 3.5: o desconectar da única conta, com a lista vazia, levaria o aviso verde junto.
  it('Desconectar a única conta mantém o cartão com o aviso', async () => {
    const { user, api } = abrir([COM_GOOGLE, ACCOUNT], {
      'GET /auth/federated/providers': { status: 200, body: { provedores: [] } },
      [DESCONECTAR]: { status: 204 },
    });
    const card = await cartao();
    await user.click(await within(card).findByRole('button', { name: 'Desconectar' }));
    await user.click(within(card).getByRole('button', { name: 'Desconectar' }));

    await waitFor(() => expect(api.calls('GET /account')).toHaveLength(2));
    const depois = await cartao();
    expect(within(depois).getByText('Conta Google desconectada.')).toBeInTheDocument();
    await waitFor(() => expect(within(depois).getAllByRole('row')).toHaveLength(2));
    expect(within(depois).queryByText('ana@empresa.com')).not.toBeInTheDocument();
  });

  it('A lista dos provedores falhando esconde só o conectar', async () => {
    abrir(COM_GOOGLE, { 'GET /auth/federated/providers': 'rede' });

    const card = await cartao();
    expect(await within(card).findByText('ana@empresa.com')).toBeInTheDocument();
    expect(linha(card, 'Senha')).toBeDefined();
    expect(within(card).queryByRole('button', { name: 'Conectar Google' })).not.toBeInTheDocument();
  });

  it('O vínculo fora da configuração não tem ação', async () => {
    abrir({ ...COM_GOOGLE, contasConectadas: [GOOGLE_CONECTADA, GOOGLE_FORA] });

    const card = await cartao();
    const fora = (await within(card).findByText('ana@antiga.com.br')).closest('tr') as HTMLElement;
    expect(within(fora).getByText('Provedor fora da configuração')).toBeInTheDocument();
    expect(within(fora).getByText('Sem ação')).toBeInTheDocument();
    expect(within(fora).queryByRole('button')).not.toBeInTheDocument();
  });

  // GUARDA 3.4: com uma forma só, o botão habilitado deixaria tentar remover a última.
  it('Com uma forma só, Desconectar fica desabilitado', async () => {
    abrir(ACCOUNT_SO_GOOGLE);

    const card = await cartao();
    const google = (await within(card).findByText('ana@empresa.com')).closest('tr') as HTMLElement;
    expect(within(google).getByRole('button', { name: 'Desconectar' })).toBeDisabled();
    expect(within(google).getByText('É a sua única forma de entrada.')).toBeInTheDocument();
    expect(within(card).queryByText('Senha')).not.toBeInTheDocument();
  });

  // GUARDA 3.3: o vínculo indisponível contado como forma habilitaria o botão.
  it('O vínculo indisponível não conta como forma', async () => {
    abrir({ ...ACCOUNT_SO_GOOGLE, contasConectadas: [GOOGLE_CONECTADA, GOOGLE_FORA] });

    const card = await cartao();
    const google = (await within(card).findByText('ana@empresa.com')).closest('tr') as HTMLElement;
    expect(within(google).getByRole('button', { name: 'Desconectar' })).toBeDisabled();
    expect(within(google).getByText('É a sua única forma de entrada.')).toBeInTheDocument();
  });

  it('O tipo desconhecido sai cru', async () => {
    abrir(
      {
        ...ACCOUNT,
        formasDeEntrada: ['senha', 'keycloak'],
        contasConectadas: [
          { ...GOOGLE_CONECTADA, chave: 'sso', tipo: 'keycloak', email: 'ana@sso.test' },
        ],
      },
      {
        'GET /auth/federated/providers': {
          status: 200,
          body: { provedores: [{ chave: 'sso', tipo: 'keycloak' }] },
        },
      },
    );

    const card = await cartao();
    await within(card).findByText('ana@sso.test');
    expect(linha(card, 'keycloak')).toBeDefined();
    expect(within(card).queryByRole('button', { name: /^Conectar/ })).not.toBeInTheDocument();
  });
});

async function abrirConectar(conta: unknown, routes: Record<string, FakeReply | FakeReply[]> = {}) {
  const view = abrir(conta, routes);
  const card = await cartao();
  await view.user.click(await within(card).findByRole('button', { name: 'Conectar Google' }));
  const form = screen.getByRole('form', { name: 'Conectar a sua conta Google' });
  return { ...view, form };
}

describe('Conectar a conta do provedor', () => {
  it('O formulário com a senha', async () => {
    const { form } = await abrirConectar(ACCOUNT);

    expect(
      within(form).getByText(/Você vai ao Google, escolhe a conta e volta para cá/),
    ).toBeInTheDocument();
    expect(within(form).getByLabelText('Sua senha')).toBeInTheDocument();
    expect(within(form).getByText('Para conectar, confirme que é você.')).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Continuar com Google' })).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Cancelar' })).toBeInTheDocument();
  });

  it('O formulário com o código', async () => {
    const { form } = await abrirConectar({
      ...ACCOUNT,
      formasDeEntrada: [],
      contasConectadas: [GOOGLE_FORA],
    });

    expect(within(form).getByLabelText('Código do app autenticador')).toBeInTheDocument();
    expect(
      within(form).getByText(
        'Você entra só pelo Google: confirme com o código de 6 dígitos do app. O código de recuperação não vale aqui.',
      ),
    ).toBeInTheDocument();
  });

  // GUARDA 4.3: o conectar no modo código mandando a senha.
  it('O código vai como codigo', async () => {
    const { user, form, api } = await abrirConectar(
      { ...ACCOUNT, formasDeEntrada: [], contasConectadas: [GOOGLE_FORA] },
      {
        [CONECTAR]: {
          status: 200,
          body: { url: 'https://accounts.google.com/o/oauth2/v2/auth?x=1' },
        },
      },
    );
    await user.type(within(form).getByLabelText('Código do app autenticador'), '123456');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    await waitFor(() => expect(api.calls(CONECTAR)).toHaveLength(1));
    expect(api.calls(CONECTAR)[0].body).toEqual({ codigo: '123456' });
  });

  it('A senha vai como senha', async () => {
    const { user, form, api } = await abrirConectar(ACCOUNT, {
      [CONECTAR]: {
        status: 200,
        body: { url: 'https://accounts.google.com/o/oauth2/v2/auth?x=1' },
      },
    });
    await user.type(within(form).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    await waitFor(() => expect(api.calls(CONECTAR)).toHaveLength(1));
    expect(api.calls(CONECTAR)[0].body).toEqual({ senha: 'minha-senha' });
  });

  it('Redirecionando', async () => {
    const url = 'https://accounts.google.com/o/oauth2/v2/auth?x=1';
    const { user, form } = await abrirConectar(ACCOUNT, {
      [CONECTAR]: { status: 200, body: { url } },
    });
    await user.type(within(form).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    expect(await screen.findByText('Redirecionando para o Google…')).toBeInTheDocument();
    expect(authRuntime.assign).toHaveBeenCalledWith(url);
    expect(
      screen.queryByRole('form', { name: 'Conectar a sua conta Google' }),
    ).not.toBeInTheDocument();
  });

  it('Código errado fica no campo', async () => {
    const { user, form, api } = await abrirConectar(
      { ...ACCOUNT, formasDeEntrada: [], contasConectadas: [GOOGLE_FORA] },
      { [CONECTAR]: problem(401, 'Código inválido.') },
    );
    await user.type(within(form).getByLabelText('Código do app autenticador'), '123456');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    expect(await within(form).findByText('Código inválido.')).toBeInTheDocument();
    expect(within(form).getByLabelText('Código do app autenticador')).toHaveAttribute(
      'aria-invalid',
      'true',
    );
    expect(within(form).getByLabelText('Código do app autenticador')).toHaveValue('');
    expect(api.calls('POST /connect/token')).toHaveLength(0);
  });

  it('Provedor não configurado', async () => {
    const { user, form, api } = await abrirConectar(ACCOUNT, {
      [CONECTAR]: problem(404, 'Provedor não configurado.'),
      'GET /auth/federated/providers': [
        { status: 200, body: PROVEDORES_GOOGLE },
        { status: 200, body: { provedores: [] } },
      ],
    });
    await user.type(within(form).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    expect(await within(form).findByText('Provedor não configurado.')).toBeInTheDocument();
    await waitFor(() => expect(api.calls('GET /auth/federated/providers')).toHaveLength(2));
    await waitFor(() => expect(screen.queryByText('Não conectada')).not.toBeInTheDocument());
  });

  it('Outro 404', async () => {
    const { user, form, api } = await abrirConectar(ACCOUNT, { [CONECTAR]: { status: 404 } });
    await user.type(within(form).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    expect(
      await within(form).findByText('Não foi possível conectar a conta Google. Tente de novo.'),
    ).toBeInTheDocument();
    expect(api.calls('GET /auth/federated/providers')).toHaveLength(1);
  });

  it('Provedor indisponível', async () => {
    const { user, form } = await abrirConectar(ACCOUNT, {
      [CONECTAR]: problem(503, 'Indisponível.'),
    });
    await user.type(within(form).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    expect(
      await within(form).findByText(
        'Não foi possível falar com o provedor. Tente de novo em instantes.',
      ),
    ).toBeInTheDocument();
  });

  it('Conta pausada no conectar', async () => {
    const { user, form } = await abrirConectar(ACCOUNT, {
      [CONECTAR]: problem(
        423,
        'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.',
        { 'Retry-After': '300' },
      ),
    });
    await user.type(within(form).getByLabelText('Sua senha'), 'minha-senha');
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    expect(
      await within(form).findByText(
        'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.',
      ),
    ).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Continuar com Google' })).toBeDisabled();
  });

  it('Campo vazio não chama a rota', async () => {
    const { user, form, api } = await abrirConectar(ACCOUNT);
    await user.click(within(form).getByRole('button', { name: 'Continuar com Google' }));

    expect(await within(form).findByText('Informe a sua senha.')).toBeInTheDocument();
    expect(api.calls(CONECTAR)).toHaveLength(0);
  });
});

async function pedirDesconectar(routes: Record<string, FakeReply | FakeReply[]>) {
  const view = abrir([COM_GOOGLE, COM_GOOGLE], routes);
  const card = await cartao();
  await view.user.click(await within(card).findByRole('button', { name: 'Desconectar' }));
  return { ...view, card };
}

describe('Desconectar a própria conta do provedor', () => {
  it('A confirmação na linha', async () => {
    const { card } = await pedirDesconectar({});

    const google = linha(card, 'Google');
    expect(
      within(google).getByText(
        'Desconectar o Google (ana@empresa.com)? Você deixa de entrar com essa conta.',
      ),
    ).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Desconectar' })).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Cancelar' })).toBeInTheDocument();
  });

  it('Desconectada', async () => {
    const { card, user, api } = await pedirDesconectar({ [DESCONECTAR]: { status: 204 } });
    await user.click(within(linha(card, 'Google')).getByRole('button', { name: 'Desconectar' }));

    expect(await within(card).findByText('Conta Google desconectada.')).toBeInTheDocument();
    await waitFor(() => expect(api.calls('GET /account')).toHaveLength(2));
    expect(api.calls(DESCONECTAR)[0].body).toBeUndefined();
    expect(api.calls('POST /connect/token')).toHaveLength(0);
    expect(authRuntime.assign).not.toHaveBeenCalled();
  });

  it('A última forma pela outra aba', async () => {
    const { card, user, api } = await pedirDesconectar({
      [DESCONECTAR]: problem(409, 'A última forma de entrada não pode ser removida.'),
    });
    await user.click(within(linha(card, 'Google')).getByRole('button', { name: 'Desconectar' }));

    const google = linha(card, 'Google');
    expect(
      await within(google).findByText('A última forma de entrada não pode ser removida.'),
    ).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Fechar' })).toBeInTheDocument();
    await waitFor(() => expect(api.calls('GET /account')).toHaveLength(2));
  });

  it('Já desconectada em outra aba', async () => {
    const { card, user, api } = await pedirDesconectar({
      [DESCONECTAR]: problem(404, 'Esta conta não está conectada a esse provedor.'),
    });
    await user.click(within(linha(card, 'Google')).getByRole('button', { name: 'Desconectar' }));

    expect(
      await within(card).findByText('Esta conta não está conectada a esse provedor.'),
    ).toBeInTheDocument();
    await waitFor(() => expect(api.calls('GET /account')).toHaveLength(2));
  });
});
