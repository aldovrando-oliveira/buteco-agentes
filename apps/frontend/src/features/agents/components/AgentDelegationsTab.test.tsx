import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentDelegationsTab } from './AgentDelegationsTab';
import { ApiError, replaceAgentDelegations } from '../api/agentsApi';
import type { Agent } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return { ...actual, replaceAgentDelegations: vi.fn() };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

const agent: Agent = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  description: null,
  skills: [],
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
  delegatesTo: [],
  knowledgeBases: [],
  a2a: null,
};

const delegateB: Agent = {
  ...agent,
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Cobrança',
  model: 'claude-opus-5',
};

const delegateC: Agent = {
  ...agent,
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Financeiro',
  isActive: false,
};

function renderTab(overrides?: { agent?: Agent; agentsCatalog?: Agent[] }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      {
        path: '/agents/:id',
        element: (
          <AgentDelegationsTab
            agent={overrides?.agent ?? agent}
            agentsCatalog={overrides?.agentsCatalog ?? [agent, delegateB, delegateC]}
          />
        ),
      },
    ],
    { initialEntries: [`/agents/${agent.id}?tab=delegacoes`] },
  );

  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );

  return router;
}

function checkboxFor(name: string) {
  return screen.getByRole('checkbox', { name });
}

describe('AgentDelegationsTab', () => {
  beforeEach(() => {
    vi.mocked(replaceAgentDelegations).mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('lista os agentes do catálogo como opções, exceto o próprio agente', () => {
    renderTab();

    expect(checkboxFor('Cobrança')).toBeInTheDocument();
    expect(checkboxFor('Financeiro')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: 'Atendente' })).not.toBeInTheDocument();
  });

  it('pré-seleciona os agentes já presentes em delegatesTo', () => {
    renderTab({
      agent: { ...agent, delegatesTo: [{ id: delegateB.id, name: delegateB.name }] },
    });

    expect(checkboxFor('Cobrança')).toBeChecked();
    expect(checkboxFor('Financeiro')).not.toBeChecked();
  });

  it('não seleciona nada quando o agente não tem delegações', () => {
    renderTab();

    expect(checkboxFor('Cobrança')).not.toBeChecked();
    expect(checkboxFor('Financeiro')).not.toBeChecked();
  });

  it('exibe o modelo de cada agente-alvo e a marca de inativo', () => {
    renderTab();

    const activeRow = within(screen.getByTestId(`delegation-row-${delegateB.id}`));
    expect(activeRow.getByText('claude-opus-5')).toBeInTheDocument();

    const inactiveRow = within(screen.getByTestId(`delegation-row-${delegateC.id}`));
    expect(inactiveRow.getByText('(inativo)')).toBeInTheDocument();
  });

  it('permite selecionar um agente inativo', async () => {
    const user = userEvent.setup();
    renderTab();

    const inactive = checkboxFor('Financeiro');
    await user.click(inactive);

    expect(inactive).toBeChecked();
  });

  it('explica a ausência quando o catálogo só tem o próprio agente', () => {
    renderTab({ agentsCatalog: [agent] });

    expect(
      screen.getByText('Nenhum outro agente cadastrado para receber delegações.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('a busca filtra a lista pelo nome, ignorando maiúsculas e minúsculas', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.type(screen.getByLabelText('Buscar por nome'), 'cobr');

    expect(checkboxFor('Cobrança')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: 'Financeiro' })).not.toBeInTheDocument();
  });

  it('indica quando a busca não corresponde a nenhum agente', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.type(screen.getByLabelText('Buscar por nome'), 'inexistente');

    expect(screen.getByText('Nenhum agente corresponde à busca.')).toBeInTheDocument();
  });

  it('filtrar não altera a seleção já feita', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click(checkboxFor('Financeiro'));
    const search = screen.getByLabelText('Buscar por nome');
    await user.type(search, 'cobr');
    expect(screen.queryByRole('checkbox', { name: 'Financeiro' })).not.toBeInTheDocument();

    await user.clear(search);

    expect(checkboxFor('Financeiro')).toBeChecked();
  });

  it('a barra de alterações não salvas só aparece quando há diferença, e some ao desfazer', async () => {
    const user = userEvent.setup();
    renderTab();

    expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument();

    await user.click(checkboxFor('Cobrança'));
    expect(await screen.findByTestId('unsaved-changes-bar')).toBeInTheDocument();

    await user.click(checkboxFor('Cobrança'));
    await waitFor(() =>
      expect(screen.queryByTestId('unsaved-changes-bar')).not.toBeInTheDocument(),
    );
  });

  it('descartar restaura a seleção original sem enviar requisição', async () => {
    const user = userEvent.setup();
    renderTab({
      agent: { ...agent, delegatesTo: [{ id: delegateB.id, name: delegateB.name }] },
    });

    await user.click(checkboxFor('Cobrança'));
    expect(checkboxFor('Cobrança')).not.toBeChecked();

    await user.click(await screen.findByRole('button', { name: 'Descartar' }));

    expect(checkboxFor('Cobrança')).toBeChecked();
    expect(replaceAgentDelegations).not.toHaveBeenCalled();
  });

  it('salvar envia os ids selecionados e notifica sucesso, permanecendo na aba', async () => {
    vi.mocked(replaceAgentDelegations).mockResolvedValue({
      ...agent,
      delegatesTo: [{ id: delegateB.id, name: delegateB.name }],
      a2a: null,
    });
    const user = userEvent.setup();
    const router = renderTab();

    await user.click(checkboxFor('Cobrança'));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() =>
      expect(replaceAgentDelegations).toHaveBeenCalledWith(agent.id, [delegateB.id]),
    );
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
    expect(router.state.location.search).toBe('?tab=delegacoes');
  });

  it('salvar sem nenhum selecionado envia uma lista vazia explícita', async () => {
    vi.mocked(replaceAgentDelegations).mockResolvedValue({ ...agent, delegatesTo: [] });
    const user = userEvent.setup();
    renderTab({
      agent: { ...agent, delegatesTo: [{ id: delegateB.id, name: delegateB.name }] },
    });

    await user.click(checkboxFor('Cobrança'));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() => expect(replaceAgentDelegations).toHaveBeenCalledWith(agent.id, []));
  });

  it('erro no submit exibe notificação genérica e mantém a seleção', async () => {
    vi.mocked(replaceAgentDelegations).mockRejectedValue(new Error('network down'));
    const user = userEvent.setup();
    renderTab();

    await user.click(checkboxFor('Cobrança'));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(checkboxFor('Cobrança')).toBeChecked();
  });
  // --- Recusa permanente x falha transitória --------------------------------
  //
  // Até esta change, QUALQUER erro produzia a mesma notificação com "Tente
  // novamente". A API recusa ciclo (e mais três casos) com 400 sob
  // `targetAgentIds` desde `delegacao-ciclo-no-cadastro`, carregando o caminho
  // pelos nomes dos agentes — e o `onError: () => {}` descartava o argumento
  // inteiro.

  // Todos os guardas de recusa usam ESTE arranjo: um ApiError de verdade (o
  // módulo é mockado com importOriginal, então a classe real está disponível).
  // Construir um objeto qualquer com `status: 400` passaria com o
  // `instanceof` errado e não provaria nada.
  const refusal = (message: string) =>
    new ApiError(400, 'Validation failed', {
      title: 'Validation failed',
      status: 400,
      errors: { targetAgentIds: [message] },
    });

  const cyclePath = 'Atendente → Cobrança → Suporte → Atendente';
  const cycleMessage =
    `Esta delegação fecha um ciclo entre agentes: ${cyclePath}. ` +
    'Um agente não pode delegar, direta ou indiretamente, para um agente que delega de volta para ele.';

  async function saveWith(rejection: unknown) {
    vi.mocked(replaceAgentDelegations).mockRejectedValue(rejection);
    const user = userEvent.setup();
    renderTab();

    await user.click(checkboxFor('Cobrança'));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    return user;
  }

  // G1 — o caminho do ciclo chega à tela. É a informação que resolve o
  // problema, e ela já vinha na resposta; o que faltava era exibi-la.
  it('recusa por ciclo exibe a mensagem da API, com o caminho', async () => {
    await saveWith(refusal(cycleMessage));

    const alert = await screen.findByTestId('delegations-refusal');
    expect(alert).toHaveTextContent(cyclePath);
  });

  // G2 — A NEGATIVA QUE PRENDE O DEFEITO. Sem ela, acrescentar a mensagem nova
  // sem remover a velha passa verde, e o operador continua sendo mandado
  // repetir uma operação que nunca vai funcionar (convenção 13).
  it('recusa por ciclo não instrui a tentar de novo', async () => {
    await saveWith(refusal(cycleMessage));

    await screen.findByTestId('delegations-refusal');
    expect(notifications.show).not.toHaveBeenCalled();
    expect(screen.queryByText(/tente novamente/i)).not.toBeInTheDocument();
  });

  // G3 — o ramo é pela CHAVE do ValidationProblem, não pelo texto. Este guarda
  // é o que impede alguém de reintroduzir casamento de texto ("fecha um
  // ciclo"): a rota tem QUATRO 400 sob a mesma chave, e os outros três não
  // podem cair no genérico.
  it('outra recusa de validação da mesma rota também é exibida', async () => {
    await saveWith(refusal('Um agente não pode delegar para si mesmo.'));

    const alert = await screen.findByTestId('delegations-refusal');
    expect(alert).toHaveTextContent('Um agente não pode delegar para si mesmo.');
    expect(notifications.show).not.toHaveBeenCalled();
  });

  // G4 — PASSA EM `HEAD` DE PROPÓSITO, e não prova nada sobre o defeito: é
  // regressão contra a correção errada, que seria trocar o genérico em vez de
  // acrescentar um ramo. Para rede e 5xx, "Tente novamente" está correto.
  it('falha transitória mantém a notificação genérica e não exibe recusa', async () => {
    await saveWith(new Error('network down'));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(screen.queryByTestId('delegations-refusal')).not.toBeInTheDocument();
  });

  // G5 e G6 — um aviso de recusa que sobrevive afirma uma recusa que já não
  // vale. É a forma da convenção 13 que nenhuma revisão de tela pega, porque a
  // tela não muda: a afirmação era verdadeira quando apareceu.
  it('salvar com sucesso depois de uma recusa remove o aviso', async () => {
    const user = await saveWith(refusal(cycleMessage));
    await screen.findByTestId('delegations-refusal');

    vi.mocked(replaceAgentDelegations).mockResolvedValue({
      ...agent,
      delegatesTo: [{ id: delegateB.id, name: delegateB.name }],
      a2a: null,
    });
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() =>
      expect(screen.queryByTestId('delegations-refusal')).not.toBeInTheDocument(),
    );
  });

  it('descartar depois de uma recusa remove o aviso', async () => {
    const user = await saveWith(refusal(cycleMessage));
    await screen.findByTestId('delegations-refusal');

    await user.click(await screen.findByRole('button', { name: 'Descartar' }));

    expect(screen.queryByTestId('delegations-refusal')).not.toBeInTheDocument();
  });
});
