import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../test/authFakes';
import { authRuntime } from '../../auth/session';
import { ACCOUNT, ACCOUNT_SO_GOOGLE } from '../account/test/fakeAccountApi';
import { CRIADA, detalhe, fakeApplicationsApi } from '../applications/test/fixtures';
import { renderApplications } from '../applications/test/renderApplications';
import {
  ATIVO,
  CONVIDADA,
  LINK,
  PROPRIO_ID,
  RAFAEL,
  RESET_LINK,
  fakeUsersApi,
  problem,
} from './test/fakeUsersApi';
import { renderUsers } from './test/renderUsers';
import { renderUserDetail, signInAs } from './test/renderUserDetail';

// A confirmação de quem chama com o código do app (requisito "Senha de quem chama", `panel-users-page`;
// design.md da change telas-contas-federadas, D2). O modo vem de `GET /account`: sem `"senha"` em
// `formasDeEntrada`, o campo é o do código, e o corpo leva `codigo` no lugar de `senha` (nunca os dois).
// Os seis formulários não estão desenhados no modo código; a referência é `MinhaConta/confirmar-codigo`.
// Os casos de corpo exato são os dos guardas 2.7 e 2.8 do tasks.md.

const SEM_SENHA = { 'GET /account': { status: 200, body: ACCOUNT_SO_GOOGLE } };
const CODIGO = 'Código do app autenticador';
const AJUDA =
  'Você entra só pelo Google: confirme com o código de 6 dígitos do app. O código de recuperação não vale aqui.';

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function abrirConvite() {
  const user = userEvent.setup();
  renderUsers();
  await user.click(await screen.findByRole('button', { name: 'Convidar usuário' }));
  const form = screen.getByRole('form', { name: 'Convidar usuário' });
  await user.type(within(form).getByLabelText('Nome'), 'Marcos Lima');
  await user.type(within(form).getByLabelText('E-mail'), 'marcos@exemplo.test');
  await user.selectOptions(within(form).getByLabelText('Papel'), 'operador');
  return { user, form };
}

const CRIADO = {
  usuario: { ...CONVIDADA, id: 'u-marcos', nome: 'Marcos Lima' },
  link: LINK,
  expiraEm: '2026-11-10T21:29:00Z',
};

