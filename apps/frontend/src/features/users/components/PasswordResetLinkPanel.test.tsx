import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { formatLinkExpiry } from '../../auth/linkExpiry';
import { BRUNO, PROPRIO_ID, RAFAEL, RESET_LINK, fakeUsersApi } from '../test/fakeUsersApi';
import { renderUserDetail, signInAs } from '../test/renderUserDetail';

// O link de redefinição, uma vez e só na memória (requisito "Link de redefinição mostrado uma
// vez"; design.md da change editar-usuario-painel, D7; decisões do dono Q5 e Q11).

const TOKEN = RESET_LINK.split('#')[1];
const EXPIRA = '2026-11-08T21:30:00Z';

beforeEach(() => {
  signInAs(PROPRIO_ID);
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function gerarLink() {
  const user = userEvent.setup();
  const api = fakeUsersApi({
    'GET /users/u-rafael': { status: 200, body: RAFAEL },
    'GET /users/u-bruno': { status: 200, body: BRUNO },
    'POST /users/u-rafael/password-reset': { status: 200, body: { link: RESET_LINK, expiraEm: EXPIRA } },
  });
  const rendered = renderUserDetail('u-rafael');
  await user.click(await screen.findByRole('button', { name: 'Redefinir senha' }));
  await user.type(screen.getByLabelText('Sua senha'), 'minha-senha-certa');
  await user.click(screen.getByRole('button', { name: 'Gerar link' }));
  await screen.findByText(RESET_LINK);
  return { user, api, ...rendered };
}

function caches(queryClient: ReturnType<typeof renderUserDetail>['queryClient']): string {
  const queries = queryClient.getQueryCache().getAll().map((q) => q.state);
  const mutations = queryClient.getMutationCache().getAll().map((m) => m.state);
  return JSON.stringify({ queries, mutations });
}

describe('Link de redefinição', () => {
  it('o painel do link, com a forma do quadro', async () => {
    const { api } = await gerarLink();

    const painel = screen.getByRole('status', { name: 'Link para redefinir a senha' });
    expect(within(painel).getByText(RESET_LINK)).toBeInTheDocument();
    expect(within(painel).getByRole('button', { name: 'Copiar link' })).toBeInTheDocument();
    expect(painel).toHaveTextContent(
      `Envie o link à pessoa pelo canal que preferir. Vale até ${formatLinkExpiry(EXPIRA)} e funciona uma única vez. O Buteco não mostra este link de novo; se ele se perder, gere outro, e o anterior deixa de funcionar.`,
    );
    expect(formatLinkExpiry(EXPIRA, 'America/Sao_Paulo')).toBe('08/11/2026 às 18:30');
    // Sem o e-mail, o ícone e o aviso amarelo do painel do convite (Q11).
    expect(within(painel).queryByText('rafael.duarte@empresa.com')).not.toBeInTheDocument();
    expect(within(painel).queryByText(/Gerar novo link/)).not.toBeInTheDocument();
    // O detalhe é relido depois do 200.
    await waitFor(() => expect(api.calls('GET /users/u-rafael')).toHaveLength(2));
  });

  it('Copiar link', async () => {
    const { user } = await gerarLink();

    await user.click(screen.getByRole('button', { name: 'Copiar link' }));

    await expect(navigator.clipboard.readText()).resolves.toBe(RESET_LINK);
    expect(await screen.findByRole('button', { name: 'Copiado ✓' })).toBeInTheDocument();
  });

  it('Concluir tira o link', async () => {
    const { user, container } = await gerarLink();

    await user.click(screen.getByRole('button', { name: 'Concluir' }));

    expect(screen.queryByText('Link para redefinir a senha')).not.toBeInTheDocument();
    expect(container.innerHTML).not.toContain(TOKEN);
  });

  it('abrir outra ação tira o link', async () => {
    const { user, container } = await gerarLink();

    await user.click(screen.getByRole('button', { name: 'Encerrar todas as sessões' }));

    expect(screen.getByText(/Encerrar as 2 sessões de Rafael Duarte\?/)).toBeInTheDocument();
    expect(screen.queryByText('Link para redefinir a senha')).not.toBeInTheDocument();
    expect(container.innerHTML).not.toContain(TOKEN);
  });

  it('o link não fica no MutationCache', async () => {
    const { user, queryClient } = await gerarLink();
    await waitFor(() => expect(queryClient.isFetching()).toBe(0));

    expect(queryClient.getMutationCache().getAll()).toHaveLength(0);
    expect(caches(queryClient)).not.toContain(TOKEN);

    await user.click(screen.getByRole('link', { name: 'sair' }));
    expect(await screen.findByText('fora de Usuários')).toBeInTheDocument();
    expect(caches(queryClient)).not.toContain(TOKEN);
  });

  it('o link não vai a storage', async () => {
    const { router } = await gerarLink();

    // Pela API da Storage (`length` e `key`), e não por `Object.keys`, que o `MemoryStorage` da
    // suíte não expõe: a premissa que caiu na #250.
    const storage = [window.sessionStorage, window.localStorage].flatMap((s) =>
      Array.from({ length: s.length }, (_, i) => s.key(i)!).map((k) => `${k}=${s.getItem(k)}`),
    );
    expect(storage.some((linha) => linha.startsWith('buteco.sessao='))).toBe(true);
    expect(storage.join('\n')).not.toContain(TOKEN);
    expect(window.location.href).not.toContain(TOKEN);
    expect(JSON.stringify(window.history.state)).not.toContain(TOKEN);
    expect(JSON.stringify(router.state.location)).not.toContain(TOKEN);
  });

  it('o link não passa para a página de outro usuário', async () => {
    // O router reaproveita a página quando só o `:id` muda; sem remontar, a ação aberta (com o
    // link) de uma pessoa apareceria no detalhe de outra (achado da conferência, tarefa 11.3).
    const { router, container } = await gerarLink();

    await router.navigate('/users/u-bruno');

    expect(await screen.findByRole('heading', { name: 'Bruno Teixeira', level: 2 })).toBeInTheDocument();
    expect(screen.queryByText('Link para redefinir a senha')).not.toBeInTheDocument();
    expect(container.innerHTML).not.toContain(TOKEN);
  });
});
