import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { theme } from '../../../theme';
import { authRuntime } from '../../../auth/session';
import { PENDING_LOGIN_KEY } from '../../../auth/loginFlow';
import { jsonResponse } from '../../../test/authFakes';
import { PROVIDERS_PATH } from '../../../test/federatedFakes';
import { ConvitePage, RedefinirSenhaPage } from './AccessLinkPage';
import { EntrarPage } from './EntrarPage';
import { LINK_TOKEN_KEYS, type LinkPage } from '../linkToken';
import { formatLinkExpiry } from '../linkExpiry';

// As páginas do link de uso único (spec `panel-access-link-pages`). O endereço do jsdom
// faz o papel da barra do navegador: o fragmento é posto nele antes de renderizar, e a
// página o lê de `window.location`, como no navegador.

const assign = vi.fn<(url: string) => void>();

const ROUTES: Record<LinkPage, { lookup: string; complete: string }> = {
  convite: { lookup: '/auth/invitation', complete: '/auth/invitation/accept' },
  'redefinir-senha': { lookup: '/auth/password-reset', complete: '/auth/password-reset/complete' },
};

const LINK = {
  nome: 'Marcos Lima',
  email: 'marcos.lima@empresa.com',
  convidadoPor: 'Aldovrando Oliveira',
  expiraEm: '2026-10-26T00:29:00Z',
};

type Handler = Response | Error | (() => Response | Promise<Response>);

interface Call {
  url: string;
  body: unknown;
  /** O endereço da barra no instante do pedido. */
  href: string;
}

