import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { AgentTable } from './AgentTable';
import type { Agent } from '../types/agent';

const activeAgent: Agent = {
  id: '44444444-4444-4444-4444-444444444444',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
};

const inactiveAgent: Agent = {
  id: '55555555-5555-5555-5555-555555555555',
  name: 'Vendedor',
  instructions: 'Você é um vendedor objetivo.',
  isActive: false,
  provider: 'anthropic',
  model: 'claude-opus-5',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
};

function renderTable(agents: Agent[]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <AgentTable agents={agents} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('AgentTable', () => {
  it('exibe o indicador de "Ativo" para um agente ativo', () => {
    renderTable([activeAgent]);

    expect(screen.getByText('Ativo')).toBeInTheDocument();
  });

  it('exibe indicadores visuais distintos para agente ativo e agente inativo', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByText('Inativo')).toBeInTheDocument();
    expect(screen.queryByText('Ativo')).not.toBe(screen.queryByText('Inativo'));
  });

  it('exibe provider e model de cada agente', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.getByText('openai')).toBeInTheDocument();
    expect(screen.getByText('gpt-5.6-sol')).toBeInTheDocument();
    expect(screen.getByText('anthropic')).toBeInTheDocument();
    expect(screen.getByText('claude-opus-5')).toBeInTheDocument();
  });

  it('exibe o indicador de "precisa de reconfiguração" apenas para o agente sem provider/model', () => {
    renderTable([activeAgent, { ...inactiveAgent, provider: null, model: null }]);

    expect(screen.getAllByText('Precisa de reconfiguração')).toHaveLength(1);
  });

  it('não exibe o indicador de "precisa de reconfiguração" quando todos os agentes têm provider/model', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.queryByText('Precisa de reconfiguração')).not.toBeInTheDocument();
  });
});
