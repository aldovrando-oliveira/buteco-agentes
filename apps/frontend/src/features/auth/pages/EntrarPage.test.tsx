import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { StrictMode } from 'react';
import { PASSWORD_CHANGED_KEY } from '../passwordChangedNotice';
import { theme } from '../../../theme';
import { EntrarPage } from './EntrarPage';
import { authRuntime } from '../../../auth/session';
import { PENDING_LOGIN_KEY } from '../../../auth/loginFlow';
import { SESSION_KEY } from '../../../auth/sessionStore';
import { jsonResponse } from '../../../test/authFakes';

const assign = vi.fn<(url: string) => void>();

function renderPage(path = '/entrar', state?: unknown) {
  const [pathname, search = ''] = path.split('?');
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter initialEntries={[{ pathname, search: search ? `?${search}` : '', state }]}>
        <EntrarPage />
      </MemoryRouter>
    </MantineProvider>,
  );
}

async function submit(
  user: ReturnType<typeof userEvent.setup>,
  email = 'ana@exemplo.test',
  senha = 'senha-certa',
) {
  if (email) await user.type(screen.getByLabelText('E-mail'), email);
  if (senha) await user.type(screen.getByLabelText('Senha'), senha);
  await user.click(screen.getByRole('button', { name: 'Entrar' }));
}

