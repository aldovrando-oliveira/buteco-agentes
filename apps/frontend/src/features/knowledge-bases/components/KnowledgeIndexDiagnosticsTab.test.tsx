import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeIndexDiagnosticsTab } from './KnowledgeIndexDiagnosticsTab';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

const qwen: KnowledgeIndexProvenance = {
  provider: 'openai',
  model: 'qwen-qwen3-embedding-8b',
  dimensions: 4096,
  fragmentCount: 3,
};

const nomic: KnowledgeIndexProvenance = {
  provider: 'openai',
  model: 'nomic-embed-text-v1.5',
  dimensions: 4096,
  fragmentCount: 1,
};

function doc(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: 'd1',
    knowledgeBaseId: 'k1',
    title: 'Faixas de atraso',
    sourceType: 'markdown',
    contentLengthBytes: 8420,
    indexingStatus: 'Indexed',
    indexedAt: '2026-09-02T03:14:00Z',
    failureReason: null,
    contentRevision: 1,
    fragmentCount: 14,
    indexingAttempts: 1,
    lastAttemptAt: '2026-09-02T03:14:00Z',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-02T00:00:00Z',
    ...overrides,
  };
}

function renderTab(props: Partial<React.ComponentProps<typeof KnowledgeIndexDiagnosticsTab>> = {}) {
  const onGoToDocuments = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <KnowledgeIndexDiagnosticsTab
        diagnostics={[qwen]}
        isLoading={false}
        error={null}
        documents={[doc()]}
        documentsError={null}
        onGoToDocuments={onGoToDocuments}
        {...props}
      />
    </MantineProvider>,
  );
  return { onGoToDocuments };
}