/** Cada caminho responde a fila dele, na ordem; um pedido sem resposta reprova o teste. */
function respondByPath(handlers: Record<string, Handler[]>) {
  const calls: Call[] = [];
  const fetchMock = vi.fn<(input: string, init?: RequestInit) => Promise<Response>>(async (input, init) => {
    calls.push({ url: input, body: init?.body ? JSON.parse(String(init.body)) : undefined, href: window.location.href });
    // A fila é do caminho, sem a query: uma query a mais no pedido é o que o caso do
    // token na URL precisa ver, e não um pedido sem resposta.
    // A lista dos provedores (o convite, depois da consulta válida, e o /entrar, na montagem; change
    // telas-login-google) responde vazia quando o caso não a pede: o bloco do provedor não aparece.
    if (input === PROVIDERS_PATH && !(PROVIDERS_PATH in handlers)) return jsonResponse({ provedores: [] }, 200);
    const next = handlers[input.split('?')[0]]?.shift();
    if (next === undefined) throw new Error(`pedido sem resposta: ${input}`);
    if (next instanceof Error) throw next;
    return typeof next === 'function' ? next() : next;
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

/** Os `state` com que o /entrar foi aberto, lidos do router (o MemoryRouter não usa o window.history). */
const entrarStates: unknown[] = [];

function EntrarStateProbe() {
  entrarStates.push(useLocation().state);
  return null;
}

function renderAt(page: LinkPage, fragment?: string, { strict = false } = {}) {
  window.history.replaceState(null, '', `/${page}${fragment ? `#${fragment}` : ''}`);
  const tree = (
    <MantineProvider theme={theme}>
      <MemoryRouter initialEntries={[`/${page}`]}>
        <Routes>
          <Route path="/convite" element={<ConvitePage />} />
          <Route path="/redefinir-senha" element={<RedefinirSenhaPage />} />
          <Route
            path="/entrar"
            element={
              <>
                <EntrarPage />
                <EntrarStateProbe />
              </>
            }
          />
        </Routes>
      </MemoryRouter>
    </MantineProvider>
  );
  return render(strict ? <StrictMode>{tree}</StrictMode> : tree);
}

const ok = (body: unknown = LINK) => jsonResponse(body, 200);
const noContent = () => new Response(null, { status: 204 });
// O login depois da senha criada: o convidado (e quem teve a verificação redefinida) cadastra o
// app autenticador no /entrar (change verificacao-2-etapas-obrigatoria, D7). O setup fica sem
// resposta: o caso só precisa da etapa aberta.
const enrollmentStep = () => jsonResponse({ segundoPasso: 'cadastro' }, 200);
const pendingSetup = () => new Promise<Response>(() => {});
const notFound = () =>
  jsonResponse(
    { title: 'Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.' },
    404,
  );

// O convite pede a lista dos provedores depois da consulta válida (change telas-login-google); a
// redefinição não. Os casos que conferem a lista exata de pedidos contam o da lista na posição dele.
const LISTA: Record<LinkPage, string[]> = { convite: [PROVIDERS_PATH], 'redefinir-senha': [] };

const SUBMIT: Record<LinkPage, string> = { convite: 'Criar senha e entrar', 'redefinir-senha': 'Salvar senha e entrar' };
const PASSWORD_LABEL: Record<LinkPage, string> = { convite: 'Senha', 'redefinir-senha': 'Nova senha' };
const INVALID: Record<LinkPage, string> = {
  convite:
    'O convite pode ter vencido, já ter sido usado ou ter sido cancelado. Peça um novo link a quem convidou você.',
  'redefinir-senha':
    'O link pode ter vencido, já ter sido usado, ou outro link pode ter sido gerado depois dele. Peça um novo a um administrador do painel.',
};

async function fillAndSubmit(
  user: ReturnType<typeof userEvent.setup>,
  page: LinkPage,
  senha = 'senha-de-doze',
  repeticao = senha,
) {
  await user.type(await screen.findByLabelText(PASSWORD_LABEL[page]), senha);
  await user.type(screen.getByLabelText('Repita a senha'), repeticao);
  await user.click(screen.getByRole('button', { name: SUBMIT[page] }));
}

beforeEach(() => {
  entrarStates.length = 0;
  assign.mockReset();
  vi.spyOn(authRuntime, 'assign').mockImplementation(assign);
  sessionStorage.clear();
  localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
  window.history.replaceState(null, '', '/');
});

describe('formatLinkExpiry', () => {
  it('formata como dd/mm/aaaa às hh:mm no fuso pedido', () => {
    expect(formatLinkExpiry('2026-10-26T00:29:00Z', 'America/Sao_Paulo')).toBe('25/10/2026 às 21:29');
  });
});

describe.each(['convite', 'redefinir-senha'] as const)('/%s', (page) => {
  describe('o token', () => {
    it('o endereço troca antes do primeiro pedido, e o token vai no corpo', async () => {
      const calls = respondByPath({ [ROUTES[page].lookup]: [ok()] });
      renderAt(page, 'k7Qp2vX9');

      await screen.findByLabelText(PASSWORD_LABEL[page]);
      expect(calls[0].href).toBe(`http://localhost:3000/${page}`);
      expect(calls[0].body).toEqual({ token: 'k7Qp2vX9' });
    });

    it('o token nunca vai numa URL', async () => {
      const calls = respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [noContent()],
        '/auth/session': [enrollmentStep()],
        '/auth/session/enrollment/setup': [pendingSetup],
      });
      const user = userEvent.setup();
      renderAt(page, 'k7Qp2vX9');

      await fillAndSubmit(user, page);

      await screen.findByText('Cadastre a verificação em 2 etapas');
      // A lista dos provedores: a do convite, depois da consulta, e a do /entrar, na montagem (o efeito
      // do cadastro, filho, roda antes do da página).
      const esperados = [
        ROUTES[page].lookup,
        ...LISTA[page],
        ROUTES[page].complete,
        '/auth/session',
        '/auth/session/enrollment/setup',
        PROVIDERS_PATH,
      ];
      await waitFor(() => expect(calls).toHaveLength(esperados.length));
      expect(calls.map((c) => c.url)).toEqual(esperados);
      for (const call of calls) expect(call.url).not.toContain('k7Qp2vX9');
      for (const call of calls) expect(call.href).not.toContain('k7Qp2vX9');
      expect(JSON.stringify(entrarStates)).not.toContain('k7Qp2vX9');
      expect(assign).not.toHaveBeenCalled();
    });

    it('recarregar reaproveita o token guardado', async () => {
      const calls = respondByPath({ [ROUTES[page].lookup]: [ok(), ok()] });
      renderAt(page, 'k7Qp2vX9');
      await screen.findByLabelText(PASSWORD_LABEL[page]);

      // A recarga não roda a limpeza do React: a segunda árvore nasce com a primeira montada.
      const segunda = renderAt(page);

      await within(segunda.container).findByLabelText(PASSWORD_LABEL[page]);
      // Cada árvore faz a consulta com o token e, no convite, pede a lista dos provedores, sem corpo.
      const umaArvore = [[ROUTES[page].lookup, { token: 'k7Qp2vX9' }], ...LISTA[page].map((url) => [url, undefined])];
      await waitFor(() => expect(calls.map((c) => [c.url, c.body])).toEqual([...umaArvore, ...umaArvore]));
    });

    it('sem token não há pedido, e a página mostra o link inválido', () => {
      const calls = respondByPath({});
      renderAt(page);

      expect(screen.getByText('Este link não vale mais')).toBeInTheDocument();
      expect(calls).toEqual([]);
    });

    it('o sucesso apaga o token', async () => {
      respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [noContent()],
        '/auth/session': [enrollmentStep()],
        '/auth/session/enrollment/setup': [pendingSetup],
      });
      const user = userEvent.setup();
      renderAt(page, 'k7Qp2vX9');

      await fillAndSubmit(user, page);

      await screen.findByText('Cadastre a verificação em 2 etapas');
      expect(sessionStorage.getItem(LINK_TOKEN_KEYS[page])).toBeNull();
    });

    it('o link inválido apaga o token', async () => {
      respondByPath({ [ROUTES[page].lookup]: [notFound()] });
      renderAt(page, 'k7Qp2vX9');

      await screen.findByText('Este link não vale mais');
      expect(sessionStorage.getItem(LINK_TOKEN_KEYS[page])).toBeNull();
    });

    it('sair da página apaga o token', async () => {
      respondByPath({ [ROUTES[page].lookup]: [ok()] });
      const { unmount } = renderAt(page, 'k7Qp2vX9');
      await screen.findByLabelText(PASSWORD_LABEL[page]);
      expect(sessionStorage.getItem(LINK_TOKEN_KEYS[page])).toBe('k7Qp2vX9');

      unmount();

      expect(sessionStorage.getItem(LINK_TOKEN_KEYS[page])).toBeNull();
    });

    it('o StrictMode não perde o token', async () => {
      const calls = respondByPath({ [ROUTES[page].lookup]: [ok(), ok()] });
      renderAt(page, 'k7Qp2vX9', { strict: true });

      await screen.findByLabelText(PASSWORD_LABEL[page]);
      // A lista exata de pedidos, na ordem (decisão do dono, 14/11/2026): a consulta com o token (duas
      // vezes, pelo efeito duplo do StrictMode na montagem, como já era) e, no convite, a lista dos
      // provedores, sem corpo; nenhum outro. Um pedido a mais reprova.
      await waitFor(() =>
        expect(calls.map((c) => [c.url, c.body])).toEqual([
          [ROUTES[page].lookup, { token: 'k7Qp2vX9' }],
          [ROUTES[page].lookup, { token: 'k7Qp2vX9' }],
          ...LISTA[page].map((url) => [url, undefined]),
        ]),
      );
      for (const call of calls) expect(call.url).not.toContain('k7Qp2vX9');
      expect(sessionStorage.getItem(LINK_TOKEN_KEYS[page])).toBe('k7Qp2vX9');
    });
  });

  describe('consulta', () => {
    it('carregando enquanto a consulta não responde', async () => {
      let responder: (r: Response) => void = () => {};
      respondByPath({ [ROUTES[page].lookup]: [() => new Promise<Response>((resolve) => (responder = resolve))] });
      renderAt(page, 'tok');

      expect(await screen.findByRole('status')).toHaveTextContent('Conferindo o link…');
      expect(screen.queryByLabelText(PASSWORD_LABEL[page])).not.toBeInTheDocument();
      await act(async () => responder(ok()));
    });

    it('link inválido mostra o texto da página, e não o title do servidor', async () => {
      respondByPath({ [ROUTES[page].lookup]: [notFound()] });
      renderAt(page, 'tok');

      expect(await screen.findByText('Este link não vale mais')).toBeInTheDocument();
      expect(screen.getByText(INVALID[page])).toBeInTheDocument();
      expect(screen.queryByText(/Se você já criou a senha/)).not.toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Ir para o login' })).toHaveAttribute('href', '/entrar');
    });

    it('link que vence entre a consulta e a conclusão', async () => {
      respondByPath({ [ROUTES[page].lookup]: [ok()], [ROUTES[page].complete]: [notFound()] });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      expect(await screen.findByText('Este link não vale mais')).toBeInTheDocument();
    });

    it('indisponível e tentar de novo', async () => {
      respondByPath({ [ROUTES[page].lookup]: [jsonResponse({}, 503, { 'Retry-After': '1' }), ok()] });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível abrir o link agora');
      await user.click(screen.getByRole('button', { name: 'Tentar de novo' }));

      expect(await screen.findByLabelText(PASSWORD_LABEL[page])).toBeInTheDocument();
    });

    it('sem conexão na consulta', async () => {
      respondByPath({ [ROUTES[page].lookup]: [new TypeError('Failed to fetch')] });
      renderAt(page, 'tok');

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'O painel está indisponível ou sem conexão. O link continua valendo: tente de novo em alguns segundos.',
      );
    });
  });

  describe('o formulário da senha nova', () => {
    it('o e-mail fica travado, para o gerenciador de senhas', async () => {
      respondByPath({ [ROUTES[page].lookup]: [ok()] });
      renderAt(page, 'tok');

      const email = await screen.findByLabelText('E-mail');
      expect(email).toHaveValue('marcos.lima@empresa.com');
      expect(email).toHaveAttribute('readonly');
      expect(email).toHaveAttribute('autocomplete', 'username');
      expect(screen.getByLabelText(PASSWORD_LABEL[page])).toHaveAttribute('autocomplete', 'new-password');
      expect(screen.getByLabelText('Repita a senha')).toHaveAttribute('autocomplete', 'new-password');
      expect(screen.getByText('De 12 a 128 caracteres.')).toBeInTheDocument();
    });

    it('senha curta não envia', async () => {
      const calls = respondByPath({ [ROUTES[page].lookup]: [ok()] });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page, 'onze-chars!');

      expect(await screen.findByText('A senha precisa ter de 12 a 128 caracteres.')).toBeInTheDocument();
      expect(screen.getByLabelText(PASSWORD_LABEL[page])).toHaveAttribute('aria-invalid', 'true');
      await waitFor(() => expect(calls.map((c) => c.url)).toEqual([ROUTES[page].lookup, ...LISTA[page]]));
    });

    it('senha longa demais não envia', async () => {
      const calls = respondByPath({ [ROUTES[page].lookup]: [ok()] });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      const longa = 'a'.repeat(129);
      await user.click(await screen.findByLabelText(PASSWORD_LABEL[page]));
      await user.paste(longa);
      await user.click(screen.getByLabelText('Repita a senha'));
      await user.paste(longa);
      await user.click(screen.getByRole('button', { name: SUBMIT[page] }));

      expect(await screen.findByText('A senha precisa ter de 12 a 128 caracteres.')).toBeInTheDocument();
      await waitFor(() => expect(calls.map((c) => c.url)).toEqual([ROUTES[page].lookup, ...LISTA[page]]));
    });

    it('espaços contam como caracteres', async () => {
      const calls = respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [noContent()],
        '/auth/session': [noContent()],
      });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page, '  abcdefghij');

      await waitFor(() => expect(calls[1 + LISTA[page].length]?.body).toEqual({ token: 'tok', senha: '  abcdefghij' }));
    });

    it('senhas diferentes não enviam', async () => {
      const calls = respondByPath({ [ROUTES[page].lookup]: [ok()] });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page, 'senha-de-doze', 'senha-de-onze');

      expect(await screen.findByText('As senhas não conferem.')).toBeInTheDocument();
      expect(screen.getByLabelText('Repita a senha')).toHaveAttribute('aria-invalid', 'true');
      await waitFor(() => expect(calls.map((c) => c.url)).toEqual([ROUTES[page].lookup, ...LISTA[page]]));
    });

    it('a política do servidor aparece no campo', async () => {
      respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [jsonResponse({ title: 'A senha precisa ter de 12 a 128 caracteres.' }, 400)],
      });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      expect(await screen.findByText('A senha precisa ter de 12 a 128 caracteres.')).toBeInTheDocument();
      expect(screen.getByLabelText(PASSWORD_LABEL[page])).toHaveAttribute('aria-invalid', 'true');
    });

    it('muitas tentativas na conclusão desabilita o botão até o Retry-After e mantém o que foi digitado', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [jsonResponse({}, 429, { 'Retry-After': '1' })],
      });
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      expect(await screen.findByRole('alert')).toHaveTextContent('Muitas tentativas ao mesmo tempo. Tente de novo em instantes.');
      expect(screen.getByRole('button', { name: SUBMIT[page] })).toBeDisabled();
      expect(screen.getByLabelText(PASSWORD_LABEL[page])).toHaveValue('senha-de-doze');
      await act(() => vi.advanceTimersByTimeAsync(1_100));
      expect(screen.getByRole('button', { name: SUBMIT[page] })).toBeEnabled();
    });

    it('painel indisponível na conclusão mantém o formulário', async () => {
      respondByPath({ [ROUTES[page].lookup]: [ok()], [ROUTES[page].complete]: [jsonResponse({}, 503)] });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Não foi possível salvar agora. O que você digitou continua aqui: tente de novo em alguns segundos.',
      );
      expect(screen.getByLabelText(PASSWORD_LABEL[page])).toHaveValue('senha-de-doze');
      expect(screen.getByLabelText('Repita a senha')).toHaveValue('senha-de-doze');
    });

    it('sem conexão na conclusão mantém o formulário', async () => {
      respondByPath({ [ROUTES[page].lookup]: [ok()], [ROUTES[page].complete]: [new TypeError('Failed to fetch')] });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Não foi possível salvar agora. O que você digitou continua aqui: tente de novo em alguns segundos.',
      );
      expect(screen.getByLabelText(PASSWORD_LABEL[page])).toHaveValue('senha-de-doze');
    });
  });

  describe('depois de criar a senha', () => {
    // Era "entra com o e-mail da consulta e segue o fluxo do emissor para a tela inicial": o login
    // não responde mais 204, e quem cria a senha sem a verificação cadastra o app no /entrar.
    it('entra com o e-mail da consulta e chega ao /entrar na etapa do cadastro, sem a senha no state', async () => {
      const calls = respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [noContent()],
        '/auth/session': [enrollmentStep()],
        '/auth/session/enrollment/setup': [pendingSetup],
      });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      expect(await screen.findByText('Cadastre a verificação em 2 etapas')).toBeInTheDocument();
      expect(screen.getByText('Senha salva. Falta só cadastrar o app autenticador para entrar.')).toBeInTheDocument();
      expect(calls[2 + LISTA[page].length]).toMatchObject({
        url: '/auth/session',
        body: { email: 'marcos.lima@empresa.com', senha: 'senha-de-doze' },
      });
      expect(assign).not.toHaveBeenCalled();
      expect(sessionStorage.getItem(PENDING_LOGIN_KEY)).toBeNull();
      expect(entrarStates.length).toBeGreaterThan(0);
      for (const state of entrarStates) expect(JSON.stringify(state ?? null)).not.toContain('senha-de-doze');
    });

    it('com as 2 etapas, chega ao /entrar no passo do código, sem a senha no state', async () => {
      respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [noContent()],
        '/auth/session': [jsonResponse({ segundoPasso: 'totp' }, 200)],
      });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      expect(await screen.findByLabelText('Código')).toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent(
        'Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.',
      );
      expect(assign).not.toHaveBeenCalled();
      expect(entrarStates.length).toBeGreaterThan(0);
      for (const state of entrarStates) expect(JSON.stringify(state ?? null)).not.toContain('senha-de-doze');
    });

    it('login recusado depois de criar a senha chega ao /entrar com o e-mail e o aviso', async () => {
      respondByPath({
        [ROUTES[page].lookup]: [ok()],
        [ROUTES[page].complete]: [noContent()],
        '/auth/session': [jsonResponse({ title: 'Acesso pausado…' }, 423, { 'Retry-After': '300' })],
      });
      const user = userEvent.setup();
      renderAt(page, 'tok');

      await fillAndSubmit(user, page);

      // Espera o que só existe no /entrar: a página do link também tem um campo "E-mail",
      // e esperá-lo acharia o da página do link antes da navegação.
      expect(
        await screen.findByText('Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por 5 minutos.'),
      ).toBeInTheDocument();
      expect(screen.queryByLabelText('Repita a senha')).not.toBeInTheDocument();
      expect(screen.getByLabelText('E-mail')).toHaveValue('marcos.lima@empresa.com');
    });
  });
});

