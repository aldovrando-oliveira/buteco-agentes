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
  GOOGLE,
  GOOGLE_URL,
  LOGIN_START_PATH,
  notFoundWith,
  PROVIDERS_PATH,
  providersResponse,
  REFUSAL_PATH,
  refusalRoute,
  startOk,
} from '../../../test/federatedFakes';
import { START_404_TITLES } from '../api/federatedApi';
import { LINK_TOKEN_KEYS } from '../linkToken';
import { EntrarPage } from './EntrarPage';

// O login pelo provedor no /entrar (spec `panel-federated-login`; design.md da change
// telas-login-google, D1 e D2; quadros `Main` e `LoginRecusado`, versão 1794626941-64ac).

const assign = vi.fn<(url: string) => void>();
const AUTHORIZE = '/connect/authorize?client_id=buteco-painel&response_type=code&state=s';

type Handler = Response | Error | (() => Response | Promise<Response>);
interface Call {
  url: string;
  body: unknown;
}

/** Cada caminho responde a fila dele; uma função responde sempre. Pedido sem resposta reprova. */
function respondByPath(handlers: Record<string, Handler[] | (() => Response | Promise<Response>)>) {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init?: RequestInit) => {
      calls.push({ url: input, body: init?.body ? JSON.parse(String(init.body)) : undefined });
      const handler = handlers[input];
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
  enderecos.push(`${current.pathname}${current.search}`);
  return null;
}

function renderAt(path: string, { strict = false } = {}) {
  const [pathname, search = ''] = path.split('?');
  const tree = (
    <MantineProvider theme={theme}>
      <MemoryRouter initialEntries={[{ pathname, search: search ? `?${search}` : '' }]}>
        <Routes>
          <Route
            path="/entrar"
            element={
              <>
                <EntrarPage />
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

/** A cor do aviso do Mantine, lida da variável de cor do componente. */
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
});

describe('os botões dos provedores no /entrar', () => {
  it('com o Google configurado, "ou entre com" e "Entrar com Google" abaixo do Entrar', async () => {
    respondByPath({ [PROVIDERS_PATH]: [providersResponse()] });
    renderAt('/entrar');

    const botao = await screen.findByRole('button', { name: 'Entrar com Google' });
    expect(screen.getByText('ou entre com')).toBeInTheDocument();
    const entrar = screen.getByRole('button', { name: 'Entrar' });
    expect(entrar.compareDocumentPosition(botao) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('sem provedor configurado, nem separador nem botão', async () => {
    const calls = respondByPath({ [PROVIDERS_PATH]: [providersResponse([])] });
    renderAt('/entrar');

    await waitFor(() => expect(calls.map((c) => c.url)).toEqual([PROVIDERS_PATH]));
    expect(screen.queryByText('ou entre com')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Google/ })).not.toBeInTheDocument();
  });

  it('o tipo desconhecido não ganha botão', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse([GOOGLE, { chave: 'sso', tipo: 'keycloak' }])],
    });
    renderAt('/entrar');

    await screen.findByRole('button', { name: 'Entrar com Google' });
    expect(screen.getAllByRole('button', { name: /^Entrar com/ })).toHaveLength(1);
  });

  it.each([
    ['500', () => jsonResponse({}, 500)],
    ['a rede', () => new TypeError('Failed to fetch')],
  ])('a lista falhando (%s) esconde o bloco sem aviso', async (_nome, resposta) => {
    const calls = respondByPath({ [PROVIDERS_PATH]: [resposta() as Response] });
    renderAt('/entrar');

    await waitFor(() => expect(calls).toHaveLength(1));
    expect(screen.queryByText('ou entre com')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled();
  });

  it.each([
    ['{}', {}],
    ['provedores que não é lista', { provedores: 'google' }],
    ['itens sem chave em texto', { provedores: [{ chave: 1 }] }],
  ])('a lista fora do formato (%s) esconde o bloco sem aviso', async (_nome, corpo) => {
    const calls = respondByPath({ [PROVIDERS_PATH]: [jsonResponse(corpo, 200)] });
    renderAt('/entrar');

    await waitFor(() => expect(calls).toHaveLength(1));
    expect(screen.queryByText('ou entre com')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByLabelText('E-mail')).toBeInTheDocument();
  });
});

