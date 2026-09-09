import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseTable } from './KnowledgeBaseTable';
import type { KnowledgeBase } from '../types/knowledgeBase';
import type { Agent } from '../../agents/types/agent';

const activeBase: KnowledgeBase = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação, prazos e faixas de desconto.',
  isActive: true,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-02T00:00:00Z',
};

const inactiveBase: KnowledgeBase = {
  id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  name: 'Rotinas Internas',
  description: 'Procedimentos de abertura e fechamento.',
  isActive: false,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-02T00:00:00Z',
};

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
    knowledgeBases: [],
    a2a: null,
    ...overrides,
  };
}

function renderTable(knowledgeBases: KnowledgeBase[], agents?: Agent[]) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <KnowledgeBaseTable knowledgeBases={knowledgeBases} agents={agents} />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('KnowledgeBaseTable', () => {
  it('exibe nome, descrição e estado de cada base', () => {
    renderTable([activeBase, inactiveBase]);

    expect(screen.getByText(activeBase.name)).toBeInTheDocument();
    expect(screen.getByText(activeBase.description)).toBeInTheDocument();
    expect(screen.getByText('Ativa')).toBeInTheDocument();
    expect(screen.getByText(inactiveBase.name)).toBeInTheDocument();
    expect(screen.getByText('Inativa')).toBeInTheDocument();
  });

  it('usa o nome como link para o detalhe da base', () => {
    renderTable([activeBase]);

    expect(screen.getByRole('link', { name: activeBase.name })).toHaveAttribute(
      'href',
      `/knowledge-bases/${activeBase.id}`,
    );
  });

  // Asserção negativa (convenção 13): é ela que impede a regressão
  // bem-intencionada de "completar" a tabela com as colunas do protótipo que
  // dependem de dado que a API não devolve (design.md, D1).
  it('não afirma contagem de documentos nem resumo de indexação', () => {
    renderTable([activeBase, inactiveBase], []);

    expect(screen.queryByText(/documento/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/indexad/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/fragmento/i)).not.toBeInTheDocument();
  });

  it('tem exatamente as colunas Base, Consultada por e Estado', () => {
    renderTable([activeBase]);

    const headers = screen.getAllByRole('columnheader');
    expect(headers.map((header) => header.textContent)).toEqual([
      'Base',
      'Consultada por',
      'Estado',
    ]);
  });

  it('renderiza cabeçalho sem nenhuma linha quando a lista é vazia', () => {
    renderTable([]);

    expect(screen.getAllByRole('columnheader')).toHaveLength(3);
    expect(screen.queryAllByRole('link')).toHaveLength(0);
  });

  it('lista os agentes que consultam cada base', () => {
    renderTable(
      [activeBase, inactiveBase],
      [
        agent({
          id: 'a1',
          name: 'Atendimento Financeiro',
          knowledgeBases: [{ id: activeBase.id, name: activeBase.name }],
        }),
        agent({
          id: 'a2',
          name: 'Suporte Técnico',
          knowledgeBases: [{ id: activeBase.id, name: activeBase.name }],
        }),
      ],
    );

    expect(screen.getByText('Atendimento Financeiro, Suporte Técnico')).toBeInTheDocument();
  });

  it('diz "Nenhum agente" quando a base não é consultada por ninguém', () => {
    renderTable([activeBase], []);

    expect(screen.getByText('Nenhum agente')).toBeInTheDocument();
  });

  // Três estados, e o protótipo só tem dois: ele usa "—" para zero. Aqui "—" é
  // "não sei" e "Nenhum agente" é "sei que é zero" — a distinção que a convenção
  // 13 protege, e a mesma que McpServerTable já faz (design.md, D21).
  it('sem o catálogo de agentes, não afirma que ninguém consulta a base', () => {
    renderTable([activeBase]);

    expect(screen.queryByText('Nenhum agente')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: activeBase.name })).toBeInTheDocument();
  });
});
