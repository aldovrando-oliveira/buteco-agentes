import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QRCodeSVG } from 'qrcode.react';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { defaultRoutes, fakeAccountApi, problem, type FakeReply } from '../test/fakeAccountApi';
import { renderAccount } from '../test/renderAccount';
import { RECOVERY_CODES_FILE_NAME } from '../recoveryCodesFile';
import { TwoFactorCard } from './TwoFactorCard';

// Os requisitos das 2 etapas da spec `panel-account-page` (design.md da change
// minha-conta-painel, D4, D5 e D8).

const SEGREDO = 'JBSWY3DPEHPK3PXP';
const URI = 'otpauth://totp/Buteco%20Agentes:ana%40exemplo.test?secret=JBSWY3DPEHPK3PXP&issuer=Buteco%20Agentes';
const CODIGOS = Array.from({ length: 10 }, (_, i) => `C0D${i}-AAAA-BBBB-CCCC`);
const SENHA = 'a-senha-da-ana';

const status = (s: Partial<{ ativa: boolean; ativadaEm: string | null; codigosRestantes: number; cadastroDisponivel: boolean }>) => ({
  status: 200,
  body: { ativa: false, ativadaEm: null, codigosRestantes: 0, cadastroDisponivel: true, ...s },
});
const ATIVA = status({ ativa: true, ativadaEm: '2026-08-14T15:00:00Z', codigosRestantes: 8 });