describe('o início do login pelo provedor', () => {
  async function clicarNoGoogle(user: ReturnType<typeof userEvent.setup>) {
    await user.click(await screen.findByRole('button', { name: 'Entrar com Google' }));
  }

  it('redirecionando para o Google, com o retorno do authorize no corpo', async () => {
    let responder: (r: Response) => void = () => {};
    const calls = respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [LOGIN_START_PATH]: [() => new Promise<Response>((resolve) => (responder = resolve))],
    });
    const user = userEvent.setup();
    renderAt(`/entrar?retorno=${encodeURIComponent(AUTHORIZE)}`);

    await clicarNoGoogle(user);
    expect(await screen.findByText('Redirecionando para o Google…')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar' })).not.toBeInTheDocument();
    responder(startOk());

    await waitFor(() => expect(assign).toHaveBeenCalledWith(GOOGLE_URL));
    expect(calls.find((c) => c.url === LOGIN_START_PATH)?.body).toEqual({ retorno: AUTHORIZE });
  });

  it.each([
    ['sem retorno', '/entrar'],
    [
      'com um retorno fora da regra',
      `/entrar?retorno=${encodeURIComponent('https://malicioso.exemplo/connect/authorize?x=1')}`,
    ],
  ])('%s, o corpo leva retorno null', async (_nome, path) => {
    const calls = respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [LOGIN_START_PATH]: [startOk()],
    });
    const user = userEvent.setup();
    renderAt(path);

    await clicarNoGoogle(user);
    await waitFor(() => expect(assign).toHaveBeenCalled());
    expect(calls.find((c) => c.url === LOGIN_START_PATH)?.body).toEqual({ retorno: null });
  });

  it('o Google fora do alcance (503): o aviso vermelho', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [LOGIN_START_PATH]: [jsonResponse({}, 503)],
    });
    const user = userEvent.setup();
    renderAt('/entrar');

    await clicarNoGoogle(user);
    const aviso = await screen.findByText(
      'Não foi possível falar com o provedor. Tente de novo em instantes.',
    );
    expect(corDe(alertaCom(aviso.textContent ?? ''))).toBe('red');
    expect(screen.getByLabelText('E-mail')).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
  });

  it('o provedor saiu da configuração (404 com o title dele): o aviso, a lista relida, e o botão some', async () => {
    const calls = respondByPath({
      [PROVIDERS_PATH]: [providersResponse(), providersResponse([])],
      [LOGIN_START_PATH]: [notFoundWith(START_404_TITLES.providerNotConfigured)],
    });
    const user = userEvent.setup();
    renderAt('/entrar');

    await clicarNoGoogle(user);
    expect(corDe(await waitFor(() => alertaCom(START_404_TITLES.providerNotConfigured)))).toBe(
      'red',
    );
    await waitFor(() => expect(calls.filter((c) => c.url === PROVIDERS_PATH)).toHaveLength(2));
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Entrar com Google' })).not.toBeInTheDocument(),
    );
    expect(screen.queryByText('ou entre com')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled();
  });

  it.each([
    ['sem corpo', () => notFoundWith()],
    ['com outro title', () => notFoundWith('Not Found')],
  ])(
    '404 sem o title conhecido no login (%s): o aviso da falha, sem reler a lista',
    async (_nome, resposta) => {
      const calls = respondByPath({
        [PROVIDERS_PATH]: [providersResponse()],
        [LOGIN_START_PATH]: [resposta()],
      });
      const user = userEvent.setup();
      renderAt('/entrar');

      await clicarNoGoogle(user);
      expect(
        corDe(
          await waitFor(() => alertaCom('Não foi possível entrar com o Google. Tente de novo.')),
        ),
      ).toBe('red');
      expect(calls.filter((c) => c.url === PROVIDERS_PATH)).toHaveLength(1);
      expect(screen.getByRole('button', { name: 'Entrar com Google' })).toBeInTheDocument();
    },
  );

  it('muitas tentativas no início (429): o aviso amarelo', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [LOGIN_START_PATH]: [jsonResponse({}, 429, { 'Retry-After': '1' })],
    });
    const user = userEvent.setup();
    renderAt('/entrar');

    await clicarNoGoogle(user);
    expect(
      corDe(
        await waitFor(() =>
          alertaCom('Muitas tentativas ao mesmo tempo. Tente de novo em instantes.'),
        ),
      ),
    ).toBe('yellow');
  });

  it('falha de rede no início: "Não foi possível falar com o servidor."', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [LOGIN_START_PATH]: [new TypeError('Failed to fetch')],
    });
    const user = userEvent.setup();
    renderAt('/entrar');

    await clicarNoGoogle(user);
    expect(corDe(await waitFor(() => alertaCom('Não foi possível falar com o servidor.')))).toBe(
      'red',
    );
  });
});

