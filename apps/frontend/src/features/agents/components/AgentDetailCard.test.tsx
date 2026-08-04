import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentDetailCard } from './AgentDetailCard';
import type { Agent } from '../types/agent';

const baseAgent: Agent = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Atendente',
  instructions: 'Você é um atendente simpático.',
  isActive: true,
  provider: 'openai',
  model: 'gpt-5.6-sol',
  createdAt: '2026-07-26T00:00:00Z',
  updatedAt: '2026-07-26T00:00:00Z',
  mcpServers: [],
};

function renderCard(instructions: string) {
  return render(
    <MantineProvider theme={theme}>
      <AgentDetailCard agent={{ ...baseAgent, instructions }} />
    </MantineProvider>,
  );
}

describe('AgentDetailCard', () => {
  it('renderiza um título markdown como elemento de heading real, não como texto literal', () => {
    renderCard('# Assistente de Vendas\n\nInstruções gerais.');

    expect(
      screen.getByRole('heading', { name: 'Assistente de Vendas', level: 1 }),
    ).toBeInTheDocument();
    expect(screen.queryByText('# Assistente de Vendas')).not.toBeInTheDocument();
  });

  it('renderiza lista e negrito markdown como elementos reais', () => {
    renderCard('**Importante**: siga os passos abaixo.\n\n- Primeiro passo\n- Segundo passo');

    expect(screen.getByRole('list')).toBeInTheDocument();
    expect(screen.getByText('Primeiro passo')).toBeInTheDocument();
    expect(screen.getByText('Segundo passo')).toBeInTheDocument();
    expect(screen.getByText('Importante').tagName).toBe('STRONG');
  });

  it('renderiza tabela markdown (remark-gfm) como elemento de tabela real', () => {
    renderCard('| Canal | Ativo |\n| --- | --- |\n| ChatWoot | Sim |\n| Waha | Não |');

    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByText('ChatWoot')).toBeInTheDocument();
  });

  it('envolve o conteúdo de instruções em um container que usa a altura disponível, com altura mínima (ScrollArea)', () => {
    renderCard('Instruções curtas.');

    const scrollArea = screen.getByTestId('instructions-scroll-area');
    // `height` (não `max-height`) é obrigatório aqui: o Viewport interno do
    // ScrollArea do Mantine é estilizado com `height: 100%`, que só resolve
    // contra uma altura explícita do container pai — com `max-height` sozinho
    // (altura do pai efetivamente "auto"), o Viewport cresce para caber todo o
    // conteúdo e a rolagem fica inerte, mesmo com o conteúdo visualmente
    // cortado. Confirmado em um browser real via Playwright antes desta
    // correção: o `scrollTop` do Viewport não se movia com a roda do mouse.
    expect(scrollArea.style.height).toBeTruthy();
    expect(scrollArea.style.minHeight).toBeTruthy();
  });

  it('exibe o provider e o model do agente', () => {
    renderCard('Instruções curtas.');

    expect(screen.getByText(/openai/)).toBeInTheDocument();
    expect(screen.getByText(/gpt-5\.6-sol/)).toBeInTheDocument();
  });

  it('exibe o indicador de "precisa de reconfiguração" quando provider ou model são nulos', () => {
    render(
      <MantineProvider theme={theme}>
        <AgentDetailCard agent={{ ...baseAgent, provider: null, model: null }} />
      </MantineProvider>,
    );

    expect(screen.getByText('Precisa de reconfiguração')).toBeInTheDocument();
  });

  it('não exibe o indicador de "precisa de reconfiguração" quando provider e model estão preenchidos', () => {
    renderCard('Instruções curtas.');

    expect(screen.queryByText('Precisa de reconfiguração')).not.toBeInTheDocument();
  });

  it('exibe os nomes dos servidores MCP vinculados no resumo', () => {
    render(
      <MantineProvider theme={theme}>
        <AgentDetailCard
          agent={{
            ...baseAgent,
            mcpServers: [
              { id: 'a', name: 'Zendesk MCP', allowedTools: ['read'] },
              { id: 'b', name: 'Slack MCP', allowedTools: [] },
            ],
          }}
        />
      </MantineProvider>,
    );

    expect(screen.getByText(/Zendesk MCP/)).toBeInTheDocument();
    expect(screen.getByText(/Slack MCP/)).toBeInTheDocument();
  });

  it('indica ausência de vínculo quando nenhum servidor MCP está vinculado', () => {
    renderCard('Instruções curtas.');

    expect(screen.getByText(/nenhum servidor MCP vinculado/)).toBeInTheDocument();
  });
});