beforeEach(() => {
  signInForTest();
  sessionStorage.clear();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function setup(routes: Record<string, FakeReply | FakeReply[]> = {}) {
  const api = fakeAccountApi({ ...defaultRoutes(), ...routes });
  const view = renderAccount(<TwoFactorCard />);
  return { api, ...view, user: userEvent.setup() };
}

async function ateOQr(t: ReturnType<typeof setup>) {
  await t.user.click(await screen.findByRole('button', { name: 'Ligar verificação' }));
  await t.user.type(screen.getByLabelText('Sua senha'), SENHA);
  await t.user.click(screen.getByRole('button', { name: 'Continuar' }));
  await screen.findByRole('img', { name: 'QR para o app autenticador' });
}

async function confirmar(t: ReturnType<typeof setup>, codigo = '123456') {
  await t.user.type(screen.getByLabelText('Digite o código de 6 dígitos que o app mostra'), codigo);
  await t.user.click(screen.getByRole('button', { name: 'Confirmar' }));
}

const SETUP_OK: FakeReply = { status: 200, body: { segredo: SEGREDO, otpauthUri: URI } };
const CONFIRM_OK: FakeReply = { status: 200, body: { codigosRecuperacao: CODIGOS } };

describe('TwoFactorCard: o estado', () => {
  it('cadastro fechado: "Não configurada", o texto e "Ligar verificação" desabilitado', async () => {
    setup({ 'GET /account/two-factor': status({ cadastroDisponivel: false }) });

    expect(await screen.findByText('Não configurada')).toBeInTheDocument();
    expect(screen.getByText('O cadastro da verificação em 2 etapas ainda não está disponível neste painel.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ligar verificação' })).toBeDisabled();
  });

  it('cadastro aberto, desligada: "Ligar verificação" habilitado', async () => {
    setup({ 'GET /account/two-factor': status({}) });

    expect(await screen.findByRole('button', { name: 'Ligar verificação' })).toBeEnabled();
    expect(
      screen.getByText('Com a verificação ligada, além da senha você informa um código do app autenticador do celular.'),
    ).toBeInTheDocument();
  });

  it('ativa com o cadastro fechado continua gerenciável', async () => {
    setup({ 'GET /account/two-factor': status({ ativa: true, ativadaEm: '2026-08-14T15:00:00Z', codigosRestantes: 8, cadastroDisponivel: false }) });

    expect(await screen.findByText('Ativa')).toBeInTheDocument();
    expect(screen.getByText('desde 14/08/2026')).toBeInTheDocument();
    expect(screen.getByText('8 códigos de recuperação restantes.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Gerar novos códigos' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Remover verificação' })).toBeEnabled();
  });

  it.each([
    [3, 'Restam 3 códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.'],
    [2, 'Restam 2 códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.'],
    [1, 'Resta 1 código de recuperação. Gere novos para não ficar sem acesso se perder o celular.'],
    [0, 'Não restam códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.'],
  ])('poucos códigos: com %i, o aviso do número', async (restantes, texto) => {
    setup({ 'GET /account/two-factor': status({ ativa: true, ativadaEm: '2026-08-14T15:00:00Z', codigosRestantes: restantes }) });

    expect(await screen.findByRole('status')).toHaveTextContent(texto);
  });

  it('com 4 códigos ou mais, nenhum aviso de poucos códigos', async () => {
    setup({ 'GET /account/two-factor': status({ ativa: true, ativadaEm: '2026-08-14T15:00:00Z', codigosRestantes: 4 }) });

    await screen.findByText('4 códigos de recuperação restantes.');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});

describe('TwoFactorCard: ligar a verificação', () => {
  it('senha errada no início do cadastro fica no campo, sem deslogar', async () => {
    const t = setup({ 'POST /account/two-factor/setup': problem(401, 'Senha inválida.') });

    await t.user.click(await screen.findByRole('button', { name: 'Ligar verificação' }));
    await t.user.type(screen.getByLabelText('Sua senha'), 'errada');
    await t.user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByText('Senha inválida.')).toBeInTheDocument();
    expect(screen.getByLabelText('Sua senha')).toHaveAttribute('aria-invalid', 'true');
    expect(t.api.calls('POST /account/two-factor/setup')).toHaveLength(1);
    expect(authRuntime.assign).not.toHaveBeenCalled();
  });

  it('código errado no QR: o texto do quadro, no campo', async () => {
    const t = setup({ 'POST /account/two-factor/setup': SETUP_OK, 'POST /account/two-factor/confirm': problem(401, 'Código inválido.') });
    await ateOQr(t);

    await confirmar(t, '000000');

    expect(await screen.findByText('Código inválido. Confira se o relógio do celular está certo.')).toBeInTheDocument();
    expect(screen.getByLabelText('Digite o código de 6 dígitos que o app mostra')).toHaveAttribute('aria-invalid', 'true');
  });

  it('cadastro vencido: o aviso do quadro', async () => {
    const t = setup({
      'POST /account/two-factor/setup': SETUP_OK,
      'POST /account/two-factor/confirm': problem(400, 'Não há cadastro em curso. Comece de novo.'),
    });
    await ateOQr(t);

    await confirmar(t);

    expect(await screen.findByRole('alert')).toHaveTextContent('O cadastro venceu. Comece de novo para gerar outro QR.');
  });

  it('o campo do QR: numérico, de código único, com a ajuda dos 10 minutos', async () => {
    const t = setup({ 'POST /account/two-factor/setup': SETUP_OK });
    await ateOQr(t);

    const campo = screen.getByLabelText('Digite o código de 6 dígitos que o app mostra');
    expect(campo).toHaveAttribute('inputmode', 'numeric');
    expect(campo).toHaveAttribute('autocomplete', 'one-time-code');
    expect(campo).toHaveAttribute('placeholder', '000 000');
    expect(screen.getByText('Este cadastro vale por 10 minutos. Depois disso, comece de novo.')).toBeInTheDocument();
  });

  it('cadastro de ponta a ponta: senha, QR, código, os 10 códigos e "Concluir"', async () => {
    const t = setup({
      'GET /account/two-factor': [status({}), ATIVA],
      'POST /account/two-factor/setup': SETUP_OK,
      'POST /account/two-factor/confirm': CONFIRM_OK,
    });
    await ateOQr(t);

    await confirmar(t, ' 123456 ');

    expect(await screen.findByText('Verificação ligada.')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Códigos de recuperação' })).getAllByRole('listitem')).toHaveLength(10);
    expect(t.api.calls('POST /account/two-factor/setup')[0].body).toEqual({ senha: SENHA });
    expect(t.api.calls('POST /account/two-factor/confirm')[0].body).toEqual({ codigo: '123456' });

    await t.user.click(screen.getByRole('button', { name: 'Concluir' }));

    expect(await screen.findByText('Ativa')).toBeInTheDocument();
    expect(screen.queryByText(CODIGOS[0])).not.toBeInTheDocument();
  });
});

describe('TwoFactorCard: QR e segredo', () => {
  it('o QR é o do otpauthUri', async () => {
    const t = setup({ 'POST /account/two-factor/setup': SETUP_OK });
    await ateOQr(t);

    const qr = screen.getByRole('img', { name: 'QR para o app autenticador' });
    const { container } = render(<QRCodeSVG value={URI} size={164} marginSize={2} />);
    const caminho = (svg: Element) => [...svg.querySelectorAll('path')].map((p) => p.getAttribute('d')).join('|');
    expect(caminho(qr)).toBe(caminho(container.querySelector('svg')!));
  });

  it('o QR não traz imagem, foreignObject nem script', async () => {
    const t = setup({ 'POST /account/two-factor/setup': SETUP_OK });
    await ateOQr(t);

    const qr = screen.getByRole('img', { name: 'QR para o app autenticador' });
    expect(qr.tagName.toLowerCase()).toBe('svg');
    expect(qr.querySelector('image, foreignObject, script')).toBeNull();
  });

  it('o segredo em grupos de 4, e "Copiar" copia sem espaços', async () => {
    const t = setup({ 'POST /account/two-factor/setup': SETUP_OK });
    await ateOQr(t);

    expect(screen.getByTestId('segredo')).toHaveTextContent('JBSW Y3DP EHPK 3PXP');
    expect(screen.getByText('Buteco Agentes')).toBeInTheDocument();

    await t.user.click(screen.getByRole('button', { name: 'Copiar' }));

    // O `userEvent.setup()` põe a área de transferência falsa dele no `navigator.clipboard`.
    expect(await navigator.clipboard.readText()).toBe(SEGREDO);
  });
});

describe('TwoFactorCard: os códigos de recuperação', () => {
  async function ateOsCodigos() {
    const t = setup({ 'POST /account/two-factor/setup': SETUP_OK, 'POST /account/two-factor/confirm': CONFIRM_OK });
    await ateOQr(t);
    await confirmar(t);
    await screen.findByText('Verificação ligada.');
    return t;
  }

  it('"Copiar todos" copia um código por linha', async () => {
    const t = await ateOsCodigos();

    await t.user.click(screen.getByRole('button', { name: 'Copiar todos' }));

    expect(await navigator.clipboard.readText()).toBe(CODIGOS.join('\n'));
  });

  it('o .txt sai do navegador, com o cabeçalho e os códigos, e libera a URL do blob', async () => {
    const t = await ateOsCodigos();
    const blobs: Blob[] = [];
    const create = vi.fn((blob: Blob) => {
      blobs.push(blob);
      return 'blob:minha-conta/1';
    });
    const revoke = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: create });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revoke });
    let baixado: { href: string; download: string } | null = null;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      baixado = { href: this.getAttribute('href') ?? '', download: this.download };
    });
    const pedidosAntes = t.api.requests.length;

    await t.user.click(screen.getByRole('button', { name: 'Baixar arquivo .txt' }));

    expect(baixado).toEqual({ href: 'blob:minha-conta/1', download: RECOVERY_CODES_FILE_NAME });
    expect(revoke).toHaveBeenCalledWith('blob:minha-conta/1');
    expect(t.api.requests).toHaveLength(pedidosAntes);
    expect(blobs[0].type).toBe('text/plain;charset=utf-8');
    const texto = await blobs[0].text();
    expect(texto).toContain('Buteco Agentes — códigos de recuperação');
    expect(texto).toContain('ana@exemplo.test');
    for (const codigo of CODIGOS) expect(texto).toContain(codigo);
  });

  it('nada no storage, no passo do QR e no dos códigos', async () => {
    const t = setup({ 'POST /account/two-factor/setup': SETUP_OK, 'POST /account/two-factor/confirm': CONFIRM_OK });
    // O spread de um `Storage` do jsdom não enumera as chaves: lê uma por uma.
    const conteudo = (storage: Storage) =>
      Array.from({ length: storage.length }, (_, i) => `${storage.key(i)}=${storage.getItem(storage.key(i)!)}`).join('\n');
    const guardado = () => conteudo(sessionStorage) + conteudo(localStorage);
    await ateOQr(t);
    expect(guardado()).not.toContain(SEGREDO);

    await confirmar(t);
    await screen.findByText('Verificação ligada.');

    for (const codigo of CODIGOS) expect(guardado()).not.toContain(codigo);
    expect(guardado()).not.toContain(SEGREDO);
  });

  it('nada no cache do TanStack Query depois de sair', async () => {
    const t = await ateOsCodigos();

    t.unmount();

    const noCache = JSON.stringify([
      t.queryClient.getMutationCache().getAll().map((m) => m.state.data),
      t.queryClient.getQueryCache().getAll().map((q) => q.state.data),
    ]);
    expect(noCache).not.toContain(SEGREDO);
    for (const codigo of CODIGOS) expect(noCache).not.toContain(codigo);
  });

  it('voltar à página não mostra os códigos', async () => {
    const t = await ateOsCodigos();
    t.unmount();
    t.api.on('GET /account/two-factor', ATIVA);

    renderAccount(<TwoFactorCard />);

    expect(await screen.findByText('Ativa')).toBeInTheDocument();
    expect(screen.queryByText(CODIGOS[0])).not.toBeInTheDocument();
  });
});

