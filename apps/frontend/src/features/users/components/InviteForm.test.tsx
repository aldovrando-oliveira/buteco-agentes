import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime, sessionStore } from '../../../auth/session';
import { ATIVO, CONVIDADA, LINK, fakeUsersApi, problem } from '../test/fakeUsersApi';
import { renderUsers } from '../test/renderUsers';
import type { UserSummary } from '../api/usersApi';

// "Convidar usuário" e a senha de quem chama (requisitos "Convidar usuário", "Senha de quem
// chama" e "Um painel por vez no cartão Usuários"). O `sessionFetch` é o real: o `fetch` falso
// fica por baixo dele, e o `401` com problem+json passa pelo critério de verdade (#246 D10).

const MARCOS: UserSummary = {
  ...CONVIDADA,
  id: 'u-marcos',
  nome: 'Marcos Lima',
  email: 'marcos@exemplo.test',
};

const CRIADO = { usuario: MARCOS, link: LINK, expiraEm: '2026-11-10T21:29:00Z' };

beforeEach(() => {
  signInForTest();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function abrirConvite(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Convidar usuário' }));
  return screen.getByRole('form', { name: 'Convidar usuário' });
}

// O papel é obrigatório desde a #184 (change papeis-no-painel): o preenchimento escolhe
// Operador, e só os casos do papel ausente deixam de escolher. É mudança de arranjo, sem mudar
// nenhuma asserção dos casos de antes.
async function preencher(user: ReturnType<typeof userEvent.setup>, senha = 'minha-senha-certa', papel: string | null = 'operador') {
  await user.type(screen.getByLabelText('Nome'), 'Marcos Lima');
  await user.type(screen.getByLabelText('E-mail'), 'marcos@exemplo.test');
  if (papel) await user.selectOptions(screen.getByLabelText('Papel'), papel);
  if (senha) await user.type(screen.getByLabelText('Sua senha'), senha);
}

describe('Convidar usuário', () => {
  it('o formulário tem os campos, o texto das 72 horas e os botões', async () => {
    const user = userEvent.setup();
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] } });
    renderUsers();

    const form = await abrirConvite(user);

    expect(within(form).getByLabelText('Nome')).toBeInTheDocument();
    expect(within(form).getByLabelText('E-mail')).toBeInTheDocument();
    expect(within(form).getByLabelText('Sua senha')).toHaveAttribute('autocomplete', 'current-password');
    expect(within(form).getByText('Para convidar alguém, confirme que é você.')).toBeInTheDocument();
    expect(within(form).getByText(/válido por 72 horas/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Convidar usuário' })).toBeDisabled();
  });

  it('convite criado', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users': [{ status: 200, body: [ATIVO] }, { status: 200, body: [ATIVO, MARCOS] }],
      'POST /users/invitations': { status: 201, body: CRIADO },
    });
    renderUsers();

    await abrirConvite(user);
    await preencher(user);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(await screen.findByRole('heading', { name: 'Convite criado para Marcos Lima' })).toBeInTheDocument();
    expect(screen.getByText(LINK)).toBeInTheDocument();
    // O corpo leva o papel (change papeis-no-painel; caso que muda de asserção, listado no tasks.md).
    expect(api.calls('POST /users/invitations')[0].body).toEqual({
      nome: 'Marcos Lima',
      email: 'marcos@exemplo.test',
      papel: 'operador',
      senha: 'minha-senha-certa',
    });
    const painel = screen.getByRole('status', { name: 'Convite criado para Marcos Lima' });
    expect(painel).toHaveTextContent('marcos@exemplo.test· Leitor');
    await waitFor(() => expect(api.calls('GET /users')).toHaveLength(2));
    await waitFor(() => expect(screen.getByText('Marcos Lima', { selector: 'p' }).closest('tr')).toHaveAttribute('data-marcada', 'true'));
  });

  it('e-mail em uso', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'POST /users/invitations': problem(409, 'Já existe um usuário com este e-mail.'),
    });
    renderUsers();

    await abrirConvite(user);
    await preencher(user);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    const email = screen.getByLabelText('E-mail');
    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'));
    expect(screen.getByText('Já existe um usuário com este e-mail.')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('form', { name: 'Convidar usuário' })).toBeInTheDocument();
  });

  it('campos inválidos', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'POST /users/invitations': {
        status: 400,
        body: { title: 'One or more validation errors occurred.', errors: { email: ['Informe um e-mail válido.'] } },
      },
    });
    renderUsers();

    await abrirConvite(user);
    await preencher(user);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(await screen.findByText('Informe um e-mail válido.')).toBeInTheDocument();
  });

  it('a linha nova fica na ordem do servidor', async () => {
    const user = userEvent.setup();
    const JOANA = { ...CONVIDADA, nome: 'Joana' };
    const PAULO = { ...ATIVO, id: 'u-paulo', nome: 'Paulo' };
    fakeUsersApi({
      'GET /users': [{ status: 200, body: [JOANA, PAULO] }, { status: 200, body: [JOANA, MARCOS, PAULO] }],
      'POST /users/invitations': { status: 201, body: CRIADO },
    });
    renderUsers();

    await abrirConvite(user);
    await preencher(user);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(4));
    const nomes = screen.getAllByRole('row').slice(1).map((tr) => within(tr).getAllByRole('cell')[0].querySelector('p')?.textContent);
    expect(nomes).toEqual(['Joana', 'Marcos Lima', 'Paulo']);
    const marcos = screen.getAllByRole('row')[2];
    expect(marcos).toHaveAttribute('data-marcada', 'true');

    await user.click(screen.getByRole('button', { name: 'Concluir' }));
    expect(screen.getAllByRole('row')[2]).not.toHaveAttribute('data-marcada');
  });
});

