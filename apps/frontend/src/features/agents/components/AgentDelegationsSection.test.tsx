import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { theme } from '../../../theme';
import { AgentDelegationsSection } from './AgentDelegationsSection';
import { replaceAgentDelegations } from '../api/agentsApi';
import type { Agent } from '../types/agent';

vi.mock('../api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/agentsApi')>();
  return {
    ...actual,
    replaceAgentDelegations: vi.fn(),
  };
});

vi.mock('@mantine/notifications', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@mantine/notifications')>();
  return { ...actual, notifications: { ...actual.notifications, show: vi.fn() } };
});

function makeAgent(overrides: Partial<Agent>): Agent {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Atendente',
    instructions: 'Você é um atendente simpático.',
    isActive: true,
    provider: 'openai',
    model: 'gpt-5.6-sol',
    createdAt: '2026-07-26T00:00:00Z',
    updatedAt: '2026-07-26T00:00:00Z',
    mcpServers: [],
    description: null,
    skills: [],
    delegatesTo: [],
    ...overrides,
  };
}

const agent = makeAgent({
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Atendente',
});

const delegateB = makeAgent({
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Vendedor',
  isActive: true,
});

const delegateC = makeAgent({
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Suporte',
  isActive: false,
});

// O dropdown do MultiSelect (Mantine) é renderizado via portal, sempre
// presente no DOM (mesmo fechado, só oculto via CSS) e fora do `container`
// que `render()` devolve. Consultas via `screen` (getByText/getByLabelText)
// não filtram por visibilidade e colidem com o texto das opções do portal;
// por isso toda checagem de presença/ausência do que está pré-selecionado
// (os "pills") é escopada com `within(container)`, que exclui o portal.
function renderSection(props: { agent: Agent; agentsCatalog: Agent[] }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <AgentDelegationsSection {...props} />
      </QueryClientProvider>
    </MantineProvider>,
  );
}

function getMultiSelectInput() {
  return screen.getByRole('combobox', { name: /agentes para os quais este agente delega/i });
}

function getPillRemoveButton(container: HTMLElement, name: string) {
  const pillLabel = within(container).getByText(name);
  const pillRoot = pillLabel.closest('.mantine-Pill-root') as HTMLElement;
  return within(pillRoot).getByRole('button', { hidden: true });
}

