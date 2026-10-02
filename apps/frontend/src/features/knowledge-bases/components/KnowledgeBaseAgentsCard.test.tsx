import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
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

// O estado da consulta vai explícito, e não deduzido de `agents`: é exatamente a
// dedução que fazia o carregamento passar por falha (design.md, D5).
function renderCard(agents: Agent[] | undefined, status: 'pending' | 'error' | 'success') {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <KnowledgeBaseAgentsCard knowledgeBaseId={baseId} agents={agents} status={status} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('KnowledgeBaseAgentsCard', () => {
  it('lista os agentes que consultam a base, com link para o detalhe', () => {
    renderCard([agent({ id: 'a1', name: 'Atendimento Financeiro' })], 'success');

    expect(screen.getByRole('link', { name: 'Atendimento Financeiro' })).toHaveAttribute(
      'href',
      '/agents/a1',
    );
  });

  // Correção de protótipo (design.md, D2): os chips do protótipo só têm o nome,
  // mas agente inativo não consulta a base, e um chip igual aos outros afirmaria
  // uma consulta que não acontece. A marca é TEXTO e fica fora do link. As
  // negativas impedem a volta do rótulo duplicado ("Ativo" em todo chip) e do
  // chip mudo.
  it('marca por texto só o agente inativo', () => {
    renderCard(
      [
        agent({ id: 'a1', name: 'Triagem' }),
        agent({ id: 'a2', name: 'Pós-venda', isActive: false }),
      ],
      'success',
    );

    const inactiveLink = screen.getByRole('link', { name: 'Pós-venda' });
    const inactiveChip = inactiveLink.closest('li')!;
    expect(within(inactiveChip).getByText('Inativo')).toBeInTheDocument();
    expect(inactiveLink).not.toHaveTextContent('Inativo');

    const activeChip = screen.getByRole('link', { name: 'Triagem' }).closest('li')!;
    expect(within(activeChip).queryByText('Inativo')).not.toBeInTheDocument();
    expect(screen.queryByText('Ativo')).not.toBeInTheDocument();
  });

  it('apresenta cada agente como chip com link, numa lista só', () => {
    const names = [
      'Atendente de Suporte',
      'Triagem',
      'Pós-venda',
      'Ouvidoria',
      'Atendimento WhatsApp',
      'Atendimento Telegram',
      'Supervisor de turno',
    ];
    renderCard(
      names.map((name, index) => agent({ id: `a${index}`, name })),
      'success',
    );

    const list = screen.getByRole('list');
    expect(within(list).getAllByRole('listitem')).toHaveLength(7);
    names.forEach((name, index) => {
      expect(within(list).getByRole('link', { name })).toHaveAttribute('href', `/agents/a${index}`);
    });
  });

  // A contagem conta os chips, inativos incluídos (design.md, D3).
  it('exibe a contagem no cabeçalho com a lista carregada', () => {
    renderCard(
      Array.from({ length: 7 }, (_, index) =>
        agent({ id: `a${index}`, name: `Agente ${index}`, isActive: index !== 0 }),
      ),
      'success',
    );

    expect(screen.getByTestId('agents-count')).toHaveTextContent('7 agentes');
  });

  it('usa o singular com um agente', () => {
    renderCard([agent({ id: 'a1' })], 'success');

    expect(screen.getByTestId('agents-count')).toHaveTextContent('1 agente');
    expect(screen.getByTestId('agents-count')).not.toHaveTextContent('agentes');
  });

  // O zero medido não vira "0 agentes": o estado vazio já diz o fato.
  it('não exibe contagem quando nenhum agente consulta a base', () => {
    renderCard([agent({ id: 'a1', knowledgeBases: [] })], 'success');

    expect(screen.getByTestId('agents-empty')).toBeInTheDocument();
    expect(screen.queryByTestId('agents-count')).not.toBeInTheDocument();
    expect(screen.queryByText(/\d+ agentes?$/)).not.toBeInTheDocument();
  });

  // Carregamento não é falha nem ausência (design.md, D5): antes desta change o
  // card mostrava o texto de indisponibilidade enquanto GET /agents nem tinha
  // respondido.
  it('durante o carregamento, indica carregamento e não afirma falha, ausência nem contagem', () => {
    renderCard(undefined, 'pending');

    expect(screen.getByTestId('agents-loading')).toHaveTextContent('Carregando agentes');
    expect(screen.queryByTestId('agents-unavailable')).not.toBeInTheDocument();
    expect(screen.queryByTestId('agents-empty')).not.toBeInTheDocument();
    expect(screen.queryByTestId('agents-count')).not.toBeInTheDocument();
  });

  it('não exibe contagem com o catálogo indisponível', () => {
    renderCard(undefined, 'error');

    expect(screen.queryByTestId('agents-count')).not.toBeInTheDocument();
    expect(screen.queryByTestId('agents-loading')).not.toBeInTheDocument();
  });

  // Revalidação em segundo plano que falha mantém o dado já lido: a lista é uma
  // medição que aconteceu, e trocá-la por "não foi possível carregar" apagaria
  // o que o sistema sabe (design.md, D5).
  it('com a lista na mão, uma falha posterior não esconde os agentes', () => {
    renderCard([agent({ id: 'a1', name: 'Triagem' })], 'error');

    expect(screen.getByRole('link', { name: 'Triagem' })).toBeInTheDocument();
    expect(screen.queryByTestId('agents-unavailable')).not.toBeInTheDocument();
  });

  it('não lista agente vinculado a outra base', () => {
    renderCard(
      [
        agent({ id: 'a1', name: 'Consulta esta' }),
        agent({ id: 'a2', name: 'Consulta outra', knowledgeBases: [{ id: 'outra', name: 'x' }] }),
      ],
      'success',
    );

    expect(screen.getByRole('link', { name: 'Consulta esta' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Consulta outra' })).not.toBeInTheDocument();
  });

  it('informa quando nenhum agente consulta a base', () => {
    renderCard([agent({ id: 'a1', knowledgeBases: [] })], 'success');

    expect(screen.getByTestId('agents-empty')).toHaveTextContent(
      'Nenhum agente consulta esta base.',
    );
  });

  // A aba de vínculo passou a existir, então a copy aponta para ela em vez de
  // anunciar etapa futura. O "não Visão geral" continua valendo: a revisão 2 do
  // handoff moveu o vínculo para uma aba própria, e o protótipo não acompanhou.
  it('aponta a aba Conhecimento do detalhe do agente, e não a visão geral', () => {
    renderCard([], 'success');

    const vazio = screen.getByTestId('agents-empty');
    expect(vazio).toHaveTextContent(/aba Conhecimento do detalhe do agente/i);
    expect(vazio).not.toHaveTextContent(/visão geral/i);
    expect(vazio).not.toHaveTextContent(/próxima etapa/i);
  });

  // Falha ao carregar agentes não pode virar "nenhum agente consulta": seria
  // afirmar ausência de vínculo a partir de uma requisição que não respondeu
  // (convenção 13).
  it('sem o catálogo de agentes, não afirma que ninguém consulta a base', () => {
    renderCard(undefined, 'error');

    expect(screen.getByTestId('agents-unavailable')).toBeInTheDocument();
    expect(screen.queryByTestId('agents-empty')).not.toBeInTheDocument();
  });
});
