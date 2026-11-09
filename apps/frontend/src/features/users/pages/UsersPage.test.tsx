import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest, tokenResponse } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { formatRelative, formatShortDateTime } from '../../../utils/panelDates';
import { ATIVO, CONVIDADA, DESATIVADO, VENCIDO, fakeUsersApi, problem } from '../test/fakeUsersApi';
import { renderUsers } from '../test/renderUsers';

beforeEach(() => {
  signInForTest();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const linha = (nome: string) => screen.getByText(nome).closest('tr') as HTMLElement;

describe('UsersPage', () => {
  it('título, subtítulo e os dois cartões de texto', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] } });
    renderUsers();

    expect(screen.getByRole('heading', { name: 'Usuários', level: 2 })).toBeInTheDocument();
    expect(screen.getByText('Quem pode entrar no painel. O acesso é só por convite.')).toBeInTheDocument();
    expect(screen.getByText('Acesso só por convite')).toBeInTheDocument();
    expect(screen.getByText('Senha e recuperação')).toBeInTheDocument();
    // O texto do `m2` (change papeis-no-painel; caso que muda de asserção, listado no tasks.md).
    expect(
      screen.getByText(
        'Esqueceu a senha: um Admin gera um link de redefinição em Editar usuário. A verificação em 2 etapas é obrigatória para todos os papéis: cada pessoa cadastra o app autenticador no primeiro login.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/ligada por cada pessoa/)).not.toBeInTheDocument();
    expect(await screen.findByText('Ana Souza')).toBeInTheDocument();
  });

  // Spec `panel-users-page`, "Colunas da lista de Usuários" e "Página Usuários em /users"
  // (change papeis-no-painel).
  it('as colunas, com Papel entre Formas de entrada e Verificação em 2 etapas', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] } });
    renderUsers();

    await screen.findByText('Ana Souza');
    expect(screen.getAllByRole('columnheader').map((th) => th.textContent)).toEqual([
      'Usuário',
      'Formas de entrada',
      'Papel',
      'Verificação em 2 etapas',
      'Estado',
      'Último acesso',
      'Ações',
    ]);
  });

  it('o papel na linha', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [{ ...ATIVO, papel: 'operador' }] } });
    renderUsers();

    const tr = (await screen.findByText('Ana Souza')).closest('tr') as HTMLElement;
    expect(within(tr).getByText('Operador')).toBeInTheDocument();
  });

  it('O papel do convidado', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [CONVIDADA] } });
    renderUsers();

    const tr = (await screen.findByText('Joana Prado')).closest('tr') as HTMLElement;
    expect(within(tr).getByText('Leitor')).toBeInTheDocument();
  });

  it('Papel desconhecido sai neutro', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [{ ...ATIVO, papel: 'superusuario' }] } });
    renderUsers();

    const tr = (await screen.findByText('Ana Souza')).closest('tr') as HTMLElement;
    const celula = within(tr).getAllByRole('cell')[2];
    expect(celula).toHaveTextContent('—');
    expect(celula.textContent).not.toMatch(/Admin|Operador|Leitor/);
  });

  it('Admin rebaixado recebe 403', async () => {
    // O 403 renova a sessão uma vez (spec `panel-issuer-session`); o emissor responde.
    const api = fakeUsersApi({ 'GET /users': { status: 403 }, 'POST /connect/token': tokenResponse(2) });

    renderUsers();

    expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
    expect(screen.queryByText('Não foi possível carregar os usuários agora.')).not.toBeInTheDocument();
    expect(api.calls('POST /connect/token')).toHaveLength(1);
    expect(api.calls('GET /users')).toHaveLength(1);
  });

  it('carregando', () => {
    fakeUsersApi({ 'GET /users': () => new Promise(() => {}) });
    renderUsers();

    expect(screen.getByRole('status')).toHaveTextContent('Carregando os usuários…');
  });

  it('erro de carga com Tentar de novo', async () => {
    const user = userEvent.setup();
    const api = fakeUsersApi({ 'GET /users': ['rede', { status: 200, body: [ATIVO] }] });
    renderUsers();

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('Não foi possível carregar os usuários agora.');
    await user.click(within(alerta).getByRole('button', { name: 'Tentar de novo' }));

    expect(await screen.findByText('Ana Souza')).toBeInTheDocument();
    expect(api.calls('GET /users')).toHaveLength(2);
  });

  it('usuário ativo com a verificação ligada', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO] } });
    renderUsers();

    const tr = (await screen.findByText('Ana Souza')).closest('tr') as HTMLElement;
    expect(within(tr).getByText('ana@exemplo.test')).toBeInTheDocument();
    expect(within(tr).getByText('Senha')).toBeInTheDocument();
    expect(within(tr).getByText('Ativa')).toBeInTheDocument();
    expect(within(tr).getByText('Ativo')).toBeInTheDocument();
    // O formato de Minha conta ("hoje, HH:MM", "ontem, HH:MM", dd/mm/aaaa, HH:MM), e não "há N dias" (Q3).
    expect(within(tr).getByText(formatRelative(ATIVO.ultimoAcesso!))).toBeInTheDocument();
    expect(within(tr).getByText(/^(hoje|ontem|\d\d\/\d\d\/\d{4}), \d\d:\d\d$/)).toBeInTheDocument();
  });

  it('convite pendente', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [CONVIDADA] } });
    renderUsers();

    const tr = (await screen.findByText('Joana Prado')).closest('tr') as HTMLElement;
    const celulas = within(tr).getAllByRole('cell');
    // Os índices passam uma célula adiante com a coluna Papel (change papeis-no-painel): as
    // mesmas asserções, sobre as mesmas colunas (formas, verificação, último acesso).
    expect(celulas[1]).toHaveTextContent('—');
    expect(celulas[3]).toHaveTextContent('—');
    expect(within(tr).getByText('Convite pendente')).toBeInTheDocument();
    expect(within(tr).getByText(`vence em ${formatShortDateTime(CONVIDADA.convite!.expiraEm)}`)).toBeInTheDocument();
    expect(celulas[5]).toHaveTextContent('—');
  });

  it('o vencido vem do servidor', async () => {
    const passado = new Date(Date.now() - 60 * 60 * 1000).toISOString();
    fakeUsersApi({ 'GET /users': { status: 200, body: [{ ...CONVIDADA, convite: { expiraEm: passado, vencido: false } }] } });
    renderUsers();

    const tr = (await screen.findByText('Joana Prado')).closest('tr') as HTMLElement;
    expect(within(tr).getByText('Convite pendente')).toBeInTheDocument();
    expect(within(tr).queryByText('Convite vencido')).not.toBeInTheDocument();
  });

  it('convite vencido', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [VENCIDO] } });
    renderUsers();

    const tr = (await screen.findByText('Paulo Souza')).closest('tr') as HTMLElement;
    expect(within(tr).getByText('Convite vencido').closest('[data-tom]')).toHaveAttribute('data-tom', 'gray');
    expect(within(tr).getByText(`venceu em ${formatShortDateTime(VENCIDO.convite!.expiraEm)}`)).toBeInTheDocument();
  });

  it('estado desconhecido sai neutro', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [{ ...ATIVO, estado: 'suspenso' }] } });
    renderUsers();

    await screen.findByText('Ana Souza');
    expect(screen.getByText('suspenso').closest('[data-tom]')).toHaveAttribute('data-tom', 'neutral');
    expect(screen.queryByText('Ativo')).not.toBeInTheDocument();
  });

  it('a ordem é a da resposta', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [DESATIVADO, ATIVO] } });
    renderUsers();

    await screen.findByText('Ana Souza');
    const nomes = screen.getAllByRole('row').slice(1).map((tr) => within(tr).getAllByRole('cell')[0].textContent);
    expect(nomes[0]).toContain('Bruno Teixeira');
    expect(nomes[1]).toContain('Ana Souza');
  });

  it('convidado tem as duas ações', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [CONVIDADA] } });
    renderUsers();

    await screen.findByText('Joana Prado');
    expect(screen.getByRole('button', { name: 'Gerar novo link para Joana Prado' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar convite de Joana Prado' })).toBeInTheDocument();
  });

  // O "Editar" do quadro (design.md da change editar-usuario-painel, D1): substitui o caso da
  // #250 "a lista não tem link para o detalhe", porque `/users/:id` passou a existir.
  it('a linha do ativo e a do desativado têm Editar para /users/{id}', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO, DESATIVADO, CONVIDADA] } });
    renderUsers();

    await screen.findByText('Ana Souza');
    expect(within(linha('Ana Souza')).getByRole('link', { name: 'Editar Ana Souza' })).toHaveAttribute(
      'href',
      '/users/u-ana',
    );
    expect(within(linha('Bruno Teixeira')).getByRole('link', { name: 'Editar Bruno Teixeira' })).toHaveAttribute(
      'href',
      '/users/u-bruno',
    );
  });

  it('a linha do convidado não tem Editar', async () => {
    fakeUsersApi({ 'GET /users': { status: 200, body: [ATIVO, CONVIDADA, VENCIDO] } });
    renderUsers();

    await screen.findByText('Joana Prado');
    for (const nome of ['Joana Prado', 'Paulo Souza']) {
      const tr = linha(nome);
      expect(within(tr).queryByRole('link', { name: /Editar/ })).not.toBeInTheDocument();
      const hrefs = Array.from(tr.querySelectorAll('[href]')).map((a) => a.getAttribute('href'));
      expect(hrefs.filter((h) => h?.startsWith('/users/'))).toEqual([]);
    }
  });

  describe('cancelar convite', () => {
    it('o primeiro clique só pede confirmação', async () => {
      const user = userEvent.setup();
      const api = fakeUsersApi({ 'GET /users': { status: 200, body: [CONVIDADA] } });
      renderUsers();

      await user.click(await screen.findByRole('button', { name: 'Cancelar convite de Joana Prado' }));

      expect(within(linha('Joana Prado')).getByText('Cancelar o convite? O link deixa de funcionar.')).toBeInTheDocument();
      expect(api.calls('POST /users/u-joana/invitation/cancel')).toHaveLength(0);
      expect(screen.getByRole('button', { name: 'Convidar usuário' })).toBeDisabled();
    });

    it('Voltar desfaz a confirmação sem chamar a rota', async () => {
      const user = userEvent.setup();
      const api = fakeUsersApi({ 'GET /users': { status: 200, body: [CONVIDADA] } });
      renderUsers();

      await user.click(await screen.findByRole('button', { name: 'Cancelar convite de Joana Prado' }));
      await user.click(screen.getByRole('button', { name: 'Voltar' }));

      expect(screen.queryByText('Cancelar o convite? O link deixa de funcionar.')).not.toBeInTheDocument();
      expect(api.calls('POST /users/u-joana/invitation/cancel')).toHaveLength(0);
    });

    it('convite cancelado', async () => {
      const user = userEvent.setup();
      const api = fakeUsersApi({
        'GET /users': [{ status: 200, body: [ATIVO, CONVIDADA] }, { status: 200, body: [ATIVO] }],
        'POST /users/u-joana/invitation/cancel': { status: 204 },
      });
      renderUsers();

      await user.click(await screen.findByRole('button', { name: 'Cancelar convite de Joana Prado' }));
      await user.click(within(linha('Joana Prado')).getByRole('button', { name: 'Cancelar convite' }));

      await waitFor(() => expect(screen.queryByText('Joana Prado')).not.toBeInTheDocument());
      expect(api.calls('POST /users/u-joana/invitation/cancel')).toHaveLength(1);
      expect(api.calls('GET /users')).toHaveLength(2);
    });

    it('convite já aceito', async () => {
      const user = userEvent.setup();
      const api = fakeUsersApi({
        'GET /users': [{ status: 200, body: [CONVIDADA] }, { status: 200, body: [{ ...CONVIDADA, estado: 'ativo', convite: null }] }],
        'POST /users/u-joana/invitation/cancel': problem(409, 'O convite já foi aceito.'),
      });
      renderUsers();

      await user.click(await screen.findByRole('button', { name: 'Cancelar convite de Joana Prado' }));
      await user.click(within(linha('Joana Prado')).getByRole('button', { name: 'Cancelar convite' }));

      expect(
        (await screen.findByText('O convite de Joana Prado já foi aceito, então não há o que cancelar. A lista foi atualizada.')).closest(
          '[role="status"]',
        ),
      ).not.toBeNull();
      await waitFor(() => expect(api.calls('GET /users')).toHaveLength(2));
      expect(await screen.findByText('Ativo')).toBeInTheDocument();
    });

    it('o 404 relê a lista sem aviso', async () => {
      const user = userEvent.setup();
      const api = fakeUsersApi({
        'GET /users': [{ status: 200, body: [CONVIDADA] }, { status: 200, body: [] }],
        'POST /users/u-joana/invitation/cancel': problem(404, 'Usuário não encontrado.'),
      });
      renderUsers();

      await user.click(await screen.findByRole('button', { name: 'Cancelar convite de Joana Prado' }));
      await user.click(within(linha('Joana Prado')).getByRole('button', { name: 'Cancelar convite' }));

      await waitFor(() => expect(api.calls('GET /users')).toHaveLength(2));
      expect(screen.queryByText(/já foi aceito/)).not.toBeInTheDocument();
    });

    it('falha passageira fica na linha, com a confirmação aberta', async () => {
      const user = userEvent.setup();
      fakeUsersApi({
        'GET /users': { status: 200, body: [CONVIDADA] },
        'POST /users/u-joana/invitation/cancel': 'rede',
      });
      renderUsers();

      await user.click(await screen.findByRole('button', { name: 'Cancelar convite de Joana Prado' }));
      await user.click(within(linha('Joana Prado')).getByRole('button', { name: 'Cancelar convite' }));

      expect(await within(linha('Joana Prado')).findByRole('alert')).toHaveTextContent('Não foi possível falar com o servidor.');
      expect(within(linha('Joana Prado')).getByText('Cancelar o convite? O link deixa de funcionar.')).toBeInTheDocument();
    });
  });
});