describe('AgentDelegationsSection', () => {
  beforeEach(() => {
    vi.mocked(replaceAgentDelegations).mockReset();
    vi.mocked(notifications.show).mockReset();
  });

  it('pré-seleciona os agentes já presentes em delegatesTo', () => {
    const agentWithDelegations = {
      ...agent,
      delegatesTo: [{ id: delegateB.id, name: delegateB.name }],
    };

    const { container } = renderSection({
      agent: agentWithDelegations,
      agentsCatalog: [agent, delegateB, delegateC],
    });

    expect(within(container).getByText(delegateB.name)).toBeInTheDocument();
  });

  it('não lista o próprio agente entre as opções', async () => {
    const user = userEvent.setup();
    renderSection({ agent, agentsCatalog: [agent, delegateB, delegateC] });

    await user.click(getMultiSelectInput());

    const listbox = await screen.findByRole('listbox');
    expect(within(listbox).queryByText(agent.name)).not.toBeInTheDocument();
    expect(within(listbox).getByText(delegateB.name)).toBeInTheDocument();
  });

  it('catálogo com um único agente (o próprio) resulta em nenhuma opção disponível, sem quebrar a seção', async () => {
    const user = userEvent.setup();
    renderSection({ agent, agentsCatalog: [agent] });

    await user.click(getMultiSelectInput());

    expect(screen.getByRole('button', { name: /salvar delegações/i })).toBeInTheDocument();
    const listbox = screen.queryByRole('listbox');
    if (listbox) {
      expect(within(listbox).queryAllByRole('option')).toHaveLength(0);
    }
  });

  it('exibe indicador "(inativo)" para um agente inativo entre as opções', async () => {
    const user = userEvent.setup();
    renderSection({ agent, agentsCatalog: [agent, delegateB, delegateC] });

    await user.click(getMultiSelectInput());

    const listbox = await screen.findByRole('listbox');
    expect(within(listbox).getByText(`${delegateC.name} (inativo)`)).toBeInTheDocument();
  });

  it('permite selecionar um agente inativo como delegação', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentDelegations).mockResolvedValue({
      ...agent,
      delegatesTo: [{ id: delegateC.id, name: delegateC.name }],
    });
    renderSection({ agent, agentsCatalog: [agent, delegateB, delegateC] });

    await user.click(getMultiSelectInput());
    const listbox = await screen.findByRole('listbox');
    await user.click(within(listbox).getByText(`${delegateC.name} (inativo)`));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() =>
      expect(replaceAgentDelegations).toHaveBeenCalledWith(agent.id, [delegateC.id]),
    );
  });

  it('seleção e desseleção atualizam o estado local', async () => {
    const user = userEvent.setup();
    const agentWithDelegations = {
      ...agent,
      delegatesTo: [{ id: delegateB.id, name: delegateB.name }],
    };
    const { container } = renderSection({
      agent: agentWithDelegations,
      agentsCatalog: [agent, delegateB, delegateC],
    });

    expect(within(container).getByText(delegateB.name)).toBeInTheDocument();

    await user.click(getPillRemoveButton(container, delegateB.name));

    expect(within(container).queryByText(delegateB.name)).not.toBeInTheDocument();
  });

  it('salvar com agentes selecionados envia targetAgentIds correto e mostra notificação de sucesso', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentDelegations).mockResolvedValue({
      ...agent,
      delegatesTo: [{ id: delegateB.id, name: delegateB.name }],
    });
    renderSection({ agent, agentsCatalog: [agent, delegateB, delegateC] });

    await user.click(getMultiSelectInput());
    const listbox = await screen.findByRole('listbox');
    await user.click(within(listbox).getByText(delegateB.name));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() =>
      expect(replaceAgentDelegations).toHaveBeenCalledWith(agent.id, [delegateB.id]),
    );
    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'green' })),
    );
  });

  it('salvar depois de desmarcar todos os agentes envia targetAgentIds: [] (array vazio explícito)', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentDelegations).mockResolvedValue({ ...agent, delegatesTo: [] });
    const agentWithDelegations = {
      ...agent,
      delegatesTo: [{ id: delegateB.id, name: delegateB.name }],
    };
    const { container } = renderSection({
      agent: agentWithDelegations,
      agentsCatalog: [agent, delegateB, delegateC],
    });

    await user.click(getPillRemoveButton(container, delegateB.name));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() => expect(replaceAgentDelegations).toHaveBeenCalledWith(agent.id, []));
  });

  it('cancelar restaura a seleção original sem enviar requisição', async () => {
    const user = userEvent.setup();
    const agentWithDelegations = {
      ...agent,
      delegatesTo: [{ id: delegateB.id, name: delegateB.name }],
    };
    const { container } = renderSection({
      agent: agentWithDelegations,
      agentsCatalog: [agent, delegateB, delegateC],
    });

    await user.click(getMultiSelectInput());
    const listbox = await screen.findByRole('listbox');
    await user.click(within(listbox).getByText(`${delegateC.name} (inativo)`));
    expect(within(container).getByText(`${delegateC.name} (inativo)`)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    expect(within(container).getByText(delegateB.name)).toBeInTheDocument();
    expect(within(container).queryByText(`${delegateC.name} (inativo)`)).not.toBeInTheDocument();
    expect(replaceAgentDelegations).not.toHaveBeenCalled();
  });

  it('em falha no submit, exibe notificação de erro genérica sem quebrar a seção', async () => {
    const user = userEvent.setup();
    vi.mocked(replaceAgentDelegations).mockRejectedValue(new Error('network down'));
    renderSection({ agent, agentsCatalog: [agent, delegateB, delegateC] });

    await user.click(getMultiSelectInput());
    const listbox = await screen.findByRole('listbox');
    await user.click(within(listbox).getByText(delegateB.name));
    await user.click(screen.getByRole('button', { name: /salvar delegações/i }));

    await waitFor(() =>
      expect(notifications.show).toHaveBeenCalledWith(expect.objectContaining({ color: 'red' })),
    );
    expect(screen.getByRole('button', { name: /salvar delegações/i })).toBeInTheDocument();
  });
});
