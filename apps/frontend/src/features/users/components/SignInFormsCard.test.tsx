import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { BRUNO, PROPRIO, PROPRIO_ID, RAFAEL, RESET_LINK, fakeUsersApi, problem, type FakeReply } from '../test/fakeUsersApi';
import { GOOGLE_CONECTADA, GOOGLE_FORA } from '../../account/test/fakeAccountApi';
import type { UserDetail } from '../api/usersApi';
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
    // O rodapé e as colunas da M3 (lista do topo da change telas-contas-federadas, D5).
    expect(
      screen.getByText(
        'Contas de provedores são conectadas pela própria pessoa em Minha conta. Aqui o administrador só desconecta. A última forma de entrada não pode ser removida.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Conectada em' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Último uso' })).toBeInTheDocument();
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

// As contas conectadas em Editar usuário (spec `panel-user-detail-page`, "Formas de entrada" e
// "Redefinir senha com a senha de quem chama"; spec `panel-federated-accounts`, "Desconectar pelo Admin
// em Editar usuário"; design.md da change telas-contas-federadas, D5; quadro `UsuarioDetalhe`,
// `entrega=m3`, versão 81). O caso marcado GUARDA é o da 7.5 do tasks.md.

const GOOGLE_RAFAEL = { ...GOOGLE_CONECTADA, email: 'rafael.duarte@empresa.com' };
const RAFAEL_COM_GOOGLE: UserDetail = { ...RAFAEL, formasDeEntrada: ['senha', 'google'], contasConectadas: [GOOGLE_RAFAEL] };
const RAFAEL_SO_GOOGLE: UserDetail = { ...RAFAEL, formasDeEntrada: ['google'], contasConectadas: [GOOGLE_RAFAEL] };
const DESCONECTAR = 'DELETE /users/u-rafael/federated/google';

// O `SectionedCard` não é região: o cartão é o `Paper` em volta do título.
const formas = async () =>
  (await screen.findByText('Formas de entrada')).closest('.mantine-Paper-root') as HTMLElement;
const linhaGoogle = async () =>
  (await within(await formas()).findByText('Google', { selector: 'td' })).closest('tr') as HTMLElement;

function detalhe(body: UserDetail | UserDetail[], extra: Record<string, FakeReply | FakeReply[]> = {}, id = 'u-rafael') {
  const user = userEvent.setup();
  const api = fakeUsersApi({
    [`GET /users/${id}`]: Array.isArray(body) ? body.map((b) => ({ status: 200, body: b })) : { status: 200, body },
    ...extra,
  });
  renderUserDetail(id);
  return { user, api };
}

describe('Formas de entrada, com as contas conectadas', () => {
  it('A senha e o Google', async () => {
    detalhe(RAFAEL_COM_GOOGLE);

    const google = await linhaGoogle();
    expect(within(google).getByText('rafael.duarte@empresa.com')).toBeInTheDocument();
    expect(within(google).getByText('14/08/2026')).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Desconectar' })).toBeEnabled();
  });

  it('A pessoa sem senha', async () => {
    detalhe(RAFAEL_SO_GOOGLE);

    const card = await formas();
    const senha = (await within(card).findByText('Senha', { selector: 'td' })).closest('tr') as HTMLElement;
    expect(within(senha).getByText('Sem senha')).toBeInTheDocument();
    expect(within(senha).getByRole('button', { name: 'Gerar link de senha' })).toBeInTheDocument();
  });

  it('O vínculo fora da configuração, no Admin', async () => {
    detalhe({ ...RAFAEL, contasConectadas: [{ ...GOOGLE_FORA, email: 'rafael@antiga.com.br' }] });

    const fora = (await within(await formas()).findByText('rafael@antiga.com.br')).closest('tr') as HTMLElement;
    expect(within(fora).getByText('Provedor fora da configuração')).toBeInTheDocument();
    expect(within(fora).getByText('Sem ação')).toBeInTheDocument();
    expect(within(fora).queryByRole('button')).not.toBeInTheDocument();
  });

  it('O link de senha para quem entra só pelo Google', async () => {
    const { user } = detalhe(RAFAEL_SO_GOOGLE);

    await user.click(await screen.findByRole('button', { name: 'Gerar link de senha' }));

    const form = screen.getByRole('form', { name: 'Gerar link para Rafael Duarte criar uma senha' });
    expect(
      within(form).getByText(
        'Hoje a pessoa entra só pelo Google. Com o link, ela cria uma senha e passa a ter as duas formas de entrada.',
      ),
    ).toBeInTheDocument();
  });
});

describe('Desconectar pelo Admin em Editar usuário', () => {
  it('O Admin desconecta o Google de outra pessoa', async () => {
    const { user } = detalhe(RAFAEL_COM_GOOGLE);

    await user.click(within(await linhaGoogle()).getByRole('button', { name: 'Desconectar' }));

    const google = await linhaGoogle();
    expect(within(google).getByText('Desconectar o Google de Rafael Duarte? A pessoa sai de todos os navegadores.')).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Desconectar' })).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Cancelar' })).toBeInTheDocument();
  });

  it('Depois do 204', async () => {
    const { user, api } = detalhe(
      [RAFAEL_COM_GOOGLE, { ...RAFAEL, sessoes: { ativas: 0, ultimaAtividade: null } }],
      { [DESCONECTAR]: { status: 204 }, 'GET /users': { status: 200, body: [] } },
    );

    await user.click(within(await linhaGoogle()).getByRole('button', { name: 'Desconectar' }));
    await user.click(within(await linhaGoogle()).getByRole('button', { name: 'Desconectar' }));

    expect(await screen.findByText('Google desconectado. Rafael Duarte saiu de todos os navegadores.')).toBeInTheDocument();
    expect(await screen.findByText('Nenhuma sessão ativa')).toBeInTheDocument();
    expect(api.calls(DESCONECTAR)[0].body).toBeUndefined();
    await waitFor(() => expect(api.calls('GET /users/u-rafael')).toHaveLength(2));
  });

  it('A única forma de outra pessoa', async () => {
    detalhe(RAFAEL_SO_GOOGLE);

    const google = await linhaGoogle();
    expect(within(google).getByRole('button', { name: 'Desconectar' })).toBeDisabled();
    expect(within(google).getByText('É a única forma de entrada de Rafael Duarte.')).toBeInTheDocument();
  });

  // GUARDA 7.5: o Admin vendo "Desconectar" na própria linha, que o backend recusa com `403`.
  it('O próprio desconecta em Minha conta', async () => {
    detalhe(
      { ...PROPRIO, formasDeEntrada: ['senha', 'google'], contasConectadas: [GOOGLE_CONECTADA] },
      {},
      PROPRIO_ID,
    );

    const google = await linhaGoogle();
    expect(within(google).getByRole('link', { name: 'Desconectar em Minha conta' })).toHaveAttribute('href', '/account');
    expect(within(google).queryByRole('button', { name: 'Desconectar' })).not.toBeInTheDocument();
  });

  it('O desativado também desconecta', async () => {
    detalhe(
      { ...BRUNO, id: 'u-rafael', formasDeEntrada: ['senha', 'google'], contasConectadas: [GOOGLE_RAFAEL] },
    );

    expect(within(await linhaGoogle()).getByRole('button', { name: 'Desconectar' })).toBeEnabled();
  });

  it('A última forma pela outra aba, no Admin', async () => {
    const { user, api } = detalhe([RAFAEL_COM_GOOGLE, RAFAEL_COM_GOOGLE], {
      [DESCONECTAR]: problem(409, 'A última forma de entrada não pode ser removida.'),
    });

    await user.click(within(await linhaGoogle()).getByRole('button', { name: 'Desconectar' }));
    await user.click(within(await linhaGoogle()).getByRole('button', { name: 'Desconectar' }));

    const google = await linhaGoogle();
    expect(await within(google).findByText('A última forma de entrada não pode ser removida.')).toBeInTheDocument();
    expect(within(google).getByRole('button', { name: 'Fechar' })).toBeInTheDocument();
    await waitFor(() => expect(api.calls('GET /users/u-rafael')).toHaveLength(2));
  });
});

// "Uma ação aberta por vez" (spec `panel-user-detail-page`): o desconectar é uma das ações da página
// (design.md da change telas-contas-federadas, "O que o apply mudou", 3).
describe('Uma ação aberta por vez, com o desconectar', () => {
  it('Abrir outra ação fecha o desconectar', async () => {
    const { user } = detalhe(RAFAEL_COM_GOOGLE);

    await user.click(within(await linhaGoogle()).getByRole('button', { name: 'Desconectar' }));
    expect(
      screen.getByText('Desconectar o Google de Rafael Duarte? A pessoa sai de todos os navegadores.'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Redefinir senha' }));

    expect(screen.queryByText(/Desconectar o Google de Rafael Duarte\?/)).not.toBeInTheDocument();
    expect(formulario()).toBeInTheDocument();
  });
});

