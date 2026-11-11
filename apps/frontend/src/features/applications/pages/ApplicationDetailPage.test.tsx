import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CRM, PORTAL, REVOGADO, detalhe, fakeApplicationsApi, problem } from '../test/fixtures';
import { renderApplications } from '../test/renderApplications';

// Spec `panel-external-applications`, "Detalhe da aplicação" e "Desativar e reativar" (quadro
// `AplicacaoDetalhe`, prop `aplicacao`, versão 66).

const DETALHE = 'GET /applications/app-portal';
const AGENTES = 'GET /applications/app-portal/agents';

function rotas(body = detalhe()) {
  return { [DETALHE]: { status: 200, body }, [AGENTES]: { status: 200, body: [] } };
}

describe('ApplicationDetailPage', () => {
  it('o cabeçalho de uma aplicação ativa, com a descrição', async () => {
    fakeApplicationsApi(rotas());
    renderApplications('/applications/app-portal');

    expect(await screen.findByRole('heading', { name: 'Portal do Parceiro' })).toBeInTheDocument();
    expect(screen.getByText('Ativa')).toBeInTheDocument();
    expect(screen.getByText('Autoatendimento de revendas')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Editar' })).toHaveAttribute(
      'href',
      '/applications/app-portal/edit',
    );
    expect(screen.getByRole('button', { name: 'Desativar' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reativar' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Aplicações externas' })).toHaveAttribute(
      'href',
      '/applications',
    );
  });

  it('Desativada no cabeçalho', async () => {
    fakeApplicationsApi({
      'GET /applications/app-crm': { status: 200, body: detalhe(CRM, [REVOGADO]) },
      'GET /applications/app-crm/agents': { status: 200, body: [] },
    });
    renderApplications('/applications/app-crm');

    expect(await screen.findByText('Desativada')).toBeInTheDocument();
    expect(screen.getByText(/Desativada em 08\/11\/2026/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reativar' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Desativar' })).not.toBeInTheDocument();
  });

  it('Aplicação inexistente', async () => {
    const api = fakeApplicationsApi({ [DETALHE]: problem(404, 'Aplicação não encontrada.') });
    renderApplications('/applications/app-portal');

    expect(await screen.findByText('Aplicação não encontrada.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voltar para Aplicações externas' })).toHaveAttribute(
      'href',
      '/applications',
    );
    expect(screen.queryByRole('tab')).not.toBeInTheDocument();
    // O 404 não repete: a página não fica segundos em "Carregando…".
    expect(api.calls(DETALHE)).toHaveLength(1);
  });

  it('erro de carga com "Tentar de novo"', async () => {
    fakeApplicationsApi({
      [DETALHE]: [problem(500, 'Erro.'), { status: 200, body: detalhe() }],
      [AGENTES]: { status: 200, body: [] },
    });
    const user = userEvent.setup();
    renderApplications('/applications/app-portal');

    expect(await screen.findByText('Não foi possível carregar a aplicação.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Tentar de novo' }));
    expect(await screen.findByRole('heading', { name: 'Portal do Parceiro' })).toBeInTheDocument();
  });

  it('carregando', async () => {
    fakeApplicationsApi({ [DETALHE]: () => new Promise(() => {}) });
    renderApplications('/applications/app-portal');

    expect(await screen.findByRole('status')).toHaveTextContent('Carregando a aplicação…');
  });

  it('o 403 da carga mostra a tela de acesso negado', async () => {
    fakeApplicationsApi({ [DETALHE]: { status: 403 } });
    renderApplications('/applications/app-portal');

    expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
  });

  it('A aba na URL', async () => {
    fakeApplicationsApi(rotas());
    const user = userEvent.setup();
    const { router } = renderApplications('/applications/app-portal?tab=credencial');

    expect(await screen.findByRole('tab', { name: 'Credencial e conexão' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await user.click(screen.getByRole('tab', { name: /Agentes/ }));

    expect(screen.getByRole('tab', { name: /Agentes/ })).toHaveAttribute('aria-selected', 'true');
    expect(router.state.location.search).toBe('');
  });

  it('sem `tab`, a aba Agentes, com o contador', async () => {
    fakeApplicationsApi(rotas());
    renderApplications('/applications/app-portal');

    const aba = await screen.findByRole('tab', { name: /Agentes/ });
    expect(aba).toHaveAttribute('aria-selected', 'true');
    await waitFor(() => expect(aba).toHaveTextContent('0'));
  });

  it('Cancelar não desativa', async () => {
    const api = fakeApplicationsApi(rotas());
    const user = userEvent.setup();
    renderApplications('/applications/app-portal');

    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    expect(
      screen.getByText(
        'Desativar Portal do Parceiro? Todos os segredos dela são revogados e as chamadas A2A são recusadas na hora. Os agentes liberados ficam guardados. Ao reativar, você precisa gerar um segredo novo.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(api.calls('POST /applications/app-portal/deactivate')).toHaveLength(0);
    expect(screen.getByText('Ativa')).toBeInTheDocument();
  });

  it('Confirmar desativa', async () => {
    const desativada = detalhe({ ...PORTAL, ativa: false, desativadaEm: '2026-11-09T15:00:00Z' }, [
      { ...REVOGADO },
    ]);
    const api = fakeApplicationsApi({
      [DETALHE]: [
        { status: 200, body: detalhe() },
        { status: 200, body: desativada },
      ],
      [AGENTES]: { status: 200, body: [] },
      'POST /applications/app-portal/deactivate': { status: 200, body: desativada },
    });
    const user = userEvent.setup();
    renderApplications('/applications/app-portal');

    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const faixa = screen
      .getByText(/^Desativar Portal do Parceiro\?/)
      .closest('[role="alert"]') as HTMLElement;
    await user.click(faixa.querySelector('button') as HTMLElement);

    expect(await screen.findByRole('button', { name: 'Reativar' })).toBeInTheDocument();
    expect(screen.getByText('Desativada')).toBeInTheDocument();
    expect(api.calls('POST /applications/app-portal/deactivate')).toHaveLength(1);
  });

  it('a falha de desativar aparece na confirmação', async () => {
    fakeApplicationsApi({
      ...rotas(),
      'POST /applications/app-portal/deactivate': problem(404, 'Aplicação não encontrada.'),
    });
    const user = userEvent.setup();
    renderApplications('/applications/app-portal');

    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const faixa = screen
      .getByText(/^Desativar Portal do Parceiro\?/)
      .closest('[role="alert"]') as HTMLElement;
    await user.click(faixa.querySelector('button') as HTMLElement);

    expect(await screen.findByText('Aplicação não encontrada.')).toBeInTheDocument();
  });

  it('Reativar sem confirmação', async () => {
    const reativada = detalhe({ ...CRM, ativa: true, desativadaEm: null }, [REVOGADO]);
    const api = fakeApplicationsApi({
      'GET /applications/app-crm': [
        { status: 200, body: detalhe(CRM, [REVOGADO]) },
        { status: 200, body: reativada },
      ],
      'GET /applications/app-crm/agents': { status: 200, body: [] },
      'POST /applications/app-crm/activate': { status: 200, body: reativada },
    });
    const user = userEvent.setup();
    renderApplications('/applications/app-crm');

    await user.click(await screen.findByRole('button', { name: 'Reativar' }));

    expect(
      await screen.findByText(
        'Aplicação reativada. Gere um segredo para ela voltar a obter token.',
      ),
    ).toBeInTheDocument();
    expect(api.calls('POST /applications/app-crm/activate')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Desativar' })).toBeInTheDocument();
  });
});
