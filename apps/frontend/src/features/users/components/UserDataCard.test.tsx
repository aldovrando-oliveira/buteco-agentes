import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { ACCOUNT_QUERY_KEY } from '../../account/queryKeys';
import { USERS_QUERY_KEY } from '../queryKeys';
import { BRUNO, PROPRIO, PROPRIO_ID, RAFAEL, fakeUsersApi, problem } from '../test/fakeUsersApi';
import { renderUserDetail, signInAs } from '../test/renderUserDetail';

// O cartão Dados de Editar usuário (requisito "Cartão Dados de Editar usuário"; design.md da
// change editar-usuario-painel, D5; decisão do dono Q13).

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const salvar = () => screen.getByRole('button', { name: 'Salvar alterações' });

async function abrir(routes: Parameters<typeof fakeUsersApi>[0] = {}, id = 'u-rafael') {
  const user = userEvent.setup();
  const api = fakeUsersApi({ 'GET /users/u-rafael': { status: 200, body: RAFAEL }, ...routes });
  const rendered = renderUserDetail(id);
  await screen.findByRole('heading', { level: 2 });
  return { user, api, ...rendered };
}

describe('Cartão Dados', () => {
  it('Salvar desabilitado sem mudança', async () => {
    await abrir();

    expect(screen.getByLabelText('Nome')).toHaveValue('Rafael Duarte');
    expect(screen.getByLabelText('E-mail')).toHaveValue('rafael.duarte@empresa.com');
    expect(screen.getByText('É o e-mail de entrada no painel.')).toBeInTheDocument();
    expect(salvar()).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Descartar' })).not.toBeInTheDocument();
  });

  it('voltar ao valor lido desabilita Salvar', async () => {
    const { user } = await abrir();
    const nome = screen.getByLabelText('Nome');

    await user.type(nome, 'x');
    expect(salvar()).toBeEnabled();
    await user.type(nome, '{Backspace}');

    expect(nome).toHaveValue('Rafael Duarte');
    expect(salvar()).toBeDisabled();
  });

  it('salvar com sucesso', async () => {
    const novo = { ...RAFAEL, nome: 'Rafael D. Souza' };
    const { user, api, queryClient } = await abrir({ 'PUT /users/u-rafael': { status: 200, body: novo } });
    const invalidar = vi.spyOn(queryClient, 'invalidateQueries');

    await user.clear(screen.getByLabelText('Nome'));
    await user.type(screen.getByLabelText('Nome'), 'Rafael D. Souza');
    await user.click(salvar());

    expect(await screen.findByText('Alterações salvas.')).toBeInTheDocument();
    expect(api.calls('PUT /users/u-rafael')[0].body).toEqual({ nome: 'Rafael D. Souza', email: 'rafael.duarte@empresa.com' });
    expect(screen.getByRole('heading', { name: 'Rafael D. Souza', level: 2 })).toBeInTheDocument();
    expect(salvar()).toBeDisabled();
    expect(invalidar).toHaveBeenCalledWith({ queryKey: USERS_QUERY_KEY, exact: true });
    expect(invalidar).not.toHaveBeenCalledWith({ queryKey: ACCOUNT_QUERY_KEY });
  });

  it('o próprio salva e Minha conta é relida', async () => {
    const { user, queryClient, api } = await abrir(
      {
        [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO },
        [`PUT /users/${PROPRIO_ID}`]: { status: 200, body: { ...PROPRIO, nome: 'Aldo Oliveira' } },
      },
      PROPRIO_ID,
    );
    const invalidar = vi.spyOn(queryClient, 'invalidateQueries');

    await user.clear(screen.getByLabelText('Nome'));
    await user.type(screen.getByLabelText('Nome'), 'Aldo Oliveira');
    await user.click(salvar());

    expect(await screen.findByText('Alterações salvas.')).toBeInTheDocument();
    expect(invalidar).toHaveBeenCalledWith({ queryKey: ACCOUNT_QUERY_KEY });
    // Sem renovação forçada (Q13): nenhum pedido ao emissor.
    expect(api.requests.filter((r) => r.path.startsWith('/connect'))).toEqual([]);
  });

  it('e-mail em uso', async () => {
    const { user } = await abrir({ 'PUT /users/u-rafael': problem(409, 'Já existe um usuário com este e-mail.') });

    await user.clear(screen.getByLabelText('E-mail'));
    await user.type(screen.getByLabelText('E-mail'), 'ana@exemplo.test');
    await user.click(salvar());

    expect(await screen.findByText('Já existe um usuário com este e-mail.')).toBeInTheDocument();
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('aria-invalid', 'true');
  });

  it('erros de validação do servidor', async () => {
    const { user } = await abrir({
      'PUT /users/u-rafael': {
        status: 400,
        body: { title: 'One or more validation errors occurred.', errors: { nome: ['O nome precisa ter de 1 a 200 caracteres.'] } },
      },
    });

    await user.clear(screen.getByLabelText('Nome'));
    await user.type(screen.getByLabelText('Nome'), ' ');
    await user.click(salvar());

    expect(await screen.findByText('O nome precisa ter de 1 a 200 caracteres.')).toBeInTheDocument();
  });

  it('falha passageira no aviso do cartão', async () => {
    const { user } = await abrir({ 'PUT /users/u-rafael': 'rede' });

    await user.type(screen.getByLabelText('Nome'), 'x');
    await user.click(salvar());

    const cartao = screen.getByLabelText('Nome').closest('form') as HTMLElement;
    expect(await within(cartao).findByRole('alert')).toBeInTheDocument();
  });

  it('Descartar', async () => {
    const { user } = await abrir();

    await user.type(screen.getByLabelText('E-mail'), 'x');
    await user.click(screen.getByRole('button', { name: 'Descartar' }));

    expect(screen.getByLabelText('E-mail')).toHaveValue('rafael.duarte@empresa.com');
    expect(salvar()).toBeDisabled();
  });

  it('o rascunho de Dados sobrevive a uma ação', async () => {
    const { user } = await abrir();

    await user.type(screen.getByLabelText('Nome'), ' Filho');
    await user.click(screen.getByRole('button', { name: 'Redefinir senha' }));
    await user.click(within(screen.getByRole('form', { name: /Gerar link para/ })).getByRole('button', { name: 'Cancelar' }));

    expect(screen.getByLabelText('Nome')).toHaveValue('Rafael Duarte Filho');
    expect(salvar()).toBeEnabled();
  });

  it('404 no salvar leva a Usuário não encontrado', async () => {
    const { user } = await abrir({
      'GET /users/u-rafael': [{ status: 200, body: RAFAEL }, problem(404, 'Usuário não encontrado.')],
      'PUT /users/u-rafael': problem(404, 'Usuário não encontrado.'),
    });

    await user.type(screen.getByLabelText('Nome'), 'x');
    await user.click(salvar());

    await waitFor(() => expect(screen.getByText('Usuário não encontrado')).toBeInTheDocument());
  });
});