describe('Senha de quem chama: o modo pela conta', () => {
  it('Sem senha, o campo é o do código', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] }, ...SEM_SENHA });
    const { form } = await abrirConvite();

    const campo = await within(form).findByLabelText(CODIGO);
    expect(campo).toHaveAttribute('autocomplete', 'one-time-code');
    expect(campo).toHaveAttribute('inputmode', 'numeric');
    expect(within(form).getByText(AJUDA)).toBeInTheDocument();
    expect(within(form).queryByLabelText('Sua senha')).not.toBeInTheDocument();
  });

  it('Com a senha, o campo é o de hoje', async () => {
    fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'GET /account': { status: 200, body: ACCOUNT },
    });
    const { form } = await abrirConvite();

    expect(within(form).getByLabelText('Sua senha')).toHaveAttribute(
      'autocomplete',
      'current-password',
    );
    expect(within(form).queryByLabelText(CODIGO)).not.toBeInTheDocument();
  });

  it('Com a conta em erro, o modo é a senha', async () => {
    const api = fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'GET /account': 'rede',
    });
    const { form } = await abrirConvite();

    await waitFor(() => expect(api.calls('GET /account')).toHaveLength(1));
    expect(within(form).getByLabelText('Sua senha')).toBeInTheDocument();
  });

  it('Com a conta pendente, o modo é a senha', async () => {
    fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'GET /account': () => new Promise(() => {}),
    });
    const { form } = await abrirConvite();

    expect(within(form).getByLabelText('Sua senha')).toBeInTheDocument();
  });

  it('Código vazio não é enviado', async () => {
    const api = fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] }, ...SEM_SENHA });
    const { user, form } = await abrirConvite();
    await within(form).findByLabelText(CODIGO);

    await user.click(within(form).getByRole('button', { name: 'Gerar convite' }));

    expect(await within(form).findByText('Informe o código.')).toBeInTheDocument();
    expect(api.calls('POST /users/invitations')).toHaveLength(0);
  });

  it('Código errado fica no campo', async () => {
    const api = fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      ...SEM_SENHA,
      'POST /users/invitations': problem(401, 'Código inválido.'),
    });
    const { user, form } = await abrirConvite();
    await user.type(await within(form).findByLabelText(CODIGO), '123456');
    await user.click(within(form).getByRole('button', { name: 'Gerar convite' }));

    expect(await within(form).findByText('Código inválido.')).toBeInTheDocument();
    expect(within(form).getByLabelText(CODIGO)).toHaveAttribute('aria-invalid', 'true');
    expect(within(form).getByLabelText(CODIGO)).toHaveValue('');
    expect(api.calls('POST /connect/token')).toHaveLength(0);
  });

  // GUARDA 2.6: sem a releitura, o campo fica "Sua senha" para quem só tem o código.
  it('O código inválido no modo senha relê a conta', async () => {
    const api = fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'GET /account': ['rede', { status: 200, body: ACCOUNT_SO_GOOGLE }],
      'POST /users/invitations': problem(401, 'Código inválido.'),
    });
    const { user, form } = await abrirConvite();
    await waitFor(() => expect(api.calls('GET /account')).toHaveLength(1));
    await user.type(within(form).getByLabelText('Sua senha'), '123456');
    await user.click(within(form).getByRole('button', { name: 'Gerar convite' }));

    expect(await within(form).findByLabelText(CODIGO)).toBeInTheDocument();
    expect(api.calls('GET /account')).toHaveLength(2);
  });

  // GUARDA 2.9: o "Código inválido." da tentativa com a senha herdado pelo campo do código.
  it('A troca para o código chega limpa', async () => {
    const api = fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'GET /account': ['rede', { status: 200, body: ACCOUNT_SO_GOOGLE }],
      'POST /users/invitations': problem(401, 'Código inválido.'),
    });
    const { user, form } = await abrirConvite();
    await waitFor(() => expect(api.calls('GET /account')).toHaveLength(1));
    await user.type(within(form).getByLabelText('Sua senha'), 'senha-digitada');
    await user.click(within(form).getByRole('button', { name: 'Gerar convite' }));

    const campo = await within(form).findByLabelText(CODIGO);
    expect(campo).toHaveValue('');
    expect(campo).not.toHaveAttribute('aria-invalid', 'true');
    expect(within(form).queryByText('Código inválido.')).not.toBeInTheDocument();
    expect(within(form).getByText(AJUDA)).toBeInTheDocument();
  });
});

