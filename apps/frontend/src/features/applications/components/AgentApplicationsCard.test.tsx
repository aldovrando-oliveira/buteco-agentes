import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { signInAsRole } from '../../../test/authFakes';
import { APPS_DO_AGENTE, NOW, fakeApplicationsApi, problem } from '../test/fixtures';
import { useAgentApplications } from '../useAgentApplications';
import { AgentApplicationsCard } from './AgentApplicationsCard';

// Spec `panel-external-application-agents`, "Card "Aplicações com acesso" no detalhe do agente"
// (quadro `AgenteDetalhe`, props `aplicacoes` e `papel`, versão 66). O caso marcado GUARDA é o da
// 9.4 do tasks.md.

const LISTA = 'GET /agents/ag-varejo/applications';
const CONTAGEM = 'GET /agents/ag-varejo/applications/count';

function Card() {
  return <AgentApplicationsCard estado={useAgentApplications('ag-varejo')} />;
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <Card />
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return screen.getByTestId('agent-applications-card');
}

describe('AgentApplicationsCard', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Admin vê a lista', async () => {
    const api = fakeApplicationsApi({ [LISTA]: { status: 200, body: APPS_DO_AGENTE } });
    const card = renderCard();

    const portal = await within(card).findByRole('link', { name: 'Portal do Parceiro' });
    expect(portal).toHaveAttribute('href', '/applications/app-portal');
    expect(
      within(card).getByText('Enviar mensagem · Consultar task · Notificação push'),
    ).toBeInTheDocument();
    expect(within(card).getByText('aplicação usada há 3 min')).toBeInTheDocument();
    expect(within(card).getByText('aplicação nunca usada')).toBeInTheDocument();
    const crm = within(card).getByRole('link', { name: 'CRM legado' }).parentElement as HTMLElement;
    expect(within(crm).getByText('Desativada')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: 'Gerenciar' })).toHaveAttribute(
      'href',
      '/applications',
    );
    expect(card).toHaveTextContent(
      'Desativar este agente afeta 2 aplicações ativas: as tasks que elas abrirem nele passam a terminar recusadas.',
    );
    expect(api.calls(CONTAGEM)).toHaveLength(0);
  });

  // GUARDA (tasks.md 9.4). Reprova com a escolha da rota sem o papel (sempre a lista); a reprovação
  // se repete com a sessão de Operador.
  it.each(['leitor', 'operador'] as const)('Leitor vê só a contagem (%s)', async (papel) => {
    signInAsRole(papel);
    const api = fakeApplicationsApi({
      [CONTAGEM]: { status: 200, body: { total: 3, ativas: 2 } },
      [LISTA]: { status: 403 },
    });
    const card = renderCard();

    expect(await within(card).findByText(/Usado por/)).toHaveTextContent(
      'Usado por 2 aplicações externas ativas (3 no total).',
    );
    expect(within(card).queryByRole('link')).not.toBeInTheDocument();
    expect(within(card).queryByText('Portal do Parceiro')).not.toBeInTheDocument();
    expect(api.calls(LISTA)).toHaveLength(0);
    expect(card).toHaveTextContent('afeta 2 aplicações ativas');
  });

  it('Operador com só desativadas', async () => {
    signInAsRole('operador');
    fakeApplicationsApi({ [CONTAGEM]: { status: 200, body: { total: 2, ativas: 0 } } });
    const card = renderCard();

    expect(
      await within(card).findByText('Usado por 2 aplicações externas, todas desativadas.'),
    ).toBeInTheDocument();
    expect(card).not.toHaveTextContent('Desativar este agente afeta');
  });

  it('uma aplicação ativa, no singular', async () => {
    signInAsRole('leitor');
    fakeApplicationsApi({ [CONTAGEM]: { status: 200, body: { total: 1, ativas: 1 } } });
    const card = renderCard();

    expect(await within(card).findByText(/Usado por/)).toHaveTextContent(
      'Usado por 1 aplicação externa ativa (1 no total).',
    );
    expect(card).toHaveTextContent(
      'Desativar este agente afeta 1 aplicação ativa: as tasks que ela abrir nele passam a terminar recusadas.',
    );
  });

  it('Nenhuma aplicação: o Admin', async () => {
    fakeApplicationsApi({ [LISTA]: { status: 200, body: [] } });
    const card = renderCard();

    expect(
      await within(card).findByText('Nenhuma aplicação externa tem acesso a este agente.'),
    ).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: 'Gerenciar' })).toBeInTheDocument();
    expect(card).not.toHaveTextContent('Desativar este agente afeta');
  });

  it('Nenhuma aplicação: o Operador', async () => {
    signInAsRole('operador');
    fakeApplicationsApi({ [CONTAGEM]: { status: 200, body: { total: 0, ativas: 0 } } });
    const card = renderCard();

    expect(
      await within(card).findByText('Nenhuma aplicação externa usa este agente.'),
    ).toBeInTheDocument();
    expect(within(card).queryByRole('link', { name: 'Gerenciar' })).not.toBeInTheDocument();
  });

  it('Falha do card', async () => {
    const api = fakeApplicationsApi({
      [LISTA]: [problem(500, 'Erro.'), { status: 200, body: [] }],
    });
    const user = userEvent.setup();
    const card = renderCard();

    expect(
      await within(card).findByText('Não foi possível carregar as aplicações.'),
    ).toBeInTheDocument();
    await user.click(within(card).getByRole('button', { name: 'Tentar de novo' }));

    expect(
      await within(card).findByText('Nenhuma aplicação externa tem acesso a este agente.'),
    ).toBeInTheDocument();
    expect(api.calls(LISTA)).toHaveLength(2);
  });

  it('carregando', async () => {
    fakeApplicationsApi({ [LISTA]: () => new Promise(() => {}) });
    const card = renderCard();

    expect(await within(card).findByText('Carregando…')).toBeInTheDocument();
  });

  it('o 403 da lista (Admin rebaixado) mostra o aviso de ação no card', async () => {
    fakeApplicationsApi({ [LISTA]: { status: 403 } });
    const card = renderCard();

    expect(
      await within(card).findByText('Você não tem permissão para esta ação.'),
    ).toBeInTheDocument();
  });
});