describe('a volta do provedor no /entrar', () => {
  const pendingSetup = () => new Promise<Response>(() => {});

  it('volta do Google para o código: o aviso verde, e o endereço sem federado e segundoPasso', async () => {
    respondByPath({ [PROVIDERS_PATH]: [providersResponse()] });
    renderAt(
      `/entrar?federado=corporativo&segundoPasso=totp&retorno=${encodeURIComponent(AUTHORIZE)}`,
    );

    expect(await screen.findByLabelText('Código')).toBeInTheDocument();
    const aviso = screen.getByText(
      'O Google confirmou a sua conta. Falta o código do app autenticador para entrar.',
    );
    expect(aviso.closest('[role="status"]')).not.toBeNull();
    await waitFor(() => expect(lugar()).toBe(`/entrar?retorno=${encodeURIComponent(AUTHORIZE)}`));
  });

  it('volta do Google para o cadastro: o aviso verde, e o endereço limpo', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      '/auth/session/enrollment/setup': [pendingSetup],
    });
    renderAt('/entrar?federado=corporativo&segundoPasso=cadastro');

    expect(await screen.findByText('Cadastre a verificação em 2 etapas')).toBeInTheDocument();
    expect(
      screen.getByText(
        'O Google confirmou a sua conta. Falta só cadastrar o app autenticador para entrar.',
      ),
    ).toBeInTheDocument();
    await waitFor(() => expect(lugar()).toBe('/entrar'));
  });

  it('convite aceito pelo Google: a chave sai antes do cadastro, e o aviso é só o do convite', async () => {
    sessionStorage.setItem(LINK_TOKEN_KEYS.convite, 'k7Qp2vX9');
    let chaveNoSetup: string | null = 'não pedido';
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      '/auth/session/enrollment/setup': () => {
        chaveNoSetup = sessionStorage.getItem(LINK_TOKEN_KEYS.convite);
        return new Promise<Response>(() => {}) as unknown as Response;
      },
    });
    renderAt('/entrar?federado=corporativo&segundoPasso=cadastro&convite=aceito');

    expect(await screen.findByText('Cadastre a verificação em 2 etapas')).toBeInTheDocument();
    expect(sessionStorage.getItem(LINK_TOKEN_KEYS.convite)).toBeNull();
    await waitFor(() => expect(chaveNoSetup).toBeNull());
    expect(
      screen.getByText('Convite aceito. Falta cadastrar o app autenticador para entrar.'),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(
        'O Google confirmou a sua conta. Falta só cadastrar o app autenticador para entrar.',
      ),
    ).not.toBeInTheDocument();
    await waitFor(() => expect(lugar()).toBe('/entrar'));
  });

  it('segundoPasso desconhecido: o e-mail e senha, sem aviso, e o endereço limpo', async () => {
    respondByPath({ [PROVIDERS_PATH]: [providersResponse()] });
    renderAt('/entrar?federado=corporativo&segundoPasso=xyz');

    expect(await screen.findByLabelText('E-mail')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    await waitFor(() => expect(lugar()).toBe('/entrar'));
  });

  it('recarregar depois de ler volta ao e-mail e senha, sem o aviso verde', async () => {
    respondByPath({ [PROVIDERS_PATH]: [providersResponse(), providersResponse()] });
    const primeira = renderAt('/entrar?federado=corporativo&segundoPasso=totp');
    await screen.findByLabelText('Código');
    await waitFor(() => expect(lugar()).toBe('/entrar'));
    primeira.unmount();

    // A recarga abre o endereço que a limpeza deixou.
    renderAt(lugar());

    expect(await screen.findByLabelText('E-mail')).toBeInTheDocument();
    expect(
      screen.queryByText(
        'O Google confirmou a sua conta. Falta o código do app autenticador para entrar.',
      ),
    ).not.toBeInTheDocument();
  });

  it('o código completa a entrada com o retorno, como no login por senha', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      '/auth/session/second-factor': [new Response(null, { status: 204 })],
    });
    const user = userEvent.setup();
    renderAt(
      `/entrar?federado=corporativo&segundoPasso=totp&retorno=${encodeURIComponent(AUTHORIZE)}`,
    );

    await user.type(await screen.findByLabelText('Código'), '123456');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith(AUTHORIZE));
  });
});

