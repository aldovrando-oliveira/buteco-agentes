import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { ChannelTable } from './ChannelTable';
import type { Channel } from '../types/channel';
import type { Agent } from '../../agents/types/agent';

const agent: Agent = {
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
};

const activeChannel: Channel = {
  id: '77777777-7777-7777-7777-777777777777',
  channelType: 'waha',
  name: 'Canal WAHA',
  agentId: agent.id,
  isActive: true,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:00:00Z',
  webhookUrl: 'https://inbox.exemplo.com/webhooks/77777777-7777-7777-7777-777777777777',
};

const inactiveChannel: Channel = {
  ...activeChannel,
  id: '88888888-8888-8888-8888-888888888888',
  channelType: 'telegram',
  name: 'Canal Telegram',
  isActive: false,
};

function renderTable(channels: Channel[], agents: Agent[] = [agent]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <ChannelTable channels={channels} agents={agents} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('ChannelTable', () => {
  it('exibe nome, tipo e agente responsável de cada canal', () => {
    renderTable([activeChannel]);

    expect(screen.getByRole('link', { name: activeChannel.name })).toBeInTheDocument();
    expect(screen.getByText('WAHA')).toBeInTheDocument();
    expect(screen.getByText(agent.name)).toBeInTheDocument();
  });

  it('indicador distingue canal inativo na lista, com indicador visual diferente do canal ativo', () => {
    renderTable([activeChannel, inactiveChannel]);

    const activeBadge = screen.getByText('Ativo');
    const inactiveBadge = screen.getByText('Inativo');
    expect(activeBadge).toBeInTheDocument();
    expect(inactiveBadge).toBeInTheDocument();
    expect(activeBadge).not.toBe(inactiveBadge);
  });
});