describe('textos de cada página', () => {
  it('o convite mostra quem convidou, o e-mail travado e a validade', async () => {
    respondByPath({ '/auth/invitation': [ok()] });
    renderAt('convite', 'tok');

    expect(await screen.findByRole('heading', { name: 'Olá, Marcos Lima' })).toBeInTheDocument();
    expect(screen.getByText(/Aldovrando Oliveira convidou você para o/)).toHaveTextContent(
      'Aldovrando Oliveira convidou você para o Buteco Agentes. Crie sua senha para entrar.',
    );
    expect(
      screen.getByText(`Este convite vale até ${formatLinkExpiry(LINK.expiraEm)} e só pode ser usado uma vez.`),
    ).toBeInTheDocument();
  });

  it('a redefinição avisa que encerra as sessões e mantém as 2 etapas', async () => {
    respondByPath({ '/auth/password-reset': [ok({ nome: 'Rafael Duarte', email: 'r@x.test', expiraEm: LINK.expiraEm })] });
    renderAt('redefinir-senha', 'tok');

    expect(await screen.findByRole('heading', { name: 'Criar nova senha' })).toBeInTheDocument();
    expect(screen.getByText(/Olá, Rafael Duarte\./)).toHaveTextContent(
      'Olá, Rafael Duarte. Crie uma nova senha para entrar no Buteco Agentes.',
    );
    expect(
      screen.getByText(
        'Ao salvar, você sai de todos os navegadores em que estava conectado. Se a sua conta usa verificação em 2 etapas, o código continua sendo pedido.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Salvar senha e entrar' })).toBeInTheDocument();
    expect(
      screen.getByText(`Este link vale até ${formatLinkExpiry(LINK.expiraEm)} e só pode ser usado uma vez.`),
    ).toBeInTheDocument();
  });
});
