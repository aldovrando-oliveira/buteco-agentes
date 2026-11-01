import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { EntrarPage } from './EntrarPage';
import { authRuntime } from '../../../auth/session';
import { PENDING_LOGIN_KEY } from '../../../auth/loginFlow';
import { SESSION_KEY } from '../../../auth/sessionStore';
import { jsonResponse } from '../../../test/authFakes';

const assign = vi.fn<(url: string) => void>();

function renderPage(path = '/entrar') {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter initialEntries={[path]}>
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

  it('tem E-mail e Senha, sem "Esqueceu a senha?" e sem o convite', () => {
    renderPage();

    expect(screen.getByLabelText('E-mail')).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('placeholder', 'voce@empresa.com');
    expect(screen.getByLabelText('Senha')).toHaveAttribute('autocomplete', 'current-password');
    expect(screen.queryByText(/esqueceu a senha/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/convite/i)).not.toBeInTheDocument();
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
});