describe('as recusas no /entrar', () => {
  it.each([
    [
      'convite-pendente',
      'blue',
      'Você tem um convite pendente',
      'Abra o link do convite que você recebeu para ativar o acesso. Se o link expirou, peça um novo ao administrador.',
      'Entrar com Google',
    ],
    [
      'nao-conectada',
      'yellow',
      'Esta conta Google ainda não está conectada',
      'Entre com seu e-mail e senha e conecte o Google em Minha conta → Formas de entrada. Depois disso, o botão do Google passa a funcionar.',
      'Entrar com Google',
    ],
    [
      'desativado',
      'red',
      'Acesso desativado',
      'Seu acesso ao Buteco Agentes foi desativado. Fale com o administrador do painel.',
      'Entrar com Google',
    ],
    [
      'email-nao-verificado',
      'yellow',
      'E-mail não confirmado',
      'O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou com e-mail e senha.',
      'Entrar com outra conta Google',
    ],
    [
      'dominio-nao-permitido',
      'red',
      'Domínio não permitido',
      'Esta conta Google não é do domínio permitido neste painel.',
      'Entrar com outra conta Google',
    ],
  ])('%s: o aviso %s, o título, o texto e o botão', async (motivo, cor, titulo, texto, botao) => {
    const calls = respondByPath({ [PROVIDERS_PATH]: [providersResponse()] });
    renderAt(`/entrar?recusa=${motivo}&provedor=corporativo`);

    const alerta = await waitFor(() => alertaCom(texto));
    expect(alerta).toHaveTextContent(titulo);
    expect(corDe(alerta)).toBe(cor);
    expect(await screen.findByRole('button', { name: botao })).toBeInTheDocument();
    await waitFor(() => expect(lugar()).toBe('/entrar'));
    expect(calls.map((c) => c.url)).not.toContain(REFUSAL_PATH);
  });

  it('conta sem acesso, com o e-mail confirmado', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [REFUSAL_PATH]: refusalRoute({
        motivo: 'sem-acesso',
        provedor: 'corporativo',
        email: 'maria@empresa.com',
      }),
    });
    renderAt('/entrar?recusa=sem-acesso&provedor=corporativo');

    const alerta = await waitFor(() =>
      alertaCom(
        'O Google confirmou maria@empresa.com, mas essa conta não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.',
      ),
    );
    expect(alerta).toHaveTextContent('Conta sem acesso');
    expect(corDe(alerta)).toBe('red');
    expect(
      await screen.findByRole('button', { name: 'Entrar com outra conta Google' }),
    ).toBeInTheDocument();
    await waitFor(() => expect(lugar()).toBe('/entrar'));
  });

  it('conta sem acesso, sem o e-mail (o aviso já lido ou passado o prazo de 120 s: 404)', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [REFUSAL_PATH]: [new Response(null, { status: 404 })],
    });
    renderAt('/entrar?recusa=sem-acesso&provedor=corporativo');

    const alerta = await waitFor(() =>
      alertaCom(
        'Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.',
      ),
    );
    expect(alerta).toHaveTextContent('Conta sem acesso');
    expect(
      await screen.findByRole('button', { name: 'Entrar com outra conta Google' }),
    ).toBeInTheDocument();
  });

  it.each([
    [
      'o aviso de outra recusa não empresta o e-mail',
      { motivo: 'nao-conectada', provedor: 'corporativo' },
    ],
    [
      'o aviso de outro provedor não empresta o e-mail',
      { motivo: 'sem-acesso', provedor: 'outro' },
    ],
  ])('%s', async (_nome, gravada) => {
    const calls = respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [REFUSAL_PATH]: refusalRoute({ ...gravada, email: 'maria@empresa.com' }),
    });
    renderAt('/entrar?recusa=sem-acesso&provedor=corporativo');

    const alerta = await waitFor(() =>
      alertaCom(
        'Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.',
      ),
    );
    expect(alerta).toHaveTextContent('Conta sem acesso');
    expect(screen.queryByText(/maria@empresa\.com/)).not.toBeInTheDocument();
    expect(calls.filter((c) => c.url === REFUSAL_PATH)).toHaveLength(1);
  });

  it('sem o provedor na query, o motivo igual basta para o e-mail', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [REFUSAL_PATH]: refusalRoute({
        motivo: 'sem-acesso',
        provedor: 'corporativo',
        email: 'maria@empresa.com',
      }),
    });
    renderAt('/entrar?recusa=sem-acesso');

    await waitFor(() =>
      alertaCom(
        'O Google confirmou maria@empresa.com, mas essa conta não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.',
      ),
    );
  });

  it.each([['/entrar?recusa=falha&provedor=corporativo'], ['/entrar?recusa=falha']])(
    'falha (%s): o aviso vermelho sem título',
    async (path) => {
      respondByPath({ [PROVIDERS_PATH]: [providersResponse()] });
      renderAt(path);

      const alerta = await waitFor(() =>
        alertaCom('Não foi possível entrar com o Google. Tente de novo.'),
      );
      expect(alerta.textContent).toBe('Não foi possível entrar com o Google. Tente de novo.');
      expect(corDe(alerta)).toBe('red');
      expect(await screen.findByRole('button', { name: 'Entrar com Google' })).toBeInTheDocument();
    },
  );

  it('motivo desconhecido sai como a falha, e não como tela sem aviso', async () => {
    respondByPath({ [PROVIDERS_PATH]: [providersResponse()] });
    renderAt('/entrar?recusa=motivo-novo&provedor=corporativo');

    const alerta = await waitFor(() =>
      alertaCom('Não foi possível entrar com o Google. Tente de novo.'),
    );
    expect(alerta.textContent).toBe('Não foi possível entrar com o Google. Tente de novo.');
  });

  it('a limpeza mantém o retorno, e recarregar abre o formulário sem o aviso', async () => {
    respondByPath({ [PROVIDERS_PATH]: [providersResponse(), providersResponse()] });
    const primeira = renderAt(
      `/entrar?recusa=desativado&provedor=corporativo&retorno=${encodeURIComponent(AUTHORIZE)}`,
    );
    await waitFor(() => alertaCom('Acesso desativado'));
    await waitFor(() => expect(lugar()).toBe(`/entrar?retorno=${encodeURIComponent(AUTHORIZE)}`));
    primeira.unmount();

    renderAt(lugar());

    expect(await screen.findByLabelText('E-mail')).toBeInTheDocument();
    expect(screen.queryByText('Acesso desativado')).not.toBeInTheDocument();
  });
});

