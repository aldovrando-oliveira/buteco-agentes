import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { AgentA2ACard } from './AgentA2ACard';
import { theme } from '../../../theme';
import type { AgentA2AAddresses } from '../types/agent';

const enderecos: AgentA2AAddresses = {
  url: 'https://api.exemplo.com/agents/11111111-1111-1111-1111-111111111111/a2a',
  agentCardUrl:
    'https://api.exemplo.com/agents/11111111-1111-1111-1111-111111111111/.well-known/agent-card.json',
};

function renderCard(a2a: AgentA2AAddresses | null | undefined, isActive = true) {
  return render(
    <MantineProvider theme={theme} defaultColorScheme="light">
      <AgentA2ACard a2a={a2a} isActive={isActive} />
    </MantineProvider>,
  );
}

describe('AgentA2ACard', () => {
  it('exibe os dois endereços, cada um com ação de copiar', () => {
    renderCard(enderecos);

    expect(screen.getByText(enderecos.url)).toBeInTheDocument();
    expect(screen.getByText(enderecos.agentCardUrl)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copiar Endpoint de execução' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copiar Card de descoberta' })).toBeInTheDocument();
  });

  it('permite abrir o card de descoberta, e só ele', () => {
    renderCard(enderecos);

    const abrir = screen.getByRole('link', { name: 'Abrir Card de descoberta' });

    expect(abrir).toHaveAttribute('href', enderecos.agentCardUrl);
    // O endpoint de execução é JSON-RPC: abrir no navegador não leva a nada
    // útil, e o único que faz sentido abrir é o card.
    expect(
      screen.queryByRole('link', { name: 'Abrir Endpoint de execução' }),
    ).not.toBeInTheDocument();
  });

  it('avisa que o agente inativo segue descobrível e rejeita mensagens', () => {
    renderCard(enderecos, false);

    // As duas condições valem ao mesmo tempo, e o endereço visível sozinho
    // sugere que o agente está atendendo.
    expect(screen.getByTestId('a2a-inactive-callout')).toBeInTheDocument();
    expect(screen.getByText(enderecos.url)).toBeInTheDocument();
  });

  it('não exibe o aviso para agente ativo', () => {
    renderCard(enderecos);

    expect(screen.queryByTestId('a2a-inactive-callout')).not.toBeInTheDocument();
  });

  it.each([
    ['nulos', null],
    ['ausentes', undefined],
  ])('com endereços %s, explica a ausência sem quebrar', (_caso, a2a) => {
    // `undefined` não é hipótese: é o que uma API que ainda não subiu com esta
    // mudança devolve, porque ela omite o campo. A primeira versão deste
    // componente comparava com `null` e quebrava a tela inteira nesse caso.
    renderCard(a2a);

    expect(
      screen.getByText(/endereço público do servidor não está configurado/i),
    ).toBeInTheDocument();
  });

  it('sem endereços, explica a ausência em vez de exibir endereço vazio', () => {
    const { container } = renderCard(null);

    expect(
      screen.getByText(/endereço público do servidor não está configurado/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /copiar/i })).not.toBeInTheDocument();
    expect(container.querySelector('a[href]')).toBeNull();
  });
});
