import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { theme } from '../../../theme';
import { authRuntime } from '../../../auth/session';
import { jsonResponse } from '../../../test/authFakes';
import {
  GOOGLE_URL,
  INVITATION_START_PATH,
  notFoundWith,
  PROVIDERS_PATH,
  providersResponse,
  REFUSAL_PATH,
  refusalRoute,
  startOk,
} from '../../../test/federatedFakes';
import { START_404_TITLES } from '../api/federatedApi';
import { LINK_TOKEN_KEYS } from '../linkToken';
import { ConvitePage } from './AccessLinkPage';

// O convite com o provedor e as recusas que voltam a ele (spec `panel-access-link-pages`; design.md
// da change telas-login-google, D3; quadro `AceitarConvite`, versão 1794626941-64ac).

const assign = vi.fn<(url: string) => void>();
const LOOKUP = '/auth/invitation';
const KEY = LINK_TOKEN_KEYS.convite;
const TOKEN = 'k7Qp2vX9';
const LINK = {
  nome: 'Marcos',
  email: 'marcos.lima@empresa.com',
  convidadoPor: 'Aldovrando Oliveira',
  expiraEm: '2026-10-26T00:29:00Z',
};
const FORMULARIO = 'Criar senha e entrar';
const PROVEDOR_INDISPONIVEL_TEXTO =
  'Não foi possível falar com o provedor. Tente de novo em instantes.';

type Handler = Response | Error | (() => Response | Promise<Response>);
interface Call {
  url: string;
  body: unknown;
  href: string;
}

function respondByPath(handlers: Record<string, Handler[] | (() => Response | Promise<Response>)>) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init?: RequestInit) => {
      calls.push({
        url: input,
        body: init?.body ? JSON.parse(String(init.body)) : undefined,
        href: lugar(),
      });
      // A fila é do caminho, sem a query: uma query a mais é o que o caso do token na URL precisa ver.
      const handler = handlers[input.split('?')[0]];
      const next = typeof handler === 'function' ? handler : handler?.shift();
      if (next === undefined) throw new Error(`pedido sem resposta: ${input}`);
      if (next instanceof Error) throw next;
      return typeof next === 'function' ? next() : next;
    }),
  );
  return calls;
}

/** Os endereços do router, na ordem, gravados pela sonda (como o `entrarStates` do AccessLinkPage.test). */
const enderecos: string[] = [];
const lugar = () => enderecos.at(-1) ?? '';
function LocationProbe() {
  const current = useLocation();
  enderecos.push(`${current.pathname}${current.search}${current.hash}`);
  return null;
}

/**
 * O /convite com o fragmento (o link aberto) ou com a chave já na aba (a volta do Google), e a query
 * da volta.
 */