function respond(response: Response | Error) {
  const fetchMock = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

beforeEach(() => {
  assign.mockReset();
  vi.spyOn(authRuntime, 'assign').mockImplementation(assign);
  sessionStorage.clear();
  localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('EntrarPage', () => {
  it('encabeça a tela com a marca, nomeando o produto uma única vez', () => {
    const { container } = renderPage();

    expect(screen.getByRole('heading', { level: 2, name: 'Buteco Agentes' })).toBeInTheDocument();
    const anunciado = screen.getAllByText('Buteco Agentes', {
      ignore: '[aria-hidden="true"], [aria-hidden="true"] *',
    });
    expect(anunciado).toHaveLength(1);
    expect(container.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  // Renomeado na change entrada-publica-painel: o requisito da `operator-login-ui` que
  // proibia "Esqueceu a senha?" e o convite até a #181 foi invertido nela. Era "tem E-mail
  // e Senha, sem "Esqueceu a senha?" e sem o convite".
  it('tem E-mail e Senha, com "Esqueceu a senha?" e o convite ao administrador', () => {
    renderPage();

    expect(screen.getByLabelText('E-mail')).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('placeholder', 'voce@empresa.com');
    expect(screen.getByLabelText('Senha')).toHaveAttribute('autocomplete', 'current-password');
    expect(screen.getByRole('button', { name: 'Esqueceu a senha?' })).toBeInTheDocument();
    expect(
      screen.getByText('Ainda não tem acesso? Peça um convite ao administrador do painel.'),
    ).toBeInTheDocument();
  });

  it('"Esqueceu a senha?" abre e fecha o texto na linha, sem enviar nada', async () => {
    const fetchMock = respond(new Response(null, { status: 204 }));
    const user = userEvent.setup();
    renderPage();
    const texto =
      'Peça a um administrador do painel um link para criar uma nova senha. O link vale por 24 horas.';

    expect(screen.queryByText(texto)).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Esqueceu a senha?' }));
    expect(screen.getByText(texto)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Esqueceu a senha?' }));
    expect(screen.queryByText(texto)).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('204 sem retorno começa o fluxo: POST com email e senha, e o authorize com o voltarPara guardado', async () => {
    const fetchMock = respond(new Response(null, { status: 204 }));
    const user = userEvent.setup();
    renderPage('/entrar?voltarPara=%2Fknowledge-bases');

    await submit(user);

    await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/auth/session');
    expect(JSON.parse(String(init.body))).toEqual({ email: 'ana@exemplo.test', senha: 'senha-certa' });
    const authorize = new URL(assign.mock.calls[0][0], 'http://localhost');
    expect(authorize.pathname).toBe('/connect/authorize');
    expect(authorize.searchParams.get('code_challenge_method')).toBe('S256');
    expect(JSON.parse(sessionStorage.getItem(PENDING_LOGIN_KEY)!).voltarPara).toBe('/knowledge-bases');
  });

  it('204 com retorno válido navega para exatamente a string recebida', async () => {
    respond(new Response(null, { status: 204 }));
    const retorno =
      '/connect/authorize?client_id=buteco-painel&redirect_uri=https%3A%2F%2Fpainel%2Fentrar%2Fretorno&state=a%2Bb%3D';
    const user = userEvent.setup();
    renderPage(`/entrar?retorno=${encodeURIComponent(retorno)}`);

    await submit(user);

    await waitFor(() => expect(assign).toHaveBeenCalledWith(retorno));
    expect(sessionStorage.getItem(PENDING_LOGIN_KEY)).toBeNull();
  });

  it('204 com retorno fora da regra recomeça o fluxo e termina na tela inicial', async () => {
    respond(new Response(null, { status: 204 }));
    const user = userEvent.setup();
    renderPage(`/entrar?retorno=${encodeURIComponent('/connect/authorizex?x')}&voltarPara=%2Fagents`);

    await submit(user);

    await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
    expect(new URL(assign.mock.calls[0][0], 'http://localhost').pathname).toBe('/connect/authorize');
    expect(JSON.parse(sessionStorage.getItem(PENDING_LOGIN_KEY)!).voltarPara).toBe('/');
  });

  it('e-mail ou senha vazios mostram "Informe e-mail e senha." sem enviar', async () => {
    const fetchMock = respond(new Response(null, { status: 204 }));
    const user = userEvent.setup();
    renderPage();

    await submit(user, 'ana@exemplo.test', '');

    expect(await screen.findByText('Informe e-mail e senha.')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('401 mostra o title do servidor, continua no /entrar e não grava sessão', async () => {
    respond(jsonResponse({ title: 'E-mail ou senha inválidos.' }, 401));
    const user = userEvent.setup();
    renderPage();

    await submit(user);

    expect(await screen.findByText('E-mail ou senha inválidos.')).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
    expect(localStorage.getItem(SESSION_KEY)).toBeNull();
  });

  it('403 mostra o title do servidor', async () => {
    respond(jsonResponse({ title: 'Acesso desativado. Fale com o administrador.' }, 403));
    const user = userEvent.setup();
    renderPage();

    await submit(user);

    expect(await screen.findByText('Acesso desativado. Fale com o administrador.')).toBeInTheDocument();
  });

  it.each([
    ['600', '10 minutos'],
    ['61', '2 minutos'],
    ['60', '1 minuto'],
  ])('423 com Retry-After %s mostra o bloqueio com %s', async (retryAfter, minutos) => {
    respond(jsonResponse({ title: 'Acesso pausado…' }, 423, { 'Retry-After': retryAfter }));
    const user = userEvent.setup();
    renderPage();

    await submit(user);

    expect(
      await screen.findByText(
        `Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por ${minutos}.`,
      ),
    ).toBeInTheDocument();
  });

  it('429 mostra o texto de tentativas ao mesmo tempo', async () => {
    respond(jsonResponse({}, 429, { 'Retry-After': '1' }));
    const user = userEvent.setup();
    renderPage();

    await submit(user);

    expect(
      await screen.findByText('Muitas tentativas ao mesmo tempo. Tente de novo em instantes.'),
    ).toBeInTheDocument();
  });

  it('503 mostra "O painel está iniciando" e desabilita o Entrar até o Retry-After', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fetchMock = respond(jsonResponse({}, 503, { 'Retry-After': '5' }));
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderPage();

    await submit(user);

    expect(
      await screen.findByText('O painel está iniciando. Tente de novo em alguns segundos.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeDisabled();
    await act(() => vi.advanceTimersByTimeAsync(4_000));
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeDisabled();
    await act(() => vi.advanceTimersByTimeAsync(1_100));
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('falha de rede mostra que não foi possível falar com o servidor', async () => {
    respond(new TypeError('Failed to fetch'));
    const user = userEvent.setup();
    renderPage();

    await submit(user);

    expect(await screen.findByText('Não foi possível falar com o servidor.')).toBeInTheDocument();
  });

  it('?motivo=sessao-expirada mostra o aviso num elemento com role="status"', () => {
    renderPage('/entrar?motivo=sessao-expirada');

    expect(screen.getByRole('status')).toHaveTextContent(
      'Sua sessão expirou. Entre novamente para continuar de onde parou.',
    );
  });

  it('?motivo=falha-na-entrada mostra o aviso genérico do código recusado', () => {
    renderPage('/entrar?motivo=falha-na-entrada&voltarPara=%2Fagents');

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Não foi possível concluir a entrada. Entre de novo.',
    );
  });

  it('sem Web Locks o formulário não envia, e o aviso de HTTPS aparece', async () => {
    const locks = navigator.locks;
    Object.defineProperty(navigator, 'locks', { value: undefined, configurable: true });
    try {
      const fetchMock = respond(new Response(null, { status: 204 }));
      const user = userEvent.setup();
      renderPage();

      expect(screen.getByText('O painel precisa ser aberto por HTTPS.')).toBeInTheDocument();
      await submit(user);

      expect(fetchMock).not.toHaveBeenCalled();
      expect(assign).not.toHaveBeenCalled();
    } finally {
      Object.defineProperty(navigator, 'locks', { value: locks, configurable: true });
    }
  });

  // O aviso depois da troca da senha em Minha conta (requisito "Aviso de senha alterada no
  // /entrar"; design.md da change minha-conta-painel, D3; decisão do dono Q1).
  describe('aviso de senha alterada', () => {
    const SENHA_ALTERADA = 'Senha alterada. Entre com a nova senha.';

    it('com a chave na aba, avisa em role="status" e apaga a chave', () => {
      sessionStorage.setItem(PASSWORD_CHANGED_KEY, '1');
      renderPage();

      expect(screen.getByRole('status')).toHaveTextContent(SENHA_ALTERADA);
      expect(sessionStorage.getItem(PASSWORD_CHANGED_KEY)).toBeNull();
    });

    it('no StrictMode, o aviso aparece e a chave sai', () => {
      sessionStorage.setItem(PASSWORD_CHANGED_KEY, '1');
      render(
        <StrictMode>
          <MantineProvider theme={theme}>
            <MemoryRouter initialEntries={['/entrar']}>
              <EntrarPage />
            </MemoryRouter>
          </MantineProvider>
        </StrictMode>,
      );

      expect(screen.getByRole('status')).toHaveTextContent(SENHA_ALTERADA);
      expect(sessionStorage.getItem(PASSWORD_CHANGED_KEY)).toBeNull();
    });

    it('o aviso aparece uma vez só', () => {
      sessionStorage.setItem(PASSWORD_CHANGED_KEY, '1');
      const first = renderPage();
      first.unmount();

      renderPage();

      expect(screen.queryByText(SENHA_ALTERADA)).not.toBeInTheDocument();
    });

    it('vence a sessão expirada', () => {
      sessionStorage.setItem(PASSWORD_CHANGED_KEY, '1');
      renderPage('/entrar?motivo=sessao-expirada');

      expect(screen.getByRole('status')).toHaveTextContent(SENHA_ALTERADA);
      expect(screen.queryByText(/Sua sessão expirou/)).not.toBeInTheDocument();
    });

    it('sem a chave, não há aviso de senha alterada', () => {
      renderPage();

      expect(screen.queryByText(SENHA_ALTERADA)).not.toBeInTheDocument();
    });

    it('some ao enviar, e o resultado do envio segue as regras', async () => {
      const user = userEvent.setup();
      sessionStorage.setItem(PASSWORD_CHANGED_KEY, '1');
      respond(jsonResponse({ title: 'E-mail ou senha inválidos.' }, 401));
      renderPage('/entrar?motivo=sessao-expirada');

      await submit(user);

      expect(await screen.findByRole('alert')).toHaveTextContent('E-mail ou senha inválidos.');
      expect(screen.queryByText(SENHA_ALTERADA)).not.toBeInTheDocument();
      expect(screen.queryByText(/Sua sessão expirou/)).not.toBeInTheDocument();
    });
  });

  describe('passo das 2 etapas', () => {
    // O primeiro pedido é o da senha (200 com o segundo passo); os seguintes, o do código.
    function respondCode(...codeResponses: Array<Response | Error>) {
      const queue = [...codeResponses];
      const fetchMock = vi.fn<(input: string, init?: RequestInit) => Promise<Response>>(async (input) => {
        if (input === '/auth/session') return jsonResponse({ segundoPasso: 'totp' }, 200);
        const next = queue.shift();
        if (next === undefined) throw new Error('pedido a mais');
        if (next instanceof Error) throw next;
        return next;
      });
      vi.stubGlobal('fetch', fetchMock);
      return fetchMock;
    }

    async function openCodeStep(user: ReturnType<typeof userEvent.setup>) {
      await submit(user);
      return screen.findByLabelText('Código');
    }

    async function verify(user: ReturnType<typeof userEvent.setup>, codigo: string) {
      const campo = screen.getByLabelText('Código');
      await user.clear(campo);
      if (codigo) await user.type(campo, codigo);
      await user.click(screen.getByRole('button', { name: 'Verificar' }));
    }

    function codeBodies(fetchMock: ReturnType<typeof respondCode>) {
      return fetchMock.mock.calls
        .filter(([input]) => input === '/auth/session/second-factor')
        .map(([, init]) => JSON.parse(String(init?.body)));
    }

    it('a senha certa de quem tem as 2 etapas abre o passo do código, sem sessão e sem navegar', async () => {
      respondCode();
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);

      expect(screen.getByRole('button', { name: 'Verificar' })).toBeInTheDocument();
      expect(screen.queryByLabelText('E-mail')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Senha')).not.toBeInTheDocument();
      expect(assign).not.toHaveBeenCalled();
      expect(localStorage.getItem(SESSION_KEY)).toBeNull();
    });

    it('o campo aceita texto, com a ajuda do código de recuperação', async () => {
      respondCode();
      const user = userEvent.setup();
      renderPage();

      const campo = await openCodeStep(user);

      expect(campo).toHaveAttribute('inputmode', 'text');
      expect(campo).toHaveAttribute('autocapitalize', 'characters');
      expect(campo).toHaveAttribute('autocomplete', 'one-time-code');
      expect(campo).toHaveAttribute('spellcheck', 'false');
      expect(campo).toHaveAttribute('placeholder', '000 000');
      const ids = (campo.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(Boolean);
      expect(ids.map((id) => document.getElementById(id)?.textContent)).toContain(
        'Código de recuperação: 16 letras e números, como XXXX-XXXX-XXXX-XXXX.',
      );
    });

    it('código certo segue o fluxo do emissor com o voltarPara da URL', async () => {
      respondCode(new Response(null, { status: 204 }));
      const user = userEvent.setup();
      renderPage('/entrar?voltarPara=%2Fagents');

      await openCodeStep(user);
      await verify(user, '123456');

      await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
      expect(new URL(assign.mock.calls[0][0], 'http://localhost').pathname).toBe('/connect/authorize');
      expect(JSON.parse(sessionStorage.getItem(PENDING_LOGIN_KEY)!).voltarPara).toBe('/agents');
    });

    it('código certo com retorno do authorize segue o retorno', async () => {
      respondCode(new Response(null, { status: 204 }));
      const retorno = '/connect/authorize?client_id=buteco-painel&state=x';
      const user = userEvent.setup();
      renderPage(`/entrar?retorno=${encodeURIComponent(retorno)}`);

      await openCodeStep(user);
      await verify(user, '123456');

      await waitFor(() => expect(assign).toHaveBeenCalledWith(retorno));
    });

    it('código errado aparece no campo, com aria-invalid, e a tela continua no passo', async () => {
      respondCode(jsonResponse({ title: 'Código inválido.' }, 401));
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '000000');

      expect(await screen.findByText('Código inválido.')).toBeInTheDocument();
      expect(screen.getByLabelText('Código')).toHaveAttribute('aria-invalid', 'true');
      expect(assign).not.toHaveBeenCalled();
    });

    it('entrada expirada volta a e-mail e senha, com o aviso em role="status"', async () => {
      respondCode(jsonResponse({ title: 'A entrada expirou. Informe e-mail e senha de novo.' }, 401));
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '123456');

      expect(await screen.findByLabelText('E-mail')).toBeInTheDocument();
      expect(screen.queryByLabelText('Código')).not.toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent('A entrada expirou. Informe e-mail e senha de novo.');
    });

    it('outro texto de 401 não é tomado por entrada expirada', async () => {
      respondCode(jsonResponse({ title: 'A entrada expirou.' }, 401));
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '123456');

      expect(await screen.findByText('Código inválido.')).toBeInTheDocument();
      expect(screen.getByLabelText('Código')).toBeInTheDocument();
    });

    it('campo vazio ou só com espaços não envia e mostra "Informe o código."', async () => {
      const fetchMock = respondCode();
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '   ');

      expect(await screen.findByText('Informe o código.')).toBeInTheDocument();
      expect(screen.getByLabelText('Código')).toHaveAttribute('aria-invalid', 'true');
      expect(codeBodies(fetchMock)).toEqual([]);
    });

    it('espaços nas pontas saem, e o resto vai como está', async () => {
      const fetchMock = respondCode(new Response(null, { status: 204 }));
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '  7kq2 m9xd-4trb-pw6h  ');

      await waitFor(() => expect(codeBodies(fetchMock)).toEqual([{ codigo: '7kq2 m9xd-4trb-pw6h' }]));
    });

    it('bloqueio no passo mostra os minutos do Retry-After', async () => {
      respondCode(jsonResponse({ title: 'Acesso pausado…' }, 423, { 'Retry-After': '600' }));
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '123456');

      expect(
        await screen.findByText('Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por 10 minutos.'),
      ).toBeInTheDocument();
      expect(screen.getByLabelText('Código')).toBeInTheDocument();
    });

    it('muitas tentativas desabilita Verificar até o Retry-After', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      respondCode(jsonResponse({}, 429, { 'Retry-After': '1' }));
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderPage();

      await openCodeStep(user);
      await verify(user, '123456');

      expect(await screen.findByText('Muitas tentativas ao mesmo tempo. Tente de novo em instantes.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Verificar' })).toBeDisabled();
      await act(() => vi.advanceTimersByTimeAsync(1_100));
      expect(screen.getByRole('button', { name: 'Verificar' })).toBeEnabled();
    });

    it('painel iniciando no passo desabilita Verificar até o Retry-After', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      respondCode(jsonResponse({}, 503, { 'Retry-After': '5' }));
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderPage();

      await openCodeStep(user);
      await verify(user, '123456');

      expect(await screen.findByText('O painel está iniciando. Tente de novo em alguns segundos.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Verificar' })).toBeDisabled();
      await act(() => vi.advanceTimersByTimeAsync(4_000));
      expect(screen.getByRole('button', { name: 'Verificar' })).toBeDisabled();
      await act(() => vi.advanceTimersByTimeAsync(1_100));
      expect(screen.getByRole('button', { name: 'Verificar' })).toBeEnabled();
    });

    it('chave trocada mostra o texto do servidor em role="alert"', async () => {
      const texto =
        'Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador.';
      respondCode(jsonResponse({ title: texto }, 500));
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '123456');

      expect(await screen.findByRole('alert')).toHaveTextContent(texto);
      expect(screen.getByLabelText('Código')).toBeEnabled();
    });

    it('sem conexão no passo mantém o código digitado', async () => {
      respondCode(new TypeError('Failed to fetch'));
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await verify(user, '123456');

      expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível falar com o servidor.');
      expect(screen.getByLabelText('Código')).toHaveValue('123456');
    });

    it('Voltar retorna a e-mail e senha sem enviar nada', async () => {
      const fetchMock = respondCode();
      const user = userEvent.setup();
      renderPage();

      await openCodeStep(user);
      await user.click(screen.getByRole('button', { name: 'Voltar' }));

      expect(await screen.findByLabelText('E-mail')).toBeInTheDocument();
      expect(codeBodies(fetchMock)).toEqual([]);
    });

    it('recarregar volta a e-mail e senha', async () => {
      respondCode();
      const user = userEvent.setup();
      const { unmount } = renderPage();
      await openCodeStep(user);

      unmount();
      renderPage();

      expect(screen.getByLabelText('E-mail')).toBeInTheDocument();
      expect(screen.queryByLabelText('Código')).not.toBeInTheDocument();
    });
  });

  describe('vindo das páginas do link', () => {
    it('com { etapa: "codigo" }, abre no passo do código com o aviso de senha salva', () => {
      renderPage('/entrar', { etapa: 'codigo' });

      expect(screen.getByLabelText('Código')).toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent(
        'Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.',
      );
    });

    it('com o login recusado, o e-mail chega preenchido e o aviso do status aparece', () => {
      renderPage('/entrar', { email: 'marcos.lima@empresa.com', falha: { kind: 'http', status: 429, retryAfter: 1 } });

      expect(screen.getByLabelText('E-mail')).toHaveValue('marcos.lima@empresa.com');
      expect(screen.getByText('Muitas tentativas ao mesmo tempo. Tente de novo em instantes.')).toBeInTheDocument();
    });
  });
});

