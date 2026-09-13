import { describe, expect, it } from 'vitest';
import { isIndexCorrupted, isIndexEmpty, shouldPollIndexDiagnostics } from './indexDiagnostics';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

function doc(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: 'd1',
    knowledgeBaseId: 'k1',
    title: 'Documento',
    sourceType: 'markdown',
    contentLengthBytes: 1000,
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

describe('isIndexEmpty', () => {
  it('lista vazia é índice vazio — fato conhecido, não desconhecido', () => {
    expect(isIndexEmpty([])).toBe(true);
  });

  it('uma combinação não é índice vazio', () => {
    expect(isIndexEmpty([qwen])).toBe(false);
  });

  // A distinção que a tela inteira depende: `undefined` é "ainda não sei" —
  // carregando ou falhou —, e NÃO é o estado de índice vazio. Quem renderiza o
  // vazio não pode ser acionado por ausência de resposta.
  it('resposta ainda indefinida NÃO é índice vazio', () => {
    expect(isIndexEmpty(undefined)).toBe(false);
  });
});

describe('isIndexCorrupted', () => {
  it('duas combinações são corrupção', () => {
    expect(isIndexCorrupted([nomic, qwen])).toBe(true);
  });

  it('uma combinação não é corrupção', () => {
    expect(isIndexCorrupted([qwen])).toBe(false);
  });

  it('vazio e indefinido não são corrupção', () => {
    expect(isIndexCorrupted([])).toBe(false);
    expect(isIndexCorrupted(undefined)).toBe(false);
  });
});

// AS QUATRO COMBINAÇÕES DA CONDIÇÃO. As duas metades são necessárias, e cada
// linha abaixo mata uma forma de simplificar a condição:
//
//   vazio + não terminal  → acompanha   (é a única transição que muda o dado)
//   vazio + tudo terminal → não         (nada vai mudar; não há o que esperar)
//   povoado + não terminal→ não         (a metade que evita a varredura de heap)
//   povoado + tudo terminal→ não
describe('shouldPollIndexDiagnostics', () => {
  it('acompanha com índice vazio e documento não terminal', () => {
    expect(shouldPollIndexDiagnostics([], [doc({ indexingStatus: 'Indexing' })])).toBe(true);
  });

  it('não acompanha com índice vazio e todos os documentos terminais', () => {
    expect(
      shouldPollIndexDiagnostics(
        [],
        [
          doc({ indexingStatus: 'Indexed' }),
          doc({ id: 'd2', indexingStatus: 'Failed', indexedAt: null, fragmentCount: 0 }),
        ],
      ),
    ).toBe(false);
  });

  // Esta é a metade que sozinha não se justificaria por correção, e sim por
  // custo: com o índice já povoado a proveniência não muda mais, e repetir a
  // consulta pagaria a varredura do heap inteiro por um valor estável.
  it('não acompanha com índice povoado, mesmo com documento não terminal', () => {
    expect(shouldPollIndexDiagnostics([qwen], [doc({ indexingStatus: 'Indexing' })])).toBe(false);
  });

  it('não acompanha com índice povoado e documentos terminais', () => {
    expect(shouldPollIndexDiagnostics([qwen], [doc()])).toBe(false);
  });

  it('não acompanha enquanto proveniência ou listagem ainda não responderam', () => {
    expect(shouldPollIndexDiagnostics(undefined, [doc({ indexingStatus: 'Indexing' })])).toBe(
      false,
    );
    expect(shouldPollIndexDiagnostics([], undefined)).toBe(false);
    expect(shouldPollIndexDiagnostics(undefined, undefined)).toBe(false);
  });

  it('não acompanha com índice vazio e base sem documento', () => {
    expect(shouldPollIndexDiagnostics([], [])).toBe(false);
  });
});
