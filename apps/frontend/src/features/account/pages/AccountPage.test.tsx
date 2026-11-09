import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { ACCOUNT, defaultRoutes, fakeAccountApi } from '../test/fakeAccountApi';
import { renderAccount } from '../test/renderAccount';
import { AccountPage } from './AccountPage';

// Os requisitos "Página Minha conta em /account" e "Cartão Dados" (spec
// `panel-account-page`; design.md da change minha-conta-painel, D1 e D2).

beforeEach(() => {
  signInForTest();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const card = (title: string) => screen.getByRole('region', { name: title });

describe('AccountPage', () => {
  it('mostra o título, o subtítulo e os quatro cartões, na ordem', async () => {
    fakeAccountApi(defaultRoutes());
    renderAccount(<AccountPage />);

    expect(screen.getByRole('heading', { level: 2, name: 'Minha conta' })).toBeInTheDocument();
    expect(
      screen.getByText('Os seus dados, a sua senha, os códigos de recuperação e os navegadores em que você está conectado.'),
    ).toBeInTheDocument();
    expect(screen.getAllByRole('region').map((r) => within(r).getAllByRole('heading')[0].textContent)).toEqual([
      'Dados',
      'Senha',
      'Verificação em 2 etapas',
      'Sessões',
    ]);
  });

  it('o erro de um cartão não derruba os outros', async () => {
    fakeAccountApi({ ...defaultRoutes(), 'GET /account/sessions': 'rede' });
    renderAccount(<AccountPage />);

    expect(await within(card('Sessões')).findByRole('alert')).toHaveTextContent('Não foi possível falar com o servidor.');
    expect(within(card('Sessões')).getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument();
    expect(await within(card('Dados')).findByText('Ana Souza')).toBeInTheDocument();
    expect(await within(card('Verificação em 2 etapas')).findByText('Não configurada')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Minha conta' })).toBeInTheDocument();
  });

  it('"Tentar de novo" relê o cartão', async () => {
    const user = userEvent.setup();
    const api = fakeAccountApi({ ...defaultRoutes(), 'GET /account/sessions': ['rede', { status: 200, body: [] }] });
    renderAccount(<AccountPage />);

    await user.click(await within(card('Sessões')).findByRole('button', { name: 'Tentar de novo' }));

    await waitFor(() => expect(within(card('Sessões')).queryByRole('alert')).not.toBeInTheDocument());
    expect(api.calls('GET /account/sessions')).toHaveLength(2);
  });

  describe('cartão Dados', () => {
    it('mostra os dados da conta, do GET /account', async () => {
      const agora = new Date();
      fakeAccountApi({
        ...defaultRoutes(),
        'GET /account': {
          status: 200,
          body: { ...ACCOUNT, criadoEm: '2026-08-12T15:00:00Z', ultimoAcesso: new Date(agora.getTime() - 60_000).toISOString() },
        },
      });
      renderAccount(<AccountPage />);

      const dados = card('Dados');
      expect(await within(dados).findByText('Ana Souza')).toBeInTheDocument();
      expect(within(dados).getByText('ana@exemplo.test')).toBeInTheDocument();
      expect(within(dados).getByText('12/08/2026')).toBeInTheDocument();
      expect(within(dados).getByText(/^hoje, \d\d:\d\d$/)).toBeInTheDocument();
    });

    // Substitui o caso da #246 "use Usuários fica sem link até a tela existir": o link leva ao
    // detalhe do próprio, pelo id do GET /account (design.md da change editar-usuario-painel, D1).
    it('o link leva a /users/{id da conta}', async () => {
      fakeAccountApi(defaultRoutes());
      renderAccount(<AccountPage />);

      const link = await within(card('Dados')).findByRole('link', { name: 'Usuários' });
      expect(link).toHaveAttribute('href', '/users/u-1');
      expect(link.closest('p')).toHaveTextContent('Para mudar o nome ou o e-mail, use Usuários.');
    });
  });

  // Spec `panel-account-page` (change papeis-no-painel). O papel do cartão é o GRAVADO, do
  // GET /account; a sessão do setup é de Admin.
  describe('papel', () => {
    it('Os dados da conta, com o papel', async () => {
      fakeAccountApi({ ...defaultRoutes(), 'GET /account': { status: 200, body: { ...ACCOUNT, papel: 'operador' } } });
      renderAccount(<AccountPage />);

      const dados = card('Dados');
      expect(await within(dados).findByText('Papel')).toBeInTheDocument();
      expect(within(dados).getByText('Operador')).toBeInTheDocument();
    });

    it.each(['operador', 'leitor', 'superusuario'])('Quem não é Admin pede a um Admin (%s)', async (papel) => {
      fakeAccountApi({ ...defaultRoutes(), 'GET /account': { status: 200, body: { ...ACCOUNT, papel } } });
      renderAccount(<AccountPage />);

      expect(
        await within(card('Dados')).findByText('Para mudar o nome ou o e-mail, peça a um Admin do painel.'),
      ).toBeInTheDocument();
      expect(within(card('Dados')).queryByRole('link', { name: 'Usuários' })).not.toBeInTheDocument();
    });

    it('O papel gravado vale sobre o do token', async () => {
      // O id token da sessão ainda é de Admin (setup); o servidor já diz Operador.
      fakeAccountApi({ ...defaultRoutes(), 'GET /account': { status: 200, body: { ...ACCOUNT, papel: 'operador' } } });
      renderAccount(<AccountPage />);

      const dados = card('Dados');
      expect(await within(dados).findByText('Operador')).toBeInTheDocument();
      expect(within(dados).getByText('Para mudar o nome ou o e-mail, peça a um Admin do painel.')).toBeInTheDocument();
    });

    it('Chegada depois do rebaixamento', async () => {
      fakeAccountApi({ ...defaultRoutes(), 'GET /account': { status: 200, body: { ...ACCOUNT, papel: 'operador' } } });
      renderAccount(<AccountPage />, '/account', { papelNovo: 'operador' });

      expect(screen.getByText('O seu papel agora é Operador.')).toBeInTheDocument();
    });

    it('Entrada comum sem aviso', async () => {
      fakeAccountApi(defaultRoutes());
      renderAccount(<AccountPage />);

      await within(card('Dados')).findByText('Ana Souza');
      expect(screen.queryByText(/O seu papel agora é/)).not.toBeInTheDocument();
    });

    it('o estado com Admin não mostra aviso', async () => {
      fakeAccountApi(defaultRoutes());
      renderAccount(<AccountPage />, '/account', { papelNovo: 'admin' });

      await within(card('Dados')).findByText('Ana Souza');
      expect(screen.queryByText(/O seu papel agora é/)).not.toBeInTheDocument();
    });
  });
});
