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
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
  knowledgeBases: [],
  a2a: null,
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
  mcpServers: [],
  description: null,
  skills: [],
  delegatesTo: [],
  knowledgeBases: [],
  a2a: null,
};

// Descrição do tamanho do caso real da #287 (~330 caracteres), longa o bastante
// para passar da coluna do agente em qualquer largura de tela.
const longDescription =
  'Conduz a triagem de candidatos para as vagas abertas do Butecando: lê o currículo enviado, ' +
  'confere os requisitos obrigatórios da vaga, faz as perguntas de pré-entrevista sobre ' +
  'disponibilidade, pretensão salarial e experiência, registra as respostas no histórico do ' +
  'candidato e encaminha para o recrutador responsável os perfis aprovados na etapa inicial.';

// Larguras do design D2, escritas aqui e não lidas do componente: se o teste lesse
// a mesma fonte que o AgentTable, não testaria a fonte (convenção 15).
const expectedColumnWidths = ['38%', '18%', '13%', '17%', '14%'];

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

  it('exibe provedor e modelo de cada agente em uma única coluna', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.getByText('openai / gpt-5.6-sol')).toBeInTheDocument();
    expect(screen.getByText('anthropic / claude-opus-5')).toBeInTheDocument();
  });

  it('identifica o modelo não configurado na coluna de provedor', () => {
    renderTable([{ ...activeAgent, provider: null, model: null }]);

    expect(screen.getByText('— / não configurado')).toBeInTheDocument();
  });

  it('exibe o indicador de "precisa de reconfiguração" apenas para o agente sem provider/model', () => {
    renderTable([activeAgent, { ...inactiveAgent, provider: null, model: null }]);

    expect(screen.getAllByText('Precisa de reconfiguração')).toHaveLength(1);
  });

  it('não exibe o indicador de "precisa de reconfiguração" quando todos os agentes têm provider/model', () => {
    renderTable([activeAgent, inactiveAgent]);

    expect(screen.queryByText('Precisa de reconfiguração')).not.toBeInTheDocument();
  });

  it('exibe a descrição do agente junto do nome quando ela existe', () => {
    renderTable([{ ...activeAgent, description: 'Atende o financeiro' }]);

    expect(screen.getByText('Atende o financeiro')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: activeAgent.name })).toBeInTheDocument();
  });

  it('não exibe descrição quando o agente não tem uma', () => {
    renderTable([activeAgent]);

    expect(screen.getByRole('link', { name: activeAgent.name })).toBeInTheDocument();
    expect(screen.queryByText('Atende o financeiro')).not.toBeInTheDocument();
  });

  it('resume servidores e tools do agente na coluna de ferramentas', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [
          { id: 's1', name: 'Zendesk MCP', allowedTools: ['read', 'write'] },
          { id: 's2', name: 'Slack MCP', allowedTools: ['post'] },
        ],
      },
    ]);

    expect(screen.getByText('2 servidores · 3 tools')).toBeInTheDocument();
  });

  it('usa o singular com um servidor e uma tool', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [{ id: 's1', name: 'Zendesk MCP', allowedTools: ['read'] }],
      },
    ]);

    expect(screen.getByText('1 servidor · 1 tool')).toBeInTheDocument();
  });

  it('indica quando o agente não tem nenhum servidor vinculado', () => {
    renderTable([activeAgent]);

    expect(screen.getByText('Nenhum servidor')).toBeInTheDocument();
  });

  it('avisa quantos servidores estão vinculados sem nenhuma tool', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [
          { id: 's1', name: 'Zendesk MCP', allowedTools: [] },
          { id: 's2', name: 'Slack MCP', allowedTools: [] },
          { id: 's3', name: 'Github MCP', allowedTools: ['read'] },
        ],
      },
    ]);

    expect(screen.getByTestId(`agent-servers-without-tools-${activeAgent.id}`)).toHaveTextContent(
      '2 servidores sem tools',
    );
  });

  it('não avisa sobre servidores sem tools quando todos têm ao menos uma', () => {
    renderTable([
      {
        ...activeAgent,
        mcpServers: [{ id: 's1', name: 'Zendesk MCP', allowedTools: ['read'] }],
      },
    ]);

    expect(
      screen.queryByTestId(`agent-servers-without-tools-${activeAgent.id}`),
    ).not.toBeInTheDocument();
  });

  it('lista os agentes-alvo de delegação', () => {
    renderTable([
      {
        ...activeAgent,
        delegatesTo: [
          { id: 'd1', name: 'Cobrança' },
          { id: 'd2', name: 'Financeiro' },
        ],
        a2a: null,
      },
    ]);

    expect(screen.getByText('Cobrança, Financeiro')).toBeInTheDocument();
  });

  it('indica ausência de delegação quando o agente não delega para ninguém', () => {
    renderTable([activeAgent]);

    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('trunca a descrição em uma linha e guarda o texto completo no title', () => {
    renderTable([{ ...activeAgent, description: longDescription }]);

    const description = screen.getByText(longDescription);
    expect(description).toHaveAttribute('title', longDescription);
    // O atributo que o Text do Mantine grava com `truncate`, e no qual o CSS dele
    // pendura o nowrap e as reticências; o jsdom não aplica esse CSS (design D6).
    expect(description).toHaveAttribute('data-truncate', 'end');
  });

  it('fixa a largura das colunas, independente do conteúdo', () => {
    renderTable([{ ...activeAgent, description: longDescription }, inactiveAgent]);

    const table = screen.getByRole('table');
    expect(table.style.getPropertyValue('--table-layout')).toBe('fixed');
    expect(screen.getAllByRole('columnheader').map((header) => header.style.width)).toEqual(
      expectedColumnWidths,
    );
  });
});