describe('TwoFactorCard: gerar novos códigos e remover', () => {
  it('"Gerar novos códigos" mostra os 10 novos com o outro título', async () => {
    const t = setup({ 'GET /account/two-factor': ATIVA, 'POST /account/two-factor/recovery-codes': CONFIRM_OK });

    await t.user.click(await screen.findByRole('button', { name: 'Gerar novos códigos' }));
    expect(screen.getByText('Os códigos atuais deixam de valer.')).toBeInTheDocument();
    const campo = screen.getByLabelText('Código do app ou de recuperação');
    expect(campo).toHaveAttribute('inputmode', 'text');
    await t.user.type(campo, '  abcd-efgh-ijkl-mnop ');
    await t.user.click(screen.getByRole('button', { name: 'Gerar códigos' }));

    expect(await screen.findByText('Novos códigos gerados.')).toBeInTheDocument();
    expect(t.api.calls('POST /account/two-factor/recovery-codes')[0].body).toEqual({ codigo: 'abcd-efgh-ijkl-mnop' });
  });

  it('código errado ao remover fica no campo, e a verificação continua', async () => {
    const t = setup({ 'GET /account/two-factor': ATIVA, 'POST /account/two-factor/remove': problem(401, 'Código inválido.') });

    await t.user.click(await screen.findByRole('button', { name: 'Remover verificação' }));
    await t.user.type(screen.getByLabelText('Código do app ou de recuperação'), '000000');
    await t.user.click(screen.getByRole('button', { name: 'Remover verificação' }));

    expect(await screen.findByText('Código inválido.')).toBeInTheDocument();
    expect(authRuntime.assign).not.toHaveBeenCalled();
  });

  it('remover desliga: o cartão relê e mostra "Não configurada"', async () => {
    const t = setup({ 'GET /account/two-factor': [ATIVA, status({})], 'POST /account/two-factor/remove': { status: 204 } });

    await t.user.click(await screen.findByRole('button', { name: 'Remover verificação' }));
    expect(
      screen.getByText('Depois disso, só a senha protege a sua conta, e os códigos de recuperação deixam de valer.'),
    ).toBeInTheDocument();
    await t.user.type(screen.getByLabelText('Código do app ou de recuperação'), '123456');
    await t.user.click(screen.getByRole('button', { name: 'Remover verificação' }));

    expect(await screen.findByText('Não configurada')).toBeInTheDocument();
  });
});

