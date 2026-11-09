import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { ATIVO, CONVIDADA, LINK, fakeUsersApi } from '../test/fakeUsersApi';
import { renderUsers } from '../test/renderUsers';
import { formatLinkExpiry } from '../../auth/linkExpiry';

// O link do convite, uma vez e só na memória (requisito "Link de convite mostrado uma vez e só
// na memória da tela"; design.md D5). O token vai no fragmento do link, e nenhum pedaço dele
// pode ficar em storage, endereço, `history.state` ou nos caches do TanStack Query.

const TOKEN = LINK.split('#')[1];
const CRIADO = {
  usuario: { ...CONVIDADA, id: 'u-marcos', nome: 'Marcos Lima', email: 'marcos@exemplo.test' },
  link: LINK,
  expiraEm: '2026-11-10T21:29:00Z',
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

async function criarConvite() {
  const user = userEvent.setup();
  fakeUsersApi({
    'GET /users': { status: 200, body: [ATIVO, CONVIDADA] },
    'POST /users/invitations': { status: 201, body: CRIADO },
  });
  const rendered = renderUsers();
  await user.click(await screen.findByRole('button', { name: 'Convidar usuário' }));
  await user.type(screen.getByLabelText('Nome'), 'Marcos Lima');
  await user.type(screen.getByLabelText('E-mail'), 'marcos@exemplo.test');
  // O papel é obrigatório desde a #184 (change papeis-no-painel): mudança de arranjo.
  await user.selectOptions(screen.getByLabelText('Papel'), 'operador');
  await user.type(screen.getByLabelText('Sua senha'), 'minha-senha-certa');
  await user.click(screen.getByRole('button', { name: 'Gerar convite' }));
  await screen.findByText(LINK);
  return { user, ...rendered };
}

/** Tudo o que os dois caches guardam, serializado. */
function caches(queryClient: ReturnType<typeof renderUsers>['queryClient']): string {
  const queries = queryClient.getQueryCache().getAll().map((q) => q.state);
  const mutations = queryClient.getMutationCache().getAll().map((m) => m.state);
  return JSON.stringify({ queries, mutations });
}

describe('Link do convite', () => {
  it('mostra o link, a validade e o aviso de que não aparece de novo', async () => {
    await criarConvite();

    expect(screen.getByText('Link do convite')).toBeInTheDocument();
    const validade = screen.getByText(/Envie para a pessoa por e-mail/);
    expect(validade).toHaveTextContent(`Vale até ${formatLinkExpiry(CRIADO.expiraEm)} e funciona uma única vez.`);
    expect(screen.getByText(/O Buteco não mostra este link de novo/)).toBeInTheDocument();
  });

  it('Copiar link', async () => {
    const { user } = await criarConvite();

    await user.click(screen.getByRole('button', { name: 'Copiar link' }));

    // O `userEvent.setup()` instala a área de transferência falsa do próprio user-event.
    await expect(navigator.clipboard.readText()).resolves.toBe(LINK);
    expect(await screen.findByRole('button', { name: 'Copiado ✓' })).toBeInTheDocument();
  });

  it('Concluir apaga o link', async () => {
    const { user, container } = await criarConvite();

    await user.click(screen.getByRole('button', { name: 'Concluir' }));

    expect(container.innerHTML).not.toContain(TOKEN);
    expect(screen.queryByText('Link do convite')).not.toBeInTheDocument();
  });

  it('nenhum cache guarda o link', async () => {
    const { user, queryClient } = await criarConvite();
    await waitFor(() => expect(queryClient.isFetching()).toBe(0));

    expect(caches(queryClient)).not.toContain(TOKEN);

    await user.click(screen.getByRole('link', { name: 'sair' }));
    expect(await screen.findByText('fora de Usuários')).toBeInTheDocument();
    expect(caches(queryClient)).not.toContain(TOKEN);
  });

  it('nada em storage nem na URL', async () => {
    await criarConvite();

    // Pela API da Storage (`length` e `key`), e não por `Object.keys`: o `MemoryStorage` da
    // suíte não expõe as chaves como propriedades, e a lista sairia vazia.
    const storage = [window.sessionStorage, window.localStorage].flatMap((s) =>
      Array.from({ length: s.length }, (_, i) => s.key(i)!).map((k) => `${k}=${s.getItem(k)}`),
    );
    expect(storage.some((linha) => linha.startsWith('buteco.sessao='))).toBe(true);
    expect(storage.join('\n')).not.toContain(TOKEN);
    expect(window.location.href).not.toContain(TOKEN);
    expect(JSON.stringify(window.history.state)).not.toContain(TOKEN);
  });

  it('voltar à página não mostra o link de novo', async () => {
    const { user, container } = await criarConvite();

    await user.click(screen.getByRole('link', { name: 'sair' }));
    await user.click(await screen.findByRole('link', { name: 'voltar' }));

    expect(await screen.findByText('Ana Souza')).toBeInTheDocument();
    expect(container.innerHTML).not.toContain(TOKEN);
    expect(screen.queryByText('Link do convite')).not.toBeInTheDocument();
  });

  it('abrir outro painel descarta o link', async () => {
    const { user, container } = await criarConvite();

    await user.click(screen.getByRole('button', { name: 'Gerar novo link para Joana Prado' }));

    expect(screen.getByRole('form', { name: 'Gerar novo link para Joana Prado' })).toBeInTheDocument();
    expect(container.innerHTML).not.toContain(TOKEN);
  });
});
