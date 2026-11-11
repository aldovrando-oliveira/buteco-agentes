import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  ATIVO,
  CRM,
  EXPIRADO,
  GERADO,
  REVOGADO,
  SUBSTITUIDO,
  VALOR_GERADO,
  detalhe,
  fakeApplicationsApi,
  problem,
} from '../test/fixtures';
import { cachesSerializados, renderApplications } from '../test/renderApplications';
import type { ApplicationDetail, SecretSummary } from '../types';

// Spec `panel-external-applications`: "Aba Credencial e conexão", "Gerar novo segredo", "Revogar com
// confirmação na linha" e "Uma ação por vez no cartão da credencial" (quadro `AplicacaoDetalhe`,
// prop `credencial`, versão 66). Os casos marcados GUARDA são os da seção 6 do tasks.md.

const DETALHE = 'GET /applications/app-portal';
const GERAR = 'POST /applications/app-portal/secrets';

function abrir(body: ApplicationDetail | ApplicationDetail[], extra: Record<string, unknown> = {}) {
  const api = fakeApplicationsApi({
    [DETALHE]: Array.isArray(body)
      ? body.map((b) => ({ status: 200, body: b }))
      : { status: 200, body },
    'GET /applications/app-portal/agents': { status: 200, body: [] },
    ...(extra as Record<string, never>),
  });
  const view = renderApplications('/applications/app-portal?tab=credencial');
  return { api, ...view };
}

const linhaDo = (sufixo: string) =>
  screen.getByText(`btc_sk_••••${sufixo}`).closest('tr') as HTMLElement;

async function abrirGerar(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Gerar novo segredo' }));
}