describe('KnowledgeIndexDiagnosticsTab', () => {
  it('separa as duas escalas em dois grupos, e diz qual é do sistema', () => {
    renderTab();

    expect(screen.getByText('Como o índice foi construído (sistema)')).toBeInTheDocument();
    expect(screen.getByText('Volume desta base')).toBeInTheDocument();
  });

  it('exibe a proveniência gravada com a contagem de fragmentos da combinação', () => {
    renderTab();

    expect(screen.getByText('openai')).toBeInTheDocument();
    expect(screen.getByText('qwen-qwen3-embedding-8b')).toBeInTheDocument();
    expect(screen.getByText('4096')).toBeInTheDocument();
  });

  it('informa que está lendo enquanto a proveniência não chega', () => {
    renderTab({ diagnostics: undefined, isLoading: true });

    expect(screen.getByText('Lendo a proveniência do índice...')).toBeInTheDocument();
    expect(screen.queryByTestId('index-provenance-empty')).not.toBeInTheDocument();
  });

  describe('índice vazio', () => {
    it('explica, em vez de afirmar configuração', () => {
      renderTab({ diagnostics: [] });

      expect(screen.getByTestId('index-provenance-empty')).toBeInTheDocument();
      expect(screen.getByText(/O índice de conhecimento está vazio/)).toBeInTheDocument();
    });

    // A asserção negativa que impede a tela de dizer qual modelo SERIA usado.
    it('não exibe nome de provedor, de modelo nem dimensão', () => {
      renderTab({ diagnostics: [] });

      expect(screen.queryByText('openai')).not.toBeInTheDocument();
      expect(screen.queryByText('qwen-qwen3-embedding-8b')).not.toBeInTheDocument();
      expect(screen.queryByText('4096')).not.toBeInTheDocument();
      expect(screen.queryByText('Provedor de embedding')).not.toBeInTheDocument();
    });

    // C12: `—` significa "não sei" nesta área do painel. Índice vazio é fato
    // conhecido, e usar o mesmo símbolo apaga a distinção.
    // O alvo é o travessão como VALOR DE LINHA, não como pontuação de prosa: o
    // defeito do protótipo é renderizar `Provedor de embedding  —`. Por isso a
    // asserção é de texto exato (um elemento cujo conteúdo é só o travessão) mais
    // a ausência de qualquer linha de proveniência — e não uma varredura do
    // texto inteiro, que reprovaria por um travessão de cópia e daria a impressão
    // de guarda funcionando pelo motivo errado.
    it('não representa o vazio com travessão numa linha de proveniência', () => {
      renderTab({ diagnostics: [] });

      expect(screen.queryByText('—')).not.toBeInTheDocument();
      expect(screen.queryByTestId('index-provenance-0')).not.toBeInTheDocument();
    });

    it('não atribui o vazio a esta base', () => {
      renderTab({ diagnostics: [] });

      expect(screen.getByTestId('index-provenance-empty').textContent).not.toMatch(/nesta base/i);
    });

    it('exibe a proveniência normalmente numa base sem documento, com índice povoado', () => {
      renderTab({ diagnostics: [qwen], documents: [] });

      expect(screen.getByText('qwen-qwen3-embedding-8b')).toBeInTheDocument();
      expect(screen.queryByTestId('index-provenance-empty')).not.toBeInTheDocument();
      expect(screen.getByTestId('base-volume-documents').textContent).toMatch(/0 de 0/);
    });
  });

  describe('mais de uma combinação', () => {
    it('nomeia a corrupção e exibe as duas, cada uma com a sua contagem', () => {
      renderTab({ diagnostics: [nomic, qwen] });

      expect(screen.getByTestId('index-corruption')).toBeInTheDocument();
      expect(screen.getByText('nomic-embed-text-v1.5')).toBeInTheDocument();
      expect(screen.getByText('qwen-qwen3-embedding-8b')).toBeInTheDocument();
      expect(within(screen.getByTestId('index-provenance-0')).getByText('1')).toBeInTheDocument();
      expect(within(screen.getByTestId('index-provenance-1')).getByText('3')).toBeInTheDocument();
    });

    // A ordem da resposta, com o arranjo FORA de ordem alfabética do cliente: com
    // a lista já ordenada, reordenar no cliente produziria o mesmo DOM e o guarda
    // passaria verde com o defeito presente (convenção 15, quinta forma).
    it('preserva a ordem da resposta, sem reordenar no cliente', () => {
      renderTab({ diagnostics: [qwen, nomic] });

      expect(
        within(screen.getByTestId('index-provenance-0')).getByText(qwen.model),
      ).toBeInTheDocument();
      expect(
        within(screen.getByTestId('index-provenance-1')).getByText(nomic.model),
      ).toBeInTheDocument();
    });

    // A regressão bem-intencionada mais provável desta tela.
    it('não elege nenhuma combinação como atual, correta ou configurada', () => {
      renderTab({ diagnostics: [nomic, qwen] });

      expect(screen.queryByText(/\batual\b/i)).not.toBeInTheDocument();
      expect(screen.queryByText(/\bcorreta\b/i)).not.toBeInTheDocument();
      expect(screen.queryByText(/configurad/i)).not.toBeInTheDocument();
    });

    it('não oferece reindexação em massa', () => {
      renderTab({ diagnostics: [nomic, qwen] });

      expect(screen.queryByRole('button', { name: /reindexar/i })).not.toBeInTheDocument();
    });

    // A tela não mede processo nenhum, então não afirma o estado de execução.
    it('não afirma que a indexação está fora do ar agora', () => {
      renderTab({ diagnostics: [nomic, qwen] });

      expect(screen.getByTestId('index-corruption').textContent).not.toMatch(
        /fora do ar|no chão|parad/i,
      );
    });
  });

  describe('falha ao ler a proveniência', () => {
    it('informa a indisponibilidade', () => {
      renderTab({ diagnostics: undefined, error: new Error('boom') });

      expect(screen.getByTestId('index-provenance-error')).toBeInTheDocument();
    });

    it('não vira índice vazio', () => {
      renderTab({ diagnostics: undefined, error: new Error('boom') });

      expect(screen.queryByTestId('index-provenance-empty')).not.toBeInTheDocument();
      expect(screen.queryByText(/O índice de conhecimento está vazio/)).not.toBeInTheDocument();
    });
  });

  describe('volume desta base', () => {
    it('conta o documento que indexou e falhou depois', () => {
      renderTab({
        documents: [
          doc({ id: 'a', indexingStatus: 'Indexed', fragmentCount: 14 }),
          doc({
            id: 'b',
            indexingStatus: 'Failed',
            indexedAt: '2026-09-02T03:14:00Z',
            failureReason: 'O provedor devolveu 429.',
            fragmentCount: 9,
          }),
          doc({ id: 'c', indexingStatus: 'Pending', indexedAt: null, fragmentCount: 0 }),
        ],
      });

      expect(screen.getByTestId('base-volume-documents').textContent).toMatch(/2 de 3/);
      expect(screen.getByTestId('base-volume-fragments').textContent).toMatch(/23/);
    });

    // GUARDA DO RÓTULO, NÍVEL DE COMPONENTE (tarefa 5.8a): a asserção é sobre o
    // TEXTO RENDERIZADO, nunca constante contra constante — duas constantes
    // iguais passam verde quando alguém devolve o rótulo para `Documentos
    // indexados`, que é a cópia familiar e o movimento que a regra impede.
    // Escopada à linha: a aba contém `indexar` e `indexação` legitimamente.
    it('o rótulo renderizado não reusa a palavra do badge de estado', () => {
      renderTab({
        documents: [doc({ indexingStatus: 'Failed', indexedAt: '2026-09-02T03:14:00Z' })],
      });

      expect(screen.getByTestId('base-volume-documents').textContent).not.toMatch(/indexad/i);
    });

    it('indisponibilidade da listagem não vira zero', () => {
      renderTab({ documents: undefined, documentsError: new Error('boom') });

      expect(screen.getByTestId('base-volume-error')).toBeInTheDocument();
      expect(screen.queryByTestId('base-volume-documents')).not.toBeInTheDocument();
      expect(screen.queryByTestId('base-volume-fragments')).not.toBeInTheDocument();
    });
  });

  describe('a lista de falhas aponta, não repete', () => {
    const comFalha = [
      doc({ id: 'a', indexingStatus: 'Indexed' }),
      doc({
        id: 'b',
        indexingStatus: 'Failed',
        indexedAt: null,
        fragmentCount: 0,
        failureReason: 'O provedor de embedding devolveu 429 nas três tentativas.',
      }),
    ];

    it('informa a contagem e oferece caminho para a aba de documentos', async () => {
      const user = userEvent.setup();
      const { onGoToDocuments } = renderTab({ documents: comFalha });

      expect(screen.getByTestId('base-volume-failures').textContent).toMatch(
        /1 documento falhou ao indexar/,
      );

      await user.click(screen.getByRole('button', { name: /ver na aba documentos/i }));
      expect(onGoToDocuments).toHaveBeenCalledTimes(1);
    });

    it('não repete o motivo da falha nem a ação de reindexar', () => {
      renderTab({ documents: comFalha });

      expect(screen.queryByText(/O provedor de embedding devolveu 429/)).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: /reindexar documento/i }),
      ).not.toBeInTheDocument();
    });

    it('sem falha, a linha some', () => {
      renderTab({ documents: [doc()] });

      expect(screen.queryByTestId('base-volume-failures')).not.toBeInTheDocument();
      expect(screen.queryByText(/0 documentos falharam/i)).not.toBeInTheDocument();
    });
  });

  describe('a aba não afirma o que o sistema não coleta', () => {
    it('declara a ausência de progresso e de métrica de uso', () => {
      renderTab();

      expect(
        screen.getByText(/não há barra de progresso nem número de acessos nesta tela/),
      ).toBeInTheDocument();
    });

    it('não oferece formulário de configuração', () => {
      renderTab();

      expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
      expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
      expect(screen.getByTestId('diagnostics-readonly-note')).toBeInTheDocument();
    });

    it('não exibe barra de progresso', () => {
      renderTab({ documents: [doc({ indexingStatus: 'Indexing' })] });

      expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    });
  });
});
