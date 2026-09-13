import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseTable } from './KnowledgeBaseTable';
import { statusPresentation } from '../utils/documentIndexing';
import type { KnowledgeBase, KnowledgeBaseIndexingSummary } from '../types/knowledgeBase';
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

function summary(
  knowledgeBaseId: string,
  overrides: Partial<KnowledgeBaseIndexingSummary> = {},
): KnowledgeBaseIndexingSummary {
  return {
    knowledgeBaseId,
    documentCount: 5,
    indexedCount: 3,
    failedCount: 1,
    ...overrides,
  };
}

function renderTable(
  knowledgeBases: KnowledgeBase[],
  agents?: Agent[],
  indexingSummary?: KnowledgeBaseIndexingSummary[],
) {
  return render(
    <MantineProvider theme={theme}>
      <MemoryRouter>
        <KnowledgeBaseTable
          knowledgeBases={knowledgeBases}
          agents={agents}
          indexingSummary={indexingSummary}
        />
      </MemoryRouter>
    </MantineProvider>,
  );
}

function cellText(testId: string) {
  return screen.getByTestId(testId).textContent ?? '';
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

  it('tem exatamente as colunas Base, Documentos, Indexação, Consultada por e Estado', () => {
    renderTable([activeBase]);

    const headers = screen.getAllByRole('columnheader');
    expect(headers.map((header) => header.textContent)).toEqual([
      'Base',
      'Documentos',
      'Indexação',
      'Consultada por',
      'Estado',
    ]);
  });

  it('renderiza cabeçalho sem nenhuma linha quando a lista é vazia', () => {
    renderTable([]);

    expect(screen.getAllByRole('columnheader')).toHaveLength(5);
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

  describe('coluna Documentos', () => {
    it('exibe a contagem da própria base', () => {
      renderTable([activeBase], [], [summary(activeBase.id, { documentCount: 4 })]);

      expect(cellText(`documentos-${activeBase.id}`)).toBe('4 documentos');
    });

    it('exibe o singular com um documento', () => {
      renderTable(
        [activeBase],
        [],
        [summary(activeBase.id, { documentCount: 1, indexedCount: 1, failedCount: 0 })],
      );

      expect(cellText(`documentos-${activeBase.id}`)).toBe('1 documento');
    });

    // O zero é MEDIDO — a agregação percorreu os documentos e não achou nenhum —,
    // ao contrário do zero de `fragmentCount` em documento nunca indexado. Por
    // isso ele pode ser exibido.
    it('exibe Nenhum quando a base não tem documento, e deixa a indexação em branco', () => {
      renderTable(
        [activeBase],
        [],
        [summary(activeBase.id, { documentCount: 0, indexedCount: 0, failedCount: 0 })],
      );

      expect(cellText(`documentos-${activeBase.id}`)).toBe('Nenhum');
      // A coluna vizinha não repete a mesma informação: o protótipo escreve
      // "Nenhum documento" aqui, e a linha passa a dizer o mesmo em duas células.
      expect(cellText(`indexacao-${activeBase.id}`)).toBe('');
    });
  });

  describe('resumo indisponível ou sem linha para a base', () => {
    // ASSERÇÃO NEGATIVA, e é o guarda que impede alguém de "completar" a célula
    // com zero. Zerar afirmaria uma contagem que ninguém fez — o mesmo defeito de
    // `0 fragmentos` na tela vizinha (design.md, D3).
    it('exibe travessão e nunca zero quando o resumo não respondeu', () => {
      renderTable([activeBase], []);

      expect(cellText(`documentos-${activeBase.id}`)).toBe('—');
      expect(cellText(`indexacao-${activeBase.id}`)).toBe('—');
      expect(cellText(`documentos-${activeBase.id}`)).not.toMatch(/Nenhum|0/);
      expect(cellText(`indexacao-${activeBase.id}`)).not.toMatch(/Nenhum|0/);
    });

    // As duas requisições são independentes: uma base criada entre elas aparece
    // no catálogo e não no resumo. Ausência de linha é "não sei", nunca zero.
    it('exibe travessão para a base que o resumo não trouxe', () => {
      renderTable([activeBase, inactiveBase], [], [summary(activeBase.id)]);

      expect(cellText(`documentos-${inactiveBase.id}`)).toBe('—');
      expect(cellText(`indexacao-${inactiveBase.id}`)).toBe('—');
      expect(cellText(`documentos-${activeBase.id}`)).toBe('5 documentos');
    });
  });

  // ARRANJO PRÓPRIO (design.md, R2). O resumo é montado em ordem DELIBERADAMENTE
  // diferente da do catálogo.
  //
  // Sem isso o teste passaria verde com casamento posicional, porque as duas
  // respostas usam hoje o mesmo critério de ordenação — é a quinta forma da
  // convenção 15, o guarda cujo critério a fonte às vezes já produz sozinha.
  it('casa cada linha com a sua própria base, e preserva a ordem do catálogo', () => {
    renderTable(
      [activeBase, inactiveBase],
      [],
      [
        summary(inactiveBase.id, { documentCount: 2, indexedCount: 2, failedCount: 0 }),
        summary(activeBase.id, { documentCount: 9, indexedCount: 7, failedCount: 1 }),
      ],
    );

    const links = screen.getAllByRole('link');
    expect(links.map((link) => link.textContent)).toEqual([activeBase.name, inactiveBase.name]);

    expect(cellText(`documentos-${activeBase.id}`)).toBe('9 documentos');
    expect(cellText(`documentos-${inactiveBase.id}`)).toBe('2 documentos');
  });

  describe('coluna Indexação', () => {
    it('soma indexados, em andamento e falhas', () => {
      renderTable(
        [activeBase],
        [],
        [summary(activeBase.id, { documentCount: 5, indexedCount: 3, failedCount: 1 })],
      );

      expect(cellText(`indexacao-${activeBase.id}`)).toBe('3 indexados·1 em andamento·1 falhou');
    });

    // A ASSERÇÃO NEGATIVA DE D5, SOBRE O DOM.
    //
    // Este é o guarda que protege a TELA, e não o de `indexingSummary.test.ts`. O
    // modo de falha real não é alguém mudar a função pura: é alguém "melhorar" a
    // coluna montando as duas parcelas direto na célula, de um jeito que pareça
    // distinguir. Nesse caminho a função fica intocada e o teste dela continua
    // verde (convenção 15, guarda no componente certo).
    it('a célula não contém pendente nem indexando, em nenhum arranjo', () => {
      const arranjos: KnowledgeBaseIndexingSummary[] = [
        summary(activeBase.id, { documentCount: 5, indexedCount: 1, failedCount: 1 }),
        summary(activeBase.id, { documentCount: 3, indexedCount: 0, failedCount: 0 }),
        summary(activeBase.id, { documentCount: 1, indexedCount: 0, failedCount: 0 }),
      ];

      for (const arranjo of arranjos) {
        const { unmount } = renderTable([activeBase], [], [arranjo]);

        const texto = cellText(`indexacao-${activeBase.id}`);
        expect(texto).not.toMatch(/pendente/i);
        expect(texto).not.toMatch(/indexando/i);

        unmount();
      }
    });

    // O arranjo em que a distinção seria tentadora: 4 não terminais poderiam ser
    // 1 pendente + 3 indexando, e o dado não sabe. UMA parcela.
    it('reúne o complemento não terminal numa única parcela', () => {
      renderTable(
        [activeBase],
        [],
        [summary(activeBase.id, { documentCount: 6, indexedCount: 1, failedCount: 1 })],
      );

      const texto = cellText(`indexacao-${activeBase.id}`);
      expect(texto).toContain('4 em andamento');
      expect(texto.match(/em andamento/g)).toHaveLength(1);
    });
  });

  describe('tom de alerta', () => {
    // jsdom não enxerga cor. O que se afirma aqui é CONTRATO — qual elemento
    // recebe qual cor —, que é exatamente o que a convenção 14 diz que a suíte
    // pode cobrir.
    //
    // A cor esperada vem de `statusPresentation('Failed')`, e não de uma string
    // repetida no teste: é assim que a igualdade com a tabela de documentos fica
    // afirmada, em vez de ser coincidência de duas literais iguais.
    function failureElement() {
      return screen.getByText(/falhou|falharam/);
    }

    it('a parcela de falha carrega a cor semântica de erro', () => {
      renderTable([activeBase], [], [summary(activeBase.id)]);

      expect(failureElement()).toHaveStyle({
        color: `var(--mantine-color-${statusPresentation('Failed').color}-text)`,
      });
    });

    it('a parcela em andamento não carrega tom de alerta', () => {
      renderTable(
        [activeBase],
        [],
        [summary(activeBase.id, { documentCount: 4, indexedCount: 1, failedCount: 0 })],
      );

      expect(screen.getByText('3 em andamento')).not.toHaveStyle({
        color: `var(--mantine-color-${statusPresentation('Failed').color}-text)`,
      });
    });

    it('a parcela de indexados não carrega tom de alerta', () => {
      renderTable([activeBase], [], [summary(activeBase.id)]);

      expect(screen.getByText('3 indexados')).not.toHaveStyle({
        color: `var(--mantine-color-${statusPresentation('Failed').color}-text)`,
      });
    });
  });
});