describe('TwoFactorCard: bloqueio e falhas', () => {
  it('servidor indisponível no cadastro: o aviso, e "Continuar" desabilitado pelo Retry-After', async () => {
    const t = setup({ 'POST /account/two-factor/setup': { status: 503, headers: { 'Retry-After': '10' } } });

    await t.user.click(await screen.findByRole('button', { name: 'Ligar verificação' }));
    await t.user.type(screen.getByLabelText('Sua senha'), SENHA);
    await t.user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('O painel está iniciando. Tente de novo em alguns segundos.');
    expect(screen.getByRole('button', { name: 'Continuar' })).toBeDisabled();
    expect(screen.getByLabelText('Sua senha')).toHaveValue(SENHA);
  });

  it('senha bloqueada no início do cadastro: o aviso com os minutos', async () => {
    const t = setup({
      'POST /account/two-factor/setup': problem(423, 'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.', { 'Retry-After': '60' }),
    });

    await t.user.click(await screen.findByRole('button', { name: 'Ligar verificação' }));
    await t.user.type(screen.getByLabelText('Sua senha'), SENHA);
    await t.user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 1 minuto.',
    );
  });

  it('cadastro fechado no meio do caminho: relê e mostra o indisponível', async () => {
    const t = setup({
      'GET /account/two-factor': [status({}), status({ cadastroDisponivel: false })],
      'POST /account/two-factor/setup': problem(403, 'O cadastro da verificação em 2 etapas ainda não está disponível.'),
    });

    await t.user.click(await screen.findByRole('button', { name: 'Ligar verificação' }));
    await t.user.type(screen.getByLabelText('Sua senha'), SENHA);
    await t.user.click(screen.getByRole('button', { name: 'Continuar' }));

    expect(await screen.findByText('O cadastro da verificação em 2 etapas ainda não está disponível neste painel.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Ligar verificação' })).toBeDisabled());
  });

  it('chave do TOTP trocada ao gerar códigos: o title no cartão, e o campo mantém o que foi digitado', async () => {
    const titulo = 'Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador.';
    const t = setup({ 'GET /account/two-factor': ATIVA, 'POST /account/two-factor/recovery-codes': problem(500, titulo) });

    await t.user.click(await screen.findByRole('button', { name: 'Gerar novos códigos' }));
    await t.user.type(screen.getByLabelText('Código do app ou de recuperação'), '123456');
    await t.user.click(screen.getByRole('button', { name: 'Gerar códigos' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(titulo);
    expect(screen.getByLabelText('Código do app ou de recuperação')).toHaveValue('123456');
  });
});