describe('Senha de quem chama', () => {
  it('senha vazia não é enviada', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] } });
    renderUsers();

    await abrirConvite(user);
    await preencher(user, '');
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(screen.getByText('Informe a sua senha.')).toBeInTheDocument();
    expect(screen.getByLabelText('Sua senha')).toHaveAttribute('aria-invalid', 'true');
    expect(api.calls('POST /users/invitations')).toHaveLength(0);
  });

  it('senha errada fica no campo, sem deslogar', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'POST /users/invitations': problem(401, 'Senha inválida.'),
    });
    renderUsers();

    await abrirConvite(user);
    await preencher(user, 'senha-errada');
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(await screen.findByText('Senha inválida.')).toBeInTheDocument();
    expect(screen.getByLabelText('Sua senha')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Sua senha')).toHaveValue('');
    expect(api.calls('POST /users/invitations')).toHaveLength(1);
    expect(api.requests.filter((r) => r.path.startsWith('/connect/'))).toEqual([]);
    expect(sessionStore.read()).not.toBeNull();
    expect(authRuntime.assign).not.toHaveBeenCalled();
    expect(screen.getByRole('form', { name: 'Convidar usuário' })).toBeInTheDocument();
  });

  it('conta pausada', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'POST /users/invitations': problem(423, 'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.', {
        'Retry-After': '300',
      }),
    });
    renderUsers();

    await abrirConvite(user);
    await preencher(user);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.',
    );
    expect(screen.getByRole('button', { name: 'Gerar convite' })).toBeDisabled();
    vi.advanceTimersByTime(300_000);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Gerar convite' })).toBeEnabled());
    vi.useRealTimers();
  });

  it('falha de rede vai no aviso do formulário', async () => {
    const user = userEvent.setup();
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] }, 'POST /users/invitations': 'rede' });
    renderUsers();

    await abrirConvite(user);
    await preencher(user);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível falar com o servidor.');
  });

  // Spec `panel-users-page`, "Convidar usuário" (change papeis-no-painel).
  it('Sem papel escolhido', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] } });
    renderUsers();

    await abrirConvite(user);
    await preencher(user, 'minha-senha-certa', null);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(await screen.findByText('Escolha o papel.')).toBeInTheDocument();
    expect(screen.getByLabelText('Papel')).toHaveAttribute('aria-invalid', 'true');
    expect(api.calls('POST /users/invitations')).toHaveLength(0);
  });

  it('A descrição acompanha a escolha', async () => {
    const user = userEvent.setup();
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] } });
    renderUsers();

    const form = await abrirConvite(user);
    expect(within(form).getByLabelText('Papel')).toHaveValue('');
    expect(within(form).getByRole('option', { name: 'Escolha o papel' })).toBeInTheDocument();
    expect(
      within(form).getByText('O que a pessoa pode fazer no painel. Dá para mudar depois em Editar usuário.'),
    ).toBeInTheDocument();

    await user.selectOptions(within(form).getByLabelText('Papel'), 'leitor');
    expect(
      within(form).getByText(
        'Vê agentes, MCP, conhecimento, canais e Insights. Não altera nada, não vê o histórico de conversas nem usa os conectores.',
      ),
    ).toBeInTheDocument();
    await user.selectOptions(within(form).getByLabelText('Papel'), 'operador');
    expect(
      within(form).getByText(
        'Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade.',
      ),
    ).toBeInTheDocument();
    await user.selectOptions(within(form).getByLabelText('Papel'), 'admin');
    expect(within(form).getByText('Tudo, inclusive usuários e Identidade.')).toBeInTheDocument();
  });

  it('o erro de papel do servidor vai ao campo', async () => {
    const user = userEvent.setup();
    fakeUsersApi({
      'GET /users': { status: 200, body: [ATIVO] },
      'POST /users/invitations': {
        status: 400,
        body: { title: 'One or more validation errors occurred.', errors: { papel: ['O papel precisa ser admin, operador ou leitor.'] } },
      },
    });
    renderUsers();

    await abrirConvite(user);
    await preencher(user);
    await user.click(screen.getByRole('button', { name: 'Gerar convite' }));

    expect(await screen.findByText('O papel precisa ser admin, operador ou leitor.')).toBeInTheDocument();
  });
});
