import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseAgentsCard } from './KnowledgeBaseAgentsCard';
import type { Agent } from '../../agents/types/agent';

const baseId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

function agent(overrides: Partial<Agent>): Agent {
  return {
    id: 'agent-1',
    name: 'Atendimento Financeiro',
    instructions: 'Você é um atendente.',
    isActive: true,
    provider: 'openai',
    model: 'gpt-5.6-sol',
    description: null,
    skills: [],
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    mcpServers: [],
    delegatesTo: [],
    knowledgeBases: [{ id: baseId, name: 'Políticas de Cobrança' }],
    a2a: null,
    ...overrides,
  };
}

function renderCard(agents?: Agent[]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <KnowledgeBaseAgentsCard knowledgeBaseId={baseId} agents={agents} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('KnowledgeBaseAgentsCard', () => {
  it('lista os agentes que consultam a base, com link para o detalhe', () => {
    renderCard([agent({ id: 'a1', name: 'Atendimento Financeiro' })]);

    expect(screen.getByRole('link', { name: 'Atendimento Financeiro' })).toHaveAttribute(
      'href',
      '/agents/a1',
    );
  });

  it('exibe o estado de cada agente', () => {
    renderCard([
      agent({ id: 'a1', name: 'Ativo Um' }),
      agent({ id: 'a2', name: 'Inativo Dois', isActive: false }),
    ]);

    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByText('Inativo')).toBeInTheDocument();
  });

  it('não lista agente vinculado a outra base', () => {
    renderCard([
      agent({ id: 'a1', name: 'Consulta esta' }),
      agent({ id: 'a2', name: 'Consulta outra', knowledgeBases: [{ id: 'outra', name: 'x' }] }),
    ]);

    expect(screen.getByRole('link', { name: 'Consulta esta' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Consulta outra' })).not.toBeInTheDocument();
  });

  it('informa quando nenhum agente consulta a base', () => {
    renderCard([agent({ id: 'a1', knowledgeBases: [] })]);

    expect(screen.getByTestId('agents-empty')).toHaveTextContent(
      'Nenhum agente consulta esta base.',
    );
  });

  // A aba de vínculo passou a existir, então a copy aponta para ela em vez de
  // anunciar etapa futura. O "não Visão geral" continua valendo: a revisão 2 do
  // handoff moveu o vínculo para uma aba própria, e o protótipo não acompanhou.
  it('aponta a aba Conhecimento do detalhe do agente, e não a visão geral', () => {
    renderCard([]);

    const vazio = screen.getByTestId('agents-empty');
    expect(vazio).toHaveTextContent(/aba Conhecimento do detalhe do agente/i);
    expect(vazio).not.toHaveTextContent(/visão geral/i);
    expect(vazio).not.toHaveTextContent(/próxima etapa/i);
  });

  // Falha ao carregar agentes não pode virar "nenhum agente consulta": seria
  // afirmar ausência de vínculo a partir de uma requisição que não respondeu
  // (convenção 13).
  it('sem o catálogo de agentes, não afirma que ninguém consulta a base', () => {
    renderCard(undefined);

    expect(screen.getByTestId('agents-unavailable')).toBeInTheDocument();
    expect(screen.queryByTestId('agents-empty')).not.toBeInTheDocument();
  });
});
