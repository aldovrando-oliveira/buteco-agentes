import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CHATBOT, CRM, NOW, PORTAL, fakeApplicationsApi, problem } from '../test/fixtures';
import { renderApplications } from '../test/renderApplications';

// Spec `panel-external-applications`, "Lista de aplicações externas" (quadro `AplicacoesLista`,
// `carga`, versão 66).

const LISTA = 'GET /applications/';

function pedidosDaLista(api: ReturnType<typeof fakeApplicationsApi>) {
  return api.fetch.mock.calls
    .map(([url]) => new URL(url as string, 'http://localhost'))
    .filter((u) => u.pathname === '/applications/');
}

describe('ApplicationsListPage', () => {
  beforeEach(() => {
    // Só o relógio: a espera da busca e o `waitFor` continuam com os temporizadores reais.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('A lista do quadro', async () => {
    fakeApplicationsApi({ [LISTA]: { status: 200, body: [PORTAL, CHATBOT, CRM] } });

    renderApplications('/applications');

    const portal = (await screen.findByRole('link', { name: 'Portal do Parceiro' })).closest('tr')!;
    expect(within(portal).getByText('Atendimento Varejo')).toBeInTheDocument();
    expect(within(portal).getByText('Triagem de Pedidos')).toBeInTheDocument();
    expect(within(portal).getByText('btc_app_7kq2m9x4')).toBeInTheDocument();
    expect(within(portal).getByText('Credencial do Buteco')).toBeInTheDocument();
    expect(within(portal).getByText('Ativa')).toBeInTheDocument();
    expect(within(portal).getByText('há 3 min')).toBeInTheDocument();

    const chatbot = screen.getByRole('link', { name: 'Chatbot do site' }).closest('tr')!;
    expect(within(chatbot).getByText('Nenhum')).toBeInTheDocument();
    expect(within(chatbot).getByText('Nunca usada')).toBeInTheDocument();

    const crm = screen.getByRole('link', { name: 'CRM legado' }).closest('tr')!;
    expect(within(crm).getByText('Desativada')).toBeInTheDocument();
    expect(within(crm).getByText('há 41 dias')).toBeInTheDocument();

    expect(
      screen.getByText('3 aplicações · as desativadas continuam listadas e podem ser reativadas.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Portal do Parceiro' })).toHaveAttribute(
      'href',
      '/applications/app-portal',
    );
  });

  it('Uma aplicação no rodapé', async () => {
    fakeApplicationsApi({ [LISTA]: { status: 200, body: [PORTAL] } });

    renderApplications('/applications');

    expect(
      await screen.findByText(
        '1 aplicação · as desativadas continuam listadas e podem ser reativadas.',
      ),
    ).toBeInTheDocument();
  });

  it('A busca vai ao servidor', async () => {
    const api = fakeApplicationsApi({ [LISTA]: { status: 200, body: [PORTAL] } });
    const user = userEvent.setup();
    renderApplications('/applications');
    await screen.findByRole('link', { name: 'Portal do Parceiro' });

    await user.type(screen.getByRole('searchbox', { name: 'Buscar aplicação' }), 'erp');
    await user.selectOptions(screen.getByLabelText('Estado'), 'ativas');

    await waitFor(() => {
      const ultimo = pedidosDaLista(api).at(-1)!;
      expect(ultimo.searchParams.get('busca')).toBe('erp');
      expect(ultimo.searchParams.get('estado')).toBe('ativas');
    });
    // Nenhuma chamada a cada tecla: só a busca inteira chega ao servidor.
    const buscas = pedidosDaLista(api).map((u) => u.searchParams.get('busca'));
    expect(buscas).not.toContain('e');
    expect(buscas).not.toContain('er');
  });

  it('a lista anterior fica na tela enquanto a busca carrega', async () => {
    const api = fakeApplicationsApi({ [LISTA]: { status: 200, body: [PORTAL, CHATBOT] } });
    const user = userEvent.setup();
    renderApplications('/applications');
    await screen.findByRole('link', { name: 'Chatbot do site' });

    api.on(LISTA, () => new Promise(() => {}));
    await user.type(screen.getByRole('searchbox', { name: 'Buscar aplicação' }), 'portal');
    await waitFor(() =>
      expect(pedidosDaLista(api).at(-1)!.searchParams.get('busca')).toBe('portal'),
    );

    expect(screen.getByRole('link', { name: 'Chatbot do site' })).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Carregando aplicações' })).not.toBeInTheDocument();
  });

  it('Vazia e sem resultado são estados diferentes', async () => {
    const api = fakeApplicationsApi({ [LISTA]: { status: 200, body: [] } });
    const user = userEvent.setup();
    renderApplications('/applications');

    expect(await screen.findByText('Nenhuma aplicação externa cadastrada.')).toBeInTheDocument();
    expect(
      screen.getByText('Cadastre uma aplicação para um sistema de fora chamar agentes pelo A2A.'),
    ).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Nova aplicação' })).toHaveLength(2);

    await user.type(screen.getByRole('searchbox', { name: 'Buscar aplicação' }), 'erp');
    await user.selectOptions(screen.getByLabelText('Estado'), 'ativas');

    expect(
      await screen.findByText('Nenhuma aplicação encontrada para "erp" entre as ativas.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Nenhuma aplicação externa cadastrada.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Limpar busca' }));

    expect(screen.getByRole('searchbox', { name: 'Buscar aplicação' })).toHaveValue('');
    expect(screen.getByLabelText('Estado')).toHaveValue('todas');
    expect(await screen.findByText('Nenhuma aplicação externa cadastrada.')).toBeInTheDocument();
    expect(pedidosDaLista(api).length).toBeGreaterThan(1);
  });

  it('carregando', async () => {
    fakeApplicationsApi({ [LISTA]: () => new Promise(() => {}) });

    renderApplications('/applications');

    expect(
      await screen.findByRole('status', { name: 'Carregando aplicações' }),
    ).toBeInTheDocument();
  });

  it('Erro de carga', async () => {
    const api = fakeApplicationsApi({
      [LISTA]: [problem(500, 'Erro.'), { status: 200, body: [PORTAL] }],
    });
    const user = userEvent.setup();
    renderApplications('/applications');

    expect(await screen.findByText('Não foi possível carregar as aplicações.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Tentar de novo' }));

    expect(await screen.findByRole('link', { name: 'Portal do Parceiro' })).toBeInTheDocument();
    expect(api.calls(LISTA)).toHaveLength(2);
  });

  it('o 403 da carga mostra a tela de acesso negado', async () => {
    fakeApplicationsApi({ [LISTA]: { status: 403 } });

    renderApplications('/applications');

    expect(await screen.findByText('Você não tem acesso a esta página.')).toBeInTheDocument();
  });
});
