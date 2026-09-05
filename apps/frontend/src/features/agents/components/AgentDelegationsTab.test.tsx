import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentDelegationsTab } from './AgentDelegationsTab';
import { replaceAgentDelegations } from '../api/agentsApi';
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

    await user.type(screen.getByLabelText(/buscar agente/i), 'cobr');

    expect(checkboxFor('Cobrança')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: 'Financeiro' })).not.toBeInTheDocument();
  });

  it('indica quando a busca não corresponde a nenhum agente', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.type(screen.getByLabelText(/buscar agente/i), 'inexistente');

    expect(screen.getByText('Nenhum agente corresponde à busca.')).toBeInTheDocument();
  });

  it('filtrar não altera a seleção já feita', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click(checkboxFor('Financeiro'));
    const search = screen.getByLabelText(/buscar agente/i);
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
});
