import { describe, expect, it } from 'vitest';
import {
  aplicacoesAtivas,
  formatAgo,
  operacoesPorExtenso,
  rodapeDaLista,
  semResultado,
  usadoPorAtivas,
} from './applicationsTexts';

// Os textos com número e o "há …" do quadro (versão 66).

const NOW = new Date('2026-11-09T15:00:00Z');
const antes = (ms: number) => new Date(NOW.getTime() - ms).toISOString();

describe('applicationsTexts', () => {
  it.each([
    [20_000, 'há menos de 1 min'],
    [3 * 60_000, 'há 3 min'],
    [2 * 3_600_000, 'há 2 h'],
    [86_400_000, 'há 1 dia'],
    [41 * 86_400_000, 'há 41 dias'],
    [400 * 86_400_000, 'há 1 ano'],
    [800 * 86_400_000, 'há 2 anos'],
  ])('formatAgo de %d ms é "%s"', (ms, texto) => {
    expect(formatAgo(antes(ms), NOW)).toBe(texto);
  });

  it('o rodapé da lista no singular e no plural', () => {
    expect(rodapeDaLista(1)).toBe(
      '1 aplicação · as desativadas continuam listadas e podem ser reativadas.',
    );
    expect(rodapeDaLista(3)).toBe(
      '3 aplicações · as desativadas continuam listadas e podem ser reativadas.',
    );
  });

  it('a busca sem resultado com e sem o filtro', () => {
    expect(semResultado('erp', 'ativas')).toBe(
      'Nenhuma aplicação encontrada para "erp" entre as ativas.',
    );
    expect(semResultado(' erp ', 'todas')).toBe('Nenhuma aplicação encontrada para "erp".');
    expect(semResultado('', 'desativadas')).toBe(
      'Nenhuma aplicação encontrada entre as desativadas.',
    );
  });

  it('os singulares do card do agente', () => {
    expect(usadoPorAtivas(1)).toBe('1 aplicação externa ativa');
    expect(usadoPorAtivas(2)).toBe('2 aplicações externas ativas');
    expect(aplicacoesAtivas(1)).toBe('1 aplicação ativa');
    expect(aplicacoesAtivas(2)).toBe('2 aplicações ativas');
  });

  it('as operações por extenso, na ordem recebida', () => {
    expect(operacoesPorExtenso(['enviar-mensagem', 'consultar-task'])).toBe(
      'Enviar mensagem · Consultar task',
    );
  });
});