describe('ApplicationCredentialTab', () => {
  it('Os quatro estados', async () => {
    abrir(detalhe());

    await screen.findByText('btc_sk_••••a91f');
    expect(within(linhaDo('a91f')).getByText('Ativo')).toBeInTheDocument();
    expect(
      within(linhaDo('a91f')).getByRole('button', { name: /^Revogar btc_sk/ }),
    ).toBeInTheDocument();
    expect(within(linhaDo('3c07')).getByText('Substituído')).toBeInTheDocument();
    expect(
      within(linhaDo('3c07')).getByRole('button', { name: /^Revogar agora/ }),
    ).toBeInTheDocument();
    expect(within(linhaDo('77d2')).getByText('Revogado')).toBeInTheDocument();
    expect(within(linhaDo('77d2')).queryByRole('button')).not.toBeInTheDocument();
    expect(within(linhaDo('5e18')).getByText('Expirado')).toBeInTheDocument();
    expect(within(linhaDo('5e18')).getByText('Nunca usado')).toBeInTheDocument();
    expect(within(linhaDo('3c07')).getByText('28/10/2026')).toBeInTheDocument();
    expect(screen.getByText('btc_app_7kq2m9x4')).toBeInTheDocument();
  });

  it('o rodapé e o curl de conexão', async () => {
    abrir(detalhe());

    expect(
      await screen.findByText(
        /Cada aplicação tem no máximo 3 segredos vivos, e cada segredo vale 1 ano\./,
      ),
    ).toBeInTheDocument();
    const curl = screen.getByText(/grant_type=client_credentials/);
    expect(curl).toHaveTextContent('scope=buteco-api');
    expect(curl).toHaveTextContent('"method":"SendMessage"');
    expect(curl).toHaveTextContent('client_id=btc_app_7kq2m9x4');
  });

  it('Reativada, sem segredo vivo', async () => {
    abrir(detalhe(undefined, [REVOGADO]));

    expect(
      await screen.findByText(
        'Nenhum segredo válido: a aplicação não consegue obter token. Gere um novo segredo.',
      ),
    ).toBeInTheDocument();
  });

  // GUARDA (tasks.md 6.5). Reprova com a contagem de vivos só com `estado === 'ativo'`.
  it('Gerar com 3 vivos', async () => {
    const segundo: SecretSummary = { ...SUBSTITUIDO, id: 's-subst-2', sufixo: '9b1c' };
    abrir(detalhe(undefined, [ATIVO, SUBSTITUIDO, segundo, REVOGADO]));

    expect(await screen.findByRole('button', { name: 'Gerar novo segredo' })).toBeDisabled();
    expect(
      screen.getByText('Já há 3 segredos válidos. Revogue um para gerar outro.'),
    ).toBeInTheDocument();
  });

  it('Desativada não gera', async () => {
    abrir(detalhe({ ...CRM, id: 'app-portal' }, [REVOGADO, EXPIRADO]));

    expect(await screen.findByRole('button', { name: 'Gerar novo segredo' })).toBeDisabled();
    expect(screen.getByText('Reative a aplicação para gerar um segredo.')).toBeInTheDocument();
    expect(screen.queryByText(/Nenhum segredo válido/)).not.toBeInTheDocument();
  });

  it('o formulário de gerar, com o "por até 7 dias" só com segredo vivo', async () => {
    const user = userEvent.setup();
    abrir(detalhe());

    await abrirGerar(user);

    expect(
      screen.getByText(
        'O segredo atual continua valendo por até 7 dias, para a aplicação trocar sem parar.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Gerar um segredo cria uma credencial nova. Confirme com a sua senha.'),
    ).toBeInTheDocument();
  });

  it('sem segredo vivo, o formulário não fala do segredo atual', async () => {
    const user = userEvent.setup();
    abrir(detalhe(undefined, [REVOGADO]));

    await abrirGerar(user);

    expect(screen.getByRole('button', { name: 'Gerar segredo' })).toBeInTheDocument();
    expect(screen.queryByText(/O segredo atual continua valendo/)).not.toBeInTheDocument();
  });

  it('senha vazia não envia; o 401 volta ao campo', async () => {
    const user = userEvent.setup();
    const { api } = abrir(detalhe(), { [GERAR]: problem(401, 'Senha inválida.') });

    await abrirGerar(user);
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));
    expect(await screen.findByText('Informe a sua senha.')).toBeInTheDocument();
    expect(api.calls(GERAR)).toHaveLength(0);

    await user.type(screen.getByLabelText('Sua senha'), 'errada');
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));

    expect(await screen.findByText('Senha inválida.')).toBeInTheDocument();
    expect(screen.getByLabelText('Sua senha')).toHaveValue('');
  });

  it('o 423 bloqueia "Gerar segredo"', async () => {
    const user = userEvent.setup();
    abrir(detalhe(), { [GERAR]: problem(423, 'Conta bloqueada.', { 'Retry-After': '300' }) });

    await abrirGerar(user);
    await user.type(screen.getByLabelText('Sua senha'), 's');
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));

    expect(await screen.findByText(/pausada por 5 minutos/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Gerar segredo' })).toBeDisabled();
  });

  it('O limite pela corrida', async () => {
    const user = userEvent.setup();
    const { api } = abrir(detalhe(), {
      [GERAR]: problem(409, 'Revogue um segredo antes de gerar outro.'),
    });

    await abrirGerar(user);
    await user.type(screen.getByLabelText('Sua senha'), 's');
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));

    expect(await screen.findByText('Revogue um segredo antes de gerar outro.')).toBeInTheDocument();
    await waitFor(() => expect(api.calls(DETALHE).length).toBeGreaterThan(1));
  });

  it('O segredo novo', async () => {
    const depois = detalhe(undefined, [
      { ...ATIVO, id: 's-gerado', sufixo: 'IjK0', ultimoUso: null },
      { ...ATIVO, estado: 'substituido', validoAte: '2026-11-16T12:00:00Z' },
      REVOGADO,
    ]);
    const user = userEvent.setup();
    abrir([detalhe(), depois], { [GERAR]: { status: 201, body: GERADO } });

    await abrirGerar(user);
    await user.type(screen.getByLabelText('Sua senha'), 'certa');
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));

    expect(await screen.findByRole('heading', { name: 'Segredo novo' })).toBeInTheDocument();
    expect(screen.getByText(VALOR_GERADO)).toBeInTheDocument();
    expect(
      await screen.findByText(/O anterior continua valendo até 16\/11\/2026\./),
    ).toBeInTheDocument();
    const concluir = screen.getByRole('button', { name: 'Concluir' });
    expect(concluir).toBeDisabled();

    await user.click(screen.getByLabelText('Guardei o segredo em um local seguro'));
    await user.click(screen.getByRole('button', { name: 'Concluir' }));

    expect(screen.queryByText(VALOR_GERADO)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Gerar novo segredo' })).toBeEnabled();
  });

  it('o segredo novo continua na tela ao trocar de aba e voltar', async () => {
    const user = userEvent.setup();
    abrir(detalhe(), { [GERAR]: { status: 201, body: GERADO } });

    await abrirGerar(user);
    await user.type(screen.getByLabelText('Sua senha'), 'certa');
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));
    await screen.findByText(VALOR_GERADO);

    await user.click(screen.getByRole('tab', { name: /Agentes/ }));
    expect(screen.queryByText(VALOR_GERADO)).not.toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Credencial e conexão' }));

    expect(await screen.findByText(VALOR_GERADO)).toBeInTheDocument();
  });

  it('o segredo gerado não vai aos caches do TanStack Query', async () => {
    const user = userEvent.setup();
    const { queryClient } = abrir(detalhe(), { [GERAR]: { status: 201, body: GERADO } });

    await abrirGerar(user);
    await user.type(screen.getByLabelText('Sua senha'), 'certa');
    await user.click(screen.getByRole('button', { name: 'Gerar segredo' }));
    await screen.findByText(VALOR_GERADO);

    expect(cachesSerializados(queryClient)).not.toContain('btc_sk_');
    expect(JSON.stringify({ ...window.sessionStorage, ...window.localStorage })).not.toContain(
      'btc_sk_',
    );
  });

  // GUARDA (tasks.md 6.6). Reprova com "Revogar" chamando a rota direto.
  it('O único vivo: a confirmação na linha, e nada sai antes de confirmar', async () => {
    const user = userEvent.setup();
    const { api } = abrir(detalhe(undefined, [ATIVO, REVOGADO]), {
      'POST /applications/app-portal/secrets/s-ativo/revoke': {
        status: 200,
        body: detalhe(undefined, [{ ...ATIVO, estado: 'revogado' }, REVOGADO]),
      },
    });

    await user.click(await screen.findByRole('button', { name: 'Revogar btc_sk_••••a91f' }));

    expect(
      screen.getByText(
        'Revogar o segredo btc_sk_••••a91f? A aplicação deixa de obter token com ele na hora; um token já emitido continua valendo por até 5 minutos. Ela fica sem segredo válido até você gerar outro.',
      ),
    ).toBeInTheDocument();
    expect(api.calls('POST /applications/app-portal/secrets/s-ativo/revoke')).toHaveLength(0);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('Revogar agora o substituído', async () => {
    const depois = detalhe(undefined, [
      ATIVO,
      { ...SUBSTITUIDO, estado: 'revogado' },
      REVOGADO,
      EXPIRADO,
    ]);
    const user = userEvent.setup();
    const { api } = abrir([detalhe(), depois], {
      'POST /applications/app-portal/secrets/s-subst/revoke': { status: 200, body: depois },
    });

    await user.click(await screen.findByRole('button', { name: 'Revogar agora btc_sk_••••3c07' }));
    const faixa = screen
      .getByText(/^Revogar agora o segredo btc_sk_••••3c07\? Ele valeria até 28\/10\/2026\./)
      .closest('[role="alert"]') as HTMLElement;
    await user.click(within(faixa).getByRole('button', { name: 'Revogar agora' }));

    await waitFor(() => expect(within(linhaDo('3c07')).getByText('Revogado')).toBeInTheDocument());
    expect(api.calls('POST /applications/app-portal/secrets/s-subst/revoke')).toHaveLength(1);
  });

  it('o 404 do revogar aparece na faixa', async () => {
    const user = userEvent.setup();
    abrir(detalhe(), {
      'POST /applications/app-portal/secrets/s-ativo/revoke': problem(
        404,
        'Segredo não encontrado.',
      ),
    });

    await user.click(await screen.findByRole('button', { name: 'Revogar btc_sk_••••a91f' }));
    const faixa = screen
      .getByText(/^Revogar o segredo btc_sk_••••a91f\?/)
      .closest('[role="alert"]') as HTMLElement;
    await user.click(within(faixa).getByRole('button', { name: 'Revogar' }));

    expect(await within(faixa).findByText('Segredo não encontrado.')).toBeInTheDocument();
  });

  it('Revogar com o formulário aberto', async () => {
    const user = userEvent.setup();
    abrir(detalhe());

    await abrirGerar(user);

    expect(screen.getByRole('button', { name: 'Revogar btc_sk_••••a91f' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Revogar agora btc_sk_••••3c07' })).toBeDisabled();
  });
});
