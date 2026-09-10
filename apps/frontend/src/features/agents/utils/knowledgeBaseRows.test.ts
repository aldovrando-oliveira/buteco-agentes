import { describe, expect, it } from 'vitest';
import { knowledgeBaseRows } from './knowledgeBaseRows';
import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';

function base(
  overrides: Partial<KnowledgeBase> & Pick<KnowledgeBase, 'id' | 'name'>,
): KnowledgeBase {
  return {
    description: 'Descrição da base.',
    isActive: true,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

describe('knowledgeBaseRows', () => {
  it('ordena por nome, independentemente da ordem dos ids recebidos', () => {
    const catalog = [
      base({ id: 'k1', name: 'Rodízio' }),
      base({ id: 'k2', name: 'Cardápio' }),
      base({ id: 'k3', name: 'Horários' }),
    ];

    const rows = knowledgeBaseRows(['k1', 'k3', 'k2'], catalog);

    expect(rows.map((row) => row.name)).toEqual(['Cardápio', 'Horários', 'Rodízio']);
  });

  // Nome não é único em nenhum catálogo desta base — não há índice único de
  // nome e nenhum handler de criação valida duplicata. Sem desempate por id, a
  // ordem entre homônimas seria a de entrada, e a lista se remontaria ao salvar.
  //
  // A entrada vai deliberadamente na ordem INVERTIDA do resultado esperado: com
  // a ordem já correta, o teste passaria mesmo sem o desempate.
  it('desempata por id entre bases de nome igual', () => {
    const catalog = [
      base({ id: 'kb-b', name: 'Promoções' }),
      base({ id: 'kb-a', name: 'Promoções' }),
    ];

    const rows = knowledgeBaseRows(['kb-b', 'kb-a'], catalog);

    expect(rows.map((row) => row.id)).toEqual(['kb-a', 'kb-b']);
  });

  it('devolve lista vazia quando nenhuma base está vinculada', () => {
    const catalog = [base({ id: 'k1', name: 'Cardápio' })];

    expect(knowledgeBaseRows([], catalog)).toEqual([]);
  });

  it('descarta em silêncio id sem correspondência no catálogo, sem quebrar as demais linhas', () => {
    const catalog = [base({ id: 'k1', name: 'Cardápio' })];

    const rows = knowledgeBaseRows(['k1', 'id-que-nao-existe'], catalog);

    expect(rows).toHaveLength(1);
    expect(rows[0].name).toBe('Cardápio');
  });

  it('carrega descrição e estado vindos do catálogo, não do vínculo', () => {
    const catalog = [
      base({ id: 'k1', name: 'Rotinas', description: 'Processos internos.', isActive: false }),
    ];

    const [row] = knowledgeBaseRows(['k1'], catalog);

    expect(row.description).toBe('Processos internos.');
    expect(row.isActive).toBe(false);
  });
});
