import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentOverviewTab } from './AgentOverviewTab';
import type { Agent } from '../types/agent';

const baseAgent: Agent = {
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

function renderTab(overrides?: Partial<Agent>) {
  return render(
    <MantineProvider theme={theme}>
      <AgentOverviewTab agent={{ ...baseAgent, ...overrides }} />
    </MantineProvider>,
  );
}

describe('AgentOverviewTab', () => {
  it('renderiza um título markdown como elemento de heading real, não como texto literal', () => {
    renderTab({ instructions: '# Assistente de Vendas\n\nInstruções gerais.' });

    expect(
      screen.getByRole('heading', { name: 'Assistente de Vendas', level: 1 }),
    ).toBeInTheDocument();
    expect(screen.queryByText('# Assistente de Vendas')).not.toBeInTheDocument();
  });

  it('renderiza lista e negrito markdown como elementos reais', () => {
    renderTab({
      instructions: '**Importante**: siga os passos abaixo.\n\n- Primeiro passo\n- Segundo passo',
    });

    expect(screen.getByText('Primeiro passo')).toBeInTheDocument();
    expect(screen.getByText('Segundo passo')).toBeInTheDocument();
    expect(screen.getByText('Importante').tagName).toBe('STRONG');
  });

  it('renderiza tabela markdown (remark-gfm) como elemento de tabela real', () => {
    renderTab({
      instructions: '| Canal | Ativo |\n| --- | --- |\n| ChatWoot | Sim |\n| Waha | Não |',
    });

    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByText('ChatWoot')).toBeInTheDocument();
  });

  it('envolve as instruções em um container com altura definida e altura mínima', () => {
    renderTab();

    const scrollArea = screen.getByTestId('instructions-scroll-area');
    expect(scrollArea.style.height).toBeTruthy();
    expect(scrollArea.style.minHeight).toBeTruthy();
  });

  it('exibe o provider e o model do agente', () => {
    renderTab();

    expect(screen.getByText('openai')).toBeInTheDocument();
    expect(screen.getByText('gpt-5.6-sol')).toBeInTheDocument();
  });

  it('exibe o aviso de reconfiguração quando provider ou model são nulos', () => {
    renderTab({ provider: null, model: null });

    expect(screen.getByTestId('reconfiguration-callout')).toBeInTheDocument();
    expect(screen.getByText('não configurado')).toBeInTheDocument();
  });

  it('não exibe o aviso de reconfiguração quando provider e model estão preenchidos', () => {
    renderTab();

    expect(screen.queryByTestId('reconfiguration-callout')).not.toBeInTheDocument();
  });

  it('exibe as skills do agente e as datas de criação e atualização', () => {
    renderTab({ skills: [{ name: 'Segunda via de boleto', description: null }] });

    expect(screen.getByText('Segunda via de boleto')).toBeInTheDocument();
    expect(screen.getByText('Criado em')).toBeInTheDocument();
    expect(screen.getByText('Atualizado em')).toBeInTheDocument();
  });

  it('indica ausência de skills quando o agente não tem nenhuma', () => {
    renderTab();

    expect(screen.getByText('Nenhuma skill declarada.')).toBeInTheDocument();
  });
});