// "Dados e papel" (spec `panel-user-detail-page`, "Cartão Dados de Editar usuário" e "Rebaixar a
// si mesmo"; change papeis-no-painel, D4). A sessão é a de um Admin (`signInAs`).
describe('Dados e papel', () => {
  const papel = () => screen.getByLabelText('Papel');
  const RAFAEL_ADMIN = { ...RAFAEL, papel: 'admin' };
  const TEXTO_DO_PAPEL = /O novo papel chega ao painel de/;

  const ordem = (api: Awaited<ReturnType<typeof abrir>>['api']) =>
    api.requests.filter((r) => r.method === 'PUT').map((r) => `${r.method} ${r.path}`);

  it('o título, o papel lido e a descrição', async () => {
    await abrir();

    expect(screen.getByText('Dados e papel')).toBeInTheDocument();
    expect(papel()).toHaveValue('operador');
    expect(
      screen.getByText('Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade.'),
    ).toBeInTheDocument();
  });

  it('só nome e e-mail dão "Alterações salvas.", sem o texto do papel', async () => {
    const { user, api } = await abrir({ 'PUT /users/u-rafael': { status: 200, body: { ...RAFAEL, nome: 'Rafael D.' } } });

    await user.type(screen.getByLabelText('Nome'), ' D.');
    await user.click(salvar());

    expect(await screen.findByText('Alterações salvas.')).toBeInTheDocument();
    expect(screen.queryByText(TEXTO_DO_PAPEL)).not.toBeInTheDocument();
    expect(api.calls('PUT /users/u-rafael/role')).toHaveLength(0);
  });

  it('Rebaixar outro', async () => {
    const { user, api } = await abrir({
      'GET /users/u-rafael': { status: 200, body: RAFAEL_ADMIN },
      'PUT /users/u-rafael/role': { status: 200, body: { ...RAFAEL_ADMIN, papel: 'operador' } },
    });

    await user.selectOptions(papel(), 'operador');
    await user.click(salvar());

    expect(
      await screen.findByText(
        'Alterações salvas. O novo papel chega ao painel de Rafael Duarte em até 15 minutos, quando a sessão renovar; um rebaixamento já vale na hora em Usuários e Identidade.',
      ),
    ).toBeInTheDocument();
    expect(api.calls('PUT /users/u-rafael/role')[0].body).toEqual({ papel: 'operador' });
    expect(api.calls('PUT /users/u-rafael')).toHaveLength(0);
  });

  it('Dados antes do papel', async () => {
    const { user, api } = await abrir({
      'GET /users/u-rafael': { status: 200, body: RAFAEL_ADMIN },
      'PUT /users/u-rafael': { status: 200, body: { ...RAFAEL_ADMIN, nome: 'Rafael D.' } },
      'PUT /users/u-rafael/role': { status: 200, body: { ...RAFAEL_ADMIN, nome: 'Rafael D.', papel: 'leitor' } },
    });

    await user.type(screen.getByLabelText('Nome'), ' D.');
    await user.selectOptions(papel(), 'leitor');
    await user.click(salvar());

    expect(await screen.findByText(TEXTO_DO_PAPEL)).toBeInTheDocument();
    expect(ordem(api)).toEqual(['PUT /users/u-rafael', 'PUT /users/u-rafael/role']);
  });

  it('Dados com falha não mudam o papel', async () => {
    const { user, api } = await abrir({
      'GET /users/u-rafael': { status: 200, body: RAFAEL_ADMIN },
      'PUT /users/u-rafael': problem(409, 'Já existe um usuário com este e-mail.'),
    });

    await user.type(screen.getByLabelText('E-mail'), 'x');
    await user.selectOptions(papel(), 'leitor');
    await user.click(salvar());

    expect(await screen.findByText('Já existe um usuário com este e-mail.')).toBeInTheDocument();
    expect(api.calls('PUT /users/u-rafael/role')).toHaveLength(0);
  });

  it('O último Admin', async () => {
    const { user } = await abrir({
      'GET /users/u-rafael': { status: 200, body: RAFAEL_ADMIN },
      'PUT /users/u-rafael/role': problem(409, 'A instalação precisa de pelo menos um Admin ativo.'),
    });

    await user.selectOptions(papel(), 'operador');
    await user.click(salvar());

    expect(await screen.findByText('A instalação precisa de pelo menos um Admin ativo.')).toBeInTheDocument();
    expect(papel()).toHaveValue('operador');
    expect(screen.queryByText(/Alterações salvas/)).not.toBeInTheDocument();
  });

  it('Promover pede a senha', async () => {
    const { user, api } = await abrir({
      'PUT /users/u-rafael/role': { status: 200, body: RAFAEL_ADMIN },
    });

    await user.selectOptions(papel(), 'admin');
    expect(
      screen.getByText('Como Admin, Rafael Duarte passa a gerir usuários e a ver Identidade. Para promover, confirme com a sua senha.'),
    ).toBeInTheDocument();

    await user.click(salvar());
    expect(await screen.findByText('Informe a sua senha.')).toBeInTheDocument();
    expect(api.calls('PUT /users/u-rafael/role')).toHaveLength(0);

    await user.type(screen.getByLabelText('Sua senha'), 'minha-senha');
    await user.click(salvar());

    expect(await screen.findByText(TEXTO_DO_PAPEL)).toBeInTheDocument();
    expect(api.calls('PUT /users/u-rafael/role')[0].body).toEqual({ papel: 'admin', senha: 'minha-senha' });
  });

  it('Promoção com a senha errada', async () => {
    const { user, api } = await abrir({ 'PUT /users/u-rafael/role': problem(401, 'Senha inválida.') });

    await user.selectOptions(papel(), 'admin');
    await user.type(screen.getByLabelText('Sua senha'), 'errada');
    await user.click(salvar());

    expect(await screen.findByText('Senha inválida.')).toBeInTheDocument();
    expect(screen.getByLabelText('Sua senha')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Sua senha')).toHaveValue('');
    expect(api.calls('PUT /users/u-rafael/role')).toHaveLength(1);
    // O 401 com problem+json é da rota: não renova a sessão (#246, D10).
    expect(api.calls('POST /connect/token')).toHaveLength(0);
  });

  it('Promoção bloqueada', async () => {
    const { user } = await abrir({
      'PUT /users/u-rafael/role': problem(423, 'Acesso pausado por tentativas seguidas.', { 'Retry-After': '300' }),
    });

    await user.selectOptions(papel(), 'admin');
    await user.type(screen.getByLabelText('Sua senha'), 'minha-senha');
    await user.click(salvar());

    expect(await screen.findByText(/5 minutos/)).toBeInTheDocument();
    expect(salvar()).toBeDisabled();
  });

  it('Desativado não muda de papel', async () => {
    await abrir({ 'GET /users/u-bruno': { status: 200, body: { ...BRUNO, papel: 'leitor' } } }, 'u-bruno');

    expect(papel()).toBeDisabled();
    expect(screen.getByText('Reative o usuário antes de mudar o papel.')).toBeInTheDocument();
    expect(screen.getByLabelText('Nome')).toBeEnabled();
  });

  it('Descartar volta o papel', async () => {
    const { user } = await abrir();

    await user.type(screen.getByLabelText('E-mail'), 'x');
    await user.selectOptions(papel(), 'leitor');
    await user.click(screen.getByRole('button', { name: 'Descartar' }));

    expect(screen.getByLabelText('E-mail')).toHaveValue('rafael.duarte@empresa.com');
    expect(papel()).toHaveValue('operador');
    expect(salvar()).toBeDisabled();
  });

  it('o 403 sem corpo no papel é a ação recusada', async () => {
    const { user } = await abrir({ 'PUT /users/u-rafael/role': { status: 403 } });

    await user.selectOptions(papel(), 'leitor');
    await user.click(salvar());

    expect(await screen.findByText('Você não tem permissão para esta ação.')).toBeInTheDocument();
  });

  describe('Rebaixar a si mesmo', () => {
    const PROPRIO_ADMIN = { ...PROPRIO, papel: 'admin' };

    async function abrirProprio(routes: Parameters<typeof fakeUsersApi>[0] = {}) {
      return abrir({ [`GET /users/${PROPRIO_ID}`]: { status: 200, body: PROPRIO_ADMIN }, ...routes }, PROPRIO_ID);
    }

    it('A confirmação antes da rota', async () => {
      const { user, api } = await abrirProprio();

      await user.selectOptions(papel(), 'operador');
      await user.click(salvar());

      expect(
        screen.getByText(
          'Deixar de ser Admin? Usuários e Identidade saem do seu menu agora, e você vai para Minha conta. Para voltar a ser Admin, outro Admin precisa promover você.',
        ),
      ).toBeInTheDocument();
      expect(api.calls(`PUT /users/${PROPRIO_ID}/role`)).toHaveLength(0);

      await user.click(screen.getByRole('button', { name: 'Cancelar' }));
      expect(screen.queryByText(/Deixar de ser Admin\?/)).not.toBeInTheDocument();
      expect(papel()).toHaveValue('operador');
    });

    it('Rebaixado vai a Minha conta sem reler Usuários', async () => {
      const { user, api, router, queryClient } = await abrirProprio({
        [`PUT /users/${PROPRIO_ID}/role`]: { status: 200, body: { ...PROPRIO_ADMIN, papel: 'operador' } },
      });
      const invalidar = vi.spyOn(queryClient, 'invalidateQueries');

      await user.selectOptions(papel(), 'operador');
      await user.click(salvar());
      await user.click(screen.getByRole('button', { name: 'Deixar de ser Admin' }));

      await waitFor(() => expect(router.state.location.pathname).toBe('/account'));
      expect(router.state.location.state).toEqual({ papelNovo: 'operador' });
      expect(api.calls('POST /connect/token')).toHaveLength(1);
      const depois = api.requests.slice(api.requests.findIndex((r) => r.method === 'PUT') + 1);
      expect(depois.filter((r) => r.method === 'GET' && r.path.startsWith('/users'))).toEqual([]);
      expect(invalidar).not.toHaveBeenCalledWith(expect.objectContaining({ queryKey: USERS_QUERY_KEY }));
    });

    it('O último Admin não se rebaixa', async () => {
      const { user, router } = await abrirProprio({
        [`PUT /users/${PROPRIO_ID}/role`]: problem(409, 'A instalação precisa de pelo menos um Admin ativo.'),
      });

      await user.selectOptions(papel(), 'leitor');
      await user.click(salvar());
      await user.click(screen.getByRole('button', { name: 'Deixar de ser Admin' }));

      expect(await screen.findByText('A instalação precisa de pelo menos um Admin ativo.')).toBeInTheDocument();
      expect(router.state.location.pathname).toBe(`/users/${PROPRIO_ID}`);
    });

    it('Gravação parcial', async () => {
      const { user } = await abrirProprio({
        [`PUT /users/${PROPRIO_ID}`]: { status: 200, body: { ...PROPRIO_ADMIN, nome: 'Aldo' } },
        [`PUT /users/${PROPRIO_ID}/role`]: problem(409, 'A instalação precisa de pelo menos um Admin ativo.'),
      });

      await user.clear(screen.getByLabelText('Nome'));
      await user.type(screen.getByLabelText('Nome'), 'Aldo');
      await user.selectOptions(papel(), 'operador');
      await user.click(salvar());
      await user.click(screen.getByRole('button', { name: 'Deixar de ser Admin' }));

      expect(
        await screen.findByText('Nome e e-mail salvos. O papel não mudou: A instalação precisa de pelo menos um Admin ativo.'),
      ).toBeInTheDocument();
      expect(papel()).toHaveValue('operador');
    });
  });
});