// GUARDAS 2.7 e 2.8: o corpo exato, com `codigo` e sem `senha`, em cada um dos seis formulários.
describe('Senha de quem chama: o código vai como codigo, nos seis formulários', () => {
  it('convite', async () => {
    const api = fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      ...SEM_SENHA,
      'POST /users/invitations': { status: 201, body: CRIADO },
    });
    const { user, form } = await abrirConvite();
    await user.type(await within(form).findByLabelText(CODIGO), '123456');
    await user.click(within(form).getByRole('button', { name: 'Gerar convite' }));

    await waitFor(() => expect(api.calls('POST /users/invitations')).toHaveLength(1));
    expect(api.calls('POST /users/invitations')[0].body).toEqual({
      nome: 'Marcos Lima',
      email: 'marcos@exemplo.test',
      papel: 'operador',
      codigo: '123456',
    });
  });

  it('novo link', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users': { status: 200, body: [CONVIDADA] },
      ...SEM_SENHA,
      'POST /users/u-joana/invitation/renew': {
        status: 200,
        body: { link: LINK, expiraEm: '2026-11-10T21:29:00Z' },
      },
    });
    renderUsers();
    await user.click(
      await screen.findByRole('button', { name: 'Gerar novo link para Joana Prado' }),
    );
    const form = screen.getByRole('form', { name: 'Gerar novo link para Joana Prado' });
    await user.type(await within(form).findByLabelText(CODIGO), '123456');
    await user.click(within(form).getByRole('button', { name: 'Gerar novo link' }));

    await waitFor(() => expect(api.calls('POST /users/u-joana/invitation/renew')).toHaveLength(1));
    expect(api.calls('POST /users/u-joana/invitation/renew')[0].body).toEqual({ codigo: '123456' });
  });

  it('redefinição de senha', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      ...SEM_SENHA,
      'POST /users/u-rafael/password-reset': {
        status: 200,
        body: { link: RESET_LINK, expiraEm: '2026-11-08T18:00:00Z' },
      },
    });
    renderUserDetail('u-rafael');
    await user.click(await screen.findByRole('button', { name: 'Redefinir senha' }));
    await user.type(await screen.findByLabelText(CODIGO), '123456');
    await user.click(screen.getByRole('button', { name: 'Gerar link' }));

    await waitFor(() => expect(api.calls('POST /users/u-rafael/password-reset')).toHaveLength(1));
    expect(api.calls('POST /users/u-rafael/password-reset')[0].body).toEqual({ codigo: '123456' });
  });

  it('promoção a Admin', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users/u-rafael': { status: 200, body: RAFAEL },
      ...SEM_SENHA,
      'PUT /users/u-rafael/role': { status: 200, body: { ...RAFAEL, papel: 'admin' } },
    });
    renderUserDetail('u-rafael');
    await user.selectOptions(await screen.findByLabelText('Papel'), 'admin');

    expect(
      await screen.findByText(
        'Como Admin, Rafael Duarte passa a gerir usuários e a ver Identidade. Para promover, confirme com o código do app autenticador.',
      ),
    ).toBeInTheDocument();
    await user.type(screen.getByLabelText(CODIGO), '123456');
    await user.click(screen.getByRole('button', { name: 'Salvar alterações' }));

    await waitFor(() => expect(api.calls('PUT /users/u-rafael/role')).toHaveLength(1));
    expect(api.calls('PUT /users/u-rafael/role')[0].body).toEqual({
      papel: 'admin',
      codigo: '123456',
    });
  });

  it('criar aplicação', async () => {
    const api = fakeApplicationsApi({
      ...SEM_SENHA,
      'POST /applications/': { status: 201, body: CRIADA },
    });
    const user = userEvent.setup();
    renderApplications('/applications/new');
    await user.type(await screen.findByLabelText('Nome'), 'Portal do Parceiro');
    await user.type(await screen.findByLabelText(CODIGO), '123456');
    await user.click(screen.getByRole('button', { name: 'Criar aplicação' }));

    await waitFor(() => expect(api.calls('POST /applications/')).toHaveLength(1));
    expect(api.calls('POST /applications/')[0].body).toEqual({
      nome: 'Portal do Parceiro',
      descricao: null,
      codigo: '123456',
    });
  });

  it('gerar segredo', async () => {
    const api = fakeApplicationsApi({
      ...SEM_SENHA,
      'GET /applications/app-portal': { status: 200, body: detalhe() },
      'GET /applications/app-portal/agents': { status: 200, body: [] },
      'POST /applications/app-portal/secrets': problem(
        409,
        'Revogue um segredo antes de gerar outro.',
      ),
    });
    const user = userEvent.setup();
    renderApplications('/applications/app-portal?tab=credencial');
    await user.click(await screen.findByRole('button', { name: 'Gerar novo segredo' }));
    await user.type(await screen.findByLabelText(CODIGO), '123456');
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));

    await waitFor(() => expect(api.calls('POST /applications/app-portal/secrets')).toHaveLength(1));
    expect(api.calls('POST /applications/app-portal/secrets')[0].body).toEqual({
      codigo: '123456',
    });
  });
});