describe('o e-mail confirmado da recusa é lido uma vez', () => {
  it('uma chamada só no StrictMode, e o aviso mostra o e-mail', async () => {
    const calls = respondByPath({
      [PROVIDERS_PATH]: () => providersResponse(),
      [REFUSAL_PATH]: refusalRoute({
        motivo: 'sem-acesso',
        provedor: 'corporativo',
        email: 'maria@empresa.com',
      }),
    });
    renderAt('/entrar?recusa=sem-acesso&provedor=corporativo', { strict: true });

    await waitFor(() => alertaCom('O Google confirmou maria@empresa.com'));
    expect(calls.filter((c) => c.url === REFUSAL_PATH)).toHaveLength(1);
  });

  it('falha de rede na leitura do aviso: a variante sem o e-mail', async () => {
    respondByPath({
      [PROVIDERS_PATH]: [providersResponse()],
      [REFUSAL_PATH]: [new TypeError('Failed to fetch')],
    });
    renderAt('/entrar?recusa=sem-acesso&provedor=corporativo');

    const alerta = await waitFor(() =>
      alertaCom(
        'Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.',
      ),
    );
    expect(alerta).toHaveTextContent('Conta sem acesso');
  });

  it('os outros motivos não leem o aviso', async () => {
    const calls = respondByPath({ [PROVIDERS_PATH]: [providersResponse()] });
    renderAt('/entrar?recusa=convite-pendente&provedor=corporativo');

    await waitFor(() => alertaCom('Você tem um convite pendente'));
    expect(calls.map((c) => c.url)).toEqual([PROVIDERS_PATH]);
  });
});