function renderAt({
  fragment,
  search = '',
  strict = false,
}: { fragment?: string; search?: string; strict?: boolean } = {}) {
  window.history.replaceState(null, '', `/convite${search}${fragment ? `#${fragment}` : ''}`);
  const tree = (
    <MantineProvider theme={theme}>
      <MemoryRouter initialEntries={[`/convite${search}`]}>
        <Routes>
          <Route
            path="/convite"
            element={
              <>
                <ConvitePage />
                <LocationProbe />
              </>
            }
          />
        </Routes>
      </MemoryRouter>
    </MantineProvider>
  );
  return render(strict ? <StrictMode>{tree}</StrictMode> : tree);
}

const ok = () => jsonResponse(LINK, 200);

function corDe(alerta: HTMLElement): string | undefined {
  return ['red', 'yellow', 'blue', 'green'].find((cor) =>
    alerta.getAttribute('style')?.includes(`--mantine-color-${cor}`),
  );
}

function alertaCom(texto: string): HTMLElement {
  const alerta = screen.getAllByRole('alert').find((el) => el.textContent?.includes(texto));
  if (!alerta) throw new Error(`sem aviso com "${texto}"`);
  return alerta;
}

beforeEach(() => {
  assign.mockReset();
  vi.spyOn(authRuntime, 'assign').mockImplementation(assign);
  sessionStorage.clear();
  localStorage.clear();
  enderecos.length = 0;
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('o convite com o provedor', () => {
  it('a escolha com o Google: o cabeçalho sem o papel, os botões, a nota e "ou crie uma senha"', async () => {
    const calls = respondByPath({ [LOOKUP]: [ok()], [PROVIDERS_PATH]: [providersResponse()] });
    renderAt({ fragment: TOKEN });

    expect(await screen.findByRole('button', { name: 'Continuar com Google' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Olá, Marcos' })).toBeInTheDocument();
    expect(screen.getByText(/convidou você para o/)).toHaveTextContent(
      'Aldovrando Oliveira convidou você para o Buteco Agentes.',
    );
    expect(screen.getByText(/convidou você para o/).textContent).not.toContain('Crie sua senha');
    expect(screen.getByText('Entre com uma conta Google')).toBeInTheDocument();
    expect(screen.getByText(/A conta precisa ter o e-mail/)).toHaveTextContent(
      'A conta precisa ter o e-mail marcos.lima@empresa.com.',
    );
    expect(screen.getByText('ou crie uma senha')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
    expect(calls.map((c) => c.url)).toEqual([LOOKUP, PROVIDERS_PATH]);
  });

  it.each([
    ['vazia', () => providersResponse([])],
    ['falhando', () => jsonResponse({}, 500)],
  ])('com a lista %s, a página de hoje, sem aviso', async (_nome, resposta) => {
    respondByPath({ [LOOKUP]: [ok()], [PROVIDERS_PATH]: [resposta()] });
    renderAt({ fragment: TOKEN });

    expect(await screen.findByRole('button', { name: FORMULARIO })).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByText('Entre com uma conta Google')).not.toBeInTheDocument(),
    );
    expect(screen.queryByText('ou crie uma senha')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByText(/convidou você para o/)).toHaveTextContent(
      'Crie sua senha para entrar.',
    );
  });

  it.each([
    ['{}', {}],
    ['provedores que não é lista', { provedores: 'google' }],
    ['itens sem chave em texto', { provedores: [{ chave: 1 }] }],
  ])(
    'a lista fora do formato no convite (%s): a página de hoje, e o formulário funciona',
    async (_nome, corpo) => {
      const calls = respondByPath({
        [LOOKUP]: [ok()],
        [PROVIDERS_PATH]: [jsonResponse(corpo, 200)],
        '/auth/invitation/accept': [
          jsonResponse({ title: 'A senha precisa ter de 12 a 128 caracteres.' }, 400),
        ],
      });
      const user = userEvent.setup();
      renderAt({ fragment: TOKEN });

      await user.type(await screen.findByLabelText('Senha'), 'senha-de-doze');
      await user.type(screen.getByLabelText('Repita a senha'), 'senha-de-doze');
      await user.click(screen.getByRole('button', { name: FORMULARIO }));

      await waitFor(() => expect(calls.map((c) => c.url)).toContain('/auth/invitation/accept'));
      expect(screen.queryByText('Entre com uma conta Google')).not.toBeInTheDocument();
    },
  );

  it('redirecionando para o Google: o token no corpo, sem Cancelar, e a navegação', async () => {
    let responder: (r: Response) => void = () => {};
    const calls = respondByPath({
      [LOOKUP]: [ok()],
      [PROVIDERS_PATH]: [providersResponse()],
      [INVITATION_START_PATH]: [() => new Promise<Response>((resolve) => (responder = resolve))],
    });
    const user = userEvent.setup();
    renderAt({ fragment: TOKEN });

    await user.click(await screen.findByRole('button', { name: 'Continuar com Google' }));
    expect(await screen.findByText('Redirecionando para o Google…')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: FORMULARIO })).not.toBeInTheDocument();
    responder(startOk());

    await waitFor(() => expect(assign).toHaveBeenCalledWith(GOOGLE_URL));
    expect(calls.find((c) => c.url === INVITATION_START_PATH)?.body).toEqual({ token: TOKEN });
  });

  it.each([
    [
      'o provedor indisponível (503)',
      () => jsonResponse({}, 503),
      'red',
      'Não foi possível falar com o provedor. Tente de novo em instantes.',
    ],
    [
      'muitas tentativas (429)',
      () => jsonResponse({}, 429, { 'Retry-After': '1' }),
      'yellow',
      'Muitas tentativas ao mesmo tempo. Tente de novo em instantes.',
    ],
    [
      'a falha de rede',
      () => new TypeError('Failed to fetch'),
      'red',
      'Não foi possível falar com o servidor.',
    ],
    [
      'outra resposta (500)',
      () => jsonResponse({}, 500),
      'red',
      'Não foi possível entrar com o Google. Tente de novo.',
    ],
  ])(
    '%s no início do convite: o aviso, e a escolha de volta',
    async (_nome, resposta, cor, texto) => {
      respondByPath({
        [LOOKUP]: [ok()],
        [PROVIDERS_PATH]: [providersResponse()],
        [INVITATION_START_PATH]: [resposta() as Response],
      });
      const user = userEvent.setup();
      renderAt({ fragment: TOKEN });

      await user.click(await screen.findByRole('button', { name: 'Continuar com Google' }));
      expect(corDe(await waitFor(() => alertaCom(texto)))).toBe(cor);
      expect(screen.getByRole('button', { name: 'Continuar com Google' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
      expect(sessionStorage.getItem(KEY)).toBe(TOKEN);
    },
  );

  it('o provedor não configurado (404 com o title dele): o aviso, a lista relida, e o formulário continua', async () => {
    const calls = respondByPath({
      [LOOKUP]: [ok()],
      [PROVIDERS_PATH]: [providersResponse(), providersResponse([])],
      [INVITATION_START_PATH]: [notFoundWith(START_404_TITLES.providerNotConfigured)],
    });
    const user = userEvent.setup();
    renderAt({ fragment: TOKEN });

    await user.click(await screen.findByRole('button', { name: 'Continuar com Google' }));
    expect(corDe(await waitFor(() => alertaCom(START_404_TITLES.providerNotConfigured)))).toBe(
      'red',
    );
    await waitFor(() => expect(calls.filter((c) => c.url === PROVIDERS_PATH)).toHaveLength(2));
    await waitFor(() =>
      expect(screen.queryByText('Entre com uma conta Google')).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
  });

  it.each([
    ['sem corpo', () => notFoundWith()],
    ['com outro title', () => notFoundWith('Not Found')],
  ])(
    '404 sem o title conhecido no convite (%s): a falha, a chave continua, e a lista não é relida',
    async (_nome, resposta) => {
      const calls = respondByPath({
        [LOOKUP]: [ok()],
        [PROVIDERS_PATH]: [providersResponse()],
        [INVITATION_START_PATH]: [resposta()],
      });
      const user = userEvent.setup();
      renderAt({ fragment: TOKEN });

      await user.click(await screen.findByRole('button', { name: 'Continuar com Google' }));
      expect(
        corDe(
          await waitFor(() => alertaCom('Não foi possível entrar com o Google. Tente de novo.')),
        ),
      ).toBe('red');
      expect(sessionStorage.getItem(KEY)).toBe(TOKEN);
      expect(calls.filter((c) => c.url === PROVIDERS_PATH)).toHaveLength(1);
      expect(screen.queryByText('Este link não vale mais')).not.toBeInTheDocument();
    },
  );

  it('o link deixou de valer no início (404 com o title do link): o cartão, e a chave apagada', async () => {
    respondByPath({
      [LOOKUP]: [ok()],
      [PROVIDERS_PATH]: [providersResponse()],
      [INVITATION_START_PATH]: [notFoundWith(START_404_TITLES.invalidLink)],
    });
    const user = userEvent.setup();
    renderAt({ fragment: TOKEN });

    await user.click(await screen.findByRole('button', { name: 'Continuar com Google' }));
    expect(await screen.findByText('Este link não vale mais')).toBeInTheDocument();
    expect(sessionStorage.getItem(KEY)).toBeNull();
  });
});

describe('as recusas que voltam ao convite', () => {
  function voltaComToken(extra: Record<string, Handler[] | (() => Response)> = {}) {
    sessionStorage.setItem(KEY, TOKEN);
    return respondByPath({ [LOOKUP]: [ok()], [PROVIDERS_PATH]: [providersResponse()], ...extra });
  }

  it('outro e-mail: os dois e-mails, os botões, o formulário e o endereço limpo', async () => {
    voltaComToken({
      [REFUSAL_PATH]: refusalRoute({
        motivo: 'outro-email',
        provedor: 'corporativo',
        email: 'marcos.pessoal@gmail.com',
      }),
    });
    renderAt({ search: '?recusa=outro-email&provedor=corporativo' });

    const alerta = await waitFor(() =>
      alertaCom(
        'O Google confirmou marcos.pessoal@gmail.com, mas este convite é para marcos.lima@empresa.com. Entre com a conta certa ou crie uma senha abaixo.',
      ),
    );
    expect(alerta).toHaveTextContent('E-mail diferente do convite');
    expect(corDe(alerta)).toBe('red');
    expect(screen.getByRole('button', { name: 'Continuar com Google' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
    await waitFor(() => expect(lugar()).toBe('/convite'));
  });

  it.each([
    [
      'email-nao-verificado',
      'yellow',
      'E-mail não confirmado',
      'O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou crie uma senha abaixo.',
    ],
    [
      'em-uso',
      'red',
      'Conta Google já conectada',
      'Esta conta Google já está conectada a outro usuário do Buteco Agentes. Entre com outra conta ou crie uma senha abaixo.',
    ],
    [
      'dominio-nao-permitido',
      'red',
      'Domínio não permitido',
      'Esta conta Google não é do domínio permitido neste painel.',
    ],
  ])('%s no convite: o aviso %s e o formulário', async (motivo, cor, titulo, texto) => {
    const calls = voltaComToken();
    renderAt({ search: `?recusa=${motivo}&provedor=corporativo` });

    const alerta = await waitFor(() => alertaCom(texto));
    expect(alerta).toHaveTextContent(titulo);
    expect(corDe(alerta)).toBe(cor);
    expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
    expect(calls.map((c) => c.url)).not.toContain(REFUSAL_PATH);
  });

  it.each([['falha'], ['motivo-novo']])(
    '%s no convite: o aviso vermelho sem título e o formulário',
    async (motivo) => {
      voltaComToken();
      renderAt({ search: `?recusa=${motivo}&provedor=corporativo` });

      const alerta = await waitFor(() =>
        alertaCom('Não foi possível entrar com o Google. Tente de novo.'),
      );
      expect(alerta.textContent).toBe('Não foi possível entrar com o Google. Tente de novo.');
      expect(corDe(alerta)).toBe('red');
      expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
    },
  );

  it('convite que deixou de valer: o cartão de hoje, e a chave apagada', async () => {
    sessionStorage.setItem(KEY, TOKEN);
    respondByPath({ [LOOKUP]: [notFoundWith(START_404_TITLES.invalidLink)] });
    renderAt({ search: '?recusa=convite-invalido&provedor=corporativo' });

    expect(await screen.findByText('Este link não vale mais')).toBeInTheDocument();
    expect(sessionStorage.getItem(KEY)).toBeNull();
  });

  it.each([
    [
      'outro-email',
      'E-mail diferente do convite',
      'O Google confirmou outro e-mail, diferente do e-mail deste convite.',
    ],
    ['falha', undefined, 'Não foi possível entrar com o Google. Tente de novo.'],
  ])(
    '%s sem o token: nenhum pedido, "Convite", o aviso e "Abra o link…", sem formulário',
    async (motivo, titulo, texto) => {
      const calls = respondByPath({});
      renderAt({ search: `?recusa=${motivo}&provedor=corporativo` });

      expect(screen.getByRole('heading', { name: 'Convite' })).toBeInTheDocument();
      const alerta = alertaCom(texto);
      if (titulo) expect(alerta).toHaveTextContent(titulo);
      else expect(alerta.textContent).toBe(texto);
      expect(corDe(alerta)).toBe('red');
      expect(
        screen.getByText('Abra o link do convite que você recebeu de novo.'),
      ).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: FORMULARIO })).not.toBeInTheDocument();
      await waitFor(() => expect(lugar()).toBe('/convite'));
      expect(calls).toEqual([]);
    },
  );

  it('recarregar depois da recusa: nenhum pedido, e "Este link não vale mais"', async () => {
    voltaComToken();
    const primeira = renderAt({ search: '?recusa=em-uso&provedor=corporativo' });
    await waitFor(() => alertaCom('Conta Google já conectada'));
    await waitFor(() => expect(sessionStorage.getItem(KEY)).toBeNull());
    await waitFor(() => expect(lugar()).toBe('/convite'));
    primeira.unmount();

    const calls = respondByPath({});
    renderAt();

    expect(screen.getByText('Este link não vale mais')).toBeInTheDocument();
    expect(calls).toEqual([]);
  });

  it('uma leitura só do aviso no convite, no StrictMode', async () => {
    const calls = voltaComToken({
      [LOOKUP]: () => ok(),
      [PROVIDERS_PATH]: () => providersResponse(),
      [REFUSAL_PATH]: refusalRoute({
        motivo: 'outro-email',
        provedor: 'corporativo',
        email: 'marcos.pessoal@gmail.com',
      }),
    });
    renderAt({ search: '?recusa=outro-email&provedor=corporativo', strict: true });

    await waitFor(() => alertaCom('O Google confirmou marcos.pessoal@gmail.com'));
    expect(calls.filter((c) => c.url === REFUSAL_PATH)).toHaveLength(1);
  });

  it.each([
    [
      'o aviso de outra recusa não empresta o e-mail do provedor',
      { motivo: 'em-uso', provedor: 'corporativo' },
    ],
    [
      'o aviso de outro provedor não empresta o e-mail do provedor',
      { motivo: 'outro-email', provedor: 'outro' },
    ],
  ])('%s', async (_nome, gravada) => {
    const calls = voltaComToken({
      [REFUSAL_PATH]: refusalRoute({ ...gravada, email: 'marcos.pessoal@gmail.com' }),
    });
    renderAt({ search: '?recusa=outro-email&provedor=corporativo' });

    const alerta = await waitFor(() =>
      alertaCom('O Google confirmou outro e-mail, diferente do e-mail deste convite.'),
    );
    expect(alerta).toHaveTextContent('E-mail diferente do convite');
    expect(screen.queryByText(/marcos\.pessoal@gmail\.com/)).not.toBeInTheDocument();
    expect(calls.filter((c) => c.url === REFUSAL_PATH)).toHaveLength(1);
    expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
  });

  it('o novo clique troca o aviso da recusa pelo do início', async () => {
    const calls = voltaComToken({
      [REFUSAL_PATH]: refusalRoute({
        motivo: 'outro-email',
        provedor: 'corporativo',
        email: 'marcos.pessoal@gmail.com',
      }),
      [INVITATION_START_PATH]: [jsonResponse({}, 503)],
    });
    const user = userEvent.setup();
    renderAt({ search: '?recusa=outro-email&provedor=corporativo' });

    await waitFor(() => alertaCom('O Google confirmou marcos.pessoal@gmail.com'));
    await user.click(screen.getByRole('button', { name: 'Continuar com Google' }));

    await waitFor(() =>
      alertaCom('Não foi possível falar com o provedor. Tente de novo em instantes.'),
    );
    expect(screen.getAllByRole('alert')).toHaveLength(1);
    expect(screen.queryByText('E-mail diferente do convite')).not.toBeInTheDocument();
    expect(calls.filter((c) => c.url === REFUSAL_PATH)).toHaveLength(1);
    expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
  });

  it.each([
    [
      'outro-email sem o e-mail',
      'o 503',
      'outro-email',
      () => jsonResponse({}, 503),
      PROVEDOR_INDISPONIVEL_TEXTO,
    ],
    [
      'motivo desconhecido',
      'o 429',
      'motivo-novo',
      () => jsonResponse({}, 429, { 'Retry-After': '1' }),
      'Muitas tentativas ao mesmo tempo. Tente de novo em instantes.',
    ],
    [
      'motivo desconhecido',
      'a falha de rede',
      'motivo-novo',
      () => new TypeError('Failed to fetch'),
      'Não foi possível falar com o servidor.',
    ],
    [
      'em-uso',
      'o 404 do provedor',
      'em-uso',
      () => notFoundWith(START_404_TITLES.providerNotConfigured),
      START_404_TITLES.providerNotConfigured,
    ],
    [
      'em-uso',
      'o 404 sem title conhecido',
      'em-uso',
      () => notFoundWith(),
      'Não foi possível entrar com o Google. Tente de novo.',
    ],
    [
      'em-uso',
      'outra resposta (500)',
      'em-uso',
      () => jsonResponse({}, 500),
      'Não foi possível entrar com o Google. Tente de novo.',
    ],
  ])(
    'a recusa %s some com %s do início, e só o aviso dele fica',
    async (_nome, _resposta, motivo, resposta, texto) => {
      const calls = voltaComToken({
        [PROVIDERS_PATH]: [providersResponse(), providersResponse()],
        // Sem e-mail para ler: a rota do aviso responde 404 (o outro-email cai na variante sem e-mail).
        [REFUSAL_PATH]: [notFoundWith()],
        [INVITATION_START_PATH]: [resposta() as Response],
      });
      const user = userEvent.setup();
      renderAt({ search: `?recusa=${motivo}&provedor=corporativo` });

      const button = await screen.findByRole('button', { name: 'Continuar com Google' });
      expect(screen.getAllByRole('alert')).toHaveLength(1);
      await user.click(button);

      await waitFor(() => alertaCom(texto));
      expect(screen.getAllByRole('alert')).toHaveLength(1);
      expect(calls.filter((c) => c.url === REFUSAL_PATH)).toHaveLength(
        motivo === 'outro-email' ? 1 : 0,
      );
      expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
    },
  );
});

describe('o token do convite na aba', () => {
  it('a ida ao provedor não apaga o token', async () => {
    let chaveNaNavegacao: string | null = 'não navegou';
    assign.mockImplementation(() => {
      chaveNaNavegacao = sessionStorage.getItem(KEY);
    });
    respondByPath({
      [LOOKUP]: [ok()],
      [PROVIDERS_PATH]: [providersResponse()],
      [INVITATION_START_PATH]: [startOk()],
    });
    const user = userEvent.setup();
    renderAt({ fragment: TOKEN });

    await user.click(await screen.findByRole('button', { name: 'Continuar com Google' }));

    await waitFor(() => expect(assign).toHaveBeenCalled());
    expect(chaveNaNavegacao).toBe(TOKEN);
  });

  it('segunda tentativa depois de uma recusa: a chave regravada com o mesmo token na navegação', async () => {
    let chaveNaNavegacao: string | null = 'não navegou';
    assign.mockImplementation(() => {
      chaveNaNavegacao = sessionStorage.getItem(KEY);
    });
    sessionStorage.setItem(KEY, TOKEN);
    respondByPath({
      [LOOKUP]: [ok()],
      [PROVIDERS_PATH]: [providersResponse()],
      [REFUSAL_PATH]: refusalRoute({
        motivo: 'outro-email',
        provedor: 'corporativo',
        email: 'marcos.pessoal@gmail.com',
      }),
      [INVITATION_START_PATH]: [startOk()],
    });
    const user = userEvent.setup();
    renderAt({ search: '?recusa=outro-email&provedor=corporativo' });

    await waitFor(() => alertaCom('O Google confirmou marcos.pessoal@gmail.com'));
    await waitFor(() => expect(sessionStorage.getItem(KEY)).toBeNull());
    await user.click(screen.getByRole('button', { name: 'Continuar com Google' }));

    await waitFor(() => expect(assign).toHaveBeenCalled());
    expect(chaveNaNavegacao).toBe(TOKEN);
  });

  it('a recusa apaga o token, e o formulário continua', async () => {
    sessionStorage.setItem(KEY, TOKEN);
    respondByPath({ [LOOKUP]: [ok()], [PROVIDERS_PATH]: [providersResponse()] });
    renderAt({ search: '?recusa=em-uso&provedor=corporativo' });

    await waitFor(() => alertaCom('Conta Google já conectada'));
    expect(screen.getByRole('button', { name: FORMULARIO })).toBeInTheDocument();
    await waitFor(() => expect(sessionStorage.getItem(KEY)).toBeNull());
  });

  it('o token não vai para a URL nem para o log', async () => {
    const console_ = (['log', 'info', 'warn', 'error', 'debug'] as const).map((nivel) =>
      vi.spyOn(console, nivel).mockImplementation(() => {}),
    );
    const calls = respondByPath({
      [LOOKUP]: [ok(), ok()],
      [PROVIDERS_PATH]: () => providersResponse(),
      [INVITATION_START_PATH]: [startOk()],
    });
    const user = userEvent.setup();
    const primeira = renderAt({ fragment: TOKEN });
    await user.click(await screen.findByRole('button', { name: 'Continuar com Google' }));
    await waitFor(() => expect(assign).toHaveBeenCalled());
    primeira.unmount();
    // A volta de uma recusa, com a chave que o clique regravou.
    renderAt({ search: '?recusa=em-uso&provedor=corporativo' });
    await waitFor(() => alertaCom('Conta Google já conectada'));

    for (const call of calls) {
      expect(call.url).not.toContain(TOKEN);
      expect(call.href).not.toContain(TOKEN);
    }
    expect(lugar()).not.toContain(TOKEN);
    for (const spy of console_) {
      for (const args of spy.mock.calls) expect(JSON.stringify(args)).not.toContain(TOKEN);
    }
    expect(JSON.stringify({ ...localStorage })).not.toContain(TOKEN);
  });
});
