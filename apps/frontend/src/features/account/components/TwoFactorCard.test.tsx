import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { defaultRoutes, fakeAccountApi, problem, type FakeReply } from '../test/fakeAccountApi';
import { renderAccount } from '../test/renderAccount';
import { RECOVERY_CODES_FILE_NAME } from '../../two-factor/recoveryCodesFile';
import { TwoFactorCard } from './TwoFactorCard';

// Os requisitos das 2 etapas da spec `panel-account-page` (design.md da change
// minha-conta-painel, D4, D5 e D8; e da change verificacao-2-etapas-obrigatoria, D7). Sem
// cadastro e sem remoção em Minha conta: o cadastro é o do /entrar (EnrollmentForm.test.tsx).

const CODIGOS = Array.from({ length: 10 }, (_, i) => `C0D${i}-AAAA-BBBB-CCCC`);

const status = (s: Partial<{ ativa: boolean; ativadaEm: string | null; codigosRestantes: number }>) => ({
  status: 200,
  body: { ativa: false, ativadaEm: null, codigosRestantes: 0, ...s },
});
const ATIVA = status({ ativa: true, ativadaEm: '2026-08-14T15:00:00Z', codigosRestantes: 8 });
const NOVOS: FakeReply = { status: 200, body: { codigosRecuperacao: CODIGOS } };

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

async function gerar(t: ReturnType<typeof setup>, codigo = '123456') {
  await t.user.click(await screen.findByRole('button', { name: 'Gerar novos códigos' }));
  await t.user.type(screen.getByLabelText('Código do app ou de recuperação'), codigo);
  await t.user.click(screen.getByRole('button', { name: 'Gerar códigos' }));
}

describe('TwoFactorCard: o estado', () => {
  it('sem a verificação: "Não configurada" e o texto do próximo login, sem botão', async () => {
    setup({ 'GET /account/two-factor': status({}) });

    expect(await screen.findByText('Não configurada')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Este painel passou a exigir a verificação em 2 etapas. Na próxima vez que você entrar, o login pede o cadastro do app autenticador.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('ativa: "Gerar novos códigos" e o texto de quem trocou de celular, sem "Remover verificação"', async () => {
    setup({ 'GET /account/two-factor': ATIVA });

    expect(await screen.findByText('Ativa')).toBeInTheDocument();
    expect(screen.getByText('desde 14/08/2026')).toBeInTheDocument();
    expect(screen.getByText('8 códigos de recuperação restantes.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Gerar novos códigos' })).toBeEnabled();
    expect(
      screen.getByText(
        'Trocou de celular ou perdeu o app? Peça a outro usuário do painel que redefina a sua verificação; no próximo login você cadastra o app de novo.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remover verificação' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ligar verificação' })).not.toBeInTheDocument();
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

describe('TwoFactorCard: os códigos de recuperação', () => {
  async function ateOsCodigos() {
    const t = setup({ 'GET /account/two-factor': ATIVA, 'POST /account/two-factor/recovery-codes': NOVOS });
    await gerar(t);
    await screen.findByText('Novos códigos gerados.');
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

  it('nada no storage, no passo dos códigos', async () => {
    const t = await ateOsCodigos();
    // O spread de um `Storage` do jsdom não enumera as chaves: lê uma por uma.
    const conteudo = (storage: Storage) =>
      Array.from({ length: storage.length }, (_, i) => `${storage.key(i)}=${storage.getItem(storage.key(i)!)}`).join('\n');
    const guardado = conteudo(sessionStorage) + conteudo(localStorage);

    expect(t.api.requests.length).toBeGreaterThan(0);
    for (const codigo of CODIGOS) expect(guardado).not.toContain(codigo);
  });

  it('nada no cache do TanStack Query depois de sair', async () => {
    const t = await ateOsCodigos();

    t.unmount();

    const noCache = JSON.stringify([
      t.queryClient.getMutationCache().getAll().map((m) => m.state.data),
      t.queryClient.getQueryCache().getAll().map((q) => q.state.data),
    ]);
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

describe('TwoFactorCard: gerar novos códigos', () => {
  it('"Gerar novos códigos" mostra os 10 novos com o outro título', async () => {
    const t = setup({ 'GET /account/two-factor': ATIVA, 'POST /account/two-factor/recovery-codes': NOVOS });

    await t.user.click(await screen.findByRole('button', { name: 'Gerar novos códigos' }));
    expect(screen.getByText('Os códigos atuais deixam de valer.')).toBeInTheDocument();
    const campo = screen.getByLabelText('Código do app ou de recuperação');
    expect(campo).toHaveAttribute('inputmode', 'text');
    await t.user.type(campo, '  abcd-efgh-ijkl-mnop ');
    await t.user.click(screen.getByRole('button', { name: 'Gerar códigos' }));

    expect(await screen.findByText('Novos códigos gerados.')).toBeInTheDocument();
    expect(t.api.calls('POST /account/two-factor/recovery-codes')[0].body).toEqual({ codigo: 'abcd-efgh-ijkl-mnop' });
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

describe('TwoFactorCard: bloqueio e falhas', () => {
  it('servidor indisponível ao gerar códigos: o aviso, e "Gerar códigos" desabilitado pelo Retry-After', async () => {
    const t = setup({ 'GET /account/two-factor': ATIVA, 'POST /account/two-factor/recovery-codes': { status: 503, headers: { 'Retry-After': '10' } } });

    await gerar(t);

    expect(await screen.findByRole('alert')).toHaveTextContent('O painel está iniciando. Tente de novo em alguns segundos.');
    expect(screen.getByRole('button', { name: 'Gerar códigos' })).toBeDisabled();
    expect(screen.getByLabelText('Código do app ou de recuperação')).toHaveValue('123456');
  });

  it('bloqueio ao gerar códigos: o aviso com os minutos', async () => {
    const t = setup({
      'GET /account/two-factor': ATIVA,
      'POST /account/two-factor/recovery-codes': problem(423, 'Acesso pausado por tentativas seguidas. Tente de novo mais tarde.', { 'Retry-After': '60' }),
    });

    await gerar(t);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 1 minuto.',
    );
  });

  it('verificação redefinida no meio do caminho: o 409 relê e mostra "Não configurada"', async () => {
    const t = setup({
      'GET /account/two-factor': [ATIVA, status({})],
      'POST /account/two-factor/recovery-codes': problem(409, 'A verificação em 2 etapas não está ligada.'),
    });

    await gerar(t);

    expect(await screen.findByText('Não configurada')).toBeInTheDocument();
  });
});
